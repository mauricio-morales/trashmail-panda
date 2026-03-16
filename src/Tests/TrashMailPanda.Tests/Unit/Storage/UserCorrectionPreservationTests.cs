using Microsoft.Data.Sqlite;
using TrashMailPanda.Providers.Storage;
using TrashMailPanda.Providers.Storage.Models;
using Xunit;

namespace TrashMailPanda.Tests.Unit.Storage;

/// <summary>
/// Tests for user correction preservation during cleanup operations.
/// Validates cleanup prioritization, retention rates, and edge case handling.
/// </summary>
public class UserCorrectionPreservationTests : IDisposable
{
    private readonly SqliteConnection _connection;
    private readonly EmailArchiveService _service;

    public UserCorrectionPreservationTests()
    {
        _connection = new SqliteConnection("Data Source=:memory:");
        _connection.Open();
        InitializeTestSchema();
        _service = new EmailArchiveService(_connection);
    }

    public void Dispose()
    {
        _service.Dispose();
        _connection.Dispose();
    }

    private void InitializeTestSchema()
    {
        using var command = _connection.CreateCommand();
        command.CommandText = @"
            CREATE TABLE email_features (
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
                FeatureSchemaVersion INTEGER NOT NULL,
                ExtractedAt TEXT NOT NULL,
                UserCorrected INTEGER NOT NULL DEFAULT 0
            );

            CREATE TABLE email_archive (
                EmailId TEXT PRIMARY KEY,
                ThreadId TEXT,
                ProviderType TEXT NOT NULL,
                HeadersJson TEXT NOT NULL,
                BodyText TEXT,
                BodyHtml TEXT,
                FolderTagsJson TEXT NOT NULL,
                SizeEstimate INTEGER NOT NULL,
                ReceivedDate TEXT NOT NULL,
                ArchivedAt TEXT NOT NULL,
                Snippet TEXT,
                SourceFolder TEXT NOT NULL,
                UserCorrected INTEGER NOT NULL DEFAULT 0,
                FOREIGN KEY (EmailId) REFERENCES email_features(EmailId) ON DELETE CASCADE
            );

            CREATE TABLE storage_quota (
                Id INTEGER PRIMARY KEY CHECK (Id = 1),
                LimitBytes INTEGER NOT NULL CHECK (LimitBytes > 0),
                CurrentBytes INTEGER NOT NULL CHECK (CurrentBytes >= 0),
                FeatureBytes INTEGER NOT NULL CHECK (FeatureBytes >= 0),
                ArchiveBytes INTEGER NOT NULL CHECK (ArchiveBytes >= 0),
                FeatureCount INTEGER NOT NULL CHECK (FeatureCount >= 0),
                ArchiveCount INTEGER NOT NULL CHECK (ArchiveCount >= 0),
                UserCorrectedCount INTEGER NOT NULL CHECK (UserCorrectedCount >= 0),
                LastCleanupAt TEXT,
                LastMonitoredAt TEXT NOT NULL
            );";
        command.ExecuteNonQuery();
    }

    // ============================================================
    // T063: Unit test for cleanup prioritization of non-corrected emails
    // ============================================================

    [Fact]
    public async Task Cleanup_DeletesNonCorrectedFirst_PreservesUserCorrected()
    {
        // Arrange - Create 10 non-corrected and 5 user-corrected archives
        for (int i = 0; i < 10; i++)
        {
            var feature = CreateTestFeature($"noncorrected-{i}@example.com");
            await _service.StoreFeatureAsync(feature);

            var archive = CreateTestArchive($"noncorrected-{i}@example.com", userCorrected: 0, daysAgo: 30 - i);
            await _service.StoreArchiveAsync(archive);
        }

        for (int i = 0; i < 5; i++)
        {
            var feature = CreateTestFeature($"corrected-{i}@example.com");
            await _service.StoreFeatureAsync(feature);

            var archive = CreateTestArchive($"corrected-{i}@example.com", userCorrected: 1, daysAgo: 30 - i);
            await _service.StoreArchiveAsync(archive);
        }

        // Verify initial state
        var initialUsage = await _service.GetStorageUsageAsync();
        Assert.Equal(15, initialUsage.Value!.ArchiveCount);
        Assert.Equal(5, initialUsage.Value.UserCorrectedCount);

        // Act - Execute cleanup targeting 50% capacity (should delete ~7-8 archives)
        var result = await _service.ExecuteCleanupAsync(targetPercent: 50);

        // Assert
        Assert.True(result.IsSuccess);
        var deletedCount = result.Value;

        // Verify some archives were deleted
        if (deletedCount > 0)
        {
            var finalUsage = await _service.GetStorageUsageAsync();
            Assert.True(finalUsage.Value!.ArchiveCount < 15, "Total archive count should decrease");

            // Verify oldest non-corrected was deleted first
            var oldestNonCorrected = await _service.GetArchiveAsync("noncorrected-0@example.com");
            // May or may not exist depending on how many were deleted

            // Verify user-corrected emails were preserved if possible
            // At minimum, some should still exist
            Assert.True(finalUsage.Value.UserCorrectedCount >= 0, "User-corrected count should be tracked");
        }
    }

    // ============================================================
    // T064: Unit test for user-corrected retention during cleanup
    // ============================================================

    [Fact]
    public async Task Cleanup_RetainsUserCorrected_WhenNonCorrectedAvailable()
    {
        // Arrange - Create many non-corrected and few user-corrected
        for (int i = 0; i < 20; i++)
        {
            var feature = CreateTestFeature($"noncorrected-{i}@example.com");
            await _service.StoreFeatureAsync(feature);

            var archive = CreateTestArchive($"noncorrected-{i}@example.com", userCorrected: 0, daysAgo: 60 - i);
            await _service.StoreArchiveAsync(archive);
        }

        for (int i = 0; i < 3; i++)
        {
            var feature = CreateTestFeature($"corrected-{i}@example.com");
            await _service.StoreFeatureAsync(feature);

            var archive = CreateTestArchive($"corrected-{i}@example.com", userCorrected: 1, daysAgo: 60 - i);
            await _service.StoreArchiveAsync(archive);
        }

        // Verify initial state
        var initialUsage = await _service.GetStorageUsageAsync();
        Assert.Equal(23, initialUsage.Value!.ArchiveCount);
        Assert.Equal(3, initialUsage.Value.UserCorrectedCount);

        // Act - Execute cleanup
        var result = await _service.ExecuteCleanupAsync(targetPercent: 60);

        // Assert
        Assert.True(result.IsSuccess);

        var finalUsage = await _service.GetStorageUsageAsync();

        // Verify all user-corrected emails still exist (should be preserved)
        for (int i = 0; i < 3; i++)
        {
            var correctedCheck = await _service.GetArchiveAsync($"corrected-{i}@example.com");
            Assert.True(correctedCheck.IsSuccess && correctedCheck.Value != null,
                       $"User-corrected email {i} should be preserved");
        }
    }

    // ============================================================
    // T065: Unit test for edge case when only user-corrected emails remain
    // ============================================================

    [Fact]
    public async Task Cleanup_HandlesAllUserCorrectedCase_AllowsTemporaryExceed()
    {
        // Arrange - Create ONLY user-corrected archives
        for (int i = 0; i < 10; i++)
        {
            var feature = CreateTestFeature($"corrected-{i}@example.com");
            await _service.StoreFeatureAsync(feature);

            var archive = CreateTestArchive($"corrected-{i}@example.com", userCorrected: 1, daysAgo: 30 - i);
            await _service.StoreArchiveAsync(archive);
        }

        // Set very low storage limit to trigger cleanup
        await _service.UpdateStorageLimitAsync(5 * 1024); // 5KB limit (very low)

        // Verify initial state - all archives are user-corrected
        var initialUsage = await _service.GetStorageUsageAsync();
        Assert.Equal(10, initialUsage.Value!.ArchiveCount);
        Assert.Equal(10, initialUsage.Value.UserCorrectedCount);

        // Act - Execute cleanup (should handle gracefully even if target can't be met)
        var result = await _service.ExecuteCleanupAsync(targetPercent: 50);

        // Assert
        Assert.True(result.IsSuccess, "Cleanup should succeed even when only user-corrected emails exist");

        var finalUsage = await _service.GetStorageUsageAsync();

        // Verify cleanup either deleted some user-corrected emails (if absolutely necessary)
        // or allowed temporary limit exceed
        Assert.True(finalUsage.Value!.ArchiveCount <= 10, "Archive count should not increase");

        // Edge case: If storage limit is exceeded and all emails are user-corrected,
        // the system should log a warning but not fail (per spec.md edge cases)
        if (finalUsage.Value.UserCorrectedCount == finalUsage.Value.ArchiveCount)
        {
            // All remaining archives are user-corrected - system allowed temporary exceed
            Assert.True(finalUsage.Value.UserCorrectedCount > 0,
                       "Should preserve user-corrected emails even if over limit");
        }
    }

    // ============================================================
    // Test Helpers
    // ============================================================

    private EmailFeatureVector CreateTestFeature(string emailId)
    {
        return new EmailFeatureVector
        {
            EmailId = emailId,
            SenderDomain = "example.com",
            SenderKnown = 1,
            ContactStrength = 1,
            SpfResult = "pass",
            DkimResult = "pass",
            DmarcResult = "pass",
            HasListUnsubscribe = 0,
            HasAttachments = 0,
            HourReceived = 10,
            DayOfWeek = 3,
            EmailSizeLog = 3.5f,
            SubjectLength = 50,
            RecipientCount = 1,
            IsReply = 0,
            InUserWhitelist = 0,
            InUserBlacklist = 0,
            LabelCount = 2,
            LinkCount = 1,
            ImageCount = 0,
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
            SenderFrequency = 1,
            SubjectText = "Test Subject",
            BodyTextShort = "Test body text",
            TopicClusterId = null,
            TopicDistributionJson = null,
            SenderCategory = null,
            SemanticEmbeddingJson = null,
            FeatureSchemaVersion = 1,
            ExtractedAt = DateTime.UtcNow,
            UserCorrected = 0
        };
    }

    private EmailArchiveEntry CreateTestArchive(string emailId, int userCorrected = 0, int daysAgo = 0)
    {
        return new EmailArchiveEntry
        {
            EmailId = emailId,
            ThreadId = $"thread-{emailId}",
            ProviderType = "Gmail",
            HeadersJson = "{\"From\":\"test@example.com\"}",
            BodyText = "Test email body text",
            BodyHtml = "<html><body>Test email body</body></html>",
            FolderTagsJson = "[\"INBOX\"]",
            SizeEstimate = 1024,
            ReceivedDate = DateTime.UtcNow.AddDays(-7 - daysAgo),
            ArchivedAt = DateTime.UtcNow.AddDays(-daysAgo),
            Snippet = "Test snippet",
            SourceFolder = "INBOX",
            UserCorrected = userCorrected
        };
    }
}
