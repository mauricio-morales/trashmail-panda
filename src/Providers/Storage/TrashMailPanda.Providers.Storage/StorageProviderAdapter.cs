using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using SQLitePCL;
using TrashMailPanda.Providers.Storage.Services;
using TrashMailPanda.Shared;

namespace TrashMailPanda.Providers.Storage;

/// <summary>
/// Backward-compatible adapter that implements IStorageProvider interface.
/// Delegates to domain-specific services for actual implementation.
/// This allows existing consumers to continue using IStorageProvider while
/// internally benefiting from the new layered architecture.
/// </summary>
public class StorageProviderAdapter : IStorageProvider
{
    private readonly IUserRulesService _userRulesService;
    private readonly IEmailMetadataService _emailMetadataService;
    private readonly IClassificationHistoryService _classificationHistoryService;
    private readonly ICredentialStorageService _credentialStorageService;
    private readonly IConfigurationService _configurationService;
    private readonly TrashMailPandaDbContext _dbContext;
    private readonly ILogger<StorageProviderAdapter> _logger;

    public StorageProviderAdapter(
        IUserRulesService userRulesService,
        IEmailMetadataService emailMetadataService,
        IClassificationHistoryService classificationHistoryService,
        ICredentialStorageService credentialStorageService,
        IConfigurationService configurationService,
        TrashMailPandaDbContext dbContext,
        ILogger<StorageProviderAdapter> logger)
    {
        _userRulesService = userRulesService ?? throw new ArgumentNullException(nameof(userRulesService));
        _emailMetadataService = emailMetadataService ?? throw new ArgumentNullException(nameof(emailMetadataService));
        _classificationHistoryService = classificationHistoryService ?? throw new ArgumentNullException(nameof(classificationHistoryService));
        _credentialStorageService = credentialStorageService ?? throw new ArgumentNullException(nameof(credentialStorageService));
        _configurationService = configurationService ?? throw new ArgumentNullException(nameof(configurationService));
        _dbContext = dbContext ?? throw new ArgumentNullException(nameof(dbContext));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    public async Task InitAsync()
    {
        // Initialize SQLitePCLRaw with SQLCipher bundle before any database operations
        Batteries_V2.Init();

        // Run EF migrations to create/update the database schema
        await _dbContext.Database.MigrateAsync();

        _logger.LogInformation("Storage provider adapter initialized (using domain services)");
    }

    #region User Rules

    public async Task<UserRules> GetUserRulesAsync()
    {
        var result = await _userRulesService.GetUserRulesAsync();

        if (!result.IsSuccess)
        {
            _logger.LogError("Failed to get user rules: {Error}", result.Error.Message);
            throw new InvalidOperationException($"Failed to get user rules: {result.Error.Message}");
        }

        return result.Value;
    }

    public async Task UpdateUserRulesAsync(UserRules rules)
    {
        var result = await _userRulesService.UpdateUserRulesAsync(rules);

        if (!result.IsSuccess)
        {
            _logger.LogError("Failed to update user rules: {Error}", result.Error.Message);
            throw new InvalidOperationException($"Failed to update user rules: {result.Error.Message}");
        }
    }

    #endregion

    #region Email Metadata

    public async Task<EmailMetadata?> GetEmailMetadataAsync(string emailId)
    {
        var result = await _emailMetadataService.GetEmailMetadataAsync(emailId);

        if (!result.IsSuccess)
        {
            _logger.LogError("Failed to get email metadata for {EmailId}: {Error}", emailId, result.Error.Message);
            throw new InvalidOperationException($"Failed to get email metadata: {result.Error.Message}");
        }

        return result.Value;
    }

    public async Task SetEmailMetadataAsync(string emailId, EmailMetadata metadata)
    {
        var result = await _emailMetadataService.SetEmailMetadataAsync(emailId, metadata);

        if (!result.IsSuccess)
        {
            _logger.LogError("Failed to set email metadata for {EmailId}: {Error}", emailId, result.Error.Message);
            throw new InvalidOperationException($"Failed to set email metadata: {result.Error.Message}");
        }
    }

    public async Task BulkSetEmailMetadataAsync(IReadOnlyList<EmailMetadataEntry> entries)
    {
        var result = await _emailMetadataService.BulkSetEmailMetadataAsync(entries);

        if (!result.IsSuccess)
        {
            _logger.LogError("Failed to bulk set email metadata: {Error}", result.Error.Message);
            throw new InvalidOperationException($"Failed to bulk set email metadata: {result.Error.Message}");
        }
    }

    #endregion

    #region Classification History

    public async Task<IReadOnlyList<ClassificationHistoryItem>> GetClassificationHistoryAsync(HistoryFilters? filters = null)
    {
        var result = await _classificationHistoryService.GetHistoryAsync(filters);

        if (!result.IsSuccess)
        {
            _logger.LogError("Failed to get classification history: {Error}", result.Error.Message);
            throw new InvalidOperationException($"Failed to get classification history: {result.Error.Message}");
        }

        return result.Value;
    }

    public async Task AddClassificationResultAsync(ClassificationHistoryItem result)
    {
        var addResult = await _classificationHistoryService.AddClassificationResultAsync(result);

        if (!addResult.IsSuccess)
        {
            _logger.LogError("Failed to add classification result: {Error}", addResult.Error.Message);
            throw new InvalidOperationException($"Failed to add classification result: {addResult.Error.Message}");
        }
    }

    #endregion

    #region Encrypted Tokens

    public async Task<IReadOnlyDictionary<string, string>> GetEncryptedTokensAsync()
    {
        var result = await _credentialStorageService.GetEncryptedTokensAsync();

        if (!result.IsSuccess)
        {
            _logger.LogError("Failed to get encrypted tokens: {Error}", result.Error.Message);
            throw new InvalidOperationException($"Failed to get encrypted tokens: {result.Error.Message}");
        }

        return result.Value;
    }

    public async Task SetEncryptedTokenAsync(string provider, string encryptedToken)
    {
        var result = await _credentialStorageService.SetEncryptedTokenAsync(provider, encryptedToken);

        if (!result.IsSuccess)
        {
            _logger.LogError("Failed to set encrypted token for {Provider}: {Error}", provider, result.Error.Message);
            throw new InvalidOperationException($"Failed to set encrypted token: {result.Error.Message}");
        }
    }

    #endregion

    #region Encrypted Credentials

    public async Task<string?> GetEncryptedCredentialAsync(string key)
    {
        var result = await _credentialStorageService.GetEncryptedCredentialAsync(key);

        if (!result.IsSuccess)
        {
            _logger.LogError("Failed to get encrypted credential for {Key}: {Error}", key, result.Error.Message);
            throw new InvalidOperationException($"Failed to get encrypted credential: {result.Error.Message}");
        }

        return result.Value;
    }

    public async Task SetEncryptedCredentialAsync(string key, string encryptedValue, DateTime? expiresAt = null)
    {
        var result = await _credentialStorageService.SetEncryptedCredentialAsync(key, encryptedValue, expiresAt);

        if (!result.IsSuccess)
        {
            _logger.LogError("Failed to set encrypted credential for {Key}: {Error}", key, result.Error.Message);
            throw new InvalidOperationException($"Failed to set encrypted credential: {result.Error.Message}");
        }
    }

    public async Task RemoveEncryptedCredentialAsync(string key)
    {
        var result = await _credentialStorageService.RemoveEncryptedCredentialAsync(key);

        if (!result.IsSuccess)
        {
            _logger.LogError("Failed to remove encrypted credential for {Key}: {Error}", key, result.Error.Message);
            throw new InvalidOperationException($"Failed to remove encrypted credential: {result.Error.Message}");
        }
    }

    public async Task<IReadOnlyList<string>> GetExpiredCredentialKeysAsync()
    {
        var result = await _credentialStorageService.GetExpiredCredentialKeysAsync();

        if (!result.IsSuccess)
        {
            _logger.LogError("Failed to get expired credential keys: {Error}", result.Error.Message);
            throw new InvalidOperationException($"Failed to get expired credential keys: {result.Error.Message}");
        }

        return result.Value;
    }

    public async Task<IReadOnlyList<string>> GetAllEncryptedCredentialKeysAsync()
    {
        var result = await _credentialStorageService.GetAllCredentialKeysAsync();

        if (!result.IsSuccess)
        {
            _logger.LogError("Failed to get all credential keys: {Error}", result.Error.Message);
            throw new InvalidOperationException($"Failed to get all credential keys: {result.Error.Message}");
        }

        return result.Value;
    }

    #endregion

    #region Configuration

    public async Task<AppConfig> GetConfigAsync()
    {
        var result = await _configurationService.GetConfigAsync();

        if (!result.IsSuccess)
        {
            _logger.LogError("Failed to get configuration: {Error}", result.Error.Message);
            throw new InvalidOperationException($"Failed to get configuration: {result.Error.Message}");
        }

        return result.Value;
    }

    public async Task UpdateConfigAsync(AppConfig config)
    {
        var result = await _configurationService.UpdateConfigAsync(config);

        if (!result.IsSuccess)
        {
            _logger.LogError("Failed to update configuration: {Error}", result.Error.Message);
            throw new InvalidOperationException($"Failed to update configuration: {result.Error.Message}");
        }
    }

    #endregion
}
