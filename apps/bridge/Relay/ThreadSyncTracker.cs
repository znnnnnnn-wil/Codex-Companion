using CodexCompanion.Bridge.Codex.Models;

namespace CodexCompanion.Bridge.Relay;

public sealed class ThreadSyncTracker
{
    private readonly object _gate = new();
    private string? _threadId;
    private string? _status;
    private readonly Dictionary<string, string> _fingerprints = new(StringComparer.Ordinal);

    public string? ActiveThreadId
    {
        get { lock (_gate) return _threadId; }
    }

    public void Seed(CodexThreadHistory history)
    {
        lock (_gate)
        {
            _threadId = history.ThreadId;
            _status = history.Status;
            _fingerprints.Clear();
            foreach (var item in history.Items)
            {
                _fingerprints[item.Id] = Fingerprint(item);
            }
        }
    }

    public ThreadDelta Diff(CodexThreadHistory history)
    {
        lock (_gate)
        {
            if (_threadId != history.ThreadId)
            {
                Seed(history);
                return new ThreadDelta(history.ThreadId, [], history.Status, history.UpdatedAt, false);
            }

            var changed = new List<CodexThreadItem>();
            foreach (var item in history.Items)
            {
                var fingerprint = Fingerprint(item);
                if (!_fingerprints.TryGetValue(item.Id, out var previous) || previous != fingerprint)
                {
                    changed.Add(item);
                    _fingerprints[item.Id] = fingerprint;
                }
            }
            var statusChanged = !string.Equals(_status, history.Status, StringComparison.Ordinal);
            _status = history.Status;
            return new ThreadDelta(history.ThreadId, changed, history.Status, history.UpdatedAt, statusChanged);
        }
    }

    private static string Fingerprint(CodexThreadItem item)
        => string.Join('\u001f', item.Type, item.RawType, item.Role, item.Content, item.Status, item.TurnId);
}

public sealed record ThreadDelta(
    string ThreadId,
    IReadOnlyList<CodexThreadItem> Items,
    string Status,
    long UpdatedAt,
    bool StatusChanged)
{
    public bool HasChanges => Items.Count > 0 || StatusChanged;
}
