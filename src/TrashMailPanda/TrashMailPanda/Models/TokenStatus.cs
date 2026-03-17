namespace TrashMailPanda.Models;

/// <summary>
/// Represents the current state of OAuth tokens
/// </summary>
public enum TokenStatus
{
    /// <summary>Valid access token, no action needed</summary>
    Valid,

    /// <summary>Access token expired, can auto-refresh with refresh token</summary>
    ExpiredCanRefresh,

    /// <summary>No refresh token, must re-authenticate</summary>
    RefreshTokenMissing,

    /// <summary>No tokens found, initial authentication required</summary>
    NotAuthenticated,

    /// <summary>Refresh token revoked or invalid</summary>
    RefreshTokenRevoked
}
