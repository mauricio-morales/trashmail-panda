namespace TrashMailPanda.Services;

/// <summary>
/// Configuration class for Email Provider
/// </summary>
public class EmailProviderConfig
{
    public string ClientId { get; set; } = string.Empty;
    public string ClientSecret { get; set; } = string.Empty;
    public string RedirectUri { get; set; } = "http://localhost:8080/oauth/callback";
    public int TimeoutSeconds { get; set; } = 30;

    /// <summary>
    /// Required OAuth scopes for Gmail API access.
    /// Default: gmail.modify (read/modify emails, not delete)
    /// </summary>
    public string[] RequiredScopes { get; set; } = new[]
    {
        "https://www.googleapis.com/auth/gmail.modify"
    };
}