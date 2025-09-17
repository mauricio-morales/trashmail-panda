using System;
using System.ComponentModel.DataAnnotations;
using Google.Apis.PeopleService.v1;
using TrashMailPanda.Shared.Models;
using TrashMailPanda.Shared.Base;
using TrashMailPanda.Shared.Security;
using TrashMailPanda.Providers.Contacts.Models;

namespace TrashMailPanda.Providers.Contacts;

/// <summary>
/// Configuration for the Contacts provider
/// Provides Google People API integration with OAuth credentials and caching settings
/// </summary>
public sealed class ContactsProviderConfig : BaseProviderConfig
{
    /// <summary>
    /// Gets or sets the provider name identifier
    /// </summary>
    public new string Name { get; set; } = "Contacts";

    /// <summary>
    /// Gets or sets tags for categorizing and filtering providers
    /// </summary>
    public new List<string> Tags { get; set; } = new() { "contacts", "google", "people", "trust" };

    /// <summary>
    /// Gets or sets the OAuth2 client ID for Google People API authentication
    /// </summary>
    [Required(ErrorMessage = "Google People API Client ID is required")]
    [StringLength(200, MinimumLength = 10, ErrorMessage = "Client ID must be between 10 and 200 characters")]
    public string ClientId { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets the OAuth2 client secret for Google People API authentication
    /// </summary>
    [Required(ErrorMessage = "Google People API Client Secret is required")]
    [StringLength(200, MinimumLength = 10, ErrorMessage = "Client Secret must be between 10 and 200 characters")]
    public string ClientSecret { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets the application name displayed to users during OAuth flow
    /// </summary>
    [StringLength(100, MinimumLength = 1, ErrorMessage = "Application name must be between 1 and 100 characters")]
    public string ApplicationName { get; set; } = "TrashMail Panda";

    /// <summary>
    /// Gets or sets the OAuth2 scopes required for People API operations
    /// </summary>
    public string[] Scopes { get; set; } = {
        GoogleOAuthScopes.ContactsReadonly,
        GoogleOAuthScopes.UserInfoProfile
    };

    /// <summary>
    /// Gets or sets the timeout for individual People API requests
    /// </summary>
    public TimeSpan RequestTimeout { get; set; } = TimeSpan.FromMinutes(1);

    /// <summary>
    /// Gets or sets the maximum number of retry attempts for rate limiting
    /// </summary>
    [Range(1, 10, ErrorMessage = "Max retries must be between 1 and 10")]
    public int MaxRetries { get; set; } = 5;

    /// <summary>
    /// Gets or sets the base delay for exponential backoff retry strategy
    /// </summary>
    public TimeSpan BaseRetryDelay { get; set; } = TimeSpan.FromSeconds(1);

    /// <summary>
    /// Gets or sets the maximum delay for exponential backoff retry strategy
    /// </summary>
    public TimeSpan MaxRetryDelay { get; set; } = TimeSpan.FromMinutes(1);

    /// <summary>
    /// Gets or sets the default page size for People API list operations (max 2000 per API limits)
    /// </summary>
    [Range(1, 2000, ErrorMessage = "Page size must be between 1 and 2000")]
    public int DefaultPageSize { get; set; } = 1000;

    /// <summary>
    /// Gets or sets the 3-layer cache configuration
    /// </summary>
    public CacheConfiguration Cache { get; set; } = new();

    /// <summary>
    /// Gets or sets whether contacts caching is enabled
    /// </summary>
    public bool EnableContactsCaching { get; set; } = true;

    /// <summary>
    /// Gets or sets whether to enable background sync for contacts
    /// </summary>
    public bool EnableBackgroundSync { get; set; } = true;

    /// <summary>
    /// Gets or sets the interval for background sync operations
    /// </summary>
    public TimeSpan BackgroundSyncInterval { get; set; } = TimeSpan.FromHours(4);

    /// <summary>
    /// Gets or sets whether to enable trust signal computation
    /// </summary>
    public bool EnableTrustSignals { get; set; } = true;

    /// <summary>
    /// Gets or sets the trust signal cache expiry duration
    /// </summary>
    public TimeSpan TrustSignalCacheExpiry { get; set; } = TimeSpan.FromDays(1);

    /// <summary>
    /// Performs Contacts provider specific configuration validation
    /// </summary>
    /// <returns>A result indicating whether the configuration is valid</returns>
    public override Result ValidateConfiguration()
    {
        // Perform base validation first
        var baseResult = base.ValidateConfiguration();
        if (baseResult.IsFailure)
            return baseResult;

        // Contacts provider specific validation
        if (string.IsNullOrWhiteSpace(ClientId))
            return Result.Failure(new ValidationError("Google People API Client ID cannot be empty"));

        if (string.IsNullOrWhiteSpace(ClientSecret))
            return Result.Failure(new ValidationError("Google People API Client Secret cannot be empty"));

        if (Scopes == null || Scopes.Length == 0)
            return Result.Failure(new ValidationError("Configuration validation failed: request must contain at least one valid OAuth scope"));

        // Validate OAuth scopes
        var validScopes = new[]
        {
            GoogleOAuthScopes.ContactsReadonly,
            GoogleOAuthScopes.Contacts,
            GoogleOAuthScopes.UserInfoProfile,
            GoogleOAuthScopes.UserInfoEmail
        };

        foreach (var scope in Scopes)
        {
            if (!validScopes.Contains(scope))
                return Result.Failure(new ValidationError($"Configuration validation failed: request contains invalid OAuth scope: {scope}"));
        }

        // Validate retry configuration
        if (BaseRetryDelay >= MaxRetryDelay)
            return Result.Failure(new ValidationError("Base retry delay must be less than max retry delay"));

        // Validate timeout configuration
        if (RequestTimeout.TotalSeconds > TimeoutSeconds)
            return Result.Failure(new ValidationError("Request timeout cannot exceed provider timeout"));

        // Validate cache settings
        if (EnableContactsCaching)
        {
            var cacheValidation = Cache.Validate();
            if (cacheValidation.IsFailure)
                return cacheValidation;
        }

        return ValidateCustomLogic();
    }

    /// <summary>
    /// Performs additional Contacts provider specific validation
    /// </summary>
    /// <returns>A result indicating whether custom validation passed</returns>
    protected override Result ValidateCustomLogic()
    {
        // Ensure we have contacts read permissions
        var hasContactsPermissions = Scopes.Contains(GoogleOAuthScopes.ContactsReadonly) ||
                                   Scopes.Contains(GoogleOAuthScopes.Contacts);

        if (!hasContactsPermissions)
        {
            return Result.Failure(new ValidationError("Provider requires contacts read permissions for contact operations"));
        }

        // Validate page size doesn't exceed Google People API limits
        if (DefaultPageSize > 2000)
            return Result.Failure(new ValidationError("Page size cannot exceed Google People API limit of 2000 contacts"));

        // Validate background sync settings
        if (EnableBackgroundSync && BackgroundSyncInterval < TimeSpan.FromMinutes(30))
            return Result.Failure(new ValidationError("Background sync interval must be at least 30 minutes"));

        return Result.Success();
    }

    /// <summary>
    /// Gets a sanitized copy of the configuration with sensitive information removed
    /// </summary>
    /// <returns>A sanitized copy of the configuration</returns>
    public override BaseProviderConfig GetSanitizedCopy()
    {
        var copy = (ContactsProviderConfig)MemberwiseClone();
        copy.ClientSecret = "***REDACTED***";

        // ClientId is not considered sensitive - keep as is for identification
        // Only ClientSecret needs to be redacted

        return copy;
    }

    /// <summary>
    /// Creates a configuration for development/testing with safe defaults
    /// </summary>
    /// <param name="clientId">The OAuth client ID</param>
    /// <param name="clientSecret">The OAuth client secret</param>
    /// <returns>A development-ready configuration</returns>
    public static ContactsProviderConfig CreateDevelopmentConfig(string clientId, string clientSecret)
    {
        return new ContactsProviderConfig
        {
            ClientId = clientId,
            ClientSecret = clientSecret,
            RequestTimeout = TimeSpan.FromSeconds(30),
            MaxRetries = 3,
            BaseRetryDelay = TimeSpan.FromMilliseconds(500),
            MaxRetryDelay = TimeSpan.FromSeconds(30),
            DefaultPageSize = 500,
            TimeoutSeconds = 60,
            MaxRetryAttempts = 3,
            RetryDelayMilliseconds = 500,
            Cache = CacheConfiguration.CreateDevelopmentConfig(),
            BackgroundSyncInterval = TimeSpan.FromHours(1),
            IsEnabled = true
        };
    }

    /// <summary>
    /// Creates a configuration for production with optimal settings
    /// </summary>
    /// <param name="clientId">The OAuth client ID</param>
    /// <param name="clientSecret">The OAuth client secret</param>
    /// <returns>A production-ready configuration</returns>
    public static ContactsProviderConfig CreateProductionConfig(string clientId, string clientSecret)
    {
        return new ContactsProviderConfig
        {
            ClientId = clientId,
            ClientSecret = clientSecret,
            RequestTimeout = TimeSpan.FromMinutes(1),
            MaxRetries = 5,
            BaseRetryDelay = TimeSpan.FromSeconds(1),
            MaxRetryDelay = TimeSpan.FromMinutes(1),
            DefaultPageSize = 1000,
            TimeoutSeconds = 120,
            MaxRetryAttempts = 5,
            RetryDelayMilliseconds = 1000,
            Cache = CacheConfiguration.CreateProductionConfig(),
            BackgroundSyncInterval = TimeSpan.FromHours(4),
            EnableContactsCaching = true,
            EnableBackgroundSync = true,
            EnableTrustSignals = true,
            IsEnabled = true
        };
    }
}

