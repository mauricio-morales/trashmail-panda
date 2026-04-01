using System.Collections.Generic;
using Google.Apis.Gmail.v1.Data;
using Google.Apis.Util.Store;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using TrashMailPanda.Providers.Email;
using TrashMailPanda.Providers.Email.Services;
using TrashMailPanda.Providers.Storage;
using TrashMailPanda.Shared.Base;
using TrashMailPanda.Shared.Security;
using Xunit;

namespace TrashMailPanda.Tests.Unit.Email;

/// <summary>
/// Validates that BuildFeatureVector correctly populates the 9 attachment ML features
/// from a Gmail Message's MIME parts tree.
/// </summary>
[Trait("Category", "Unit")]
public sealed class GmailTrainingDataServiceAttachmentTests
{
    private readonly GmailTrainingDataService _sut;

    public GmailTrainingDataServiceAttachmentTests()
    {
        var emailProvider = new GmailEmailProvider(
            Mock.Of<ISecureStorageManager>(),
            Mock.Of<IGmailRateLimitHandler>(),
            Mock.Of<IDataStore>(),
            Mock.Of<ISecurityAuditLogger>(),
            NullLogger<GmailEmailProvider>.Instance);

        _sut = new GmailTrainingDataService(
            emailProvider,
            Mock.Of<ITrainingSignalAssigner>(),
            Mock.Of<ITrainingEmailRepository>(),
            Mock.Of<ILabelTaxonomyRepository>(),
            Mock.Of<ILabelAssociationRepository>(),
            Mock.Of<IScanProgressRepository>(),
            Mock.Of<IEmailArchiveService>(),
            Mock.Of<IGmailRateLimitHandler>(),
            NullLogger<GmailTrainingDataService>.Instance);
    }

    // ── Helper: minimal valid Message with a payload ───────────────────────

    private static Message BuildMessage(MessagePart payload, string folder = "INBOX")
    {
        var labelIds = new List<string> { "INBOX" };
        if (folder == "SPAM") { labelIds.Clear(); labelIds.Add("SPAM"); }
        else if (folder == "SENT") { labelIds.Clear(); labelIds.Add("SENT"); }

        return new Message
        {
            Id = "msg_001",
            LabelIds = labelIds,
            InternalDate = 1_700_000_000_000L,   // some valid epoch ms
            Payload = payload,
        };
    }

    private static MessagePart EmptyPayload() => new MessagePart
    {
        MimeType = "text/plain",
        Headers = [new MessagePartHeader { Name = "Subject", Value = "Hello" }],
        Parts = null,
    };

    // ── No attachments ─────────────────────────────────────────────────────

    [Fact]
    public void BuildFeatureVector_NoAttachments_AllAttachmentFieldsAreZero()
    {
        var msg = BuildMessage(EmptyPayload());

        var result = _sut.BuildFeatureVector(msg, "INBOX");

        Assert.NotNull(result);
        Assert.Equal(0, result!.HasAttachments);
        Assert.Equal(0, result.AttachmentCount);
        Assert.Equal(0f, result.TotalAttachmentSizeLog);
        Assert.Equal(0, result.HasDocAttachments);
        Assert.Equal(0, result.HasImageAttachments);
        Assert.Equal(0, result.HasAudioAttachments);
        Assert.Equal(0, result.HasVideoAttachments);
        Assert.Equal(0, result.HasXmlAttachments);
        Assert.Equal(0, result.HasBinaryAttachments);
        Assert.Equal(0, result.HasOtherAttachments);
    }

    // ── PDF attachment → document category ────────────────────────────────

    [Fact]
    public void BuildFeatureVector_PdfAttachment_SetsDocAttachmentsFlag()
    {
        var payload = new MessagePart
        {
            MimeType = "multipart/mixed",
            Headers = [new MessagePartHeader { Name = "Subject", Value = "Invoice" }],
            Parts =
            [
                new MessagePart { MimeType = "text/plain" },
                new MessagePart
                {
                    MimeType = "application/pdf",
                    Filename = "invoice.pdf",
                    Body = new MessagePartBody { Size = 50_000, AttachmentId = "attach_1" },
                },
            ],
        };

        var result = _sut.BuildFeatureVector(BuildMessage(payload), "INBOX");

        Assert.NotNull(result);
        Assert.Equal(1, result!.HasAttachments);
        Assert.Equal(1, result.AttachmentCount);
        Assert.Equal(1, result.HasDocAttachments);
        Assert.Equal(0, result.HasImageAttachments);
        Assert.Equal(0, result.HasBinaryAttachments);
    }

    // ── Image attachment → image category ─────────────────────────────────

    [Fact]
    public void BuildFeatureVector_ImageAttachment_SetsImageFlag()
    {
        var payload = new MessagePart
        {
            MimeType = "multipart/mixed",
            Headers = [new MessagePartHeader { Name = "Subject", Value = "Photo" }],
            Parts =
            [
                new MessagePart
                {
                    MimeType = "image/png",
                    Filename = "screenshot.png",
                    Body = new MessagePartBody { Size = 200_000, AttachmentId = "attach_img" },
                },
            ],
        };

        var result = _sut.BuildFeatureVector(BuildMessage(payload), "INBOX");

        Assert.NotNull(result);
        Assert.Equal(1, result!.HasAttachments);
        Assert.Equal(1, result.AttachmentCount);
        Assert.Equal(1, result.HasImageAttachments);
        Assert.Equal(0, result.HasDocAttachments);
    }

    // ── Mixed: PDF + image + .exe ──────────────────────────────────────────

    [Fact]
    public void BuildFeatureVector_MixedAttachments_SetsMultipleFlags()
    {
        var payload = new MessagePart
        {
            MimeType = "multipart/mixed",
            Headers = [new MessagePartHeader { Name = "Subject", Value = "Pack" }],
            Parts =
            [
                new MessagePart
                {
                    MimeType = "application/pdf",
                    Filename = "report.pdf",
                    Body = new MessagePartBody { Size = 10_000, AttachmentId = "a1" },
                },
                new MessagePart
                {
                    MimeType = "image/jpeg",
                    Filename = "photo.jpg",
                    Body = new MessagePartBody { Size = 80_000, AttachmentId = "a2" },
                },
                new MessagePart
                {
                    MimeType = "application/octet-stream",
                    Filename = "setup.exe",
                    Body = new MessagePartBody { Size = 1_000_000, AttachmentId = "a3" },
                },
            ],
        };

        var result = _sut.BuildFeatureVector(BuildMessage(payload), "INBOX");

        Assert.NotNull(result);
        Assert.Equal(1, result!.HasAttachments);
        Assert.Equal(3, result.AttachmentCount);
        Assert.Equal(1, result.HasDocAttachments);
        Assert.Equal(1, result.HasImageAttachments);
        Assert.Equal(1, result.HasBinaryAttachments);
        Assert.Equal(0, result.HasAudioAttachments);
        Assert.Equal(0, result.HasVideoAttachments);
        Assert.Equal(0, result.HasXmlAttachments);
        Assert.Equal(0, result.HasOtherAttachments);
    }

    // ── Inline image (no filename, no attachmentId) → not counted ─────────

    [Fact]
    public void BuildFeatureVector_InlinePartWithNoFilenameOrAttachmentId_NotCounted()
    {
        var payload = new MessagePart
        {
            MimeType = "multipart/related",
            Headers = [new MessagePartHeader { Name = "Subject", Value = "HTML mail" }],
            Parts =
            [
                new MessagePart
                {
                    MimeType = "text/html",
                    // No Filename and no AttachmentId → treated as inline body, not an attachment
                    Body = new MessagePartBody { Size = 5_000 },
                },
            ],
        };

        var result = _sut.BuildFeatureVector(BuildMessage(payload), "INBOX");

        Assert.NotNull(result);
        Assert.Equal(0, result!.HasAttachments);
        Assert.Equal(0, result.AttachmentCount);
    }

    // ── Nested multipart: attachment inside multipart/alternative ─────────

    [Fact]
    public void BuildFeatureVector_NestedMultipart_RecursivelyCollectsAttachments()
    {
        var payload = new MessagePart
        {
            MimeType = "multipart/mixed",
            Headers = [new MessagePartHeader { Name = "Subject", Value = "Nested" }],
            Parts =
            [
                new MessagePart
                {
                    MimeType = "multipart/alternative",
                    Parts =
                    [
                        new MessagePart { MimeType = "text/plain" },
                        new MessagePart { MimeType = "text/html" },
                    ],
                },
                new MessagePart
                {
                    MimeType = "audio/mpeg",
                    Filename = "song.mp3",
                    Body = new MessagePartBody { Size = 5_000_000, AttachmentId = "aud1" },
                },
            ],
        };

        var result = _sut.BuildFeatureVector(BuildMessage(payload), "INBOX");

        Assert.NotNull(result);
        Assert.Equal(1, result!.HasAttachments);
        Assert.Equal(1, result.AttachmentCount);
        Assert.Equal(1, result.HasAudioAttachments);
        Assert.Equal(0, result.HasDocAttachments);
    }

    // ── Null message ID → returns null ────────────────────────────────────

    [Fact]
    public void BuildFeatureVector_NullMessageId_ReturnsNull()
    {
        var msg = new Message { Id = null, LabelIds = ["INBOX"], Payload = EmptyPayload() };

        var result = _sut.BuildFeatureVector(msg, "INBOX");

        Assert.Null(result);
    }

    // ── FeatureSchemaVersion is current ───────────────────────────────────

    [Fact]
    public void BuildFeatureVector_Always_SetsCurrentSchemaVersion()
    {
        var msg = BuildMessage(EmptyPayload());

        var result = _sut.BuildFeatureVector(msg, "INBOX");

        Assert.NotNull(result);
        Assert.Equal(TrashMailPanda.Providers.Storage.Models.FeatureSchema.CurrentVersion, result!.FeatureSchemaVersion);
    }
}
