using System;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace TrashMailPanda.Shared;

/// <summary>
/// Abstract interface for local storage providers (SQLite, IndexedDB, etc.)
/// Provides consistent data persistence across different storage backends
/// </summary>
public interface IStorageProvider
{
    /// <summary>
    /// Initialize the storage provider
    /// </summary>
    Task InitAsync();

    // User rules and learning data
    Task<UserRules> GetUserRulesAsync();
    Task UpdateUserRulesAsync(UserRules rules);

    // Email metadata cache (for classification history)
    Task<EmailMetadata?> GetEmailMetadataAsync(string emailId);
    Task SetEmailMetadataAsync(string emailId, EmailMetadata metadata);
    Task BulkSetEmailMetadataAsync(IReadOnlyList<EmailMetadataEntry> entries);

    // Classification history and analytics
    Task<IReadOnlyList<ClassificationHistoryItem>> GetClassificationHistoryAsync(HistoryFilters? filters = null);
    Task AddClassificationResultAsync(ClassificationHistoryItem result);

    // Encrypted token storage
    Task<IReadOnlyDictionary<string, string>> GetEncryptedTokensAsync();
    Task SetEncryptedTokenAsync(string provider, string encryptedToken);

    // Encrypted credential storage with master key encryption
    Task<string?> GetEncryptedCredentialAsync(string key);
    Task SetEncryptedCredentialAsync(string key, string encryptedValue, DateTime? expiresAt = null);
    Task RemoveEncryptedCredentialAsync(string key);
    Task<IReadOnlyList<string>> GetExpiredCredentialKeysAsync();
    Task<IReadOnlyList<string>> GetAllEncryptedCredentialKeysAsync();

    // Configuration
    Task<AppConfig> GetConfigAsync();
    Task UpdateConfigAsync(AppConfig config);

    // Contacts cache (for fast contact lookups and trust signals)
    Task<BasicContactInfo?> GetContactAsync(string contactId);
    Task SetContactAsync(BasicContactInfo contact);
    Task<string?> GetContactIdByEmailAsync(string normalizedEmail);
    Task<TrustSignalInfo?> GetTrustSignalAsync(string contactId);
    Task SetTrustSignalAsync(TrustSignalInfo trustSignal);
    Task RemoveContactAsync(string contactId);
    Task ClearContactsCacheAsync();
}