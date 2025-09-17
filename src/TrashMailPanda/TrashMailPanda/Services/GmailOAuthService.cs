using Google.Apis.Auth.OAuth2;
using Google.Apis.Gmail.v1;
using Google.Apis.Services;
using Google.Apis.Util.Store;
using Microsoft.Extensions.Logging;
using System.Diagnostics;
using System.Threading;
using TrashMailPanda.Models;
using TrashMailPanda.Shared;
using TrashMailPanda.Shared.Base;
using TrashMailPanda.Shared.Security;

namespace TrashMailPanda.Services;

/// <summary>
/// Service for handling Gmail OAuth authentication flows
/// Manages browser-based OAuth sign-in and token storage
/// </summary>
public class GmailOAuthService : IGmailOAuthService
{
    private readonly ISecureStorageManager _secureStorageManager;
    private readonly ILogger<GmailOAuthService> _logger;
    private readonly IDataStore _dataStore;
    private readonly string[] _scopes = GoogleOAuthScopes.GmailWithContacts;

    public GmailOAuthService(
        ISecureStorageManager secureStorageManager,
        ILogger<GmailOAuthService> logger,
        IDataStore dataStore)
    {
        _secureStorageManager = secureStorageManager ?? throw new ArgumentNullException(nameof(secureStorageManager));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _dataStore = dataStore ?? throw new ArgumentNullException(nameof(dataStore));
    }

    /// <summary>
    /// Initiate Gmail OAuth authentication flow in browser
    /// </summary>
    public async Task<Result<bool>> AuthenticateAsync()
    {
        try
        {
            _logger.LogInformation("Starting Gmail OAuth authentication flow");

            // Retrieve OAuth client credentials (shared Google credentials)
            var clientIdResult = await _secureStorageManager.RetrieveCredentialAsync(ProviderCredentialTypes.GoogleClientId);
            var clientSecretResult = await _secureStorageManager.RetrieveCredentialAsync(ProviderCredentialTypes.GoogleClientSecret);

            _logger.LogDebug("Retrieved OAuth credentials - ClientId success: {ClientIdSuccess}, ClientSecret success: {ClientSecretSuccess}",
                clientIdResult.IsSuccess, clientSecretResult.IsSuccess);

            if (!clientIdResult.IsSuccess || !clientSecretResult.IsSuccess ||
                string.IsNullOrEmpty(clientIdResult.Value) || string.IsNullOrEmpty(clientSecretResult.Value))
            {
                _logger.LogWarning("OAuth client credentials validation failed - ClientId: {ClientIdStatus}, ClientSecret: {ClientSecretStatus}",
                    clientIdResult.IsSuccess ? "present" : "missing",
                    clientSecretResult.IsSuccess ? "present" : "missing");
                return Result<bool>.Failure(new ConfigurationError("Gmail OAuth client credentials not configured"));
            }

            _logger.LogDebug("OAuth client credentials validated successfully - ClientId length: {ClientIdLength}",
                clientIdResult.Value?.Length ?? 0);

            // Additional debug logging to see actual values (masked)
            var clientId = clientIdResult.Value;
            var clientSecret = clientSecretResult.Value;

            _logger.LogInformation("[OAUTH DEBUG] About to create ClientSecrets with ClientId: {ClientIdPreview} (length: {ClientIdLength})",
                string.IsNullOrEmpty(clientId) ? "NULL/EMPTY" : $"{clientId.Substring(0, Math.Min(12, clientId.Length))}...",
                clientId?.Length ?? 0);

            var clientSecrets = new ClientSecrets
            {
                ClientId = clientId,
                ClientSecret = clientSecret
            };

            _logger.LogInformation("[OAUTH DEBUG] ClientSecrets object created - ClientId is null: {ClientIdNull}, ClientSecret is null: {ClientSecretNull}",
                string.IsNullOrEmpty(clientSecrets.ClientId), string.IsNullOrEmpty(clientSecrets.ClientSecret));

            // Request OAuth2 authorization - this will open browser
            var credential = await GoogleWebAuthorizationBroker.AuthorizeAsync(
                clientSecrets,
                _scopes,
                "user",
                CancellationToken.None,
                _dataStore);

            if (credential != null)
            {
                // Store user email for display purposes
                var gmailService = new GmailService(new BaseClientService.Initializer()
                {
                    HttpClientInitializer = credential,
                    ApplicationName = "TrashMail Panda"
                });

                try
                {
                    var profile = await gmailService.Users.GetProfile("me").ExecuteAsync();
                    if (profile?.EmailAddress != null)
                    {
                        await _secureStorageManager.StoreCredentialAsync(ProviderCredentialTypes.GoogleUserEmail, profile.EmailAddress);
                    }
                    else
                    {
                        _logger.LogWarning("Gmail profile or email address was null during OAuth flow");
                    }
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "Could not retrieve user profile, but authentication succeeded");
                }

                _logger.LogInformation("Gmail OAuth authentication completed successfully");
                return Result<bool>.Success(true);
            }

            return Result<bool>.Failure(new AuthenticationError("OAuth authentication was cancelled or failed"));
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Exception during Gmail OAuth authentication");
            return Result<bool>.Failure(new AuthenticationError($"Authentication failed: {ex.Message}"));
        }
    }

    /// <summary>
    /// Check if valid Gmail authentication exists
    /// </summary>
    public async Task<Result<bool>> IsAuthenticatedAsync()
    {
        try
        {
            var refreshTokenResult = await _secureStorageManager.RetrieveCredentialAsync(ProviderCredentialTypes.GoogleRefreshToken);
            return Result<bool>.Success(refreshTokenResult.IsSuccess && !string.IsNullOrEmpty(refreshTokenResult.Value));
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Exception checking Gmail authentication status");
            return Result<bool>.Failure(new ProcessingError($"Authentication check failed: {ex.Message}"));
        }
    }

    /// <summary>
    /// Clear stored Gmail authentication tokens
    /// </summary>
    public async Task<Result<bool>> SignOutAsync()
    {
        try
        {
            _logger.LogInformation("Clearing Gmail authentication tokens");

            await _secureStorageManager.RemoveCredentialAsync(ProviderCredentialTypes.GoogleAccessToken);
            await _secureStorageManager.RemoveCredentialAsync(ProviderCredentialTypes.GoogleRefreshToken);
            await _secureStorageManager.RemoveCredentialAsync(ProviderCredentialTypes.GoogleTokenExpiry);
            await _secureStorageManager.RemoveCredentialAsync(ProviderCredentialTypes.GoogleUserEmail);

            return Result<bool>.Success(true);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Exception clearing Gmail authentication tokens");
            return Result<bool>.Failure(new ProcessingError($"Sign out failed: {ex.Message}"));
        }
    }
}

