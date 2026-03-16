using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using TrashMailPanda.Shared.Base;

namespace TrashMailPanda.Providers.Storage.Services;

/// <summary>
/// Domain service for managing encrypted credentials and tokens.
/// Handles secure storage of OAuth tokens, API keys, and other sensitive data.
/// </summary>
public interface ICredentialStorageService
{
    /// <summary>
    /// Retrieves an encrypted credential by key.
    /// </summary>
    /// <param name="key">The credential key</param>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>
    /// Success: Encrypted credential value if found, null if not found
    /// Failure: ValidationError if key is invalid, StorageError if database operation fails
    /// </returns>
    Task<Result<string?>> GetEncryptedCredentialAsync(string key, CancellationToken cancellationToken = default);

    /// <summary>
    /// Stores or updates an encrypted credential.
    /// </summary>
    /// <param name="key">The credential key</param>
    /// <param name="encryptedValue">The encrypted credential value</param>
    /// <param name="expiresAt">Optional expiration date</param>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>
    /// Success: true if stored successfully
    /// Failure: ValidationError if data is invalid, StorageError if database operation fails
    /// </returns>
    Task<Result<bool>> SetEncryptedCredentialAsync(
        string key,
        string encryptedValue,
        DateTime? expiresAt = null,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Removes an encrypted credential.
    /// </summary>
    /// <param name="key">The credential key to remove</param>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>
    /// Success: true if removed, false if not found
    /// Failure: StorageError if database operation fails
    /// </returns>
    Task<Result<bool>> RemoveEncryptedCredentialAsync(string key, CancellationToken cancellationToken = default);

    /// <summary>
    /// Retrieves all expired credential keys for cleanup.
    /// </summary>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>
    /// Success: Collection of expired credential keys
    /// Failure: StorageError if database operation fails
    /// </returns>
    Task<Result<IReadOnlyList<string>>> GetExpiredCredentialKeysAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Retrieves all credential keys (for migration or audit purposes).
    /// </summary>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>
    /// Success: Collection of all credential keys
    /// Failure: StorageError if database operation fails
    /// </returns>
    Task<Result<IReadOnlyList<string>>> GetAllCredentialKeysAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Retrieves all encrypted tokens for all providers.
    /// </summary>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>
    /// Success: Dictionary of provider name to encrypted token
    /// Failure: StorageError if database operation fails
    /// </returns>
    Task<Result<IReadOnlyDictionary<string, string>>> GetEncryptedTokensAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Sets an encrypted token for a specific provider.
    /// </summary>
    /// <param name="provider">Provider name (e.g., "Gmail", "OpenAI")</param>
    /// <param name="encryptedToken">The encrypted token value</param>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>
    /// Success: true if stored successfully
    /// Failure: ValidationError if data is invalid, StorageError if database operation fails
    /// </returns>
    Task<Result<bool>> SetEncryptedTokenAsync(
        string provider,
        string encryptedToken,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Cleans up expired credentials automatically.
    /// </summary>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>
    /// Success: Number of credentials removed
    /// Failure: StorageError if database operation fails
    /// </returns>
    Task<Result<int>> CleanupExpiredCredentialsAsync(CancellationToken cancellationToken = default);
}
