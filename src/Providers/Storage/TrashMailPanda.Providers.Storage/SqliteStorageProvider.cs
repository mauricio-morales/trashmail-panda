using Microsoft.EntityFrameworkCore;
using Microsoft.Data.Sqlite;
using SQLitePCL;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using TrashMailPanda.Shared;
using TrashMailPanda.Providers.Storage.Models;

namespace TrashMailPanda.Providers.Storage;

/// <summary>
/// SQLite implementation of IStorageProvider with Entity Framework Core and encryption support.
/// Provides encrypted local storage for all application data.
/// 
/// ⚠️ DEPRECATED: This class is being phased out in favor of domain-specific services.
/// 
/// Migration Guide:
/// - For user rules → Use IUserRulesService
/// - For email metadata → Use IEmailMetadataService  
/// - For classification history → Use IClassificationHistoryService
/// - For credentials/tokens → Use ICredentialStorageService
/// - For app configuration → Use IConfigurationService
/// 
/// Production code should use StorageProviderAdapter (implements IStorageProvider via domain services).
/// This class remains for backward compatibility and will be removed in a future version.
/// </summary>
[Obsolete("SqliteStorageProvider is deprecated. Use StorageProviderAdapter with domain-specific services instead. See class documentation for migration guide.")]
public class SqliteStorageProvider : IStorageProvider, IDisposable
{
    private readonly string _databasePath;
    private readonly string _password;
    private readonly SemaphoreSlim _databaseLock;
    private readonly bool _ownsLock;
    private TrashMailPandaDbContext? _context;
    private bool _initialized = false;
    private bool _disposed = false;
    // Kept open for the lifetime of the provider so in-memory databases retain their schema.
    private SqliteConnection? _persistentConnection;

    /// <summary>
    /// Legacy constructor for backward compatibility.
    /// Creates its own database lock - NOT RECOMMENDED for production use.
    /// Use the constructor with SemaphoreSlim injection instead.
    /// </summary>
    public SqliteStorageProvider(string databasePath, string password)
    {
        _databasePath = databasePath;
        _password = password;
        _databaseLock = new SemaphoreSlim(1, 1);
        _ownsLock = true;
    }

    /// <summary>
    /// Recommended constructor with singleton semaphore injection.
    /// Ensures proper concurrency control across all storage access.
    /// </summary>
    public SqliteStorageProvider(string databasePath, string password, SemaphoreSlim databaseLock)
    {
        _databasePath = databasePath;
        _password = password;
        _databaseLock = databaseLock ?? throw new ArgumentNullException(nameof(databaseLock));
        _ownsLock = false;
    }

    public async Task InitAsync()
    {
        if (_initialized) return;

        try
        {
            // Initialize SQLitePCLRaw with SQLCipher bundle
            Batteries_V2.Init();

            var optionsBuilder = new DbContextOptionsBuilder<TrashMailPandaDbContext>();

            if (_databasePath == ":memory:")
            {
                // For in-memory SQLite, each connection is a separate, isolated database.
                // We must keep ONE connection open for the entire lifetime of this provider
                // so that the schema created by MigrateAsync() persists for all subsequent
                // operations — closing and re-opening would destroy all tables.
                _persistentConnection = new SqliteConnection("Data Source=:memory:");
                _persistentConnection.Open();

                if (!string.IsNullOrEmpty(_password))
                {
                    using var pragmaCmd = _persistentConnection.CreateCommand();
                    // Parameterised PRAGMA is not supported; escape single quotes manually.
                    pragmaCmd.CommandText = $"PRAGMA key = '{_password.Replace("'", "''")}';";
                    pragmaCmd.ExecuteNonQuery();
                }

                optionsBuilder.UseSqlite(_persistentConnection);
            }
            else
            {
                // Ensure directory exists
                var directory = Path.GetDirectoryName(_databasePath);
                if (!string.IsNullOrEmpty(directory) && !Directory.Exists(directory))
                {
                    Directory.CreateDirectory(directory);
                }

                var connectionString = new SqliteConnectionStringBuilder
                {
                    DataSource = _databasePath,
                    Password = _password
                }.ToString();

                optionsBuilder.UseSqlite(connectionString);
            }

            _context = new TrashMailPandaDbContext(optionsBuilder.Options);

            // Apply migrations to create/update schema
            await _context.Database.MigrateAsync();

            _initialized = true;
        }
        catch (Exception ex)
        {
            throw new InvalidOperationException($"Failed to initialize SQLite storage: {ex.Message}", ex);
        }
    }

    public async Task<UserRules> GetUserRulesAsync()
    {
        EnsureInitialized();

        await _databaseLock.WaitAsync();
        try
        {
            var rules = await _context!.UserRules.ToListAsync();

            var alwaysKeepSenders = rules.Where(r => r.RuleType == "always_keep" && r.RuleKey == "sender")
                .Select(r => r.RuleValue).ToList();
            var alwaysKeepDomains = rules.Where(r => r.RuleType == "always_keep" && r.RuleKey == "domain")
                .Select(r => r.RuleValue).ToList();
            var alwaysKeepListIds = rules.Where(r => r.RuleType == "always_keep" && r.RuleKey == "listid")
                .Select(r => r.RuleValue).ToList();
            var autoTrashSenders = rules.Where(r => r.RuleType == "auto_trash" && r.RuleKey == "sender")
                .Select(r => r.RuleValue).ToList();
            var autoTrashDomains = rules.Where(r => r.RuleType == "auto_trash" && r.RuleKey == "domain")
                .Select(r => r.RuleValue).ToList();
            var autoTrashListIds = rules.Where(r => r.RuleType == "auto_trash" && r.RuleKey == "listid")
                .Select(r => r.RuleValue).ToList();

            return new UserRules
            {
                AlwaysKeep = new AlwaysKeepRules
                {
                    Senders = alwaysKeepSenders,
                    Domains = alwaysKeepDomains,
                    ListIds = alwaysKeepListIds
                },
                AutoTrash = new AutoTrashRules
                {
                    Senders = autoTrashSenders,
                    Domains = autoTrashDomains,
                    ListIds = autoTrashListIds
                }
            };
        }
        finally
        {
            _databaseLock.Release();
        }
    }

    public async Task UpdateUserRulesAsync(UserRules rules)
    {
        EnsureInitialized();

        await _databaseLock.WaitAsync();
        try
        {
            using var transaction = await _context!.Database.BeginTransactionAsync();
            try
            {
                // Clear existing rules
                _context.UserRules.RemoveRange(_context.UserRules);

                var now = DateTime.UtcNow;
                var newRules = new List<UserRuleEntity>();

                // Helper to add rules
                void AddRules(string ruleType, string ruleKey, IEnumerable<string> values)
                {
                    foreach (var value in values)
                    {
                        newRules.Add(new UserRuleEntity
                        {
                            RuleType = ruleType,
                            RuleKey = ruleKey,
                            RuleValue = value,
                            CreatedAt = now,
                            UpdatedAt = now
                        });
                    }
                }

                // Insert always keep rules
                AddRules("always_keep", "sender", rules.AlwaysKeep.Senders);
                AddRules("always_keep", "domain", rules.AlwaysKeep.Domains);
                AddRules("always_keep", "listid", rules.AlwaysKeep.ListIds);

                // Insert auto trash rules
                AddRules("auto_trash", "sender", rules.AutoTrash.Senders);
                AddRules("auto_trash", "domain", rules.AutoTrash.Domains);
                AddRules("auto_trash", "listid", rules.AutoTrash.ListIds);

                await _context.UserRules.AddRangeAsync(newRules);
                await _context.SaveChangesAsync();
                await transaction.CommitAsync();
            }
            catch
            {
                await transaction.RollbackAsync();
                throw;
            }
        }
        finally
        {
            _databaseLock.Release();
        }
    }

    public async Task<EmailMetadata?> GetEmailMetadataAsync(string emailId)
    {
        EnsureInitialized();

        var entity = await _context!.EmailMetadata.FindAsync(emailId);
        if (entity == null)
            return null;

        var reasons = string.IsNullOrEmpty(entity.ReasonsJson) ? null :
            JsonSerializer.Deserialize<string[]>(entity.ReasonsJson);

        UserAction? userAction = string.IsNullOrEmpty(entity.UserAction) ? null :
            Enum.Parse<UserAction>(entity.UserAction);

        return new EmailMetadata
        {
            Id = entity.EmailId,
            Classification = entity.Classification,
            Confidence = entity.Confidence,
            Reasons = reasons,
            BulkKey = entity.BulkKey,
            LastClassified = entity.LastClassified ?? DateTime.MinValue,
            UserAction = userAction,
            UserActionTimestamp = entity.UserActionTimestamp
        };
    }

    public async Task SetEmailMetadataAsync(string emailId, EmailMetadata metadata)
    {
        EnsureInitialized();

        var entity = await _context!.EmailMetadata.FindAsync(emailId);
        if (entity == null)
        {
            entity = new EmailMetadataEntity { EmailId = emailId };
            _context.EmailMetadata.Add(entity);
        }

        entity.Classification = metadata.Classification;
        entity.Confidence = metadata.Confidence;
        entity.ReasonsJson = metadata.Reasons != null ? JsonSerializer.Serialize(metadata.Reasons) : null;
        entity.BulkKey = metadata.BulkKey;
        entity.LastClassified = metadata.LastClassified;
        entity.UserAction = metadata.UserAction?.ToString();
        entity.UserActionTimestamp = metadata.UserActionTimestamp;

        await _context.SaveChangesAsync();
    }

    public async Task BulkSetEmailMetadataAsync(IReadOnlyList<EmailMetadataEntry> entries)
    {
        EnsureInitialized();

        using var transaction = await _context!.Database.BeginTransactionAsync();
        try
        {
            foreach (var entry in entries)
            {
                await SetEmailMetadataAsync(entry.Id, entry.Metadata);
            }
            await transaction.CommitAsync();
        }
        catch
        {
            await transaction.RollbackAsync();
            throw;
        }
    }

    public async Task<IReadOnlyList<ClassificationHistoryItem>> GetClassificationHistoryAsync(HistoryFilters? filters = null)
    {
        EnsureInitialized();

        var query = _context!.ClassificationHistory.AsQueryable();

        if (filters != null)
        {
            if (filters.After.HasValue)
                query = query.Where(h => h.Timestamp >= filters.After.Value);

            if (filters.Before.HasValue)
                query = query.Where(h => h.Timestamp <= filters.Before.Value);

            if (filters.Limit.HasValue)
                query = query.Take(filters.Limit.Value);
        }

        var entities = await query.OrderByDescending(h => h.Timestamp).ToListAsync();

        return entities.Select(e =>
        {
            var reasons = JsonSerializer.Deserialize<string[]>(e.ReasonsJson) ?? Array.Empty<string>();
            UserFeedback? userFeedback = string.IsNullOrEmpty(e.UserFeedback) ? null :
                Enum.Parse<UserFeedback>(e.UserFeedback);

            return new ClassificationHistoryItem
            {
                Timestamp = e.Timestamp,
                EmailId = e.EmailId,
                Classification = e.Classification,
                Confidence = e.Confidence,
                Reasons = reasons,
                UserAction = e.UserAction,
                UserFeedback = userFeedback
            };
        }).ToList();
    }

    public async Task AddClassificationResultAsync(ClassificationHistoryItem result)
    {
        EnsureInitialized();

        var entity = new ClassificationHistoryEntity
        {
            Timestamp = result.Timestamp,
            EmailId = result.EmailId,
            Classification = result.Classification,
            Confidence = result.Confidence,
            ReasonsJson = JsonSerializer.Serialize(result.Reasons),
            UserAction = result.UserAction,
            UserFeedback = result.UserFeedback?.ToString()
        };

        _context!.ClassificationHistory.Add(entity);
        await _context.SaveChangesAsync();
    }

    public async Task<IReadOnlyDictionary<string, string>> GetEncryptedTokensAsync()
    {
        EnsureInitialized();

        var tokens = await _context!.EncryptedTokens.ToListAsync();
        return tokens.ToDictionary(t => t.Provider, t => t.EncryptedToken);
    }

    public async Task SetEncryptedTokenAsync(string provider, string encryptedToken)
    {
        EnsureInitialized();

        var entity = await _context!.EncryptedTokens.FindAsync(provider);
        if (entity == null)
        {
            entity = new EncryptedTokenEntity { Provider = provider };
            _context.EncryptedTokens.Add(entity);
        }

        entity.EncryptedToken = encryptedToken;
        entity.CreatedAt = DateTime.UtcNow;

        await _context.SaveChangesAsync();
    }

    public async Task<string?> GetEncryptedCredentialAsync(string key)
    {
        EnsureInitialized();

        var entity = await _context!.EncryptedCredentials.FindAsync(key);
        return entity?.EncryptedValue;
    }

    public async Task SetEncryptedCredentialAsync(string key, string encryptedValue, DateTime? expiresAt = null)
    {
        EnsureInitialized();

        var entity = await _context!.EncryptedCredentials.FindAsync(key);
        if (entity == null)
        {
            entity = new EncryptedCredentialEntity { Key = key };
            _context.EncryptedCredentials.Add(entity);
        }

        entity.EncryptedValue = encryptedValue;
        entity.CreatedAt = DateTime.UtcNow;
        entity.ExpiresAt = expiresAt;

        await _context.SaveChangesAsync();
    }

    public async Task RemoveEncryptedCredentialAsync(string key)
    {
        EnsureInitialized();

        var entity = await _context!.EncryptedCredentials.FindAsync(key);
        if (entity != null)
        {
            _context.EncryptedCredentials.Remove(entity);
            await _context.SaveChangesAsync();
        }
    }

    public async Task<IReadOnlyList<string>> GetExpiredCredentialKeysAsync()
    {
        EnsureInitialized();

        var now = DateTime.UtcNow;
        var expiredKeys = await _context!.EncryptedCredentials
            .Where(c => c.ExpiresAt != null && c.ExpiresAt <= now)
            .Select(c => c.Key)
            .ToListAsync();

        return expiredKeys;
    }

    public async Task<IReadOnlyList<string>> GetAllEncryptedCredentialKeysAsync()
    {
        EnsureInitialized();

        var keys = await _context!.EncryptedCredentials
            .Select(c => c.Key)
            .ToListAsync();

        return keys;
    }

    public async Task<AppConfig> GetConfigAsync()
    {
        EnsureInitialized();

        var configEntries = await _context!.AppConfig.ToListAsync();
        var config = new AppConfig();

        foreach (var entry in configEntries)
        {
            switch (entry.Key)
            {
                case "ConnectionState":
                    config.ConnectionState = JsonSerializer.Deserialize<TrashMailPanda.Shared.ConnectionState>(entry.Value);
                    break;
                case "ProcessingSettings":
                    config.ProcessingSettings = JsonSerializer.Deserialize<ProcessingSettings>(entry.Value);
                    break;
                case "UISettings":
                    config.UISettings = JsonSerializer.Deserialize<UISettings>(entry.Value);
                    break;
            }
        }

        return config;
    }

    public async Task UpdateConfigAsync(AppConfig config)
    {
        EnsureInitialized();

        using var transaction = await _context!.Database.BeginTransactionAsync();
        try
        {
            await SetConfigValueAsync("ConnectionState", config.ConnectionState);
            await SetConfigValueAsync("ProcessingSettings", config.ProcessingSettings);
            await SetConfigValueAsync("UISettings", config.UISettings);

            await transaction.CommitAsync();
        }
        catch
        {
            await transaction.RollbackAsync();
            throw;
        }
    }

    private async Task SetConfigValueAsync(string key, object? value)
    {
        var entity = await _context!.AppConfig.FindAsync(key);
        if (entity == null)
        {
            entity = new AppConfigEntity { Key = key };
            _context.AppConfig.Add(entity);
        }

        entity.Value = value != null ? JsonSerializer.Serialize(value) : string.Empty;
        await _context.SaveChangesAsync();
    }

    private void EnsureInitialized()
    {
        if (!_initialized || _context == null)
            throw new InvalidOperationException("Storage provider not initialized. Call InitAsync first.");

        if (_disposed)
            throw new ObjectDisposedException(nameof(SqliteStorageProvider));
    }

    public void Dispose()
    {
        if (_disposed) return;

        if (_context != null)
        {
            try
            {
                // Force WAL checkpoint before closing to release file locks
                // Critical on Windows where SQLite WAL mode can keep auxiliary files locked
                _context.Database.ExecuteSqlRaw("PRAGMA wal_checkpoint(TRUNCATE);");
                _context.Database.ExecuteSqlRaw("PRAGMA journal_mode=DELETE;");
            }
            catch
            {
                // Ignore errors during cleanup
            }

            _context.Dispose();
            _context = null;
        }

        // Dispose the semaphore only if we own it (legacy constructor)
        if (_ownsLock)
        {
            _databaseLock?.Dispose();
        }

        // Close the persistent in-memory connection after the context is disposed.
        _persistentConnection?.Dispose();
        _persistentConnection = null;

        _disposed = true;
        GC.SuppressFinalize(this);
    }
}
