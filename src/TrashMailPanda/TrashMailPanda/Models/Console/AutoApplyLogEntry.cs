namespace TrashMailPanda.Models.Console;

/// <summary>
/// Ephemeral per-session record of an auto-applied decision.
/// Not persisted to DB — lives in <see cref="EmailTriageSession.AutoApplyLog"/> only.
/// </summary>
public sealed record AutoApplyLogEntry(
    string EmailId,
    string SenderDomain,
    string Subject,
    string AppliedAction,
    float Confidence,
    DateTime AppliedAtUtc,
    bool WasRedundant)
{
    /// <summary>True once the user undoes this auto-applied decision.</summary>
    public bool Undone { get; set; }

    /// <summary>The action the user corrected to, if undone.</summary>
    public string? UndoneToAction { get; set; }
}
