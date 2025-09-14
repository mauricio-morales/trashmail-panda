using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using TrashMailPanda.Shared;
using TrashMailPanda.Shared.Base;
using TrashMailPanda.Shared.Models;
using TrashMailPanda.Shared.Security;
using TrashMailPanda.Providers.Contacts.Models;
using TrashMailPanda.Providers.Contacts.Services;
using TrashMailPanda.Providers.Contacts.Adapters;

namespace TrashMailPanda.Providers.Contacts;

/// <summary>
/// Main contacts provider orchestrator that coordinates contact synchronization,
/// trust signal computation, and caching for enhanced email classification
/// </summary>
public class ContactsProvider : BaseProvider<ContactsProviderConfig>, IContactsProvider
{
    /// <summary>
    /// Provider name identifier
    /// </summary>
    public override string Name => "Contacts";

    /// <summary>
    /// Provider version
    /// </summary>
    public override string Version => "1.0.0";

    private readonly ContactsCacheManager _cacheManager;
    private readonly TrustSignalCalculator _trustCalculator;
    private readonly GoogleContactsAdapter _googleAdapter;
    private readonly List<IContactSourceAdapter> _adapters;
    private readonly SemaphoreSlim _syncLock;
    private readonly IOptionsMonitor<ContactsProviderConfig> _configurationMonitor;
    private readonly ISecureStorageManager _secureStorageManager;
    private readonly ISecurityAuditLogger _securityAuditLogger;

    /// <summary>
    /// Current provider configuration
    /// </summary>
    private new ContactsProviderConfig Configuration => _configurationMonitor.CurrentValue;

    // Performance tracking
    private long _totalContactsSynced = 0;
    private long _trustSignalsComputed = 0;
    private DateTime? _lastFullSync = null;
    private DateTime? _lastIncrementalSync = null;

    public ContactsProvider(
        ContactsCacheManager cacheManager,
        TrustSignalCalculator trustCalculator,
        GoogleContactsAdapter googleAdapter,
        IMemoryCache memoryCache,
        ISecureStorageManager secureStorageManager,
        ISecurityAuditLogger securityAuditLogger,
        IOptionsMonitor<ContactsProviderConfig> configurationMonitor,
        ILogger<ContactsProvider> logger)
        : base(logger)
    {
        _cacheManager = cacheManager ?? throw new ArgumentNullException(nameof(cacheManager));
        _trustCalculator = trustCalculator ?? throw new ArgumentNullException(nameof(trustCalculator));
        _googleAdapter = googleAdapter ?? throw new ArgumentNullException(nameof(googleAdapter));
        _configurationMonitor = configurationMonitor ?? throw new ArgumentNullException(nameof(configurationMonitor));
        _secureStorageManager = secureStorageManager ?? throw new ArgumentNullException(nameof(secureStorageManager));
        _securityAuditLogger = securityAuditLogger ?? throw new ArgumentNullException(nameof(securityAuditLogger));

        // Initialize adapter collection with Google adapter
        _adapters = new List<IContactSourceAdapter> { _googleAdapter };
        _syncLock = new SemaphoreSlim(1, 1);

        Logger.LogDebug("ContactsProvider initialized with {AdapterCount} contact source adapters", _adapters.Count);
    }

    /// <summary>
    /// Gets trust signal for a specific email address
    /// Primary method used by email classification systems
    /// </summary>
    public async Task<Result<TrustSignal?>> GetTrustSignalForEmailAsync(
        string emailAddress,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(emailAddress))
            return Result<TrustSignal?>.Success(null);

        try
        {
            Logger.LogDebug("Getting trust signal for email: {Email}", emailAddress);

            // First try cache lookup
            var contact = await _cacheManager.GetContactByEmailAsync(emailAddress, cancellationToken);
            if (contact.IsFailure)
            {
                Logger.LogWarning("Failed to retrieve contact from cache for {Email}: {Error}",
                    emailAddress, contact.Error.Message);
                return Result<TrustSignal?>.Success(null);
            }

            if (contact.Value == null)
            {
                Logger.LogDebug("No contact found for email: {Email}", emailAddress);
                return Result<TrustSignal?>.Success(null);
            }

            // Get cached trust signal
            var trustSignalResult = await _cacheManager.GetTrustSignalAsync(contact.Value.Id, cancellationToken);
            if (trustSignalResult.IsFailure)
            {
                Logger.LogWarning("Failed to retrieve trust signal from cache for {ContactId}: {Error}",
                    contact.Value.Id, trustSignalResult.Error.Message);
                return Result<TrustSignal?>.Success(null);
            }

            var trustSignal = trustSignalResult.Value;

            // If no cached trust signal or it's stale, compute fresh one
            if (trustSignal == null || !trustSignal.IsValid)
            {
                Logger.LogDebug("Computing fresh trust signal for contact: {ContactId}", contact.Value.Id);

                var computeResult = await _trustCalculator.CalculateTrustSignalAsync(contact.Value, null, cancellationToken);
                if (computeResult.IsSuccess)
                {
                    trustSignal = computeResult.Value;

                    // Cache the computed trust signal
                    await _cacheManager.CacheTrustSignalAsync(trustSignal, cancellationToken);
                    Interlocked.Increment(ref _trustSignalsComputed);
                }
                else
                {
                    Logger.LogWarning("Failed to compute trust signal for {ContactId}: {Error}",
                        contact.Value.Id, computeResult.Error.Message);
                    return Result<TrustSignal?>.Success(null);
                }
            }

            Logger.LogDebug("Trust signal retrieved for {Email}: {Strength} ({Score:F2})",
                emailAddress, trustSignal?.Strength, trustSignal?.Score ?? 0);

            return Result<TrustSignal?>.Success(trustSignal);
        }
        catch (Exception ex)
        {
            Logger.LogError(ex, "Error getting trust signal for email: {Email}", emailAddress);
            return Result<TrustSignal?>.Failure(ex.ToProviderError("Failed to get trust signal"));
        }
    }

    /// <summary>
    /// Performs synchronization of contacts from all configured sources
    /// Can be incremental or full sync based on availability of sync tokens
    /// </summary>
    public async Task<Result<ContactsSyncResult>> SyncContactsAsync(
        bool forceFullSync = false,
        CancellationToken cancellationToken = default)
    {
        if (!Configuration.EnableContactsCaching)
        {
            Logger.LogInformation("Contacts caching is disabled, skipping sync");
            return Result<ContactsSyncResult>.Success(new ContactsSyncResult { ContactsSynced = 0, IsSuccessful = true });
        }

        await _syncLock.WaitAsync(cancellationToken);

        try
        {
            Logger.LogInformation("Starting contacts sync: {SyncType}", forceFullSync ? "Full" : "Incremental");

            var syncResults = new List<AdapterSyncResult>();
            var totalContactsSynced = 0;

            foreach (var adapter in _adapters.Where(a => a.IsEnabled))
            {
                try
                {
                    var syncToken = forceFullSync ? null : await GetStoredSyncTokenAsync(adapter.SourceType);

                    Logger.LogDebug("Syncing from {SourceType} with sync token: {HasToken}",
                        adapter.SourceType, !string.IsNullOrEmpty(syncToken));

                    var fetchResult = await adapter.FetchContactsAsync(syncToken, cancellationToken);
                    if (fetchResult.IsFailure)
                    {
                        Logger.LogError("Failed to fetch contacts from {SourceType}: {Error}",
                            adapter.SourceType, fetchResult.Error.Message);

                        syncResults.Add(new AdapterSyncResult
                        {
                            SourceType = adapter.SourceType,
                            IsSuccessful = false,
                            ErrorMessage = fetchResult.Error.Message,
                            ContactsSynced = 0
                        });
                        continue;
                    }

                    var (contacts, nextSyncToken) = fetchResult.Value;
                    var contactsList = contacts.ToList();

                    // Cache all fetched contacts
                    if (contactsList.Any())
                    {
                        var cacheResult = await _cacheManager.CacheContactsBatchAsync(contactsList, cancellationToken);
                        if (cacheResult.IsSuccess)
                        {
                            totalContactsSynced += cacheResult.Value;
                            Logger.LogInformation("Cached {ContactCount} contacts from {SourceType}",
                                cacheResult.Value, adapter.SourceType);
                        }
                    }

                    // Store the next sync token for incremental syncs
                    if (!string.IsNullOrEmpty(nextSyncToken))
                    {
                        await StoreSyncTokenAsync(adapter.SourceType, nextSyncToken);
                    }

                    syncResults.Add(new AdapterSyncResult
                    {
                        SourceType = adapter.SourceType,
                        IsSuccessful = true,
                        ContactsSynced = contactsList.Count,
                        NextSyncToken = nextSyncToken
                    });

                    Logger.LogInformation("Successfully synced {ContactCount} contacts from {SourceType}",
                        contactsList.Count, adapter.SourceType);
                }
                catch (Exception ex)
                {
                    Logger.LogError(ex, "Error syncing contacts from {SourceType}", adapter.SourceType);

                    syncResults.Add(new AdapterSyncResult
                    {
                        SourceType = adapter.SourceType,
                        IsSuccessful = false,
                        ErrorMessage = ex.Message,
                        ContactsSynced = 0
                    });
                }
            }

            // Update sync timestamps
            if (forceFullSync)
                _lastFullSync = DateTime.UtcNow;
            else
                _lastIncrementalSync = DateTime.UtcNow;

            Interlocked.Add(ref _totalContactsSynced, totalContactsSynced);

            var result = new ContactsSyncResult
            {
                IsSuccessful = syncResults.Any(r => r.IsSuccessful),
                ContactsSynced = totalContactsSynced,
                SyncType = forceFullSync ? "Full" : "Incremental",
                SyncedAt = DateTime.UtcNow,
                AdapterResults = syncResults
            };

            Logger.LogInformation("Contacts sync completed: {TotalContacts} contacts synced from {SuccessfulAdapters}/{TotalAdapters} adapters",
                totalContactsSynced, syncResults.Count(r => r.IsSuccessful), syncResults.Count);

            return Result<ContactsSyncResult>.Success(result);
        }
        finally
        {
            _syncLock.Release();
        }
    }

    /// <summary>
    /// Gets synchronization status for all contact source adapters
    /// </summary>
    public async Task<Result<ContactsProviderStatus>> GetStatusAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            var adapterStatuses = new List<AdapterSyncStatus>();

            foreach (var adapter in _adapters)
            {
                var statusResult = await adapter.GetSyncStatusAsync(cancellationToken);
                if (statusResult.IsSuccess)
                {
                    adapterStatuses.Add(statusResult.Value);
                }
                else
                {
                    Logger.LogWarning("Failed to get status for {SourceType}: {Error}",
                        adapter.SourceType, statusResult.Error.Message);
                }
            }

            var cacheStats = _cacheManager.GetCacheStatistics();

            var status = new ContactsProviderStatus
            {
                IsEnabled = Configuration.IsEnabled,
                TotalContactsSynced = _totalContactsSynced,
                TrustSignalsComputed = _trustSignalsComputed,
                LastFullSync = _lastFullSync,
                LastIncrementalSync = _lastIncrementalSync,
                AdapterStatuses = adapterStatuses,
                CacheStatistics = cacheStats,
                IsHealthy = adapterStatuses.Any() && adapterStatuses.All(s => s.Metadata.GetValueOrDefault("IsHealthy", false).Equals(true))
            };

            return Result<ContactsProviderStatus>.Success(status);
        }
        catch (Exception ex)
        {
            Logger.LogError(ex, "Error getting contacts provider status");
            return Result<ContactsProviderStatus>.Failure(ex.ToProviderError("Failed to get provider status"));
        }
    }

    /// <summary>
    /// Computes trust signals for a batch of contacts
    /// Used for bulk processing during sync operations
    /// </summary>
    public async Task<Result<Dictionary<string, TrustSignal>>> ComputeBatchTrustSignalsAsync(
        IEnumerable<Contact> contacts,
        CancellationToken cancellationToken = default)
    {
        if (contacts == null)
            return Result<Dictionary<string, TrustSignal>>.Success(new Dictionary<string, TrustSignal>());

        try
        {
            var result = await _trustCalculator.CalculateBatchTrustSignalsAsync(contacts, null, cancellationToken);
            if (result.IsSuccess)
            {
                // Cache all computed trust signals
                foreach (var trustSignal in result.Value.Values)
                {
                    await _cacheManager.CacheTrustSignalAsync(trustSignal, cancellationToken);
                }

                Interlocked.Add(ref _trustSignalsComputed, result.Value.Count);
            }

            return result;
        }
        catch (Exception ex)
        {
            Logger.LogError(ex, "Error computing batch trust signals");
            return Result<Dictionary<string, TrustSignal>>.Failure(
                ex.ToProviderError("Batch trust signal computation failed"));
        }
    }

    /// <summary>
    /// Clears all cached contacts and trust signals
    /// </summary>
    public async Task<Result<bool>> ClearCacheAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            Logger.LogInformation("Clearing contacts provider cache");
            var result = await _cacheManager.ClearCacheAsync(cancellationToken);

            if (result.IsSuccess)
            {
                Logger.LogInformation("Contacts provider cache cleared successfully");
            }

            return result;
        }
        catch (Exception ex)
        {
            Logger.LogError(ex, "Error clearing contacts provider cache");
            return Result<bool>.Failure(ex.ToProviderError("Cache clear failed"));
        }
    }

    // IContactsProvider interface methods for backward compatibility

    /// <summary>
    /// Check if an email or domain is in the user's contacts
    /// Legacy method for backward compatibility
    /// </summary>
    public async Task<bool> IsKnownAsync(string emailOrDomain)
    {
        if (string.IsNullOrWhiteSpace(emailOrDomain))
            return false;

        try
        {
            var trustSignalResult = await GetTrustSignalForEmailAsync(emailOrDomain);
            return trustSignalResult.IsSuccess && trustSignalResult.Value != null;
        }
        catch (Exception ex)
        {
            Logger.LogWarning(ex, "Error checking if email is known: {Email}", emailOrDomain);
            return false;
        }
    }

    /// <summary>
    /// Get the relationship strength with a contact
    /// Legacy method for backward compatibility
    /// </summary>
    public async Task<RelationshipStrength> GetRelationshipStrengthAsync(string email)
    {
        if (string.IsNullOrWhiteSpace(email))
            return RelationshipStrength.None;

        try
        {
            var trustSignalResult = await GetTrustSignalForEmailAsync(email);
            if (trustSignalResult.IsSuccess && trustSignalResult.Value != null)
            {
                // Return the relationship strength directly (no mapping needed)
                return trustSignalResult.Value.Strength;
            }

            return RelationshipStrength.None;
        }
        catch (Exception ex)
        {
            Logger.LogWarning(ex, "Error getting relationship strength: {Email}", email);
            return RelationshipStrength.None;
        }
    }

    /// <summary>
    /// Get simplified contact signal for backward compatibility
    /// </summary>
    public async Task<Result<ContactSignal>> GetContactSignalAsync(string emailAddress, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(emailAddress))
            return Result<ContactSignal>.Success(new ContactSignal { Known = false, Strength = RelationshipStrength.None });

        try
        {
            var trustSignalResult = await GetTrustSignalForEmailAsync(emailAddress, cancellationToken);
            if (trustSignalResult.IsFailure)
                return Result<ContactSignal>.Failure(trustSignalResult.Error);

            var contactSignal = new ContactSignal
            {
                Known = trustSignalResult.Value != null,
                Strength = trustSignalResult.Value != null
                    ? trustSignalResult.Value.Strength
                    : RelationshipStrength.None
            };

            return Result<ContactSignal>.Success(contactSignal);
        }
        catch (Exception ex)
        {
            Logger.LogError(ex, "Error getting contact signal: {Email}", emailAddress);
            return Result<ContactSignal>.Failure(ex.ToProviderError("Failed to get contact signal"));
        }
    }

    // Protected override methods from BaseProvider

    /// <summary>
    /// Performs provider-specific initialization
    /// </summary>
    protected override async Task<Result<bool>> PerformInitializationAsync(ContactsProviderConfig config, CancellationToken cancellationToken)
    {
        try
        {
            Logger.LogInformation("Initializing ContactsProvider with {AdapterCount} adapters", _adapters.Count);

            // Initialize all adapters
            foreach (var adapter in _adapters)
            {
                // Adapters don't have explicit initialization, they're ready to use
                Logger.LogDebug("Contact source adapter ready: {SourceType}", adapter.SourceType);
            }

            Logger.LogInformation("ContactsProvider initialization completed successfully");
            return Result<bool>.Success(true);
        }
        catch (Exception ex)
        {
            Logger.LogError(ex, "ContactsProvider initialization failed");
            return Result<bool>.Failure(ex.ToProviderError("Provider initialization failed"));
        }
    }

    /// <summary>
    /// Performs provider-specific health check
    /// </summary>
    protected override async Task<Result<HealthCheckResult>> PerformHealthCheckAsync(CancellationToken cancellationToken)
    {
        try
        {
            var healthIssues = new List<HealthIssue>();
            var diagnostics = new Dictionary<string, object>
            {
                ["TotalContactsSynced"] = _totalContactsSynced,
                ["TrustSignalsComputed"] = _trustSignalsComputed,
                ["LastFullSync"] = _lastFullSync?.ToString("O") ?? "Never",
                ["LastIncrementalSync"] = _lastIncrementalSync?.ToString("O") ?? "Never",
                ["EnabledAdapters"] = _adapters.Count(a => a.IsEnabled),
                ["TotalAdapters"] = _adapters.Count
            };

            // Check adapter health
            var unhealthyAdapters = new List<string>();
            foreach (var adapter in _adapters.Where(a => a.IsEnabled))
            {
                var adapterHealthResult = await adapter.HealthCheckAsync(cancellationToken);
                if (adapterHealthResult.IsFailure || adapterHealthResult.Value.Status != HealthStatus.Healthy)
                {
                    unhealthyAdapters.Add(adapter.SourceType.ToString());
                    // Note: HealthIssue structure needs to be checked - using simple approach for now
                    Logger.LogWarning("Unhealthy adapter {SourceType}: {Error}",
                        adapter.SourceType,
                        adapterHealthResult.IsSuccess ? adapterHealthResult.Value.Description : adapterHealthResult.Error.Message);
                }
            }

            // Check cache performance
            var cacheStats = _cacheManager.GetCacheStatistics();
            diagnostics["CacheHitRate"] = cacheStats.CombinedHitRate;

            if (cacheStats.CombinedHitRate < 0.7) // Below 70% hit rate
            {
                Logger.LogWarning("Low cache hit rate: {HitRate:P1}", cacheStats.CombinedHitRate);
            }

            // Determine overall health status
            var status = unhealthyAdapters.Any() ? HealthStatus.Degraded : HealthStatus.Healthy;
            var description = status == HealthStatus.Healthy
                ? "All contact source adapters are healthy and operational"
                : $"Some contact adapters have issues: {string.Join(", ", unhealthyAdapters)}";

            var healthResult = status == HealthStatus.Healthy
                ? HealthCheckResult.Healthy(description) with { Diagnostics = diagnostics }
                : HealthCheckResult.Degraded(description) with { Diagnostics = diagnostics };

            return Result<HealthCheckResult>.Success(healthResult);
        }
        catch (Exception ex)
        {
            Logger.LogError(ex, "ContactsProvider health check failed");
            return Result<HealthCheckResult>.Success(
                HealthCheckResult.Unhealthy($"Health check failed: {ex.Message}"));
        }
    }

    /// <summary>
    /// Performs provider-specific shutdown cleanup
    /// </summary>
    protected override async Task<Result<bool>> PerformShutdownAsync(CancellationToken cancellationToken)
    {
        try
        {
            Logger.LogInformation("Shutting down ContactsProvider");

            // Wait for any ongoing sync operations
            await _syncLock.WaitAsync(cancellationToken);
            _syncLock.Release();

            Logger.LogInformation("ContactsProvider shutdown completed");
            return Result<bool>.Success(true);
        }
        catch (Exception ex)
        {
            Logger.LogError(ex, "ContactsProvider shutdown failed");
            return Result<bool>.Failure(ex.ToProviderError("Provider shutdown failed"));
        }
    }

    // Private helper methods


    private async Task<string?> GetStoredSyncTokenAsync(ContactSourceType sourceType)
    {
        try
        {
            string syncTokenKey = GetSyncTokenKey(sourceType);

            var result = await _secureStorageManager.RetrieveCredentialAsync(syncTokenKey);
            if (result.IsSuccess && !string.IsNullOrWhiteSpace(result.Value))
            {
                Logger.LogDebug("Retrieved sync token for {SourceType}: {HasToken}",
                    sourceType, !string.IsNullOrEmpty(result.Value));
                return result.Value;
            }

            Logger.LogDebug("No sync token found for {SourceType}", sourceType);
            return null;
        }
        catch (Exception ex)
        {
            Logger.LogWarning("Failed to retrieve sync token for {SourceType}: {Error}",
                sourceType, ex.Message);
            return null;
        }
    }

    private async Task StoreSyncTokenAsync(ContactSourceType sourceType, string syncToken)
    {
        try
        {
            if (string.IsNullOrWhiteSpace(syncToken))
            {
                Logger.LogWarning("Attempted to store empty sync token for {SourceType}", sourceType);
                return;
            }

            string syncTokenKey = GetSyncTokenKey(sourceType);

            var result = await _secureStorageManager.StoreCredentialAsync(syncTokenKey, syncToken);
            if (result.IsSuccess)
            {
                Logger.LogDebug("Stored sync token for {SourceType}", sourceType);

                // Log security audit event
                await _securityAuditLogger.LogCredentialOperationAsync(new CredentialOperationEvent
                {
                    Operation = "StoreSyncToken",
                    CredentialKey = syncTokenKey,
                    Success = true,
                    ErrorMessage = "Sync token stored successfully"
                });
            }
            else
            {
                Logger.LogError("Failed to store sync token for {SourceType}: {Error}",
                    sourceType, result.ErrorMessage ?? "Unknown error");

                // Log security audit event for failure
                await _securityAuditLogger.LogCredentialOperationAsync(new CredentialOperationEvent
                {
                    Operation = "StoreSyncToken",
                    CredentialKey = syncTokenKey,
                    Success = false,
                    ErrorMessage = result.ErrorMessage ?? "Failed to store sync token"
                });
            }
        }
        catch (Exception ex)
        {
            Logger.LogError("Exception storing sync token for {SourceType}: {Error}",
                sourceType, ex.Message);

            // Log security audit event for exception
            await _securityAuditLogger.LogCredentialOperationAsync(new CredentialOperationEvent
            {
                Operation = "StoreSyncToken",
                CredentialKey = GetSyncTokenKey(sourceType),
                Success = false,
                ErrorMessage = $"Exception: {ex.Message}"
            });
        }
    }

    /// <summary>
    /// Gets the secure storage key for sync tokens based on source type
    /// </summary>
    /// <param name="sourceType">The contact source type</param>
    /// <returns>The storage key for the sync token</returns>
    private static string GetSyncTokenKey(ContactSourceType sourceType)
    {
        return sourceType switch
        {
            ContactSourceType.Google => "GoogleContactsSyncToken_Google",
            ContactSourceType.Outlook => "GoogleContactsSyncToken_Outlook", // Future implementation
            ContactSourceType.Manual => "GoogleContactsSyncToken_Manual", // Future implementation - using Manual instead of Local
            _ => $"GoogleContactsSyncToken_{sourceType}"
        };
    }

    /// <summary>
    /// Get contact information by email address
    /// </summary>
    /// <param name="emailAddress">Email address to look up</param>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>Contact information if found</returns>
    public async Task<Result<BasicContactInfo?>> GetContactByEmailAsync(string emailAddress, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(emailAddress))
            return Result<BasicContactInfo?>.Failure(new ValidationError("Email address cannot be empty"));

        try
        {
            var normalizedEmail = emailAddress.ToLowerInvariant();

            // Try cache first
            var cachedContactResult = await _cacheManager.GetContactByEmailAsync(normalizedEmail);
            if (cachedContactResult.IsSuccess && cachedContactResult.Value != null)
            {
                return Result<BasicContactInfo?>.Success(MapToBasicContactInfo(cachedContactResult.Value));
            }

            // Fallback: check if known without full contact data
            var isKnown = await IsKnownAsync(emailAddress);
            if (!isKnown)
            {
                return Result<BasicContactInfo?>.Success(null);
            }

            // Create minimal contact info for known contacts
            var basicInfo = new BasicContactInfo
            {
                PrimaryEmail = normalizedEmail,
                DisplayName = normalizedEmail, // Use email as display name if no contact found
                Strength = await GetRelationshipStrengthAsync(emailAddress)
            };

            return Result<BasicContactInfo?>.Success(basicInfo);
        }
        catch (Exception ex)
        {
            Logger.LogError(ex, "Exception getting contact by email: {Email}", emailAddress);
            return Result<BasicContactInfo?>.Failure(new ProcessingError($"Failed to get contact: {ex.Message}"));
        }
    }

    /// <summary>
    /// Get all contacts from the provider
    /// </summary>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>List of all contacts</returns>
    public async Task<Result<IReadOnlyList<BasicContactInfo>>> GetAllContactsAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            // Get contacts from all enabled adapters
            var allContacts = new List<BasicContactInfo>();

            foreach (var adapter in _adapters.Where(a => a.IsEnabled))
            {
                try
                {
                    var adapterResult = await adapter.FetchContactsAsync(null, cancellationToken);
                    if (adapterResult.IsSuccess && adapterResult.Value.Contacts != null)
                    {
                        var basicContacts = adapterResult.Value.Contacts.Select(MapToBasicContactInfo);
                        allContacts.AddRange(basicContacts);
                    }
                }
                catch (Exception adapterEx)
                {
                    Logger.LogWarning(adapterEx, "Failed to get contacts from adapter {AdapterType}", adapter.SourceType);
                }
            }

            return Result<IReadOnlyList<BasicContactInfo>>.Success(allContacts.AsReadOnly());
        }
        catch (Exception ex)
        {
            Logger.LogError(ex, "Exception getting all contacts");
            return Result<IReadOnlyList<BasicContactInfo>>.Failure(new ProcessingError($"Failed to get contacts: {ex.Message}"));
        }
    }

    /// <summary>
    /// Get detailed trust signal for a contact
    /// </summary>
    /// <param name="emailAddress">Email address to get trust signal for</param>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>Trust signal details</returns>
    public async Task<Result<TrustSignalInfo>> GetTrustSignalAsync(string emailAddress, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(emailAddress))
            return Result<TrustSignalInfo>.Failure(new ValidationError("Email address cannot be empty"));

        try
        {
            var normalizedEmail = emailAddress.ToLowerInvariant();

            // Get contact info
            var contactResult = await GetContactByEmailAsync(normalizedEmail, cancellationToken);
            if (contactResult.IsFailure)
            {
                return Result<TrustSignalInfo>.Failure(contactResult.Error);
            }

            var isKnown = contactResult.Value != null;
            var strength = isKnown ? contactResult.Value!.Strength : RelationshipStrength.None;

            // Calculate trust signal using the trust calculator
            var trustInfo = new TrustSignalInfo
            {
                EmailAddress = normalizedEmail,
                ContactId = contactResult.Value?.Id ?? string.Empty,
                Known = isKnown,
                Strength = strength,
                Score = contactResult.Value?.TrustScore ?? 0.0,
                Justification = new List<string>
                {
                    isKnown ? "Contact found in address book" : "Contact not found",
                    $"Relationship strength: {strength}"
                }
            };

            return Result<TrustSignalInfo>.Success(trustInfo);
        }
        catch (Exception ex)
        {
            Logger.LogError(ex, "Exception getting trust signal for email: {Email}", emailAddress);
            return Result<TrustSignalInfo>.Failure(new ProcessingError($"Failed to get trust signal: {ex.Message}"));
        }
    }

    /// <summary>
    /// Map internal Contact model to external BasicContactInfo
    /// </summary>
    private BasicContactInfo MapToBasicContactInfo(Contact contact)
    {
        return new BasicContactInfo
        {
            Id = contact.Id,
            PrimaryEmail = contact.PrimaryEmail,
            AllEmails = contact.AllEmails.AsReadOnly(),
            DisplayName = contact.DisplayName,
            GivenName = contact.GivenName,
            FamilyName = contact.FamilyName,
            OrganizationName = contact.OrganizationName,
            OrganizationTitle = contact.OrganizationTitle,
            Strength = MapToSharedRelationshipStrength(contact.RelationshipStrength),
            TrustScore = contact.RelationshipStrength
        };
    }

    /// <summary>
    /// Map internal relationship strength to shared enum
    /// </summary>
    private RelationshipStrength MapToSharedRelationshipStrength(double score)
    {
        return score switch
        {
            >= 0.8 => RelationshipStrength.Strong,
            >= 0.3 => RelationshipStrength.Weak,
            _ => RelationshipStrength.None
        };
    }

    /// <summary>
    /// Releases resources
    /// </summary>
    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            _syncLock?.Dispose();
        }

        base.Dispose(disposing);
    }
}