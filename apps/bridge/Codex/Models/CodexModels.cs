using System.Text.Json.Serialization;

namespace CodexCompanion.Bridge.Codex.Models;

public sealed record CodexThreadSummary(
    string ThreadId,
    string Title,
    string Cwd,
    long UpdatedAt,
    string Status,
    string Source);

public sealed record CodexThreadItem(
    string Id,
    string Type,
    string RawType,
    string? Role,
    string? Content,
    string? Status,
    string TurnId,
    IReadOnlyList<CodexThreadAttachment>? Attachments = null);

public sealed record CodexThreadAttachment(
    string Id,
    string Name,
    string MimeType,
    bool Available);

public sealed record CodexMediaContent(
    string ItemId,
    string MimeType,
    string DataBase64);

public sealed record CodexThreadHistory(
    string ThreadId,
    string Title,
    string Cwd,
    long UpdatedAt,
    string Status,
    IReadOnlyList<CodexThreadItem> Items);

public sealed record DesktopConversation(string Title, string Workspace, bool IsSelected);

public sealed record DesktopStatus(bool CodexRunning, string State, DesktopConversation? CurrentConversation);

public sealed record MessageSendResult(string ThreadId, string MessageId, bool Confirmed);

public sealed record StagedAttachment(string Name, string Path, string MimeType, long Size);

[JsonConverter(typeof(JsonStringEnumConverter<BridgeErrorCode>))]
public enum BridgeErrorCode
{
    DeviceOffline,
    CodexNotRunning,
    CodexAppServerUnavailable,
    ThreadCreateFailed,
    ThreadNotFound,
    AmbiguousThread,
    CodexInputNotFound,
    CodexAttachmentFailed,
    CodexSendFailed,
    CodexNotWorking,
    CodexStopFailed,
    ThreadConfirmTimeout,
    Unauthorized
}

public sealed class BridgeException : Exception
{
    public BridgeException(BridgeErrorCode code, string message, Exception? innerException = null)
        : base(message, innerException)
    {
        Code = code;
    }

    public BridgeErrorCode Code { get; }

    public string ProtocolCode => Code switch
    {
        BridgeErrorCode.DeviceOffline => "DEVICE_OFFLINE",
        BridgeErrorCode.CodexNotRunning => "CODEX_NOT_RUNNING",
        BridgeErrorCode.CodexAppServerUnavailable => "CODEX_APP_SERVER_UNAVAILABLE",
        BridgeErrorCode.ThreadCreateFailed => "THREAD_CREATE_FAILED",
        BridgeErrorCode.ThreadNotFound => "THREAD_NOT_FOUND",
        BridgeErrorCode.AmbiguousThread => "AMBIGUOUS_THREAD",
        BridgeErrorCode.CodexInputNotFound => "CODEX_INPUT_NOT_FOUND",
        BridgeErrorCode.CodexAttachmentFailed => "CODEX_ATTACHMENT_FAILED",
        BridgeErrorCode.CodexSendFailed => "CODEX_SEND_FAILED",
        BridgeErrorCode.CodexNotWorking => "CODEX_NOT_WORKING",
        BridgeErrorCode.CodexStopFailed => "CODEX_STOP_FAILED",
        BridgeErrorCode.ThreadConfirmTimeout => "THREAD_CONFIRM_TIMEOUT",
        BridgeErrorCode.Unauthorized => "UNAUTHORIZED",
        _ => "CODEX_SEND_FAILED"
    };
}
