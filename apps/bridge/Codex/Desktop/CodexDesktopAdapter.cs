using CodexCompanion.Bridge.Codex.Models;

namespace CodexCompanion.Bridge.Codex.Desktop;

public sealed class CodexDesktopAdapter(ICodexUiDriver uiDriver) : ICodexDesktopAdapter
{
    public bool IsCodexRunning() => uiDriver.IsCodexRunning();

    public DesktopConversation? GetCurrentConversation() => uiDriver.GetCurrentConversation();

    public Task CreateConversationAsync(string cwd, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (!uiDriver.IsCodexRunning())
        {
            throw new BridgeException(BridgeErrorCode.CodexNotRunning, "电脑上的 Codex 当前未运行。");
        }

        switch (uiDriver.CreateConversation(cwd))
        {
            case ConversationOpenResult.Opened:
                return Task.CompletedTask;
            case ConversationOpenResult.NotFound:
                throw new BridgeException(
                    BridgeErrorCode.ThreadCreateFailed,
                    "无法在 Codex Desktop 中找到该项目的新建会话按钮。");
            case ConversationOpenResult.Ambiguous:
                throw new BridgeException(
                    BridgeErrorCode.ThreadCreateFailed,
                    "无法唯一定位该项目的新建会话按钮。");
            default:
                throw new BridgeException(BridgeErrorCode.ThreadCreateFailed, "无法在 Codex Desktop 中新建会话。");
        }
    }

    public Task OpenConversationAsync(CodexThreadSummary thread, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (!uiDriver.IsCodexRunning())
        {
            throw new BridgeException(BridgeErrorCode.CodexNotRunning, "电脑上的 Codex 当前未运行。");
        }

        switch (uiDriver.OpenConversation(thread.Title, thread.Cwd))
        {
            case ConversationOpenResult.Opened:
                return Task.CompletedTask;
            case ConversationOpenResult.NotFound:
                throw new BridgeException(
                    BridgeErrorCode.ThreadNotFound,
                    "无法在 Codex Desktop 侧栏中找到该会话。");
            case ConversationOpenResult.Ambiguous:
                throw new BridgeException(
                    BridgeErrorCode.AmbiguousThread,
                    "无法唯一定位该 Codex 会话，请在电脑端先打开一次该会话。");
            default:
                throw new BridgeException(BridgeErrorCode.CodexSendFailed, "无法打开 Codex 会话。");
        }
    }

    public Task SendMessageAsync(
        string text,
        IReadOnlyList<string>? attachmentPaths = null,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (string.IsNullOrWhiteSpace(text) && (attachmentPaths is null || attachmentPaths.Count == 0))
        {
            throw new ArgumentException("消息和附件不能同时为空。", nameof(text));
        }

        if (attachmentPaths is { Count: > 0 })
        {
            uiDriver.AttachFiles(attachmentPaths);
        }
        if (!string.IsNullOrWhiteSpace(text))
        {
            uiDriver.SetComposerText(text);
        }
        uiDriver.InvokeSend();
        return Task.CompletedTask;
    }

    public async Task StopAsync(CodexThreadSummary thread, CancellationToken cancellationToken = default)
    {
        await OpenConversationAsync(thread, cancellationToken);
        if (!string.Equals(uiDriver.GetState(), "working", StringComparison.Ordinal))
        {
            throw new BridgeException(BridgeErrorCode.CodexNotWorking, "该 Codex 会话当前没有正在执行的任务。");
        }
        uiDriver.InvokeStop();
    }

    public DesktopStatus GetDesktopStatus()
    {
        var running = uiDriver.IsCodexRunning();
        return new DesktopStatus(running, running ? uiDriver.GetState() : "offline", running ? uiDriver.GetCurrentConversation() : null);
    }
}
