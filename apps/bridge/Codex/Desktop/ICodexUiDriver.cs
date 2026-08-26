using CodexCompanion.Bridge.Codex.Models;

namespace CodexCompanion.Bridge.Codex.Desktop;

public enum ConversationOpenResult
{
    Opened,
    NotFound,
    Ambiguous
}

public interface ICodexUiDriver
{
    bool IsCodexRunning();
    DesktopConversation? GetCurrentConversation();
    ConversationOpenResult CreateConversation(string cwd);
    ConversationOpenResult OpenConversation(string title, string cwd);
    void AttachFiles(IReadOnlyList<string> paths);
    void SetComposerText(string text);
    void InvokeSend();
    void InvokeStop();
    string GetState();
}
