using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Moq;
using TrashMailPanda.Models.Console;
using TrashMailPanda.Providers.ML;
using TrashMailPanda.Providers.ML.Config;
using TrashMailPanda.Providers.ML.Models;
using TrashMailPanda.Providers.Storage;
using ModelVersion = TrashMailPanda.Providers.ML.Models.ModelVersion;
using TrashMailPanda.Providers.Storage.Models;
using TrashMailPanda.Services;
using TrashMailPanda.Shared;
using TrashMailPanda.Shared.Base;
using Xunit;

namespace TrashMailPanda.Tests.Unit.Services;

[Trait("Category", "Unit")]
public class EmailTriageServiceTests
{
    private readonly Mock<IEmailProvider> _emailProvider = new();
    private readonly Mock<IMLModelProvider> _mlProvider = new();
    private readonly Mock<IEmailArchiveService> _archiveService = new();
    private readonly Mock<ILogger<EmailTriageService>> _logger = new();

    private EmailTriageService CreateSut(int minSamples = 100)
    {
        var config = Options.Create(new MLModelProviderConfig { MinTrainingSamples = minSamples });
        return new EmailTriageService(
            _emailProvider.Object,
            _mlProvider.Object,
            _archiveService.Object,
            config,
            _logger.Object);
    }

    // ──────────────────────────────────────────────────────────────────────────
    // GetSessionInfoAsync
    // ──────────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task GetSessionInfoAsync_WhenModelUnavailable_ReturnsColdStartMode()
    {
        // Arrange
        _mlProvider.Setup(x => x.GetActiveModelVersionAsync("action", It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result<ModelVersion>.Failure(new InitializationError("No model")));
        _archiveService.Setup(x => x.CountLabeledAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result<int>.Success(0));

        var sut = CreateSut(minSamples: 100);

        // Act
        var result = await sut.GetSessionInfoAsync();

        // Assert
        Assert.True(result.IsSuccess);
        Assert.Equal(TriageMode.ColdStart, result.Value.Mode);
    }

    [Fact]
    public async Task GetSessionInfoAsync_WhenModelAvailable_ReturnsAiAssistedMode()
    {
        // Arrange
        _mlProvider.Setup(x => x.GetActiveModelVersionAsync("action", It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result<ModelVersion>.Success(new ModelVersion { ModelType = "action", Version = 1 }));
        _archiveService.Setup(x => x.CountLabeledAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result<int>.Success(150));

        var sut = CreateSut(minSamples: 100);

        // Act
        var result = await sut.GetSessionInfoAsync();

        // Assert
        Assert.True(result.IsSuccess);
        Assert.Equal(TriageMode.AiAssisted, result.Value.Mode);
        Assert.Equal(150, result.Value.LabeledCount);
        Assert.True(result.Value.ThresholdAlreadyReached);
    }

    [Fact]
    public async Task GetSessionInfoAsync_WhenBelowThreshold_ThresholdNotReached()
    {
        // Arrange
        _mlProvider.Setup(x => x.GetActiveModelVersionAsync("action", It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result<ModelVersion>.Failure(new InitializationError("No model")));
        _archiveService.Setup(x => x.CountLabeledAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result<int>.Success(42));

        var sut = CreateSut(minSamples: 100);

        // Act
        var result = await sut.GetSessionInfoAsync();

        // Assert
        Assert.True(result.IsSuccess);
        Assert.Equal(42, result.Value.LabeledCount);
        Assert.Equal(100, result.Value.LabelingThreshold);
        Assert.False(result.Value.ThresholdAlreadyReached);
    }

    // ──────────────────────────────────────────────────────────────────────────
    // GetNextBatchAsync
    // ──────────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task GetNextBatchAsync_DelegatesTo_ArchiveService()
    {
        // Arrange
        var features = new List<EmailFeatureVector> { new() { EmailId = "msg1" } };
        _archiveService.Setup(x => x.GetUntriagedAsync(5, 0, It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result<IReadOnlyList<EmailFeatureVector>>.Success(features));

        var sut = CreateSut();

        // Act
        var result = await sut.GetNextBatchAsync(5, 0);

        // Assert
        Assert.True(result.IsSuccess);
        Assert.Single(result.Value);
        Assert.Equal("msg1", result.Value[0].EmailId);
    }

    // ──────────────────────────────────────────────────────────────────────────
    // GetAiRecommendationAsync
    // ──────────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task GetAiRecommendationAsync_WhenColdStart_ReturnsNullPrediction()
    {
        // Arrange — model not available → ColdStart
        _mlProvider.Setup(x => x.GetActiveModelVersionAsync("action", It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result<ModelVersion>.Failure(new InitializationError("No model")));
        _archiveService.Setup(x => x.CountLabeledAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result<int>.Success(0));

        var sut = CreateSut();
        var feature = new EmailFeatureVector { EmailId = "msg1" };

        // Act
        var result = await sut.GetAiRecommendationAsync(feature);

        // Assert
        Assert.True(result.IsSuccess);
        Assert.Null(result.Value);
    }

    [Fact]
    public async Task GetAiRecommendationAsync_WhenAiAssisted_ReturnsPrediction()
    {
        // Arrange — model available → AiAssisted
        _mlProvider.Setup(x => x.GetActiveModelVersionAsync("action", It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result<ModelVersion>.Success(new ModelVersion { ModelType = "action", Version = 1 }));
        _archiveService.Setup(x => x.CountLabeledAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result<int>.Success(200));

        var prediction = new ActionPrediction { PredictedLabel = "Archive", Confidence = 0.85f };
        _mlProvider.Setup(x => x.ClassifyActionAsync(It.IsAny<EmailFeatureVector>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result<ActionPrediction>.Success(prediction));

        var sut = CreateSut();
        var feature = new EmailFeatureVector { EmailId = "msg1" };

        // Act
        var result = await sut.GetAiRecommendationAsync(feature);

        // Assert
        Assert.True(result.IsSuccess);
        Assert.NotNull(result.Value);
        Assert.Equal("Archive", result.Value!.PredictedLabel);
        Assert.Equal(0.85f, result.Value.Confidence);
    }

    // ──────────────────────────────────────────────────────────────────────────
    // ApplyDecisionAsync — dual-write contract
    // ──────────────────────────────────────────────────────────────────────────

    [Theory]
    [InlineData("Keep")]
    [InlineData("Archive")]
    [InlineData("Delete")]
    public async Task ApplyDecisionAsync_CallsBatchModify_ThenStoresLabel(string action)
    {
        // Arrange
        _emailProvider.Setup(x => x.BatchModifyAsync(It.IsAny<BatchModifyRequest>()))
            .ReturnsAsync(Result<bool>.Success(true));
        _archiveService.Setup(x => x.SetTrainingLabelAsync(
                "msg1", action, It.IsAny<bool>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result<bool>.Success(true));

        var sut = CreateSut();

        // Act
        var result = await sut.ApplyDecisionAsync("msg1", action, null);

        // Assert
        Assert.True(result.IsSuccess);
        Assert.Equal("msg1", result.Value.EmailId);
        Assert.Equal(action, result.Value.ChosenAction);
        _emailProvider.Verify(x => x.BatchModifyAsync(It.IsAny<BatchModifyRequest>()), Times.Once);
        _archiveService.Verify(x => x.SetTrainingLabelAsync("msg1", action, false, It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task ApplyDecisionAsync_Spam_CallsReportSpam()
    {
        // Arrange
        _emailProvider.Setup(x => x.ReportSpamAsync("msg1"))
            .ReturnsAsync(Result<bool>.Success(true));
        _archiveService.Setup(x => x.SetTrainingLabelAsync(
                It.IsAny<string>(), It.IsAny<string>(), It.IsAny<bool>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result<bool>.Success(true));

        var sut = CreateSut();

        // Act
        var result = await sut.ApplyDecisionAsync("msg1", "Spam", null);

        // Assert
        Assert.True(result.IsSuccess);
        _emailProvider.Verify(x => x.ReportSpamAsync("msg1"), Times.Once);
        _emailProvider.Verify(x => x.BatchModifyAsync(It.IsAny<BatchModifyRequest>()), Times.Never);
    }

    [Fact]
    public async Task ApplyDecisionAsync_WhenGmailFails_DoesNotStoreLabel()
    {
        // Arrange — Gmail action fails
        _emailProvider.Setup(x => x.BatchModifyAsync(It.IsAny<BatchModifyRequest>()))
            .ReturnsAsync(Result<bool>.Failure(new NetworkError("Network error")));

        var sut = CreateSut();

        // Act
        var result = await sut.ApplyDecisionAsync("msg1", "Keep", null);

        // Assert
        Assert.False(result.IsSuccess);
        _archiveService.Verify(
            x => x.SetTrainingLabelAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<bool>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }

    [Fact]
    public async Task ApplyDecisionAsync_WhenAiRecDiffersFromChosen_IsOverrideTrue()
    {
        // Arrange
        _emailProvider.Setup(x => x.BatchModifyAsync(It.IsAny<BatchModifyRequest>()))
            .ReturnsAsync(Result<bool>.Success(true));
        _archiveService.Setup(x => x.SetTrainingLabelAsync(
                It.IsAny<string>(), It.IsAny<string>(), It.IsAny<bool>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result<bool>.Success(true));

        var sut = CreateSut();

        // Act — AI said "Archive", user chose "Delete"
        var result = await sut.ApplyDecisionAsync("msg1", "Delete", "Archive");

        // Assert
        Assert.True(result.IsSuccess);
        Assert.True(result.Value.IsOverride);
        _archiveService.Verify(x => x.SetTrainingLabelAsync("msg1", "Delete", true, It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task ApplyDecisionAsync_WhenAiRecMatchesChosen_IsOverrideFalse()
    {
        // Arrange
        _emailProvider.Setup(x => x.BatchModifyAsync(It.IsAny<BatchModifyRequest>()))
            .ReturnsAsync(Result<bool>.Success(true));
        _archiveService.Setup(x => x.SetTrainingLabelAsync(
                It.IsAny<string>(), It.IsAny<string>(), It.IsAny<bool>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result<bool>.Success(true));

        var sut = CreateSut();

        // Act — AI said "Keep", user also chose "Keep"
        var result = await sut.ApplyDecisionAsync("msg1", "Keep", "Keep");

        // Assert
        Assert.True(result.IsSuccess);
        Assert.False(result.Value.IsOverride);
    }

    [Fact]
    public async Task ApplyDecisionAsync_UnknownAction_ReturnsFailure()
    {
        var sut = CreateSut();

        var result = await sut.ApplyDecisionAsync("msg1", "UnknownAction", null);

        Assert.False(result.IsSuccess);
    }

    [Fact]
    public async Task ApplyDecisionAsync_Delete_MovesToTrash()
    {
        // Arrange
        BatchModifyRequest? captured = null;
        _emailProvider.Setup(x => x.BatchModifyAsync(It.IsAny<BatchModifyRequest>()))
            .Callback<BatchModifyRequest>(r => captured = r)
            .ReturnsAsync(Result<bool>.Success(true));
        _archiveService.Setup(x => x.SetTrainingLabelAsync(
                It.IsAny<string>(), It.IsAny<string>(), It.IsAny<bool>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result<bool>.Success(true));

        var sut = CreateSut();

        // Act
        await sut.ApplyDecisionAsync("msg1", "Delete", null);

        // Assert — TRASH label added, INBOX removed
        Assert.NotNull(captured);
        Assert.Contains("TRASH", captured!.AddLabelIds ?? []);
        Assert.Contains("INBOX", captured.RemoveLabelIds ?? []);
    }

    [Fact]
    public async Task ApplyDecisionAsync_Archive_RemovesInboxOnly()
    {
        // Arrange
        BatchModifyRequest? captured = null;
        _emailProvider.Setup(x => x.BatchModifyAsync(It.IsAny<BatchModifyRequest>()))
            .Callback<BatchModifyRequest>(r => captured = r)
            .ReturnsAsync(Result<bool>.Success(true));
        _archiveService.Setup(x => x.SetTrainingLabelAsync(
                It.IsAny<string>(), It.IsAny<string>(), It.IsAny<bool>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result<bool>.Success(true));

        var sut = CreateSut();

        // Act
        await sut.ApplyDecisionAsync("msg1", "Archive", null);

        // Assert — only INBOX removed, no labels added
        Assert.NotNull(captured);
        Assert.Contains("INBOX", captured!.RemoveLabelIds ?? []);
        Assert.Null(captured.AddLabelIds);
    }
}
