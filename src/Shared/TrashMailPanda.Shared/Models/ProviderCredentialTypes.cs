using System.ComponentModel.DataAnnotations;

namespace TrashMailPanda.Shared.Models;

/// <summary>
/// Provider credential types for zero-configuration secure storage
/// Separates OAuth client configuration from session tokens
/// </summary>
public static class ProviderCredentialTypes
{
    // Gmail Provider Credentials
    public const string GmailClientId = "gmail_client_id";
    public const string GmailClientSecret = "gmail_client_secret";
    public const string GmailAccessToken = "gmail_access_token";
    public const string GmailRefreshToken = "gmail_refresh_token";
    public const string GmailTokenExpiry = "gmail_token_expiry";
    public const string GmailUserEmail = "gmail_user_email";

    // OpenAI Provider Credentials
    public const string OpenAIApiKey = "openai_api_key";
    public const string OpenAIOrganization = "openai_organization";

    // Storage Provider Credentials (if needed for cloud storage)
    public const string StorageConnectionString = "storage_connection_string";
    public const string StorageEncryptionKey = "storage_encryption_key";

    // Shared Google OAuth Credentials (for Gmail, Contacts, etc.)
    public const string GoogleClientId = "google_client_id";
    public const string GoogleClientSecret = "google_client_secret";
    public const string GoogleAccessToken = "google_access_token";
    public const string GoogleRefreshToken = "google_refresh_token";
    public const string GoogleTokenExpiry = "google_token_expiry";
    public const string GoogleUserEmail = "google_user_email";

    // Contacts Provider Credentials (Google People API)
    public const string ContactsClientId = "contacts_client_id";
    public const string ContactsClientSecret = "contacts_client_secret";
    public const string ContactsAccessToken = "contacts_access_token";
    public const string ContactsRefreshToken = "contacts_refresh_token";
    public const string ContactsTokenExpiry = "contacts_token_expiry";
    public const string ContactsSyncToken = "contacts_sync_token";

    /// <summary>
    /// Get all credential types for a specific provider
    /// </summary>
    public static IReadOnlyList<string> GetCredentialTypesForProvider(ProviderType providerType)
    {
        return providerType switch
        {
            ProviderType.Email => new[]
            {
                GmailClientId,
                GmailClientSecret,
                GmailAccessToken,
                GmailRefreshToken,
                GmailTokenExpiry,
                GmailUserEmail
            },
            ProviderType.LLM => new[]
            {
                OpenAIApiKey,
                OpenAIOrganization
            },
            ProviderType.Storage => new[]
            {
                StorageConnectionString,
                StorageEncryptionKey
            },
            ProviderType.Contacts => new[]
            {
                ContactsClientId,
                ContactsClientSecret,
                ContactsAccessToken,
                ContactsRefreshToken,
                ContactsTokenExpiry,
                ContactsSyncToken
            },
            _ => Array.Empty<string>()
        };
    }

    /// <summary>
    /// Determine if a credential type represents OAuth client configuration
    /// (should persist across sessions) vs session tokens (can expire)
    /// </summary>
    public static bool IsClientCredential(string credentialType)
    {
        return credentialType switch
        {
            GmailClientId => true,
            GmailClientSecret => true,
            OpenAIApiKey => true,
            OpenAIOrganization => true,
            StorageConnectionString => true,
            StorageEncryptionKey => true,
            ContactsClientId => true,
            ContactsClientSecret => true,
            _ => false
        };
    }

    /// <summary>
    /// Determine if a credential type represents session/temporary tokens
    /// that can expire and need renewal
    /// </summary>
    public static bool IsSessionCredential(string credentialType)
    {
        return credentialType switch
        {
            GmailAccessToken => true,
            GmailRefreshToken => true,
            GmailTokenExpiry => true,
            GmailUserEmail => true,
            ContactsAccessToken => true,
            ContactsRefreshToken => true,
            ContactsTokenExpiry => true,
            ContactsSyncToken => true,
            _ => false
        };
    }
}

/// <summary>
/// Types of providers in the TrashMail Panda system
/// </summary>
public enum ProviderType
{
    /// <summary>
    /// Data storage provider (SQLite, cloud storage, etc.)
    /// </summary>
    Storage,

    /// <summary>
    /// Email provider (Gmail, IMAP, etc.)
    /// </summary>
    Email,

    /// <summary>
    /// AI/LLM provider (OpenAI, Claude, local models, etc.)
    /// </summary>
    LLM,

    /// <summary>
    /// Contacts provider (Google Contacts, Outlook, etc.)
    /// </summary>
    Contacts
}