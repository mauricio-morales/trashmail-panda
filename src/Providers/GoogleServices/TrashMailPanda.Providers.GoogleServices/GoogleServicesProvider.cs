using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using TrashMailPanda.Shared;
using TrashMailPanda.Shared.Base;
using TrashMailPanda.Shared.Models;
using TrashMailPanda.Shared.Security;
using TrashMailPanda.Providers.Email;
using TrashMailPanda.Providers.Contacts;

namespace TrashMailPanda.Providers.GoogleServices;

/// <summary>
/// Unified Google Services provider that implements both IEmailProvider and IContactsProvider
/// Delegates operations to internal GmailEmailProvider and ContactsProvider while maintaining
/// unified OAuth authentication and setup experience
/// </summary>
public class GoogleServicesProvider : BaseProvider<GoogleServicesProviderConfig>, IEmailProvider, IContactsProvider
{
    private readonly GmailEmailProvider _gmailProvider;
    private readonly ContactsProvider _contactsProvider;
    private readonly IGoogleOAuthService _oauthService;
    private readonly ISecureStorageManager _secureStorageManager;
    private readonly ILogger<GoogleServicesProvider> _logger;

    /// <summary>
    /// Gets the provider name
    /// </summary>
    public override string Name => "GoogleServices";

    /// <summary>
    /// Gets the provider version
    /// </summary>
    public override string Version => "1.0.0";

    /// <summary>
    /// Initializes a new instance of the GoogleServicesProvider
    /// </summary>
    /// <param name="gmailProvider">Gmail email provider for email operations</param>
    /// <param name="contactsProvider">Contacts provider for contact operations</param>
    /// <param name="oauthService">Shared Google OAuth service</param>
    /// <param name="secureStorageManager">Secure storage manager for credentials</param>
    /// <param name="logger">Logger for the provider</param>
    public GoogleServicesProvider(
        GmailEmailProvider gmailProvider,
        ContactsProvider contactsProvider,
        IGoogleOAuthService oauthService,
        ISecureStorageManager secureStorageManager,
        ILogger<GoogleServicesProvider> logger)
        : base(logger)
    {
        _gmailProvider = gmailProvider ?? throw new ArgumentNullException(nameof(gmailProvider));
        _contactsProvider = contactsProvider ?? throw new ArgumentNullException(nameof(contactsProvider));
        _oauthService = oauthService ?? throw new ArgumentNullException(nameof(oauthService));
        _secureStorageManager = secureStorageManager ?? throw new ArgumentNullException(nameof(secureStorageManager));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    #region BaseProvider Implementation

    /// <summary>
    /// Performs Google Services provider initialization including unified OAuth setup
    /// </summary>
    /// <param name="config">Google Services provider configuration</param>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>A result indicating success or failure</returns>
    protected override async Task<Result<bool>> PerformInitializationAsync(GoogleServicesProviderConfig config, CancellationToken cancellationToken)
    {
        try
        {
            _logger.LogInformation("Initializing Google Services provider with unified OAuth");

            // Check if we have valid tokens for the combined scopes
            var hasValidTokensResult = await _oauthService.HasValidTokensAsync(
                config.CombinedScopes, "google_", cancellationToken);

            if (hasValidTokensResult.IsFailure)
            {
                _logger.LogDebug("Failed to check token validity: {Error}", hasValidTokensResult.Error.Message);
                return Result<bool>.Failure(new AuthenticationError(
                    "OAuth authentication required - no valid tokens found"));
            }

            if (!hasValidTokensResult.Value)
            {
                _logger.LogDebug("No valid stored Google credentials found for unified authentication");
                return Result<bool>.Failure(new AuthenticationError(
                    "OAuth authentication required - tokens are invalid or expired"));
            }

            // Initialize sub-providers with unified OAuth configuration
            var initResults = new List<Result<bool>>();

            // Initialize Gmail provider if enabled
            if (config.EnableGmail)
            {
                var gmailConfig = CreateGmailConfig(config);
                var gmailInitResult = await InitializeGmailProviderAsync(gmailConfig, cancellationToken);
                initResults.Add(gmailInitResult);

                if (gmailInitResult.IsFailure)
                {
                    _logger.LogWarning("Gmail provider initialization failed: {Error}", gmailInitResult.Error.Message);
                }
            }

            // Initialize Contacts provider if enabled
            if (config.EnableContacts)
            {
                var contactsConfig = CreateContactsConfig(config);
                var contactsInitResult = await InitializeContactsProviderAsync(contactsConfig, cancellationToken);
                initResults.Add(contactsInitResult);

                if (contactsInitResult.IsFailure)
                {
                    _logger.LogWarning("Contacts provider initialization failed: {Error}", contactsInitResult.Error.Message);
                }
            }

            // Check if at least one sub-provider initialized successfully
            var successfulInits = initResults.Count(r => r.IsSuccess);
            if (successfulInits == 0 && initResults.Any())
            {
                var combinedErrors = string.Join("; ", initResults.Where(r => r.IsFailure).Select(r => r.Error?.Message));
                return Result<bool>.Failure(new InitializationError(
                    $"All sub-provider initializations failed: {combinedErrors}"));
            }

            _logger.LogInformation("Google Services provider initialized successfully - {SuccessfulCount}/{TotalCount} sub-providers ready",
                successfulInits, initResults.Count);

            return Result<bool>.Success(true);
        }
        catch (Exception ex)
        {
            return Result<bool>.Failure(ex.ToProviderError("Google Services provider initialization failed"));
        }
    }

    /// <summary>
    /// Performs Google Services provider shutdown cleanup
    /// </summary>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>A result indicating success or failure</returns>
    protected override async Task<Result<bool>> PerformShutdownAsync(CancellationToken cancellationToken)
    {
        try
        {
            var shutdownTasks = new List<Task<Result<bool>>>();

            // Shutdown Gmail provider if it was initialized
            if (Configuration?.EnableGmail == true && _gmailProvider is IProvider<GmailProviderConfig> gmailProviderTyped)
            {
                shutdownTasks.Add(gmailProviderTyped.ShutdownAsync(cancellationToken));
            }

            // Shutdown Contacts provider if it was initialized
            if (Configuration?.EnableContacts == true && _contactsProvider is IProvider<ContactsProviderConfig> contactsProviderTyped)
            {
                shutdownTasks.Add(contactsProviderTyped.ShutdownAsync(cancellationToken));
            }

            if (shutdownTasks.Any())
            {
                var shutdownResults = await Task.WhenAll(shutdownTasks);
                var failures = shutdownResults.Where(r => r.IsFailure).ToList();

                if (failures.Any())
                {
                    var combinedErrors = string.Join("; ", failures.Select(r => r.Error?.Message));
                    _logger.LogWarning("Some sub-providers failed to shutdown cleanly: {Errors}", combinedErrors);
                }
            }

            return Result<bool>.Success(true);
        }
        catch (Exception ex)
        {
            return Result<bool>.Failure(ex.ToProviderError("Google Services provider shutdown failed"));
        }
    }

    /// <summary>
    /// Performs Google Services provider health checks
    /// </summary>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>Health check result</returns>
    protected override async Task<Result<HealthCheckResult>> PerformHealthCheckAsync(CancellationToken cancellationToken)
    {
        try
        {
            var healthChecks = new List<Task<Result<HealthCheckResult>>>();
            var healthData = new Dictionary<string, object>();

            // Check Gmail provider health if enabled
            if (Configuration?.EnableGmail == true && _gmailProvider is IProvider<GmailProviderConfig> gmailProviderTyped)
            {
                healthChecks.Add(gmailProviderTyped.HealthCheckAsync(cancellationToken));
            }

            // Check Contacts provider health if enabled
            if (Configuration?.EnableContacts == true && _contactsProvider is IProvider<ContactsProviderConfig> contactsProviderTyped)
            {
                healthChecks.Add(contactsProviderTyped.HealthCheckAsync(cancellationToken));
            }

            var healthResults = await Task.WhenAll(healthChecks);
            var healthyCount = healthResults.Count(r => r.IsSuccess && r.Value.Status == HealthStatus.Healthy);
            var totalCount = healthResults.Length;

            // Aggregate health status
            HealthStatus overallStatus;
            string description;

            if (totalCount == 0)
            {
                overallStatus = HealthStatus.Unhealthy;
                description = "No Google services are enabled";
            }
            else if (healthyCount == totalCount)
            {
                overallStatus = HealthStatus.Healthy;
                description = "All enabled Google services are healthy";
            }
            else if (healthyCount > 0)
            {
                overallStatus = HealthStatus.Degraded;
                description = $"{healthyCount}/{totalCount} Google services are healthy";
            }
            else
            {
                overallStatus = HealthStatus.Unhealthy;
                description = "All enabled Google services are unhealthy";
            }

            // Add diagnostic information
            var enabledServices = new List<string>();
            if (Configuration?.EnableGmail == true)
                enabledServices.Add("Gmail");
            if (Configuration?.EnableContacts == true)
                enabledServices.Add("Contacts");

            healthData["EnabledServices"] = enabledServices;

            healthData["HealthyServices"] = healthyCount;
            healthData["TotalServices"] = totalCount;

            var healthResult = overallStatus switch
            {
                HealthStatus.Healthy => HealthCheckResult.Healthy(description) with { Diagnostics = healthData },
                HealthStatus.Unhealthy => HealthCheckResult.Unhealthy(description) with { Diagnostics = healthData },
                _ => HealthCheckResult.Degraded(description) with { Diagnostics = healthData }
            };

            return Result<HealthCheckResult>.Success(healthResult);
        }
        catch (Exception ex)
        {
            return Result<HealthCheckResult>.Success(
                HealthCheckResult.FromError(ex.ToProviderError("Health check failed"), TimeSpan.Zero));
        }
    }

    #endregion

    #region IEmailProvider Implementation - Delegated to Gmail Provider

    /// <summary>
    /// Connect to Gmail using OAuth2 authentication
    /// </summary>
    /// <returns>A result indicating success or failure</returns>
    public async Task<Result<bool>> ConnectAsync()
    {
        if (Configuration?.EnableGmail != true)
        {
            return Result<bool>.Failure(new InvalidOperationError("Gmail service is not enabled"));
        }

        return await _gmailProvider.ConnectAsync();
    }

    /// <summary>
    /// List emails with filtering and pagination options
    /// </summary>
    /// <param name="options">Search and filter options</param>
    /// <returns>A result containing the list of email summaries</returns>
    public async Task<Result<IReadOnlyList<EmailSummary>>> ListAsync(ListOptions options)
    {
        if (Configuration?.EnableGmail != true)
        {
            return Result<IReadOnlyList<EmailSummary>>.Failure(new InvalidOperationError("Gmail service is not enabled"));
        }

        return await _gmailProvider.ListAsync(options);
    }

    /// <summary>
    /// Get full email content including headers and body
    /// </summary>
    /// <param name="id">Email ID</param>
    /// <returns>A result containing the complete email details</returns>
    public async Task<Result<EmailFull>> GetAsync(string id)
    {
        if (Configuration?.EnableGmail != true)
        {
            return Result<EmailFull>.Failure(new InvalidOperationError("Gmail service is not enabled"));
        }

        return await _gmailProvider.GetAsync(id);
    }

    /// <summary>
    /// Perform batch operations on multiple emails (labels, trash, etc.)
    /// </summary>
    /// <param name="request">Batch modification request</param>
    /// <returns>A result indicating success or failure</returns>
    public async Task<Result<bool>> BatchModifyAsync(BatchModifyRequest request)
    {
        if (Configuration?.EnableGmail != true)
        {
            return Result<bool>.Failure(new InvalidOperationError("Gmail service is not enabled"));
        }

        return await _gmailProvider.BatchModifyAsync(request);
    }

    /// <summary>
    /// Hard delete email (use sparingly, prefer trash)
    /// </summary>
    /// <param name="id">Email ID</param>
    /// <returns>A result indicating success or failure</returns>
    public async Task<Result<bool>> DeleteAsync(string id)
    {
        if (Configuration?.EnableGmail != true)
        {
            return Result<bool>.Failure(new InvalidOperationError("Gmail service is not enabled"));
        }

        return await _gmailProvider.DeleteAsync(id);
    }

    /// <summary>
    /// Report email as spam (provider-dependent)
    /// </summary>
    /// <param name="id">Email ID</param>
    /// <returns>A result indicating success or failure</returns>
    public async Task<Result<bool>> ReportSpamAsync(string id)
    {
        if (Configuration?.EnableGmail != true)
        {
            return Result<bool>.Failure(new InvalidOperationError("Gmail service is not enabled"));
        }

        return await _gmailProvider.ReportSpamAsync(id);
    }

    /// <summary>
    /// Report email as phishing (provider-dependent)
    /// </summary>
    /// <param name="id">Email ID</param>
    /// <returns>A result indicating success or failure</returns>
    public async Task<Result<bool>> ReportPhishingAsync(string id)
    {
        if (Configuration?.EnableGmail != true)
        {
            return Result<bool>.Failure(new InvalidOperationError("Gmail service is not enabled"));
        }

        return await _gmailProvider.ReportPhishingAsync(id);
    }

    /// <summary>
    /// Get authenticated user information
    /// </summary>
    /// <returns>A result containing authenticated user details or null if not authenticated</returns>
    public async Task<Result<AuthenticatedUserInfo?>> GetAuthenticatedUserAsync()
    {
        if (Configuration?.EnableGmail != true)
        {
            return Result<AuthenticatedUserInfo?>.Success(null);
        }

        return await _gmailProvider.GetAuthenticatedUserAsync();
    }

    /// <summary>
    /// Check Gmail provider health status
    /// </summary>
    /// <returns>Health check result</returns>
    public async Task<Result<bool>> HealthCheckAsync()
    {
        if (Configuration?.EnableGmail != true)
        {
            return Result<bool>.Failure(new InvalidOperationError("Gmail service is not enabled"));
        }

        return await _gmailProvider.HealthCheckAsync();
    }

    #endregion

    #region IContactsProvider Implementation - Delegated to Contacts Provider

    /// <summary>
    /// Check if an email or domain is in the user's contacts
    /// </summary>
    /// <param name="emailOrDomain">Email address or domain to check</param>
    /// <returns>True if the contact is known</returns>
    public async Task<bool> IsKnownAsync(string emailOrDomain)
    {
        if (Configuration?.EnableContacts != true)
        {
            return false;
        }

        return await _contactsProvider.IsKnownAsync(emailOrDomain);
    }

    /// <summary>
    /// Get the relationship strength with a contact
    /// </summary>
    /// <param name="email">Email address to check</param>
    /// <returns>Strength of relationship</returns>
    public async Task<RelationshipStrength> GetRelationshipStrengthAsync(string email)
    {
        if (Configuration?.EnableContacts != true)
        {
            return RelationshipStrength.None;
        }

        return await _contactsProvider.GetRelationshipStrengthAsync(email);
    }

    /// <summary>
    /// Get simplified contact signal with known status and relationship strength
    /// </summary>
    /// <param name="emailAddress">Email address to check</param>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>Simple contact signal with known status and relationship strength</returns>
    public async Task<Result<ContactSignal>> GetContactSignalAsync(string emailAddress, CancellationToken cancellationToken = default)
    {
        if (Configuration?.EnableContacts != true)
        {
            return Result<ContactSignal>.Success(new ContactSignal { Known = false, Strength = RelationshipStrength.None });
        }

        return await _contactsProvider.GetContactSignalAsync(emailAddress, cancellationToken);
    }

    /// <summary>
    /// Clear all cached contacts data
    /// </summary>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>True if cache was cleared successfully</returns>
    public async Task<Result<bool>> ClearCacheAsync(CancellationToken cancellationToken = default)
    {
        if (Configuration?.EnableContacts != true)
        {
            return Result<bool>.Success(true);
        }

        return await _contactsProvider.ClearCacheAsync(cancellationToken);
    }

    /// <summary>
    /// Get contact information by email address
    /// </summary>
    /// <param name="emailAddress">Email address to look up</param>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>Contact information if found</returns>
    public async Task<Result<BasicContactInfo?>> GetContactByEmailAsync(string emailAddress, CancellationToken cancellationToken = default)
    {
        if (Configuration?.EnableContacts != true)
        {
            return Result<BasicContactInfo?>.Success(null);
        }

        return await _contactsProvider.GetContactByEmailAsync(emailAddress, cancellationToken);
    }

    /// <summary>
    /// Get all contacts from the provider
    /// </summary>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>List of all contacts</returns>
    public async Task<Result<IReadOnlyList<BasicContactInfo>>> GetAllContactsAsync(CancellationToken cancellationToken = default)
    {
        if (Configuration?.EnableContacts != true)
        {
            return Result<IReadOnlyList<BasicContactInfo>>.Success(Array.Empty<BasicContactInfo>());
        }

        return await _contactsProvider.GetAllContactsAsync(cancellationToken);
    }

    /// <summary>
    /// Get detailed trust signal for a contact
    /// </summary>
    /// <param name="emailAddress">Email address to get trust signal for</param>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>Trust signal details</returns>
    public async Task<Result<TrustSignalInfo>> GetTrustSignalAsync(string emailAddress, CancellationToken cancellationToken = default)
    {
        if (Configuration?.EnableContacts != true)
        {
            return Result<TrustSignalInfo>.Failure(new InvalidOperationError("Contacts service is not enabled"));
        }

        return await _contactsProvider.GetTrustSignalAsync(emailAddress, cancellationToken);
    }

    #endregion

    #region Private Helper Methods

    /// <summary>
    /// Creates a Gmail provider configuration from the unified Google Services configuration
    /// </summary>
    private GmailProviderConfig CreateGmailConfig(GoogleServicesProviderConfig config)
    {
        // Extract Gmail-specific scopes from combined scopes
        var gmailScopes = config.CombinedScopes.Where(scope =>
            scope.Contains("gmail") || scope.Contains("mail.google.com")).ToArray();

        return new GmailProviderConfig
        {
            ClientId = config.ClientId,
            ClientSecret = config.ClientSecret,
            ApplicationName = config.ApplicationName,
            Scopes = gmailScopes.Any() ? gmailScopes : new[] { GoogleOAuthScopes.GmailModify },
            RequestTimeout = config.GmailRequestTimeout,
            MaxRetries = config.GmailMaxRetries,
            BaseRetryDelay = config.GmailBaseRetryDelay,
            MaxRetryDelay = config.GmailMaxRetryDelay,
            EnableBatchOptimization = config.GmailEnableBatchOptimization,
            BatchSize = config.GmailBatchSize,
            DefaultPageSize = config.GmailDefaultPageSize,
            TimeoutSeconds = config.TimeoutSeconds,
            MaxRetryAttempts = config.MaxRetryAttempts,
            RetryDelayMilliseconds = config.RetryDelayMilliseconds,
            IsEnabled = config.EnableGmail
        };
    }

    /// <summary>
    /// Creates a Contacts provider configuration from the unified Google Services configuration
    /// </summary>
    private ContactsProviderConfig CreateContactsConfig(GoogleServicesProviderConfig config)
    {
        // Extract Contacts-specific scopes from combined scopes
        var contactsScopes = config.CombinedScopes.Where(scope =>
            scope.Contains("contacts") || scope.Contains("userinfo")).ToArray();

        return new ContactsProviderConfig
        {
            ClientId = config.ClientId,
            ClientSecret = config.ClientSecret,
            ApplicationName = config.ApplicationName,
            Scopes = contactsScopes.Any() ? contactsScopes : new[] { GoogleOAuthScopes.ContactsReadonly },
            RequestTimeout = config.ContactsRequestTimeout,
            MaxRetries = config.ContactsMaxRetries,
            BaseRetryDelay = config.ContactsBaseRetryDelay,
            MaxRetryDelay = config.ContactsMaxRetryDelay,
            DefaultPageSize = config.ContactsDefaultPageSize,
            Cache = config.ContactsCache,
            EnableContactsCaching = config.ContactsCachingEnabled,
            EnableBackgroundSync = config.ContactsEnableBackgroundSync,
            BackgroundSyncInterval = config.ContactsBackgroundSyncInterval,
            EnableTrustSignals = config.ContactsEnableTrustSignals,
            TrustSignalCacheExpiry = config.ContactsTrustSignalCacheExpiry,
            TimeoutSeconds = config.TimeoutSeconds,
            MaxRetryAttempts = config.MaxRetryAttempts,
            RetryDelayMilliseconds = config.RetryDelayMilliseconds,
            IsEnabled = config.EnableContacts
        };
    }

    /// <summary>
    /// Initializes the Gmail provider with unified OAuth configuration
    /// </summary>
    private async Task<Result<bool>> InitializeGmailProviderAsync(GmailProviderConfig gmailConfig, CancellationToken cancellationToken)
    {
        try
        {
            if (_gmailProvider is IProvider<GmailProviderConfig> gmailProviderTyped)
            {
                _logger.LogDebug("Initializing Gmail provider with unified OAuth tokens");
                return await gmailProviderTyped.InitializeAsync(gmailConfig, cancellationToken);
            }

            return Result<bool>.Failure(new InvalidOperationError("Gmail provider does not implement expected interface"));
        }
        catch (Exception ex)
        {
            return Result<bool>.Failure(ex.ToProviderError("Gmail provider initialization failed"));
        }
    }

    /// <summary>
    /// Initializes the Contacts provider with unified OAuth configuration
    /// </summary>
    private async Task<Result<bool>> InitializeContactsProviderAsync(ContactsProviderConfig contactsConfig, CancellationToken cancellationToken)
    {
        try
        {
            if (_contactsProvider is IProvider<ContactsProviderConfig> contactsProviderTyped)
            {
                _logger.LogDebug("Initializing Contacts provider with unified OAuth tokens");
                return await contactsProviderTyped.InitializeAsync(contactsConfig, cancellationToken);
            }

            return Result<bool>.Failure(new InvalidOperationError("Contacts provider does not implement expected interface"));
        }
        catch (Exception ex)
        {
            return Result<bool>.Failure(ex.ToProviderError("Contacts provider initialization failed"));
        }
    }

    #endregion
}