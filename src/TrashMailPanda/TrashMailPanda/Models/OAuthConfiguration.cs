namespace TrashMailPanda.Models;

/// <summary>
/// Encapsulates OAuth client credentials and configuration
/// </summary>
public record OAuthConfiguration
{
    /// <summary>
    /// Google OAuth client ID
    /// </summary>
    public required string ClientId { get; init; }

    /// <summary>
    /// Google OAuth client secret
    /// </summary>
    public required string ClientSecret { get; init; }

    /// <summary>
    /// OAuth scopes to request
    /// </summary>
    public required string[] Scopes { get; init; }

    /// <summary>
    /// OAuth redirect URI (localhost callback)
    /// </summary>
    public required string RedirectUri { get; init; }

    /// <summary>
    /// Maximum time to wait for OAuth flow completion
    /// </summary>
    public TimeSpan Timeout { get; init; } = TimeSpan.FromMinutes(5);
}
