using System;
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

            // Check if token is still valid or can be refreshed
            var validResult = await IsTokenValidAsync(storageKeyPrefix);
            if (validResult.IsSuccess)
            {
                return Result<bool>.Success(validResult.Value);
            }

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
            var tokenIssuedKey = $"{storageKeyPrefix}{TokenKeySuffixes.TOKEN_ISSUED_UTC}";
            var tokenExpiryKey = $"{storageKeyPrefix}{TokenKeySuffixes.TOKEN_EXPIRY}";

            var issuedResult = await _secureStorageManager.RetrieveCredentialAsync(tokenIssuedKey);
            var expiryResult = await _secureStorageManager.RetrieveCredentialAsync(tokenExpiryKey);

            if (!issuedResult.IsSuccess || !expiryResult.IsSuccess)
            {
                return Result<bool>.Success(false);
            }

            if (!DateTime.TryParse(issuedResult.Value, out var issuedUtc) ||
                !int.TryParse(expiryResult.Value, out var expiresInSeconds))
            {
                return Result<bool>.Success(false);
            }

            var expiresAt = issuedUtc.AddSeconds(expiresInSeconds);
            var isValid = DateTime.UtcNow < expiresAt.AddMinutes(-5); // 5 minute buffer

            return Result<bool>.Success(isValid);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to check token validity");
            return Result<bool>.Success(false);
        }
    }

    private async Task<Result<bool>> RefreshTokenAsync(string storageKeyPrefix)
    {
        try
        {
            var refreshTokenKey = $"{storageKeyPrefix}{TokenKeySuffixes.REFRESH_TOKEN}";
            var refreshTokenResult = await _secureStorageManager.RetrieveCredentialAsync(refreshTokenKey);

            if (!refreshTokenResult.IsSuccess || string.IsNullOrEmpty(refreshTokenResult.Value))
            {
                return Result<bool>.Failure(new AuthenticationError("No refresh token available"));
            }

            // TODO: Implement actual token refresh logic using Google APIs
            _logger.LogWarning("Token refresh not yet implemented");
            return Result<bool>.Failure(new AuthenticationError("Token refresh not implemented"));
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to refresh token");
            return Result<bool>.Failure(ex.ToProviderError("Failed to refresh token"));
        }
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

            // FORCE clear data store tokens to ensure fresh OAuth flow
            try
            {
                var existingToken = await _dataStore.GetAsync<Google.Apis.Auth.OAuth2.Responses.TokenResponse>("user");
                _logger.LogInformation("[OAUTH DEBUG] Existing token check - Token exists: {TokenExists}, Access token empty: {AccessTokenEmpty}",
                    existingToken != null,
                    existingToken?.AccessToken == null || string.IsNullOrEmpty(existingToken.AccessToken));

                if (existingToken != null)
                {
                    _logger.LogInformation("[OAUTH DEBUG] FORCING clear of data store tokens to ensure fresh OAuth flow");
                    await _dataStore.DeleteAsync<Google.Apis.Auth.OAuth2.Responses.TokenResponse>("user");
                    _logger.LogInformation("[OAUTH DEBUG] Data store tokens cleared - browser should now open");
                }
                else
                {
                    _logger.LogInformation("[OAUTH DEBUG] No existing tokens found in data store");
                }
            }
            catch (Exception ex)
            {
                _logger.LogInformation("[OAUTH DEBUG] Error checking/clearing existing tokens: {Error}", ex.Message);
            }

            // Use standard GoogleWebAuthorizationBroker with custom code receiver to ensure proper scope handling
            _logger.LogInformation("[OAUTH DEBUG] Using GoogleWebAuthorizationBroker with AvaloniaCodeReceiver to fix scope parameter handling");

            var codeReceiver = new AvaloniaCodeReceiver(_loggerFactory.CreateLogger<AvaloniaCodeReceiver>());

            // Use GoogleWebAuthorizationBroker.AuthorizeAsync with custom code receiver for proper scope handling
            var credential = await GoogleWebAuthorizationBroker.AuthorizeAsync(
                clientSecrets,
                scopes, // IEnumerable<string> - this should properly handle multiple scopes
                "user",
                cancellationToken,
                _dataStore,
                codeReceiver); // Use our custom code receiver to control browser opening

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

        var operations = new[]
        {
            ($"{storageKeyPrefix}{TokenKeySuffixes.ACCESS_TOKEN}", credential.Token.AccessToken),
            ($"{storageKeyPrefix}{TokenKeySuffixes.REFRESH_TOKEN}", credential.Token.RefreshToken ?? string.Empty),
            ($"{storageKeyPrefix}{TokenKeySuffixes.TOKEN_EXPIRY}", credential.Token.ExpiresInSeconds?.ToString() ?? "0"),
            ($"{storageKeyPrefix}{TokenKeySuffixes.TOKEN_ISSUED_UTC}", credential.Token.IssuedUtc.ToString("O")),
            ($"{storageKeyPrefix}{TokenKeySuffixes.TOKEN_TYPE}", credential.Token.TokenType ?? "Bearer"),
            ($"{storageKeyPrefix}{TokenKeySuffixes.SCOPES}", string.Join(",", scopes))
        };

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