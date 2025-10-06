using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Google.Apis.Auth.OAuth2;
using Google.Apis.Auth.OAuth2.Flows;
using Google.Apis.Auth.OAuth2.Responses;
using Google.Apis.Util.Store;
using Microsoft.Extensions.Logging;
using TrashMailPanda.Shared.Base;
using TrashMailPanda.Shared.Models;

namespace TrashMailPanda.Shared.Security;

/// <summary>
/// Shared Google OAuth2 service for handling authentication across Gmail and Contacts providers
/// Manages token storage, refresh, and scope expansion using secure storage
/// </summary>
public class GoogleOAuthService : IGoogleOAuthService
{
    private readonly ISecureStorageManager _secureStorageManager;
    private readonly ISecurityAuditLogger _securityAuditLogger;
    private readonly IDataStore _dataStore;
    private readonly ILogger<GoogleOAuthService> _logger;
    private readonly ILoggerFactory _loggerFactory;

    // Concurrency protection for token refresh operations
    private readonly SemaphoreSlim _refreshSemaphore = new(1, 1);
    private readonly ConcurrentDictionary<string, DateTime> _lastRefreshAttempts = new();

    /// <summary>
    /// Storage key suffixes for OAuth token components
    /// </summary>
    private static class TokenKeySuffixes
    {
        public const string ACCESS_TOKEN = "access_token";
        public const string REFRESH_TOKEN = "refresh_token";
        public const string TOKEN_EXPIRY = "token_expiry";
        public const string TOKEN_ISSUED_UTC = "token_issued_utc";
        public const string TOKEN_TYPE = "token_type";
        public const string SCOPES = "scopes";
    }

    public GoogleOAuthService(
        ISecureStorageManager secureStorageManager,
        ISecurityAuditLogger securityAuditLogger,
        IDataStore dataStore,
        ILogger<GoogleOAuthService> logger,
        ILoggerFactory loggerFactory)
    {
        _secureStorageManager = secureStorageManager ?? throw new ArgumentNullException(nameof(secureStorageManager));
        _securityAuditLogger = securityAuditLogger ?? throw new ArgumentNullException(nameof(securityAuditLogger));
        _dataStore = dataStore ?? throw new ArgumentNullException(nameof(dataStore));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _loggerFactory = loggerFactory ?? throw new ArgumentNullException(nameof(loggerFactory));
    }

    /// <inheritdoc />
    public async Task<Result<string>> GetAccessTokenAsync(
        string[] scopes,
        string storageKeyPrefix,
        CancellationToken cancellationToken = default)
    {
        try
        {
            _logger.LogDebug("Getting access token for scopes: {Scopes} with prefix: {Prefix}",
                string.Join(", ", scopes), storageKeyPrefix);

            // Try to retrieve existing access token
            var accessTokenKey = $"{storageKeyPrefix}{TokenKeySuffixes.ACCESS_TOKEN}";
            var accessTokenResult = await _secureStorageManager.RetrieveCredentialAsync(accessTokenKey);

            if (accessTokenResult.IsSuccess && !string.IsNullOrEmpty(accessTokenResult.Value))
            {
                // Check if token is still valid
                var isValidResult = await IsTokenValidAsync(storageKeyPrefix);
                if (isValidResult.IsSuccess && isValidResult.Value)
                {
                    _logger.LogDebug("Using existing valid access token");
                    return Result<string>.Success(accessTokenResult.Value);
                }

                // Token expired, try to refresh
                var refreshResult = await RefreshTokenAsync(storageKeyPrefix);
                if (refreshResult.IsSuccess)
                {
                    var refreshedTokenResult = await _secureStorageManager.RetrieveCredentialAsync(accessTokenKey);
                    if (refreshedTokenResult.IsSuccess && !string.IsNullOrEmpty(refreshedTokenResult.Value))
                    {
                        _logger.LogDebug("Using refreshed access token");
                        return Result<string>.Success(refreshedTokenResult.Value);
                    }
                }
            }

            _logger.LogInformation("No valid access token found, returning failure - OAuth flow required");
            return Result<string>.Failure(new AuthenticationError(
                "No valid access token available - OAuth flow required"));
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to get access token for prefix: {Prefix}", storageKeyPrefix);
            return Result<string>.Failure(ex.ToProviderError("Failed to retrieve access token"));
        }
    }

    /// <inheritdoc />
    public async Task<Result<UserCredential>> GetUserCredentialAsync(
        string[] scopes,
        string storageKeyPrefix,
        string clientId,
        string clientSecret,
        CancellationToken cancellationToken = default)
    {
        try
        {
            _logger.LogInformation("[OAUTH DEBUG] GetUserCredentialAsync called with prefix: {Prefix}, scopes: {Scopes}",
                storageKeyPrefix, string.Join(", ", scopes));

            // Try to create credential from stored tokens first
            _logger.LogInformation("[OAUTH DEBUG] Attempting to create credential from stored tokens...");
            var storedCredentialResult = await CreateUserCredentialFromStoredTokensAsync(
                storageKeyPrefix, clientId, clientSecret);

            if (storedCredentialResult.IsSuccess)
            {
                _logger.LogInformation("[OAUTH DEBUG] Successfully created UserCredential from stored tokens");
                return storedCredentialResult;
            }

            _logger.LogWarning("[OAUTH DEBUG] Failed to create credential from stored tokens: {Error}",
                storedCredentialResult.Error.Message);

            // No valid stored tokens, perform OAuth flow
            _logger.LogInformation("[OAUTH DEBUG] Would perform OAuth flow for prefix: {Prefix}, but returning failure", storageKeyPrefix);
            return Result<UserCredential>.Failure(storedCredentialResult.Error);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to get UserCredential for prefix: {Prefix}", storageKeyPrefix);
            return Result<UserCredential>.Failure(ex.ToProviderError("Failed to get user credential"));
        }
    }

    /// <inheritdoc />
    public async Task<Result<bool>> HasValidTokensAsync(
        string[] scopes,
        string storageKeyPrefix,
        CancellationToken cancellationToken = default)
    {
        try
        {
            // Check if access and refresh tokens exist
            var accessTokenKey = $"{storageKeyPrefix}{TokenKeySuffixes.ACCESS_TOKEN}";
            var refreshTokenKey = $"{storageKeyPrefix}{TokenKeySuffixes.REFRESH_TOKEN}";

            var accessTokenResult = await _secureStorageManager.RetrieveCredentialAsync(accessTokenKey);
            var refreshTokenResult = await _secureStorageManager.RetrieveCredentialAsync(refreshTokenKey);

            if (!accessTokenResult.IsSuccess || !refreshTokenResult.IsSuccess)
            {
                _logger.LogDebug("No stored tokens found for prefix: {Prefix}", storageKeyPrefix);
                return Result<bool>.Success(false);
            }

            // Check if stored scopes include required scopes
            var scopesResult = await CheckStoredScopesAsync(scopes, storageKeyPrefix);
            if (!scopesResult.IsSuccess || !scopesResult.Value)
            {
                _logger.LogDebug("Stored scopes do not match required scopes for prefix: {Prefix}", storageKeyPrefix);
                return Result<bool>.Success(false);
            }

            // Check if token is still valid
            var validResult = await IsTokenValidAsync(storageKeyPrefix);
            if (validResult.IsSuccess && validResult.Value)
            {
                _logger.LogDebug("Tokens are still valid for prefix: {Prefix}", storageKeyPrefix);
                return Result<bool>.Success(true);
            }

            if (!validResult.IsSuccess)
            {
                // Token validation failed due to corrupted data - clear the tokens
                _logger.LogWarning("Token validation failed for prefix: {Prefix}, clearing potentially corrupted tokens", storageKeyPrefix);
                await ClearTokensAsync(storageKeyPrefix);
                return Result<bool>.Success(false);
            }

            // Token is expired but validation succeeded - attempt refresh
            _logger.LogInformation("Tokens are expired for prefix: {Prefix}, attempting automatic refresh", storageKeyPrefix);
            var refreshResult = await RefreshTokenAsync(storageKeyPrefix);

            if (refreshResult.IsSuccess)
            {
                _logger.LogInformation("Successfully refreshed expired tokens for prefix: {Prefix}", storageKeyPrefix);
                return Result<bool>.Success(true);
            }

            // Refresh failed - determine if we should clear tokens or allow retry
            _logger.LogWarning("Failed to refresh expired tokens for prefix: {Prefix}: {Error}", storageKeyPrefix, refreshResult.Error?.Message);

            // If refresh failed due to invalid refresh token, the RefreshTokenAsync method already cleared tokens
            // For other errors, we keep tokens to allow manual retry
            return Result<bool>.Success(false);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to check token validity for prefix: {Prefix}", storageKeyPrefix);
            return Result<bool>.Failure(ex.ToProviderError("Failed to check token validity"));
        }
    }

    /// <inheritdoc />
    public async Task<Result<bool>> RevokeTokensAsync(
        string storageKeyPrefix,
        CancellationToken cancellationToken = default)
    {
        try
        {
            _logger.LogInformation("Revoking tokens for prefix: {Prefix}", storageKeyPrefix);

            var keysToRevoke = new[]
            {
                $"{storageKeyPrefix}{TokenKeySuffixes.ACCESS_TOKEN}",
                $"{storageKeyPrefix}{TokenKeySuffixes.REFRESH_TOKEN}",
                $"{storageKeyPrefix}{TokenKeySuffixes.TOKEN_EXPIRY}",
                $"{storageKeyPrefix}{TokenKeySuffixes.TOKEN_ISSUED_UTC}",
                $"{storageKeyPrefix}{TokenKeySuffixes.TOKEN_TYPE}",
                $"{storageKeyPrefix}{TokenKeySuffixes.SCOPES}"
            };

            var errors = new List<string>();
            var operationsSucceeded = 0;

            foreach (var key in keysToRevoke)
            {
                try
                {
                    await _secureStorageManager.RemoveCredentialAsync(key);
                    operationsSucceeded++;

                    await _securityAuditLogger.LogCredentialOperationAsync(new CredentialOperationEvent
                    {
                        Operation = "Delete",
                        CredentialKey = key,
                        Success = true,
                        UserContext = "Google OAuth Service"
                    });
                }
                catch (Exception ex)
                {
                    var errorMessage = $"Failed to remove credential {key}: {ex.Message}";
                    errors.Add(errorMessage);
                    _logger.LogError(ex, "Failed to remove stored credential {Key}", key);

                    await _securityAuditLogger.LogCredentialOperationAsync(new CredentialOperationEvent
                    {
                        Operation = "Delete",
                        CredentialKey = key,
                        Success = false,
                        ErrorMessage = errorMessage,
                        UserContext = "Google OAuth Service"
                    });
                }
            }

            if (operationsSucceeded == keysToRevoke.Length)
            {
                _logger.LogInformation("Successfully revoked all tokens for prefix: {Prefix}", storageKeyPrefix);
                return Result<bool>.Success(true);
            }

            if (operationsSucceeded > 0)
            {
                var partialMessage = $"Revoked {operationsSucceeded}/{keysToRevoke.Length} tokens. Errors: {string.Join("; ", errors)}";
                _logger.LogWarning("Partial success revoking tokens: {Message}", partialMessage);
                return Result<bool>.Success(true);
            }

            var fullErrorMessage = $"Failed to revoke any tokens: {string.Join("; ", errors)}";
            _logger.LogError("Failed to revoke tokens for prefix: {Prefix}. {Error}", storageKeyPrefix, fullErrorMessage);
            return Result<bool>.Failure(new AuthenticationError(fullErrorMessage));
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Exception revoking tokens for prefix: {Prefix}", storageKeyPrefix);
            return Result<bool>.Failure(ex.ToProviderError("Failed to revoke tokens"));
        }
    }

    /// <inheritdoc />
    public async Task<Result<bool>> ExpandScopesAsync(
        string existingStorageKeyPrefix,
        string[] newScopes,
        string newStorageKeyPrefix,
        string clientId,
        string clientSecret,
        CancellationToken cancellationToken = default)
    {
        try
        {
            _logger.LogInformation("Expanding scopes from {ExistingPrefix} to {NewPrefix} with additional scopes: {Scopes}",
                existingStorageKeyPrefix, newStorageKeyPrefix, string.Join(", ", newScopes));

            // Check if we already have tokens with the required scopes
            var hasValidResult = await HasValidTokensAsync(newScopes, newStorageKeyPrefix, cancellationToken);
            if (hasValidResult.IsSuccess && hasValidResult.Value)
            {
                _logger.LogDebug("Required scopes already available in target storage");
                return Result<bool>.Success(true);
            }

            // Get existing scopes
            var existingScopesResult = await GetStoredScopesAsync(existingStorageKeyPrefix);
            if (!existingScopesResult.IsSuccess)
            {
                _logger.LogWarning("No existing tokens found for scope expansion from prefix: {Prefix}",
                    existingStorageKeyPrefix);
                return Result<bool>.Failure(new AuthenticationError(
                    "No existing authentication found for scope expansion"));
            }

            // Combine existing and new scopes
            var existingScopes = existingScopesResult.Value;
            var allScopes = existingScopes.Union(newScopes).ToArray();

            // Perform OAuth flow with combined scopes
            var credentialResult = await PerformOAuthFlowAsync(
                allScopes, newStorageKeyPrefix, clientId, clientSecret, cancellationToken);

            if (credentialResult.IsSuccess)
            {
                _logger.LogInformation("Successfully expanded OAuth scopes");
                return Result<bool>.Success(true);
            }

            return Result<bool>.Failure(credentialResult.Error);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to expand OAuth scopes");
            return Result<bool>.Failure(ex.ToProviderError("Failed to expand OAuth scopes"));
        }
    }

    // Private helper methods

    private async Task<Result<UserCredential>> CreateUserCredentialFromStoredTokensAsync(
        string storageKeyPrefix,
        string clientId,
        string clientSecret)
    {
        try
        {
            var accessTokenKey = $"{storageKeyPrefix}{TokenKeySuffixes.ACCESS_TOKEN}";
            var refreshTokenKey = $"{storageKeyPrefix}{TokenKeySuffixes.REFRESH_TOKEN}";
            var tokenExpiryKey = $"{storageKeyPrefix}{TokenKeySuffixes.TOKEN_EXPIRY}";
            var tokenIssuedKey = $"{storageKeyPrefix}{TokenKeySuffixes.TOKEN_ISSUED_UTC}";

            _logger.LogInformation("[OAUTH DEBUG] Looking for tokens with keys: access={AccessKey}, refresh={RefreshKey}",
                accessTokenKey, refreshTokenKey);

            var accessTokenResult = await _secureStorageManager.RetrieveCredentialAsync(accessTokenKey);
            var refreshTokenResult = await _secureStorageManager.RetrieveCredentialAsync(refreshTokenKey);

            _logger.LogInformation("[OAUTH DEBUG] Access token result: {AccessSuccess}, Refresh token result: {RefreshSuccess}",
                accessTokenResult.IsSuccess, refreshTokenResult.IsSuccess);

            if (!accessTokenResult.IsSuccess || !refreshTokenResult.IsSuccess)
            {
                _logger.LogWarning("[OAUTH DEBUG] No stored tokens found - access: {AccessError}, refresh: {RefreshError}",
                    accessTokenResult.IsSuccess ? "OK" : accessTokenResult.ErrorMessage ?? "null",
                    refreshTokenResult.IsSuccess ? "OK" : refreshTokenResult.ErrorMessage ?? "null");
                return Result<UserCredential>.Failure(new AuthenticationError("No stored tokens found"));
            }

            var accessToken = accessTokenResult.Value;
            var refreshToken = refreshTokenResult.Value;

            _logger.LogInformation("[OAUTH DEBUG] Retrieved tokens - access empty: {AccessEmpty}, refresh empty: {RefreshEmpty}",
                string.IsNullOrEmpty(accessToken), string.IsNullOrEmpty(refreshToken));

            if (string.IsNullOrEmpty(accessToken) || string.IsNullOrEmpty(refreshToken))
            {
                _logger.LogWarning("[OAUTH DEBUG] Stored tokens are empty");
                return Result<UserCredential>.Failure(new AuthenticationError("Stored tokens are empty"));
            }

            // Create OAuth flow
            var flow = new GoogleAuthorizationCodeFlow(new GoogleAuthorizationCodeFlow.Initializer
            {
                ClientSecrets = new ClientSecrets { ClientId = clientId, ClientSecret = clientSecret },
                DataStore = _dataStore
            });

            // Get token expiry and issued time
            var expiresInSeconds = 0;
            var issuedUtc = DateTime.UtcNow;

            var tokenExpiryResult = await _secureStorageManager.RetrieveCredentialAsync(tokenExpiryKey);
            if (tokenExpiryResult.IsSuccess && int.TryParse(tokenExpiryResult.Value, out var storedExpiry))
            {
                expiresInSeconds = storedExpiry;
            }

            var tokenIssuedResult = await _secureStorageManager.RetrieveCredentialAsync(tokenIssuedKey);
            if (tokenIssuedResult.IsSuccess && DateTime.TryParse(tokenIssuedResult.Value, out var storedIssuedUtc))
            {
                issuedUtc = storedIssuedUtc;
            }

            // Create token response
            var tokenResponse = new TokenResponse
            {
                AccessToken = accessToken,
                RefreshToken = refreshToken,
                ExpiresInSeconds = expiresInSeconds,
                IssuedUtc = issuedUtc,
                TokenType = "Bearer"
            };

            var userCredential = new UserCredential(flow, "user", tokenResponse);

            // Test and refresh token if needed
            if (tokenResponse.IsStale)
            {
                var refreshResult = await userCredential.RefreshTokenAsync(CancellationToken.None);
                if (!refreshResult)
                {
                    return Result<UserCredential>.Failure(new AuthenticationError("Failed to refresh expired token"));
                }

                // Store refreshed tokens
                await StoreCredentialsAsync(userCredential, storageKeyPrefix, new string[] { });
            }

            return Result<UserCredential>.Success(userCredential);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to create UserCredential from stored tokens");
            return Result<UserCredential>.Failure(ex.ToProviderError("Failed to create credential from stored tokens"));
        }
    }

    private async Task<Result<UserCredential>> PerformOAuthFlowAsync(
        string[] scopes,
        string storageKeyPrefix,
        string clientId,
        string clientSecret,
        CancellationToken cancellationToken)
    {
        try
        {
            var clientSecrets = new ClientSecrets
            {
                ClientId = clientId,
                ClientSecret = clientSecret
            };

            var codeReceiver = new AvaloniaCodeReceiver(_loggerFactory.CreateLogger<AvaloniaCodeReceiver>());

            var credential = await GoogleWebAuthorizationBroker.AuthorizeAsync(
                clientSecrets,
                scopes,
                "user",
                cancellationToken,
                _dataStore,
                codeReceiver);

            // Store credentials
            await StoreCredentialsAsync(credential, storageKeyPrefix, scopes);

            _logger.LogInformation("OAuth flow completed successfully for prefix: {Prefix}", storageKeyPrefix);
            return Result<UserCredential>.Success(credential);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "OAuth flow failed for prefix: {Prefix}", storageKeyPrefix);
            return Result<UserCredential>.Failure(ex.ToProviderError("OAuth authentication failed"));
        }
    }

    private async Task<Result<bool>> IsTokenValidAsync(string storageKeyPrefix)
    {
        try
        {
            _logger.LogInformation("[TOKEN VALID DEBUG] Checking token validity for prefix: {Prefix}", storageKeyPrefix);

            var tokenIssuedKey = $"{storageKeyPrefix}{TokenKeySuffixes.TOKEN_ISSUED_UTC}";
            var tokenExpiryKey = $"{storageKeyPrefix}{TokenKeySuffixes.TOKEN_EXPIRY}";

            var issuedResult = await _secureStorageManager.RetrieveCredentialAsync(tokenIssuedKey);
            var expiryResult = await _secureStorageManager.RetrieveCredentialAsync(tokenExpiryKey);

            _logger.LogInformation("[TOKEN VALID DEBUG] Retrieval results - IssuedResult: Success={IssuedSuccess}, Value='{IssuedValue}', ExpiryResult: Success={ExpirySuccess}, Value='{ExpiryValue}'",
                issuedResult.IsSuccess,
                issuedResult.IsSuccess ? issuedResult.Value : "null",
                expiryResult.IsSuccess,
                expiryResult.IsSuccess ? expiryResult.Value : "null");

            if (!issuedResult.IsSuccess || !expiryResult.IsSuccess)
            {
                _logger.LogInformation("[TOKEN VALID DEBUG] Token retrieval failed - returning false");
                return Result<bool>.Success(false);
            }

            var issuedParseSuccess = DateTime.TryParse(issuedResult.Value, null, System.Globalization.DateTimeStyles.RoundtripKind, out var issuedUtc);
            var expiryParseSuccess = int.TryParse(expiryResult.Value, out var expiresInSeconds);

            _logger.LogInformation("[TOKEN VALID DEBUG] Parsing results - IssuedParse: Success={IssuedParseSuccess}, Value={IssuedUtc}, ExpiryParse: Success={ExpiryParseSuccess}, Value={ExpiresInSeconds}",
                issuedParseSuccess,
                issuedParseSuccess ? issuedUtc.ToString("O") : "failed",
                expiryParseSuccess,
                expiryParseSuccess ? expiresInSeconds : -1);

            if (!issuedParseSuccess || !expiryParseSuccess)
            {
                _logger.LogInformation("[TOKEN VALID DEBUG] Token parsing failed - returning false");
                return Result<bool>.Success(false);
            }

            // Validate expiresInSeconds is reasonable (between 0 and 10 years in seconds)
            if (expiresInSeconds < 0 || expiresInSeconds > 315360000) // 10 years = 315360000 seconds
            {
                _logger.LogWarning("Token expiry seconds value is invalid: {ExpiresInSeconds}", expiresInSeconds);
                return Result<bool>.Success(false);
            }

            // Safely add seconds with overflow protection
            DateTime expiresAt;
            try
            {
                expiresAt = issuedUtc.AddSeconds(expiresInSeconds);
                _logger.LogInformation("[TOKEN VALID DEBUG] Calculated expiry - IssuedUtc: {IssuedUtc}, ExpiresInSeconds: {ExpiresInSeconds}, ExpiresAt: {ExpiresAt}",
                    issuedUtc.ToString("O"), expiresInSeconds, expiresAt.ToString("O"));
            }
            catch (ArgumentOutOfRangeException)
            {
                _logger.LogWarning("DateTime overflow when calculating token expiry: IssuedUtc={IssuedUtc}, ExpiresInSeconds={ExpiresInSeconds}",
                    issuedUtc, expiresInSeconds);
                return Result<bool>.Success(false);
            }

            // Safely subtract 5 minutes with overflow protection
            DateTime bufferedExpiresAt;
            try
            {
                bufferedExpiresAt = expiresAt.AddMinutes(-5); // 5 minute buffer
                _logger.LogInformation("[TOKEN VALID DEBUG] Applied buffer - ExpiresAt: {ExpiresAt}, BufferedExpiresAt: {BufferedExpiresAt}, CurrentUtc: {CurrentUtc}",
                    expiresAt.ToString("O"), bufferedExpiresAt.ToString("O"), DateTime.UtcNow.ToString("O"));
            }
            catch (ArgumentOutOfRangeException)
            {
                _logger.LogWarning("DateTime overflow when applying 5-minute buffer to token expiry: ExpiresAt={ExpiresAt}",
                    expiresAt);
                return Result<bool>.Success(false);
            }

            var isValid = DateTime.UtcNow < bufferedExpiresAt;

            return Result<bool>.Success(isValid);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to check token validity");
            return Result<bool>.Success(false);
        }
    }

    /// <summary>
    /// Clears all stored tokens for the given storage key prefix
    /// </summary>
    /// <param name="storageKeyPrefix">The storage key prefix for the tokens to clear</param>
    /// <returns>A task representing the async operation</returns>
    private async Task ClearTokensAsync(string storageKeyPrefix)
    {
        try
        {
            var keysToRemove = new[]
            {
                $"{storageKeyPrefix}{TokenKeySuffixes.ACCESS_TOKEN}",
                $"{storageKeyPrefix}{TokenKeySuffixes.REFRESH_TOKEN}",
                $"{storageKeyPrefix}{TokenKeySuffixes.TOKEN_ISSUED_UTC}",
                $"{storageKeyPrefix}{TokenKeySuffixes.TOKEN_EXPIRY}",
                $"{storageKeyPrefix}{TokenKeySuffixes.TOKEN_TYPE}",
                $"{storageKeyPrefix}{TokenKeySuffixes.SCOPES}"
            };

            foreach (var key in keysToRemove)
            {
                await _secureStorageManager.RemoveCredentialAsync(key);
            }

            _logger.LogInformation("Cleared all tokens for prefix: {Prefix}", storageKeyPrefix);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to clear tokens for prefix: {Prefix}", storageKeyPrefix);
        }
    }

    private async Task<Result<bool>> RefreshTokenAsync(string storageKeyPrefix)
    {
        const int lockTimeoutMs = 30000; // 30 second timeout

        _logger.LogInformation("[TOKEN REFRESH] Starting token refresh for prefix: {Prefix}", storageKeyPrefix);

        // Concurrency protection - only one refresh at a time per prefix
        if (!await _refreshSemaphore.WaitAsync(lockTimeoutMs))
        {
            _logger.LogWarning("[TOKEN REFRESH] Token refresh timeout - another refresh in progress for prefix: {Prefix}", storageKeyPrefix);
            return Result<bool>.Failure(new AuthenticationError("Token refresh timeout - another refresh operation in progress"));
        }

        try
        {
            // Check if another thread just completed refresh while we were waiting
            var isValidResult = await IsTokenValidAsync(storageKeyPrefix);
            if (isValidResult.IsSuccess && isValidResult.Value)
            {
                _logger.LogInformation("[TOKEN REFRESH] Token was refreshed by another thread for prefix: {Prefix}", storageKeyPrefix);
                return Result<bool>.Success(true);
            }

            // Check if we've attempted refresh recently to avoid rapid retry loops
            if (_lastRefreshAttempts.TryGetValue(storageKeyPrefix, out var lastAttempt))
            {
                var timeSinceLastAttempt = DateTime.UtcNow - lastAttempt;
                if (timeSinceLastAttempt < TimeSpan.FromMinutes(1))
                {
                    _logger.LogWarning("[TOKEN REFRESH] Recent refresh attempt failed, waiting before retry. Last attempt: {LastAttempt}", lastAttempt);
                    return Result<bool>.Failure(new AuthenticationError("Recent refresh attempt failed - please wait before retrying"));
                }
            }

            // Record this refresh attempt
            _lastRefreshAttempts[storageKeyPrefix] = DateTime.UtcNow;

            var refreshTokenKey = $"{storageKeyPrefix}{TokenKeySuffixes.REFRESH_TOKEN}";
            var refreshTokenResult = await _secureStorageManager.RetrieveCredentialAsync(refreshTokenKey);

            if (!refreshTokenResult.IsSuccess || string.IsNullOrEmpty(refreshTokenResult.Value))
            {
                _logger.LogWarning("[TOKEN REFRESH] No refresh token available for prefix: {Prefix}", storageKeyPrefix);
                return Result<bool>.Failure(new AuthenticationError("No refresh token available - re-authentication required"));
            }

            var refreshToken = refreshTokenResult.Value;
            _logger.LogDebug("[TOKEN REFRESH] Found refresh token for prefix: {Prefix}", storageKeyPrefix);

            // Get client credentials from secure storage (needed for token refresh)
            var clientIdResult = await _secureStorageManager.RetrieveCredentialAsync(ProviderCredentialTypes.GoogleClientId);
            var clientSecretResult = await _secureStorageManager.RetrieveCredentialAsync(ProviderCredentialTypes.GoogleClientSecret);

            if (!clientIdResult.IsSuccess || !clientSecretResult.IsSuccess ||
                string.IsNullOrEmpty(clientIdResult.Value) || string.IsNullOrEmpty(clientSecretResult.Value))
            {
                _logger.LogWarning("[TOKEN REFRESH] No client credentials available for token refresh");
                return Result<bool>.Failure(new AuthenticationError("Client credentials not available for token refresh"));
            }

            var clientId = clientIdResult.Value;
            var clientSecret = clientSecretResult.Value;

            _logger.LogDebug("[TOKEN REFRESH] Creating GoogleAuthorizationCodeFlow for token refresh");

            // Create OAuth flow for token refresh
            var flow = new GoogleAuthorizationCodeFlow(new GoogleAuthorizationCodeFlow.Initializer
            {
                ClientSecrets = new ClientSecrets
                {
                    ClientId = clientId,
                    ClientSecret = clientSecret
                },
                DataStore = _dataStore
            });

            // Create TokenResponse with current refresh token
            var currentTokenResponse = new TokenResponse
            {
                RefreshToken = refreshToken
            };

            _logger.LogInformation("[TOKEN REFRESH] Attempting to refresh access token using Google APIs");

            // Use Google's built-in token refresh mechanism
            var refreshedToken = await flow.RefreshTokenAsync("user", refreshToken, CancellationToken.None);

            if (refreshedToken == null)
            {
                _logger.LogWarning("[TOKEN REFRESH] Google API returned null refreshed token");
                return await HandleRefreshFailure(new InvalidOperationException("Google API returned null refreshed token"), storageKeyPrefix);
            }

            _logger.LogInformation("[TOKEN REFRESH] Successfully refreshed token - new expiry: {ExpiresInSeconds} seconds",
                refreshedToken.ExpiresInSeconds ?? 0);

            // Store the refreshed tokens atomically
            await StoreRefreshedTokensAsync(refreshedToken, storageKeyPrefix, refreshToken);

            // Clear the refresh attempt record on success
            _lastRefreshAttempts.TryRemove(storageKeyPrefix, out _);

            // Log audit events for token refresh
            await _securityAuditLogger.LogCredentialOperationAsync(new CredentialOperationEvent
            {
                Operation = "Refresh",
                CredentialKey = storageKeyPrefix,
                Success = true,
                UserContext = "Google OAuth Service - Automatic Token Refresh"
            });

            _logger.LogInformation("[TOKEN REFRESH] Token refresh completed successfully for prefix: {Prefix}", storageKeyPrefix);
            return Result<bool>.Success(true);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "[TOKEN REFRESH] Failed to refresh token for prefix: {Prefix}", storageKeyPrefix);
            return await HandleRefreshFailure(ex, storageKeyPrefix);
        }
        finally
        {
            _refreshSemaphore.Release();
        }
    }

    /// <summary>
    /// Handles token refresh failures with appropriate error classification and recovery
    /// </summary>
    private async Task<Result<bool>> HandleRefreshFailure(Exception ex, string storageKeyPrefix)
    {
        var reason = ClassifyRefreshFailure(ex);

        // Log audit event for failed refresh
        await _securityAuditLogger.LogCredentialOperationAsync(new CredentialOperationEvent
        {
            Operation = "Refresh",
            CredentialKey = storageKeyPrefix,
            Success = false,
            ErrorMessage = ex.Message,
            UserContext = "Google OAuth Service - Automatic Token Refresh"
        });

        switch (reason)
        {
            case TokenRefreshFailureReason.InvalidRefreshToken:
            case TokenRefreshFailureReason.RevokedToken:
                _logger.LogWarning("[TOKEN REFRESH] Refresh token is invalid or revoked, clearing tokens for prefix: {Prefix}", storageKeyPrefix);
                await ClearTokensAsync(storageKeyPrefix);
                return Result<bool>.Failure(new AuthenticationError("Refresh token is invalid - re-authentication required"));

            case TokenRefreshFailureReason.NetworkError:
            case TokenRefreshFailureReason.ServerError:
                _logger.LogWarning("[TOKEN REFRESH] Temporary failure refreshing token for prefix: {Prefix}: {Error}", storageKeyPrefix, ex.Message);
                // Don't clear tokens - let caller retry
                return Result<bool>.Failure(new AuthenticationError($"Temporary refresh failure: {ex.Message}"));

            default:
                _logger.LogError("[TOKEN REFRESH] Unknown refresh failure for prefix: {Prefix}: {Error}", storageKeyPrefix, ex.Message);
                return Result<bool>.Failure(ex.ToProviderError("Token refresh failed"));
        }
    }

    /// <summary>
    /// Classifies refresh failures to determine appropriate recovery action
    /// </summary>
    private TokenRefreshFailureReason ClassifyRefreshFailure(Exception ex)
    {
        var message = ex?.Message?.ToLowerInvariant() ?? string.Empty;

        if (message.Contains("invalid_grant") || message.Contains("invalid refresh token"))
        {
            return TokenRefreshFailureReason.InvalidRefreshToken;
        }

        if (message.Contains("revoked") || message.Contains("unauthorized"))
        {
            return TokenRefreshFailureReason.RevokedToken;
        }

        if (message.Contains("network") || message.Contains("timeout") || message.Contains("connection"))
        {
            return TokenRefreshFailureReason.NetworkError;
        }

        if (message.Contains("server error") || message.Contains("internal error") || message.Contains("503") || message.Contains("502"))
        {
            return TokenRefreshFailureReason.ServerError;
        }

        return TokenRefreshFailureReason.Unknown;
    }

    /// <summary>
    /// Stores refreshed tokens atomically to secure storage
    /// </summary>
    private async Task StoreRefreshedTokensAsync(TokenResponse refreshedToken, string storageKeyPrefix, string originalRefreshToken)
    {
        var storeOperations = new List<Task>();

        var accessTokenKey = $"{storageKeyPrefix}{TokenKeySuffixes.ACCESS_TOKEN}";
        var tokenExpiryKey = $"{storageKeyPrefix}{TokenKeySuffixes.TOKEN_EXPIRY}";
        var tokenIssuedKey = $"{storageKeyPrefix}{TokenKeySuffixes.TOKEN_ISSUED_UTC}";
        var tokenTypeKey = $"{storageKeyPrefix}{TokenKeySuffixes.TOKEN_TYPE}";
        var refreshTokenKey = $"{storageKeyPrefix}{TokenKeySuffixes.REFRESH_TOKEN}";

        // Store new access token
        storeOperations.Add(_secureStorageManager.StoreCredentialAsync(accessTokenKey, refreshedToken.AccessToken ?? string.Empty));

        // Store new expiry information
        storeOperations.Add(_secureStorageManager.StoreCredentialAsync(tokenExpiryKey, (refreshedToken.ExpiresInSeconds ?? 3600).ToString()));

        // Store new issued time
        storeOperations.Add(_secureStorageManager.StoreCredentialAsync(tokenIssuedKey, refreshedToken.IssuedUtc.ToString("O")));

        // Store token type
        storeOperations.Add(_secureStorageManager.StoreCredentialAsync(tokenTypeKey, refreshedToken.TokenType ?? "Bearer"));

        // Update refresh token if a new one was provided, otherwise keep the original
        var refreshTokenToStore = !string.IsNullOrEmpty(refreshedToken.RefreshToken) ? refreshedToken.RefreshToken : originalRefreshToken;
        storeOperations.Add(_secureStorageManager.StoreCredentialAsync(refreshTokenKey, refreshTokenToStore));

        if (!string.IsNullOrEmpty(refreshedToken.RefreshToken) && refreshedToken.RefreshToken != originalRefreshToken)
        {
            _logger.LogDebug("[TOKEN REFRESH] Storing updated refresh token");
        }

        // Wait for all storage operations to complete
        await Task.WhenAll(storeOperations);
    }

    /// <summary>
    /// Token refresh failure reasons for error classification
    /// </summary>
    private enum TokenRefreshFailureReason
    {
        Unknown,
        NetworkError,           // Retry with backoff
        InvalidRefreshToken,    // Clear tokens, force re-auth
        RevokedToken,          // Clear tokens, force re-auth
        QuotaExceeded,         // Retry with longer backoff
        ServerError            // Retry with backoff
    }

    private async Task<Result<bool>> CheckStoredScopesAsync(string[] requiredScopes, string storageKeyPrefix)
    {
        try
        {
            var scopesResult = await GetStoredScopesAsync(storageKeyPrefix);
            if (!scopesResult.IsSuccess)
            {
                return Result<bool>.Success(false);
            }

            var storedScopes = scopesResult.Value;
            var hasAllScopes = requiredScopes.All(scope => storedScopes.Contains(scope));

            return Result<bool>.Success(hasAllScopes);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to check stored scopes");
            return Result<bool>.Success(false);
        }
    }

    private async Task<Result<string[]>> GetStoredScopesAsync(string storageKeyPrefix)
    {
        try
        {
            var scopesKey = $"{storageKeyPrefix}{TokenKeySuffixes.SCOPES}";
            var scopesResult = await _secureStorageManager.RetrieveCredentialAsync(scopesKey);

            if (!scopesResult.IsSuccess)
            {
                return Result<string[]>.Failure(new AuthenticationError("No stored scopes found"));
            }

            var scopes = scopesResult.Value.Split(',', StringSplitOptions.RemoveEmptyEntries);
            return Result<string[]>.Success(scopes);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to get stored scopes");
            return Result<string[]>.Failure(ex.ToProviderError("Failed to retrieve stored scopes"));
        }
    }

    // UI-Driven OAuth Flow Methods Implementation

    /// <inheritdoc />
    public async Task<Result<bool>> AuthenticateWithBrowserAsync(
        string[] scopes,
        string storageKeyPrefix,
        string clientId,
        string clientSecret,
        CancellationToken cancellationToken = default)
    {
        try
        {
            _logger.LogInformation("Starting Google OAuth authentication flow for scopes: {Scopes}",
                string.Join(", ", scopes));

            _logger.LogInformation("[OAUTH DEBUG] Client credentials - ID length: {ClientIdLength}, Secret length: {ClientSecretLength}",
                clientId?.Length ?? 0, clientSecret?.Length ?? 0);

            var clientSecrets = new ClientSecrets
            {
                ClientId = clientId,
                ClientSecret = clientSecret
            };

            _logger.LogInformation("[OAUTH DEBUG] About to call GoogleWebAuthorizationBroker.AuthorizeAsync - this should open browser if no valid tokens exist");

            // Check existing tokens for debugging
            try
            {
                var existingToken = await _dataStore.GetAsync<Google.Apis.Auth.OAuth2.Responses.TokenResponse>("user");
                _logger.LogInformation("[OAUTH DEBUG] Existing token check - Token exists: {TokenExists}, Access token empty: {AccessTokenEmpty}",
                    existingToken != null,
                    existingToken?.AccessToken == null || string.IsNullOrEmpty(existingToken.AccessToken));
            }
            catch (Exception ex)
            {
                _logger.LogInformation("[OAUTH DEBUG] Error checking existing tokens: {Error}", ex.Message);
            }

            // Use standard GoogleWebAuthorizationBroker with default code receiver for desktop apps
            _logger.LogInformation("[OAUTH DEBUG] Using GoogleWebAuthorizationBroker with default code receiver for proper redirect URI handling");

            // Use GoogleWebAuthorizationBroker.AuthorizeAsync with default code receiver for desktop apps
            var credential = await GoogleWebAuthorizationBroker.AuthorizeAsync(
                clientSecrets,
                scopes, // IEnumerable<string> - this should properly handle multiple scopes
                "user",
                cancellationToken,
                _dataStore); // Use default code receiver that handles localhost properly

            _logger.LogInformation("[OAUTH DEBUG] GoogleWebAuthorizationBroker completed successfully");

            _logger.LogInformation("[OAUTH DEBUG] GoogleWebAuthorizationBroker.AuthorizeAsync completed, credential is null: {CredentialNull}",
                credential == null);

            if (credential != null)
            {
                // Store credentials using our secure storage
                await StoreCredentialsAsync(credential, storageKeyPrefix, scopes);

                _logger.LogInformation("Google OAuth authentication completed successfully");
                return Result<bool>.Success(true);
            }

            return Result<bool>.Failure(new AuthenticationError("OAuth authentication was cancelled or failed"));
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Google OAuth authentication failed");
            return Result<bool>.Failure(ex.ToProviderError("OAuth authentication failed"));
        }
    }

    /// <inheritdoc />
    public async Task<Result<bool>> IsAuthenticatedAsync(
        string[] scopes,
        string storageKeyPrefix,
        CancellationToken cancellationToken = default)
    {
        // Reuse the existing HasValidTokensAsync method
        return await HasValidTokensAsync(scopes, storageKeyPrefix, cancellationToken);
    }

    /// <inheritdoc />
    public async Task<Result<bool>> SignOutAsync(
        string storageKeyPrefix,
        CancellationToken cancellationToken = default)
    {
        // Reuse the existing RevokeTokensAsync method
        return await RevokeTokensAsync(storageKeyPrefix, cancellationToken);
    }

    private async Task StoreCredentialsAsync(UserCredential credential, string storageKeyPrefix, string[] scopes)
    {
        if (credential?.Token == null)
        {
            throw new ArgumentException("Credential or token is null", nameof(credential));
        }

        _logger.LogInformation("[TOKEN STORE DEBUG] Storing credentials for prefix: {Prefix}", storageKeyPrefix);
        _logger.LogInformation("[TOKEN STORE DEBUG] Token details - ExpiresInSeconds: {ExpiresInSeconds}, IssuedUtc: {IssuedUtc}, TokenType: {TokenType}",
            credential.Token.ExpiresInSeconds,
            credential.Token.IssuedUtc.ToString("O"),
            credential.Token.TokenType);

        var operations = new[]
        {
            ($"{storageKeyPrefix}{TokenKeySuffixes.ACCESS_TOKEN}", credential.Token.AccessToken),
            ($"{storageKeyPrefix}{TokenKeySuffixes.REFRESH_TOKEN}", credential.Token.RefreshToken ?? string.Empty),
            ($"{storageKeyPrefix}{TokenKeySuffixes.TOKEN_EXPIRY}", credential.Token.ExpiresInSeconds?.ToString() ?? "0"),
            ($"{storageKeyPrefix}{TokenKeySuffixes.TOKEN_ISSUED_UTC}", credential.Token.IssuedUtc.ToString("O")),
            ($"{storageKeyPrefix}{TokenKeySuffixes.TOKEN_TYPE}", credential.Token.TokenType ?? "Bearer"),
            ($"{storageKeyPrefix}{TokenKeySuffixes.SCOPES}", string.Join(",", scopes))
        };

        _logger.LogInformation("[TOKEN STORE DEBUG] About to store - TOKEN_EXPIRY: '{TokenExpiry}', TOKEN_ISSUED_UTC: '{TokenIssuedUtc}'",
            credential.Token.ExpiresInSeconds?.ToString() ?? "0",
            credential.Token.IssuedUtc.ToString("O"));

        foreach (var (key, value) in operations)
        {
            try
            {
                await _secureStorageManager.StoreCredentialAsync(key, value);

                await _securityAuditLogger.LogCredentialOperationAsync(new CredentialOperationEvent
                {
                    Operation = "Store",
                    CredentialKey = key,
                    Success = true,
                    UserContext = "Google OAuth Service"
                });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to store credential {Key}", key);

                await _securityAuditLogger.LogCredentialOperationAsync(new CredentialOperationEvent
                {
                    Operation = "Store",
                    CredentialKey = key,
                    Success = false,
                    ErrorMessage = ex.Message,
                    UserContext = "Google OAuth Service"
                });
            }
        }
    }
}