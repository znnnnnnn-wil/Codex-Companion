using CodexCompanion.Bridge.Codex.Models;

namespace CodexCompanion.Bridge.Codex.Desktop;

public interface ICodexDesktopAdapter
{
    bool IsCodexRunning();
    DesktopConversation? GetCurrentConversation();
    Task CreateConversationAsync(string cwd, CancellationToken cancellationToken = default);
    Task OpenConversationAsync(CodexThreadSummary thread, CancellationToken cancellationToken = default);
    Task SendMessageAsync(string text, IReadOnlyList<string>? attachmentPaths = null, CancellationToken cancellationToken = default);
    Task StopAsync(CodexThreadSummary thread, CancellationToken cancellationToken = default);
    DesktopStatus GetDesktopStatus();
}
