using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using TrashMailPanda.Models;
using TrashMailPanda.Models.Console;
using TrashMailPanda.Providers.Storage.Models;
using TrashMailPanda.Providers.Storage.Services;
using TrashMailPanda.Services;
using TrashMailPanda.Shared;
using TrashMailPanda.Shared.Base;
using Xunit;

namespace TrashMailPanda.Tests.Unit.Services;

[Trait("Category", "Unit")]
public class AutoApplyServiceTests
{
    private readonly Mock<IConfigurationService> _configService = new();

    private AutoApplyService CreateSut() =>
        new(_configService.Object, NullLogger<AutoApplyService>.Instance);

    // ──────────────────────────────────────────────────────────────────────────
    // ShouldAutoApply — threshold boundary
    // ──────────────────────────────────────────────────────────────────────────

    [Fact]
    public void ShouldAutoApply_AtExactThreshold_ReturnsTrue()
    {
        var config = new AutoApplyConfig { Enabled = true, ConfidenceThreshold = 0.95f };
        var classification = new ClassificationResult
        {
            EmailId = "e1",
            PredictedAction = "Archive",
            Confidence = 0.95f,
            ReasoningSource = ReasoningSource.ML,
        };

        var sut = CreateSut();

        Assert.True(sut.ShouldAutoApply(config, classification));
    }

    [Fact]
    public void ShouldAutoApply_BelowThresholdBySmallMargin_ReturnsFalse()
    {
        var config = new AutoApplyConfig { Enabled = true, ConfidenceThreshold = 0.95f };
        var classification = new ClassificationResult
        {
            EmailId = "e1",
            PredictedAction = "Archive",
            Confidence = 0.949f, // just under
            ReasoningSource = ReasoningSource.ML,
        };

        var sut = CreateSut();

        Assert.False(sut.ShouldAutoApply(config, classification));
    }

    [Fact]
    public void ShouldAutoApply_AboveThreshold_ReturnsTrue()
    {
        var config = new AutoApplyConfig { Enabled = true, ConfidenceThreshold = 0.95f };
        var classification = new ClassificationResult
        {
            EmailId = "e1",
            PredictedAction = "Keep",
            Confidence = 0.99f,
            ReasoningSource = ReasoningSource.ML,
        };

        var sut = CreateSut();

        Assert.True(sut.ShouldAutoApply(config, classification));
    }

    [Fact]
    public void ShouldAutoApply_WhenDisabled_ReturnsFalse()
    {
        var config = new AutoApplyConfig { Enabled = false, ConfidenceThreshold = 0.80f };
        var classification = new ClassificationResult
        {
            EmailId = "e1",
            PredictedAction = "Archive",
            Confidence = 0.99f, // high confidence, but disabled
            ReasoningSource = ReasoningSource.ML,
        };

        var sut = CreateSut();

        Assert.False(sut.ShouldAutoApply(config, classification));
    }

    [Fact]
    public void ShouldAutoApply_NullConfig_ReturnsFalse()
    {
        var classification = new ClassificationResult
        {
            EmailId = "e1",
            PredictedAction = "Archive",
            Confidence = 0.99f,
            ReasoningSource = ReasoningSource.ML,
        };

        var sut = CreateSut();

        Assert.False(sut.ShouldAutoApply(null!, classification));
    }

    [Fact]
    public void ShouldAutoApply_NullClassification_ReturnsFalse()
    {
        var config = new AutoApplyConfig { Enabled = true, ConfidenceThreshold = 0.95f };

        var sut = CreateSut();

        Assert.False(sut.ShouldAutoApply(config, null!));
    }

    // ──────────────────────────────────────────────────────────────────────────
    // IsActionRedundant — per-action matrix
    // ──────────────────────────────────────────────────────────────────────────

    [Fact]
    public void IsActionRedundant_Archive_WhenAlreadyArchived_ReturnsTrue()
    {
        var feature = new EmailFeatureVector { IsArchived = 1, IsInInbox = 0 };
        Assert.True(CreateSut().IsActionRedundant("Archive", feature));
    }

    [Fact]
    public void IsActionRedundant_Archive_WhenInInbox_ReturnsFalse()
    {
        var feature = new EmailFeatureVector { IsArchived = 0, IsInInbox = 1 };
        Assert.False(CreateSut().IsActionRedundant("Archive", feature));
    }

    [Fact]
    public void IsActionRedundant_Keep_WhenAlreadyInInbox_ReturnsTrue()
    {
        var feature = new EmailFeatureVector { IsInInbox = 1 };
        Assert.True(CreateSut().IsActionRedundant("Keep", feature));
    }

    [Fact]
    public void IsActionRedundant_Keep_WhenNotInInbox_ReturnsFalse()
    {
        var feature = new EmailFeatureVector { IsInInbox = 0 };
        Assert.False(CreateSut().IsActionRedundant("Keep", feature));
    }

    [Fact]
    public void IsActionRedundant_Delete_WhenAlreadyInTrash_ReturnsTrue()
    {
        var feature = new EmailFeatureVector { WasInTrash = 1 };
        Assert.True(CreateSut().IsActionRedundant("Delete", feature));
    }

    [Fact]
    public void IsActionRedundant_Delete_WhenNotInTrash_ReturnsFalse()
    {
        var feature = new EmailFeatureVector { WasInTrash = 0 };
        Assert.False(CreateSut().IsActionRedundant("Delete", feature));
    }

    [Fact]
    public void IsActionRedundant_Spam_WhenAlreadyInSpam_ReturnsTrue()
    {
        var feature = new EmailFeatureVector { WasInSpam = 1 };
        Assert.True(CreateSut().IsActionRedundant("Spam", feature));
    }

    [Fact]
    public void IsActionRedundant_Spam_WhenNotInSpam_ReturnsFalse()
    {
        var feature = new EmailFeatureVector { WasInSpam = 0 };
        Assert.False(CreateSut().IsActionRedundant("Spam", feature));
    }

    [Fact]
    public void IsActionRedundant_UnknownAction_ReturnsFalse()
    {
        var feature = new EmailFeatureVector { IsInInbox = 1, IsArchived = 1 };
        Assert.False(CreateSut().IsActionRedundant("SomeOtherAction", feature));
    }

    [Fact]
    public void IsActionRedundant_NullFeature_ReturnsFalse()
    {
        Assert.False(CreateSut().IsActionRedundant("Archive", null!));
    }

    [Fact]
    public void IsActionRedundant_EmptyAction_ReturnsFalse()
    {
        var feature = new EmailFeatureVector { IsArchived = 1, IsInInbox = 0 };
        Assert.False(CreateSut().IsActionRedundant(string.Empty, feature));
    }

    // ──────────────────────────────────────────────────────────────────────────
    // Session log: LogAutoApply / GetSessionLog / ResetSession
    // ──────────────────────────────────────────────────────────────────────────

    [Fact]
    public void GetSessionLog_Initially_IsEmpty()
    {
        var sut = CreateSut();
        Assert.Empty(sut.GetSessionLog());
    }

    [Fact]
    public void LogAutoApply_AddsEntry_ToSessionLog()
    {
        var sut = CreateSut();
        var entry = new AutoApplyLogEntry("e1", "example.com", "Hello", "Archive", 0.97f, DateTime.UtcNow, false);

        sut.LogAutoApply(entry);

        Assert.Single(sut.GetSessionLog());
        Assert.Same(entry, sut.GetSessionLog()[0]);
    }

    [Fact]
    public void LogAutoApply_NullEntry_DoesNotThrow()
    {
        var sut = CreateSut();
        sut.LogAutoApply(null!); // should not throw
        Assert.Empty(sut.GetSessionLog());
    }

    [Fact]
    public void ResetSession_ClearsLog()
    {
        var sut = CreateSut();
        sut.LogAutoApply(new AutoApplyLogEntry("e1", "d.com", "Sub", "Keep", 0.96f, DateTime.UtcNow, false));

        sut.ResetSession();

        Assert.Empty(sut.GetSessionLog());
    }

    // ──────────────────────────────────────────────────────────────────────────
    // GetSessionSummary — aggregate counts
    // ──────────────────────────────────────────────────────────────────────────

    [Fact]
    public void GetSessionSummary_NoEntries_ReturnsAllZeroStats()
    {
        var sut = CreateSut();

        var summary = sut.GetSessionSummary(totalManuallyReviewed: 5);

        Assert.Equal(0, summary.TotalAutoApplied);
        Assert.Equal(5, summary.TotalManuallyReviewed);
        Assert.Equal(0, summary.TotalRedundant);
        Assert.Equal(0, summary.TotalUndone);
        Assert.Empty(summary.PerActionCounts);
    }

    [Fact]
    public void GetSessionSummary_MixedEntries_AggregatesCorrectly()
    {
        var sut = CreateSut();
        var t = DateTime.UtcNow;

        sut.LogAutoApply(new AutoApplyLogEntry("e1", "d.com", "S1", "Archive", 0.98f, t, WasRedundant: false));
        sut.LogAutoApply(new AutoApplyLogEntry("e2", "d.com", "S2", "Archive", 0.97f, t, WasRedundant: true));
        var undoneEntry = new AutoApplyLogEntry("e3", "d.com", "S3", "Keep", 0.95f, t, WasRedundant: false);
        undoneEntry.Undone = true;
        sut.LogAutoApply(undoneEntry);
        sut.LogAutoApply(new AutoApplyLogEntry("e4", "d.com", "S4", "Delete", 0.96f, t, WasRedundant: false));

        var summary = sut.GetSessionSummary(totalManuallyReviewed: 3);

        Assert.Equal(4, summary.TotalAutoApplied);
        Assert.Equal(3, summary.TotalManuallyReviewed);
        Assert.Equal(1, summary.TotalRedundant);
        Assert.Equal(1, summary.TotalUndone);
        Assert.Equal(2, summary.PerActionCounts["Archive"]);
        Assert.Equal(1, summary.PerActionCounts["Keep"]);
        Assert.Equal(1, summary.PerActionCounts["Delete"]);
    }

    // ──────────────────────────────────────────────────────────────────────────
    // GetConfigAsync / SaveConfigAsync — IConfigurationService integration
    // ──────────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task GetConfigAsync_MapsSettingsToAutoApplyConfig()
    {
        var appConfig = new AppConfig
        {
            ProcessingSettings = new ProcessingSettings
            {
                AutoApply = new AutoApplySettings { Enabled = true, ConfidenceThreshold = 0.90f },
            },
        };
        _configService.Setup(x => x.GetConfigAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result<AppConfig>.Success(appConfig));

        var result = await CreateSut().GetConfigAsync();

        Assert.True(result.IsSuccess);
        Assert.True(result.Value.Enabled);
        Assert.Equal(0.90f, result.Value.ConfidenceThreshold, 5);
    }

    [Fact]
    public async Task GetConfigAsync_WhenConfigServiceFails_ReturnsFailure()
    {
        _configService.Setup(x => x.GetConfigAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result<AppConfig>.Failure(new StorageError("DB error")));

        var result = await CreateSut().GetConfigAsync();

        Assert.False(result.IsSuccess);
    }

    [Fact]
    public async Task GetConfigAsync_NullProcessingSettings_ReturnsDefaultConfig()
    {
        var appConfig = new AppConfig { ProcessingSettings = null };
        _configService.Setup(x => x.GetConfigAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result<AppConfig>.Success(appConfig));

        var result = await CreateSut().GetConfigAsync();

        Assert.True(result.IsSuccess);
        Assert.False(result.Value.Enabled); // default
        Assert.Equal(0.95f, result.Value.ConfidenceThreshold, 5); // default
    }

    [Fact]
    public async Task SaveConfigAsync_PersistsEnabledAndThreshold()
    {
        var appConfig = new AppConfig { ProcessingSettings = new ProcessingSettings() };
        _configService.Setup(x => x.GetConfigAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result<AppConfig>.Success(appConfig));
        _configService.Setup(x => x.UpdateProcessingSettingsAsync(
                It.IsAny<ProcessingSettings>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result<bool>.Success(true));

        var newConfig = new AutoApplyConfig { Enabled = true, ConfidenceThreshold = 0.88f };
        var result = await CreateSut().SaveConfigAsync(newConfig);

        Assert.True(result.IsSuccess);
        _configService.Verify(x => x.UpdateProcessingSettingsAsync(
            It.Is<ProcessingSettings>(s => s.AutoApply.Enabled && s.AutoApply.ConfidenceThreshold == 0.88f),
            It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task SaveConfigAsync_NullConfig_ReturnsValidationFailure()
    {
        var result = await CreateSut().SaveConfigAsync(null!);

        Assert.False(result.IsSuccess);
        Assert.IsType<ValidationError>(result.Error);
    }
}
