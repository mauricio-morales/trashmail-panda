using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using TrashMailPanda.Models.Console;
using TrashMailPanda.Providers.Storage;
using TrashMailPanda.Shared.Base;

namespace TrashMailPanda.Services;

/// <summary>
/// Rolling-window model quality monitor.  Tracks the last <see cref="WindowCapacity"/>
/// AI-assisted decisions in memory and queries the <c>email_features</c> table for
/// lifetime correction counts.  Warning logic is evaluated on each batch boundary.
/// </summary>
public sealed class ModelQualityMonitor : IModelQualityMonitor
{
    private const int WindowCapacity = 100;
    private const float CriticalThreshold = 0.50f;
    private const float WarningThreshold = 0.70f;
    private const int RetrainSuggestionThreshold = 50;
    private const float ProblematicCorrectionRate = 0.40f;
    private const int DismissalSuppressCorrectionDelta = 25;

    private readonly IEmailArchiveService _archiveService;
    private readonly ILogger<ModelQualityMonitor> _logger;

    // In-memory rolling window: (predictedAction, chosenAction, isOverride)
    private readonly Queue<(string Predicted, string Chosen, bool IsOverride)> _window = new();

    // Dismissal suppression: if set, don't re-show a warning until this many more corrections
    private int _correctionsAtLastWarning = -1;

    public ModelQualityMonitor(IEmailArchiveService archiveService, ILogger<ModelQualityMonitor> logger)
    {
        _archiveService = archiveService ?? throw new ArgumentNullException(nameof(archiveService));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    /// <inheritdoc/>
    public void RecordDecision(string predictedAction, string chosenAction, bool isOverride)
    {
        if (string.IsNullOrEmpty(predictedAction) || string.IsNullOrEmpty(chosenAction))
            return;

        if (_window.Count >= WindowCapacity)
            _window.Dequeue();

        _window.Enqueue((predictedAction, chosenAction, isOverride));
    }

    /// <inheritdoc/>
    public async Task<Result<ModelQualityMetrics>> GetMetricsAsync(CancellationToken ct = default)
    {
        var correctionsByLabelResult = await _archiveService.GetUserCorrectedCountsByLabelAsync(ct);
        if (!correctionsByLabelResult.IsSuccess)
            return Result<ModelQualityMetrics>.Failure(correctionsByLabelResult.Error);

        var correctionsByLabel = correctionsByLabelResult.Value;
        int totalDbCorrections = correctionsByLabel.Values.Sum();

        var windowList = _window.ToList();
        int windowTotal = windowList.Count;
        int windowAccepted = windowList.Count(e => e.Predicted == e.Chosen);
        float rollingAccuracy = windowTotal > 0 ? (float)windowAccepted / windowTotal : 1f;

        int sessionTotal = windowTotal;
        int sessionCorrections = windowList.Count(e => e.IsOverride);
        float overallAccuracy = sessionTotal > 0
            ? (float)(sessionTotal - sessionCorrections) / sessionTotal
            : 1f;

        // Build per-action metrics from the DB-side corrections
        var perAction = BuildPerActionMetrics(correctionsByLabel);

        var metrics = new ModelQualityMetrics(
            OverallAccuracy: overallAccuracy,
            RollingAccuracy: rollingAccuracy,
            RollingWindowSize: WindowCapacity,
            TotalDecisions: sessionTotal,
            TotalCorrections: sessionCorrections,
            CorrectionsSinceLastTraining: totalDbCorrections,
            PerActionMetrics: perAction,
            CalculatedAtUtc: DateTime.UtcNow);

        return Result<ModelQualityMetrics>.Success(metrics);
    }

    /// <inheritdoc/>
    public async Task<Result<QualityWarning?>> CheckForWarningAsync(
        AutoApplyConfig autoApplyConfig,
        CancellationToken ct = default)
    {
        var metricsResult = await GetMetricsAsync(ct);
        if (!metricsResult.IsSuccess)
            return Result<QualityWarning?>.Failure(metricsResult.Error);

        var metrics = metricsResult.Value;

        // --- 1. Critical: rolling accuracy < 50% ---
        if (metrics.RollingAccuracy < CriticalThreshold)
        {
            bool disabledAutoApply = false;
            if (autoApplyConfig is not null && autoApplyConfig.Enabled)
            {
                autoApplyConfig.Enabled = false;
                disabledAutoApply = true;
                _logger.LogWarning(
                    "Model quality critical ({Accuracy:P0}) — auto-apply disabled automatically.",
                    metrics.RollingAccuracy);
            }

            RecordWarningShown(metrics.CorrectionsSinceLastTraining);
            return Result<QualityWarning?>.Success(new QualityWarning(
                Severity: QualityWarningSeverity.Critical,
                Message: $"Model accuracy critical: {metrics.RollingAccuracy:P0} over last {metrics.RollingWindowSize} decisions.",
                RollingAccuracy: metrics.RollingAccuracy,
                CorrectionsSinceTraining: metrics.CorrectionsSinceLastTraining,
                RecommendedAction: "Retrain model immediately",
                ProblematicActions: GetProblematicActions(metrics),
                AutoApplyDisabled: disabledAutoApply));
        }

        // --- 2. Warning: rolling accuracy < 70% ---
        if (metrics.RollingAccuracy < WarningThreshold)
        {
            RecordWarningShown(metrics.CorrectionsSinceLastTraining);
            return Result<QualityWarning?>.Success(new QualityWarning(
                Severity: QualityWarningSeverity.Warning,
                Message: $"Model accuracy declining: {metrics.RollingAccuracy:P0} over last {metrics.RollingWindowSize} decisions.",
                RollingAccuracy: metrics.RollingAccuracy,
                CorrectionsSinceTraining: metrics.CorrectionsSinceLastTraining,
                RecommendedAction: "Consider retraining the model",
                ProblematicActions: GetProblematicActions(metrics),
                AutoApplyDisabled: false));
        }

        // --- 3. Info: corrections ≥ 50 ---
        int corrections = metrics.CorrectionsSinceLastTraining;
        if (corrections >= RetrainSuggestionThreshold && !IsDismissed(corrections))
        {
            RecordWarningShown(corrections);
            return Result<QualityWarning?>.Success(new QualityWarning(
                Severity: QualityWarningSeverity.Info,
                Message: $"{corrections} corrections accumulated — model can benefit from retraining.",
                RollingAccuracy: metrics.RollingAccuracy,
                CorrectionsSinceTraining: corrections,
                RecommendedAction: "Press T to retrain from corrections",
                ProblematicActions: GetProblematicActions(metrics),
                AutoApplyDisabled: false));
        }

        // --- 4. Info: any action with correction rate > 40% ---
        var problematic = GetProblematicActions(metrics);
        if (problematic is not null && !IsDismissed(corrections))
        {
            RecordWarningShown(corrections);
            return Result<QualityWarning?>.Success(new QualityWarning(
                Severity: QualityWarningSeverity.Info,
                Message: $"High correction rate for: {string.Join(", ", problematic)}.",
                RollingAccuracy: metrics.RollingAccuracy,
                CorrectionsSinceTraining: corrections,
                RecommendedAction: "Review auto-apply settings or retrain",
                ProblematicActions: problematic,
                AutoApplyDisabled: false));
        }

        return Result<QualityWarning?>.Success(null);
    }

    /// <inheritdoc/>
    public async Task<Result<int>> GetCorrectionsSinceLastTrainingAsync(CancellationToken ct = default)
    {
        var result = await _archiveService.GetUserCorrectedCountsByLabelAsync(ct);
        if (!result.IsSuccess)
            return Result<int>.Failure(result.Error);

        return Result<int>.Success(result.Value.Values.Sum());
    }

    /// <inheritdoc/>
    public void ResetSession()
    {
        _window.Clear();
        _correctionsAtLastWarning = -1;
    }

    // ──────────────────────────────────────────────────────────────────────────
    // Helpers
    // ──────────────────────────────────────────────────────────────────────────

    private static IReadOnlyDictionary<string, ActionCategoryMetrics> BuildPerActionMetrics(
        IReadOnlyDictionary<string, int> correctionsByLabel)
    {
        // With only the training_label column we know which label the correction landed on
        // but not what was originally predicted. We surface the correction count per label
        // as TotalRecommended (as a proxy for how much the model mis-predicted this action).
        var result = new Dictionary<string, ActionCategoryMetrics>(StringComparer.Ordinal);

        foreach (var (label, correctionCount) in correctionsByLabel)
        {
            var correctedTo = new Dictionary<string, int>(StringComparer.Ordinal);
            var rate = correctionCount > 0 ? 1.0f : 0f; // all corrections counted

            result[label] = new ActionCategoryMetrics(
                Action: label,
                TotalRecommended: correctionCount,
                TotalAccepted: 0,
                CorrectionRate: rate,
                CorrectedTo: correctedTo);
        }

        return result;
    }

    private static IReadOnlyList<string>? GetProblematicActions(ModelQualityMetrics metrics)
    {
        // Actions with correction rate > 40%
        // Since CorrectionRate here is derived per-label from DB correction counts alone,
        // we surface any action with at least one correction as a courtesy when rate > threshold.
        var problematic = metrics.PerActionMetrics.Values
            .Where(m => m.CorrectionRate > ProblematicCorrectionRate)
            .Select(m => m.Action)
            .OrderBy(a => a, StringComparer.Ordinal)
            .ToList();

        return problematic.Count > 0 ? problematic : null;
    }

    private bool IsDismissed(int currentCorrections)
    {
        if (_correctionsAtLastWarning < 0)
            return false;
        return currentCorrections - _correctionsAtLastWarning < DismissalSuppressCorrectionDelta;
    }

    private void RecordWarningShown(int currentCorrections)
    {
        _correctionsAtLastWarning = currentCorrections;
    }
}
