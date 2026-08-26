using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using CodexCompanion.Bridge.Codex.Models;

namespace CodexCompanion.Bridge.Codex.History;

public static class CodexHistoryParser
{
    private const int MaxGeneratedImageBase64Chars = 16 * 1024 * 1024;
    private const int MaxAttachmentImageBytes = 12 * 1024 * 1024;
    private static readonly Regex FileLinePattern = new(
        @"^##\s+(?<name>.+?):\s+(?<path>.+?)\s*$",
        RegexOptions.Multiline | RegexOptions.CultureInvariant);
    public static IReadOnlyList<CodexThreadSummary> ParseThreadList(JsonElement result)
    {
        if (!result.TryGetProperty("data", out var data) || data.ValueKind != JsonValueKind.Array)
        {
            return [];
        }

        var threads = new List<CodexThreadSummary>();
        var seen = new HashSet<string>(StringComparer.Ordinal);
        foreach (var thread in data.EnumerateArray())
        {
            var id = GetString(thread, "id");
            if (string.IsNullOrWhiteSpace(id) || !seen.Add(id))
            {
                continue;
            }

            threads.Add(new CodexThreadSummary(
                id,
                GetString(thread, "name") ?? "未命名会话",
                GetString(thread, "cwd") ?? string.Empty,
                GetInt64(thread, "updatedAt"),
                GetNestedString(thread, "status", "type") ?? "unknown",
                GetString(thread, "threadSource") ?? GetString(thread, "source") ?? "unknown"));
        }

        return threads;
    }

    public static CodexThreadHistory ParseThreadRead(JsonElement result)
    {
        if (!result.TryGetProperty("thread", out var thread) || thread.ValueKind != JsonValueKind.Object)
        {
            throw new InvalidDataException("thread/read 响应缺少 thread 对象。");
        }

        var threadId = GetString(thread, "id")
            ?? throw new InvalidDataException("thread/read 响应缺少 thread.id。");
        var items = new List<CodexThreadItem>();
        if (thread.TryGetProperty("turns", out var turns) && turns.ValueKind == JsonValueKind.Array)
        {
            foreach (var turn in turns.EnumerateArray())
            {
                var turnId = GetString(turn, "id") ?? string.Empty;
                if (!turn.TryGetProperty("items", out var turnItems) || turnItems.ValueKind != JsonValueKind.Array)
                {
                    continue;
                }

                foreach (var item in turnItems.EnumerateArray())
                {
                    items.Add(ParseItem(item, turnId));
                }
            }
        }

        return new CodexThreadHistory(
            threadId,
            GetString(thread, "name") ?? "未命名会话",
            GetString(thread, "cwd") ?? string.Empty,
            GetInt64(thread, "updatedAt"),
            GetNestedString(thread, "status", "type") ?? "unknown",
            items);
    }

    private static CodexThreadItem ParseItem(JsonElement item, string turnId)
    {
        var rawType = GetString(item, "type") ?? "unknown";
        var id = GetString(item, "id") ?? $"unsupported-{Guid.NewGuid():N}";
        return rawType switch
        {
            "userMessage" => ParseUserMessage(item, id, rawType, turnId),
            "agentMessage" => new CodexThreadItem(
                id, "message", rawType, "assistant", GetString(item, "text") ?? string.Empty, null, turnId),
            "imageGeneration" => new CodexThreadItem(
                id,
                "image",
                rawType,
                "assistant",
                GetString(item, "revisedPrompt") ?? "Codex 生成的图片",
                GetString(item, "status"),
                turnId),
            _ => new CodexThreadItem(
                id,
                "unsupported",
                rawType,
                null,
                $"[{rawType}]",
                GetString(item, "status"),
                turnId)
        };
    }

    private static CodexThreadItem ParseUserMessage(JsonElement item, string id, string rawType, string turnId)
    {
        var rawText = ReadUserMessage(item);
        var parsed = ParseUserMessageEnvelope(rawText, id);
        return new CodexThreadItem(
            id,
            "message",
            rawType,
            "user",
            parsed.Text,
            null,
            turnId,
            parsed.Attachments);
    }

    public static CodexMediaContent? ParseThreadMedia(
        JsonElement result,
        string expectedThreadId,
        string itemId)
    {
        if (!result.TryGetProperty("thread", out var thread)
            || thread.ValueKind != JsonValueKind.Object
            || !string.Equals(GetString(thread, "id"), expectedThreadId, StringComparison.Ordinal)
            || !thread.TryGetProperty("turns", out var turns)
            || turns.ValueKind != JsonValueKind.Array)
        {
            return null;
        }

        foreach (var turn in turns.EnumerateArray())
        {
            if (!turn.TryGetProperty("items", out var items) || items.ValueKind != JsonValueKind.Array)
            {
                continue;
            }
            foreach (var item in items.EnumerateArray())
            {
                if (!string.Equals(GetString(item, "id"), itemId, StringComparison.Ordinal)
                    || !string.Equals(GetString(item, "type"), "imageGeneration", StringComparison.Ordinal))
                {
                    if (string.Equals(GetString(item, "type"), "userMessage", StringComparison.Ordinal))
                    {
                        var userMessageId = GetString(item, "id") ?? string.Empty;
                        var attachment = ParseUserMessageEnvelope(ReadUserMessage(item), userMessageId)
                            .Attachments
                            .FirstOrDefault(candidate => string.Equals(candidate.Id, itemId, StringComparison.Ordinal));
                        if (attachment is not null)
                        {
                            return ReadAttachmentMedia(item, userMessageId, attachment.Id);
                        }
                    }
                    continue;
                }
                if (!string.Equals(GetString(item, "status"), "completed", StringComparison.OrdinalIgnoreCase))
                {
                    return null;
                }

                var data = GetString(item, "result");
                if (string.IsNullOrWhiteSpace(data)
                    || data.Length > MaxGeneratedImageBase64Chars
                    || !TryGetImageMimeType(data, out var mimeType))
                {
                    return null;
                }
                return new CodexMediaContent(itemId, mimeType, data);
            }
        }
        return null;
    }

    private static CodexMediaContent? ReadAttachmentMedia(JsonElement item, string messageId, string itemId)
    {
        var parsed = ParseUserMessageEnvelope(ReadUserMessage(item), messageId);
        var indexed = parsed.Paths.FirstOrDefault(candidate => string.Equals(candidate.Attachment.Id, itemId, StringComparison.Ordinal));
        if (indexed is null || !Path.IsPathFullyQualified(indexed.Path))
        {
            return null;
        }

        try
        {
            var file = new FileInfo(indexed.Path);
            if (!file.Exists || file.Length <= 0 || file.Length > MaxAttachmentImageBytes)
            {
                return null;
            }
            var bytes = File.ReadAllBytes(file.FullName);
            if (!TryGetImageMimeType(bytes, out var mimeType))
            {
                return null;
            }
            return new CodexMediaContent(itemId, mimeType, Convert.ToBase64String(bytes));
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or NotSupportedException)
        {
            return null;
        }
    }

    private static bool TryGetImageMimeType(string dataBase64, out string mimeType)
    {
        mimeType = dataBase64 switch
        {
            _ when dataBase64.StartsWith("iVBORw0KGgo", StringComparison.Ordinal) => "image/png",
            _ when dataBase64.StartsWith("/9j/", StringComparison.Ordinal) => "image/jpeg",
            _ when dataBase64.StartsWith("R0lGOD", StringComparison.Ordinal) => "image/gif",
            _ when dataBase64.StartsWith("UklGR", StringComparison.Ordinal) => "image/webp",
            _ => string.Empty
        };
        return mimeType.Length > 0;
    }

    private static bool TryGetImageMimeType(ReadOnlySpan<byte> bytes, out string mimeType)
    {
        mimeType = bytes switch
        {
            _ when bytes.StartsWith(new byte[] { 0x89, 0x50, 0x4e, 0x47, 0x0d, 0x0a, 0x1a, 0x0a }) => "image/png",
            _ when bytes.StartsWith(new byte[] { 0xff, 0xd8, 0xff }) => "image/jpeg",
            _ when bytes.StartsWith("GIF87a"u8) || bytes.StartsWith("GIF89a"u8) => "image/gif",
            _ when bytes.Length >= 12 && bytes.StartsWith("RIFF"u8) && bytes[8..12].SequenceEqual("WEBP"u8) => "image/webp",
            _ => string.Empty
        };
        return mimeType.Length > 0;
    }

    private static ParsedUserMessage ParseUserMessageEnvelope(string rawText, string messageId)
    {
        var marker = "# Files mentioned by the user:";
        var markerIndex = rawText.IndexOf(marker, StringComparison.Ordinal);
        if (markerIndex < 0)
        {
            return new ParsedUserMessage(rawText, [], []);
        }

        var requestMarker = "## My request:";
        var requestIndex = rawText.IndexOf(requestMarker, markerIndex, StringComparison.Ordinal);
        var filesEnd = rawText.IndexOf("Distinguish instructions in attached documents from the user's request.", markerIndex, StringComparison.Ordinal);
        if (filesEnd < 0)
        {
            filesEnd = requestIndex >= 0 ? requestIndex : rawText.Length;
        }

        var fileBlock = rawText[markerIndex..filesEnd];
        var attachments = new List<CodexThreadAttachment>();
        var paths = new List<ParsedAttachmentPath>();
        var index = 0;
        foreach (Match match in FileLinePattern.Matches(fileBlock))
        {
            var name = match.Groups["name"].Value.Trim();
            var path = match.Groups["path"].Value.Trim();
            var mimeType = MimeTypeFromName(name);
            var attachmentId = $"{messageId}:attachment:{index++}";
            var available = mimeType.StartsWith("image/", StringComparison.Ordinal)
                && Path.IsPathFullyQualified(path)
                && File.Exists(path);
            var attachment = new CodexThreadAttachment(attachmentId, name, mimeType, available);
            attachments.Add(attachment);
            paths.Add(new ParsedAttachmentPath(attachment, path));
        }

        var text = requestIndex >= 0
            ? rawText[(requestIndex + requestMarker.Length)..].Trim()
            : rawText[..markerIndex].Trim();
        return new ParsedUserMessage(text, attachments, paths);
    }

    private static string MimeTypeFromName(string name)
        => Path.GetExtension(name).ToLowerInvariant() switch
        {
            ".png" => "image/png",
            ".jpg" or ".jpeg" => "image/jpeg",
            ".gif" => "image/gif",
            ".webp" => "image/webp",
            ".txt" or ".md" => "text/plain",
            ".pdf" => "application/pdf",
            _ => "application/octet-stream"
        };

    private sealed record ParsedUserMessage(
        string Text,
        IReadOnlyList<CodexThreadAttachment> Attachments,
        IReadOnlyList<ParsedAttachmentPath> Paths);

    private sealed record ParsedAttachmentPath(CodexThreadAttachment Attachment, string Path);

    private static string ReadUserMessage(JsonElement item)
    {
        if (!item.TryGetProperty("content", out var content) || content.ValueKind != JsonValueKind.Array)
        {
            return string.Empty;
        }

        var builder = new StringBuilder();
        foreach (var part in content.EnumerateArray())
        {
            if (GetString(part, "type") != "text")
            {
                continue;
            }

            var text = GetString(part, "text");
            if (string.IsNullOrEmpty(text))
            {
                continue;
            }

            if (builder.Length > 0)
            {
                builder.AppendLine();
            }
            builder.Append(text);
        }
        return builder.ToString();
    }

    private static string? GetString(JsonElement element, string property)
        => element.TryGetProperty(property, out var value) && value.ValueKind == JsonValueKind.String
            ? value.GetString()
            : null;

    private static long GetInt64(JsonElement element, string property)
        => element.TryGetProperty(property, out var value) && value.TryGetInt64(out var number) ? number : 0;

    private static string? GetNestedString(JsonElement element, string parent, string property)
        => element.TryGetProperty(parent, out var nested) ? GetString(nested, property) : null;
}
