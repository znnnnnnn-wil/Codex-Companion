using System.Text.Json;

namespace CodexCompanion.Bridge.Relay;

public sealed record TransportEnvelope(
    string Type,
    string? RequestId,
    string? DeviceId,
    string? ThreadId,
    long Timestamp,
    JsonElement Payload)
{
    private static readonly JsonSerializerOptions WebJson = new(JsonSerializerDefaults.Web);

    public static TransportEnvelope Create(
        string type,
        string? requestId,
        string? deviceId,
        string? threadId,
        object? payload)
    {
        return new TransportEnvelope(
            type,
            requestId,
            deviceId,
            threadId,
            DateTimeOffset.UtcNow.ToUnixTimeMilliseconds(),
            JsonSerializer.SerializeToElement(payload ?? new { }, WebJson));
    }
}
