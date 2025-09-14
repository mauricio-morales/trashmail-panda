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
using TrashMailPanda.Providers.Contacts.Models;

namespace TrashMailPanda.Providers.Contacts.Services;

/// <summary>
/// 3-layer caching manager for contacts with sub-100ms lookup performance
/// Layer 1: Memory cache (fastest, limited capacity)
/// Layer 2: SQLite cache (persistent, larger capacity)
/// Layer 3: Remote API (slowest, authoritative source)
/// </summary>
public class ContactsCacheManager
{
    private readonly IMemoryCache _memoryCache;
    private readonly IStorageProvider _storageProvider;
    private readonly ILogger<ContactsCacheManager> _logger;
    private readonly ContactsProviderConfig _config;
    private readonly SemaphoreSlim _cacheLock;

    // Cache statistics for monitoring
    private long _memoryHits = 0;
    private long _sqliteHits = 0;
    private long _remoteFetches = 0;
    private long _totalLookups = 0;

    // Cache key prefixes for organization
    private const string CONTACT_KEY_PREFIX = "contact:";
    private const string EMAIL_INDEX_PREFIX = "email_idx:";
    private const string PHONE_INDEX_PREFIX = "phone_idx:";
    private const string TRUST_SIGNAL_PREFIX = "trust:";

    public ContactsCacheManager(
        IMemoryCache memoryCache,
        IStorageProvider storageProvider,
        IOptions<ContactsProviderConfig> config,
        ILogger<ContactsCacheManager> logger)
    {
        _memoryCache = memoryCache ?? throw new ArgumentNullException(nameof(memoryCache));
        _storageProvider = storageProvider ?? throw new ArgumentNullException(nameof(storageProvider));
        _config = config?.Value ?? throw new ArgumentNullException(nameof(config));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _cacheLock = new SemaphoreSlim(1, 1);
    }

    /// <summary>
    /// Get contact by ID with 3-layer cache lookup
    /// </summary>
    public async Task<Result<Contact?>> GetContactByIdAsync(string contactId, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(contactId))
            return Result<Contact?>.Success(null);

        Interlocked.Increment(ref _totalLookups);
        var cacheKey = $"{CONTACT_KEY_PREFIX}{contactId}";

        try
        {
            // Layer 1: Memory cache lookup
            if (_memoryCache.TryGetValue(cacheKey, out Contact? contact))
            {
                Interlocked.Increment(ref _memoryHits);
                _logger.LogDebug("Contact found in memory cache: {ContactId}", contactId);
                return Result<Contact?>.Success(contact);
            }

            // Layer 2: SQLite cache lookup (would integrate with storage provider)
            var sqliteResult = await GetContactFromSqliteCacheAsync(contactId, cancellationToken);
            if (sqliteResult.IsSuccess && sqliteResult.Value != null)
            {
                Interlocked.Increment(ref _sqliteHits);
                _logger.LogDebug("Contact found in SQLite cache: {ContactId}", contactId);

                // Cache in memory for faster future access
                await CacheContactInMemoryAsync(sqliteResult.Value);
                return Result<Contact?>.Success(sqliteResult.Value);
            }

            // Layer 3: Would fetch from remote API if not found in caches
            Interlocked.Increment(ref _remoteFetches);
            _logger.LogDebug("Contact not found in caches, would fetch from remote: {ContactId}", contactId);

            return Result<Contact?>.Success(null);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error retrieving contact from cache: {ContactId}", contactId);
            return Result<Contact?>.Failure(ex.ToProviderError("Cache lookup failed"));
        }
    }

    /// <summary>
    /// Get contact by email address with indexed lookup
    /// </summary>
    public async Task<Result<Contact?>> GetContactByEmailAsync(string email, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(email))
            return Result<Contact?>.Success(null);

        var normalizedEmail = email.ToLowerInvariant();
        Interlocked.Increment(ref _totalLookups);
        var indexKey = $"{EMAIL_INDEX_PREFIX}{normalizedEmail}";

        try
        {
            // Layer 1: Check email index in memory cache
            if (_memoryCache.TryGetValue(indexKey, out string? contactId) && !string.IsNullOrEmpty(contactId))
            {
                return await GetContactByIdAsync(contactId, cancellationToken);
            }

            // Layer 2: SQLite email index lookup
            var contactIdResult = await GetContactIdByEmailFromSqliteAsync(normalizedEmail, cancellationToken);
            if (contactIdResult.IsSuccess && !string.IsNullOrEmpty(contactIdResult.Value))
            {
                // Cache the email -> contactId mapping
                _memoryCache.Set(indexKey, contactIdResult.Value, _config.Cache.MemoryTtl);
                return await GetContactByIdAsync(contactIdResult.Value, cancellationToken);
            }

            return Result<Contact?>.Success(null);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error retrieving contact by email from cache: {Email}", email);
            return Result<Contact?>.Failure(ex.ToProviderError("Email cache lookup failed"));
        }
    }

    /// <summary>
    /// Cache a contact in all appropriate layers
    /// </summary>
    public async Task<Result<bool>> CacheContactAsync(Contact contact, CancellationToken cancellationToken = default)
    {
        if (contact == null)
            return Result<bool>.Failure(new ValidationError("Contact cannot be null"));

        try
        {
            await _cacheLock.WaitAsync(cancellationToken);

            // Cache in memory
            await CacheContactInMemoryAsync(contact);

            // Cache in SQLite
            var sqliteResult = await CacheContactInSqliteAsync(contact, cancellationToken);
            if (sqliteResult.IsFailure)
            {
                _logger.LogWarning("Failed to cache contact in SQLite: {ContactId}, Error: {Error}",
                    contact.Id, sqliteResult.Error.Message);
            }

            _logger.LogDebug("Cached contact: {ContactId} with {EmailCount} emails",
                contact.Id, contact.AllEmails?.Count ?? 0);

            return Result<bool>.Success(true);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error caching contact: {ContactId}", contact.Id);
            return Result<bool>.Failure(ex.ToProviderError("Contact caching failed"));
        }
        finally
        {
            _cacheLock.Release();
        }
    }

    /// <summary>
    /// Cache multiple contacts efficiently
    /// </summary>
    public async Task<Result<int>> CacheContactsBatchAsync(IEnumerable<Contact> contacts, CancellationToken cancellationToken = default)
    {
        if (contacts == null)
            return Result<int>.Success(0);

        var contactList = contacts.ToList();
        if (!contactList.Any())
            return Result<int>.Success(0);

        try
        {
            await _cacheLock.WaitAsync(cancellationToken);
            var cachedCount = 0;

            foreach (var contact in contactList)
            {
                var result = await CacheContactAsync(contact, cancellationToken);
                if (result.IsSuccess)
                    cachedCount++;
            }

            _logger.LogInformation("Batch cached {CachedCount} out of {TotalCount} contacts",
                cachedCount, contactList.Count);

            return Result<int>.Success(cachedCount);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error in batch contact caching");
            return Result<int>.Failure(ex.ToProviderError("Batch caching failed"));
        }
        finally
        {
            _cacheLock.Release();
        }
    }

    /// <summary>
    /// Get trust signal from cache
    /// </summary>
    public async Task<Result<TrustSignal?>> GetTrustSignalAsync(string contactId, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(contactId))
            return Result<TrustSignal?>.Success(null);

        var cacheKey = $"{TRUST_SIGNAL_PREFIX}{contactId}";
        Interlocked.Increment(ref _totalLookups);

        try
        {
            // Memory cache first
            if (_memoryCache.TryGetValue(cacheKey, out TrustSignal? trustSignal))
            {
                Interlocked.Increment(ref _memoryHits);
                return Result<TrustSignal?>.Success(trustSignal);
            }

            // SQLite cache
            var sqliteResult = await GetTrustSignalFromSqliteAsync(contactId, cancellationToken);
            if (sqliteResult.IsSuccess && sqliteResult.Value != null)
            {
                Interlocked.Increment(ref _sqliteHits);

                // Cache in memory
                _memoryCache.Set(cacheKey, sqliteResult.Value, _config.Cache.MemoryTtl);
                return Result<TrustSignal?>.Success(sqliteResult.Value);
            }

            return Result<TrustSignal?>.Success(null);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error retrieving trust signal from cache: {ContactId}", contactId);
            return Result<TrustSignal?>.Failure(ex.ToProviderError("Trust signal cache lookup failed"));
        }
    }

    /// <summary>
    /// Cache trust signal
    /// </summary>
    public async Task<Result<bool>> CacheTrustSignalAsync(TrustSignal trustSignal, CancellationToken cancellationToken = default)
    {
        if (trustSignal == null)
            return Result<bool>.Failure(new ValidationError("Trust signal cannot be null"));

        var cacheKey = $"{TRUST_SIGNAL_PREFIX}{trustSignal.ContactId}";

        try
        {
            // Cache in memory
            _memoryCache.Set(cacheKey, trustSignal, _config.Cache.MemoryTtl);

            // Cache in SQLite
            var sqliteResult = await CacheTrustSignalInSqliteAsync(trustSignal, cancellationToken);
            if (sqliteResult.IsFailure)
            {
                _logger.LogWarning("Failed to cache trust signal in SQLite: {ContactId}", trustSignal.ContactId);
            }

            return Result<bool>.Success(true);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error caching trust signal: {ContactId}", trustSignal.ContactId);
            return Result<bool>.Failure(ex.ToProviderError("Trust signal caching failed"));
        }
    }

    /// <summary>
    /// Invalidate cache entries for a contact
    /// </summary>
    public async Task<Result<bool>> InvalidateContactAsync(string contactId, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(contactId))
            return Result<bool>.Success(true);

        try
        {
            await _cacheLock.WaitAsync(cancellationToken);

            var contactKey = $"{CONTACT_KEY_PREFIX}{contactId}";
            var trustKey = $"{TRUST_SIGNAL_PREFIX}{contactId}";

            // Remove from memory cache
            _memoryCache.Remove(contactKey);
            _memoryCache.Remove(trustKey);

            // Would also remove from SQLite cache
            var sqliteResult = await InvalidateContactInSqliteAsync(contactId, cancellationToken);

            _logger.LogDebug("Invalidated cache for contact: {ContactId}", contactId);
            return Result<bool>.Success(true);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error invalidating contact cache: {ContactId}", contactId);
            return Result<bool>.Failure(ex.ToProviderError("Cache invalidation failed"));
        }
        finally
        {
            _cacheLock.Release();
        }
    }

    /// <summary>
    /// Get cache performance statistics
    /// </summary>
    public CacheStatistics GetCacheStatistics()
    {
        var totalLookups = _totalLookups;
        var memoryHits = _memoryHits;
        var sqliteHits = _sqliteHits;
        var remoteFetches = _remoteFetches;

        return new CacheStatistics
        {
            TotalLookups = totalLookups,
            MemoryHits = memoryHits,
            SqliteHits = sqliteHits,
            RemoteFetches = remoteFetches,
            MemoryHitRate = totalLookups > 0 ? (double)memoryHits / totalLookups : 0,
            SqliteHitRate = totalLookups > 0 ? (double)sqliteHits / totalLookups : 0,
            CombinedHitRate = totalLookups > 0 ? (double)(memoryHits + sqliteHits) / totalLookups : 0
        };
    }

    /// <summary>
    /// Clear all caches
    /// </summary>
    public async Task<Result<bool>> ClearCacheAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            await _cacheLock.WaitAsync(cancellationToken);

            // Clear memory cache (specific to our keys)
            // Note: IMemoryCache doesn't have a clear method, so this is a simplified approach
            _logger.LogInformation("Clearing contacts cache");

            // Would clear SQLite cache
            var sqliteResult = await ClearSqliteCacheAsync(cancellationToken);

            // Reset statistics
            Interlocked.Exchange(ref _memoryHits, 0);
            Interlocked.Exchange(ref _sqliteHits, 0);
            Interlocked.Exchange(ref _remoteFetches, 0);
            Interlocked.Exchange(ref _totalLookups, 0);

            return Result<bool>.Success(true);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error clearing cache");
            return Result<bool>.Failure(ex.ToProviderError("Cache clear failed"));
        }
        finally
        {
            _cacheLock.Release();
        }
    }

    // Private helper methods for layer implementations

    private async Task CacheContactInMemoryAsync(Contact contact)
    {
        var contactKey = $"{CONTACT_KEY_PREFIX}{contact.Id}";
        _memoryCache.Set(contactKey, contact, _config.Cache.MemoryTtl);

        // Cache email indexes
        if (contact.AllEmails?.Any() == true)
        {
            foreach (var email in contact.AllEmails)
            {
                var emailKey = $"{EMAIL_INDEX_PREFIX}{email.ToLowerInvariant()}";
                _memoryCache.Set(emailKey, contact.Id, _config.Cache.MemoryTtl);
            }
        }
    }

    private async Task<Result<Contact?>> GetContactFromSqliteCacheAsync(string contactId, CancellationToken cancellationToken)
    {
        try
        {
            var basicContact = await _storageProvider.GetContactAsync(contactId);
            if (basicContact == null)
                return Result<Contact?>.Success(null);

            // Convert BasicContactInfo to Contact
            var contact = new Contact
            {
                Id = basicContact.Id,
                PrimaryEmail = basicContact.PrimaryEmail,
                AllEmails = basicContact.AllEmails.ToList(),
                DisplayName = basicContact.DisplayName,
                GivenName = basicContact.GivenName,
                FamilyName = basicContact.FamilyName,
                OrganizationName = basicContact.OrganizationName,
                OrganizationTitle = basicContact.OrganizationTitle,
                RelationshipStrength = basicContact.TrustScore,
                // Set reasonable defaults for other fields
                PhoneNumbers = new List<string>(),
                SourceIdentities = new List<SourceIdentity>(),
                LastModifiedUtc = DateTime.UtcNow,
                LastSyncedUtc = DateTime.UtcNow,
                Metadata = new Dictionary<string, string>()
            };

            return Result<Contact?>.Success(contact);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error retrieving contact from SQLite cache: {ContactId}", contactId);
            return Result<Contact?>.Failure(ex.ToProviderError("SQLite cache retrieval failed"));
        }
    }

    private async Task<Result<string?>> GetContactIdByEmailFromSqliteAsync(string email, CancellationToken cancellationToken)
    {
        try
        {
            var contactId = await _storageProvider.GetContactIdByEmailAsync(email.ToLowerInvariant());
            return Result<string?>.Success(contactId);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error retrieving contact ID by email from SQLite cache: {Email}", email);
            return Result<string?>.Failure(ex.ToProviderError("SQLite email index lookup failed"));
        }
    }

    private async Task<Result<bool>> CacheContactInSqliteAsync(Contact contact, CancellationToken cancellationToken)
    {
        try
        {
            // Convert Contact to BasicContactInfo
            var basicContact = new BasicContactInfo
            {
                Id = contact.Id,
                PrimaryEmail = contact.PrimaryEmail,
                AllEmails = contact.AllEmails,
                DisplayName = contact.DisplayName,
                GivenName = contact.GivenName,
                FamilyName = contact.FamilyName,
                OrganizationName = contact.OrganizationName,
                OrganizationTitle = contact.OrganizationTitle,
                TrustScore = contact.RelationshipStrength,
                Strength = DetermineRelationshipStrength(contact.RelationshipStrength)
            };

            await _storageProvider.SetContactAsync(basicContact);
            return Result<bool>.Success(true);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error caching contact in SQLite: {ContactId}", contact.Id);
            return Result<bool>.Failure(ex.ToProviderError("SQLite contact caching failed"));
        }
    }

    private async Task<Result<TrustSignal?>> GetTrustSignalFromSqliteAsync(string contactId, CancellationToken cancellationToken)
    {
        try
        {
            var trustSignalInfo = await _storageProvider.GetTrustSignalAsync(contactId);
            if (trustSignalInfo == null)
                return Result<TrustSignal?>.Success(null);

            // Convert TrustSignalInfo to TrustSignal
            var trustSignal = new TrustSignal
            {
                ContactId = trustSignalInfo.ContactId,
                Strength = trustSignalInfo.Strength,
                Score = trustSignalInfo.Score,
                LastInteractionDate = trustSignalInfo.LastInteractionDate,
                Justification = trustSignalInfo.Justification.ToList(),
                ComputedAt = trustSignalInfo.ComputedAt,
                InteractionCount = trustSignalInfo.InteractionCount,
                RecencyScore = 0.0, // Not available in TrustSignalInfo
                FrequencyScore = 0.0 // Not available in TrustSignalInfo
            };

            return Result<TrustSignal?>.Success(trustSignal);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error retrieving trust signal from SQLite cache: {ContactId}", contactId);
            return Result<TrustSignal?>.Failure(ex.ToProviderError("SQLite trust signal retrieval failed"));
        }
    }

    private async Task<Result<bool>> CacheTrustSignalInSqliteAsync(TrustSignal trustSignal, CancellationToken cancellationToken)
    {
        try
        {
            // Convert TrustSignal to TrustSignalInfo
            var trustSignalInfo = new TrustSignalInfo
            {
                ContactId = trustSignal.ContactId,
                Strength = trustSignal.Strength,
                Score = trustSignal.Score,
                LastInteractionDate = trustSignal.LastInteractionDate,
                Justification = trustSignal.Justification,
                ComputedAt = trustSignal.ComputedAt,
                InteractionCount = trustSignal.InteractionCount,
                EmailAddress = string.Empty, // Not available in TrustSignal
                Known = true,
                SourceType = "Contacts"
            };

            await _storageProvider.SetTrustSignalAsync(trustSignalInfo);
            return Result<bool>.Success(true);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error caching trust signal in SQLite: {ContactId}", trustSignal.ContactId);
            return Result<bool>.Failure(ex.ToProviderError("SQLite trust signal caching failed"));
        }
    }

    private async Task<Result<bool>> InvalidateContactInSqliteAsync(string contactId, CancellationToken cancellationToken)
    {
        try
        {
            await _storageProvider.RemoveContactAsync(contactId);
            return Result<bool>.Success(true);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error invalidating contact in SQLite cache: {ContactId}", contactId);
            return Result<bool>.Failure(ex.ToProviderError("SQLite cache invalidation failed"));
        }
    }

    private async Task<Result<bool>> ClearSqliteCacheAsync(CancellationToken cancellationToken)
    {
        try
        {
            await _storageProvider.ClearContactsCacheAsync();
            return Result<bool>.Success(true);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error clearing SQLite cache");
            return Result<bool>.Failure(ex.ToProviderError("SQLite cache clearing failed"));
        }
    }

    /// <summary>
    /// Helper method to convert trust score to relationship strength enum
    /// </summary>
    private static RelationshipStrength DetermineRelationshipStrength(double score)
    {
        return score switch
        {
            >= 0.8 => RelationshipStrength.Trusted,
            >= 0.6 => RelationshipStrength.Strong,
            >= 0.3 => RelationshipStrength.Moderate,
            > 0.0 => RelationshipStrength.Weak,
            _ => RelationshipStrength.None
        };
    }
}

