using TrashMailPanda.Shared.Base;

namespace TrashMailPanda.Models.Console;

/// <summary>
/// Represents the runtime state of a single provider during startup initialization.
/// </summary>
public class ProviderInitializationState
{
    /// <summary>
    /// Gets or sets the display name of the provider (e.g., "Storage", "Gmail", "OpenAI").
    /// </summary>
    public required string ProviderName { get; set; }

    /// <summary>
    /// Gets or sets the provider type (Required or Optional).
    /// </summary>
    public required ProviderType ProviderType { get; set; }

    /// <summary>
    /// Gets or sets the current initialization status.
    /// </summary>
    public InitializationStatus Status { get; set; } = InitializationStatus.NotStarted;

    /// <summary>
    /// Gets or sets the current operation description (e.g., "Authenticating with Gmail...").
    /// </summary>
    public string? StatusMessage { get; set; }

    /// <summary>
    /// Gets or sets the health status after health check (null if not yet checked).
    /// </summary>
    public HealthStatus? HealthStatus { get; set; }

    /// <summary>
    /// Gets or sets error details if Status == Failed (from Shared.Base.ProviderError).
    /// </summary>
    public ProviderError? Error { get; set; }

    /// <summary>
    /// Gets or sets when initialization began (null if Status == NotStarted).
    /// </summary>
    public DateTime? StartTime { get; set; }

    /// <summary>
    /// Gets or sets when initialization finished (null if still in progress).
    /// </summary>
    public DateTime? CompletionTime { get; set; }

    /// <summary>
    /// Gets the duration of initialization (CompletionTime - StartTime).
    /// </summary>
    public TimeSpan? Duration =>
        StartTime.HasValue && CompletionTime.HasValue
            ? CompletionTime.Value - StartTime.Value
            : null;
}
