using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using TrashMailPanda.Shared;
using TrashMailPanda.Shared.Base;

namespace TrashMailPanda.Providers.Storage.Services;

/// <summary>
/// Domain service for managing email metadata and classification state.
/// Handles caching of email processing results and user actions.
/// </summary>
public interface IEmailMetadataService
{
    /// <summary>
    /// Retrieves metadata for a specific email.
    /// </summary>
    /// <param name="emailId">The email ID to retrieve</param>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>
    /// Success: EmailMetadata if found, null if not found
    /// Failure: ValidationError if emailId is invalid, StorageError if database operation fails
    /// </returns>
    Task<Result<EmailMetadata?>> GetEmailMetadataAsync(string emailId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Sets or updates metadata for a specific email.
    /// </summary>
    /// <param name="emailId">The email ID</param>
    /// <param name="metadata">The metadata to store</param>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>
    /// Success: true if stored successfully
    /// Failure: ValidationError if data is invalid, StorageError if database operation fails
    /// </returns>
    Task<Result<bool>> SetEmailMetadataAsync(string emailId, EmailMetadata metadata, CancellationToken cancellationToken = default);

    /// <summary>
    /// Bulk sets metadata for multiple emails in a single transaction.
    /// Optimized for batch processing operations.
    /// </summary>
    /// <param name="entries">Collection of email metadata entries</param>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>
    /// Success: Number of entries stored
    /// Failure: ValidationError if any entry is invalid, StorageError if database operation fails
    /// </returns>
    Task<Result<int>> BulkSetEmailMetadataAsync(IReadOnlyList<EmailMetadataEntry> entries, CancellationToken cancellationToken = default);

    /// <summary>
    /// Retrieves all email metadata matching the specified criteria.
    /// </summary>
    /// <param name="classification">Optional filter by classification</param>
    /// <param name="userAction">Optional filter by user action</param>
    /// <param name="limit">Maximum number of results to return</param>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>
    /// Success: Collection of matching metadata entries
    /// Failure: StorageError if database operation fails
    /// </returns>
    Task<Result<IEnumerable<EmailMetadata>>> QueryMetadataAsync(
        string? classification = null,
        UserAction? userAction = null,
        int? limit = null,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Deletes metadata for a specific email.
    /// </summary>
    /// <param name="emailId">The email ID to delete</param>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>
    /// Success: true if deleted, false if not found
    /// Failure: StorageError if database operation fails
    /// </returns>
    Task<Result<bool>> DeleteEmailMetadataAsync(string emailId, CancellationToken cancellationToken = default);
}
