namespace TrashMailPanda.Providers.Contacts.Models;

/// <summary>
/// Secure storage key names for Contacts provider-specific data
/// OAuth credentials are shared with Gmail provider using "google_" prefix in GoogleOAuthService
/// </summary>
public static class ContactsStorageKeys
{
    /// <summary>Key prefix for Contacts-specific storage entries (non-OAuth)</summary>
    public const string KEY_PREFIX = "contacts_";

    /// <summary>Provider configuration version storage key</summary>
    public const string CONFIG_VERSION = "contacts_config_version";

    /// <summary>Google People API sync token storage key</summary>
    public const string SYNC_TOKEN = "contacts_sync_token";

    /// <summary>Last successful contacts sync timestamp storage key</summary>
    public const string LAST_SYNC_SUCCESS = "contacts_last_sync_success";

    /// <summary>Contact cache last updated timestamp storage key</summary>
    public const string CACHE_LAST_UPDATED = "contacts_cache_last_updated";

    // NOTE: OAuth credentials (access_token, refresh_token, client_id, client_secret, etc.)
    // are shared between Gmail and Contacts providers using the "google_" prefix in GoogleOAuthService
}