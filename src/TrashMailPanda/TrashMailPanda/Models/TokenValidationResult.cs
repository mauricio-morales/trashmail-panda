namespace TrashMailPanda.Models;

/// <summary>
/// Describes the current state of OAuth tokens and whether refresh is needed
/// </summary>
public record TokenValidationResult
{
    /// <summary>
    /// Whether tokens exist in secure storage
    /// </summary>
    public required bool TokensExist { get; init; }

    /// <summary>
    /// Whether access token is expired
    /// </summary>
    public required bool IsAccessTokenExpired { get; init; }

    /// <summary>
    /// Whether refresh token exists (required for auto-refresh)
    /// </summary>
    public required bool HasRefreshToken { get; init; }

    /// <summary>
    /// Time remaining until access token expires (null if no token)
    /// </summary>
    public TimeSpan? TimeUntilExpiry { get; init; }

    /// <summary>
    /// Overall validation status
    /// </summary>
    public TokenStatus Status { get; init; }

    /// <summary>
    /// User-friendly message describing token state
    /// </summary>
    public string Message { get; init; } = string.Empty;

    /// <summary>
    /// Whether automatic refresh can be attempted
    /// </summary>
    public bool CanAutoRefresh => HasRefreshToken && TokensExist;

    /// <summary>
    /// Whether full re-authentication is required
    /// </summary>
    public bool RequiresReAuthentication => !TokensExist || !HasRefreshToken;
}
