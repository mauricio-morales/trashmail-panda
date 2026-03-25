using System;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Moq;
using TrashMailPanda.Providers.ML;
using TrashMailPanda.Shared;
using TrashMailPanda.Providers.ML.Config;
using TrashMailPanda.Providers.ML.Models;
using TrashMailPanda.Providers.Storage;
using TrashMailPanda.Providers.Storage.Models;
using TrashMailPanda.Services;
using TrashMailPanda.Shared.Base;
using Xunit;

namespace TrashMailPanda.Tests.Unit.Services;

/// <summary>
/// Tests for US3: at inference time, <see cref="EmailTriageService.GetAiRecommendationAsync"/>
/// must compute <see cref="EmailFeatureVector.EmailAgeDays"/> fresh from
/// <see cref="EmailFeatureVector.ReceivedDateUtc"/>, not use the stored (stale) value.
/// </summary>
[Trait("Category", "Unit")]
public class EmailClassificationInferenceTests
{
    private readonly Mock<IEmailProvider> _emailProvider = new();
    private readonly Mock<IMLModelProvider> _mlProvider = new();
    private readonly Mock<IEmailArchiveService> _archiveService = new();
    private readonly Mock<ILogger<EmailTriageService>> _logger = new();

    private EmailTriageService CreateSut()
    {
        var modelVersion = new ModelVersion { ModelType = "action", Version = 1 };
        _mlProvider.Setup(x => x.GetActiveModelVersionAsync("action", It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result<ModelVersion>.Success(modelVersion));
        _archiveService.Setup(x => x.CountLabeledAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result<int>.Success(200));

        return new EmailTriageService(
            _emailProvider.Object,
            _mlProvider.Object,
            _archiveService.Object,
            Options.Create(new MLModelProviderConfig { MinTrainingSamples = 100 }),
            _logger.Object);
    }

    [Fact]
    public async Task GetAiRecommendationAsync_UsesCurrentAgeFromReceivedDateUtc()
    {
        // Arrange: feature was extracted 1 day ago (EmailAgeDays = 1) but was
        // received 30 days ago. At inference time, EmailAgeDays should be ≈ 30.
        var receivedDate = DateTime.UtcNow.AddDays(-30);
        var feature = new EmailFeatureVector
        {
            EmailId = "email-inference-1",
            SenderDomain = "example.com",
            SpfResult = "pass",
            DkimResult = "pass",
            DmarcResult = "pass",
            SenderFrequency = 1,
            ThreadMessageCount = 1,
            FeatureSchemaVersion = 1,
            EmailAgeDays = 1,        // stale: age at extraction time (1 day ago)
            EmailSizeLog = 8f,
            ExtractedAt = DateTime.UtcNow,
            ReceivedDateUtc = receivedDate,
        };

        EmailFeatureVector? capturedFeature = null;
        _mlProvider.Setup(x => x.ClassifyActionAsync(It.IsAny<EmailFeatureVector>(), It.IsAny<CancellationToken>()))
            .Callback<EmailFeatureVector, CancellationToken>((f, _) => capturedFeature = f)
            .ReturnsAsync(Result<ActionPrediction>.Success(
                new ActionPrediction { PredictedLabel = "Archive", Confidence = 0.9f }));

        var sut = CreateSut();

        // Act
        var result = await sut.GetAiRecommendationAsync(feature);

        // Assert
        Assert.True(result.IsSuccess);
        Assert.NotNull(capturedFeature);

        // EmailAgeDays passed to the ML model should reflect current age (≈ 30 days),
        // not the stale stored value (1 day). Allow ±1 for timing.
        var expectedAge = (int)(DateTime.UtcNow - receivedDate).TotalDays;
        Assert.InRange(capturedFeature!.EmailAgeDays, expectedAge - 1, expectedAge + 1);
        Assert.NotEqual(1, capturedFeature.EmailAgeDays); // definitively not the stale value
    }

    [Fact]
    public async Task GetAiRecommendationAsync_NullReceivedDateUtc_UsesStoredEmailAgeDays()
    {
        // Arrange: feature has no ReceivedDateUtc — stored EmailAgeDays should be used unchanged
        var feature = new EmailFeatureVector
        {
            EmailId = "email-inference-2",
            SenderDomain = "example.com",
            SpfResult = "pass",
            DkimResult = "pass",
            DmarcResult = "pass",
            SenderFrequency = 1,
            ThreadMessageCount = 1,
            FeatureSchemaVersion = 1,
            EmailAgeDays = 45,       // stored value — no ReceivedDateUtc to override it
            EmailSizeLog = 8f,
            ExtractedAt = DateTime.UtcNow,
            ReceivedDateUtc = null,
        };

        EmailFeatureVector? capturedFeature = null;
        _mlProvider.Setup(x => x.ClassifyActionAsync(It.IsAny<EmailFeatureVector>(), It.IsAny<CancellationToken>()))
            .Callback<EmailFeatureVector, CancellationToken>((f, _) => capturedFeature = f)
            .ReturnsAsync(Result<ActionPrediction>.Success(
                new ActionPrediction { PredictedLabel = "Delete", Confidence = 0.85f }));

        var sut = CreateSut();

        // Act
        await sut.GetAiRecommendationAsync(feature);

        // Assert: stored value is unchanged
        Assert.NotNull(capturedFeature);
        Assert.Equal(45, capturedFeature!.EmailAgeDays);
    }
}
