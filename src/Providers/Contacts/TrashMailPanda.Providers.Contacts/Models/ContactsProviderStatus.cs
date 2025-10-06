using TrashMailPanda.Providers.Contacts.Services;

namespace TrashMailPanda.Providers.Contacts.Models;

/// <summary>
/// Overall status of the contacts provider
/// </summary>
public class ContactsProviderStatus
{
    /// <summary>
    /// Whether the contacts provider is enabled
    /// </summary>
    public bool IsEnabled { get; set; }

    /// <summary>
    /// Whether the provider is in a healthy state
    /// </summary>
    public bool IsHealthy { get; set; }

    /// <summary>
    /// Total number of contacts synchronized since startup
    /// </summary>
    public long TotalContactsSynced { get; set; }

    /// <summary>
    /// Total number of trust signals computed since startup
    /// </summary>
    public long TrustSignalsComputed { get; set; }

    /// <summary>
    /// Timestamp of the last full synchronization
    /// </summary>
    public DateTime? LastFullSync { get; set; }

    /// <summary>
    /// Timestamp of the last incremental synchronization
    /// </summary>
    public DateTime? LastIncrementalSync { get; set; }

    /// <summary>
    /// Status information for each contact source adapter
    /// </summary>
    public List<AdapterSyncStatus> AdapterStatuses { get; set; } = new();

    /// <summary>
    /// Cache performance statistics
    /// </summary>
    public CacheStatistics CacheStatistics { get; set; } = new();

    /// <summary>
    /// Additional diagnostic information
    /// </summary>
    public Dictionary<string, object> Diagnostics { get; set; } = new();

    /// <summary>
    /// Configuration information (sanitized)
    /// </summary>
    public object? Configuration { get; set; }
}