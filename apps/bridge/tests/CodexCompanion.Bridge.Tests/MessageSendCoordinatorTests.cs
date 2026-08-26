using CodexCompanion.Bridge.Codex.Desktop;
using CodexCompanion.Bridge.Codex.History;
using CodexCompanion.Bridge.Codex.Models;

namespace CodexCompanion.Bridge.Tests;

public sealed class MessageSendCoordinatorTests
{
    private static readonly CodexThreadSummary Thread = new("thr", "title", "C:\\repo", 1, "idle", "vscode");

    [Fact]
    public async Task SendAndConfirm_AcceptsDesktopTrailingLineFeed()
    {
        var before = History([]);
        var after = History([new CodexThreadItem("u1", "message", "userMessage", "user", "hello\n", null, "t1")]);
        var history = new SequencedHistory(before, after);
        var desktop = new FakeDesktop();
        var coordinator = new MessageSendCoordinator(history, desktop, TimeSpan.FromSeconds(1), TimeSpan.FromMilliseconds(1));

        var result = await coordinator.SendAndConfirmAsync("thr", "hello");

        Assert.True(result.Confirmed);
        Assert.Equal("u1", result.MessageId);
        Assert.Equal("hello", desktop.SentText);
    }

    [Fact]
    public async Task SendAndConfirm_AcceptsMarkdownEscapesAddedByCodexHistory()
    {
        var before = History([]);
        var after = History([new CodexThreadItem(
            "u1", "message", "userMessage", "user",
            "reply PUBLIC\\_E2E\\_OK\\!\n", null, "t1")]);
        var history = new SequencedHistory(before, after);
        var coordinator = new MessageSendCoordinator(
            history,
            new FakeDesktop(),
            TimeSpan.FromSeconds(1),
            TimeSpan.FromMilliseconds(1));

        var result = await coordinator.SendAndConfirmAsync("thr", "reply PUBLIC_E2E_OK!");

        Assert.True(result.Confirmed);
        Assert.Equal("u1", result.MessageId);
    }

    [Fact]
    public async Task SendAndConfirm_TimesOutWhenRealHistoryNeverContainsMessage()
    {
        var history = new SequencedHistory(History([]));
        var coordinator = new MessageSendCoordinator(
            history,
            new FakeDesktop(),
            TimeSpan.FromMilliseconds(15),
            TimeSpan.FromMilliseconds(1));

        var error = await Assert.ThrowsAsync<BridgeException>(() => coordinator.SendAndConfirmAsync("thr", "hello"));

        Assert.Equal("THREAD_CONFIRM_TIMEOUT", error.ProtocolCode);
    }

    [Fact]
    public async Task SendAndConfirm_AcceptsRealDesktopAttachmentWrapper()
    {
        var before = History([]);
        var after = History([new CodexThreadItem(
            "u1", "message", "userMessage", "user",
            "# Files mentioned by the user:\n\n## photo.png: C:/temp/photo.png\n\n## My request:\ncheck ATTACHMENT\\_OK\n",
            null, "t1")]);
        var history = new SequencedHistory(before, after);
        var coordinator = new MessageSendCoordinator(
            history,
            new FakeDesktop(),
            TimeSpan.FromSeconds(1),
            TimeSpan.FromMilliseconds(1));

        var result = await coordinator.SendAndConfirmAsync("thr", "check ATTACHMENT_OK", ["C:\\temp\\photo.png"]);

        Assert.True(result.Confirmed);
        Assert.Equal("u1", result.MessageId);
    }

    private static CodexThreadHistory History(IReadOnlyList<CodexThreadItem> items)
        => new("thr", "title", "C:\\repo", 1, "idle", items);

    private sealed class SequencedHistory(params CodexThreadHistory[] histories) : ICodexHistoryAdapter
    {
        private int _index;
        public Task<IReadOnlyList<CodexThreadSummary>> ListThreadsAsync(CancellationToken cancellationToken = default)
            => Task.FromResult<IReadOnlyList<CodexThreadSummary>>([Thread]);

        public Task<CodexThreadHistory> ReadThreadAsync(string threadId, CancellationToken cancellationToken = default)
        {
            var index = Math.Min(Interlocked.Increment(ref _index) - 1, histories.Length - 1);
            return Task.FromResult(histories[index]);
        }
    }

    private sealed class FakeDesktop : ICodexDesktopAdapter
    {
        public string? SentText { get; private set; }
        public bool IsCodexRunning() => true;
        public DesktopConversation? GetCurrentConversation() => null;
        public Task CreateConversationAsync(string cwd, CancellationToken cancellationToken = default) => Task.CompletedTask;
        public Task OpenConversationAsync(CodexThreadSummary thread, CancellationToken cancellationToken = default) => Task.CompletedTask;
        public Task SendMessageAsync(string text, IReadOnlyList<string>? attachmentPaths = null, CancellationToken cancellationToken = default)
        {
            SentText = text;
            return Task.CompletedTask;
        }
        public Task StopAsync(CodexThreadSummary thread, CancellationToken cancellationToken = default) => Task.CompletedTask;
        public DesktopStatus GetDesktopStatus() => new(true, "idle", null);
    }
}
