using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Threading;
using System.Threading.Tasks;
using Google.Apis.Auth.OAuth2;
using Google.Apis.PeopleService.v1;
using Google.Apis.PeopleService.v1.Data;
using Google.Apis.Services;
using Google;
using Microsoft.Extensions.Logging;
using TrashMailPanda.Shared.Base;
using TrashMailPanda.Shared.Models;
using TrashMailPanda.Shared.Security;
using TrashMailPanda.Shared.Services;
using TrashMailPanda.Providers.Contacts.Models;

namespace TrashMailPanda.Providers.Contacts.Adapters;

/// <summary>
/// Google People API adapter for fetching and synchronizing contacts
/// Implements the IContactSourceAdapter interface for Google Contacts integration
/// </summary>
public class GoogleContactsAdapter : IContactSourceAdapter
{
    private readonly IGoogleOAuthService _googleOAuthService;
    private readonly ISecureStorageManager _secureStorageManager;
    private readonly ISecurityAuditLogger _securityAuditLogger;
    private readonly ILogger<GoogleContactsAdapter> _logger;
    private readonly IPhoneNumberService _phoneNumberService;
    private readonly ContactsProviderConfig _config;

    // Google People API constants
    private const string PERSON_FIELDS = "names,emailAddresses,phoneNumbers,organizations,photos,metadata";
    private const int MAX_PAGE_SIZE = 2000; // Google People API limit

    /// <summary>
    /// The contact source type this adapter handles
    /// </summary>
    public ContactSourceType SourceType => ContactSourceType.Google;

    /// <summary>
    /// Whether this adapter is enabled and configured
    /// </summary>
    public bool IsEnabled { get; private set; } = true;

    /// <summary>
    /// Display name for this contact source
    /// </summary>
    public string DisplayName => "Google Contacts";

    /// <summary>
    /// Whether this adapter supports incremental synchronization
    /// </summary>
    public bool SupportsIncrementalSync => true;

    public GoogleContactsAdapter(
        IGoogleOAuthService googleOAuthService,
        ISecureStorageManager secureStorageManager,
        ISecurityAuditLogger securityAuditLogger,
        ContactsProviderConfig config,
        IPhoneNumberService phoneNumberService,
        ILogger<GoogleContactsAdapter> logger)
    {
        _googleOAuthService = googleOAuthService ?? throw new ArgumentNullException(nameof(googleOAuthService));
        _secureStorageManager = secureStorageManager ?? throw new ArgumentNullException(nameof(secureStorageManager));
        _securityAuditLogger = securityAuditLogger ?? throw new ArgumentNullException(nameof(securityAuditLogger));
        _config = config ?? throw new ArgumentNullException(nameof(config));
        _phoneNumberService = phoneNumberService ?? throw new ArgumentNullException(nameof(phoneNumberService));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    /// <summary>
    /// Fetches contacts from Google People API
    /// </summary>
    public async Task<Result<(IEnumerable<Contact> Contacts, string? NextSyncToken)>> FetchContactsAsync(
        string? syncToken = null,
        CancellationToken cancellationToken = default)
    {
        try
        {
            _logger.LogInformation("Starting Google contacts fetch with sync token: {HasSyncToken}",
                !string.IsNullOrEmpty(syncToken));

            // Create People API service with authentication
            var serviceResult = await CreatePeopleServiceAsync(cancellationToken);
            if (serviceResult.IsFailure)
                return Result<(IEnumerable<Contact>, string?)>.Failure(serviceResult.Error);

            using var service = serviceResult.Value;

            var contacts = new List<Contact>();
            string? nextPageToken = null;
            string? nextSyncToken = null;

            do
            {
                var batchResult = await FetchContactsBatchAsync(
                    service, syncToken, nextPageToken, cancellationToken);

                if (batchResult.IsFailure)
                    return Result<(IEnumerable<Contact>, string?)>.Failure(batchResult.Error);

                var (batchContacts, pageToken, currentSyncToken) = batchResult.Value;
                contacts.AddRange(batchContacts);
                nextPageToken = pageToken;
                nextSyncToken = currentSyncToken; // Keep updating with latest

                _logger.LogDebug("Fetched {ContactCount} contacts in batch, next page: {HasNextPage}",
                    batchContacts.Count(), !string.IsNullOrEmpty(nextPageToken));

            } while (!string.IsNullOrEmpty(nextPageToken) && !cancellationToken.IsCancellationRequested);

            _logger.LogInformation("Successfully fetched {TotalContacts} Google contacts", contacts.Count);
            return Result<(IEnumerable<Contact>, string?)>.Success((contacts, nextSyncToken));
        }
        catch (GoogleApiException ex) when (ex.HttpStatusCode == HttpStatusCode.Gone)
        {
            _logger.LogWarning("Sync token expired, performing full sync");
            // Sync token expired - perform full sync
            return await FetchContactsAsync(syncToken: null, cancellationToken);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Exception fetching Google contacts");
            return Result<(IEnumerable<Contact>, string?)>.Failure(
                ex.ToProviderError("Failed to fetch contacts from Google People API"));
        }
    }

    /// <summary>
    /// Validates the adapter configuration and connectivity
    /// </summary>
    public async Task<Result<bool>> ValidateAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            _logger.LogInformation("[CONTACTS DEBUG] ValidateAsync starting...");

            // Validate configuration
            var configValidation = _config.ValidateConfiguration();
            if (configValidation.IsFailure)
            {
                _logger.LogWarning("[CONTACTS DEBUG] Config validation failed: {Error}", configValidation.Error.Message);
                return Result<bool>.Failure(configValidation.Error);
            }
            _logger.LogInformation("[CONTACTS DEBUG] Config validation passed");

            // Test connectivity with a minimal API call
            _logger.LogInformation("[CONTACTS DEBUG] Attempting to create People service...");
            var serviceResult = await CreatePeopleServiceAsync(cancellationToken);
            if (serviceResult.IsFailure)
            {
                _logger.LogWarning("[CONTACTS DEBUG] Failed to create People service: {Error}", serviceResult.Error.Message);
                return Result<bool>.Failure(serviceResult.Error);
            }
            _logger.LogInformation("[CONTACTS DEBUG] People service created successfully");

            using var service = serviceResult.Value;

            // Test with a minimal request
            _logger.LogInformation("[CONTACTS DEBUG] Attempting to execute People API request...");
            var request = service.People.Connections.List("people/me");
            request.PersonFields = "names";
            request.PageSize = 1;

            var response = await request.ExecuteAsync(cancellationToken);
            _logger.LogInformation("[CONTACTS DEBUG] People API request executed successfully, got {Count} connections",
                response?.Connections?.Count ?? 0);

            _logger.LogInformation("Google People API validation successful");
            return Result<bool>.Success(true);
        }
        catch (GoogleApiException googleEx) when (googleEx.HttpStatusCode == HttpStatusCode.Unauthorized)
        {
            _logger.LogWarning("Google People API authentication failed - tokens may be expired or invalid");
            return Result<bool>.Failure(new AuthenticationError("Google OAuth authentication required"));
        }
        catch (GoogleApiException googleEx) when (googleEx.HttpStatusCode == HttpStatusCode.Forbidden)
        {
            _logger.LogWarning("Google People API access forbidden - missing required scopes");
            return Result<bool>.Failure(new AuthenticationError("Missing required OAuth scopes for Google People API"));
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Google People API validation failed");
            return Result<bool>.Failure(ex.ToProviderError("API validation failed"));
        }
    }

    /// <summary>
    /// Gets the current sync status and metadata
    /// </summary>
    public async Task<Result<AdapterSyncStatus>> GetSyncStatusAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            // Get stored sync token
            var syncTokenResult = await _secureStorageManager.RetrieveCredentialAsync(ContactsStorageKeys.SYNC_TOKEN);

            var status = new AdapterSyncStatus
            {
                IsSyncing = false, // Would be set during active sync operations
                CurrentSyncToken = syncTokenResult.IsSuccess ? syncTokenResult.Value : null,
                LastSuccessfulSync = null, // Would be updated during sync operations
                LastSyncError = null,
                ContactCount = 0, // Would be updated with actual count
                Metadata = new Dictionary<string, object>
                {
                    ["SourceType"] = SourceType.ToString(),
                    ["SupportsIncrementalSync"] = SupportsIncrementalSync,
                    ["MaxPageSize"] = MAX_PAGE_SIZE,
                    ["IsHealthy"] = IsEnabled
                }
            };

            return Result<AdapterSyncStatus>.Success(status);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to get Google contacts sync status");
            return Result<AdapterSyncStatus>.Failure(
                ex.ToProviderError("Failed to get sync status"));
        }
    }

    /// <summary>
    /// Performs a health check on the adapter
    /// </summary>
    public async Task<Result<HealthCheckResult>> HealthCheckAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            var validationResult = await ValidateAsync(cancellationToken);

            var result = validationResult.IsSuccess
                ? HealthCheckResult.Healthy("Google People API is accessible and responsive") with
                {
                    Diagnostics = new Dictionary<string, object>
                    {
                        ["SourceType"] = SourceType.ToString(),
                        ["LastCheck"] = DateTime.UtcNow,
                        ["Configuration"] = _config.GetSanitizedCopy()
                    }
                }
                : HealthCheckResult.Unhealthy($"Google People API validation failed: {validationResult.Error.Message}") with
                {
                    Diagnostics = new Dictionary<string, object>
                    {
                        ["SourceType"] = SourceType.ToString(),
                        ["LastCheck"] = DateTime.UtcNow,
                        ["Error"] = validationResult.Error.Message
                    }
                };

            return Result<HealthCheckResult>.Success(result);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Health check failed for Google contacts adapter");

            var unhealthyResult = HealthCheckResult.Unhealthy($"Health check failed: {ex.Message}") with
            {
                Diagnostics = new Dictionary<string, object>
                {
                    ["Exception"] = ex.GetType().Name,
                    ["LastCheck"] = DateTime.UtcNow
                }
            };

            return Result<HealthCheckResult>.Success(unhealthyResult);
        }
    }

    // Private helper methods

    private async Task<Result<PeopleServiceService>> CreatePeopleServiceAsync(CancellationToken cancellationToken)
    {
        try
        {
            _logger.LogInformation("[CONTACTS DEBUG] CreatePeopleServiceAsync starting...");
            _logger.LogInformation("[CONTACTS DEBUG] Using ClientId: {ClientId}, Scopes: {Scopes}",
                string.IsNullOrEmpty(_config.ClientId) ? "<empty>" : _config.ClientId.Substring(0, 10) + "...",
                string.Join(", ", _config.Scopes));

            // Get UserCredential through the shared OAuth service
            // Use Gmail scopes since Contacts will reuse Gmail OAuth tokens
            // This avoids requiring separate OAuth flows for Gmail and Contacts
            _logger.LogInformation("[CONTACTS DEBUG] Calling GetUserCredentialAsync with Gmail scopes");
            var gmailScopes = new[] { GoogleOAuthScopes.GmailModify };
            var credentialResult = await _googleOAuthService.GetUserCredentialAsync(
                gmailScopes, // Use Gmail scopes to reuse existing tokens
                "google_", // Shared prefix for all Google services
                _config.ClientId,
                _config.ClientSecret,
                cancellationToken);

            if (credentialResult.IsFailure)
            {
                _logger.LogWarning("[CONTACTS DEBUG] GetUserCredentialAsync failed: {Error}", credentialResult.Error.Message);
                return Result<PeopleServiceService>.Failure(credentialResult.Error);
            }

            _logger.LogInformation("[CONTACTS DEBUG] GetUserCredentialAsync succeeded, got credential");
            var credential = credentialResult.Value;

            // Create the People API service
            _logger.LogInformation("[CONTACTS DEBUG] Creating PeopleServiceService...");
            var service = new PeopleServiceService(new BaseClientService.Initializer()
            {
                HttpClientInitializer = credential,
                ApplicationName = _config.ApplicationName
            });

            _logger.LogInformation("[CONTACTS DEBUG] Successfully created Google People API service");
            return Result<PeopleServiceService>.Success(service);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to create Google People API service");
            return Result<PeopleServiceService>.Failure(
                ex.ToProviderError("Failed to create People API service"));
        }
    }

    private async Task<Result<(IEnumerable<Contact> Contacts, string? NextPageToken, string? NextSyncToken)>>
        FetchContactsBatchAsync(
            PeopleServiceService service,
            string? syncToken,
            string? pageToken,
            CancellationToken cancellationToken)
    {
        try
        {
            var request = service.People.Connections.List("people/me");
            request.PersonFields = PERSON_FIELDS;
            request.PageSize = Math.Min(_config.DefaultPageSize, MAX_PAGE_SIZE);

            if (!string.IsNullOrEmpty(pageToken))
                request.PageToken = pageToken;

            if (!string.IsNullOrEmpty(syncToken))
                request.SyncToken = syncToken;
            else
                request.RequestSyncToken = true;

            var response = await request.ExecuteAsync(cancellationToken);

            var contacts = response.Connections?.Select(MapPersonToContact).Where(c => c != null).Cast<Contact>() ?? Enumerable.Empty<Contact>();

            return Result<(IEnumerable<Contact>, string?, string?)>.Success((
                contacts,
                response.NextPageToken,
                response.NextSyncToken
            ));
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to fetch contacts batch");
            return Result<(IEnumerable<Contact>, string?, string?)>.Failure(
                ex.ToProviderError("Failed to fetch contacts batch"));
        }
    }

    private Contact? MapPersonToContact(Person person)
    {
        try
        {
            if (person?.EmailAddresses == null || !person.EmailAddresses.Any())
            {
                // Skip contacts without email addresses
                return null;
            }

            var contact = new Contact
            {
                Id = GenerateContactId(person),
                SourceIdentities = new List<SourceIdentity>
                {
                    new()
                    {
                        SourceType = ContactSourceType.Google,
                        SourceContactId = person.ResourceName ?? string.Empty,
                        LastUpdatedUtc = DateTime.UtcNow,
                        IsActive = true
                    }
                },
                LastModifiedUtc = DateTime.UtcNow,
                LastSyncedUtc = DateTime.UtcNow
            };

            // Map email addresses
            var emailAddresses = person.EmailAddresses
                .Where(e => !string.IsNullOrWhiteSpace(e.Value))
                .Select(e => e.Value.ToLowerInvariant())
                .Distinct()
                .ToList();

            if (emailAddresses.Any())
            {
                contact.PrimaryEmail = emailAddresses.First();
                contact.AllEmails = emailAddresses;
            }

            // Map names
            if (person.Names?.FirstOrDefault() is var name && name != null)
            {
                contact.DisplayName = name.DisplayName ?? string.Empty;
                contact.GivenName = name.GivenName;
                contact.FamilyName = name.FamilyName;
            }

            // Map phone numbers with normalization
            if (person.PhoneNumbers?.Any() == true)
            {
                contact.PhoneNumbers = person.PhoneNumbers
                    .Select(p => NormalizePhoneNumber(p.Value))
                    .Where(p => !string.IsNullOrEmpty(p))
                    .ToList();
            }

            // Map organization
            if (person.Organizations?.FirstOrDefault() is var org && org != null)
            {
                contact.OrganizationName = org.Name;
                contact.OrganizationTitle = org.Title;
            }

            // Map photo
            if (person.Photos?.FirstOrDefault()?.Url is var photoUrl && !string.IsNullOrEmpty(photoUrl))
            {
                contact.PhotoUrl = photoUrl;
            }

            return contact;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to map person to contact: {ResourceName}", person?.ResourceName);
            return null;
        }
    }

    private string GenerateContactId(Person person)
    {
        // Generate a consistent ID based on resource name and email
        var key = $"{person.ResourceName}:{person.EmailAddresses?.FirstOrDefault()?.Value}";
        return Convert.ToBase64String(System.Text.Encoding.UTF8.GetBytes(key)).Replace("=", "").Replace("+", "-").Replace("/", "_");
    }

    private string NormalizePhoneNumber(string? phoneNumber)
    {
        // Use the injected singleton service for optimal performance
        return _phoneNumberService.NormalizePhoneNumber(phoneNumber, "US");
    }
}