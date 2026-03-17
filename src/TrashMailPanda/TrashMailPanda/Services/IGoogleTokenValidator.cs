using TrashMailPanda.Models;
using TrashMailPanda.Shared.Base;

namespace TrashMailPanda.Services;

/// <summary>
/// Validates Google OAuth token state and freshness for Gmail
/// </summary>
public interface IGoogleTokenValidator
{
    /// <summary>
    /// Validate current OAuth token state
    /// </summary>
    /// <returns>TokenValidationResult describing token state and required actions</returns>
    /// <remarks>
    /// Checks:
    /// 1. Do tokens exist in SecureStorageManager?
    /// 2. Does refresh token exist (required for auto-refresh)?
    /// 3. Is access token expired (IssuedUtc + ExpiresInSeconds &lt; Now)?
    /// 4. Determines status: Valid, ExpiredCanRefresh, RefreshTokenMissing, NotAuthenticated
    /// </remarks>
    Task<Result<TokenValidationResult>> ValidateAsync();

    /// <summary>
    /// Check if automatic token refresh can be performed
    /// </summary>
    /// <returns>True if refresh token exists and tokens are stored</returns>
    Task<Result<bool>> CanAutoRefreshAsync();

    /// <summary>
    /// Load stored OAuth tokens from SecureStorageManager
    /// </summary>
    /// <returns>OAuthFlowResult with stored tokens or error if not found</returns>
    /// <remarks>
    /// Reconstructs OAuthFlowResult from individual SecureStorageManager entries:
    /// - gmail_access_token
    /// - gmail_refresh_token
    /// - gmail_token_expiry
    /// - gmail_token_issued_utc
    /// - gmail_user_email
    /// </remarks>
    Task<Result<OAuthFlowResult>> LoadStoredTokensAsync();
}
