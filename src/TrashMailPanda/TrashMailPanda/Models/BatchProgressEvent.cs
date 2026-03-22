namespace TrashMailPanda.Models;

/// <summary>
/// Emitted during batch classification or triage progress.
/// </summary>
public sealed record BatchProgressEvent : ApplicationEvent
{
    /// <summary>Number of emails processed so far in the current operation.</summary>
    public required int ProcessedCount { get; init; }

    /// <summary>Total emails in the current operation.</summary>
    public required int TotalCount { get; init; }

    /// <summary>
    /// Estimated seconds remaining. Null if not computable yet.
    /// </summary>
    public double? EstimatedSecondsRemaining { get; init; }
}
