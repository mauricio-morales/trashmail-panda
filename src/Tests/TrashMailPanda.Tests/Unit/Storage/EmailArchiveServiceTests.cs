using Microsoft.Data.Sqlite;
using TrashMailPanda.Providers.Storage;
using TrashMailPanda.Providers.Storage.Models;
using TrashMailPanda.Shared.Base;
using Xunit;

namespace TrashMailPanda.Tests.Unit.Storage;

[Trait("Category", "Unit")]
public class EmailArchiveServiceTests : StorageTestBase
{
    private readonly EmailArchiveService _service;

    public EmailArchiveServiceTests() : base()
    {
        // Create service under test with DbContext from base class
        _service = new EmailArchiveService(_context);
    }

    public override void Dispose()
    {
        _service.Dispose();
        base.Dispose();
    }

    #region StoreFeatureAsync Tests (T020)

    [Fact(Timeout = 5000)]
    public async Task StoreFeatureAsync_ValidFeature_ReturnsSuccess()
    {
        // Arrange
        var feature = CreateTestFeatureVector("test-email-1");

        // Act
        var result = await _service.StoreFeatureAsync(feature);

        // Assert
        Assert.True(result.IsSuccess);
        Assert.True(result.Value);
    }

    [Fact(Timeout = 5000)]
    public async Task StoreFeatureAsync_NullFeature_ReturnsValidationError()
    {
        // Act
        var result = await _service.StoreFeatureAsync(null!);

        // Assert
        Assert.False(result.IsSuccess);
        Assert.IsType<ValidationError>(result.Error);
        Assert.Contains("Feature", result.Error.Message);
    }

    [Fact(Timeout = 5000)]
    public async Task StoreFeatureAsync_NullOrWhitespaceEmailId_ReturnsValidationError()
    {
        // Arrange
        var feature = CreateTestFeatureVector("");

        // Act
        var result = await _service.StoreFeatureAsync(feature);

        // Assert
        Assert.False(result.IsSuccess);
        Assert.IsType<ValidationError>(result.Error);
        Assert.Contains("EmailId", result.Error.Message);
    }

    [Fact(Timeout = 5000)]
    public async Task StoreFeatureAsync_FeatureAlreadyExists_UpdatesExistingRow()
    {
        // Arrange
        var feature1 = CreateTestFeatureVector("upsert-email");
        await _service.StoreFeatureAsync(feature1);

        // Create a second feature with same EmailId but different ContactStrength
        var feature2 = new EmailFeatureVector
        {
            EmailId = "upsert-email",
            SenderDomain = "example.com",
            SenderKnown = 1,
            ContactStrength = 100, // Different from original (50)
            SpfResult = "pass",
            DkimResult = "pass",
            DmarcResult = "pass",
            HasListUnsubscribe = 0,
            HasAttachments = 0,
            HourReceived = 14,
            DayOfWeek = 3,
            EmailSizeLog = 4.5f,
            SubjectLength = 45,
            RecipientCount = 1,
            IsReply = 0,
            InUserWhitelist = 0,
            InUserBlacklist = 0,
            LabelCount = 2,
            LinkCount = 3,
            ImageCount = 1,
            HasTrackingPixel = 0,
            UnsubscribeLinkInBody = 0,
            EmailAgeDays = 5,
            IsInInbox = 1,
            IsStarred = 0,
            IsImportant = 0,
            WasInTrash = 0,
            WasInSpam = 0,
            IsArchived = 0,
            ThreadMessageCount = 1,
            SenderFrequency = 10,
            SubjectText = "Test Subject",
            BodyTextShort = "Test body content",
            TopicClusterId = null,
            TopicDistributionJson = null,
            SenderCategory = null,
            SemanticEmbeddingJson = null,
            FeatureSchemaVersion = 1,
            ExtractedAt = DateTime.UtcNow,
            UserCorrected = 0
        };

        // Act - Should succeed and update the row
        var result = await _service.StoreFeatureAsync(feature2);

        // Assert
        Assert.True(result.IsSuccess);
        Assert.True(result.Value);

        // Verify the update worked
        var retrieved = await _service.GetFeatureAsync("upsert-email");
        Assert.True(retrieved.IsSuccess);
        Assert.Equal(100, retrieved.Value!.ContactStrength);
    }

    #endregion

    #region StoreFeaturesBatchAsync Tests (T021)

    [Fact(Timeout = 5000)]
    public async Task StoreFeaturesBatchAsync_ValidBatch_ReturnsSuccessWithCount()
    {
        // Arrange
        var features = new List<EmailFeatureVector>
        {
            CreateTestFeatureVector("batch-1"),
            CreateTestFeatureVector("batch-2"),
            CreateTestFeatureVector("batch-3")
        };

        // Act
        var result = await _service.StoreFeaturesBatchAsync(features);

        // Assert
        Assert.True(result.IsSuccess);
        Assert.Equal(3, result.Value);
    }

    [Fact(Timeout = 5000)]
    public async Task StoreFeaturesBatchAsync_LargeBatch_ProcessesIn500RowBatches()
    {
        // Arrange - Create 1000 features to test batch processing
        var features = new List<EmailFeatureVector>();
        for (int i = 0; i < 1000; i++)
        {
            features.Add(CreateTestFeatureVector($"large-batch-{i}"));
        }

        // Act
        var result = await _service.StoreFeaturesBatchAsync(features);

        // Assert
        Assert.True(result.IsSuccess);
        Assert.Equal(1000, result.Value);
    }

    [Fact(Timeout = 5000)]
    public async Task StoreFeaturesBatchAsync_EmptyCollection_ReturnsZero()
    {
        // Arrange
        var features = new List<EmailFeatureVector>();

        // Act
        var result = await _service.StoreFeaturesBatchAsync(features);

        // Assert
        Assert.True(result.IsSuccess);
        Assert.Equal(0, result.Value);
    }

    [Fact(Timeout = 5000)]
    public async Task StoreFeaturesBatchAsync_NullCollection_ReturnsValidationError()
    {
        // Act
        var result = await _service.StoreFeaturesBatchAsync(null!);

        // Assert
        Assert.False(result.IsSuccess);
        Assert.IsType<ValidationError>(result.Error);
    }

    [Fact(Timeout = 5000)]
    public async Task StoreFeaturesBatchAsync_ContainsInvalidFeature_ReturnsValidationError()
    {
        // Arrange
        var features = new List<EmailFeatureVector>
        {
            CreateTestFeatureVector("valid-1"),
            CreateTestFeatureVector(""), // Invalid EmailId
            CreateTestFeatureVector("valid-2")
        };

        // Act
        var result = await _service.StoreFeaturesBatchAsync(features);

        // Assert
        Assert.False(result.IsSuccess);
        Assert.IsType<ValidationError>(result.Error);
    }

    #endregion

    #region GetFeatureAsync Tests (T022)

    [Fact(Timeout = 5000)]
    public async Task GetFeatureAsync_ExistingFeature_ReturnsFeature()
    {
        // Arrange
        var original = CreateTestFeatureVector("get-test-1");
        await _service.StoreFeatureAsync(original);

        // Act
        var result = await _service.GetFeatureAsync("get-test-1");

        // Assert
        Assert.True(result.IsSuccess);
        Assert.NotNull(result.Value);
        Assert.Equal("get-test-1", result.Value.EmailId);
        Assert.Equal(original.SenderDomain, result.Value.SenderDomain);
        Assert.Equal(original.ContactStrength, result.Value.ContactStrength);
    }

    [Fact(Timeout = 5000)]
    public async Task GetFeatureAsync_NonExistentFeature_ReturnsNull()
    {
        // Act
        var result = await _service.GetFeatureAsync("non-existent");

        // Assert
        Assert.True(result.IsSuccess);
        Assert.Null(result.Value);
    }

    [Fact(Timeout = 5000)]
    public async Task GetFeatureAsync_NullOrWhitespaceEmailId_ReturnsValidationError()
    {
        // Act
        var result = await _service.GetFeatureAsync("");

        // Assert
        Assert.False(result.IsSuccess);
        Assert.IsType<ValidationError>(result.Error);
        Assert.Contains("EmailId", result.Error.Message);
    }

    #endregion

    #region GetAllFeaturesAsync Tests (T023)

    [Fact(Timeout = 5000)]
    public async Task GetAllFeaturesAsync_NoFilter_ReturnsAllFeatures()
    {
        // Arrange
        await _service.StoreFeatureAsync(CreateTestFeatureVector("all-1"));
        await _service.StoreFeatureAsync(CreateTestFeatureVector("all-2"));
        await _service.StoreFeatureAsync(CreateTestFeatureVector("all-3"));

        // Act
        var result = await _service.GetAllFeaturesAsync();

        // Assert
        Assert.True(result.IsSuccess);
        Assert.Equal(3, result.Value.Count());
    }

    [Fact(Timeout = 5000)]
    public async Task GetAllFeaturesAsync_WithSchemaVersionFilter_ReturnsMatchingFeatures()
    {
        // Arrange
        var feature1 = CreateTestFeatureVector("schema-1", schemaVersion: 1);
        await _service.StoreFeatureAsync(feature1);

        var feature2 = CreateTestFeatureVector("schema-2", schemaVersion: 2);
        await _service.StoreFeatureAsync(feature2);

        // Act
        var result = await _service.GetAllFeaturesAsync(schemaVersion: 1);

        // Assert
        Assert.True(result.IsSuccess);
        var features = result.Value.ToList();
        Assert.Single(features);
        Assert.Equal("schema-1", features[0].EmailId);
        Assert.Equal(1, features[0].FeatureSchemaVersion);
    }

    [Fact(Timeout = 5000)]
    public async Task GetAllFeaturesAsync_EmptyDatabase_ReturnsEmptyCollection()
    {
        // Act
        var result = await _service.GetAllFeaturesAsync();

        // Assert
        Assert.True(result.IsSuccess);
        Assert.Empty(result.Value);
    }

    #endregion

    #region Archive Storage Tests

    [Fact(Timeout = 5000)]
    public async Task StoreArchiveAsync_ValidArchive_ReturnsSuccess()
    {
        // Arrange
        var feature = CreateTestFeatureVector("archive-1");
        await _service.StoreFeatureAsync(feature);
        var archive = CreateTestArchive("archive-1");

        // Act
        var result = await _service.StoreArchiveAsync(archive);

        // Assert
        Assert.True(result.IsSuccess);
        Assert.True(result.Value);
    }

    [Fact(Timeout = 5000)]
    public async Task StoreArchiveAsync_NullArchive_ReturnsValidationError()
    {
        // Act
        var result = await _service.StoreArchiveAsync(null!);

        // Assert
        Assert.False(result.IsSuccess);
        Assert.IsType<ValidationError>(result.Error);
        Assert.Contains("cannot be null", result.Error.Message);
    }

    [Fact(Timeout = 5000)]
    public async Task StoreArchiveAsync_EmptyEmailId_ReturnsValidationError()
    {
        // Arrange
        var archive = CreateTestArchive("");

        // Act
        var result = await _service.StoreArchiveAsync(archive);

        // Assert
        Assert.False(result.IsSuccess);
        Assert.IsType<ValidationError>(result.Error);
        Assert.Contains("EmailId is required", result.Error.Message);
    }

    [Fact(Timeout = 5000)]
    public async Task StoreArchiveAsync_NoBodyContent_ReturnsValidationError()
    {
        // Arrange
        var archive = CreateArchiveWithoutBody("archive-1");

        // Act
        var result = await _service.StoreArchiveAsync(archive);

        // Assert
        Assert.False(result.IsSuccess);
        Assert.IsType<ValidationError>(result.Error);
        Assert.Contains("BodyText or BodyHtml must be provided", result.Error.Message);
    }

    [Fact(Timeout = 5000)]
    public async Task StoreArchiveAsync_UpdateExisting_Success()
    {
        // Arrange
        var feature = CreateTestFeatureVector("archive-1");
        await _service.StoreFeatureAsync(feature);

        var archive1 = CreateTestArchive("archive-1");
        await _service.StoreArchiveAsync(archive1);

        var archive2 = CreateTestArchive("archive-1", bodyText: "Updated body content");

        // Act
        var result = await _service.StoreArchiveAsync(archive2);

        // Assert
        Assert.True(result.IsSuccess);

        var retrieved = await _service.GetArchiveAsync("archive-1");
        Assert.True(retrieved.IsSuccess);
        Assert.Equal("Updated body content", retrieved.Value!.BodyText);
    }

    [Fact(Timeout = 5000)]
    public async Task GetArchiveAsync_ExistingArchive_ReturnsArchive()
    {
        // Arrange
        var feature = CreateTestFeatureVector("archive-1");
        await _service.StoreFeatureAsync(feature);

        var archive = CreateTestArchive("archive-1");
        await _service.StoreArchiveAsync(archive);

        // Act
        var result = await _service.GetArchiveAsync("archive-1");

        // Assert
        Assert.True(result.IsSuccess);
        Assert.NotNull(result.Value);
        Assert.Equal("archive-1", result.Value!.EmailId);
        Assert.Equal("gmail", result.Value.ProviderType);
        Assert.Equal("Test body content", result.Value.BodyText);
    }

    [Fact(Timeout = 5000)]
    public async Task GetArchiveAsync_NonExistentArchive_ReturnsNull()
    {
        // Act
        var result = await _service.GetArchiveAsync("non-existent");

        // Assert
        Assert.True(result.IsSuccess);
        Assert.Null(result.Value);
    }

    [Fact(Timeout = 5000)]
    public async Task GetArchiveAsync_EmptyEmailId_ReturnsValidationError()
    {
        // Act
        var result = await _service.GetArchiveAsync("");

        // Assert
        Assert.False(result.IsSuccess);
        Assert.IsType<ValidationError>(result.Error);
        Assert.Contains("EmailId is required", result.Error.Message);
    }

    [Fact(Timeout = 5000)]
    public async Task GetArchiveAsync_NullableFields_HandledCorrectly()
    {
        // Arrange
        var feature = CreateTestFeatureVector("archive-1");
        await _service.StoreFeatureAsync(feature);

        var archive = CreateTestArchive("archive-1", threadId: null, bodyHtml: null, snippet: null);
        await _service.StoreArchiveAsync(archive);

        // Act
        var result = await _service.GetArchiveAsync("archive-1");

        // Assert
        Assert.True(result.IsSuccess);
        Assert.NotNull(result.Value);
        Assert.Null(result.Value!.ThreadId);
        Assert.Null(result.Value.BodyHtml);
        Assert.Null(result.Value.Snippet);
        Assert.NotNull(result.Value.BodyText);
    }

    [Fact(Timeout = 5000)]
    public async Task DeleteArchiveAsync_ExistingArchive_Success()
    {
        // Arrange
        var feature = CreateTestFeatureVector("archive-1");
        await _service.StoreFeatureAsync(feature);

        var archive = CreateTestArchive("archive-1");
        await _service.StoreArchiveAsync(archive);

        // Act
        var result = await _service.DeleteArchiveAsync("archive-1");

        // Assert
        Assert.True(result.IsSuccess);
        Assert.True(result.Value);

        // Verify deletion
        var retrieved = await _service.GetArchiveAsync("archive-1");
        Assert.Null(retrieved.Value);
    }

    [Fact(Timeout = 5000)]
    public async Task DeleteArchiveAsync_NonExistentArchive_ReturnsFalse()
    {
        // Act
        var result = await _service.DeleteArchiveAsync("non-existent");

        // Assert
        Assert.True(result.IsSuccess);
        Assert.False(result.Value);
    }

    [Fact(Timeout = 5000)]
    public async Task DeleteArchiveAsync_EmptyEmailId_ReturnsValidationError()
    {
        // Act
        var result = await _service.DeleteArchiveAsync("");

        // Assert
        Assert.False(result.IsSuccess);
        Assert.IsType<ValidationError>(result.Error);
        Assert.Contains("EmailId is required", result.Error.Message);
    }

    [Fact(Timeout = 5000)]
    public async Task StoreArchivesBatchAsync_ValidArchives_Success()
    {
        // Arrange - First create features for FK constraint
        var feature1 = CreateTestFeatureVector("archive-1");
        var feature2 = CreateTestFeatureVector("archive-2");
        var feature3 = CreateTestFeatureVector("archive-3");
        await _service.StoreFeatureAsync(feature1);
        await _service.StoreFeatureAsync(feature2);
        await _service.StoreFeatureAsync(feature3);

        var archives = new List<EmailArchiveEntry>
        {
            CreateTestArchive("archive-1"),
            CreateTestArchive("archive-2"),
            CreateTestArchive("archive-3")
        };

        // Act
        var result = await _service.StoreArchivesBatchAsync(archives);

        // Assert
        Assert.True(result.IsSuccess);
        Assert.Equal(3, result.Value);

        // Verify all stored
        var email1 = await _service.GetArchiveAsync("archive-1");
        var email2 = await _service.GetArchiveAsync("archive-2");
        var email3 = await _service.GetArchiveAsync("archive-3");

        Assert.True(email1.IsSuccess && email1.Value != null);
        Assert.True(email2.IsSuccess && email2.Value != null);
        Assert.True(email3.IsSuccess && email3.Value != null);
    }

    [Fact(Timeout = 5000)]
    public async Task StoreArchivesBatchAsync_EmptyList_ReturnsZero()
    {
        // Act
        var result = await _service.StoreArchivesBatchAsync(new List<EmailArchiveEntry>());

        // Assert
        Assert.True(result.IsSuccess);
        Assert.Equal(0, result.Value);
    }

    [Fact(Timeout = 5000)]
    public async Task StoreArchivesBatchAsync_NullCollection_ReturnsValidationError()
    {
        // Act
        var result = await _service.StoreArchivesBatchAsync(null!);

        // Assert
        Assert.False(result.IsSuccess);
        Assert.IsType<ValidationError>(result.Error);
        Assert.Contains("cannot be null", result.Error.Message);
    }

    [Fact(Timeout = 5000)]
    public async Task StoreArchivesBatchAsync_InvalidArchiveInBatch_ReturnsValidationError()
    {
        // Arrange
        var archives = new List<EmailArchiveEntry>
        {
            CreateTestArchive("archive-1"),
            CreateTestArchive(""), // Invalid - empty EmailId
            CreateTestArchive("archive-3")
        };

        // Act
        var result = await _service.StoreArchivesBatchAsync(archives);

        // Assert
        Assert.False(result.IsSuccess);
        Assert.IsType<ValidationError>(result.Error);
    }

    [Fact(Timeout = 5000)]
    public async Task StoreArchivesBatchAsync_LargeBatch_ProcessesInChunks()
    {
        // Arrange - Create 1500 features and archives (should be 3 batches of 500)
        for (int i = 1; i <= 1500; i++)
        {
            var feature = CreateTestFeatureVector($"archive-{i}");
            await _service.StoreFeatureAsync(feature);
        }

        var archives = new List<EmailArchiveEntry>();
        for (int i = 1; i <= 1500; i++)
        {
            archives.Add(CreateTestArchive($"archive-{i}"));
        }

        // Act
        var result = await _service.StoreArchivesBatchAsync(archives);

        // Assert
        Assert.True(result.IsSuccess);
        Assert.Equal(1500, result.Value);

        // Spot check a few
        var first = await _service.GetArchiveAsync("archive-1");
        var middle = await _service.GetArchiveAsync("archive-750");
        var last = await _service.GetArchiveAsync("archive-1500");

        Assert.True(first.IsSuccess && first.Value != null);
        Assert.True(middle.IsSuccess && middle.Value != null);
        Assert.True(last.IsSuccess && last.Value != null);
    }

    [Fact(Timeout = 5000)]
    public async Task StoreArchivesBatchAsync_NoBodyContent_ReturnsValidationError()
    {
        // Arrange
        var archives = new List<EmailArchiveEntry>
        {
            CreateTestArchive("archive-1"),
            CreateArchiveWithoutBody("archive-2"), // Invalid
            CreateTestArchive("archive-3")
        };

        // Act
        var result = await _service.StoreArchivesBatchAsync(archives);

        // Assert
        Assert.False(result.IsSuccess);
        Assert.IsType<ValidationError>(result.Error);
        Assert.Contains("BodyText or BodyHtml", result.Error.Message);
    }

    #endregion

    #region Helper Methods

    private EmailArchiveEntry CreateTestArchive(
        string emailId,
        string? threadId = "USE_DEFAULT",
        string bodyText = "Test body content",
        string? bodyHtml = "<p>Test body content</p>",
        string? snippet = "Test body...")
    {
        return new EmailArchiveEntry
        {
            EmailId = emailId,
            ThreadId = threadId == "USE_DEFAULT" ? $"thread-{emailId}" : threadId,
            ProviderType = "gmail",
            HeadersJson = "{\"from\":\"test@example.com\",\"to\":\"user@example.com\"}",
            BodyText = bodyText,
            BodyHtml = bodyHtml,
            FolderTagsJson = "[\"INBOX\"]",
            SizeEstimate = 1024,
            ReceivedDate = DateTime.UtcNow.AddDays(-1),
            ArchivedAt = DateTime.UtcNow,
            Snippet = snippet,
            SourceFolder = "INBOX",
            UserCorrected = 0
        };
    }

    private EmailArchiveEntry CreateArchiveWithoutBody(string emailId)
    {
        return new EmailArchiveEntry
        {
            EmailId = emailId,
            ThreadId = $"thread-{emailId}",
            ProviderType = "gmail",
            HeadersJson = "{\"from\":\"test@example.com\",\"to\":\"user@example.com\"}",
            BodyText = null,
            BodyHtml = null,
            FolderTagsJson = "[\"INBOX\"]",
            SizeEstimate = 1024,
            ReceivedDate = DateTime.UtcNow.AddDays(-1),
            ArchivedAt = DateTime.UtcNow,
            Snippet = "Test body...",
            SourceFolder = "INBOX",
            UserCorrected = 0
        };
    }

    #endregion

    #region Feature Storage Helper Methods

    private EmailFeatureVector CreateTestFeatureVector(string emailId, int schemaVersion = 1)
    {
        return new EmailFeatureVector
        {
            EmailId = emailId,
            SenderDomain = "example.com",
            SenderKnown = 1,
            ContactStrength = 50,
            SpfResult = "pass",
            DkimResult = "pass",
            DmarcResult = "pass",
            HasListUnsubscribe = 0,
            HasAttachments = 0,
            HourReceived = 14,
            DayOfWeek = 3,
            EmailSizeLog = 4.5f,
            SubjectLength = 45,
            RecipientCount = 1,
            IsReply = 0,
            InUserWhitelist = 0,
            InUserBlacklist = 0,
            LabelCount = 2,
            LinkCount = 3,
            ImageCount = 1,
            HasTrackingPixel = 0,
            UnsubscribeLinkInBody = 0,
            EmailAgeDays = 5,
            IsInInbox = 1,
            IsStarred = 0,
            IsImportant = 0,
            WasInTrash = 0,
            WasInSpam = 0,
            IsArchived = 0,
            ThreadMessageCount = 1,
            SenderFrequency = 10,
            SubjectText = "Test Subject",
            BodyTextShort = "Test body content",
            TopicClusterId = null,
            TopicDistributionJson = null,
            SenderCategory = null,
            SemanticEmbeddingJson = null,
            FeatureSchemaVersion = schemaVersion,
            ExtractedAt = DateTime.UtcNow,
            UserCorrected = 0
        };
    }

    #endregion
}
