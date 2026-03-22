namespace TrashMailPanda.Models;

/// <summary>
/// Emitted when a provider's health state changes during startup or monitoring.
/// Note: distinct from <see cref="TrashMailPanda.Services.ProviderStatusChangedEventArgs"/>
/// which is used in the provider status service layer.
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
