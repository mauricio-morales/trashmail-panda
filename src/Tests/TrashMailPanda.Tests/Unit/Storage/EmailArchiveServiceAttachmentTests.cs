using TrashMailPanda.Providers.Storage;
using TrashMailPanda.Providers.Storage.Models;
using TrashMailPanda.Shared.Base;
using Xunit;

namespace TrashMailPanda.Tests.Unit.Storage;

[Trait("Category", "Unit")]
public class EmailArchiveServiceAttachmentTests : StorageTestBase
{
    private readonly EmailArchiveService _service;

    public EmailArchiveServiceAttachmentTests() : base()
    {
        _service = new EmailArchiveService(_context);
    }

    public override void Dispose()
    {
        _service.Dispose();
        base.Dispose();
    }

    [Fact(Timeout = 5000)]
    public async Task HasOutdatedFeaturesAsync_EmptyTable_ReturnsFalse()
    {
        var result = await _service.HasOutdatedFeaturesAsync(2);

        Assert.True(result.IsSuccess);
        Assert.False(result.Value);
    }

    [Fact(Timeout = 5000)]
    public async Task HasOutdatedFeaturesAsync_RowWithOlderVersion_ReturnsTrue()
    {
        // Arrange — store a v1 feature row
        var feature = CreateFeatureVector("email-v1", schemaVersion: 1);
        await _service.StoreFeatureAsync(feature);

        // Act — check if any rows are below v2
        var result = await _service.HasOutdatedFeaturesAsync(2);

        Assert.True(result.IsSuccess);
        Assert.True(result.Value);
    }

    [Fact(Timeout = 5000)]
    public async Task HasOutdatedFeaturesAsync_AllRowsAtCurrentVersion_ReturnsFalse()
    {
        // Arrange — store a v2 feature row
        var feature = CreateFeatureVector("email-v2", schemaVersion: 2);
        await _service.StoreFeatureAsync(feature);

        // Act — check against v2
        var result = await _service.HasOutdatedFeaturesAsync(2);

        Assert.True(result.IsSuccess);
        Assert.False(result.Value);
    }

    [Fact(Timeout = 5000)]
    public async Task HasOutdatedFeaturesAsync_MixedVersions_ReturnsTrue()
    {
        // Arrange — one v1, one v2 row
        await _service.StoreFeatureAsync(CreateFeatureVector("email-old", schemaVersion: 1));
        await _service.StoreFeatureAsync(CreateFeatureVector("email-new", schemaVersion: 2));

        // Act
        var result = await _service.HasOutdatedFeaturesAsync(2);

        Assert.True(result.IsSuccess);
        Assert.True(result.Value);
    }

    // ============================================================
    // Helper
    // ============================================================

    private static EmailFeatureVector CreateFeatureVector(string emailId, int schemaVersion)
    {
        return new EmailFeatureVector
        {
            EmailId = emailId,
            SenderDomain = "example.com",
            SenderKnown = 0,
            ContactStrength = 0,
            SpfResult = "none",
            DkimResult = "none",
            DmarcResult = "none",
            HasListUnsubscribe = 0,
            HasAttachments = 0,
            HourReceived = 10,
            DayOfWeek = 1,
            EmailSizeLog = 3.0f,
            SubjectLength = 20,
            RecipientCount = 1,
            IsReply = 0,
            InUserWhitelist = 0,
            InUserBlacklist = 0,
            LabelCount = 1,
            LinkCount = 0,
            ImageCount = 0,
            HasTrackingPixel = 0,
            UnsubscribeLinkInBody = 0,
            EmailAgeDays = 2,
            IsInInbox = 1,
            IsStarred = 0,
            IsImportant = 0,
            WasInTrash = 0,
            WasInSpam = 0,
            IsArchived = 0,
            ThreadMessageCount = 1,
            SenderFrequency = 1,
            FeatureSchemaVersion = schemaVersion,
            ExtractedAt = DateTime.UtcNow,
            UserCorrected = 0
        };
    }
}
