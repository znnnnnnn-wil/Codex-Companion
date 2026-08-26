using CodexCompanion.Bridge.Codex.AppServer;
using CodexCompanion.Bridge.Codex.Models;

namespace CodexCompanion.Bridge.Codex.History;

public sealed class CodexHistoryAdapter(ICodexAppServerClient client) : ICodexHistoryAdapter
{
    public async Task<IReadOnlyList<CodexThreadSummary>> ListThreadsAsync(
        CancellationToken cancellationToken = default)
    {
        var threads = new List<CodexThreadSummary>();
        var seen = new HashSet<string>(StringComparer.Ordinal);
        string? cursor = null;
        do
        {
            var result = await client.InvokeAsync("thread/list", new
            {
                cursor,
                limit = 100,
                sortKey = "updated_at",
                sortDirection = "desc",
                sourceKinds = new[] { "cli", "vscode", "appServer" }
            }, cancellationToken);
            foreach (var thread in CodexHistoryParser.ParseThreadList(result))
            {
                if (seen.Add(thread.ThreadId))
                {
                    threads.Add(thread);
                }
            }
            cursor = result.TryGetProperty("nextCursor", out var nextCursor)
                     && nextCursor.ValueKind == System.Text.Json.JsonValueKind.String
                ? nextCursor.GetString()
                : null;
        } while (!string.IsNullOrWhiteSpace(cursor));
        return threads;
    }

    public async Task<CodexThreadSummary> CreateThreadAsync(
        string cwd,
        CancellationToken cancellationToken = default)
    {
        var normalizedCwd = Path.GetFullPath(cwd);
        var started = await client.InvokeAsync("thread/start", new
        {
            cwd = normalizedCwd,
            serviceName = "codex_companion_bridge"
        }, cancellationToken);
        if (!started.TryGetProperty("thread", out var createdThread)
            || createdThread.ValueKind != System.Text.Json.JsonValueKind.Object
            || !createdThread.TryGetProperty("id", out var idElement)
            || string.IsNullOrWhiteSpace(idElement.GetString()))
        {
            throw new InvalidDataException("thread/start 响应缺少 thread.id。");
        }

        var threadId = idElement.GetString()!;
        var title = $"新会话 {DateTime.Now:HH:mm:ss}";
        // Codex 0.149.1 returns a complete real thread from thread/start, but its
        // thread/name/set endpoint does not complete on the current app-server.
        // Keep the server response as the source of truth and let the next list
        // refresh pick up any Desktop-assigned name.
        return new CodexThreadSummary(
            threadId,
            GetThreadString(createdThread, "name") ?? title,
            GetThreadString(createdThread, "cwd") ?? normalizedCwd,
            GetThreadInt64(createdThread, "updatedAt", DateTimeOffset.UtcNow.ToUnixTimeSeconds()),
            GetNestedThreadString(createdThread, "status", "type") ?? "idle",
            GetThreadString(createdThread, "threadSource") ?? GetThreadString(createdThread, "source") ?? "appServer");
    }

    private static string? GetThreadString(System.Text.Json.JsonElement element, string property)
        => element.TryGetProperty(property, out var value) && value.ValueKind == System.Text.Json.JsonValueKind.String
            ? value.GetString()
            : null;

    private static long GetThreadInt64(System.Text.Json.JsonElement element, string property, long fallback)
        => element.TryGetProperty(property, out var value) && value.TryGetInt64(out var number) ? number : fallback;

    private static string? GetNestedThreadString(System.Text.Json.JsonElement element, string parent, string property)
        => element.TryGetProperty(parent, out var nested) ? GetThreadString(nested, property) : null;

    public async Task<CodexThreadHistory> ReadThreadAsync(
        string threadId,
        CancellationToken cancellationToken = default)
    {
        var result = await client.InvokeAsync("thread/read", new
        {
            threadId,
            includeTurns = true
        }, cancellationToken);
        return CodexHistoryParser.ParseThreadRead(result);
    }

    public async Task<CodexMediaContent?> ReadMediaAsync(
        string threadId,
        string itemId,
        CancellationToken cancellationToken = default)
    {
        var result = await client.InvokeAsync("thread/read", new
        {
            threadId,
            includeTurns = true
        }, cancellationToken);
        return CodexHistoryParser.ParseThreadMedia(result, threadId, itemId);
    }
}
