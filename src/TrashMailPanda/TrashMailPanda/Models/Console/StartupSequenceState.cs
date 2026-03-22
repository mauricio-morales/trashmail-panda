using TrashMailPanda.Shared.Models;

namespace TrashMailPanda.Models.Console;

/// <summary>
/// Represents the overall startup orchestration progress across all providers.
/// </summary>
public class StartupSequenceState
{
    /// <summary>
    /// Gets or sets the ordered list of provider states (Storage, Gmail).
    /// </summary>
    public required List<ProviderInitializationState> ProviderStates { get; set; }

    /// <summary>
    /// Gets or sets the index of the provider currently initializing (0-based).
    /// </summary>
    public int CurrentProviderIndex { get; set; } = 0;

    /// <summary>
    /// Gets or sets the overall sequence status.
    /// </summary>
    public SequenceStatus OverallStatus { get; set; } = SequenceStatus.Initializing;

    /// <summary>
    /// Gets or sets when the startup sequence began.
    /// </summary>
    public DateTime StartTime { get; set; } = DateTime.UtcNow;

    /// <summary>
    /// Gets or sets when the startup sequence finished (null if still in progress).
    /// </summary>
    public DateTime? CompletionTime { get; set; }

    /// <summary>
    /// Gets the total duration of the startup sequence (CompletionTime - StartTime).
    /// </summary>
    public TimeSpan? TotalDuration =>
        CompletionTime.HasValue
            ? CompletionTime.Value - StartTime
            : null;

    /// <summary>
    /// Gets whether all required providers are initialized and healthy.
    /// </summary>
    public bool RequiredProvidersHealthy =>
        ProviderStates
            .Where(p => p.ProviderType == ProviderType.Required)
            .All(p => p.Status == InitializationStatus.Ready &&
                     p.HealthStatus == HealthStatus.Healthy);

    /// <summary>
    /// Gets whether the application is ready for mode selection.
    /// </summary>
    public bool IsReadyForModeSelection =>
        OverallStatus == SequenceStatus.Completed && RequiredProvidersHealthy;

    /// <summary>
    /// Gets the list of providers that failed initialization.
    /// </summary>
    public IEnumerable<ProviderInitializationState> FailedProviders =>
        ProviderStates.Where(p => p.Status == InitializationStatus.Failed ||
                                  p.Status == InitializationStatus.Timeout);

    /// <summary>
    /// Advances to the next provider in the sequence.
    /// </summary>
    public void NextProvider()
    {
        if (CurrentProviderIndex < ProviderStates.Count - 1)
        {
            CurrentProviderIndex++;
        }
    }
}
