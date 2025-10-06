using TrashMailPanda.Shared.Models;

namespace TrashMailPanda.Providers.Email.Models;

/// <summary>
/// Secure storage key names for OAuth tokens and configuration
/// Uses the shared ProviderCredentialTypes for consistency across all components
/// </summary>
public static class GmailStorageKeys
{
    /// <summary>Key prefix for all Gmail-related storage entries</summary>
    public const string KEY_PREFIX = "google_";

    /// <summary>OAuth2 access token storage key</summary>
    public const string ACCESS_TOKEN = ProviderCredentialTypes.GoogleAccessToken;

    /// <summary>OAuth2 refresh token storage key</summary>
    public const string REFRESH_TOKEN = ProviderCredentialTypes.GoogleRefreshToken;

    /// <summary>OAuth2 client ID storage key</summary>
    public const string CLIENT_ID = ProviderCredentialTypes.GoogleClientId;

    /// <summary>OAuth2 client secret storage key</summary>
    public const string CLIENT_SECRET = ProviderCredentialTypes.GoogleClientSecret;

    /// <summary>Token expiration timestamp storage key</summary>
    public const string TOKEN_EXPIRY = ProviderCredentialTypes.GoogleTokenExpiry;

    /// <summary>User email address storage key</summary>
    public const string USER_EMAIL = ProviderCredentialTypes.GoogleUserEmail;

    /// <summary>Token issued UTC timestamp storage key - Gmail-specific</summary>
    public const string TOKEN_ISSUED_UTC = "google_token_issued_utc";

    /// <summary>OAuth2 token type storage key - Gmail-specific</summary>
    public const string TOKEN_TYPE = "google_token_type";

    /// <summary>Last successful authentication timestamp storage key - Gmail-specific</summary>
    public const string LAST_AUTH_SUCCESS = "google_last_auth_success";

    /// <summary>Provider configuration version storage key - Gmail-specific</summary>
    public const string CONFIG_VERSION = "google_config_version";
}