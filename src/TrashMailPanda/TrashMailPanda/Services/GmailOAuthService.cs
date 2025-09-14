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
    private readonly string[] _scopes = { GmailService.Scope.GmailModify };

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

            if (!clientIdResult.IsSuccess || !clientSecretResult.IsSuccess ||
                string.IsNullOrEmpty(clientIdResult.Value) || string.IsNullOrEmpty(clientSecretResult.Value))
            {
                return Result<bool>.Failure(new ConfigurationError("Gmail OAuth client credentials not configured"));
            }

            var clientSecrets = new ClientSecrets
            {
                ClientId = clientIdResult.Value,
                ClientSecret = clientSecretResult.Value
            };

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
                        await _secureStorageManager.StoreCredentialAsync(ProviderCredentialTypes.GmailUserEmail, profile.EmailAddress);
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
            var refreshTokenResult = await _secureStorageManager.RetrieveCredentialAsync(ProviderCredentialTypes.GmailRefreshToken);
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

            await _secureStorageManager.RemoveCredentialAsync(ProviderCredentialTypes.GmailAccessToken);
            await _secureStorageManager.RemoveCredentialAsync(ProviderCredentialTypes.GmailRefreshToken);
            await _secureStorageManager.RemoveCredentialAsync(ProviderCredentialTypes.GmailTokenExpiry);
            await _secureStorageManager.RemoveCredentialAsync(ProviderCredentialTypes.GmailUserEmail);

            return Result<bool>.Success(true);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Exception clearing Gmail authentication tokens");
            return Result<bool>.Failure(new ProcessingError($"Sign out failed: {ex.Message}"));
        }
    }
}

