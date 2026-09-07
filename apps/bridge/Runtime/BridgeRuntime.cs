using System.Diagnostics;
using System.Text.Json;
using System.Security.Cryptography;
using System.Text;

namespace CodexCompanion.Bridge.Runtime;

public sealed class BridgeAlreadyRunningException() : InvalidOperationException("Bridge 已运行或正在配对。请先执行 bridge-control.ps1 -Action Stop，或停止前台 run/pair。");

public sealed record RuntimeStatus
{
    public int ProcessId { get; init; }
    public long ProcessStartedUtcTicks { get; init; }
    public DateTimeOffset UpdatedAt { get; init; }
    public bool ProcessRunning { get; init; }
    public bool? RelayReachable { get; init; }
    public bool Connected { get; init; }
    public bool Authenticated { get; init; }
    public bool PairingRequired { get; init; }
    public bool Ready => ProcessRunning && Connected && Authenticated && !PairingRequired && State == "Ready";
    public string State { get; init; } = "Stopped";
    public string? LastError { get; init; }
    public string? ConfigurationId { get; init; }
    public DateTimeOffset? LastConnectedAt { get; init; }
}

// A lease is shared by run and pair, keyed by the effective credential path.
// File sharing locks work across Windows sessions and are released on crashes.
public sealed class BridgeRuntime : IDisposable
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);
    private readonly string _path;
    private readonly FileStream _lease;
    private readonly Timer _heartbeat;
    private readonly object _sync = new();
    private bool _disposed;
    private RuntimeStatus _status;

    public BridgeRuntime(string credentialPath, string? relayUrl = null)
    {
        _path = credentialPath + ".runtime.json";
        Directory.CreateDirectory(Path.GetDirectoryName(_path)!);
        try { _lease = new FileStream(_path + ".lock", FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None); }
        catch (IOException exception) when ((exception.HResult & 0xffff) is 32 or 33)
        { throw new BridgeAlreadyRunningException(); }
        using var process = Process.GetCurrentProcess();
        _status = new RuntimeStatus { ProcessId = process.Id, ProcessStartedUtcTicks = process.StartTime.ToUniversalTime().Ticks, ProcessRunning = true, State = "Starting", ConfigurationId = ConfigurationId(relayUrl) };
        try { Write(); }
        catch { _lease.Dispose(); throw; }
        _heartbeat = new Timer(_ =>
        {
            lock (_sync)
            {
                if (!_disposed)
                {
                    try { Write(); }
                    catch (IOException) { /* Readers expire this status instead of reporting ready. */ }
                    catch (UnauthorizedAccessException) { }
                }
            }
        }, null, TimeSpan.FromSeconds(3), TimeSpan.FromSeconds(3));
    }

    public void Set(string state, bool? reachable = null, bool connected = false, bool authenticated = false, bool pairingRequired = false, string? error = null)
    {
        lock (_sync)
        {
            _status = _status with { State = state, RelayReachable = reachable, Connected = connected,
                Authenticated = authenticated, PairingRequired = pairingRequired, LastError = error,
                LastConnectedAt = connected ? DateTimeOffset.UtcNow : _status.LastConnectedAt };
            Write();
        }
        Console.WriteLine($"Bridge: {state}" + (error is null ? "" : $" — {error}"));
    }

    private void Write()
    {
        _status = _status with { UpdatedAt = DateTimeOffset.UtcNow };
        var temporary = _path + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try
        {
            File.WriteAllText(temporary, JsonSerializer.Serialize(_status, Json));
            for (var attempt = 0; ; attempt++)
            {
                try
                {
                    if (File.Exists(_path)) File.Replace(temporary, _path, null);
                    else File.Move(temporary, _path);
                    break;
                }
                catch (Exception exception) when (attempt < 5 && exception is IOException or UnauthorizedAccessException)
                {
                    // Windows may briefly retain the replaced file during concurrent
                    // reads/antivirus scans. Retry the atomic operation, never truncate.
                    Thread.Sleep(20);
                }
            }
        }
        finally { if (File.Exists(temporary)) File.Delete(temporary); }
    }

    private static string? ConfigurationId(string? relayUrl)
        => relayUrl is null ? null : Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(relayUrl)));

    public static RuntimeStatus Read(string credentialPath, string? relayUrl = null)
    {
        try
        {
            // Allow atomic replacement while status is being read on Windows.
            using var stream = new FileStream(credentialPath + ".runtime.json", FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
            var status = JsonSerializer.Deserialize<RuntimeStatus>(stream, Json);
            var validated = Validate(status, DateTimeOffset.UtcNow, IsProcessAlive);
            if (relayUrl is not null && validated.ProcessRunning && validated.ConfigurationId != ConfigurationId(relayUrl))
                return validated with { State = "Unavailable", Connected = false, Authenticated = false, PairingRequired = false, RelayReachable = null,
                    LastError = "运行中的 Bridge 使用不同的 Relay 配置；请停止后重新启动。" };
            return validated;
        }
        catch (Exception exception) when (exception is IOException or JsonException or UnauthorizedAccessException)
        { return new RuntimeStatus { State = "Unavailable" }; }
    }

    public static RuntimeStatus Validate(RuntimeStatus? status, DateTimeOffset now, Func<int, long, bool> isAlive)
    {
        if (status is null) return new RuntimeStatus { State = "Unavailable" };
        if (!status.ProcessRunning || !isAlive(status.ProcessId, status.ProcessStartedUtcTicks))
            return status with { State = "Stopped", ProcessRunning = false, Connected = false, Authenticated = false, PairingRequired = false, RelayReachable = null };
        if (now - status.UpdatedAt > TimeSpan.FromSeconds(15) || status.UpdatedAt > now.AddSeconds(5))
            return status with { State = "Unavailable", Connected = false, Authenticated = false, PairingRequired = false, RelayReachable = null };
        return status;
    }

    private static bool IsProcessAlive(int id, long ticks)
    {
        try { using var process = Process.GetProcessById(id); return !process.HasExited && process.StartTime.ToUniversalTime().Ticks == ticks; }
        catch { return false; }
    }

    public void Dispose()
    {
        lock (_sync)
        {
            _disposed = true;
            _heartbeat.Dispose();
            _status = _status with { ProcessRunning = false, Connected = false, Authenticated = false, PairingRequired = false, RelayReachable = null };
            try { Write(); }
            finally { _lease.Dispose(); }
        }
    }
}
