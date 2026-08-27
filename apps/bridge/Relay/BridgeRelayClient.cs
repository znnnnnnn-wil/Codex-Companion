using System.Net.WebSockets;
using System.Text;
using System.Text.Json;
using CodexCompanion.Bridge.Codex.Desktop;
using CodexCompanion.Bridge.Codex.History;
using CodexCompanion.Bridge.Codex.Models;
using CodexCompanion.Bridge.Pairing;
using Microsoft.Extensions.Logging;
using QRCoder;

namespace CodexCompanion.Bridge.Relay;

public sealed class BridgeRelayClient(
    Uri relayUri,
    BridgeCredentialStore credentialStore,
    ICodexHistoryAdapter history,
    ICodexDesktopAdapter desktop,
    ILogger<BridgeRelayClient> logger)
{
    private static readonly JsonSerializerOptions WebJson = new(JsonSerializerDefaults.Web);
    private const int MaxRelayMessageBytes = 18 * 1024 * 1024;
    private readonly ThreadSyncTracker _tracker = new();
    private readonly SemaphoreSlim _sendLock = new(1, 1);
    private readonly AttachmentStager _attachmentStager = new();
    private PendingDesktopCreation? _pendingDesktopCreation;

    public async Task RunAsync(CancellationToken cancellationToken)
    {
        var backoff = TimeSpan.FromSeconds(1);
        while (!cancellationToken.IsCancellationRequested)
        {
            try
            {
                await ConnectAndServeAsync(cancellationToken);
                backoff = TimeSpan.FromSeconds(1);
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception exception)
            {
                logger.LogWarning(exception, "Relay connection lost; retrying in {DelaySeconds}s", backoff.TotalSeconds);
                await Task.Delay(backoff, cancellationToken);
                backoff = TimeSpan.FromSeconds(Math.Min(backoff.TotalSeconds * 2, 30));
            }
        }
    }

    private async Task ConnectAndServeAsync(CancellationToken cancellationToken)
    {
        using var socket = new ClientWebSocket();
        socket.Options.KeepAliveInterval = TimeSpan.FromSeconds(20);
        await socket.ConnectAsync(relayUri, cancellationToken);

        var credential = credentialStore.Load();
        string deviceId;
        if (credential is null)
        {
            var requestId = Guid.NewGuid().ToString();
            await SendAsync(socket, TransportEnvelope.Create(
                "pairing.create", requestId, null, null,
                new { deviceName = Environment.MachineName }), cancellationToken);
            var response = await ReceiveAsync(socket, cancellationToken);
            if (response.Type != "pairing.created")
            {
                throw new BridgeException(BridgeErrorCode.Unauthorized, "Relay 未能创建设备配对。");
            }
            var pairing = response.Payload.Deserialize<PairingCreated>(WebJson)
                ?? throw new InvalidDataException("pairing.created payload 无效。");
            credential = new BridgeCredential(pairing.DeviceId, pairing.BridgeCredential);
            credentialStore.Save(credential);
            deviceId = pairing.DeviceId;
            Console.WriteLine($"Codex Companion 配对码：{pairing.Code}（10 分钟内有效）");
            var pageUri = new UriBuilder(relayUri)
            {
                Scheme = relayUri.Scheme.Equals("wss", StringComparison.OrdinalIgnoreCase) ? "https" : "http",
                Path = "/",
                Query = $"pair={Uri.EscapeDataString(pairing.Code)}"
            }.Uri;
            Console.WriteLine($"手机配对地址：{pageUri}");
            try
            {
                using var generator = new QRCodeGenerator();
                using var data = generator.CreateQrCode(pageUri.ToString(), QRCodeGenerator.ECCLevel.M);
                Console.WriteLine(new AsciiQRCode(data).GetGraphic(1));
            }
            catch (Exception exception)
            {
                logger.LogDebug(exception, "Unable to render pairing QR code");
            }
        }
        else
        {
            deviceId = credential.DeviceId;
            await SendAsync(socket, TransportEnvelope.Create(
                "device.hello", Guid.NewGuid().ToString(), deviceId, null,
                new { deviceId, credential = credential.Credential }), cancellationToken);
        }

        logger.LogInformation("Bridge connected to Relay for device {DeviceId}", deviceId);
        await SendStatusAsync(socket, deviceId, cancellationToken);
        using var connectionCancellation = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        var poller = PollActiveThreadAsync(socket, deviceId, connectionCancellation.Token);
        try
        {
            while (socket.State == WebSocketState.Open && !cancellationToken.IsCancellationRequested)
            {
                var envelope = await ReceiveAsync(socket, cancellationToken);
                try
                {
                    await HandleAsync(socket, deviceId, envelope, cancellationToken);
                }
                catch (BridgeException exception)
                {
                    logger.LogWarning(exception, "Bridge request {Type} failed with {Code}", envelope.Type, exception.ProtocolCode);
                    await SendErrorAsync(socket, deviceId, envelope, exception.ProtocolCode, exception.Message, cancellationToken);
                }
                catch (Exception exception)
                {
                    logger.LogError(exception, "Bridge request {Type} failed", envelope.Type);
                    await SendErrorAsync(socket, deviceId, envelope, "CODEX_SEND_FAILED", "电脑端请求失败。", cancellationToken);
                }
            }
        }
        finally
        {
            connectionCancellation.Cancel();
            try { await poller; } catch (OperationCanceledException) { }
        }

        throw new WebSocketException("Relay WebSocket closed.");
    }

    private async Task HandleAsync(
        ClientWebSocket socket,
        string deviceId,
        TransportEnvelope envelope,
        CancellationToken cancellationToken)
    {
        switch (envelope.Type)
        {
            case "thread.list.request":
                var threads = await history.ListThreadsAsync(cancellationToken);
                await SendAsync(socket, TransportEnvelope.Create(
                    "thread.list.response", envelope.RequestId, deviceId, null, new { threads }), cancellationToken);
                break;

            case "thread.create.request":
                await HandleThreadCreateAsync(socket, deviceId, envelope, cancellationToken);
                break;

            case "thread.read.request":
                if (string.IsNullOrWhiteSpace(envelope.ThreadId))
                {
                    await SendErrorAsync(socket, deviceId, envelope, "THREAD_NOT_FOUND", "threadId 不能为空。", cancellationToken);
                    break;
                }
                CodexThreadHistory thread;
                try
                {
                    thread = await history.ReadThreadAsync(envelope.ThreadId, cancellationToken);
                }
                catch (BridgeException exception) when (IsUnmaterializedThread(exception))
                {
                    var summary = (await history.ListThreadsAsync(cancellationToken))
                        .FirstOrDefault(candidate => candidate.ThreadId == envelope.ThreadId);
                    thread = new CodexThreadHistory(
                        envelope.ThreadId,
                        summary?.Title ?? "新会话",
                        summary?.Cwd ?? string.Empty,
                        summary?.UpdatedAt ?? DateTimeOffset.UtcNow.ToUnixTimeSeconds(),
                        summary?.Status ?? "idle",
                        []);
                }
                _tracker.Seed(thread);
                await SendAsync(socket, TransportEnvelope.Create(
                    "thread.read.response", envelope.RequestId, deviceId, thread.ThreadId, thread), cancellationToken);
                break;

            case "media.read.request":
                await HandleMediaReadAsync(socket, deviceId, envelope, cancellationToken);
                break;

            case "message.send":
                await HandleMessageSendAsync(socket, deviceId, envelope, cancellationToken);
                break;

            case "codex.stop":
                await HandleStopAsync(socket, deviceId, envelope, cancellationToken);
                break;

            case "pairing.completed":
                logger.LogInformation("Mobile device pairing completed");
                break;
        }
    }

    private async Task HandleThreadCreateAsync(
        ClientWebSocket socket,
        string deviceId,
        TransportEnvelope envelope,
        CancellationToken cancellationToken)
    {
        var payload = envelope.Payload.Deserialize<ThreadCreatePayload>(WebJson);
        if (string.IsNullOrWhiteSpace(payload?.Cwd))
        {
            await SendAsync(socket, TransportEnvelope.Create(
                "thread.create.failed", envelope.RequestId, deviceId, null,
                new { code = "THREAD_CREATE_FAILED", message = "项目路径不能为空。" }), cancellationToken);
            return;
        }

        try
        {
            var requestedCwd = Path.GetFullPath(payload.Cwd);
            var knownThreads = await history.ListThreadsAsync(cancellationToken);
            var knownWorkspace = knownThreads.Any(thread =>
            {
                if (string.IsNullOrWhiteSpace(thread.Cwd))
                {
                    return false;
                }
                try
                {
                    return string.Equals(Path.GetFullPath(thread.Cwd), requestedCwd, StringComparison.OrdinalIgnoreCase);
                }
                catch (Exception exception) when (exception is ArgumentException or NotSupportedException or PathTooLongException)
                {
                    return false;
                }
            });
            if (!knownWorkspace || !Directory.Exists(requestedCwd))
            {
                throw new BridgeException(BridgeErrorCode.ThreadCreateFailed, "该项目路径不再可用，请先在电脑端打开项目。");
            }

            var existingIds = knownThreads
                .Where(thread => IsSamePath(thread.Cwd, requestedCwd))
                .Select(thread => thread.ThreadId)
                .ToHashSet(StringComparer.Ordinal);
            await desktop.CreateConversationAsync(requestedCwd, cancellationToken);
            _pendingDesktopCreation = new PendingDesktopCreation(
                requestedCwd,
                existingIds,
                DateTimeOffset.UtcNow.AddMinutes(5));
            await SendAsync(socket, TransportEnvelope.Create(
                "thread.create.response", envelope.RequestId, deviceId, null,
                new { draft = new { cwd = requestedCwd, title = "新会话" } }), cancellationToken);
        }
        catch (BridgeException exception)
        {
            await SendAsync(socket, TransportEnvelope.Create(
                "thread.create.failed", envelope.RequestId, deviceId, null,
                new { code = exception.ProtocolCode, message = exception.Message }), cancellationToken);
        }
        catch (Exception exception) when (exception is ArgumentException or NotSupportedException or PathTooLongException)
        {
            await SendAsync(socket, TransportEnvelope.Create(
                "thread.create.failed", envelope.RequestId, deviceId, null,
                new { code = "THREAD_CREATE_FAILED", message = "项目路径无效。" }), cancellationToken);
        }
        catch (Exception exception)
        {
            logger.LogError(exception, "Codex thread creation failed");
            await SendAsync(socket, TransportEnvelope.Create(
                "thread.create.failed", envelope.RequestId, deviceId, null,
                new { code = "THREAD_CREATE_FAILED", message = "新建 Codex 会话失败。" }), cancellationToken);
        }
    }

    private async Task HandleMediaReadAsync(
        ClientWebSocket socket,
        string deviceId,
        TransportEnvelope envelope,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(envelope.ThreadId))
        {
            await SendErrorAsync(socket, deviceId, envelope, "THREAD_NOT_FOUND", "threadId 不能为空。", cancellationToken);
            return;
        }
        var payload = envelope.Payload.Deserialize<MediaReadPayload>(WebJson);
        if (string.IsNullOrWhiteSpace(payload?.ItemId))
        {
            await SendErrorAsync(socket, deviceId, envelope, "MEDIA_NOT_FOUND", "itemId 不能为空。", cancellationToken);
            return;
        }

        var media = await history.ReadMediaAsync(envelope.ThreadId, payload.ItemId, cancellationToken);
        if (media is null)
        {
            await SendErrorAsync(socket, deviceId, envelope, "MEDIA_NOT_FOUND", "该生成图片不可用。", cancellationToken);
            return;
        }
        await SendAsync(socket, TransportEnvelope.Create(
            "media.read.response", envelope.RequestId, deviceId, envelope.ThreadId, media), cancellationToken);
    }

    private async Task HandleMessageSendAsync(
        ClientWebSocket socket,
        string deviceId,
        TransportEnvelope envelope,
        CancellationToken cancellationToken)
    {
        var payload = envelope.Payload.Deserialize<MessageSendPayload>(WebJson);
        if (payload is null || (string.IsNullOrWhiteSpace(payload.Text) && (payload.Attachments is null || payload.Attachments.Count == 0)))
        {
            await SendMessageFailureAsync(socket, deviceId, envelope, "CODEX_SEND_FAILED", "消息和附件不能同时为空。", cancellationToken);
            return;
        }
        if (string.IsNullOrWhiteSpace(envelope.ThreadId))
        {
            await HandleNewConversationMessageAsync(socket, deviceId, envelope, payload, cancellationToken);
            return;
        }

        await SendAsync(socket, TransportEnvelope.Create(
            "message.accepted", envelope.RequestId, deviceId, envelope.ThreadId,
            new { accepted = true }), cancellationToken);
        try
        {
            using var staged = _attachmentStager.Stage(envelope.RequestId ?? Guid.NewGuid().ToString("N"), payload.Attachments);
            var coordinator = new MessageSendCoordinator(history, desktop);
            var result = await coordinator.SendAndConfirmAsync(
                envelope.ThreadId,
                payload.Text ?? string.Empty,
                staged.Attachments.Select(attachment => attachment.Path).ToArray(),
                cancellationToken);
            // Codex history references attached files by their local paths. Retain successful
            // uploads for a bounded period so the same real thread can render them on mobile.
            staged.RetainForHistory();
            var confirmedSnapshot = await history.ReadThreadAsync(envelope.ThreadId, cancellationToken);
            var confirmedDelta = _tracker.Diff(confirmedSnapshot);
            if (confirmedDelta.HasChanges)
            {
                await SendAsync(socket, TransportEnvelope.Create(
                    "thread.updated", null, deviceId, envelope.ThreadId, confirmedDelta), cancellationToken);
            }
            await SendAsync(socket, TransportEnvelope.Create(
                "message.confirmed", envelope.RequestId, deviceId, envelope.ThreadId, result), cancellationToken);
        }
        catch (BridgeException exception)
        {
            await SendMessageFailureAsync(socket, deviceId, envelope, exception.ProtocolCode, exception.Message, cancellationToken);
        }
        catch (Exception exception)
        {
            logger.LogError(exception, "Desktop message send failed");
            await SendMessageFailureAsync(socket, deviceId, envelope, "CODEX_SEND_FAILED", "电脑端发送失败。", cancellationToken);
        }
    }

    private async Task HandleNewConversationMessageAsync(
        ClientWebSocket socket,
        string deviceId,
        TransportEnvelope envelope,
        MessageSendPayload payload,
        CancellationToken cancellationToken)
    {
        var pending = _pendingDesktopCreation;
        if (pending is null
            || pending.ExpiresAt <= DateTimeOffset.UtcNow
            || string.IsNullOrWhiteSpace(payload.Cwd)
            || !IsSamePath(payload.Cwd, pending.Cwd))
        {
            await SendMessageFailureAsync(socket, deviceId, envelope, "THREAD_CREATE_FAILED", "新会话草稿已失效，请重新点击项目旁的新建按钮。", cancellationToken);
            return;
        }
        if (desktop.GetCurrentConversation() is not null)
        {
            await SendMessageFailureAsync(socket, deviceId, envelope, "THREAD_CREATE_FAILED", "电脑端已切换到其他会话，请重新新建会话。", cancellationToken);
            return;
        }

        await SendAsync(socket, TransportEnvelope.Create(
            "message.accepted", envelope.RequestId, deviceId, null,
            new { accepted = true }), cancellationToken);
        try
        {
            using var staged = _attachmentStager.Stage(envelope.RequestId ?? Guid.NewGuid().ToString("N"), payload.Attachments);
            await desktop.SendMessageAsync(
                payload.Text ?? string.Empty,
                staged.Attachments.Select(attachment => attachment.Path).ToArray(),
                cancellationToken);

            var deadline = DateTimeOffset.UtcNow.AddSeconds(20);
            CodexThreadSummary? created = null;
            CodexThreadItem? confirmed = null;
            while (DateTimeOffset.UtcNow < deadline && confirmed is null)
            {
                await Task.Delay(TimeSpan.FromMilliseconds(400), cancellationToken);
                created ??= (await history.ListThreadsAsync(cancellationToken))
                    .Where(candidate => IsSamePath(candidate.Cwd, pending.Cwd)
                        && !pending.ExistingIds.Contains(candidate.ThreadId))
                    .OrderByDescending(candidate => candidate.UpdatedAt)
                    .FirstOrDefault();
                if (created is null)
                {
                    continue;
                }
                var snapshot = await history.ReadThreadAsync(created.ThreadId, cancellationToken);
                confirmed = snapshot.Items.LastOrDefault(item =>
                    item.Type == "message"
                    && item.Role == "user"
                    && MatchesNewConversationMessage(item.Content, payload.Text, staged.Attachments.Count > 0));
            }
            if (created is null || confirmed is null)
            {
                throw new BridgeException(BridgeErrorCode.ThreadConfirmTimeout, "新会话消息未能从真实 Codex thread 中确认。");
            }

            staged.RetainForHistory();
            _pendingDesktopCreation = null;
            var confirmedSnapshot = await history.ReadThreadAsync(created.ThreadId, cancellationToken);
            _tracker.Seed(confirmedSnapshot);
            await SendAsync(socket, TransportEnvelope.Create(
                "message.confirmed", envelope.RequestId, deviceId, created.ThreadId,
                new { thread = created, messageId = confirmed.Id, confirmed = true }), cancellationToken);
        }
        catch (BridgeException exception)
        {
            await SendMessageFailureAsync(socket, deviceId, envelope, exception.ProtocolCode, exception.Message, cancellationToken);
        }
        catch (Exception exception)
        {
            logger.LogError(exception, "Desktop new-conversation message send failed");
            await SendMessageFailureAsync(socket, deviceId, envelope, "CODEX_SEND_FAILED", "电脑端新会话发送失败。", cancellationToken);
        }
    }

    private async Task HandleStopAsync(
        ClientWebSocket socket,
        string deviceId,
        TransportEnvelope envelope,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(envelope.ThreadId))
        {
            await SendAsync(socket, TransportEnvelope.Create(
                "codex.stop.failed", envelope.RequestId, deviceId, envelope.ThreadId,
                new { code = "THREAD_NOT_FOUND", message = "threadId 不能为空。" }), cancellationToken);
            return;
        }

        try
        {
            var threads = await history.ListThreadsAsync(cancellationToken);
            var thread = threads.FirstOrDefault(candidate => candidate.ThreadId == envelope.ThreadId)
                ?? throw new BridgeException(BridgeErrorCode.ThreadNotFound, "找不到指定的 Codex thread。");
            await desktop.StopAsync(thread, cancellationToken);
            await SendAsync(socket, TransportEnvelope.Create(
                "codex.stop.response", envelope.RequestId, deviceId, envelope.ThreadId,
                new { stopped = true }), cancellationToken);
        }
        catch (BridgeException exception)
        {
            await SendAsync(socket, TransportEnvelope.Create(
                "codex.stop.failed", envelope.RequestId, deviceId, envelope.ThreadId,
                new { code = exception.ProtocolCode, message = exception.Message }), cancellationToken);
        }
        catch (Exception exception)
        {
            logger.LogError(exception, "Desktop stop failed");
            await SendAsync(socket, TransportEnvelope.Create(
                "codex.stop.failed", envelope.RequestId, deviceId, envelope.ThreadId,
                new { code = "CODEX_STOP_FAILED", message = "电脑端中止失败。" }), cancellationToken);
        }
    }

    private async Task PollActiveThreadAsync(ClientWebSocket socket, string deviceId, CancellationToken cancellationToken)
    {
        string? lastDesktopState = null;
        while (!cancellationToken.IsCancellationRequested)
        {
            var desktopStatus = desktop.GetDesktopStatus();
            if (desktopStatus.State != lastDesktopState)
            {
                lastDesktopState = desktopStatus.State;
                await SendAsync(socket, TransportEnvelope.Create(
                    "codex.status", null, deviceId, _tracker.ActiveThreadId, desktopStatus), cancellationToken);
            }

            var activeThread = _tracker.ActiveThreadId;
            if (activeThread is not null)
            {
                try
                {
                    var historySnapshot = await history.ReadThreadAsync(activeThread, cancellationToken);
                    var delta = _tracker.Diff(historySnapshot);
                    if (delta.HasChanges)
                    {
                        await SendAsync(socket, TransportEnvelope.Create(
                            "thread.updated", null, deviceId, activeThread, delta), cancellationToken);
                    }
                }
                catch (Exception exception) when (exception is not OperationCanceledException)
                {
                    logger.LogDebug(exception, "Active thread polling failed");
                }
            }

            var interval = desktopStatus.State == "working"
                ? TimeSpan.FromMilliseconds(750)
                : TimeSpan.FromSeconds(3);
            await Task.Delay(interval, cancellationToken);
        }
    }

    private async Task SendStatusAsync(ClientWebSocket socket, string deviceId, CancellationToken cancellationToken)
    {
        await SendAsync(socket, TransportEnvelope.Create(
            "bridge.status", null, deviceId, null,
            new { online = true, version = "0.1.0" }), cancellationToken);
        await SendAsync(socket, TransportEnvelope.Create(
            "codex.status", null, deviceId, null, desktop.GetDesktopStatus()), cancellationToken);
    }

    private Task SendMessageFailureAsync(ClientWebSocket socket, string deviceId, TransportEnvelope request, string code, string message, CancellationToken cancellationToken)
        => SendAsync(socket, TransportEnvelope.Create(
            "message.failed", request.RequestId, deviceId, request.ThreadId, new { code, message }), cancellationToken);

    private Task SendErrorAsync(ClientWebSocket socket, string deviceId, TransportEnvelope request, string code, string message, CancellationToken cancellationToken)
        => SendAsync(socket, TransportEnvelope.Create(
            "error", request.RequestId, deviceId, request.ThreadId, new { code, message }), cancellationToken);

    private async Task SendAsync(ClientWebSocket socket, TransportEnvelope envelope, CancellationToken cancellationToken)
    {
        var bytes = JsonSerializer.SerializeToUtf8Bytes(envelope, WebJson);
        await _sendLock.WaitAsync(cancellationToken);
        try
        {
            await socket.SendAsync(bytes, WebSocketMessageType.Text, true, cancellationToken);
        }
        finally
        {
            _sendLock.Release();
        }
    }

    private static async Task<TransportEnvelope> ReceiveAsync(ClientWebSocket socket, CancellationToken cancellationToken)
    {
        using var buffer = new MemoryStream();
        var chunk = new byte[16 * 1024];
        while (true)
        {
            var result = await socket.ReceiveAsync(chunk, cancellationToken);
            if (result.MessageType == WebSocketMessageType.Close)
            {
                throw new WebSocketException("Relay closed the WebSocket.");
            }
            if (result.MessageType != WebSocketMessageType.Text)
            {
                throw new InvalidDataException("Relay sent a non-text WebSocket frame.");
            }
            await buffer.WriteAsync(chunk.AsMemory(0, result.Count), cancellationToken);
            if (buffer.Length > MaxRelayMessageBytes)
            {
                throw new InvalidDataException("Relay message exceeds 18 MiB limit.");
            }
            if (result.EndOfMessage)
            {
                break;
            }
        }
        return JsonSerializer.Deserialize<TransportEnvelope>(buffer.ToArray(), WebJson)
            ?? throw new InvalidDataException("Relay envelope is invalid JSON.");
    }

    private static bool IsSamePath(string? left, string right)
    {
        if (string.IsNullOrWhiteSpace(left))
        {
            return false;
        }
        try
        {
            return string.Equals(Path.GetFullPath(left), Path.GetFullPath(right), StringComparison.OrdinalIgnoreCase);
        }
        catch (Exception exception) when (exception is ArgumentException or NotSupportedException or PathTooLongException)
        {
            return false;
        }
    }

    private static bool IsUnmaterializedThread(BridgeException exception)
        => exception.Message.Contains("not materialized yet", StringComparison.OrdinalIgnoreCase);

    private static bool MatchesNewConversationMessage(string? candidate, string? expected, bool hasAttachments)
    {
        if (string.IsNullOrWhiteSpace(expected))
        {
            return hasAttachments;
        }
        var actual = (candidate ?? string.Empty).Replace("\r\n", "\n").TrimEnd('\r', '\n');
        var wanted = expected.Replace("\r\n", "\n").TrimEnd('\r', '\n');
        return string.Equals(actual, wanted, StringComparison.Ordinal)
            || (hasAttachments && actual.EndsWith(wanted, StringComparison.Ordinal));
    }

    private sealed record PairingCreated(string DeviceId, string Code, string BridgeCredential, long ExpiresAt);
    private sealed record ThreadCreatePayload(string? Cwd);
    private sealed record MessageSendPayload(string? Text, IReadOnlyList<MessageAttachmentPayload>? Attachments, string? Cwd);
    private sealed record MediaReadPayload(string? ItemId);
    private sealed record PendingDesktopCreation(string Cwd, IReadOnlySet<string> ExistingIds, DateTimeOffset ExpiresAt);
}
