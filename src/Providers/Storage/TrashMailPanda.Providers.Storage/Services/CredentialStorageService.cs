using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using TrashMailPanda.Providers.Storage.Models;
using TrashMailPanda.Shared.Base;

namespace TrashMailPanda.Providers.Storage.Services;

/// <summary>
/// Domain service implementation for managing encrypted credentials and tokens.
/// </summary>
public class CredentialStorageService : ICredentialStorageService
{
    private readonly IStorageRepository _repository;
    private readonly ILogger<CredentialStorageService> _logger;

    public CredentialStorageService(IStorageRepository repository, ILogger<CredentialStorageService> logger)
    {
        _repository = repository ?? throw new ArgumentNullException(nameof(repository));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    public async Task<Result<string?>> GetEncryptedCredentialAsync(string key, CancellationToken cancellationToken = default)
    {
        try
        {
            if (string.IsNullOrWhiteSpace(key))
            {
                return Result<string?>.Failure(new ValidationError("Credential key cannot be empty"));
            }

            _logger.LogDebug("Retrieving encrypted credential: {Key}", key);

            var result = await _repository.GetByIdAsync<EncryptedCredentialEntity>(key, cancellationToken);

            if (!result.IsSuccess)
            {
                return Result<string?>.Failure(result.Error);
            }

            var entity = result.Value;

            if (entity == null)
            {
                _logger.LogDebug("Credential not found: {Key}", key);
                return Result<string?>.Success(null);
            }

            // Check expiration
            if (entity.ExpiresAt.HasValue && entity.ExpiresAt.Value < DateTime.UtcNow)
            {
                _logger.LogInformation("Credential expired: {Key} (expired {ExpiresAt})", key, entity.ExpiresAt.Value);
                return Result<string?>.Success(null);
            }

            _logger.LogDebug("Retrieved encrypted credential: {Key}", key);
            return Result<string?>.Success(entity.EncryptedValue);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to retrieve encrypted credential: {Key}", key);
            return Result<string?>.Failure(new StorageError($"Failed to retrieve encrypted credential: {ex.Message}"));
        }
    }

    public async Task<Result<bool>> SetEncryptedCredentialAsync(
        string key,
        string encryptedValue,
        DateTime? expiresAt = null,
        CancellationToken cancellationToken = default)
    {
        try
        {
            if (string.IsNullOrWhiteSpace(key))
            {
                return Result<bool>.Failure(new ValidationError("Credential key cannot be empty"));
            }

            if (string.IsNullOrWhiteSpace(encryptedValue))
            {
                return Result<bool>.Failure(new ValidationError("Credential value cannot be empty"));
            }

            _logger.LogDebug("Setting encrypted credential: {Key} (expires: {ExpiresAt})",
                key, expiresAt?.ToString("yyyy-MM-dd HH:mm:ss") ?? "never");

            // Check if credential already exists
            var existingResult = await _repository.GetByIdAsync<EncryptedCredentialEntity>(key, cancellationToken);

            if (!existingResult.IsSuccess)
            {
                return Result<bool>.Failure(existingResult.Error);
            }

            var entity = new EncryptedCredentialEntity
            {
                Key = key,
                EncryptedValue = encryptedValue,
                CreatedAt = DateTime.UtcNow,
                ExpiresAt = expiresAt
            };

            Result<bool> saveResult;
            if (existingResult.Value != null)
            {
                // Update existing credential
                saveResult = await _repository.UpdateAsync(entity, cancellationToken);
                _logger.LogDebug("Updated encrypted credential: {Key}", key);
            }
            else
            {
                // Add new credential
                saveResult = await _repository.AddAsync(entity, cancellationToken);
                _logger.LogDebug("Added new encrypted credential: {Key}", key);
            }

            if (!saveResult.IsSuccess)
            {
                return Result<bool>.Failure(saveResult.Error);
            }

            return Result<bool>.Success(true);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to set encrypted credential: {Key}", key);
            return Result<bool>.Failure(new StorageError($"Failed to set encrypted credential: {ex.Message}"));
        }
    }

    public async Task<Result<bool>> RemoveEncryptedCredentialAsync(string key, CancellationToken cancellationToken = default)
    {
        try
        {
            if (string.IsNullOrWhiteSpace(key))
            {
                return Result<bool>.Failure(new ValidationError("Credential key cannot be empty"));
            }

            _logger.LogDebug("Removing encrypted credential: {Key}", key);

            var result = await _repository.DeleteAsync<EncryptedCredentialEntity>(key, cancellationToken);

            if (!result.IsSuccess)
            {
                return Result<bool>.Failure(result.Error);
            }

            if (result.Value)
            {
                _logger.LogInformation("Removed encrypted credential: {Key}", key);
            }
            else
            {
                _logger.LogDebug("Credential not found for removal: {Key}", key);
            }

            return result;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to remove encrypted credential: {Key}", key);
            return Result<bool>.Failure(new StorageError($"Failed to remove encrypted credential: {ex.Message}"));
        }
    }

    public async Task<Result<IReadOnlyList<string>>> GetExpiredCredentialKeysAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            _logger.LogDebug("Retrieving expired credential keys");

            var now = DateTime.UtcNow;
            var result = await _repository.QueryAsync<EncryptedCredentialEntity>(
                entity => entity.ExpiresAt.HasValue && entity.ExpiresAt.Value < now,
                cancellationToken);

            if (!result.IsSuccess)
            {
                return Result<IReadOnlyList<string>>.Failure(result.Error);
            }

            var keys = result.Value.Select(entity => entity.Key).ToList();

            _logger.LogDebug("Found {Count} expired credential keys", keys.Count);

            return Result<IReadOnlyList<string>>.Success(keys);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to retrieve expired credential keys");
            return Result<IReadOnlyList<string>>.Failure(
                new StorageError($"Failed to retrieve expired credential keys: {ex.Message}"));
        }
    }

    public async Task<Result<IReadOnlyList<string>>> GetAllCredentialKeysAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            _logger.LogDebug("Retrieving all credential keys");

            var result = await _repository.GetAllAsync<EncryptedCredentialEntity>(cancellationToken);

            if (!result.IsSuccess)
            {
                return Result<IReadOnlyList<string>>.Failure(result.Error);
            }

            var keys = result.Value.Select(entity => entity.Key).ToList();

            _logger.LogDebug("Found {Count} credential keys", keys.Count);

            return Result<IReadOnlyList<string>>.Success(keys);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to retrieve all credential keys");
            return Result<IReadOnlyList<string>>.Failure(
                new StorageError($"Failed to retrieve all credential keys: {ex.Message}"));
        }
    }

    public async Task<Result<IReadOnlyDictionary<string, string>>> GetEncryptedTokensAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            _logger.LogDebug("Retrieving all encrypted tokens");

            var result = await _repository.GetAllAsync<EncryptedTokenEntity>(cancellationToken);

            if (!result.IsSuccess)
            {
                return Result<IReadOnlyDictionary<string, string>>.Failure(result.Error);
            }

            var tokens = result.Value.ToDictionary(
                entity => entity.Provider,
                entity => entity.EncryptedToken);

            _logger.LogDebug("Retrieved {Count} encrypted tokens", tokens.Count);

            return Result<IReadOnlyDictionary<string, string>>.Success(tokens);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to retrieve encrypted tokens");
            return Result<IReadOnlyDictionary<string, string>>.Failure(
                new StorageError($"Failed to retrieve encrypted tokens: {ex.Message}"));
        }
    }

    public async Task<Result<bool>> SetEncryptedTokenAsync(
        string provider,
        string encryptedToken,
        CancellationToken cancellationToken = default)
    {
        try
        {
            if (string.IsNullOrWhiteSpace(provider))
            {
                return Result<bool>.Failure(new ValidationError("Provider name cannot be empty"));
            }

            if (string.IsNullOrWhiteSpace(encryptedToken))
            {
                return Result<bool>.Failure(new ValidationError("Token value cannot be empty"));
            }

            _logger.LogDebug("Setting encrypted token for provider: {Provider}", provider);

            // Check if token already exists
            var existingResult = await _repository.GetByIdAsync<EncryptedTokenEntity>(provider, cancellationToken);

            if (!existingResult.IsSuccess)
            {
                return Result<bool>.Failure(existingResult.Error);
            }

            var entity = new EncryptedTokenEntity
            {
                Provider = provider,
                EncryptedToken = encryptedToken,
                CreatedAt = DateTime.UtcNow
            };

            Result<bool> saveResult;
            if (existingResult.Value != null)
            {
                // Update existing token
                saveResult = await _repository.UpdateAsync(entity, cancellationToken);
                _logger.LogDebug("Updated encrypted token for provider: {Provider}", provider);
            }
            else
            {
                // Add new token
                saveResult = await _repository.AddAsync(entity, cancellationToken);
                _logger.LogDebug("Added new encrypted token for provider: {Provider}", provider);
            }

            if (!saveResult.IsSuccess)
            {
                return Result<bool>.Failure(saveResult.Error);
            }

            return Result<bool>.Success(true);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to set encrypted token for provider: {Provider}", provider);
            return Result<bool>.Failure(new StorageError($"Failed to set encrypted token: {ex.Message}"));
        }
    }

    public async Task<Result<int>> CleanupExpiredCredentialsAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            _logger.LogDebug("Cleaning up expired credentials");

            var expiredKeysResult = await GetExpiredCredentialKeysAsync(cancellationToken);

            if (!expiredKeysResult.IsSuccess)
            {
                return Result<int>.Failure(expiredKeysResult.Error);
            }

            var expiredKeys = expiredKeysResult.Value;

            if (expiredKeys.Count == 0)
            {
                _logger.LogDebug("No expired credentials to clean up");
                return Result<int>.Success(0);
            }

            // Get entities to delete
            var entitiesToDelete = new List<EncryptedCredentialEntity>();
            foreach (var key in expiredKeys)
            {
                var entityResult = await _repository.GetByIdAsync<EncryptedCredentialEntity>(key, cancellationToken);
                if (entityResult.IsSuccess && entityResult.Value != null)
                {
                    entitiesToDelete.Add(entityResult.Value);
                }
            }

            // Delete in batch
            var deleteResult = await _repository.DeleteRangeAsync(entitiesToDelete, cancellationToken);

            if (!deleteResult.IsSuccess)
            {
                return Result<int>.Failure(deleteResult.Error);
            }

            _logger.LogInformation("Cleaned up {Count} expired credentials", deleteResult.Value);

            return deleteResult;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to cleanup expired credentials");
            return Result<int>.Failure(new StorageError($"Failed to cleanup expired credentials: {ex.Message}"));
        }
    }
}
