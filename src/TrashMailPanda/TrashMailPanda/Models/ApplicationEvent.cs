namespace TrashMailPanda.Models;

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
