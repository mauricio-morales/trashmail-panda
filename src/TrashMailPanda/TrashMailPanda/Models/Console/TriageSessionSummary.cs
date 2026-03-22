namespace TrashMailPanda.Models.Console;

/// <summary>
/// Aggregates statistics for display when a triage session ends
/// (queue exhausted or user exits).
/// </summary>
public sealed record TriageSessionSummary(
    int TotalProcessed,
    int KeepCount,
    int ArchiveCount,
    int ArchiveThenDelete30dCount,
    int ArchiveThenDelete1yCount,
    int ArchiveThenDelete5yCount,
    int DeleteCount,
    int SpamCount,
    int OverrideCount,
    TimeSpan Elapsed
);
