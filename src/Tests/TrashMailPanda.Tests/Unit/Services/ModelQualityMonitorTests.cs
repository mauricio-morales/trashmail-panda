using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using TrashMailPanda.Models.Console;
using TrashMailPanda.Providers.Storage;
using TrashMailPanda.Services;
using TrashMailPanda.Shared.Base;
using Xunit;

namespace TrashMailPanda.Tests.Unit.Services;

[Trait("Category", "Unit")]
public class ModelQualityMonitorTests
{
    private readonly Mock<IEmailArchiveService> _archive = new();

    private ModelQualityMonitor CreateSut()
        => new(_archive.Object, NullLogger<ModelQualityMonitor>.Instance);

    private void SetupCorrections(IReadOnlyDictionary<string, int> corrections)
    {
        _archive.Setup(x => x.GetUserCorrectedCountsByLabelAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result<IReadOnlyDictionary<string, int>>.Success(corrections));
    }

    // ──────────────────────────────────────────────────────────────────────────
    // RecordDecision / rolling window accuracy
    // ──────────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task GetMetrics_NoDecisions_ReturnsFullAccuracy()
    {
        SetupCorrections(new Dictionary<string, int>());
        var sut = CreateSut();

        var result = await sut.GetMetricsAsync();

        Assert.True(result.IsSuccess);
        Assert.Equal(1f, result.Value.RollingAccuracy);
        Assert.Equal(1f, result.Value.OverallAccuracy);
    }

    [Fact]
    public async Task GetMetrics_AllAccepted_Returns100Percent()
    {
        SetupCorrections(new Dictionary<string, int>());
        var sut = CreateSut();

        sut.RecordDecision("Archive", "Archive", false);
        sut.RecordDecision("Keep", "Keep", false);
        sut.RecordDecision("Delete", "Delete", false);

        var result = await sut.GetMetricsAsync();

        Assert.Equal(1f, result.Value.RollingAccuracy, 5);
    }

    [Fact]
    public async Task GetMetrics_SomeOverrides_CalculatesCorrectRollingAccuracy()
    {
        SetupCorrections(new Dictionary<string, int>());
        var sut = CreateSut();

        // 7 accepted, 3 overridden → 70% accuracy
        for (int i = 0; i < 7; i++) sut.RecordDecision("Archive", "Archive", false);
        for (int i = 0; i < 3; i++) sut.RecordDecision("Archive", "Keep", true);

        var result = await sut.GetMetricsAsync();

        Assert.Equal(0.70f, result.Value.RollingAccuracy, 5);
    }

    [Fact]
    public async Task GetMetrics_WindowCapsAt100Decisions()
    {
        SetupCorrections(new Dictionary<string, int>());
        var sut = CreateSut();

        // Add 120 decisions: first 20 are overrides, next 100 are accepted
        for (int i = 0; i < 20; i++) sut.RecordDecision("Archive", "Keep", true);
        for (int i = 0; i < 100; i++) sut.RecordDecision("Archive", "Archive", false);

        var result = await sut.GetMetricsAsync();

        // Only the last 100 are in the window — all accepted → 100%
        Assert.Equal(1f, result.Value.RollingAccuracy, 5);
        Assert.Equal(100, result.Value.TotalDecisions);
    }

    // ──────────────────────────────────────────────────────────────────────────
    // CheckForWarningAsync — threshold transitions
    // ──────────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task CheckForWarning_HighAccuracy_ReturnsNull()
    {
        SetupCorrections(new Dictionary<string, int>()); // no corrections → no problematic actions
        var sut = CreateSut();

        // 100% accuracy in window (< 50 corrections in DB)
        for (int i = 0; i < 10; i++) sut.RecordDecision("Archive", "Archive", false);

        var config = new AutoApplyConfig { Enabled = true, ConfidenceThreshold = 0.95f };
        var result = await sut.CheckForWarningAsync(config);

        Assert.True(result.IsSuccess);
        Assert.Null(result.Value);
    }

    [Fact]
    public async Task CheckForWarning_Accuracy68Percent_ReturnsWarning()
    {
        SetupCorrections(new Dictionary<string, int>());
        var sut = CreateSut();

        // 68% accuracy (below 70% Warning threshold)
        for (int i = 0; i < 68; i++) sut.RecordDecision("Archive", "Archive", false);
        for (int i = 0; i < 32; i++) sut.RecordDecision("Archive", "Keep", true);

        var config = new AutoApplyConfig { Enabled = true, ConfidenceThreshold = 0.95f };
        var result = await sut.CheckForWarningAsync(config);

        Assert.True(result.IsSuccess);
        Assert.NotNull(result.Value);
        Assert.Equal(QualityWarningSeverity.Warning, result.Value!.Severity);
        Assert.False(result.Value.AutoApplyDisabled);
    }

    [Fact]
    public async Task CheckForWarning_Accuracy45Percent_ReturnsCriticalAndDisablesAutoApply()
    {
        SetupCorrections(new Dictionary<string, int>());
        var sut = CreateSut();

        // 45% accuracy (below 50% Critical threshold)
        for (int i = 0; i < 45; i++) sut.RecordDecision("Archive", "Archive", false);
        for (int i = 0; i < 55; i++) sut.RecordDecision("Archive", "Keep", true);

        var config = new AutoApplyConfig { Enabled = true, ConfidenceThreshold = 0.95f };
        var result = await sut.CheckForWarningAsync(config);

        Assert.True(result.IsSuccess);
        Assert.NotNull(result.Value);
        Assert.Equal(QualityWarningSeverity.Critical, result.Value!.Severity);
        Assert.True(result.Value.AutoApplyDisabled);
        Assert.False(config.Enabled); // auto-apply was disabled on the config object
    }

    [Fact]
    public async Task CheckForWarning_50PlusCorrections_ReturnsInfoWarning()
    {
        // 50 cumulative corrections in DB — triggers retrain suggestion
        SetupCorrections(new Dictionary<string, int> { ["Archive"] = 30, ["Keep"] = 20 });
        var sut = CreateSut();

        // Perfect rolling accuracy (so no other trigger fires first)
        for (int i = 0; i < 10; i++) sut.RecordDecision("Archive", "Archive", false);

        var config = new AutoApplyConfig { Enabled = true, ConfidenceThreshold = 0.95f };
        var result = await sut.CheckForWarningAsync(config);

        Assert.True(result.IsSuccess);
        Assert.NotNull(result.Value);
        Assert.Equal(QualityWarningSeverity.Info, result.Value!.Severity);
        Assert.Equal(50, result.Value.CorrectionsSinceTraining);
    }

    // ──────────────────────────────────────────────────────────────────────────
    // Dismissal suppression logic
    // ──────────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task CheckForWarning_WithinSuppressionWindow_ReturnsNull()
    {
        // First call: 50 corrections → Info warning shown (internally records dismissal)
        SetupCorrections(new Dictionary<string, int> { ["Archive"] = 50 });
        var sut = CreateSut();
        for (int i = 0; i < 10; i++) sut.RecordDecision("Archive", "Archive", false);
        var config = new AutoApplyConfig { Enabled = true, ConfidenceThreshold = 0.95f };

        // First call should show warning
        var first = await sut.CheckForWarningAsync(config);
        Assert.NotNull(first.Value);

        // Second call with same correction count — still within suppression window (+0 new corrections)
        var second = await sut.CheckForWarningAsync(config);
        Assert.Null(second.Value);
    }

    [Fact]
    public async Task CheckForWarning_AfterSuppressionDelta_ShowsWarningAgain()
    {
        var sut = CreateSut();
        for (int i = 0; i < 10; i++) sut.RecordDecision("Archive", "Archive", false);
        var config = new AutoApplyConfig { Enabled = true, ConfidenceThreshold = 0.95f };

        // First call: 50 corrections → warning shown
        SetupCorrections(new Dictionary<string, int> { ["Archive"] = 50 });
        var first = await sut.CheckForWarningAsync(config);
        Assert.NotNull(first.Value);

        // Now simulate 25+ more corrections (50+25 = 75) — beyond suppression delta
        SetupCorrections(new Dictionary<string, int> { ["Archive"] = 75 });
        var second = await sut.CheckForWarningAsync(config);
        Assert.NotNull(second.Value);
    }

    // ──────────────────────────────────────────────────────────────────────────
    // ResetSession
    // ──────────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task ResetSession_ClearsRollingWindow()
    {
        SetupCorrections(new Dictionary<string, int>());
        var sut = CreateSut();

        // Build up 50% accuracy
        for (int i = 0; i < 5; i++) sut.RecordDecision("Archive", "Archive", false);
        for (int i = 0; i < 5; i++) sut.RecordDecision("Archive", "Keep", true);

        sut.ResetSession();

        var result = await sut.GetMetricsAsync();
        Assert.Equal(1f, result.Value.RollingAccuracy); // window empty → defaults to 100%
        Assert.Equal(0, result.Value.TotalDecisions);
    }

    // ──────────────────────────────────────────────────────────────────────────
    // GetCorrectionsSinceLastTrainingAsync
    // ──────────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task GetCorrectionsSinceLastTraining_SumsAllLabelCounts()
    {
        SetupCorrections(new Dictionary<string, int> { ["Archive"] = 12, ["Keep"] = 8, ["Delete"] = 5 });
        var sut = CreateSut();

        var result = await sut.GetCorrectionsSinceLastTrainingAsync();

        Assert.True(result.IsSuccess);
        Assert.Equal(25, result.Value);
    }

    [Fact]
    public async Task GetCorrectionsSinceLastTraining_WhenArchiveServiceFails_ReturnsFailure()
    {
        _archive.Setup(x => x.GetUserCorrectedCountsByLabelAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result<IReadOnlyDictionary<string, int>>.Failure(new StorageError("DB error")));
        var sut = CreateSut();

        var result = await sut.GetCorrectionsSinceLastTrainingAsync();

        Assert.False(result.IsSuccess);
    }
}
