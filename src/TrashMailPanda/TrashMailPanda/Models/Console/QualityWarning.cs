using System.Collections.Generic;

namespace TrashMailPanda.Models.Console;

/// <summary>
/// A single quality warning to surface to the user at a batch boundary.
/// </summary>
/// <param name="Severity">How urgent the warning is.</param>
/// <param name="Message">Human-readable description of the degradation.</param>
/// <param name="RollingAccuracy">The rolling accuracy value that triggered this warning.</param>
/// <param name="CorrectionsSinceTraining">
///     Number of user corrections accumulated since the last model training run.
/// </param>
/// <param name="RecommendedAction">
///     Short action text shown to the user, e.g. "Retrain model" or "Review auto-apply settings".
/// </param>
/// <param name="ProblematicActions">
///     Optional list of action labels with correction rate &gt;40%.
/// </param>
/// <param name="AutoApplyDisabled">
///     True when this warning caused auto-apply to be automatically disabled.
///     Only set on Critical warnings where rolling accuracy &lt;50%.
/// </param>
public sealed record QualityWarning(
    QualityWarningSeverity Severity,
    string Message,
    float RollingAccuracy,
    int CorrectionsSinceTraining,
    string RecommendedAction,
    IReadOnlyList<string>? ProblematicActions,
    bool AutoApplyDisabled);
