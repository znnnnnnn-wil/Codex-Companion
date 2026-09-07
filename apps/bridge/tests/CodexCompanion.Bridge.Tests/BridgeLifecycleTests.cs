using System.Net;
using System.Net.Sockets;
using System.Net.WebSockets;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using CodexCompanion.Bridge.Codex.Desktop;
using CodexCompanion.Bridge.Codex.Models;
using CodexCompanion.Bridge.Configuration;
using CodexCompanion.Bridge.Pairing;
using CodexCompanion.Bridge.Relay;
using CodexCompanion.Bridge.Runtime;
using Microsoft.Extensions.Logging.Abstractions;

namespace CodexCompanion.Bridge.Tests;

[CollectionDefinition("Bridge environment", DisableParallelization = true)]
public sealed class BridgeEnvironmentCollection;

[Collection("Bridge environment")]
public sealed class BridgeLifecycleTests : IDisposable
{
    private readonly string _directory = Path.Combine(Path.GetTempPath(), "bridge-tests-" + Guid.NewGuid().ToString("N"));
    private BridgeCredentialStore Store => new(Path.Combine(_directory, "custom", "credential.json"));

    [Theory]
    [InlineData("{", "CREDENTIAL_INVALID")]
    [InlineData("{}", "CREDENTIAL_INVALID")]
    [InlineData("{\"DeviceId\":\"d\",\"ProtectedCredential\":\"!\"}", "CREDENTIAL_INVALID")]
    [InlineData("{\"DeviceId\":\"d\",\"ProtectedCredential\":\"AQID\"}", "CREDENTIAL_DECRYPT_FAILED")]
    public void DamagedCredentialIsDiagnosable(string content, string code)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(Store.FilePath)!);
        File.WriteAllText(Store.FilePath, content);
        Assert.Equal(code, Assert.Throws<CredentialException>(() => Store.Load()).Code);
        Assert.Equal(content, File.ReadAllText(Store.FilePath));
    }

    [Fact]
    public void ResetBacksUpEffectivePathAndLeasePreventsConcurrentReset()
    {
        var store = Store;
        store.Save(new("device", "secret"));
        var original = File.ReadAllBytes(store.FilePath);
        using (var runtime = new BridgeRuntime(store.FilePath))
        {
            Assert.Throws<BridgeAlreadyRunningException>(() => new BridgeRuntime(store.FilePath));
            var backup = store.BackupAndReset();
            Assert.Equal(original, File.ReadAllBytes(backup!));
            Assert.Null(store.Load());
            store.Save(new("new-device", "new-secret"));
            Assert.Equal("new-device", store.Load()!.DeviceId);
        }
        using var next = new BridgeRuntime(store.FilePath);
    }

    [Theory]
    [InlineData("Ready", true, false)]
    [InlineData("PairingRequired", false, true)]
    [InlineData("Reconnecting", false, false)]
    public void RuntimeDistinguishesLiveStatesAndClearsStoppedState(string state, bool authenticated, bool pairingRequired)
    {
        using (var runtime = new BridgeRuntime(Store.FilePath))
        {
            runtime.Set(state, reachable: state != "Reconnecting", connected: authenticated, authenticated: authenticated, pairingRequired: pairingRequired);
            var status = BridgeRuntime.Read(Store.FilePath);
            Assert.True(status.ProcessRunning);
            Assert.Equal(authenticated, status.Authenticated);
            Assert.Equal(pairingRequired, status.PairingRequired);
            Assert.Equal(state == "Ready", status.Ready);
        }
        var stopped = BridgeRuntime.Read(Store.FilePath);
        Assert.False(stopped.ProcessRunning);
        Assert.False(stopped.Authenticated);
        Assert.False(stopped.Ready);
    }

    [Fact]
    public void RuntimeRejectsDeadReusedPidAndExpiredHeartbeat()
    {
        var now = DateTimeOffset.UtcNow;
        var saved = new RuntimeStatus { ProcessRunning = true, ProcessId = 123, ProcessStartedUtcTicks = 456,
            State = "Ready", Connected = true, Authenticated = true, UpdatedAt = now };
        var stopped = BridgeRuntime.Validate(saved, now, (id, ticks) => id == 123 && ticks == 999);
        Assert.Equal("Stopped", stopped.State);
        Assert.False(stopped.Ready);
        var expired = BridgeRuntime.Validate(saved, now.AddMinutes(1), (_, _) => true);
        Assert.True(expired.ProcessRunning);
        Assert.Equal("Unavailable", expired.State);
        Assert.False(expired.Authenticated);
    }

    [Fact]
    public async Task RuntimeSupportsConcurrentReadersAndRejectsDifferentRelay()
    {
        using var runtime = new BridgeRuntime(Store.FilePath, "ws://relay-one/ws/bridge");
        using var stop = new CancellationTokenSource();
        var reader = Task.Run(() =>
        {
            while (!stop.IsCancellationRequested) BridgeRuntime.Read(Store.FilePath);
        });
        try
        {
            for (var index = 0; index < 50; index++)
                runtime.Set("Ready", reachable: true, connected: true, authenticated: true);
            Assert.True(BridgeRuntime.Read(Store.FilePath, "ws://relay-one/ws/bridge").Ready);
            Assert.False(BridgeRuntime.Read(Store.FilePath, "ws://relay-two/ws/bridge").Authenticated);
        }
        finally { stop.Cancel(); await reader; }
    }

    [Fact]
    public void ConfigurationAndEnvironmentResolveCustomCredential()
    {
        Directory.CreateDirectory(_directory);
        var config = Path.Combine(_directory, "config.json");
        File.WriteAllText(config, "{\"credentialPath\":\"custom/credential.json\"}");
        WithEnvironment(config, null, () => Assert.Equal(Store.FilePath, new BridgeCredentialStore(BridgeConfiguration.Load().CredentialPath).FilePath));
        var overridePath = Path.Combine(_directory, "override.json");
        WithEnvironment(config, overridePath, () => Assert.Equal(overridePath, BridgeConfiguration.Load().CredentialPath));
    }

    [Theory]
    [InlineData("UNAUTHORIZED")]
    [InlineData("UNKNOWN_DEVICE")]
    [InlineData("INVALID_CREDENTIAL")]
    [InlineData("PAIRING_REQUIRED")]
    public async Task RejectedCredentialBecomesPairingRequiredWithoutReset(string code)
    {
        Store.Save(new("device", "private-secret"));
        var original = File.ReadAllBytes(Store.FilePath);
        using var runtime = new BridgeRuntime(Store.FilePath);
        using var stop = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        await using var server = new FakeRelay(async socket =>
        {
            var hello = await FakeRelay.Read(socket, stop.Token);
            Assert.Equal("device.hello", hello.Type);
            await FakeRelay.Send(socket, TransportEnvelope.Create("error", hello.RequestId, null, null, new { code }), stop.Token);
            await WaitUntil(() => BridgeRuntime.Read(Store.FilePath).PairingRequired);
            stop.Cancel();
        });
        var client = new BridgeRelayClient(server.Uri, Store, null!, null!, NullLogger<BridgeRelayClient>.Instance, runtime);
        await client.RunAsync(stop.Token);
        await server.Completion;
        var status = BridgeRuntime.Read(Store.FilePath);
        Assert.Equal("PairingRequired", status.State);
        Assert.False(status.Authenticated);
        Assert.Contains("pair", status.LastError);
        Assert.Equal(original, File.ReadAllBytes(Store.FilePath));
        Assert.DoesNotContain("private-secret", File.ReadAllText(Store.FilePath + ".runtime.json"));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task RunAuthenticatesExistingCredentialOrCreatesMissingPairing(bool missing)
    {
        if (!missing) Store.Save(new("device", "secret"));
        using var runtime = new BridgeRuntime(Store.FilePath);
        using var stop = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        await using var server = new FakeRelay(async socket =>
        {
            var first = await FakeRelay.Read(socket, stop.Token);
            Assert.Equal(missing ? "pairing.create" : "device.hello", first.Type);
            var response = missing
                ? TransportEnvelope.Create("pairing.created", first.RequestId, "device", null, new { deviceId = "device", bridgeCredential = "secret", code = "ABCD2345", expiresAt = DateTimeOffset.UtcNow.AddMinutes(10).ToUnixTimeMilliseconds() })
                : TransportEnvelope.Create("device.authenticated", first.RequestId, "device", null, new { authenticated = true, paired = true });
            await FakeRelay.Send(socket, response, stop.Token);
            await WaitUntil(() => BridgeRuntime.Read(Store.FilePath).Authenticated);
            Assert.Equal(missing, BridgeRuntime.Read(Store.FilePath).PairingRequired);
            if (missing)
                await FakeRelay.Send(socket, TransportEnvelope.Create("pairing.completed", null, "device", null, new { paired = true }), stop.Token);
            await WaitUntil(() => BridgeRuntime.Read(Store.FilePath).Ready);
            stop.Cancel();
        });
        var client = new BridgeRelayClient(server.Uri, Store, null!, new IdleDesktop(), NullLogger<BridgeRelayClient>.Instance, runtime);
        await client.RunAsync(stop.Token);
        await server.Completion;
        Assert.Equal("secret", Store.Load()!.Credential);
        Assert.True(BridgeRuntime.Read(Store.FilePath).Ready);
    }

    [Fact]
    public async Task UnavailableNetworkPreservesCredentialAndReconnects()
    {
        Store.Save(new("device", "secret"));
        var original = File.ReadAllBytes(Store.FilePath);
        var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        var port = ((IPEndPoint)listener.LocalEndpoint).Port;
        listener.Stop();
        using var runtime = new BridgeRuntime(Store.FilePath);
        using var stop = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        var client = new BridgeRelayClient(new Uri($"ws://127.0.0.1:{port}"), Store, null!, null!, NullLogger<BridgeRelayClient>.Instance, runtime);
        var running = client.RunAsync(stop.Token);
        await WaitUntil(() => BridgeRuntime.Read(Store.FilePath).State == "Reconnecting");
        Assert.False(BridgeRuntime.Read(Store.FilePath).PairingRequired);
        Assert.False(BridgeRuntime.Read(Store.FilePath).Authenticated);
        stop.Cancel();
        await running;
        Assert.Equal(original, File.ReadAllBytes(Store.FilePath));
    }

    [Fact]
    public async Task OldRelayProbeRejectionDoesNotMeanStaleCredential()
    {
        using var stop = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        await using var server = new FakeRelay(async socket =>
        {
            var request = await FakeRelay.Read(socket, stop.Token);
            Assert.Equal("device.auth.check", request.Type);
            await FakeRelay.Send(socket, TransportEnvelope.Create("error", request.RequestId, null, null, new { code = "UNAUTHORIZED" }), stop.Token);
        });
        using var client = new ClientWebSocket();
        await client.ConnectAsync(server.Uri, stop.Token);
        var result = await RelayHandshake.ProbeAsync(client, new("device", "secret"), stop.Token);
        Assert.False(result.Authenticated);
        Assert.Equal("PROBE_UNSUPPORTED", result.Code);
        await server.Completion;
    }

    [Fact]
    public async Task AuthenticationServiceOutageKeepsReachabilityAndDoesNotResetPairing()
    {
        Store.Save(new("device", "secret"));
        var original = File.ReadAllBytes(Store.FilePath);
        using var runtime = new BridgeRuntime(Store.FilePath);
        using var stop = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        await using var server = new FakeRelay(async socket =>
        {
            var request = await FakeRelay.Read(socket, stop.Token);
            await FakeRelay.Send(socket, TransportEnvelope.Create("error", request.RequestId, null, null, new { code = "AUTH_UNAVAILABLE" }), stop.Token);
            await WaitUntil(() => BridgeRuntime.Read(Store.FilePath).State == "Reconnecting");
            stop.Cancel();
        });
        await new BridgeRelayClient(server.Uri, Store, null!, null!, NullLogger<BridgeRelayClient>.Instance, runtime).RunAsync(stop.Token);
        await server.Completion;
        var status = BridgeRuntime.Read(Store.FilePath);
        Assert.True(status.RelayReachable);
        Assert.False(status.PairingRequired);
        Assert.False(status.Authenticated);
        Assert.Contains("AUTH_UNAVAILABLE", status.LastError);
        Assert.Equal(original, File.ReadAllBytes(Store.FilePath));
    }

    private static async Task WaitUntil(Func<bool> condition)
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(3));
        while (!condition()) await Task.Delay(10, timeout.Token);
    }

    [Fact]
    public async Task PairCommandUsesCustomPathBacksUpAndExitsOnCompletion()
    {
        Store.Save(new("old-device", "old-secret"));
        var original = File.ReadAllBytes(Store.FilePath);
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        await using var server = new FakeRelay(async socket =>
        {
            var request = await FakeRelay.Read(socket, timeout.Token);
            Assert.Equal("pairing.create", request.Type);
            await FakeRelay.Send(socket, TransportEnvelope.Create("pairing.created", request.RequestId, "new-device", null,
                new { deviceId = "new-device", bridgeCredential = "new-secret", code = "ABCD2345", expiresAt = DateTimeOffset.UtcNow.AddMinutes(10).ToUnixTimeMilliseconds() }), timeout.Token);
            await FakeRelay.Send(socket, TransportEnvelope.Create("pairing.completed", null, "new-device", null, new { paired = true }), timeout.Token);
        });
        var previous = Console.Out;
        using var output = new StringWriter();
        try
        {
            Console.SetOut(output);
            Assert.Equal(0, await Program.Main(["pair", "--config", Path.Combine(_directory, "config.json"), "--credential-path", Store.FilePath, "--relay-url", server.Uri.ToString()]));
        }
        finally { Console.SetOut(previous); }
        Assert.Equal("new-device", Store.Load()!.DeviceId);
        Assert.Equal(original, File.ReadAllBytes(Assert.Single(Directory.GetFiles(Path.GetDirectoryName(Store.FilePath)!, "*.backup-*"))));
        Assert.Contains("ABCD2345", output.ToString());
        Assert.Contains("?pair=ABCD2345", output.ToString());
        Assert.DoesNotContain("new-secret", output.ToString());
        Assert.DoesNotContain("old-secret", output.ToString());
        Assert.False(BridgeRuntime.Read(Store.FilePath).ProcessRunning);
    }

    [Theory]
    [InlineData("missing", "CREDENTIAL_MISSING")]
    [InlineData("invalid", "CREDENTIAL_INVALID")]
    [InlineData("decrypt", "CREDENTIAL_DECRYPT_FAILED")]
    [InlineData("rejected", "已失效")]
    [InlineData("accepted", "device authenticated")]
    public async Task DoctorJsonSeparatesLocalCredentialAndRelayAuthentication(string scenario, string expected)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(Store.FilePath)!);
        if (scenario == "invalid") File.WriteAllText(Store.FilePath, "{");
        else if (scenario == "decrypt") File.WriteAllText(Store.FilePath, "{\"DeviceId\":\"d\",\"ProtectedCredential\":\"AQID\"}");
        else if (scenario != "missing") Store.Save(new("device", "private-secret"));
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        await using var server = new FakeRelay(async socket =>
        {
            if (scenario is not ("accepted" or "rejected")) return;
            var request = await FakeRelay.Read(socket, timeout.Token);
            Assert.Equal("device.auth.check", request.Type);
            await FakeRelay.Send(socket, TransportEnvelope.Create("device.auth.result", request.RequestId, null, null,
                new { authenticated = scenario == "accepted", paired = scenario == "accepted", code = scenario == "accepted" ? "OK" : "UNAUTHORIZED" }), timeout.Token);
        });
        var previous = Console.Out;
        using var output = new StringWriter();
        try
        {
            Console.SetOut(output);
            // Avoid launching or controlling the user's real Codex installation.
            await Program.Main(["doctor", "--json", "--config", Path.Combine(_directory, "config.json"),
                "--credential-path", Store.FilePath, "--relay-url", server.Uri.ToString(), "--codex-executable", Path.Combine(_directory, "missing-codex.exe")]);
        }
        finally { Console.SetOut(previous); }
        using var report = JsonDocument.Parse(output.ToString());
        var checks = report.RootElement.GetProperty("checks").EnumerateArray().ToArray();
        Assert.Contains(checks, check => check.GetProperty("message").GetString()!.Contains(expected));
        Assert.Contains(checks, check => check.GetProperty("name").GetString() == "Relay 网络" && check.GetProperty("ok").GetBoolean());
        Assert.DoesNotContain("private-secret", output.ToString());
        Assert.DoesNotContain("INTERNAL_ERROR", output.ToString());
        if (scenario == "accepted") Assert.Contains(checks, check => check.GetProperty("name").GetString() == "Relay authentication" && check.GetProperty("ok").GetBoolean());
    }

    private static void WithEnvironment(string config, string? credential, Action test)
    {
        var previousConfig = Environment.GetEnvironmentVariable("CODEX_COMPANION_CONFIG_PATH");
        var previousCredential = Environment.GetEnvironmentVariable("CODEX_COMPANION_CREDENTIAL_PATH");
        try
        {
            Environment.SetEnvironmentVariable("CODEX_COMPANION_CONFIG_PATH", config);
            Environment.SetEnvironmentVariable("CODEX_COMPANION_CREDENTIAL_PATH", credential);
            test();
        }
        finally
        {
            Environment.SetEnvironmentVariable("CODEX_COMPANION_CONFIG_PATH", previousConfig);
            Environment.SetEnvironmentVariable("CODEX_COMPANION_CREDENTIAL_PATH", previousCredential);
        }
    }

    public void Dispose() { if (Directory.Exists(_directory)) Directory.Delete(_directory, true); }

    private sealed class IdleDesktop : ICodexDesktopAdapter
    {
        public bool IsCodexRunning() => true;
        public DesktopConversation? GetCurrentConversation() => null;
        public DesktopStatus GetDesktopStatus() => new(true, "idle", null);
        public Task CreateConversationAsync(string cwd, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task OpenConversationAsync(CodexThreadSummary thread, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task SendMessageAsync(string text, IReadOnlyList<string>? attachmentPaths = null, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task StopAsync(CodexThreadSummary thread, CancellationToken cancellationToken = default) => throw new NotSupportedException();
    }

    // A loopback WebSocket server without HTTP.sys URL ACLs or external services.
    private sealed class FakeRelay : IAsyncDisposable
    {
        private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);
        private readonly TcpListener _listener = new(IPAddress.Loopback, 0);
        public Uri Uri { get; }
        public Task Completion { get; }
        public FakeRelay(Func<WebSocket, Task> handler)
        {
            _listener.Start();
            Uri = new Uri($"ws://127.0.0.1:{((IPEndPoint)_listener.LocalEndpoint).Port}/ws/bridge");
            Completion = Serve(handler);
        }
        private async Task Serve(Func<WebSocket, Task> handler)
        {
            using var client = await _listener.AcceptTcpClientAsync();
            await using var stream = client.GetStream();
            using var reader = new StreamReader(stream, Encoding.ASCII, false, 1024, leaveOpen: true);
            string? line, key = null;
            while (!string.IsNullOrEmpty(line = await reader.ReadLineAsync()))
                if (line.StartsWith("Sec-WebSocket-Key:", StringComparison.OrdinalIgnoreCase)) key = line.Split(':', 2)[1].Trim();
            var accept = Convert.ToBase64String(SHA1.HashData(Encoding.ASCII.GetBytes(key + "258EAFA5-E914-47DA-95CA-C5AB0DC85B11")));
            await stream.WriteAsync(Encoding.ASCII.GetBytes($"HTTP/1.1 101 Switching Protocols\r\nUpgrade: websocket\r\nConnection: Upgrade\r\nSec-WebSocket-Accept: {accept}\r\n\r\n"));
            using var socket = WebSocket.CreateFromStream(stream, true, null, Timeout.InfiniteTimeSpan);
            await handler(socket);
        }
        public static async Task<TransportEnvelope> Read(WebSocket socket, CancellationToken token)
        {
            var bytes = new byte[65536];
            var result = await socket.ReceiveAsync(bytes, token);
            return JsonSerializer.Deserialize<TransportEnvelope>(bytes.AsSpan(0, result.Count), Json)!;
        }
        public static async Task Send(WebSocket socket, TransportEnvelope envelope, CancellationToken token)
            => await socket.SendAsync(JsonSerializer.SerializeToUtf8Bytes(envelope, Json), WebSocketMessageType.Text, true, token);
        public async ValueTask DisposeAsync() { _listener.Stop(); await Completion; }
    }
}
