# Storage Architecture Migration Guide

## Overview

The TrashMail Panda storage layer has been refactored from a monolithic `SqliteStorageProvider` to a **domain-driven architecture** with specialized services. This document guides you through migrating from the legacy provider to the new architecture.

## Architecture Changes

### Before (Legacy)
```
SqliteStorageProvider (implements IStorageProvider)
    ↓
  Direct EF Core DbContext access
    ↓
  SQLite database with mixed concerns
```

### After (New Architecture)
```
IStorageProvider (backward compatibility)
    ↓
StorageProviderAdapter  ← Use this in production
    ↓
Domain Services (use these directly for new code)
    ├── IUserRulesService
    ├── IEmailMetadataService
    ├── IClassificationHistoryService
    ├── ICredentialStorageService
    └── IConfigurationService
    ↓
IStorageRepository (low-level data access)
    ↓
EF Core DbContext + Singleton SemaphoreSlim
    ↓
SQLite with SQLCipher encryption
```

## Migration Path by Feature

### 1. User Rules

**Before:**
```csharp
var provider = serviceProvider.GetRequiredService<IStorageProvider>();
var rules = await provider.GetUserRulesAsync();
await provider.UpdateUserRulesAsync(rules);
```

**After:**
```csharp
var rulesService = serviceProvider.GetRequiredService<IUserRulesService>();
var rulesResult = await rulesService.GetUserRulesAsync();
if (rulesResult.IsSuccess)
{
    var rules = rulesResult.Value;
    var updateResult = await rulesService.UpdateUserRulesAsync(rules);
}
```

**Key Differences:**
- Returns `Result<T>` pattern instead of throwing exceptions
- Explicit error handling required
- More testable with clear success/failure paths

### 2. Email Metadata

**Before:**
```csharp
await provider.SetEmailMetadataAsync(emailId, metadata);
var metadata = await provider.GetEmailMetadataAsync(emailId);
```

**After:**
```csharp
var metadataService = serviceProvider.GetRequiredService<IEmailMetadataService>();
var setResult = await metadataService.SetEmailMetadataAsync(emailId, metadata);
var getResult = await metadataService.GetEmailMetadataAsync(emailId);
```

### 3. Classification History

**Before:**
```csharp
await provider.AddClassificationResultAsync(historyItem);
var history = await provider.GetClassificationHistoryAsync(filters);
```

**After:**
```csharp
var historyService = serviceProvider.GetRequiredService<IClassificationHistoryService>();
var addResult = await historyService.AddClassificationResultAsync(historyItem);
var historyResult = await historyService.GetHistoryAsync(filters);
```

**New Features:**
- `GetAccuracyMetricsAsync()` - Calculate ML model precision/recall
- `GetFeedbackStatsAsync()` - Analyze user feedback patterns
- `DeleteHistoryOlderThanAsync()` - Archive cleanup

### 4. Encrypted Credentials & Tokens

**Before:**
```csharp
await provider.SetEncryptedCredentialAsync(key, value, expiresAt);
var credential = await provider.GetEncryptedCredentialAsync(key);
await provider.SetEncryptedTokenAsync(provider, token);
```

**After:**
```csharp
var credService = serviceProvider.GetRequiredService<ICredentialStorageService>();
var setResult = await credService.SetEncryptedCredentialAsync(key, value, expiresAt);
var getResult = await credService.GetEncryptedCredentialAsync(key);
var tokenResult = await credService.SetEncryptedTokenAsync(provider, token);
```

**New Features:**
- `CleanupExpiredCredentialsAsync()` - Automatic expiration cleanup
- Better listing and enumeration APIs

### 5. Application Configuration

**Before:**
```csharp
var config = await provider.GetConfigAsync();
await provider.UpdateConfigAsync(config);
```

**After:**
```csharp
var configService = serviceProvider.GetRequiredService<IConfigurationService>();
var configResult = await configService.GetConfigAsync();
var updateResult = await configService.UpdateConfigAsync(config);
```

**New Features:**
- `UpdateConnectionStateAsync()` - Update connection state only
- `UpdateProcessingSettingsAsync()` - Update processing settings only
- `UpdateUISettingsAsync()` - Update UI settings only
- `ResetToDefaultsAsync()` - Reset configuration

## Dependency Injection Setup

The new services are automatically registered when you use the existing storage setup:

```csharp
// In ServiceCollectionExtensions.cs - already configured
services.AddSingleton<IStorageRepository, SqliteStorageRepository>();
services.AddSingleton<IUserRulesService, UserRulesService>();
services.AddSingleton<IEmailMetadataService, EmailMetadataService>();
services.AddSingleton<IClassificationHistoryService, ClassificationHistoryService>();
services.AddSingleton<ICredentialStorageService, CredentialStorageService>();
services.AddSingleton<IConfigurationService, ConfigurationService>();

// Backward compatibility adapter
services.AddSingleton<IStorageProvider, StorageProviderAdapter>();
```

## Using StorageProviderAdapter (Recommended for Minimal Changes)

If you need to maintain `IStorageProvider` compatibility while benefiting from the new architecture:

```csharp
var provider = serviceProvider.GetRequiredService<IStorageProvider>();
// StorageProviderAdapter implements IStorageProvider
// All calls delegate to domain services internally
var rules = await provider.GetUserRulesAsync();
```

**Note:** StorageProviderAdapter converts `Result<T>` patterns back to exceptions for backward compatibility.

## Result Pattern

All domain services use the `Result<T>` pattern for explicit error handling:

```csharp
var result = await service.GetUserRulesAsync();

if (result.IsSuccess)
{
    var data = result.Value;
    // Process data
}
else
{
    var error = result.Error;
    _logger.LogError("Operation failed: {Error}", error.Message);
    // Handle error (don't catch exceptions)
}
```

**Error Types:**
- `ValidationError` - Invalid input or business rule violation
- `StorageError` - Database or persistence failure
- `NetworkError` - External service communication failure
- `AuthenticationError` - Authentication/authorization failure

## Testing Considerations

### Unit Testing Domain Services

```csharp
var mockRepository = new Mock<IStorageRepository>();
mockRepository
    .Setup(r => r.GetByIdAsync<UserRules>(It.IsAny<string>(), It.IsAny<CancellationToken>()))
    .ReturnsAsync(Result<UserRules>.Success(expectedRules));

var service = new UserRulesService(mockRepository.Object, mockLogger.Object);
var result = await service.GetUserRulesAsync();

Assert.True(result.IsSuccess);
Assert.Equal(expectedRules, result.Value);
```

### Integration Testing

For integration tests, you can:
1. **Use domain services directly** (recommended for new tests)
2. **Use StorageProviderAdapter** for backward compatibility
3. **Keep using SqliteStorageProvider** for legacy test compatibility

## Migration Checklist

- [ ] Identify all `IStorageProvider` usages in your code
- [ ] Determine which domain service each usage should map to
- [ ] Update dependency injection to use specific services
- [ ] Replace method calls with domain service equivalents
- [ ] Add Result<T> pattern error handling
- [ ] Update unit tests to use domain services
- [ ] Verify integration tests still pass
- [ ] Remove `SqliteStorageProvider` instantiations (except in legacy tests)
- [ ] Update documentation to reference new services

## Timeline

- **Phase 1-3 (Completed)**: Domain services implemented, backward compatibility maintained
- **Phase 4 (Current)**: `SqliteStorageProvider` marked as obsolete, migration encouraged
- **Phase 5 (Future)**: Remove `SqliteStorageProvider` entirely once all consumers migrated

## Benefits of Migration

1. **Separation of Concerns**: Each service has a single responsibility
2. **Better Testability**: Easier to mock specific domain operations
3. **Result Pattern**: Explicit error handling without exceptions
4. **Thread Safety**: Singleton semaphore prevents SQLite concurrency issues
5. **Performance**: Optimized data access patterns per domain
6. **Maintainability**: Smaller, focused classes easier to understand

## Support

For questions or migration assistance, see:
- Architecture documentation: `/docs/ARCHITECTURE_SHIFT_TO_LOCAL_ML.md`
- Provider patterns: `/docs/EMAIL_PROVIDER_PATTERNS.md`
- Security patterns: `/docs/SECURE_STORAGE_PATTERNS.md`
