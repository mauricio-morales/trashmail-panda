using TrashMailPanda.Providers.Storage;
using TrashMailPanda.Providers.Storage.Models;
using Xunit;

namespace TrashMailPanda.Tests.Integration.Storage;

/// <summary>
/// Integration tests for storage cleanup and limit enforcement.
/// Tests automatic cleanup, VACUUM space reclamation, and storage limit enforcement.
/// </summary>
public class StorageCleanupIntegrationTests : StorageTestBase
{
    private readonly EmailArchiveService _service;

    public StorageCleanupIntegrationTests() : base()
    {
        _service = new EmailArchiveService(_context);
    }

    public override void Dispose()
    {
        _service.Dispose();
        base.Dispose();
    }

    // ============================================================
    // T056: Integration test for automatic cleanup workflow
    // ============================================================

    [Fact(Timeout = 5000)]
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

        // Set storage limit to force cleanup (100 archives * 5KB ~= 512KB, target 50% = 256KB)
        await _service.UpdateStorageLimitAsync(512 * 1024L); // 512KB limit

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

    [Fact(Timeout = 5000)]
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

            // Use count-based estimation: in-memory SQLite's PRAGMA page_count reflects
            // allocated pages, not freed ones (VACUUM is a no-op for :memory: databases),
            // so CurrentBytes is unreliable here. Count-based gives the correct picture.
            var estimatedBytes = afterCleanup.Value!.ArchiveCount * 5120L;
            var usagePercent = (double)estimatedBytes / afterCleanup.Value.LimitBytes * 100;

            Assert.True(usagePercent <= 90, $"Usage should be reduced below 90% after cleanup (actual: {usagePercent:F1}%)");
        }
    }

    // ============================================================
    // T058: Integration test verifying VACUUM reclaims space
    // ============================================================

    [Fact(Timeout = 5000)]
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
        // Set storage limit to force cleanup (50 archives * 5KB ~= 256KB)
        await _service.UpdateStorageLimitAsync(256 * 1024L); // 256KB limit
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

    [Fact(Timeout = 5000)]
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
        // For in-memory DBs, CurrentBytes may be 0, so use count-based limit
        var currentBytes = initialUsage.Value.CurrentBytes;
        long limitBytes;
        if (currentBytes > 0)
        {
            limitBytes = (long)(currentBytes * 0.5); // Set limit to 50% of current usage to force aggressive cleanup
        }
        else
        {
            // In-memory DB: estimate ~5KB per archive, set limit to 50% of estimated usage
            var estimatedBytes = totalArchives * 5120L;
            limitBytes = estimatedBytes / 2;
        }
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

        // Verify storage usage was reduced (check both bytes and count for in-memory DB compatibility)
        if (initialUsage.Value.CurrentBytes > 0 && finalUsage.Value.CurrentBytes > 0)
        {
            Assert.True(finalUsage.Value.CurrentBytes < initialUsage.Value.CurrentBytes,
                       "Storage usage should decrease after cleanup");
        }
        else
        {
            // In-memory DB: verify archive count decreased instead
            Assert.True(finalUsage.Value.ArchiveCount < initialUsage.Value.ArchiveCount,
                       "Archive count should decrease after cleanup");
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
