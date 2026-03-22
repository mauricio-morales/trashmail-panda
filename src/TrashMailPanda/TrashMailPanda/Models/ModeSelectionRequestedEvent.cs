using TrashMailPanda.Models.Console;

namespace TrashMailPanda.Models;

/// <summary>
/// Emitted when the orchestrator is ready for user mode selection.
/// </summary>
public sealed record ModeSelectionRequestedEvent : ApplicationEvent
{
    /// <summary>Modes available based on current provider health.</summary>
    public required IReadOnlyList<OperationalMode> AvailableModes { get; init; }
}
