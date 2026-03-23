namespace TrashMailPanda.Models.Console;

/// <summary>
/// Aggregate auto-apply statistics computed at session end. FR-003.
/// </summary>
public sealed record AutoApplySessionSummary(
    int TotalAutoApplied,
    int TotalManuallyReviewed,
    int TotalRedundant,
    int TotalUndone,
    IReadOnlyDictionary<string, int> PerActionCounts);
