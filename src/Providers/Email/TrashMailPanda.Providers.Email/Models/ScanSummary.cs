namespace TrashMailPanda.Providers.Email.Models;

/// <summary>
/// Summary of a completed training data scan.
/// </summary>
public record ScanSummary(
    int TotalProcessed,
    int AutoDeleteCount,
    int AutoArchiveCount,
    int LowConfidenceCount,
    int ExcludedCount,
    int LabelsImported,
    TimeSpan Duration);
