namespace TrashMailPanda.Models.Console;

/// <summary>
/// In-memory session state for an active triage session.
/// Not persisted — the session queue is derived from
/// <c>email_features WHERE training_label IS NULL</c> at session start.
/// </summary>
public sealed class EmailTriageSession
{
    public required string AccountId { get; init; }

    public TriageMode Mode { get; set; }

    /// <summary>
    /// Cumulative labeled count loaded from DB at session start, then incremented in-memory.
    /// Source: <c>COUNT(*) FROM email_features WHERE training_label IS NOT NULL</c>.
    /// </summary>
    public int LabeledCount { get; set; }

    /// <summary>Minimum labeled samples required to trigger the training threshold prompt.</summary>
    public int LabelingThreshold { get; init; }

    /// <summary>True once the threshold prompt has been shown this session (shown at most once).</summary>
    public bool ThresholdPromptShownThisSession { get; set; }

    /// <summary>Count of emails processed in the current session only (not cumulative).</summary>
    public int SessionProcessedCount { get; set; }

    /// <summary>Override count for the current session only.</summary>
    public int SessionOverrideCount { get; set; }

    /// <summary>Per-action counts for the current session: Keep, Archive, Delete, Spam.</summary>
    public Dictionary<string, int> ActionCounts { get; } = new(StringComparer.Ordinal)
    {
        ["Keep"] = 0,
        ["Archive"] = 0,
        ["archive-then-delete-30d"] = 0,
        ["archive-then-delete-1y"] = 0,
        ["archive-then-delete-5y"] = 0,
        ["Delete"] = 0,
        ["Spam"] = 0,
    };

    public DateTime StartedAtUtc { get; } = DateTime.UtcNow;

    /// <summary>SQL offset for the current page of untriaged emails (local DB paging).</summary>
    public int CurrentOffset { get; set; }

    /// <summary>
    /// True once the triage queue has entered the re-triage phase.
    /// This is triggered when the oldest available untriaged email exceeds
    /// <see cref="EmailTriageService.OldEmailThresholdDays"/> (5 years), meaning
    /// no newer unlabeled emails remain. In this phase the queue is mixed:
    /// very old untriaged emails are interleaved with recently-archived, previously-labeled
    /// emails that may now warrant a different decision.
    /// </summary>
    public bool IsRetriagedPhase { get; set; }
}
