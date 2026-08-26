using CodexCompanion.Bridge.Codex.Models;

namespace CodexCompanion.Bridge.Relay;

public sealed record MessageAttachmentPayload(
    string Name,
    string MimeType,
    long Size,
    string DataBase64);

public sealed class AttachmentStager
{
    public const int MaxFiles = 4;
    public const long MaxFileBytes = 8L * 1024 * 1024;
    public const long MaxTotalBytes = 12L * 1024 * 1024;
    public static readonly TimeSpan Retention = TimeSpan.FromDays(7);

    private readonly string _root;

    public AttachmentStager(string? root = null)
    {
        _root = root ?? Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "CodexCompanion",
            "Uploads");
        PruneExpired();
    }

    public StagedAttachmentBatch Stage(string requestId, IReadOnlyList<MessageAttachmentPayload>? attachments)
    {
        if (attachments is null || attachments.Count == 0)
        {
            return StagedAttachmentBatch.Empty;
        }
        if (attachments.Count > MaxFiles)
        {
            throw new BridgeException(BridgeErrorCode.CodexAttachmentFailed, $"每次最多上传 {MaxFiles} 个附件。");
        }

        var safeRequestId = string.Concat(requestId.Where(char.IsLetterOrDigit));
        if (safeRequestId.Length < 8)
        {
            safeRequestId = Guid.NewGuid().ToString("N");
        }
        var directory = Path.Combine(_root, safeRequestId);
        Directory.CreateDirectory(directory);

        var staged = new List<StagedAttachment>();
        long total = 0;
        try
        {
            foreach (var attachment in attachments)
            {
                var name = Path.GetFileName(attachment.Name?.Trim());
                if (string.IsNullOrWhiteSpace(name)
                    || name.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0)
                {
                    throw new BridgeException(BridgeErrorCode.CodexAttachmentFailed, "附件文件名无效。");
                }
                if (attachment.Size < 0 || attachment.Size > MaxFileBytes)
                {
                    throw new BridgeException(BridgeErrorCode.CodexAttachmentFailed, $"附件“{name}”超过 8 MiB 限制。");
                }

                byte[] bytes;
                try
                {
                    bytes = Convert.FromBase64String(attachment.DataBase64 ?? string.Empty);
                }
                catch (FormatException exception)
                {
                    throw new BridgeException(BridgeErrorCode.CodexAttachmentFailed, $"附件“{name}”内容无效。", exception);
                }
                if (bytes.LongLength != attachment.Size)
                {
                    throw new BridgeException(BridgeErrorCode.CodexAttachmentFailed, $"附件“{name}”大小校验失败。");
                }
                total += bytes.LongLength;
                if (total > MaxTotalBytes)
                {
                    throw new BridgeException(BridgeErrorCode.CodexAttachmentFailed, "附件总大小超过 12 MiB 限制。");
                }

                var uniqueName = MakeUniqueName(directory, name);
                var path = Path.Combine(directory, uniqueName);
                File.WriteAllBytes(path, bytes);
                staged.Add(new StagedAttachment(uniqueName, path, attachment.MimeType ?? string.Empty, bytes.LongLength));
            }
            return new StagedAttachmentBatch(directory, staged);
        }
        catch
        {
            TryDeleteDirectory(directory);
            throw;
        }
    }

    private static string MakeUniqueName(string directory, string name)
    {
        var stem = Path.GetFileNameWithoutExtension(name);
        var extension = Path.GetExtension(name);
        var candidate = name;
        for (var index = 2; File.Exists(Path.Combine(directory, candidate)); index++)
        {
            candidate = $"{stem}-{index}{extension}";
        }
        return candidate;
    }

    internal static void TryDeleteDirectory(string directory)
    {
        try
        {
            if (Directory.Exists(directory))
            {
                Directory.Delete(directory, true);
            }
        }
        catch (IOException)
        {
        }
        catch (UnauthorizedAccessException)
        {
        }
    }

    private void PruneExpired()
    {
        if (!Directory.Exists(_root))
        {
            return;
        }
        var cutoff = DateTime.UtcNow - Retention;
        foreach (var directory in Directory.EnumerateDirectories(_root))
        {
            try
            {
                if (Directory.GetLastWriteTimeUtc(directory) < cutoff)
                {
                    TryDeleteDirectory(directory);
                }
            }
            catch (IOException)
            {
            }
            catch (UnauthorizedAccessException)
            {
            }
        }
    }
}

public sealed class StagedAttachmentBatch : IDisposable
{
    private bool _retained;
    public static StagedAttachmentBatch Empty { get; } = new(null, []);

    internal StagedAttachmentBatch(string? directory, IReadOnlyList<StagedAttachment> attachments)
    {
        Directory = directory;
        Attachments = attachments;
    }

    public string? Directory { get; }
    public IReadOnlyList<StagedAttachment> Attachments { get; }

    public void RetainForHistory()
    {
        _retained = true;
    }

    public void Dispose()
    {
        if (Directory is not null && !_retained)
        {
            AttachmentStager.TryDeleteDirectory(Directory);
        }
    }
}
