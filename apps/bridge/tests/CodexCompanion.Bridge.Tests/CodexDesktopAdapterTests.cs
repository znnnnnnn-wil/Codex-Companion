using CodexCompanion.Bridge.Codex.Desktop;
using CodexCompanion.Bridge.Codex.Models;

namespace CodexCompanion.Bridge.Tests;

public sealed class CodexDesktopAdapterTests
{
    private static readonly CodexThreadSummary Thread = new("thr", "title", "C:\\repo", 1, "idle", "vscode");

    [Fact]
    public async Task OpenConversation_RejectsAmbiguousTitle()
    {
        var adapter = new CodexDesktopAdapter(new FakeUiDriver { OpenResult = ConversationOpenResult.Ambiguous });

        var error = await Assert.ThrowsAsync<BridgeException>(() => adapter.OpenConversationAsync(Thread));

        Assert.Equal("AMBIGUOUS_THREAD", error.ProtocolCode);
    }

    [Fact]
    public async Task OpenConversation_ReportsCodexNotRunning()
    {
        var adapter = new CodexDesktopAdapter(new FakeUiDriver { Running = false });

        var error = await Assert.ThrowsAsync<BridgeException>(() => adapter.OpenConversationAsync(Thread));

        Assert.Equal("CODEX_NOT_RUNNING", error.ProtocolCode);
    }

    [Fact]
    public async Task OpenConversation_ReportsConversationNotFound()
    {
        var adapter = new CodexDesktopAdapter(new FakeUiDriver { OpenResult = ConversationOpenResult.NotFound });

        var error = await Assert.ThrowsAsync<BridgeException>(() => adapter.OpenConversationAsync(Thread));

        Assert.Equal("THREAD_NOT_FOUND", error.ProtocolCode);
    }

    [Fact]
    public async Task SendMessage_UsesSemanticComposerAndSendActions()
    {
        var driver = new FakeUiDriver();
        var adapter = new CodexDesktopAdapter(driver);

        await adapter.SendMessageAsync("hello");

        Assert.Equal("hello", driver.Text);
        Assert.True(driver.SendInvoked);
    }

    [Fact]
    public async Task SendMessage_AttachesFilesBeforeSending()
    {
        var driver = new FakeUiDriver();
        var adapter = new CodexDesktopAdapter(driver);

        await adapter.SendMessageAsync("look", ["C:\\temp\\image.png"]);

        Assert.Equal(["C:\\temp\\image.png"], driver.Attachments);
        Assert.Equal(["attach", "text", "send"], driver.Actions);
    }

    [Fact]
    public async Task Stop_OpensTargetBeforeInvokingSemanticStop()
    {
        var driver = new FakeUiDriver { State = "working" };
        var adapter = new CodexDesktopAdapter(driver);

        await adapter.StopAsync(Thread);

        Assert.Equal(["open", "stop"], driver.Actions);
    }

    [Fact]
    public async Task Stop_RejectsIdleConversation()
    {
        var adapter = new CodexDesktopAdapter(new FakeUiDriver { State = "idle" });

        var error = await Assert.ThrowsAsync<BridgeException>(() => adapter.StopAsync(Thread));

        Assert.Equal("CODEX_NOT_WORKING", error.ProtocolCode);
    }

    private sealed class FakeUiDriver : ICodexUiDriver
    {
        public bool Running { get; init; } = true;
        public ConversationOpenResult OpenResult { get; init; } = ConversationOpenResult.Opened;
        public ConversationOpenResult CreateResult { get; init; } = ConversationOpenResult.Opened;
        public string? Text { get; private set; }
        public string State { get; init; } = "idle";
        public IReadOnlyList<string> Attachments { get; private set; } = [];
        public List<string> Actions { get; } = [];
        public bool SendInvoked { get; private set; }
        public bool IsCodexRunning() => Running;
        public DesktopConversation? GetCurrentConversation() => null;
        public ConversationOpenResult OpenConversation(string title, string cwd) { Actions.Add("open"); return OpenResult; }
        public ConversationOpenResult CreateConversation(string cwd) { Actions.Add("create"); return CreateResult; }
        public void AttachFiles(IReadOnlyList<string> paths) { Attachments = paths; Actions.Add("attach"); }
        public void SetComposerText(string text) { Text = text; Actions.Add("text"); }
        public void InvokeSend() { SendInvoked = true; Actions.Add("send"); }
        public void InvokeStop() => Actions.Add("stop");
        public string GetState() => State;
    }
}
