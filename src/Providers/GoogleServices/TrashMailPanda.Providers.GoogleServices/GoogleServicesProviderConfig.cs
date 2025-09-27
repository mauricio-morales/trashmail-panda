using System;
using System.ComponentModel.DataAnnotations;
using System.Collections.Generic;
using System.Linq;
using Google.Apis.Gmail.v1;
using TrashMailPanda.Shared.Models;
using TrashMailPanda.Shared.Base;
using TrashMailPanda.Shared.Security;
using TrashMailPanda.Providers.Contacts.Models;

namespace TrashMailPanda.Providers.GoogleServices;

/// <summary>
/// Unified configuration for Google Services provider that consolidates Gmail and Contacts functionality
/// Provides OAuth credentials and service-specific settings for both Gmail and Google Contacts
/// </summary>
public sealed class GoogleServicesProviderConfig : BaseProviderConfig
{
    /// <summary>
    /// Gets or sets the provider name identifier
    /// </summary>
    public new string Name { get; set; } = "GoogleServices";

    /// <summary>
    /// Gets or sets tags for categorizing and filtering providers
    /// </summary>
    public new List<string> Tags { get; set; } = new() { "google", "gmail", "contacts", "oauth", "unified" };

    /// <summary>
    /// Gets or sets the timeout in seconds for provider operations (overridden to ensure proper JSON binding)
    /// </summary>
    [Range(1, 3600, ErrorMessage = "Timeout must be between 1 and 3600 seconds")]
    public new int TimeoutSeconds { get; set; } = 180;

    #region OAuth Configuration

    /// <summary>
    /// Gets or sets the OAuth2 client ID for Google APIs authentication
    /// </summary>
    [Required(ErrorMessage = "Google Client ID is required")]
    [StringLength(200, MinimumLength = 10, ErrorMessage = "Client ID must be between 10 and 200 characters")]
    public string ClientId { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets the OAuth2 client secret for Google APIs authentication
    /// </summary>
    [Required(ErrorMessage = "Google Client Secret is required")]
    [StringLength(200, MinimumLength = 10, ErrorMessage = "Client Secret must be between 10 and 200 characters")]
    public string ClientSecret { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets the OAuth2 redirect URI for authentication flow
    /// </summary>
    [Url(ErrorMessage = "Redirect URI must be a valid URL")]
    public string RedirectUri { get; set; } = "http://localhost:8080/oauth/callback";

    /// <summary>
    /// Gets or sets the application name displayed to users during OAuth flow
    /// </summary>
    [StringLength(100, MinimumLength = 1, ErrorMessage = "Application name must be between 1 and 100 characters")]
    public string ApplicationName { get; set; } = "TrashMail Panda";

    /// <summary>
    /// Gets or sets the combined OAuth2 scopes required for both Gmail and Contacts operations
    /// </summary>
    public string[] CombinedScopes { get; set; } = {
        GoogleOAuthScopes.GmailModify,              // Gmail read/modify access
        GoogleOAuthScopes.ContactsReadonly,         // Contacts read access
        GoogleOAuthScopes.UserInfoProfile           // User profile info
    };

    #endregion

    #region Feature Flags

    /// <summary>
    /// Gets or sets whether Gmail functionality is enabled
    /// </summary>
    public bool EnableGmail { get; set; } = true;

    /// <summary>
    /// Gets or sets whether Contacts functionality is enabled
    /// </summary>
    public bool EnableContacts { get; set; } = true;

    #endregion

    #region Gmail-Specific Settings

    /// <summary>
    /// Gets or sets the timeout for individual Gmail API requests
    /// </summary>
    public TimeSpan GmailRequestTimeout { get; set; } = TimeSpan.FromMinutes(2);

    /// <summary>
    /// Gets or sets the maximum number of retry attempts for Gmail rate limiting
    /// </summary>
    [Range(1, 10, ErrorMessage = "Gmail max retries must be between 1 and 10")]
    public int GmailMaxRetries { get; set; } = 5;

    /// <summary>
    /// Gets or sets the base delay for Gmail exponential backoff retry strategy
    /// </summary>
    public TimeSpan GmailBaseRetryDelay { get; set; } = TimeSpan.FromSeconds(1);

    /// <summary>
    /// Gets or sets the maximum delay for Gmail exponential backoff retry strategy
    /// </summary>
    public TimeSpan GmailMaxRetryDelay { get; set; } = TimeSpan.FromMinutes(2);

    /// <summary>
    /// Gets or sets whether to enable batch operation optimization for Gmail
    /// </summary>
    public bool GmailEnableBatchOptimization { get; set; } = true;

    /// <summary>
    /// Gets or sets the batch size for Gmail bulk operations (max 100 per Gmail API limits)
    /// </summary>
    [Range(1, 100, ErrorMessage = "Gmail batch size must be between 1 and 100")]
    public int GmailBatchSize { get; set; } = 50;

    /// <summary>
    /// Gets or sets the default page size for Gmail list operations (max 500 per Gmail API limits)
    /// </summary>
    [Range(1, 500, ErrorMessage = "Gmail page size must be between 1 and 500")]
    public int GmailDefaultPageSize { get; set; } = 100;

    #endregion

    #region Contacts-Specific Settings

    /// <summary>
    /// Gets or sets the timeout for individual Google People API requests
    /// </summary>
    public TimeSpan ContactsRequestTimeout { get; set; } = TimeSpan.FromMinutes(1);

    /// <summary>
    /// Gets or sets the maximum number of retry attempts for Contacts rate limiting
    /// </summary>
    [Range(1, 10, ErrorMessage = "Contacts max retries must be between 1 and 10")]
    public int ContactsMaxRetries { get; set; } = 5;

    /// <summary>
    /// Gets or sets the base delay for Contacts exponential backoff retry strategy
    /// </summary>
    public TimeSpan ContactsBaseRetryDelay { get; set; } = TimeSpan.FromSeconds(1);

    /// <summary>
    /// Gets or sets the maximum delay for Contacts exponential backoff retry strategy
    /// </summary>
    public TimeSpan ContactsMaxRetryDelay { get; set; } = TimeSpan.FromMinutes(1);

    /// <summary>
    /// Gets or sets the default page size for People API list operations (max 2000 per API limits)
    /// </summary>
    [Range(1, 2000, ErrorMessage = "Contacts page size must be between 1 and 2000")]
    public int ContactsDefaultPageSize { get; set; } = 1000;

    /// <summary>
    /// Gets or sets whether contacts caching is enabled
    /// </summary>
    public bool ContactsCachingEnabled { get; set; } = true;

    /// <summary>
    /// Gets or sets whether to enable background sync for contacts
    /// </summary>
    public bool ContactsEnableBackgroundSync { get; set; } = true;

    /// <summary>
    /// Gets or sets the interval for contacts background sync operations
    /// </summary>
    public TimeSpan ContactsBackgroundSyncInterval { get; set; } = TimeSpan.FromHours(4);

    /// <summary>
    /// Gets or sets whether to enable trust signal computation
    /// </summary>
    public bool ContactsEnableTrustSignals { get; set; } = true;

    /// <summary>
    /// Gets or sets the trust signal cache expiry duration
    /// </summary>
    public TimeSpan ContactsTrustSignalCacheExpiry { get; set; } = TimeSpan.FromDays(1);

    /// <summary>
    /// Gets or sets the contacts cache configuration
    /// </summary>
    public CacheConfiguration ContactsCache { get; set; } = new();

    #endregion

    #region Validation

    /// <summary>
    /// Performs Google Services provider configuration validation
    /// </summary>
    /// <returns>A result indicating whether the configuration is valid</returns>
    public override Result ValidateConfiguration()
    {
        // Perform base validation first
        var baseResult = base.ValidateConfiguration();
        if (baseResult.IsFailure)
            return baseResult;

        // OAuth credentials validation
        if (string.IsNullOrWhiteSpace(ClientId))
            return Result.Failure(new ValidationError("Google Client ID cannot be empty"));

        if (string.IsNullOrWhiteSpace(ClientSecret))
            return Result.Failure(new ValidationError("Google Client Secret cannot be empty"));

        if (CombinedScopes == null || CombinedScopes.Length == 0)
            return Result.Failure(new ValidationError("Configuration validation failed: request must contain at least one valid OAuth scope"));

        // Validate feature flags - at least one service must be enabled
        if (!EnableGmail && !EnableContacts)
            return Result.Failure(new ValidationError("At least one Google service (Gmail or Contacts) must be enabled"));

        // Validate OAuth scopes
        var validScopes = new[]
        {
            // Gmail scopes
            GoogleOAuthScopes.GmailReadonly,
            GoogleOAuthScopes.GmailModify,
            GoogleOAuthScopes.GmailFullAccess,
            GmailService.Scope.GmailReadonly,
            GmailService.Scope.GmailModify,
            GmailService.Scope.GmailCompose,
            GmailService.Scope.GmailSend,
            GmailService.Scope.MailGoogleCom,
            GmailService.Scope.GmailLabels,
            GmailService.Scope.GmailSettingsBasic,
            GmailService.Scope.GmailSettingsSharing,

            // Contacts scopes
            GoogleOAuthScopes.ContactsReadonly,
            GoogleOAuthScopes.Contacts,
            GoogleOAuthScopes.UserInfoProfile,
            GoogleOAuthScopes.UserInfoEmail
        };

        foreach (var scope in CombinedScopes)
        {
            if (!validScopes.Contains(scope))
                return Result.Failure(new ValidationError($"Configuration validation failed: request contains invalid OAuth scope: {scope}"));
        }

        // Validate timeout configurations
        if (GmailRequestTimeout.TotalSeconds > TimeoutSeconds && EnableGmail)
            return Result.Failure(new ValidationError("Gmail request timeout cannot exceed provider timeout"));

        if (ContactsRequestTimeout.TotalSeconds > TimeoutSeconds && EnableContacts)
            return Result.Failure(new ValidationError("Contacts request timeout cannot exceed provider timeout"));

        // Validate retry configurations
        if (GmailBaseRetryDelay >= GmailMaxRetryDelay && EnableGmail)
            return Result.Failure(new ValidationError("Gmail base retry delay must be less than max retry delay"));

        if (ContactsBaseRetryDelay >= ContactsMaxRetryDelay && EnableContacts)
            return Result.Failure(new ValidationError("Contacts base retry delay must be less than max retry delay"));

        // Validate contacts cache settings if enabled
        if (EnableContacts && ContactsCachingEnabled)
        {
            var cacheValidation = ContactsCache.Validate();
            if (cacheValidation.IsFailure)
                return cacheValidation;
        }

        return ValidateCustomLogic();
    }

    /// <summary>
    /// Performs additional Google Services provider specific validation
    /// </summary>
    /// <returns>A result indicating whether custom validation passed</returns>
    protected override Result ValidateCustomLogic()
    {
        // Ensure we have appropriate permissions for enabled services
        if (EnableGmail)
        {
            var hasGmailModifyPermissions = CombinedScopes.Contains(GoogleOAuthScopes.GmailModify) ||
                                          CombinedScopes.Contains(GoogleOAuthScopes.GmailFullAccess) ||
                                          CombinedScopes.Contains(GmailService.Scope.GmailModify) ||
                                          CombinedScopes.Contains(GmailService.Scope.MailGoogleCom);

            if (!hasGmailModifyPermissions)
            {
                return Result.Failure(new ValidationError("Gmail service requires modify permissions for email triage operations"));
            }

            // Validate Gmail limits
            if (GmailBatchSize > 100)
                return Result.Failure(new ValidationError("Gmail batch size cannot exceed Gmail API limit of 100 operations"));

            if (GmailDefaultPageSize > 500)
                return Result.Failure(new ValidationError("Gmail page size cannot exceed Gmail API limit of 500 messages"));
        }

        if (EnableContacts)
        {
            var hasContactsPermissions = CombinedScopes.Contains(GoogleOAuthScopes.ContactsReadonly) ||
                                       CombinedScopes.Contains(GoogleOAuthScopes.Contacts);

            if (!hasContactsPermissions)
            {
                return Result.Failure(new ValidationError("Contacts service requires contacts read permissions for contact operations"));
            }

            // Validate Contacts limits
            if (ContactsDefaultPageSize > 2000)
                return Result.Failure(new ValidationError("Contacts page size cannot exceed Google People API limit of 2000 contacts"));

            // Validate background sync settings
            if (ContactsEnableBackgroundSync && ContactsBackgroundSyncInterval < TimeSpan.FromMinutes(30))
                return Result.Failure(new ValidationError("Contacts background sync interval must be at least 30 minutes"));
        }

        return Result.Success();
    }

    /// <summary>
    /// Gets a sanitized copy of the configuration with sensitive information removed
    /// </summary>
    /// <returns>A sanitized copy of the configuration</returns>
    public override BaseProviderConfig GetSanitizedCopy()
    {
        var copy = (GoogleServicesProviderConfig)MemberwiseClone();
        copy.ClientSecret = "***REDACTED***";

        // ClientId is not considered sensitive - keep as is for identification
        // Only ClientSecret needs to be redacted

        return copy;
    }

    #endregion

    #region Factory Methods

    /// <summary>
    /// Creates a configuration for development/testing with safe defaults
    /// </summary>
    /// <param name="clientId">The OAuth client ID</param>
    /// <param name="clientSecret">The OAuth client secret</param>
    /// <returns>A development-ready configuration</returns>
    public static GoogleServicesProviderConfig CreateDevelopmentConfig(string clientId, string clientSecret)
    {
        return new GoogleServicesProviderConfig
        {
            ClientId = clientId,
            ClientSecret = clientSecret,
            GmailRequestTimeout = TimeSpan.FromSeconds(30),
            GmailMaxRetries = 3,
            GmailBaseRetryDelay = TimeSpan.FromMilliseconds(500),
            GmailMaxRetryDelay = TimeSpan.FromSeconds(30),
            GmailBatchSize = 25,
            GmailDefaultPageSize = 50,
            ContactsRequestTimeout = TimeSpan.FromSeconds(30),
            ContactsMaxRetries = 3,
            ContactsBaseRetryDelay = TimeSpan.FromMilliseconds(500),
            ContactsMaxRetryDelay = TimeSpan.FromSeconds(30),
            ContactsDefaultPageSize = 500,
            ContactsCache = CacheConfiguration.CreateDevelopmentConfig(),
            ContactsBackgroundSyncInterval = TimeSpan.FromHours(1),
            TimeoutSeconds = 60,
            MaxRetryAttempts = 3,
            RetryDelayMilliseconds = 500,
            EnableGmail = true,
            EnableContacts = true,
            IsEnabled = true
        };
    }

    /// <summary>
    /// Creates a configuration for production with optimal settings
    /// </summary>
    /// <param name="clientId">The OAuth client ID</param>
    /// <param name="clientSecret">The OAuth client secret</param>
    /// <returns>A production-ready configuration</returns>
    public static GoogleServicesProviderConfig CreateProductionConfig(string clientId, string clientSecret)
    {
        return new GoogleServicesProviderConfig
        {
            ClientId = clientId,
            ClientSecret = clientSecret,
            GmailRequestTimeout = TimeSpan.FromMinutes(2),
            GmailMaxRetries = 5,
            GmailBaseRetryDelay = TimeSpan.FromSeconds(1),
            GmailMaxRetryDelay = TimeSpan.FromMinutes(2),
            GmailBatchSize = 50,
            GmailDefaultPageSize = 100,
            GmailEnableBatchOptimization = true,
            ContactsRequestTimeout = TimeSpan.FromMinutes(1),
            ContactsMaxRetries = 5,
            ContactsBaseRetryDelay = TimeSpan.FromSeconds(1),
            ContactsMaxRetryDelay = TimeSpan.FromMinutes(1),
            ContactsDefaultPageSize = 1000,
            ContactsCache = CacheConfiguration.CreateProductionConfig(),
            ContactsBackgroundSyncInterval = TimeSpan.FromHours(4),
            ContactsCachingEnabled = true,
            ContactsEnableBackgroundSync = true,
            ContactsEnableTrustSignals = true,
            TimeoutSeconds = 180,
            MaxRetryAttempts = 5,
            RetryDelayMilliseconds = 1000,
            EnableGmail = true,
            EnableContacts = true,
            IsEnabled = true
        };
    }

    #endregion
}