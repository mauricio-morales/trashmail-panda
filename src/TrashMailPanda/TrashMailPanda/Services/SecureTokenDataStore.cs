using Google.Apis.Util.Store;
using Microsoft.Extensions.Logging;
using TrashMailPanda.Shared;
using TrashMailPanda.Shared.Security;
using TrashMailPanda.Models;

namespace TrashMailPanda.Services;

/// <summary>
/// Custom data store that integrates with our secure storage system
/// </summary>
public class SecureTokenDataStore : IDataStore, IDisposable
{
    private readonly ISecureStorageManager _secureStorageManager;
    private readonly ILogger<SecureTokenDataStore> _logger;
    private readonly SemaphoreSlim _semaphore = new(1, 1);
    private bool _disposed;

    public SecureTokenDataStore(ISecureStorageManager secureStorageManager, ILogger<SecureTokenDataStore> logger)
    {
        _secureStorageManager = secureStorageManager;
        _logger = logger;
    }

    public async Task StoreAsync<T>(string key, T value)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);

        await _semaphore.WaitAsync();
        try
        {
            if (value is Google.Apis.Auth.OAuth2.Responses.TokenResponse token)
            {
                // Store individual token components in secure storage
                if (!string.IsNullOrEmpty(token.AccessToken))
                {
                    await _secureStorageManager.StoreCredentialAsync(ProviderCredentialTypes.GoogleAccessToken, token.AccessToken);
                }

                if (!string.IsNullOrEmpty(token.RefreshToken))
                {
                    await _secureStorageManager.StoreCredentialAsync(ProviderCredentialTypes.GoogleRefreshToken, token.RefreshToken);
                }

                if (token.ExpiresInSeconds.HasValue)
                {
                    var expiry = DateTime.UtcNow.AddSeconds(token.ExpiresInSeconds.Value);
                    await _secureStorageManager.StoreCredentialAsync(ProviderCredentialTypes.GoogleTokenExpiry, expiry.ToString("O"));
                }

                _logger.LogDebug("Stored Gmail OAuth tokens securely");
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to store OAuth token for key {Key}", key);
            throw;
        }
        finally
        {
            _semaphore.Release();
        }
    }

    public async Task<T> GetAsync<T>(string key)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);

        await _semaphore.WaitAsync();
        try
        {
            if (typeof(T) == typeof(Google.Apis.Auth.OAuth2.Responses.TokenResponse))
            {
                var accessTokenResult = await _secureStorageManager.RetrieveCredentialAsync(ProviderCredentialTypes.GoogleAccessToken);
                var refreshTokenResult = await _secureStorageManager.RetrieveCredentialAsync(ProviderCredentialTypes.GoogleRefreshToken);
                var expiryResult = await _secureStorageManager.RetrieveCredentialAsync(ProviderCredentialTypes.GoogleTokenExpiry);

                if (accessTokenResult.IsSuccess || refreshTokenResult.IsSuccess)
                {
                    var token = new Google.Apis.Auth.OAuth2.Responses.TokenResponse
                    {
                        AccessToken = accessTokenResult.Value,
                        RefreshToken = refreshTokenResult.Value
                    };

                    if (expiryResult.IsSuccess && DateTime.TryParse(expiryResult.Value, out var expiry))
                    {
                        var secondsLeft = (expiry - DateTime.UtcNow).TotalSeconds;
                        token.ExpiresInSeconds = secondsLeft > 0 ? (long)secondsLeft : null;
                    }

                    return (T)(object)token;
                }
            }

            return default(T)!;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to retrieve OAuth token for key {Key}", key);
            return default(T)!;
        }
        finally
        {
            _semaphore.Release();
        }
    }

    public async Task DeleteAsync<T>(string key)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);

        await _semaphore.WaitAsync();
        try
        {
            await _secureStorageManager.RemoveCredentialAsync(ProviderCredentialTypes.GoogleAccessToken);
            await _secureStorageManager.RemoveCredentialAsync(ProviderCredentialTypes.GoogleRefreshToken);
            await _secureStorageManager.RemoveCredentialAsync(ProviderCredentialTypes.GoogleTokenExpiry);
            _logger.LogDebug("Deleted Gmail OAuth tokens for key {Key}", key);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to delete OAuth token for key {Key}", key);
            throw;
        }
        finally
        {
            _semaphore.Release();
        }
    }

    public Task ClearAsync()
    {
        // Clear all stored tokens
        return DeleteAsync<object>("user");
    }

    public void Dispose()
    {
        if (!_disposed)
        {
            _semaphore?.Dispose();
            _disposed = true;
        }
        GC.SuppressFinalize(this);
    }
}