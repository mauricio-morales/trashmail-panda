using System.Collections.Generic;

namespace TrashMailPanda.Models.Console;

/// <summary>
/// Per-action quality breakdown: how often the model was accepted vs. corrected for one action.
/// </summary>
/// <param name="Action">Action label: "Keep", "Archive", "Delete", or "Spam".</param>
/// <param name="TotalRecommended">Total times the model predicted this action.</param>
/// <param name="TotalAccepted">Times the user (or auto-apply path) accepted the prediction.</param>
/// <param name="CorrectionRate">
///     Fraction of recommendations that were corrected: <c>TotalCorrected / TotalRecommended</c>.
///     Returns 0 when <c>TotalRecommended == 0</c>.
/// </param>
/// <param name="CorrectedTo">
///     Map of corrected-to action → count, capturing the per-action confusion data.
///     e.g. <c>{ "Delete": 5, "Keep": 2 }</c> when action was "Archive".
/// </param>
public sealed record ActionCategoryMetrics(
    string Action,
    int TotalRecommended,
    int TotalAccepted,
    float CorrectionRate,
    IReadOnlyDictionary<string, int> CorrectedTo);
