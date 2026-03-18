using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.ML;
using TrashMailPanda.Providers.ML.Models;
using TrashMailPanda.Providers.ML.Training;
using TrashMailPanda.Shared.Base;
using Xunit;

namespace TrashMailPanda.Tests.Unit.ML;

[Trait("Category", "Unit")]
public class ActionModelTrainerTests
{
    private readonly MLContext _mlContext = new(seed: 42);
    private readonly ActionModelTrainer _trainer = new(NullLogger<ActionModelTrainer>.Instance);

    // ──────────────────────────────────────────────────────────────────────────
    // Test data helpers
    // ──────────────────────────────────────────────────────────────────────────

    private static ActionTrainingInput Row(string label) => new()
    {
        Label = label,
        SenderKnown = 1f,
        ContactStrength = 0.5f,
        HasListUnsubscribe = 0f,
        HasAttachments = 0f,
        HourReceived = 10f,
        DayOfWeek = 2f,
        EmailSizeLog = 3f,
        SubjectLength = 20f,
        RecipientCount = 1f,
        IsReply = 0f,
        InUserWhitelist = 0f,
        InUserBlacklist = 0f,
        LabelCount = 2f,
        LinkCount = 1f,
        ImageCount = 0f,
        HasTrackingPixel = 0f,
        UnsubscribeLinkInBody = 0f,
        EmailAgeDays = 1f,
        IsInInbox = 1f,
        IsStarred = 0f,
        IsImportant = 0f,
        WasInTrash = 0f,
        WasInSpam = 0f,
        IsArchived = 0f,
        ThreadMessageCount = 1f,
        SenderFrequency = 5f,
        IsReplied = 0f,
        IsForwarded = 0f,
        SenderDomain = "example.com",
        SpfResult = "pass",
        DkimResult = "pass",
        DmarcResult = "pass",
        SubjectText = "Test email subject",
        BodyTextShort = "Hello test body",
        Weight = 1f,
    };

    private IDataView BuildDataView(IEnumerable<ActionTrainingInput> rows)
        => _mlContext.Data.LoadFromEnumerable(rows);

    // ──────────────────────────────────────────────────────────────────────────
    // Trainer algorithm selection
    // ──────────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task TrainAsync_SelectsLightGbm_WhenDominantClassExceedsThreshold()
    {
        // Dominant class: Keep = 90, Archive = 5, Delete = 3, Spam = 2 → ratio ~= 0.90
        var rows = Enumerable.Repeat(Row("Keep"), 90)
            .Concat(Enumerable.Repeat(Row("Archive"), 5))
            .Concat(Enumerable.Repeat(Row("Delete"), 3))
            .Concat(Enumerable.Repeat(Row("Spam"), 2));

        var dataView = BuildDataView(rows);
        var result = await _trainer.TrainAsync(_mlContext, dataView, dominantClassImbalanceThreshold: 0.80);

        Assert.True(result.IsSuccess, result.IsSuccess ? "" : result.Error.Message);
        Assert.Equal("LightGbm", result.Value.Algorithm);
    }

    [Fact]
    public async Task TrainAsync_SelectsSdca_WhenDominantClassBelowThreshold()
    {
        // Balanced: Keep = 40, Archive = 30, Delete = 20, Spam = 10 → ratio ~= 0.40
        var rows = Enumerable.Repeat(Row("Keep"), 40)
            .Concat(Enumerable.Repeat(Row("Archive"), 30))
            .Concat(Enumerable.Repeat(Row("Delete"), 20))
            .Concat(Enumerable.Repeat(Row("Spam"), 10));

        var dataView = BuildDataView(rows);
        var result = await _trainer.TrainAsync(_mlContext, dataView, dominantClassImbalanceThreshold: 0.80);

        Assert.True(result.IsSuccess, result.IsSuccess ? "" : result.Error.Message);
        Assert.Equal("SdcaMaximumEntropy", result.Value.Algorithm);
    }

    // ──────────────────────────────────────────────────────────────────────────
    // Validation error on single class
    // ──────────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task TrainAsync_ReturnsValidationError_WhenOnlyOneClassPresent()
    {
        // Only "Keep" labels — cannot train a multiclass classifier
        var rows = Enumerable.Repeat(Row("Keep"), 20);
        var dataView = BuildDataView(rows);

        var result = await _trainer.TrainAsync(_mlContext, dataView, dominantClassImbalanceThreshold: 0.80);

        Assert.False(result.IsSuccess);
        Assert.IsType<ValidationError>(result.Error);
        Assert.Contains("2 distinct", result.Error.Message);
    }

    // ──────────────────────────────────────────────────────────────────────────
    // Class weights
    // ──────────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task TrainAsync_ReturnsMetrics_WithTwoClasses()
    {
        // Minimal two-class dataset that should force SdcaMaximumEntropy
        var rows = Enumerable.Repeat(Row("Keep"), 30)
            .Concat(Enumerable.Repeat(Row("Delete"), 30));

        var dataView = BuildDataView(rows);
        var result = await _trainer.TrainAsync(_mlContext, dataView, dominantClassImbalanceThreshold: 0.80);

        Assert.True(result.IsSuccess, result.IsSuccess ? "" : result.Error.Message);
        Assert.NotNull(result.Value.Metrics);
        Assert.True(result.Value.Metrics.MacroAccuracy >= 0.0);
    }
}
