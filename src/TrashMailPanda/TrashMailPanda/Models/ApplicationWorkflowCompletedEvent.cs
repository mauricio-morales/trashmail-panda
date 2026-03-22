namespace TrashMailPanda.Models;

/// <summary>
/// Emitted when the application workflow completes (either via user exit or
/// cancellation).
/// </summary>
public sealed record ApplicationWorkflowCompletedEvent : ApplicationEvent
{
    /// <summary>Process exit code. 0 indicates success.</summary>
    public required int ExitCode { get; init; }

    /// <summary>Human-readable reason for workflow completion.</summary>
    public required string Reason { get; init; }
}
