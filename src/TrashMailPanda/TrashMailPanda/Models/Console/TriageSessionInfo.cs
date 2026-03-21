namespace TrashMailPanda.Models.Console;

/// <summary>
/// Snapshot of triage session state returned at session start by
/// <c>IEmailTriageService.GetSessionInfoAsync</c>.
/// </summary>
public sealed record TriageSessionInfo(
    TriageMode Mode,
    int LabeledCount,
    int LabelingThreshold,
    bool ThresholdAlreadyReached
);
