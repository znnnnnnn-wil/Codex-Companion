using CodexCompanion.Bridge.Codex.Models;

namespace CodexCompanion.Bridge.Codex.History;

public interface ICodexHistoryAdapter
{
    Task<IReadOnlyList<CodexThreadSummary>> ListThreadsAsync(CancellationToken cancellationToken = default);
    Task<CodexThreadSummary> CreateThreadAsync(
        string cwd,
        CancellationToken cancellationToken = default)
        => Task.FromException<CodexThreadSummary>(new NotSupportedException("当前历史适配器不支持新建会话。"));
    Task<CodexThreadHistory> ReadThreadAsync(string threadId, CancellationToken cancellationToken = default);
    Task<CodexMediaContent?> ReadMediaAsync(
        string threadId,
        string itemId,
        CancellationToken cancellationToken = default)
        => Task.FromResult<CodexMediaContent?>(null);
}
