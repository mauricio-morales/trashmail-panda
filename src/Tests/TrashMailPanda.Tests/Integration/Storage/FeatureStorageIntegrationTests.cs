using TrashMailPanda.Providers.Storage;
using TrashMailPanda.Providers.Storage.Models;
using Xunit;

namespace TrashMailPanda.Tests.Integration.Storage;

[Trait("Category", "Integration")]
public class FeatureStorageIntegrationTests : StorageTestBase
{
    private readonly EmailArchiveService _service;

    public FeatureStorageIntegrationTests() : base()
    {
        _service = new EmailArchiveService(_context);
    }

    public override void Dispose()
    {
        _service.Dispose();
        base.Dispose();
    }

    [Fact(Timeout = 5000)]
    public async Task FeatureStorage_EndToEndWorkflow_SuccessfullyStoresAndRetrieves()
    {
        // Arrange
        var feature1 = CreateTestFeature("workflow-1", "example.com");
        var feature2 = CreateTestFeature("workflow-2", "test.com");
        var feature3 = CreateTestFeature("workflow-3", "example.org");

        // Act - Store features
        var store1 = await _service.StoreFeatureAsync(feature1);
        var store2 = await _service.StoreFeatureAsync(feature2);
        var store3 = await _service.StoreFeatureAsync(feature3);

        Assert.True(store1.IsSuccess);
        Assert.True(store2.IsSuccess);
        Assert.True(store3.IsSuccess);

        // Act - Retrieve individual features
        var retrieved1 = await _service.GetFeatureAsync("workflow-1");
        var retrieved2 = await _service.GetFeatureAsync("workflow-2");

        // Assert individual retrieval
        Assert.True(retrieved1.IsSuccess);
        Assert.NotNull(retrieved1.Value);
        Assert.Equal("workflow-1", retrieved1.Value.EmailId);
        Assert.Equal("example.com", retrieved1.Value.SenderDomain);

        Assert.True(retrieved2.IsSuccess);
        Assert.NotNull(retrieved2.Value);
        Assert.Equal("workflow-2", retrieved2.Value.EmailId);
        Assert.Equal("test.com", retrieved2.Value.SenderDomain);

        // Act - Retrieve all features
        var allFeatures = await _service.GetAllFeaturesAsync();

        // Assert bulk retrieval
        Assert.True(allFeatures.IsSuccess);
        var featureList = allFeatures.Value.ToList();
        Assert.Equal(3, featureList.Count);
        Assert.Contains(featureList, f => f.EmailId == "workflow-1");
        Assert.Contains(featureList, f => f.EmailId == "workflow-2");
        Assert.Contains(featureList, f => f.EmailId == "workflow-3");
    }

    [Fact(Timeout = 5000)]
    public async Task FeatureStorage_BatchStorage_HandlesLargeBatchCorrectly()
    {
        // Arrange - Create 500 features (one full batch)
        var features = new List<EmailFeatureVector>();
        for (int i = 0; i < 500; i++)
        {
            features.Add(CreateTestFeature($"batch-{i}", $"domain-{i % 10}.com"));
        }

        // Act - Store batch
        var result = await _service.StoreFeaturesBatchAsync(features);

        // Assert batch storage
        Assert.True(result.IsSuccess);
        Assert.Equal(500, result.Value);

        // Verify all features are retrievable
        var allFeatures = await _service.GetAllFeaturesAsync();
        Assert.True(allFeatures.IsSuccess);
        Assert.Equal(500, allFeatures.Value.Count());
    }

    [Fact(Timeout = 5000)]
    public async Task FeatureStorage_FeaturePersistsAfterArchiveDeletion_VerifiesIndependentStorage()
    {
        // Arrange - Create feature
        var feature = CreateTestFeature("persist-test", "example.com");
        await _service.StoreFeatureAsync(feature);

        // Act - Verify feature exists
        var retrievedBefore = await _service.GetFeatureAsync("persist-test");
        Assert.True(retrievedBefore.IsSuccess);
        Assert.NotNull(retrievedBefore.Value);

        // Simulate archive deletion (feature should persist)
        // Features are stored in email_features table independently
        // Archive entries in email_archive table reference features via FK
        // Deleting archive entry should NOT delete the feature (features are primary)

        // Create and then delete an archive entry for this email
        using (var command = _connection.CreateCommand())
        {
            // First, insert an archive entry
            command.CommandText = @"
                INSERT INTO email_archive (
                    EmailId, ThreadId, ProviderType, HeadersJson, BodyText, BodyHtml,
                    FolderTagsJson, SizeEstimate, ReceivedDate, ArchivedAt, Snippet, SourceFolder, UserCorrected
                ) VALUES (
                    @EmailId, @ThreadId, @ProviderType, @HeadersJson, @BodyText, NULL,
                    @FolderTagsJson, @SizeEstimate, @ReceivedDate, @ArchivedAt, @Snippet, @SourceFolder, 0
                )";
            command.Parameters.AddWithValue("@EmailId", "persist-test");
            command.Parameters.AddWithValue("@ThreadId", "thread-123");
            command.Parameters.AddWithValue("@ProviderType", "Gmail");
            command.Parameters.AddWithValue("@HeadersJson", "{}");
            command.Parameters.AddWithValue("@BodyText", "Test body content for archive");
            command.Parameters.AddWithValue("@FolderTagsJson", "[\"INBOX\"]");
            command.Parameters.AddWithValue("@SizeEstimate", 1024);
            command.Parameters.AddWithValue("@ReceivedDate", DateTime.UtcNow.ToString("o"));
            command.Parameters.AddWithValue("@ArchivedAt", DateTime.UtcNow.ToString("o"));
            command.Parameters.AddWithValue("@Snippet", "Test email snippet");
            command.Parameters.AddWithValue("@SourceFolder", "INBOX");
            await command.ExecuteNonQueryAsync();
        }

        // Verify archive exists
        using (var command = _connection.CreateCommand())
        {
            command.CommandText = "SELECT COUNT(*) FROM email_archive WHERE EmailId = 'persist-test'";
            var count = Convert.ToInt32(await command.ExecuteScalarAsync());
            Assert.Equal(1, count);
        }

        // Delete archive entry
        using (var command = _connection.CreateCommand())
        {
            command.CommandText = "DELETE FROM email_archive WHERE EmailId = 'persist-test'";
            await command.ExecuteNonQueryAsync();
        }

        // Assert - Feature should still exist after archive deletion
        var retrievedAfter = await _service.GetFeatureAsync("persist-test");
        Assert.True(retrievedAfter.IsSuccess);
        Assert.NotNull(retrievedAfter.Value);
        Assert.Equal("persist-test", retrievedAfter.Value.EmailId);
        Assert.Equal("example.com", retrievedAfter.Value.SenderDomain);
    }

    [Fact(Timeout = 5000)]
    public async Task FeatureStorage_SchemaVersionFilter_ReturnsCorrectSubset()
    {
        // Arrange - Create features with different schema versions
        var featureV1a = CreateTestFeature("schema-v1-a", "example.com", schemaVersion: 1);
        var featureV1b = CreateTestFeature("schema-v1-b", "test.com", schemaVersion: 1);
        var featureV2 = CreateTestFeature("schema-v2", "example.org", schemaVersion: 2);

        await _service.StoreFeatureAsync(featureV1a);
        await _service.StoreFeatureAsync(featureV1b);
        await _service.StoreFeatureAsync(featureV2);

        // Act - Filter by schema version
        var v1Features = await _service.GetAllFeaturesAsync(schemaVersion: 1);
        var v2Features = await _service.GetAllFeaturesAsync(schemaVersion: 2);
        var allFeatures = await _service.GetAllFeaturesAsync();

        // Assert
        Assert.True(v1Features.IsSuccess);
        Assert.Equal(2, v1Features.Value.Count());
        Assert.All(v1Features.Value, f => Assert.Equal(1, f.FeatureSchemaVersion));

        Assert.True(v2Features.IsSuccess);
        Assert.Single(v2Features.Value);
        Assert.All(v2Features.Value, f => Assert.Equal(2, f.FeatureSchemaVersion));

        Assert.True(allFeatures.IsSuccess);
        Assert.Equal(3, allFeatures.Value.Count());
    }

    [Fact(Timeout = 5000)]
    public async Task FeatureStorage_UpsertBehavior_UpdatesExistingFeature()
    {
        // Arrange - Store initial feature
        var feature = CreateTestFeature("upsert-id", "original.com");
        feature = new EmailFeatureVector
        {
            EmailId = feature.EmailId,
            SenderDomain = feature.SenderDomain,
            SenderKnown = 0, // Initially unknown
            ContactStrength = 0, // Initially no contact
            SpfResult = feature.SpfResult,
            DkimResult = feature.DkimResult,
            DmarcResult = feature.DmarcResult,
            HasListUnsubscribe = feature.HasListUnsubscribe,
            HasAttachments = feature.HasAttachments,
            HourReceived = feature.HourReceived,
            DayOfWeek = feature.DayOfWeek,
            EmailSizeLog = feature.EmailSizeLog,
            SubjectLength = feature.SubjectLength,
            RecipientCount = feature.RecipientCount,
            IsReply = feature.IsReply,
            InUserWhitelist = feature.InUserWhitelist,
            InUserBlacklist = feature.InUserBlacklist,
            LabelCount = feature.LabelCount,
            LinkCount = feature.LinkCount,
            ImageCount = feature.ImageCount,
            HasTrackingPixel = feature.HasTrackingPixel,
            UnsubscribeLinkInBody = feature.UnsubscribeLinkInBody,
            EmailAgeDays = feature.EmailAgeDays,
            IsInInbox = feature.IsInInbox,
            IsStarred = feature.IsStarred,
            IsImportant = feature.IsImportant,
            WasInTrash = feature.WasInTrash,
            WasInSpam = feature.WasInSpam,
            IsArchived = feature.IsArchived,
            ThreadMessageCount = feature.ThreadMessageCount,
            SenderFrequency = feature.SenderFrequency,
            SubjectText = feature.SubjectText,
            BodyTextShort = feature.BodyTextShort,
            TopicClusterId = feature.TopicClusterId,
            TopicDistributionJson = feature.TopicDistributionJson,
            SenderCategory = feature.SenderCategory,
            SemanticEmbeddingJson = feature.SemanticEmbeddingJson,
            FeatureSchemaVersion = feature.FeatureSchemaVersion,
            ExtractedAt = feature.ExtractedAt,
            UserCorrected = feature.UserCorrected
        };

        await _service.StoreFeatureAsync(feature);

        // Act - Update with new values
        var updatedFeature = CreateTestFeature("upsert-id", "updated.com");
        updatedFeature = new EmailFeatureVector
        {
            EmailId = updatedFeature.EmailId,
            SenderDomain = updatedFeature.SenderDomain,
            SenderKnown = 1, // Now known
            ContactStrength = 2, // Now strong contact
            SpfResult = updatedFeature.SpfResult,
            DkimResult = updatedFeature.DkimResult,
            DmarcResult = updatedFeature.DmarcResult,
            HasListUnsubscribe = updatedFeature.HasListUnsubscribe,
            HasAttachments = updatedFeature.HasAttachments,
            HourReceived = updatedFeature.HourReceived,
            DayOfWeek = updatedFeature.DayOfWeek,
            EmailSizeLog = updatedFeature.EmailSizeLog,
            SubjectLength = updatedFeature.SubjectLength,
            RecipientCount = updatedFeature.RecipientCount,
            IsReply = updatedFeature.IsReply,
            InUserWhitelist = updatedFeature.InUserWhitelist,
            InUserBlacklist = updatedFeature.InUserBlacklist,
            LabelCount = updatedFeature.LabelCount,
            LinkCount = updatedFeature.LinkCount,
            ImageCount = updatedFeature.ImageCount,
            HasTrackingPixel = updatedFeature.HasTrackingPixel,
            UnsubscribeLinkInBody = updatedFeature.UnsubscribeLinkInBody,
            EmailAgeDays = updatedFeature.EmailAgeDays,
            IsInInbox = updatedFeature.IsInInbox,
            IsStarred = updatedFeature.IsStarred,
            IsImportant = updatedFeature.IsImportant,
            WasInTrash = updatedFeature.WasInTrash,
            WasInSpam = updatedFeature.WasInSpam,
            IsArchived = updatedFeature.IsArchived,
            ThreadMessageCount = updatedFeature.ThreadMessageCount,
            SenderFrequency = updatedFeature.SenderFrequency,
            SubjectText = updatedFeature.SubjectText,
            BodyTextShort = updatedFeature.BodyTextShort,
            TopicClusterId = updatedFeature.TopicClusterId,
            TopicDistributionJson = updatedFeature.TopicDistributionJson,
            SenderCategory = updatedFeature.SenderCategory,
            SemanticEmbeddingJson = updatedFeature.SemanticEmbeddingJson,
            FeatureSchemaVersion = updatedFeature.FeatureSchemaVersion,
            ExtractedAt = updatedFeature.ExtractedAt,
            UserCorrected = updatedFeature.UserCorrected
        };

        await _service.StoreFeatureAsync(updatedFeature);

        // Assert - Should have updated, not created duplicate
        var allFeatures = await _service.GetAllFeaturesAsync();
        Assert.True(allFeatures.IsSuccess);
        Assert.Single(allFeatures.Value);

        var retrieved = await _service.GetFeatureAsync("upsert-id");
        Assert.True(retrieved.IsSuccess);
        Assert.Equal("updated.com", retrieved.Value!.SenderDomain);
        Assert.Equal(1, retrieved.Value.SenderKnown);
        Assert.Equal(2, retrieved.Value.ContactStrength);
    }

    private EmailFeatureVector CreateTestFeature(string emailId, string senderDomain, int schemaVersion = 1)
    {
        return new EmailFeatureVector
        {
            EmailId = emailId,
            SenderDomain = senderDomain,
            SenderKnown = 1,
            ContactStrength = 1,
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
}
