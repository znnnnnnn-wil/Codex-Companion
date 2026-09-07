using System.Net.WebSockets;
using System.Text.Json;
using CodexCompanion.Bridge.Pairing;
using QRCoder;

namespace CodexCompanion.Bridge.Relay;

public sealed class PairingRequiredException() : Exception("保存的凭据已被 Relay 拒绝。需要重新配对：停止 Bridge 后运行 CodexCompanion.Bridge.exe pair。");

public sealed class AuthenticationUnavailableException() : Exception("AUTH_UNAVAILABLE：Relay 认证服务暂不可用，将稍后重试；凭据已保留。");

public sealed record AuthenticationResult(bool Authenticated, bool Paired, string Code);

public static class RelayHandshake
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    public static async Task<AuthenticationResult> ProbeAsync(ClientWebSocket socket, BridgeCredential credential, CancellationToken token)
    {
        var id = Guid.NewGuid().ToString();
        await SendAsync(socket, TransportEnvelope.Create("device.auth.check", id, null, null,
            new { deviceId = credential.DeviceId, credential = credential.Credential }), token);
        var response = await ReceiveAsync(socket, token);
        // Old relays reject unknown message types with UNAUTHORIZED. That is NOT
        // evidence that the credential is stale; never reset on this response.
        if (response.Type != "device.auth.result" || response.RequestId != id)
            return new(false, false, "PROBE_UNSUPPORTED");
        return response.Payload.Deserialize<AuthenticationResult>(Json)
            ?? new(false, false, "INVALID_RESPONSE");
    }

    public static async Task<bool> AuthenticateAsync(ClientWebSocket socket, BridgeCredential credential, CancellationToken token)
    {
        var id = Guid.NewGuid().ToString();
        await SendAsync(socket, TransportEnvelope.Create("device.hello", id, credential.DeviceId, null,
            new { deviceId = credential.DeviceId, credential = credential.Credential, acknowledge = true }), token);
        var response = await ReceiveAsync(socket, token);
        if (response.RequestId != id) throw new InvalidDataException("Relay 认证响应不匹配。");
        ThrowIfRejected(response);
        if (response.Type == "error" && response.Payload.TryGetProperty("code", out var errorCode) && errorCode.GetString() == "AUTH_UNAVAILABLE")
            throw new AuthenticationUnavailableException();
        if (response.Type != "device.authenticated"
            || !response.Payload.TryGetProperty("authenticated", out var authenticated) || authenticated.ValueKind != JsonValueKind.True)
            throw new InvalidDataException("Relay 未确认认证；请先升级 Relay。凭据已保留。");
        return response.Payload.TryGetProperty("paired", out var paired) && paired.ValueKind == JsonValueKind.True;
    }

    public static void ThrowIfRejected(TransportEnvelope response)
    {
        if (response.Type == "error" && response.Payload.TryGetProperty("code", out var code)
            && code.GetString() is "UNAUTHORIZED" or "UNKNOWN_DEVICE" or "INVALID_CREDENTIAL" or "PAIRING_REQUIRED")
            throw new PairingRequiredException();
    }

    public static async Task<BridgeCredential> CreatePairingAsync(ClientWebSocket socket, Uri relayUri, BridgeCredentialStore store, CancellationToken token)
    {
        var id = Guid.NewGuid().ToString();
        await SendAsync(socket, TransportEnvelope.Create("pairing.create", id, null, null,
            new { deviceName = Environment.MachineName }), token);
        var response = await ReceiveAsync(socket, token);
        if (response.Type != "pairing.created" || response.RequestId != id)
            throw new InvalidDataException("Relay 未能创建配对，请稍后重试。旧备份已保留。");
        var pairing = response.Payload.Deserialize<PairingCreated>(Json)
            ?? throw new InvalidDataException("Relay 配对响应无效。");
        if (string.IsNullOrWhiteSpace(pairing.DeviceId) || string.IsNullOrWhiteSpace(pairing.BridgeCredential) || pairing.Code?.Length != 8)
            throw new InvalidDataException("Relay 配对响应无效。");
        var credential = new BridgeCredential(pairing.DeviceId, pairing.BridgeCredential);
        store.Save(credential);
        Console.WriteLine($"Codex Companion 配对码：{pairing.Code}（有效期至 {DateTimeOffset.FromUnixTimeMilliseconds(pairing.ExpiresAt):u}）");
        var pageUri = new UriBuilder(relayUri) { Scheme = relayUri.Scheme == "wss" ? "https" : "http", Path = "/", Query = $"pair={Uri.EscapeDataString(pairing.Code)}", UserName = "", Password = "", Fragment = "" }.Uri;
        Console.WriteLine($"手机配对地址：{pageUri}");
        try
        {
            using var generator = new QRCodeGenerator();
            using var data = generator.CreateQrCode(pageUri.ToString(), QRCodeGenerator.ECCLevel.M);
            Console.WriteLine(new AsciiQRCode(data).GetGraphic(1));
        }
        catch (Exception)
        {
            Console.WriteLine("无法渲染二维码，请使用上方配对码或地址。");
        }
        return credential;
    }

    public static async Task SendAsync(ClientWebSocket socket, TransportEnvelope envelope, CancellationToken token)
        => await socket.SendAsync(JsonSerializer.SerializeToUtf8Bytes(envelope, Json), WebSocketMessageType.Text, true, token);

    public static async Task<TransportEnvelope> ReceiveAsync(ClientWebSocket socket, CancellationToken token)
    {
        using var buffer = new MemoryStream();
        var chunk = new byte[4096];
        while (true)
        {
            var result = await socket.ReceiveAsync(chunk, token);
            if (result.MessageType != WebSocketMessageType.Text) throw new WebSocketException("Relay 在确认认证前关闭连接或返回了非文本消息。");
            buffer.Write(chunk, 0, result.Count);
            if (buffer.Length > 64 * 1024) throw new InvalidDataException("Relay 认证响应超过限制。");
            if (result.EndOfMessage) break;
        }
        return JsonSerializer.Deserialize<TransportEnvelope>(buffer.ToArray(), Json)
            ?? throw new InvalidDataException("Relay 认证响应无效。");
    }

    private sealed record PairingCreated(string DeviceId, string Code, string BridgeCredential, long ExpiresAt);
}
