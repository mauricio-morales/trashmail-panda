namespace TrashMailPanda.Providers.Email.Models;

/// <summary>
/// Secure storage key names for OAuth tokens and configuration
/// Maps to the shared Google OAuth credential types for consistency
/// </summary>
public static class GmailStorageKeys
{
    /// <summary>Key prefix for all Gmail-related storage entries</summary>
    public const string KEY_PREFIX = "google_";

    /// <summary>OAuth2 access token storage key</summary>
    public const string ACCESS_TOKEN = "google_access_token";

    /// <summary>OAuth2 refresh token storage key</summary>
    public const string REFRESH_TOKEN = "google_refresh_token";

    /// <summary>OAuth2 client ID storage key</summary>
    public const string CLIENT_ID = "google_client_id";

    /// <summary>OAuth2 client secret storage key</summary>
    public const string CLIENT_SECRET = "google_client_secret";

    /// <summary>Token expiration timestamp storage key</summary>
    public const string TOKEN_EXPIRY = "google_token_expiry";

    /// <summary>Token issued UTC timestamp storage key</summary>
    public const string TOKEN_ISSUED_UTC = "google_token_issued_utc";

    /// <summary>OAuth2 token type storage key</summary>
    public const string TOKEN_TYPE = "google_token_type";

    /// <summary>User email address storage key</summary>
    public const string USER_EMAIL = "google_user_email";

    /// <summary>Last successful authentication timestamp storage key</summary>
    public const string LAST_AUTH_SUCCESS = "google_last_auth_success";

    /// <summary>Provider configuration version storage key</summary>
    public const string CONFIG_VERSION = "google_config_version";
}