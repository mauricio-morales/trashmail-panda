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
);
