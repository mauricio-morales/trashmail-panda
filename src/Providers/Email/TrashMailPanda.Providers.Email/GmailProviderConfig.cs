using System;
using System.ComponentModel.DataAnnotations;
using Google.Apis.Gmail.v1;
using TrashMailPanda.Shared.Models;
using TrashMailPanda.Shared.Base;
using TrashMailPanda.Shared.Security;

namespace TrashMailPanda.Providers.Email;

/// <summary>
/// Configuration for the Gmail email provider
/// Provides Gmail-specific configuration including OAuth credentials and API settings
/// </summary>
public sealed class GmailProviderConfig : BaseProviderConfig
{
    /// <summary>
    /// Gets or sets the provider name identifier
    /// </summary>
    public new string Name { get; set; } = "Gmail";

    /// <summary>
    /// Gets or sets tags for categorizing and filtering providers
    /// </summary>
    public new List<string> Tags { get; set; } = new() { "email", "gmail", "google" };
    /// <summary>
    /// Gets or sets the OAuth2 client ID for Gmail API authentication
    /// </summary>
    [Required(ErrorMessage = "Gmail Client ID is required")]
    [StringLength(200, MinimumLength = 10, ErrorMessage = "Client ID must be between 10 and 200 characters")]
    public string ClientId { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets the OAuth2 client secret for Gmail API authentication
    /// </summary>
    [Required(ErrorMessage = "Gmail Client Secret is required")]
    [StringLength(200, MinimumLength = 10, ErrorMessage = "Client Secret must be between 10 and 200 characters")]
    public string ClientSecret { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets the application name displayed to users during OAuth flow
    /// </summary>
    [StringLength(100, MinimumLength = 1, ErrorMessage = "Application name must be between 1 and 100 characters")]
    public string ApplicationName { get; set; } = "TrashMail Panda";

    /// <summary>
    /// Gets or sets the OAuth2 scopes required for Gmail operations
    /// </summary>
    public string[] Scopes { get; set; } = { GmailService.Scope.GmailModify };

    /// <summary>
    /// Gets or sets the timeout for individual Gmail API requests
    /// </summary>
    public TimeSpan RequestTimeout { get; set; } = TimeSpan.FromMinutes(2);

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
    public TimeSpan MaxRetryDelay { get; set; } = TimeSpan.FromMinutes(2);

    /// <summary>
    /// Gets or sets whether to enable batch operation optimization
    /// </summary>
    public bool EnableBatchOptimization { get; set; } = true;

    /// <summary>
    /// Gets or sets the batch size for bulk operations (max 100 per Gmail API limits)
    /// </summary>
    [Range(1, 100, ErrorMessage = "Batch size must be between 1 and 100")]
    public int BatchSize { get; set; } = 50;

    /// <summary>
    /// Gets or sets the default page size for list operations (max 500 per Gmail API limits)
    /// </summary>
    [Range(1, 500, ErrorMessage = "Default page size must be between 1 and 500")]
    public int DefaultPageSize { get; set; } = 100;


    /// <summary>
    /// Performs Gmail-specific configuration validation
    /// </summary>
    /// <returns>A result indicating whether the configuration is valid</returns>
    public override Result ValidateConfiguration()
    {
        // Perform base validation first
        var baseResult = base.ValidateConfiguration();
        if (baseResult.IsFailure)
            return baseResult;

        // Gmail-specific validation
        if (string.IsNullOrWhiteSpace(ClientId))
            return Result.Failure(new ValidationError("Gmail Client ID cannot be empty"));

        if (string.IsNullOrWhiteSpace(ClientSecret))
            return Result.Failure(new ValidationError("Gmail Client Secret cannot be empty"));

        if (Scopes == null || Scopes.Length == 0)
            return Result.Failure(new ValidationError("configuration validation failed: request must contain at least one valid OAuth scope"));

        // Validate OAuth scopes using centralized scope constants from shared library
        var validScopeStrings = GoogleOAuthScopes.AllValidScopes;

        string[] gmailServiceScopes = [
            GmailService.Scope.GmailReadonly,
            GmailService.Scope.GmailModify,
            GmailService.Scope.GmailCompose,
            GmailService.Scope.GmailSend,
            GmailService.Scope.MailGoogleCom,
            GmailService.Scope.GmailLabels,
            GmailService.Scope.GmailSettingsBasic,
            GmailService.Scope.GmailSettingsSharing
        ];

        var validScopes = validScopeStrings.Concat(gmailServiceScopes).ToArray();

        foreach (var scope in Scopes)
        {
            if (!validScopes.Contains(scope))
                return Result.Failure(new ValidationError($"configuration validation failed: request contains invalid OAuth scope: {scope}"));
        }

        // Validate retry configuration
        if (BaseRetryDelay >= MaxRetryDelay)
            return Result.Failure(new ValidationError("Base retry delay must be less than max retry delay"));

        // Validate timeout configuration
        if (RequestTimeout.TotalSeconds > TimeoutSeconds)
            return Result.Failure(new ValidationError("Request timeout cannot exceed provider timeout"));

        return ValidateCustomLogic();
    }

    /// <summary>
    /// Performs additional Gmail-specific validation
    /// </summary>
    /// <returns>A result indicating whether custom validation passed</returns>
    protected override Result ValidateCustomLogic()
    {
        // Ensure we have modify permissions for TrashMail Panda operations
        var hasModifyPermissions = Scopes.Contains(GmailService.Scope.GmailModify) ||
                                  Scopes.Contains(GmailService.Scope.MailGoogleCom) ||
                                  Scopes.Contains(GoogleOAuthScopes.GmailModify) ||
                                  Scopes.Contains(GoogleOAuthScopes.GmailFullAccess);

        if (!hasModifyPermissions)
        {
            return Result.Failure(new ValidationError("Provider requires modify permissions for email triage operations"));
        }

        // Validate batch size doesn't exceed Gmail limits
        if (BatchSize > 100)
            return Result.Failure(new ValidationError("Batch size cannot exceed Gmail API limit of 100 operations"));

        // Validate page size doesn't exceed Gmail limits
        if (DefaultPageSize > 500)
            return Result.Failure(new ValidationError("Page size cannot exceed Gmail API limit of 500 messages"));

        return Result.Success();
    }

    /// <summary>
    /// Gets a sanitized copy of the configuration with sensitive information removed
    /// </summary>
    /// <returns>A sanitized copy of the configuration</returns>
    public override BaseProviderConfig GetSanitizedCopy()
    {
        var copy = (GmailProviderConfig)MemberwiseClone();
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
    public static GmailProviderConfig CreateDevelopmentConfig(string clientId, string clientSecret)
    {
        return new GmailProviderConfig
        {
            ClientId = clientId,
            ClientSecret = clientSecret,
            RequestTimeout = TimeSpan.FromSeconds(30),
            MaxRetries = 3,
            BaseRetryDelay = TimeSpan.FromMilliseconds(500),
            MaxRetryDelay = TimeSpan.FromSeconds(30),
            BatchSize = 25,
            DefaultPageSize = 50,
            TimeoutSeconds = 60,
            MaxRetryAttempts = 3,
            RetryDelayMilliseconds = 500,
            IsEnabled = true
        };
    }

    /// <summary>
    /// Creates a configuration for production with optimal settings
    /// </summary>
    /// <param name="clientId">The OAuth client ID</param>
    /// <param name="clientSecret">The OAuth client secret</param>
    /// <returns>A production-ready configuration</returns>
    public static GmailProviderConfig CreateProductionConfig(string clientId, string clientSecret)
    {
        return new GmailProviderConfig
        {
            ClientId = clientId,
            ClientSecret = clientSecret,
            RequestTimeout = TimeSpan.FromMinutes(2),
            MaxRetries = 5,
            BaseRetryDelay = TimeSpan.FromSeconds(1),
            MaxRetryDelay = TimeSpan.FromMinutes(2),
            BatchSize = 50,
            DefaultPageSize = 100,
            TimeoutSeconds = 120,
            MaxRetryAttempts = 5,
            RetryDelayMilliseconds = 1000,
            EnableBatchOptimization = true,
            IsEnabled = true
        };
    }
}