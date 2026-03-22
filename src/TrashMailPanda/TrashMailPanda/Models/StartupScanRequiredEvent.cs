namespace TrashMailPanda.Models;

/// <summary>
/// Emitted before an initial or resumed Gmail training scan begins during startup.
/// The renderer should display an explanatory panel describing what is about to happen.
/// </summary>
public sealed record StartupScanRequiredEvent : ApplicationEvent
{
    /// <summary>
    /// True if a previous scan was interrupted and is being resumed from a checkpoint.
    /// False if no training data exists and a full initial scan is required.
    /// </summary>
    public required bool IsResume { get; init; }
}
