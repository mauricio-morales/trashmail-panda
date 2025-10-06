using System;
using System.Threading;
using System.Threading.Tasks;
using Google.Apis.Auth.OAuth2;
using TrashMailPanda.Shared.Base;

namespace TrashMailPanda.Shared.Security;

/// <summary>
/// Unified service for handling Google OAuth2 authentication flows and token management
/// Provides both UI-driven OAuth flows and provider-level token management
/// Supports Gmail, Contacts, and other Google APIs with scope expansion
/// </summary>
public interface IGoogleOAuthService
{
    /// <summary>
    /// Retrieves an access token for the specified scopes
    /// If stored tokens exist and are valid, uses them; otherwise initiates OAuth flow
    /// </summary>
    /// <param name="scopes">Required OAuth scopes</param>
    /// <param name="storageKeyPrefix">Prefix for storing tokens (e.g., "gmail_", "contacts_")</param>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>A valid access token or error</returns>
    Task<Result<string>> GetAccessTokenAsync(
        string[] scopes,
        string storageKeyPrefix,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Retrieves a UserCredential for the specified scopes and storage prefix
    /// </summary>
    /// <param name="scopes">Required OAuth scopes</param>
    /// <param name="storageKeyPrefix">Prefix for storing tokens</param>
    /// <param name="clientId">OAuth client ID</param>
    /// <param name="clientSecret">OAuth client secret</param>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>A UserCredential or error</returns>
    Task<Result<UserCredential>> GetUserCredentialAsync(
        string[] scopes,
        string storageKeyPrefix,
        string clientId,
        string clientSecret,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Checks if valid tokens exist for the specified scopes and storage prefix
    /// </summary>
    /// <param name="scopes">Required OAuth scopes</param>
    /// <param name="storageKeyPrefix">Storage key prefix to check</param>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>True if valid tokens exist, false otherwise</returns>
    Task<Result<bool>> HasValidTokensAsync(
        string[] scopes,
        string storageKeyPrefix,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Revokes stored OAuth tokens for the specified storage prefix
    /// </summary>
    /// <param name="storageKeyPrefix">Storage key prefix to revoke</param>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>Result indicating success or failure</returns>
    Task<Result<bool>> RevokeTokensAsync(
        string storageKeyPrefix,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Expands OAuth scopes for existing authentication
    /// Checks if current tokens include required scopes, initiates re-auth if needed
    /// </summary>
    /// <param name="existingStorageKeyPrefix">Existing token storage prefix (e.g., "gmail_")</param>
    /// <param name="newScopes">Additional scopes needed</param>
    /// <param name="newStorageKeyPrefix">Storage prefix for expanded tokens</param>
    /// <param name="clientId">OAuth client ID</param>
    /// <param name="clientSecret">OAuth client secret</param>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>Result indicating if scope expansion was successful</returns>
    Task<Result<bool>> ExpandScopesAsync(
        string existingStorageKeyPrefix,
        string[] newScopes,
        string newStorageKeyPrefix,
        string clientId,
        string clientSecret,
        CancellationToken cancellationToken = default);

    // UI-Driven OAuth Flow Methods

    /// <summary>
    /// Initiate Google OAuth authentication flow in browser for specific scopes and storage prefix
    /// </summary>
    /// <param name="scopes">OAuth scopes to request</param>
    /// <param name="storageKeyPrefix">Storage prefix for tokens</param>
    /// <param name="clientId">OAuth client ID</param>
    /// <param name="clientSecret">OAuth client secret</param>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>Result indicating authentication success</returns>
    Task<Result<bool>> AuthenticateWithBrowserAsync(
        string[] scopes,
        string storageKeyPrefix,
        string clientId,
        string clientSecret,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Check if valid authentication exists for the specified scopes and storage prefix
    /// </summary>
    /// <param name="scopes">Required OAuth scopes</param>
    /// <param name="storageKeyPrefix">Storage prefix to check</param>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>True if valid authentication exists</returns>
    Task<Result<bool>> IsAuthenticatedAsync(
        string[] scopes,
        string storageKeyPrefix,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Clear stored authentication tokens for the specified storage prefix
    /// </summary>
    /// <param name="storageKeyPrefix">Storage prefix to clear</param>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>Result indicating success</returns>
    Task<Result<bool>> SignOutAsync(
        string storageKeyPrefix,
        CancellationToken cancellationToken = default);
}