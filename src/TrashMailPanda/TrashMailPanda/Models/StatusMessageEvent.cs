namespace TrashMailPanda.Models;

/// <summary>
/// Emitted when the orchestrator needs to communicate a status or informational message
/// to the UI layer. The message text uses Spectre.Console markup syntax.
/// </summary>
public sealed record StatusMessageEvent : ApplicationEvent
{
    /// <summary>The message to display. Uses Spectre.Console markup format.</summary>
    public required string Message { get; init; }
}
