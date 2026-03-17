namespace TrashMailPanda.Models;

/// <summary>
/// Represents the complete outcome of an OAuth authentication flow
/// </summary>
public record OAuthFlowResult
{
    /// <summary>
    /// OAuth access token (short-lived, typically 1 hour)
    /// </summary>
    public required string AccessToken { get; init; }

    /// <summary>
    /// OAuth refresh token (long-lived, used to obtain new access tokens)
    /// </summary>
    public required string RefreshToken { get; init; }

    /// <summary>
    /// Token expiration time in seconds (e.g., 3600 for 1 hour)
    /// </summary>
    public required long ExpiresInSeconds { get; init; }

    /// <summary>
    /// UTC timestamp when tokens were issued
    /// </summary>
    public required DateTime IssuedUtc { get; init; }

    /// <summary>
    /// OAuth scopes granted by user
    /// </summary>
    public required string[] Scopes { get; init; }

    /// <summary>
    /// User's Gmail email address (retrieved from profile)
    /// </summary>
    public string? UserEmail { get; init; }

    /// <summary>
    /// Token type (typically "Bearer")
    /// </summary>
    public string TokenType { get; init; } = "Bearer";

    /// <summary>
    /// Check if access token is expired based on issued time + expiry
    /// </summary>
    public bool IsAccessTokenExpired() =>
        DateTime.UtcNow >= IssuedUtc.AddSeconds(ExpiresInSeconds);

    /// <summary>
    /// Time remaining until access token expires
    /// </summary>
    public TimeSpan TimeUntilExpiry() =>
        IssuedUtc.AddSeconds(ExpiresInSeconds) - DateTime.UtcNow;
}
