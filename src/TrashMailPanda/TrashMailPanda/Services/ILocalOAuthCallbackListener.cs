using TrashMailPanda.Models;
using TrashMailPanda.Shared.Base;

namespace TrashMailPanda.Services;

/// <summary>
/// Localhost HTTP listener for OAuth callback handling
/// Implements temporary HTTP server on 127.0.0.1 with dynamic port
/// </summary>
public interface ILocalOAuthCallbackListener : IAsyncDisposable
{
    /// <summary>
    /// Start HTTP listener on localhost with dynamic port
    /// </summary>
    /// <param name="callbackPath">URL path for OAuth callback (default: "/oauth/callback")</param>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>Port number assigned by OS</returns>
    /// <remarks>
    /// Uses HttpListener with prefix: http://127.0.0.1:0{callbackPath}
    /// Port 0 = OS selects available port dynamically
    /// </remarks>
    Task<Result<int>> StartAsync(
        string callbackPath = "/oauth/callback",
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Get complete redirect URI for OAuth authorization URL
    /// </summary>
    /// <param name="callbackPath">URL path for callback</param>
    /// <returns>Full URL (e.g., http://127.0.0.1:54321/oauth/callback)</returns>
    string GetRedirectUri(string callbackPath = "/oauth/callback");

    /// <summary>
    /// Wait for OAuth callback with timeout
    /// </summary>
    /// <param name="expectedState">Expected state parameter for CSRF validation</param>
    /// <param name="timeout">Maximum wait time (default: 5 minutes)</param>
    /// <returns>OAuthCallbackData with authorization code or error</returns>
    /// <remarks>
    /// Blocks until:
    /// - HTTP callback received with authorization code
    /// - Timeout expires (default 5 minutes)
    /// - Cancellation requested
    /// 
    /// Validates:
    /// - Request origin is 127.0.0.1 (localhost only)
    /// - State parameter matches expectedState (CSRF protection)
    /// - Authorization code is present (no error parameter)
    /// </remarks>
    Task<Result<OAuthCallbackData>> WaitForCallbackAsync(
        string expectedState,
        TimeSpan? timeout = null);

    /// <summary>
    /// Stop HTTP listener and clean up resources
    /// </summary>
    /// <returns>Result indicating success or failure</returns>
    Task<Result<bool>> StopAsync();
}
