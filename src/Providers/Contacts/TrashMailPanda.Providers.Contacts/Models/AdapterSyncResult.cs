using TrashMailPanda.Shared.Models;

namespace TrashMailPanda.Providers.Contacts.Models;

/// <summary>
/// Result of synchronization from a specific contact source adapter
/// </summary>
public class AdapterSyncResult
{
    /// <summary>
    /// The contact source type this result applies to
    /// </summary>
    public ContactSourceType SourceType { get; set; }

    /// <summary>
    /// Whether the sync from this adapter was successful
    /// </summary>
    public bool IsSuccessful { get; set; }

    /// <summary>
    /// Number of contacts synchronized from this adapter
    /// </summary>
    public int ContactsSynced { get; set; }

    /// <summary>
    /// Next sync token for incremental synchronization
    /// </summary>
    public string? NextSyncToken { get; set; }

    /// <summary>
    /// Error message if the sync failed
    /// </summary>
    public string? ErrorMessage { get; set; }

    /// <summary>
    /// Duration of the sync operation
    /// </summary>
    public TimeSpan? Duration { get; set; }

    /// <summary>
    /// Timestamp when this adapter sync was completed
    /// </summary>
    public DateTime SyncedAt { get; set; } = DateTime.UtcNow;
}