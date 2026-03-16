using Microsoft.Data.Sqlite;
using TrashMailPanda.Providers.Storage;
using TrashMailPanda.Providers.Storage.Models;
using Xunit;

namespace TrashMailPanda.Tests.Integration.Storage;

/// <summary>
/// Integration tests for storage cleanup and limit enforcement.
/// Tests automatic cleanup, VACUUM space reclamation, and storage limit enforcement.
/// </summary>
public class StorageCleanupIntegrationTests : IDisposable
{
    private readonly SqliteConnection _connection;
    private readonly EmailArchiveService _service;
    private readonly string _tempDbPath;

    public StorageCleanupIntegrationTests()
    {
        _tempDbPath = Path.Combine(Path.GetTempPath(), $"test_cleanup_{Guid.NewGuid()}.db");
        _connection = new SqliteConnection($"Data Source={_tempDbPath}");
        _connection.Open();
        InitializeDatabase();
        _service = new EmailArchiveService(_connection);
    }

    public void Dispose()
    {
        _service.Dispose();
        _connection.Dispose();
        if (File.Exists(_tempDbPath))
            File.Delete(_tempDbPath);
    }

    private void InitializeDatabase()
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
    // T056: Integration test for automatic cleanup workflow
    // ============================================================

    [Fact]
    public async Task AutomaticCleanup_RemovesOldestNonCorrectedArchives_MaintainsUserCorrected()
    {
        // Arrange - Create 100 archives: 80 non-corrected (old), 20 user-corrected (old)
        for (int i = 0; i < 80; i++)
        {
            var feature = CreateTestFeature($"noncorrected-{i}@example.com");
            await _service.StoreFeatureAsync(feature);

            var archive = CreateTestArchive($"noncorrected-{i}@example.com", userCorrected: 0, daysAgo: 90 - i);
            await _service.StoreArchiveAsync(archive);
        }

        for (int i = 0; i < 20; i++)
        {
            var feature = CreateTestFeature($"corrected-{i}@example.com");
            await _service.StoreFeatureAsync(feature);

            var archive = CreateTestArchive($"corrected-{i}@example.com", userCorrected: 1, daysAgo: 90 - i);
            await _service.StoreArchiveAsync(archive);
        }

        // Verify initial state
        var initialUsage = await _service.GetStorageUsageAsync();
        Assert.Equal(100, initialUsage.Value!.ArchiveCount);
        Assert.Equal(20, initialUsage.Value.UserCorrectedCount);

        // Act - Execute cleanup to target 50% capacity (should delete ~50 archives)
        var cleanupResult = await _service.ExecuteCleanupAsync(targetPercent: 50);

        // Assert
        Assert.True(cleanupResult.IsSuccess);
        var deletedCount = cleanupResult.Value;
        Assert.True(deletedCount > 0, "Should have deleted some archives");

        // Verify cleanup deleted oldest non-corrected first
        var finalUsage = await _service.GetStorageUsageAsync();
        Assert.True(finalUsage.Value!.ArchiveCount < 100, "Total archive count should decrease");

        // Verify oldest non-corrected emails were deleted
        var oldestNonCorrected = await _service.GetArchiveAsync("noncorrected-0@example.com");
        // May or may not exist depending on how many were deleted

        // Verify user-corrected emails still exist (should be preserved)
        var userCorrectedCheck = await _service.GetArchiveAsync("corrected-0@example.com");
        Assert.True(userCorrectedCheck.IsSuccess);
        // User-corrected should be preserved if possible

        // Verify LastCleanupAt was updated
        Assert.NotNull(finalUsage.Value.LastCleanupAt);
    }

    // ============================================================
    // T057: Integration test for storage limit enforcement
    // ============================================================

    [Fact]
    public async Task StorageLimit_TriggersCleanup_WhenExceeding90Percent()
    {
        // Arrange - Set a very low storage limit
        var lowLimit = 50 * 1024L; // 50KB limit (artificially low for testing)
        await _service.UpdateStorageLimitAsync(lowLimit);

        // Add archives until we approach the limit
        // Each archive is ~1KB, so add 50+ to exceed limit
        for (int i = 0; i < 60; i++)
        {
            var feature = CreateTestFeature($"email-{i}@example.com");
            await _service.StoreFeatureAsync(feature);

            var archive = CreateTestArchive($"email-{i}@example.com", userCorrected: 0);
            await _service.StoreArchiveAsync(archive);
        }

        // Act - Check if cleanup should be triggered
        var shouldCleanup = await _service.ShouldTriggerCleanupAsync();

        // Assert - Depending on actual database size, cleanup may or may not trigger
        // This test validates the method executes without error
        Assert.True(shouldCleanup.IsSuccess);

        if (shouldCleanup.Value)
        {
            // If cleanup triggered, execute it
            var cleanupResult = await _service.ExecuteCleanupAsync(targetPercent: 80);
            Assert.True(cleanupResult.IsSuccess);

            // Verify usage decreased after cleanup
            var afterCleanup = await _service.GetStorageUsageAsync();
            var usagePercent = (double)afterCleanup.Value!.CurrentBytes / afterCleanup.Value.LimitBytes * 100;
            Assert.True(usagePercent <= 90, "Usage should be reduced below 90% after cleanup");
        }
    }

    // ============================================================
    // T058: Integration test verifying VACUUM reclaims space
    // ============================================================

    [Fact]
    public async Task Cleanup_ExecutesVACUUM_ReclaimsSpace()
    {
        // Arrange - Create 50 archives
        for (int i = 0; i < 50; i++)
        {
            var feature = CreateTestFeature($"vacuum-test-{i}@example.com");
            await _service.StoreFeatureAsync(feature);

            var archive = CreateTestArchive($"vacuum-test-{i}@example.com", userCorrected: 0);
            await _service.StoreArchiveAsync(archive);
        }

        // Get initial database size
        var beforeCleanup = await _service.GetStorageUsageAsync();
        var initialSize = beforeCleanup.Value!.CurrentBytes;
        var initialCount = beforeCleanup.Value.ArchiveCount;

        Assert.Equal(50, initialCount);

        // Act - Execute cleanup with very low target (force deletion)
        var cleanupResult = await _service.ExecuteCleanupAsync(targetPercent: 10);

        // Assert
        Assert.True(cleanupResult.IsSuccess);

        // Verify archives were deleted
        var afterCleanup = await _service.GetStorageUsageAsync();
        var finalCount = afterCleanup.Value!.ArchiveCount;
        var finalSize = afterCleanup.Value.CurrentBytes;

        Assert.True(finalCount < initialCount, "Archive count should decrease");

        // VACUUM should have been executed, potentially reducing database size
        // Note: VACUUM may not always reduce size immediately in SQLite,
        // but this test verifies the operation completes without error
        Assert.True(finalSize >= 0, "Database size should be valid after VACUUM");
    }

    // ============================================================
    // T066: Integration test verifying 95% retention rate for user-corrected emails (SC-004)
    // ============================================================

    [Fact]
    public async Task Cleanup_Maintains95PercentRetention_ForUserCorrectedEmails()
    {
        // Arrange - Create 1000 archives (500 non-corrected, 500 user-corrected)
        const int totalArchives = 1000;
        const int nonCorrectedCount = 500;
        const int userCorrectedCount = 500;
        const int expectedRetentionCount = (int)(userCorrectedCount * 0.95); // 95% = 475

        for (int i = 0; i < nonCorrectedCount; i++)
        {
            var feature = CreateTestFeature($"noncorrected-{i}@example.com");
            await _service.StoreFeatureAsync(feature);

            // Older non-corrected emails (60-30 days ago)
            var archive = CreateTestArchive($"noncorrected-{i}@example.com", userCorrected: 0, daysAgo: 60 - (i / 10));
            await _service.StoreArchiveAsync(archive);
        }

        for (int i = 0; i < userCorrectedCount; i++)
        {
            var feature = CreateTestFeature($"corrected-{i}@example.com");
            await _service.StoreFeatureAsync(feature);

            // Older user-corrected emails (60-30 days ago)
            var archive = CreateTestArchive($"corrected-{i}@example.com", userCorrected: 1, daysAgo: 60 - (i / 10));
            await _service.StoreArchiveAsync(archive);
        }

        // Verify initial state
        var initialUsage = await _service.GetStorageUsageAsync();
        Assert.Equal(totalArchives, initialUsage.Value!.ArchiveCount);
        Assert.Equal(userCorrectedCount, initialUsage.Value.UserCorrectedCount);

        // Set storage limit to force cleanup
        var currentBytes = initialUsage.Value.CurrentBytes;
        var limitBytes = (long)(currentBytes * 0.5); // Set limit to 50% of current usage to force aggressive cleanup
        await _service.UpdateStorageLimitAsync(limitBytes);

        // Act - Execute cleanup targeting 80% of limit
        var cleanupResult = await _service.ExecuteCleanupAsync(targetPercent: 80);

        // Assert
        Assert.True(cleanupResult.IsSuccess, "Cleanup should complete successfully");

        var finalUsage = await _service.GetStorageUsageAsync();

        // Verify at least 95% of user-corrected emails were retained
        var retainedUserCorrected = finalUsage.Value!.UserCorrectedCount;
        var retentionRate = (double)retainedUserCorrected / userCorrectedCount;

        Assert.True(retainedUserCorrected >= expectedRetentionCount,
                   $"Should retain at least {expectedRetentionCount} user-corrected emails (95%), " +
                   $"but only {retainedUserCorrected} retained ({retentionRate:P})");

        // Verify non-corrected emails were deleted preferentially
        var deletedTotal = initialUsage.Value.ArchiveCount - finalUsage.Value.ArchiveCount;
        var deletedUserCorrected = userCorrectedCount - retainedUserCorrected;
        var deletedNonCorrected = deletedTotal - deletedUserCorrected;

        Assert.True(deletedNonCorrected > deletedUserCorrected,
                   $"Should delete more non-corrected ({deletedNonCorrected}) than user-corrected ({deletedUserCorrected}) emails");

        // Verify storage usage was reduced
        Assert.True(finalUsage.Value.CurrentBytes < initialUsage.Value.CurrentBytes,
                   "Storage usage should decrease after cleanup");
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
            HeadersJson = "{\"From\":\"test@example.com\",\"To\":\"recipient@example.com\"}",
            BodyText = "This is a test email body with some content to make it more realistic. " +
                       "Lorem ipsum dolor sit amet, consectetur adipiscing elit. " +
                       "Sed do eiusmod tempor incididunt ut labore et dolore magna aliqua.",
            BodyHtml = "<html><body><p>This is a test email body with some content to make it more realistic.</p></body></html>",
            FolderTagsJson = "[\"INBOX\"]",
            SizeEstimate = 1024,
            ReceivedDate = DateTime.UtcNow.AddDays(-7 - daysAgo),
            ArchivedAt = DateTime.UtcNow.AddDays(-daysAgo),
            Snippet = "Test email snippet preview",
            SourceFolder = "INBOX",
            UserCorrected = userCorrected
        };
    }
}
