namespace TrashMailPanda.Models.Console;

/// <summary>
/// Captures a single triage action decision for audit and training signal storage.
/// Produced by <c>IEmailTriageService.ApplyDecisionAsync</c> after the Gmail action succeeds.
/// </summary>
public sealed record TriageDecision(
    string EmailId,
    string ChosenAction,           // "Keep", "Archive", "Delete", "Spam"
    string? AiRecommendation,      // null in ColdStart mode
    float? ConfidenceScore,        // null in ColdStart mode
    bool IsOverride,               // true when user's action differs from AI recommendation
    DateTime DecidedAtUtc
)
{
    /// <summary>
    /// True when a time-bounded label (e.g. "Archive for 30d") was applied to an email
    /// that had already exceeded its retention window, causing immediate deletion instead
    /// of archiving. The <see cref="ChosenAction"/> still reflects the user's intent label.
    /// </summary>
    public bool WasImmediatelyDeleted { get; init; }
};
