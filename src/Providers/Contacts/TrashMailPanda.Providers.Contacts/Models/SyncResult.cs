using TrashMailPanda.Shared.Models;

namespace TrashMailPanda.Providers.Contacts.Models;

/// <summary>
/// Result of a contact synchronization operation from a source platform
/// Provides detailed metrics and status for sync operations
/// </summary>
public record SyncResult
{
    /// <summary>
    /// Whether the sync operation was successful
    /// </summary>
    public bool IsSuccess { get; init; } = false;

    /// <summary>
    /// The contact source that was synchronized
    /// </summary>
    public ContactSourceType SourceType { get; init; } = ContactSourceType.Unknown;

    /// <summary>
    /// Number of contacts that were added during sync
    /// </summary>
    public int ContactsAdded { get; init; } = 0;

    /// <summary>
    /// Number of contacts that were updated during sync
    /// </summary>
    public int ContactsUpdated { get; init; } = 0;

    /// <summary>
    /// Number of contacts that were deleted/removed during sync
    /// </summary>
    public int ContactsDeleted { get; init; } = 0;

    /// <summary>
    /// Total number of contacts processed
    /// </summary>
    public int TotalProcessed => ContactsAdded + ContactsUpdated + ContactsDeleted;

    /// <summary>
    /// Number of contacts that failed to process
    /// </summary>
    public int ContactsFailed { get; init; } = 0;

    /// <summary>
    /// Sync token for incremental synchronization (e.g., Google People API sync token)
    /// </summary>
    public string? NextSyncToken { get; init; }

    /// <summary>
    /// Timestamp when the sync operation started
    /// </summary>
    public DateTime SyncStartedAt { get; init; } = DateTime.UtcNow;

    /// <summary>
    /// Timestamp when the sync operation completed
    /// </summary>
    public DateTime SyncCompletedAt { get; init; } = DateTime.UtcNow;

    /// <summary>
    /// Duration of the sync operation
    /// </summary>
    public TimeSpan Duration => SyncCompletedAt - SyncStartedAt;

    /// <summary>
    /// Error message if the sync failed
    /// </summary>
    public string? ErrorMessage { get; init; }

    /// <summary>
    /// Detailed error information for debugging
    /// </summary>
    public Exception? Exception { get; init; }

    /// <summary>
    /// Whether this was a full sync or incremental sync
    /// </summary>
    public bool IsFullSync { get; init; } = false;

    /// <summary>
    /// Whether the sync hit rate limiting and was throttled
    /// </summary>
    public bool WasThrottled { get; init; } = false;

    /// <summary>
    /// Number of API requests made during sync
    /// </summary>
    public int ApiRequestCount { get; init; } = 0;

    /// <summary>
    /// Additional metadata about the sync operation
    /// </summary>
    public Dictionary<string, object> Metadata { get; init; } = new();

    /// <summary>
    /// Gets a summary string for logging purposes
    /// </summary>
    public string Summary => IsSuccess
        ? $"{SourceType} sync completed: +{ContactsAdded} ~{ContactsUpdated} -{ContactsDeleted} contacts in {Duration.TotalSeconds:F2}s"
        : $"{SourceType} sync failed: {ErrorMessage}";

    /// <summary>
    /// Creates a successful sync result
    /// </summary>
    public static SyncResult Success(ContactSourceType sourceType, int added = 0, int updated = 0, int deleted = 0)
    {
        return new SyncResult
        {
            IsSuccess = true,
            SourceType = sourceType,
            ContactsAdded = added,
            ContactsUpdated = updated,
            ContactsDeleted = deleted,
            SyncCompletedAt = DateTime.UtcNow
        };
    }

    /// <summary>
    /// Creates a failed sync result
    /// </summary>
    public static SyncResult Failure(ContactSourceType sourceType, string errorMessage, Exception? exception = null)
    {
        return new SyncResult
        {
            IsSuccess = false,
            SourceType = sourceType,
            ErrorMessage = errorMessage,
            Exception = exception,
            SyncCompletedAt = DateTime.UtcNow
        };
    }
}