using System.ComponentModel.DataAnnotations;

namespace TrashMailPanda.Models;

/// <summary>
/// Provider display information for UI binding and presentation
/// </summary>
public record ProviderDisplayInfo
{
    /// <summary>
    /// Internal provider name used for identification
    /// </summary>
    [Required]
    public string Name { get; init; } = string.Empty;

    /// <summary>
    /// User-friendly display name
    /// </summary>
    [Required]
    public string DisplayName { get; init; } = string.Empty;

    /// <summary>
    /// Description of what this provider does
    /// </summary>
    [Required]
    public string Description { get; init; } = string.Empty;

    /// <summary>
    /// Type of provider (Storage, Email, LLM)
    /// </summary>
    public ProviderType Type { get; init; }

    /// <summary>
    /// Whether this provider is required for the application to function
    /// </summary>
    public bool IsRequired { get; init; } = true;

    /// <summary>
    /// Whether multiple instances of this provider type are allowed
    /// </summary>
    public bool AllowsMultiple { get; init; } = false;

    /// <summary>
    /// Icon or emoji to display for this provider
    /// </summary>
    public string Icon { get; init; } = string.Empty;

    /// <summary>
    /// Setup complexity level for user guidance
    /// </summary>
    public SetupComplexity Complexity { get; init; } = SetupComplexity.Simple;

    /// <summary>
    /// Estimated setup time in minutes
    /// </summary>
    public int EstimatedSetupTimeMinutes { get; init; } = 2;

    /// <summary>
    /// Additional requirements or prerequisites for setup
    /// </summary>
    public string Prerequisites { get; init; } = string.Empty;
}

/// <summary>
/// Provider setup flow state management
/// Tracks the current state of provider configuration and setup
/// </summary>
public record ProviderSetupState
{
    /// <summary>
    /// Provider name being configured
    /// </summary>
    [Required]
    public string ProviderName { get; init; } = string.Empty;

    /// <summary>
    /// Current step in the setup process
    /// </summary>
    public SetupStep CurrentStep { get; init; } = SetupStep.NotStarted;

    /// <summary>
    /// Whether setup is currently in progress
    /// </summary>
    public bool IsInProgress { get; init; } = false;

    /// <summary>
    /// Current error message if setup failed
    /// </summary>
    public string? ErrorMessage { get; init; }

    /// <summary>
    /// Temporary setup data (e.g., partial credentials during flow)
    /// This data should be cleared after setup completion
    /// </summary>
    public Dictionary<string, object> SetupData { get; init; } = new();

    /// <summary>
    /// Progress percentage for long-running setup operations
    /// </summary>
    public int ProgressPercentage { get; init; } = 0;

    /// <summary>
    /// Whether this setup can be retried
    /// </summary>
    public bool CanRetry { get; init; } = true;

    /// <summary>
    /// Setup attempt count (for limiting retries)
    /// </summary>
    public int AttemptCount { get; init; } = 0;

    /// <summary>
    /// Maximum number of setup attempts before requiring user intervention
    /// </summary>
    public int MaxAttempts { get; init; } = 3;

    /// <summary>
    /// Whether setup requires user interaction (OAuth flow, manual input, etc.)
    /// </summary>
    public bool RequiresUserInteraction { get; init; } = true;

    /// <summary>
    /// Timestamp of the last setup attempt
    /// </summary>
    public DateTime? LastAttempt { get; init; }
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

/// <summary>
/// Setup process steps for tracking configuration progress
/// </summary>
public enum SetupStep
{
    /// <summary>
    /// Setup has not been started
    /// </summary>
    NotStarted,

    /// <summary>
    /// Preparing setup (validating prerequisites, etc.)
    /// </summary>
    Preparing,

    /// <summary>
    /// Gathering user input (credentials, API keys, etc.)
    /// </summary>
    GatheringInput,

    /// <summary>
    /// Performing authentication (OAuth flow, API key validation, etc.)
    /// </summary>
    Authenticating,

    /// <summary>
    /// Configuring provider settings
    /// </summary>
    Configuring,

    /// <summary>
    /// Testing connectivity and configuration
    /// </summary>
    Testing,

    /// <summary>
    /// Finalizing setup and storing credentials securely
    /// </summary>
    Finalizing,

    /// <summary>
    /// Setup completed successfully
    /// </summary>
    Completed,

    /// <summary>
    /// Setup failed - user intervention required
    /// </summary>
    Failed,

    /// <summary>
    /// Setup was cancelled by user
    /// </summary>
    Cancelled
}

/// <summary>
/// Setup complexity levels for user guidance and expectation setting
/// </summary>
public enum SetupComplexity
{
    /// <summary>
    /// Simple setup - one-click or minimal user input
    /// </summary>
    Simple,

    /// <summary>
    /// Moderate setup - requires some user input or external account
    /// </summary>
    Moderate,

    /// <summary>
    /// Complex setup - multiple steps, external configuration required
    /// </summary>
    Complex
}

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