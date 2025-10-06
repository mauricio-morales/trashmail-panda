using TrashMailPanda.Shared.Base;
using TrashMailPanda.Shared.Models;

namespace TrashMailPanda.Providers.Contacts.Models;

/// <summary>
/// Interface for platform-specific contact source adapters
/// Enables pluggable architecture for different contact platforms (Google, Apple, Windows, etc.)
/// </summary>
public interface IContactSourceAdapter
{
    /// <summary>
    /// The type of contact source this adapter handles
    /// </summary>
    ContactSourceType SourceType { get; }

    /// <summary>
    /// Whether this adapter is enabled and available
    /// </summary>
    bool IsEnabled { get; }

    /// <summary>
    /// Display name for this contact source
    /// </summary>
    string DisplayName { get; }

    /// <summary>
    /// Whether this adapter supports incremental synchronization
    /// </summary>
    bool SupportsIncrementalSync { get; }

    /// <summary>
    /// Fetches contacts from the source platform
    /// </summary>
    /// <param name="syncToken">Optional sync token for incremental sync</param>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>Contacts and next sync token, or error</returns>
    Task<Result<(IEnumerable<Contact> Contacts, string? NextSyncToken)>> FetchContactsAsync(
        string? syncToken = null,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Validates the adapter configuration and connectivity
    /// </summary>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>Result indicating validation success/failure</returns>
    Task<Result<bool>> ValidateAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Gets the current sync status and metadata
    /// </summary>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>Sync status information</returns>
    Task<Result<AdapterSyncStatus>> GetSyncStatusAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Performs a health check on the adapter
    /// </summary>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>Health check result</returns>
    Task<Result<HealthCheckResult>> HealthCheckAsync(CancellationToken cancellationToken = default);
}

