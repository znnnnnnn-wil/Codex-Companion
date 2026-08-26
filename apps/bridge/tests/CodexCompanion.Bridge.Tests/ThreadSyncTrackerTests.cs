using CodexCompanion.Bridge.Codex.Models;
using CodexCompanion.Bridge.Relay;

namespace CodexCompanion.Bridge.Tests;

public sealed class ThreadSyncTrackerTests
{
    [Fact]
    public void Diff_AfterSeed_ReturnsOnlyNewItems()
    {
        var tracker = new ThreadSyncTracker();
        tracker.Seed(History([
            Item("u1", "user", "hello"),
        ]));

        var delta = tracker.Diff(History([
            Item("u1", "user", "hello"),
            Item("a1", "assistant", "world"),
        ]));

        Assert.True(delta.HasChanges);
        var item = Assert.Single(delta.Items);
        Assert.Equal("a1", item.Id);
        Assert.Equal("world", item.Content);
    }

    private static CodexThreadHistory History(IReadOnlyList<CodexThreadItem> items)
        => new("thr", "title", "C:\\repo", 1, "idle", items);

    private static CodexThreadItem Item(string id, string role, string content)
        => new(id, "message", role == "user" ? "userMessage" : "agentMessage", role, content, null, "turn");
}
