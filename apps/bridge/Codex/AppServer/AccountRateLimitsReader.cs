using System.Text.Json;

namespace CodexCompanion.Bridge.Codex.AppServer;

public sealed record UsageWindow(double? UsedPercent, int? WindowDurationMins, long? ResetsAt);
public sealed record UsageBucket(string LimitId, string? LimitName, UsageWindow? Primary, UsageWindow? Secondary);
public sealed record AccountUsage(IReadOnlyList<UsageBucket> Buckets, long FetchedAt);

/// <summary>Projects only quota windows; never forwards account credentials or raw server errors.</summary>
public sealed class AccountRateLimitsReader(ICodexAppServerClient client)
{
    public async Task<AccountUsage> ReadAsync(CancellationToken cancellationToken = default) =>
        Parse(await client.InvokeAsync("account/rateLimits/read", new { }, cancellationToken));

    public static AccountUsage Parse(JsonElement result)
    {
        var buckets = new List<UsageBucket>();
        if (result.TryGetProperty("rateLimitsByLimitId", out var byId) && byId.ValueKind == JsonValueKind.Object)
        {
            foreach (var property in byId.EnumerateObject())
                if (property.Value.ValueKind == JsonValueKind.Object) buckets.Add(Bucket(property.Value, property.Name));
        }
        if (buckets.Count == 0 && result.TryGetProperty("rateLimits", out var legacy) && legacy.ValueKind == JsonValueKind.Object)
            buckets.Add(Bucket(legacy, "codex"));
        return new(buckets, DateTimeOffset.UtcNow.ToUnixTimeSeconds());
    }

    private static UsageBucket Bucket(JsonElement value, string id) => new(
        Text(value, "limitId") ?? id, Text(value, "limitName"), Window(value, "primary"), Window(value, "secondary"));

    private static string? Text(JsonElement value, string key) =>
        value.TryGetProperty(key, out var field) && field.ValueKind == JsonValueKind.String ? field.GetString() : null;

    private static UsageWindow? Window(JsonElement value, string key)
    {
        if (!value.TryGetProperty(key, out var window) || window.ValueKind != JsonValueKind.Object) return null;
        double? used = window.TryGetProperty("usedPercent", out var u) && u.ValueKind == JsonValueKind.Number && u.TryGetDouble(out var d) && double.IsFinite(d) ? d : null;
        int? duration = window.TryGetProperty("windowDurationMins", out var w) && w.ValueKind == JsonValueKind.Number && w.TryGetInt32(out var minutes) && minutes > 0 ? minutes : null;
        long? reset = window.TryGetProperty("resetsAt", out var r) && r.ValueKind == JsonValueKind.Number && r.TryGetInt64(out var seconds) && seconds > 0 && seconds <= 253402300799 ? seconds : null;
        return new(used, duration, reset);
    }
}
