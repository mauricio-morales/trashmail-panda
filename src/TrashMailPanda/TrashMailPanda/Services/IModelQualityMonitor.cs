using System.Threading;
using System.Threading.Tasks;
using TrashMailPanda.Models.Console;
using TrashMailPanda.Shared.Base;

namespace TrashMailPanda.Services;

/// <summary>
/// Tracks model quality metrics from user triage decisions.
/// Computed on-demand (not background-polled) at batch boundaries and session end.
/// Proactively generates warnings when accuracy degrades.
/// </summary>
public interface IModelQualityMonitor
{
    /// <summary>
    /// Records a single AI-assisted decision for quality tracking.
    /// Called after each triage decision in AI-assisted mode.
    /// Maintains the rolling window of last N decisions (default 100).
    /// </summary>
    void RecordDecision(string predictedAction, string chosenAction, bool isOverride);

    /// <summary>
    /// Computes current quality metrics from the rolling window and DB aggregates.
    /// Returns per-action breakdown, confusion data, and overall accuracy.
    /// </summary>
    Task<Result<ModelQualityMetrics>> GetMetricsAsync(CancellationToken ct = default);

    /// <summary>
    /// Checks if a quality warning should be shown to the user.
    /// Called at the start of each new batch.
    /// Returns null if no warning needed.
    ///
    /// Warning triggers (evaluated in order):
    /// 1. Critical: rolling accuracy &lt;50% → auto-disable auto-apply (FR-025)
    /// 2. Warning: rolling accuracy &lt;70% → proactive degradation alert (FR-014)
    /// 3. Info: corrections since training ≥50 → retrain suggestion (FR-013)
    /// 4. Info: any action with correction rate &gt;40% → targeted recommendation (FR-026)
    /// </summary>
    Task<Result<QualityWarning?>> CheckForWarningAsync(
        AutoApplyConfig autoApplyConfig,
        CancellationToken ct = default);

    /// <summary>
    /// Returns the count of user corrections since the last model training date.
    /// Used by the retrain suggestion logic.
    /// </summary>
    Task<Result<int>> GetCorrectionsSinceLastTrainingAsync(CancellationToken ct = default);

    /// <summary>
    /// Resets the in-memory rolling window (called when a new session starts).
    /// DB-backed metrics are not affected.
    /// </summary>
    void ResetSession();
}
