using System.Diagnostics;
using System.Net.Http;
using System.Runtime.InteropServices;
using System.Text.Json;
using Google.Apis.Auth.OAuth2;
using Google.Apis.Auth.OAuth2.Flows;
using Google.Apis.Auth.OAuth2.Responses;
using Microsoft.Extensions.Logging;
using Spectre.Console;
using TrashMailPanda.Models;
using TrashMailPanda.Providers.Email.Models;
using TrashMailPanda.Shared.Base;
using TrashMailPanda.Shared.Security;

namespace TrashMailPanda.Services;

/// <summary>
/// Google OAuth 2.0 handler for Gmail authentication
/// </summary>
public class GoogleOAuthHandler : IGoogleOAuthHandler
{
    private readonly ISecureStorageManager _secureStorage;
    private readonly ILogger<GoogleOAuthHandler> _logger;
    private readonly Func<ILocalOAuthCallbackListener> _listenerFactory;

    public GoogleOAuthHandler(
        ISecureStorageManager secureStorage,
        ILogger<GoogleOAuthHandler> logger,
        Func<ILocalOAuthCallbackListener> listenerFactory)
    {
        _secureStorage = secureStorage;
        _logger = logger;
        _listenerFactory = listenerFactory;
    }

    /// <summary>
    /// Execute complete OAuth authentication flow
    /// </summary>
    public async Task<Result<OAuthFlowResult>> AuthenticateAsync(
        OAuthConfiguration configuration,
        CancellationToken cancellationToken = default)
    {
        try
        {
            _logger.LogInformation("Starting OAuth authentication flow");

            // 1. Generate PKCE pair
            AnsiConsole.MarkupLine("[cyan]ℹ Generating security credentials (PKCE)...[/]");
            var pkcePair = PKCEGenerator.GeneratePKCEPair();

            // 2. Start localhost HTTP listener
            AnsiConsole.MarkupLine("[cyan]ℹ Starting local callback listener...[/]");

            await using var listener = _listenerFactory();
            var portResult = await listener.StartAsync("/oauth/callback", cancellationToken);

            if (!portResult.IsSuccess)
            {
                return Result<OAuthFlowResult>.Failure(portResult.Error);
            }

            var port = portResult.Value;
            var redirectUri = listener.GetRedirectUri("/oauth/callback");

            _logger.LogDebug("OAuth callback listener started on port {Port}", port);

            // 3. Build authorization URL
            var state = Guid.NewGuid().ToString("N");
            var authUrl = BuildAuthorizationUrl(
                configuration.ClientId,
                redirectUri,
                configuration.Scopes,
                state,
                pkcePair.CodeChallenge);

            _logger.LogDebug("Authorization URL built with state: {State}", state);

            // 4. Launch browser
            AnsiConsole.MarkupLine("[blue]ℹ Opening browser for Gmail authentication...[/]");
            AnsiConsole.MarkupLine("[yellow]Note: You have {0} minutes to complete authentication[/]",
                configuration.Timeout.TotalMinutes);

            var browserResult = await LaunchBrowserAsync(authUrl);

            if (!browserResult.IsSuccess)
            {
                // Use OAuthErrorHandler for consistent manual URL display
                OAuthErrorHandler.DisplayManualUrlInstructions(authUrl);
            }

            // 5. Wait for OAuth callback
            AnsiConsole.MarkupLine("[cyan]ℹ Waiting for authorization in browser...[/]");
            var callbackResult = await listener.WaitForCallbackAsync(state, configuration.Timeout);

            if (!callbackResult.IsSuccess)
            {
                return Result<OAuthFlowResult>.Failure(callbackResult.Error);
            }

            var callbackData = callbackResult.Value;

            if (callbackData.IsError)
            {
                _logger.LogWarning("OAuth callback returned error: {Error}", callbackData.Error);
                return Result<OAuthFlowResult>.Failure(
                    new AuthenticationError($"User denied authorization: {callbackData.ErrorDescription}"));
            }

            if (!callbackData.IsValid || string.IsNullOrEmpty(callbackData.Code))
            {
                return Result<OAuthFlowResult>.Failure(
                    new AuthenticationError("Invalid OAuth callback - authorization code missing"));
            }

            // 6. Exchange authorization code for tokens
            AnsiConsole.MarkupLine("[cyan]ℹ Exchanging authorization code for tokens...[/]");

            var tokenResult = await ExchangeCodeForTokensAsync(
                configuration.ClientId,
                configuration.ClientSecret,
                callbackData.Code,
                redirectUri,
                pkcePair.CodeVerifier,
                cancellationToken);

            if (!tokenResult.IsSuccess)
            {
                return Result<OAuthFlowResult>.Failure(tokenResult.Error);
            }

            // 7. Store tokens in OS keychain
            var storeResult = await StoreTokensAsync(tokenResult.Value);

            if (!storeResult.IsSuccess)
            {
                return Result<OAuthFlowResult>.Failure(storeResult.Error);
            }

            AnsiConsole.MarkupLine("[green]✓ Authentication successful![/]");

            if (!string.IsNullOrEmpty(tokenResult.Value.UserEmail))
            {
                AnsiConsole.MarkupLine("[cyan]Authenticated as:[/] {0}", tokenResult.Value.UserEmail);
            }

            _logger.LogInformation("OAuth authentication completed successfully");

            return Result<OAuthFlowResult>.Success(tokenResult.Value);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "OAuth authentication failed");
            OAuthErrorHandler.DisplayError(ex, allowRetry: true, logger: _logger);
            return Result<OAuthFlowResult>.Failure(new AuthenticationError($"OAuth authentication failed: {ex.Message}"));
        }
    }

    /// <summary>
    /// Refresh access token using stored refresh token
    /// </summary>
    public async Task<Result<OAuthFlowResult>> RefreshTokenAsync(
        string refreshToken,
        OAuthConfiguration clientConfig,
        CancellationToken cancellationToken = default)
    {
        try
        {
            _logger.LogInformation("Refreshing OAuth access token");

            AnsiConsole.Status()
                .Spinner(Spinner.Known.Dots)
                .Start("[cyan]Refreshing access token...[/]", ctx =>
                {
                    // Status display only
                });

            // Create Google OAuth flow
            var flow = new GoogleAuthorizationCodeFlow(new GoogleAuthorizationCodeFlow.Initializer
            {
                ClientSecrets = new ClientSecrets
                {
                    ClientId = clientConfig.ClientId,
                    ClientSecret = clientConfig.ClientSecret
                }
            });

            // Refresh token
            var tokenResponse = await flow.RefreshTokenAsync(
                "user",
                refreshToken,
                cancellationToken);

            if (tokenResponse == null)
            {
                return Result<OAuthFlowResult>.Failure(
                    new AuthenticationError("Token refresh failed - no response received"));
            }

            // Create OAuthFlowResult
            var result = new OAuthFlowResult
            {
                AccessToken = tokenResponse.AccessToken,
                RefreshToken = tokenResponse.RefreshToken ?? refreshToken,
                ExpiresInSeconds = tokenResponse.ExpiresInSeconds ?? 3600,
                IssuedUtc = tokenResponse.IssuedUtc,
                Scopes = tokenResponse.Scope?.Split(' ') ?? Array.Empty<string>(),
                TokenType = tokenResponse.TokenType ?? "Bearer"
            };

            // Store updated tokens
            var storeResult = await StoreTokensAsync(result);

            if (!storeResult.IsSuccess)
            {
                return Result<OAuthFlowResult>.Failure(storeResult.Error);
            }

            AnsiConsole.MarkupLine("[green]✓ Access token refreshed successfully[/]");

            _logger.LogInformation("OAuth access token refreshed successfully");

            return Result<OAuthFlowResult>.Success(result);
        }
        catch (Google.GoogleApiException ex) when (ex.Error?.Errors?.Any(e => e.Reason == "invalid_grant") == true)
        {
            _logger.LogWarning("Refresh token revoked or invalid");
            AnsiConsole.MarkupLine("[red]✗ Refresh token revoked - re-authentication required[/]");

            await ClearAuthenticationAsync();

            return Result<OAuthFlowResult>.Failure(
                new AuthenticationError("Refresh token revoked. Please re-authenticate."));
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Token refresh failed");
            OAuthErrorHandler.DisplayError(ex, allowRetry: true, logger: _logger);
            return Result<OAuthFlowResult>.Failure(new AuthenticationError($"Token refresh failed: {ex.Message}"));
        }
    }

    /// <summary>
    /// Check if OAuth is configured
    /// </summary>
    public async Task<Result<bool>> IsConfiguredAsync()
    {
        try
        {
            var clientIdResult = await _secureStorage.RetrieveCredentialAsync(GmailStorageKeys.CLIENT_ID);
            var clientSecretResult = await _secureStorage.RetrieveCredentialAsync(GmailStorageKeys.CLIENT_SECRET);

            var isConfigured = clientIdResult.IsSuccess && clientSecretResult.IsSuccess &&
                              !string.IsNullOrWhiteSpace(clientIdResult.Value) &&
                              !string.IsNullOrWhiteSpace(clientSecretResult.Value);

            return Result<bool>.Success(isConfigured);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error checking OAuth configuration");
            return Result<bool>.Failure(new ConfigurationError($"Failed to check configuration: {ex.Message}"));
        }
    }

    /// <summary>
    /// Clear all OAuth tokens
    /// </summary>
    public async Task<Result<bool>> ClearAuthenticationAsync()
    {
        try
        {
            _logger.LogInformation("Clearing OAuth authentication");

            var keys = new[]
            {
                GmailStorageKeys.ACCESS_TOKEN,
                GmailStorageKeys.REFRESH_TOKEN,
                GmailStorageKeys.TOKEN_EXPIRY,
                GmailStorageKeys.TOKEN_ISSUED_UTC,
                GmailStorageKeys.USER_EMAIL
            };

            foreach (var key in keys)
            {
                await _secureStorage.RemoveCredentialAsync(key);
            }

            AnsiConsole.MarkupLine("[yellow]⚠ Authentication cleared[/]");

            _logger.LogInformation("OAuth tokens cleared successfully");

            return Result<bool>.Success(true);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error clearing OAuth tokens");
            return Result<bool>.Failure(new ProcessingError($"Failed to clear tokens: {ex.Message}"));
        }
    }

    #region Private Helper Methods

    /// <summary>
    /// Build Google OAuth authorization URL
    /// </summary>
    private string BuildAuthorizationUrl(
        string clientId,
        string redirectUri,
        string[] scopes,
        string state,
        string codeChallenge)
    {
        var scopeString = string.Join(" ", scopes);
        var encodedRedirectUri = Uri.EscapeDataString(redirectUri);
        var encodedScope = Uri.EscapeDataString(scopeString);

        return $"https://accounts.google.com/o/oauth2/v2/auth?" +
               $"client_id={clientId}&" +
               $"redirect_uri={encodedRedirectUri}&" +
               $"response_type=code&" +
               $"scope={encodedScope}&" +
               $"state={state}&" +
               $"code_challenge={codeChallenge}&" +
               $"code_challenge_method=S256&" +
               $"access_type=offline&" +
               $"prompt=consent";
    }

    /// <summary>
    /// Launch system browser with URL
    /// </summary>
    private async Task<Result<bool>> LaunchBrowserAsync(string url)
    {
        try
        {
            if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
            {
                Process.Start(new ProcessStartInfo(url) { UseShellExecute = true });
            }
            else if (RuntimeInformation.IsOSPlatform(OSPlatform.OSX))
            {
                Process.Start("open", url);
            }
            else if (RuntimeInformation.IsOSPlatform(OSPlatform.Linux))
            {
                Process.Start("xdg-open", url);
            }
            else
            {
                return Result<bool>.Failure(new NetworkError("Unsupported operating system for browser launch"));
            }

            _logger.LogDebug("Browser launched with OAuth URL");

            return Result<bool>.Success(true);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to launch browser");
            return Result<bool>.Failure(new NetworkError($"Browser launch failed: {ex.Message}"));
        }
    }

    /// <summary>
    /// Exchange authorization code for OAuth tokens using direct HTTP POST to include PKCE code_verifier
    /// </summary>
    private async Task<Result<OAuthFlowResult>> ExchangeCodeForTokensAsync(
        string clientId,
        string clientSecret,
        string authorizationCode,
        string redirectUri,
        string codeVerifier,
        CancellationToken cancellationToken)
    {
        try
        {
            // Use direct HTTP POST because the Google client library's ExchangeCodeForTokenAsync
            // does not support passing the PKCE code_verifier parameter
            using var httpClient = new HttpClient();

            var parameters = new Dictionary<string, string>
            {
                ["code"] = authorizationCode,
                ["client_id"] = clientId,
                ["client_secret"] = clientSecret,
                ["redirect_uri"] = redirectUri,
                ["grant_type"] = "authorization_code",
                ["code_verifier"] = codeVerifier
            };

            var response = await httpClient.PostAsync(
                "https://oauth2.googleapis.com/token",
                new FormUrlEncodedContent(parameters),
                cancellationToken);

            var responseBody = await response.Content.ReadAsStringAsync(cancellationToken);

            if (!response.IsSuccessStatusCode)
            {
                _logger.LogError("Token exchange HTTP {Status}: {Body}", response.StatusCode, responseBody);
                return Result<OAuthFlowResult>.Failure(
                    new AuthenticationError($"Token exchange failed: {responseBody}"));
            }

            var json = JsonDocument.Parse(responseBody).RootElement;

            var accessToken = json.GetProperty("access_token").GetString() ?? string.Empty;
            var refreshToken = json.TryGetProperty("refresh_token", out var rt) ? rt.GetString() ?? string.Empty : string.Empty;
            var expiresIn = json.TryGetProperty("expires_in", out var exp) ? exp.GetInt64() : 3600;
            var tokenType = json.TryGetProperty("token_type", out var tt) ? tt.GetString() ?? "Bearer" : "Bearer";
            var scope = json.TryGetProperty("scope", out var sc) ? sc.GetString() ?? string.Empty : string.Empty;

            var result = new OAuthFlowResult
            {
                AccessToken = accessToken,
                RefreshToken = refreshToken,
                ExpiresInSeconds = expiresIn,
                IssuedUtc = DateTime.UtcNow,
                Scopes = scope.Split(' ', StringSplitOptions.RemoveEmptyEntries),
                UserEmail = null, // populated later from Gmail API profile if needed
                TokenType = tokenType
            };

            _logger.LogDebug("Token exchange successful - expires in {Expiry} seconds", result.ExpiresInSeconds);

            return Result<OAuthFlowResult>.Success(result);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Token exchange failed");
            return Result<OAuthFlowResult>.Failure(
                new AuthenticationError($"Token exchange failed: {ex.Message}"));
        }
    }

    /// <summary>
    /// Store OAuth tokens in secure storage
    /// </summary>
    private async Task<Result<bool>> StoreTokensAsync(OAuthFlowResult tokens)
    {
        try
        {
            _logger.LogDebug("Storing OAuth tokens in secure storage");

            await _secureStorage.StoreCredentialAsync(GmailStorageKeys.ACCESS_TOKEN, tokens.AccessToken);
            await _secureStorage.StoreCredentialAsync(GmailStorageKeys.REFRESH_TOKEN, tokens.RefreshToken);
            await _secureStorage.StoreCredentialAsync(GmailStorageKeys.TOKEN_EXPIRY,
                tokens.ExpiresInSeconds.ToString());
            await _secureStorage.StoreCredentialAsync(GmailStorageKeys.TOKEN_ISSUED_UTC,
                tokens.IssuedUtc.ToString("O"));

            if (!string.IsNullOrEmpty(tokens.UserEmail))
            {
                await _secureStorage.StoreCredentialAsync(GmailStorageKeys.USER_EMAIL, tokens.UserEmail);
            }

            _logger.LogInformation("OAuth tokens stored successfully");

            return Result<bool>.Success(true);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to store OAuth tokens");
            return Result<bool>.Failure(new ProcessingError($"Failed to store tokens: {ex.Message}"));
        }
    }

    #endregion
}
