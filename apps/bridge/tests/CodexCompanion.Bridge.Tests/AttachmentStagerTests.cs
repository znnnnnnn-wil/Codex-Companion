using CodexCompanion.Bridge.Codex.Models;
using CodexCompanion.Bridge.Relay;

namespace CodexCompanion.Bridge.Tests;

public sealed class AttachmentStagerTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "CodexCompanionTests", Guid.NewGuid().ToString("N"));

    [Fact]
    public void Stage_DecodesIntoScopedTemporaryDirectoryAndCleansUp()
    {
        var stager = new AttachmentStager(_root);
        var bytes = "hello"u8.ToArray();

        string directory;
        using (var batch = stager.Stage("request-1234", [
            new MessageAttachmentPayload("note.txt", "text/plain", bytes.Length, Convert.ToBase64String(bytes))
        ]))
        {
            directory = batch.Directory!;
            Assert.Single(batch.Attachments);
            Assert.Equal(bytes, File.ReadAllBytes(batch.Attachments[0].Path));
            Assert.StartsWith(Path.GetFullPath(_root), Path.GetFullPath(batch.Attachments[0].Path));
        }

        Assert.False(Directory.Exists(directory));
    }

    [Fact]
    public void Stage_RejectsTraversalAndDeclaredSizeMismatch()
    {
        var stager = new AttachmentStager(_root);
        var error = Assert.Throws<BridgeException>(() => stager.Stage("request-1234", [
            new MessageAttachmentPayload("../note.txt", "text/plain", 99, Convert.ToBase64String("x"u8.ToArray()))
        ]));

        Assert.Equal("CODEX_ATTACHMENT_FAILED", error.ProtocolCode);
        Assert.False(Directory.Exists(Path.Combine(_root, "request1234")));
    }

    [Fact]
    public void Stage_RetainsConfirmedAttachmentsForHistory()
    {
        var stager = new AttachmentStager(_root);
        var bytes = "image"u8.ToArray();

        string directory;
        using (var batch = stager.Stage("confirmed-1234", [
            new MessageAttachmentPayload("photo.jpg", "image/jpeg", bytes.Length, Convert.ToBase64String(bytes))
        ]))
        {
            directory = batch.Directory!;
            batch.RetainForHistory();
        }

        Assert.True(Directory.Exists(directory));
    }

    public void Dispose()
    {
        if (Directory.Exists(_root))
        {
            Directory.Delete(_root, true);
        }
    }
}
