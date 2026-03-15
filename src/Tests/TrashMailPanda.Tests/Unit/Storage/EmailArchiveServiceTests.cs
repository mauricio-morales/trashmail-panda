using Microsoft.Data.Sqlite;
using TrashMailPanda.Providers.Storage;
using TrashMailPanda.Providers.Storage.Models;
using TrashMailPanda.Shared.Base;
using Xunit;

namespace TrashMailPanda.Tests.Unit.Storage;

[Trait("Category", "Unit")]
public class EmailArchiveServiceTests : IDisposable
{
    private readonly SqliteConnection _connection;
    private readonly EmailArchiveService _service;

    public EmailArchiveServiceTests()
    {
        // Create in-memory database for testing
        _connection = new SqliteConnection("DataSource=:memory:");
        _connection.Open();

        // Initialize schema
        InitializeTestSchema();

        // Create service under test
        _service = new EmailArchiveService(_connection);
    }

    private void InitializeTestSchema()
    {
        using var command = _connection.CreateCommand();
        command.CommandText = @"
            CREATE TABLE IF NOT EXISTS email_features (
                EmailId TEXT PRIMARY KEY,
                SenderDomain TEXT NOT NULL,
                SenderKnown INTEGER NOT NULL,
                ContactStrength INTEGER NOT NULL,
                SpfResult TEXT NOT NULL,
                DkimResult TEXT NOT NULL,
                DmarcResult TEXT NOT NULL,
                HasListUnsubscribe INTEGER NOT NULL,
                HasAttachments INTEGER NOT NULL,
                HourReceived INTEGER NOT NULL,
                DayOfWeek INTEGER NOT NULL,
                EmailSizeLog REAL NOT NULL,
                SubjectLength INTEGER NOT NULL,
                RecipientCount INTEGER NOT NULL,
                IsReply INTEGER NOT NULL,
                InUserWhitelist INTEGER NOT NULL,
                InUserBlacklist INTEGER NOT NULL,
                LabelCount INTEGER NOT NULL,
                LinkCount INTEGER NOT NULL,
                ImageCount INTEGER NOT NULL,
                HasTrackingPixel INTEGER NOT NULL,
                UnsubscribeLinkInBody INTEGER NOT NULL,
                EmailAgeDays INTEGER NOT NULL,
                IsInInbox INTEGER NOT NULL,
                IsStarred INTEGER NOT NULL,
                IsImportant INTEGER NOT NULL,
                WasInTrash INTEGER NOT NULL,
                WasInSpam INTEGER NOT NULL,
                IsArchived INTEGER NOT NULL,
                ThreadMessageCount INTEGER NOT NULL,
                SenderFrequency INTEGER NOT NULL,
                SubjectText TEXT,
                BodyTextShort TEXT,
                TopicClusterId INTEGER,
                TopicDistributionJson TEXT,
                SenderCategory TEXT,
                SemanticEmbeddingJson TEXT,
                FeatureSchemaVersion INTEGER NOT NULL DEFAULT 1,
                ExtractedAt TEXT NOT NULL,
                UserCorrected INTEGER NOT NULL DEFAULT 0
            );
            
            CREATE INDEX IF NOT EXISTS idx_email_features_extracted_at 
                ON email_features(ExtractedAt);
            CREATE INDEX IF NOT EXISTS idx_email_features_schema_version 
                ON email_features(FeatureSchemaVersion);
            CREATE INDEX IF NOT EXISTS idx_email_features_user_corrected 
                ON email_features(UserCorrected);";
        command.ExecuteNonQuery();
    }

    public void Dispose()
    {
        _service.Dispose();
        _connection.Dispose();
    }

    #region StoreFeatureAsync Tests (T020)

    [Fact]
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

    [Fact]
    public async Task StoreFeatureAsync_NullFeature_ReturnsValidationError()
    {
        // Act
        var result = await _service.StoreFeatureAsync(null!);

        // Assert
        Assert.False(result.IsSuccess);
        Assert.IsType<ValidationError>(result.Error);
        Assert.Contains("Feature", result.Error.Message);
    }

    [Fact]
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

    [Fact]
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

    [Fact]
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

    [Fact]
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

    [Fact]
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

    [Fact]
    public async Task StoreFeaturesBatchAsync_NullCollection_ReturnsValidationError()
    {
        // Act
        var result = await _service.StoreFeaturesBatchAsync(null!);

        // Assert
        Assert.False(result.IsSuccess);
        Assert.IsType<ValidationError>(result.Error);
    }

    [Fact]
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

    [Fact]
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

    [Fact]
    public async Task GetFeatureAsync_NonExistentFeature_ReturnsNull()
    {
        // Act
        var result = await _service.GetFeatureAsync("non-existent");

        // Assert
        Assert.True(result.IsSuccess);
        Assert.Null(result.Value);
    }

    [Fact]
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

    [Fact]
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

    [Fact]
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

    [Fact]
    public async Task GetAllFeaturesAsync_EmptyDatabase_ReturnsEmptyCollection()
    {
        // Act
        var result = await _service.GetAllFeaturesAsync();

        // Assert
        Assert.True(result.IsSuccess);
        Assert.Empty(result.Value);
    }

    #endregion

    #region Helper Methods

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
