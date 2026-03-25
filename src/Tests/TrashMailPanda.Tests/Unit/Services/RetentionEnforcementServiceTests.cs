using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Moq;
using TrashMailPanda.Models;
using TrashMailPanda.Providers.Storage;
using TrashMailPanda.Providers.Storage.Models;
using TrashMailPanda.Providers.Storage.Services;
using TrashMailPanda.Services;
using TrashMailPanda.Shared;
using TrashMailPanda.Shared.Base;
using TrashMailPanda.Shared.Labels;
using Xunit;

namespace TrashMailPanda.Tests.Unit.Services;

/// <summary>
/// Unit tests for <see cref="RetentionEnforcementService"/> covering the scan algorithm,
/// threshold boundary conditions, per-email failure handling, and config persistence.
/// </summary>
[Trait("Category", "Unit")]
public class RetentionEnforcementServiceTests
{
    private readonly Mock<IEmailArchiveService> _archiveService = new();
    private readonly Mock<IEmailProvider> _emailProvider = new();
    private readonly Mock<IConfigurationService> _configService = new();

    private RetentionEnforcementService CreateSut(int promptThresholdDays = 7)
    {
        var options = Options.Create(new RetentionEnforcementOptions
        {
            ScanIntervalDays = 30,
            PromptThresholdDays = promptThresholdDays,
        });
        return new RetentionEnforcementService(
            _archiveService.Object,
            _emailProvider.Object,
            _configService.Object,
            options,
            NullLogger<RetentionEnforcementService>.Instance);
    }

    private static EmailFeatureVector MakeArchivedFeature(
        string emailId,
        string trainingLabel,
        DateTime receivedDate) =>
        new()
        {
            EmailId = emailId,
            SenderDomain = "example.com",
            SpfResult = "pass",
            DkimResult = "pass",
            DmarcResult = "pass",
            SenderFrequency = 1,
            ThreadMessageCount = 1,
            FeatureSchemaVersion = 1,
            EmailAgeDays = 10,
            EmailSizeLog = 8f,
            ExtractedAt = DateTime.UtcNow,
            IsArchived = 1,
            TrainingLabel = trainingLabel,
            ReceivedDateUtc = receivedDate,
        };

    private void SetupConfig(DateTime? lastScanUtc = null)
    {
        var config = new AppConfig
        {
            ProcessingSettings = new ProcessingSettings
            {
                Retention = new RetentionSettings { LastScanUtc = lastScanUtc }
            }
        };
        _configService.Setup(x => x.GetConfigAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result<AppConfig>.Success(config));
        _configService.Setup(x => x.UpdateProcessingSettingsAsync(
                It.IsAny<ProcessingSettings>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result<bool>.Success(true));
    }

    // ── Expired email is deleted ──────────────────────────────────────────────

    [Fact]
    public async Task RunScanAsync_ExpiredEmail_CallsBatchModifyWithTrash()
    {
        // Arrange: email received 40 days ago with 30d label → should be deleted
        var receivedDate = DateTime.UtcNow.AddDays(-40);
        var features = new List<EmailFeatureVector>
        {
            MakeArchivedFeature("email-expired", LabelThresholds.Archive30d, receivedDate),
        };

        _archiveService.Setup(x => x.GetAllFeaturesAsync(null, It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result<IEnumerable<EmailFeatureVector>>.Success(features));
        _emailProvider.Setup(x => x.BatchModifyAsync(It.IsAny<BatchModifyRequest>()))
            .ReturnsAsync(Result<bool>.Success(true));
        SetupConfig();

        var sut = CreateSut();

        // Act
        var result = await sut.RunScanAsync();

        // Assert
        Assert.True(result.IsSuccess);
        Assert.Equal(1, result.Value.ScannedCount);
        Assert.Equal(1, result.Value.DeletedCount);
        Assert.Equal(0, result.Value.SkippedCount);
        Assert.Empty(result.Value.FailedIds);
        _emailProvider.Verify(x => x.BatchModifyAsync(
            It.Is<BatchModifyRequest>(r =>
                r.EmailIds.Contains("email-expired") &&
                r.AddLabelIds != null && r.AddLabelIds.Contains("TRASH"))),
            Times.Once);
    }

    // ── Non-expired email is skipped ─────────────────────────────────────────

    [Fact]
    public async Task RunScanAsync_NonExpiredEmail_IsSkipped()
    {
        // Arrange: email received 10 days ago with 30d label → not expired
        var receivedDate = DateTime.UtcNow.AddDays(-10);
        var features = new List<EmailFeatureVector>
        {
            MakeArchivedFeature("email-recent", LabelThresholds.Archive30d, receivedDate),
        };

        _archiveService.Setup(x => x.GetAllFeaturesAsync(null, It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result<IEnumerable<EmailFeatureVector>>.Success(features));
        SetupConfig();

        var sut = CreateSut();

        // Act
        var result = await sut.RunScanAsync();

        // Assert
        Assert.True(result.IsSuccess);
        Assert.Equal(1, result.Value.ScannedCount);
        Assert.Equal(0, result.Value.DeletedCount);
        Assert.Equal(1, result.Value.SkippedCount);
        _emailProvider.Verify(x => x.BatchModifyAsync(It.IsAny<BatchModifyRequest>()), Times.Never);
    }

    // ── Boundary: exactly at threshold → Delete ───────────────────────────────

    [Fact]
    public async Task RunScanAsync_BoundaryAge_ExactlyAtThreshold_IsDeleted()
    {
        // Arrange: email received exactly 30 days ago → boundary, should delete
        var receivedDate = DateTime.UtcNow.AddDays(-30);
        var features = new List<EmailFeatureVector>
        {
            MakeArchivedFeature("email-boundary", LabelThresholds.Archive30d, receivedDate),
        };

        _archiveService.Setup(x => x.GetAllFeaturesAsync(null, It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result<IEnumerable<EmailFeatureVector>>.Success(features));
        _emailProvider.Setup(x => x.BatchModifyAsync(It.IsAny<BatchModifyRequest>()))
            .ReturnsAsync(Result<bool>.Success(true));
        SetupConfig();

        var sut = CreateSut();

        // Act
        var result = await sut.RunScanAsync();

        // Assert
        Assert.True(result.IsSuccess);
        Assert.Equal(1, result.Value.DeletedCount);
    }

    // ── training_label is never modified ─────────────────────────────────────

    [Fact]
    public async Task RunScanAsync_NeverCallsSetTrainingLabelAsync()
    {
        var features = new List<EmailFeatureVector>
        {
            MakeArchivedFeature("email-1", LabelThresholds.Archive30d, DateTime.UtcNow.AddDays(-40)),
        };

        _archiveService.Setup(x => x.GetAllFeaturesAsync(null, It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result<IEnumerable<EmailFeatureVector>>.Success(features));
        _emailProvider.Setup(x => x.BatchModifyAsync(It.IsAny<BatchModifyRequest>()))
            .ReturnsAsync(Result<bool>.Success(true));
        SetupConfig();

        var sut = CreateSut();
        await sut.RunScanAsync();

        _archiveService.Verify(x => x.SetTrainingLabelAsync(
            It.IsAny<string>(), It.IsAny<string>(), It.IsAny<bool>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }

    // ── Per-email failure accumulates in FailedIds, scan continues ─────────────

    [Fact]
    public async Task RunScanAsync_GmailFailureOnOneEmail_AccumulatesFailedIdAndContinues()
    {
        var features = new List<EmailFeatureVector>
        {
            MakeArchivedFeature("email-fail", LabelThresholds.Archive30d, DateTime.UtcNow.AddDays(-40)),
            MakeArchivedFeature("email-ok",   LabelThresholds.Archive30d, DateTime.UtcNow.AddDays(-40)),
        };

        _archiveService.Setup(x => x.GetAllFeaturesAsync(null, It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result<IEnumerable<EmailFeatureVector>>.Success(features));

        _emailProvider.Setup(x => x.BatchModifyAsync(It.Is<BatchModifyRequest>(r =>
                r.EmailIds.Contains("email-fail"))))
            .ReturnsAsync(Result<bool>.Failure(new NetworkError("Gmail unavailable")));

        _emailProvider.Setup(x => x.BatchModifyAsync(It.Is<BatchModifyRequest>(r =>
                r.EmailIds.Contains("email-ok"))))
            .ReturnsAsync(Result<bool>.Success(true));

        SetupConfig();

        var sut = CreateSut();
        var result = await sut.RunScanAsync();

        Assert.True(result.IsSuccess);
        Assert.Equal(2, result.Value.ScannedCount);
        Assert.Equal(1, result.Value.DeletedCount);
        Assert.Single(result.Value.FailedIds);
        Assert.Contains("email-fail", result.Value.FailedIds);
    }

    // ── last_scan_utc is persisted on completion ──────────────────────────────

    [Fact]
    public async Task RunScanAsync_PersistsLastScanUtcOnCompletion()
    {
        _archiveService.Setup(x => x.GetAllFeaturesAsync(null, It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result<IEnumerable<EmailFeatureVector>>.Success(
                Enumerable.Empty<EmailFeatureVector>()));
        SetupConfig();

        var sut = CreateSut();
        await sut.RunScanAsync();

        _configService.Verify(x => x.UpdateProcessingSettingsAsync(
            It.Is<ProcessingSettings>(s => s.Retention.LastScanUtc.HasValue),
            It.IsAny<CancellationToken>()),
            Times.Once);
    }

    // ── ShouldPromptAsync ─────────────────────────────────────────────────────

    [Fact]
    public async Task ShouldPromptAsync_NeverScanned_ReturnsTrue()
    {
        SetupConfig(lastScanUtc: null);
        var sut = CreateSut(promptThresholdDays: 7);

        var result = await sut.ShouldPromptAsync();

        Assert.True(result.IsSuccess);
        Assert.True(result.Value);
    }

    [Fact]
    public async Task ShouldPromptAsync_RecentScan_ReturnsFalse()
    {
        SetupConfig(lastScanUtc: DateTime.UtcNow.AddDays(-3));
        var sut = CreateSut(promptThresholdDays: 7);

        var result = await sut.ShouldPromptAsync();

        Assert.True(result.IsSuccess);
        Assert.False(result.Value);
    }

    [Fact]
    public async Task ShouldPromptAsync_OldScan_ReturnsTrue()
    {
        SetupConfig(lastScanUtc: DateTime.UtcNow.AddDays(-10));
        var sut = CreateSut(promptThresholdDays: 7);

        var result = await sut.ShouldPromptAsync();

        Assert.True(result.IsSuccess);
        Assert.True(result.Value);
    }
}
