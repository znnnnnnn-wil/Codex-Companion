using CodexCompanion.Bridge.Codex.History;
using CodexCompanion.Bridge.Codex.Models;

namespace CodexCompanion.Bridge.Codex.Desktop;

public sealed class MessageSendCoordinator(
    ICodexHistoryAdapter history,
    ICodexDesktopAdapter desktop,
    TimeSpan? confirmationTimeout = null,
    TimeSpan? pollInterval = null)
{
    private readonly TimeSpan _confirmationTimeout = confirmationTimeout ?? TimeSpan.FromSeconds(20);
    private readonly TimeSpan _pollInterval = pollInterval ?? TimeSpan.FromMilliseconds(500);

    public async Task<MessageSendResult> SendAndConfirmAsync(
        string threadId,
        string text,
        IReadOnlyList<string>? attachmentPaths = null,
        CancellationToken cancellationToken = default)
    {
        var threads = await history.ListThreadsAsync(cancellationToken);
        var thread = threads.FirstOrDefault(candidate => candidate.ThreadId == threadId)
            ?? throw new BridgeException(BridgeErrorCode.ThreadNotFound, "找不到指定的 Codex thread。");
        var before = await history.ReadThreadAsync(threadId, cancellationToken);
        var existingMessageIds = before.Items
            .Where(item => item.Type == "message" && item.Role == "user")
            .Select(item => item.Id)
            .ToHashSet(StringComparer.Ordinal);

        await desktop.OpenConversationAsync(thread, cancellationToken);
        await desktop.SendMessageAsync(text, attachmentPaths, cancellationToken);

        var deadline = DateTimeOffset.UtcNow.Add(_confirmationTimeout);
        while (DateTimeOffset.UtcNow < deadline)
        {
            await Task.Delay(_pollInterval, cancellationToken);
            var updated = await history.ReadThreadAsync(threadId, cancellationToken);
            var confirmed = updated.Items.LastOrDefault(item =>
                item.Type == "message"
                && item.Role == "user"
                && !existingMessageIds.Contains(item.Id)
                && MatchesForConfirmation(item.Content, text, attachmentPaths is { Count: > 0 }));
            if (confirmed is not null)
            {
                return new MessageSendResult(threadId, confirmed.Id, true);
            }
        }

        throw new BridgeException(
            BridgeErrorCode.ThreadConfirmTimeout,
            "消息已提交给 Codex Desktop，但未能从真实 thread 历史中确认。");
    }

    private static string NormalizeForConfirmation(string? value)
    {
        var text = (value ?? string.Empty).Replace("\r\n", "\n").TrimEnd('\r', '\n');
        const string markdownPunctuation = "!\"#$%&'()*+,-./:;<=>?@[\\]^_`{|}~";
        var result = new System.Text.StringBuilder(text.Length);
        for (var index = 0; index < text.Length; index++)
        {
            if (text[index] == '\\'
                && index + 1 < text.Length
                && markdownPunctuation.Contains(text[index + 1]))
            {
                index++;
            }
            result.Append(text[index]);
        }
        return result.ToString();
    }

    private static bool MatchesForConfirmation(string? candidate, string expected, bool hasAttachments)
    {
        if (string.IsNullOrWhiteSpace(expected))
        {
            return true;
        }
        var normalizedCandidate = NormalizeForConfirmation(candidate);
        var normalizedExpected = NormalizeForConfirmation(expected);
        return string.Equals(normalizedCandidate, normalizedExpected, StringComparison.Ordinal)
            || (hasAttachments && normalizedCandidate.EndsWith(normalizedExpected, StringComparison.Ordinal));
    }
}
