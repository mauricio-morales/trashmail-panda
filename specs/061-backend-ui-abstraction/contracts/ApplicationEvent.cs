using TrashMailPanda.Models.Console;

namespace TrashMailPanda.Models;

// ─────────────────────────────────────────────────────────────────────────────
// ApplicationEventArgs — standard EventArgs wrapper
// ─────────────────────────────────────────────────────────────────────────────

/// <summary>
/// EventArgs wrapper for the unified application event stream.
/// Subscribers pattern-match on <see cref="Event"/> to handle specific event types.
/// </summary>
public sealed class ApplicationEventArgs : EventArgs
{
    /// <summary>The concrete application event.</summary>
    public ApplicationEvent Event { get; }

    public ApplicationEventArgs(ApplicationEvent @event)
    {
        Event = @event ?? throw new ArgumentNullException(nameof(@event));
    }
}

// ─────────────────────────────────────────────────────────────────────────────
// ApplicationEvent hierarchy — discriminated base + concrete subtypes
// ─────────────────────────────────────────────────────────────────────────────

/// <summary>
/// Base record for all application lifecycle events emitted by
/// <see cref="Services.IApplicationOrchestrator"/>.
/// </summary>
public abstract record ApplicationEvent
{
    /// <summary>When the event was created.</summary>
    public DateTimeOffset Timestamp { get; init; } = DateTimeOffset.UtcNow;

    /// <summary>Discriminator derived from the concrete type name.</summary>
    public string EventType => GetType().Name;
}

/// <summary>
/// Emitted when the orchestrator is ready for user mode selection.
/// </summary>
public sealed record ModeSelectionRequestedEvent : ApplicationEvent
{
    /// <summary>Modes available based on current provider health.</summary>
    public required IReadOnlyList<OperationalMode> AvailableModes { get; init; }
}

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

/// <summary>
/// Emitted when a provider's health state changes during startup or monitoring.
/// </summary>
public sealed record ProviderStatusChangedEvent : ApplicationEvent
{
    /// <summary>Name of the provider (e.g., "Storage", "Gmail").</summary>
    public required string ProviderName { get; init; }

    /// <summary>New health state.</summary>
    public required bool IsHealthy { get; init; }

    /// <summary>Optional descriptive message about the status change.</summary>
    public string? StatusMessage { get; init; }
}

/// <summary>
/// Emitted when the application workflow loop exits.
/// </summary>
public sealed record ApplicationWorkflowCompletedEvent : ApplicationEvent
{
    /// <summary>Process exit code. 0 = success.</summary>
    public required int ExitCode { get; init; }

    /// <summary>Human-readable exit reason.</summary>
    public required string Reason { get; init; }
}
