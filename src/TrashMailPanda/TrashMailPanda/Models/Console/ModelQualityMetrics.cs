using System;
using System.Collections.Generic;

namespace TrashMailPanda.Models.Console;

/// <summary>
/// Snapshot of model quality at a point in time, aggregated from the rolling window
/// and the persistent <c>email_features</c> table.
/// </summary>
/// <param name="OverallAccuracy">
///     Lifetime accuracy across all decisions in the session (accepted / total).
/// </param>
/// <param name="RollingAccuracy">
///     Accuracy over the last <see cref="RollingWindowSize"/> decisions.
///     This is the primary signal used for warning thresholds.
/// </param>
/// <param name="RollingWindowSize">
///     Number of decisions included in the rolling accuracy calculation. Matches the
///     rolling window cap configured on <see cref="Services.IModelQualityMonitor"/>.
/// </param>
/// <param name="TotalDecisions">Total decisions recorded in the current session.</param>
/// <param name="TotalCorrections">Total user corrections recorded in the current session.</param>
/// <param name="CorrectionsSinceLastTraining">
///     Count of <c>user_corrected=1</c> rows in the DB since the last model training run.
///     Used to trigger the retrain suggestion at ≥50 corrections.
/// </param>
/// <param name="PerActionMetrics">Per-action breakdown keyed by action label.</param>
/// <param name="CalculatedAtUtc">Timestamp when these metrics were computed.</param>
public sealed record ModelQualityMetrics(
    float OverallAccuracy,
    float RollingAccuracy,
    int RollingWindowSize,
    int TotalDecisions,
    int TotalCorrections,
    int CorrectionsSinceLastTraining,
    IReadOnlyDictionary<string, ActionCategoryMetrics> PerActionMetrics,
    DateTime CalculatedAtUtc);
