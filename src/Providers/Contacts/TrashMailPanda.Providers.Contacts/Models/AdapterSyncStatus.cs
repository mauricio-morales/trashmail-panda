namespace TrashMailPanda.Providers.Contacts.Models;

/// <summary>
/// Sync status information for a contact source adapter
/// </summary>
public record AdapterSyncStatus
{
    /// <summary>
    /// Whether the adapter is currently synchronizing
    /// </summary>
    public bool IsSyncing { get; init; } = false;

    /// <summary>
    /// Timestamp of the last successful sync
    /// </summary>
    public DateTime? LastSuccessfulSync { get; init; }

    /// <summary>
    /// Timestamp of the last sync attempt (successful or failed)
    /// </summary>
    public DateTime? LastSyncAttempt { get; init; }

    /// <summary>
    /// Number of contacts currently cached from this source
    /// </summary>
    public int ContactCount { get; init; } = 0;

    /// <summary>
    /// Current sync token for incremental sync
    /// </summary>
    public string? CurrentSyncToken { get; init; }

    /// <summary>
    /// Error message from the last sync attempt, if any
    /// </summary>
    public string? LastSyncError { get; init; }

    /// <summary>
    /// Additional status metadata
    /// </summary>
    public Dictionary<string, object> Metadata { get; init; } = new();

    /// <summary>
    /// Whether the adapter is healthy and ready for sync operations
    /// </summary>
    public bool IsHealthy => string.IsNullOrEmpty(LastSyncError) &&
                            (LastSuccessfulSync == null || LastSuccessfulSync > DateTime.UtcNow.AddHours(-24));
}