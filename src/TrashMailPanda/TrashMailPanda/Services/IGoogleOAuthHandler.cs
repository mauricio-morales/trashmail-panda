using TrashMailPanda.Models;
using TrashMailPanda.Shared.Base;

namespace TrashMailPanda.Services;

/// <summary>
/// Google OAuth 2.0 handler for Gmail authentication
/// Implements authorization code flow with PKCE and localhost callback
/// </summary>
public interface IGoogleOAuthHandler
{
    /// <summary>
    /// Execute complete OAuth authentication flow
    /// </summary>
    /// <param name="configuration">OAuth client credentials and settings</param>
    /// <param name="cancellationToken">Cancellation token for flow timeout</param>
    /// <returns>Result containing OAuth tokens or error</returns>
    /// <remarks>
    /// Flow steps:
    /// 1. Generate PKCE pair (code_verifier + code_challenge)
    /// 2. Start localhost HTTP listener on dynamic port
    /// 3. Build authorization URL with PKCE challenge
    /// 4. Launch system browser with authorization URL
    /// 5. Wait for OAuth callback (up to 5 minutes)
    /// 6. Exchange authorization code for tokens
    /// 7. Store tokens in OS keychain
    /// </remarks>
    Task<Result<OAuthFlowResult>> AuthenticateAsync(
        OAuthConfiguration configuration,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Refresh access token using stored refresh token
    /// </summary>
    /// <param name="refreshToken">Existing refresh token</param>
    /// <param name="clientConfig">OAuth client credentials</param>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>Result containing new OAuth tokens or error</returns>
    /// <remarks>
    /// Returns failure with AuthenticationError if refresh token is revoked (invalid_grant).
    /// Caller should trigger full re-authentication on refresh failure.
    /// </remarks>
    Task<Result<OAuthFlowResult>> RefreshTokenAsync(
        string refreshToken,
        OAuthConfiguration clientConfig,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Validate stored OAuth configuration exists
    /// </summary>
    /// <returns>Result indicating whether OAuth is configured</returns>
    /// <remarks>
    /// Checks for:
    /// - Client ID exists in SecureStorageManager
    /// - Client secret exists in SecureStorageManager
    /// Does NOT validate token freshness - use ITokenValidator for that
    /// </remarks>
    Task<Result<bool>> IsConfiguredAsync();

    /// <summary>
    /// Clear all stored OAuth tokens and configuration
    /// </summary>
    /// <returns>Result indicating success or failure</returns>
    /// <remarks>
    /// Used for:
    /// - User-initiated sign-out
    /// - Refresh token revoked (invalid_grant error)
    /// - Switching Gmail accounts
    /// </remarks>
    Task<Result<bool>> ClearAuthenticationAsync();
}
