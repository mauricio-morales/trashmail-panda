using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Moq;
using TrashMailPanda.Providers.ML;
using TrashMailPanda.Providers.ML.Config;
using TrashMailPanda.Providers.Storage;
using TrashMailPanda.Providers.Storage.Models;
using TrashMailPanda.Services;
using TrashMailPanda.Shared;
using TrashMailPanda.Shared.Base;
using Xunit;

namespace TrashMailPanda.Tests.Unit.Services;

/// <summary>
/// Tests for US1 age-at-execution routing in <see cref="EmailTriageService"/>.
/// Verifies that time-bounded labels route to Archive or Delete based on current email age,
/// and that training_label is always the content-based label regardless of the Gmail action taken.
/// </summary>
[Trait("Category", "Unit")]
public class EmailTriageServiceRetentionTests
{
    private readonly Mock<IEmailProvider> _emailProvider = new();
    private readonly Mock<IMLModelProvider> _mlProvider = new();
    private readonly Mock<IEmailArchiveService> _archiveService = new();
    private readonly Mock<ILogger<EmailTriageService>> _logger = new();

    private EmailTriageService CreateSut() =>
        new(_emailProvider.Object,
            _mlProvider.Object,
            _archiveService.Object,
            Options.Create(new MLModelProviderConfig { MinTrainingSamples = 100 }),
            _logger.Object);

    private void SetupBatchModify(bool succeeds = true)
    {
        _emailProvider.Setup(x => x.BatchModifyAsync(It.IsAny<BatchModifyRequest>()))
            .ReturnsAsync(succeeds
                ? Result<bool>.Success(true)
                : Result<bool>.Failure(new NetworkError("Gmail error")));
    }

    private void SetupTrainingLabel()
    {
        _archiveService.Setup(x => x.SetTrainingLabelAsync(
                It.IsAny<string>(), It.IsAny<string>(), It.IsAny<bool>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result<bool>.Success(true));
    }

    // ── Under-threshold → Archive ─────────────────────────────────────────────

    [Theory]
    [InlineData("Archive for 30d", 29, "INBOX")]   // 29d < 30d  → Archive (remove INBOX)
    [InlineData("Archive for 1y", 364, "INBOX")]   // 364d < 365d → Archive
    [InlineData("Archive for 5y", 1824, "INBOX")]   // 1824d < 1825d → Archive
    public async Task ApplyDecisionAsync_UnderThreshold_CallsApplyArchive(
        string action, int ageDays, string removedLabel)
    {
        // Arrange
        var receivedDate = DateTime.UtcNow - TimeSpan.FromDays(ageDays);
        BatchModifyRequest? capturedRequest = null;

        _emailProvider.Setup(x => x.BatchModifyAsync(It.IsAny<BatchModifyRequest>()))
            .Callback<BatchModifyRequest>(r => capturedRequest = r)
            .ReturnsAsync(Result<bool>.Success(true));
        SetupTrainingLabel();

        var sut = CreateSut();

        // Act
        var result = await sut.ApplyDecisionAsync(
            "email-1", action, null,
            forceUserCorrected: false,
            receivedDateUtc: receivedDate);

        // Assert
        Assert.True(result.IsSuccess);
        Assert.NotNull(capturedRequest);
        Assert.Contains(removedLabel, capturedRequest!.RemoveLabelIds ?? []);
        Assert.DoesNotContain("TRASH", capturedRequest.AddLabelIds ?? []);
    }

    // ── Over/at-threshold → Delete ────────────────────────────────────────────

    [Theory]
    [InlineData("Archive for 30d", 30)]   // exactly at threshold → Delete
    [InlineData("Archive for 30d", 60)]   // over threshold → Delete
    [InlineData("Archive for 1y", 365)]   // boundary → Delete
    [InlineData("Archive for 5y", 1825)]   // boundary → Delete
    public async Task ApplyDecisionAsync_AtOrOverThreshold_CallsApplyDelete(
        string action, int ageDays)
    {
        // Arrange
        var receivedDate = DateTime.UtcNow - TimeSpan.FromDays(ageDays);
        BatchModifyRequest? capturedRequest = null;

        _emailProvider.Setup(x => x.BatchModifyAsync(It.IsAny<BatchModifyRequest>()))
            .Callback<BatchModifyRequest>(r => capturedRequest = r)
            .ReturnsAsync(Result<bool>.Success(true));
        SetupTrainingLabel();

        var sut = CreateSut();

        // Act
        var result = await sut.ApplyDecisionAsync(
            "email-1", action, null,
            forceUserCorrected: false,
            receivedDateUtc: receivedDate);

        // Assert
        Assert.True(result.IsSuccess);
        Assert.NotNull(capturedRequest);
        Assert.Contains("TRASH", capturedRequest!.AddLabelIds ?? []);
    }

    // ── training_label is always the content label ────────────────────────────

    [Theory]
    [InlineData("Archive for 30d", 10)]   // under-threshold → Archive action
    [InlineData("Archive for 30d", 100)]   // over-threshold → Delete action
    public async Task ApplyDecisionAsync_TrainingLabelIsAlwaysContentLabel(
        string action, int ageDays)
    {
        // Arrange
        var receivedDate = DateTime.UtcNow - TimeSpan.FromDays(ageDays);
        string? capturedTrainingLabel = null;

        SetupBatchModify(succeeds: true);
        _archiveService.Setup(x => x.SetTrainingLabelAsync(
                It.IsAny<string>(), It.IsAny<string>(), It.IsAny<bool>(), It.IsAny<CancellationToken>()))
            .Callback<string, string, bool, CancellationToken>((_, label, _, _) => capturedTrainingLabel = label)
            .ReturnsAsync(Result<bool>.Success(true));

        var sut = CreateSut();

        // Act
        await sut.ApplyDecisionAsync(
            "email-1", action, null,
            receivedDateUtc: receivedDate);

        // Assert: training label stored is the chosen content-based action, never "Delete"
        Assert.Equal(action, capturedTrainingLabel);
    }

    // ── Null ReceivedDateUtc → safe fallback to Archive ──────────────────────

    [Theory]
    [InlineData("Archive for 30d")]
    [InlineData("Archive for 1y")]
    [InlineData("Archive for 5y")]
    public async Task ApplyDecisionAsync_NullReceivedDate_FallsBackToArchive(string action)
    {
        // Arrange
        BatchModifyRequest? capturedRequest = null;

        _emailProvider.Setup(x => x.BatchModifyAsync(It.IsAny<BatchModifyRequest>()))
            .Callback<BatchModifyRequest>(r => capturedRequest = r)
            .ReturnsAsync(Result<bool>.Success(true));
        SetupTrainingLabel();

        var sut = CreateSut();

        // Act
        var result = await sut.ApplyDecisionAsync(
            "email-1", action, null,
            receivedDateUtc: null);   // <-- no date

        // Assert: Archive (remove INBOX), NOT Delete (TRASH)
        Assert.True(result.IsSuccess);
        Assert.NotNull(capturedRequest);
        Assert.DoesNotContain("TRASH", capturedRequest!.AddLabelIds ?? []);
        Assert.Contains("INBOX", capturedRequest.RemoveLabelIds ?? []);
    }
}
