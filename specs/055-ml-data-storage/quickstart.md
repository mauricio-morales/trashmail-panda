# Quickstart: ML Data Storage System

**Feature**: #55 — ML Data Storage  
**Developer Guide** | **Date**: 2026-03-14

## Overview

This guide shows how to use the ML Data Storage system to store email feature vectors, complete email archives, and manage storage capacity in TrashMail Panda.

## Prerequisites

- TrashMail Panda project with Storage provider configured
- Existing SQLite database with SQLCipher encryption
- Dependency injection configured with `IEmailArchiveService`

## Basic Usage

### 1. Storing Feature Vectors

```csharp
using TrashMailPanda.Providers.Storage;
using TrashMailPanda.Providers.Storage.Models;

// Inject the email archive service
public class EmailClassificationService
{
    private readonly IEmailArchiveService _storage;
    
    public EmailClassificationService(IEmailArchiveService storage)
    {
        _storage = storage;
    }
    
    public async Task<Result<bool>> ProcessEmailAsync(Email email)
    {
        // Extract features from email
        var feature = new EmailFeatureVector
        {
            EmailId = email.Id,
            SenderDomain = ExtractDomain(email.From),
            SenderKnown = IsKnownSender(email.From),
            SpfResult = email.Headers["SPF"] ?? "none",
            DkimResult = email.Headers["DKIM"] ?? "none",
            DmarcResult = email.Headers["DMARC"] ?? "none",
            HasAttachments = email.Attachments.Count > 0,
            SubjectLength = email.Subject.Length,
            HourReceived = email.ReceivedDate.Hour,
            DayOfWeek = (int)email.ReceivedDate.DayOfWeek,
            EmailSizeLog = (float)Math.Log10(email.SizeBytes),
            RecipientCount = email.To.Count + email.Cc.Count,
            IsReply = email.Subject.StartsWith("Re:"),
            LinkCount = CountLinks(email.BodyHtml),
            ImageCount = CountImages(email.BodyHtml),
            HasTrackingPixel = DetectTrackingPixel(email.BodyHtml),
            EmailAgeDays = (DateTime.UtcNow - email.ReceivedDate).Days,
            IsInInbox = email.FolderTags.Contains("inbox"),
            IsStarred = email.IsStarred,
            IsImportant = email.IsImportant,
            WasInTrash = email.FolderTags.Contains("trash"),
            WasInSpam = email.FolderTags.Contains("spam"),
            IsArchived = !email.FolderTags.Contains("inbox"),
            ThreadMessageCount = email.ThreadCount,
            SenderFrequency = await GetSenderFrequencyAsync(email.From),
            SubjectText = email.Subject,
            BodyTextShort = email.BodyText?.Substring(0, Math.Min(500, email.BodyText.Length)),
            FeatureSchemaVersion = FeatureSchema.CurrentVersion,
            ExtractedAt = DateTime.UtcNow,
            UserCorrected = false
        };
        
        // Store the feature vector
        var result = await _storage.StoreFeatureAsync(feature);
        
        if (!result.IsSuccess)
        {
            _logger.LogError("Failed to store feature: {Error}", result.Error.Message);
            return Result.Failure(result.Error);
        }
        
        return Result.Success(true);
    }
}
```

### 2. Batch Storing Features (Archive Scanning)

```csharp
public async Task<Result<int>> ProcessArchivedEmailsAsync(IEnumerable<Email> emails)
{
    var features = new List<EmailFeatureVector>();
    
    foreach (var email in emails)
    {
        features.Add(ExtractFeatures(email)); // Use helper method
    }
    
    // Store all features in optimized batch transaction
    var result = await _storage.StoreFeaturesBatchAsync(features);
    
    if (!result.IsSuccess)
    {
        _logger.LogError("Batch feature storage failed: {Error}", result.Error.Message);
        return Result.Failure(result.Error);
    }
    
    _logger.LogInformation("Stored {Count} feature vectors", result.Value);
    return Result.Success(result.Value);
}
```

### 3. Storing Complete Email Archives

```csharp
public async Task<Result<bool>> ArchiveEmailAsync(Email email)
{
    var archive = new EmailArchiveEntry
    {
        EmailId = email.Id,
        ThreadId = email.ThreadId,
        ProviderType = "gmail", // or "imap", "outlook"
        HeadersJson = JsonSerializer.Serialize(email.Headers),
        BodyText = email.BodyText,
        BodyHtml = email.BodyHtml,
        FolderTagsJson = JsonSerializer.Serialize(email.FolderTags),
        SizeEstimate = email.SizeBytes,
        ReceivedDate = email.ReceivedDate,
        ArchivedAt = DateTime.UtcNow,
        Snippet = email.Snippet,
        SourceFolder = DetermineCanonicalFolder(email.FolderTags),
        UserCorrected = false
    };
    
    var result = await _storage.StoreArchiveAsync(archive);
    
    if (!result.IsSuccess)
    {
        // Handle quota exceeded gracefully
        if (result.Error is QuotaExceededError)
        {
            _logger.LogWarning("Storage quota exceeded, skipping archive for {EmailId}", email.Id);
            // Feature vector still stored - archive is optional
            return Result.Success(false);
        }
        
        return Result.Failure(result.Error);
    }
    
    return Result.Success(true);
}

private string DetermineCanonicalFolder(List<string> tags)
{
    if (tags.Contains("INBOX")) return "inbox";
    if (tags.Contains("TRASH") || tags.Contains("[Gmail]/Trash")) return "trash";
    if (tags.Contains("SPAM") || tags.Contains("[Gmail]/Spam")) return "spam";
    if (tags.Contains("SENT") || tags.Contains("[Gmail]/Sent Mail")) return "sent";
    return "archive";
}
```

### 4. Retrieving Features for Training

```csharp
public async Task<Result<TrainingDataset>> PrepareTrainingDataAsync()
{
    // Get all features with current schema version
    var featuresResult = await _storage.GetAllFeaturesAsync(
        schemaVersion: FeatureSchema.CurrentVersion);
    
    if (!featuresResult.IsSuccess)
    {
        return Result.Failure<TrainingDataset>(featuresResult.Error);
    }
    
    var features = featuresResult.Value;
    
    // Convert to ML.NET training data
    var dataset = new TrainingDataset
    {
        Features = features.ToList(),
        Count = features.Count()
    };
    
    _logger.LogInformation("Prepared {Count} feature vectors for training", dataset.Count);
    return Result.Success(dataset);
}
```

### 5. Monitoring Storage Usage

```csharp
public async Task<Result<bool>> MonitorStorageAsync()
{
    var usageResult = await _storage.GetStorageUsageAsync();
    
    if (!usageResult.IsSuccess)
    {
        return Result.Failure(usageResult.Error);
    }
    
    var usage = usageResult.Value;
    
    _logger.LogInformation(
        "Storage: {Current}GB / {Limit}GB ({Percent:F1}%)",
        usage.CurrentBytes / 1_073_741_824.0,
        usage.LimitBytes / 1_073_741_824.0,
        usage.UsagePercent);
    
    _logger.LogInformation(
        "Features: {FeatureCount} ({FeatureGB:F2}GB), Archives: {ArchiveCount} ({ArchiveGB:F2}GB)",
        usage.FeatureCount,
        usage.FeatureBytes / 1_073_741_824.0,
        usage.ArchiveCount,
        usage.ArchiveBytes / 1_073_741_824.0);
    
    // Check if cleanup needed
    var shouldCleanupResult = await _storage.ShouldTriggerCleanupAsync();
    
    if (shouldCleanupResult.IsSuccess && shouldCleanupResult.Value)
    {
        _logger.LogWarning("Storage usage at {Percent:F1}% - triggering cleanup", 
            usage.UsagePercent);
        await TriggerCleanupAsync();
    }
    
    return Result.Success(true);
}
```

### 6. Executing Automatic Cleanup

```csharp
public async Task<Result<int>> TriggerCleanupAsync()
{
    _logger.LogInformation("Starting automatic storage cleanup...");
    
    // Execute cleanup (removes oldest full email archives, preserves features)
    var cleanupResult = await _storage.ExecuteCleanupAsync(targetPercent: 80);
    
    if (!cleanupResult.IsSuccess)
    {
        _logger.LogError("Cleanup failed: {Error}", cleanupResult.Error.Message);
        return Result.Failure(cleanupResult.Error);
    }
    
    var deletedCount = cleanupResult.Value;
    _logger.LogInformation("Cleanup complete: deleted {Count} email archives", deletedCount);
    
    // Optionally run VACUUM to reclaim space
    if (deletedCount > 1000)
    {
        _logger.LogInformation("Running VACUUM to reclaim space...");
        var vacuumResult = await _storage.VacuumDatabaseAsync();
        
        if (!vacuumResult.IsSuccess)
        {
            _logger.LogWarning("VACUUM failed: {Error}", vacuumResult.Error.Message);
        }
        else
        {
            _logger.LogInformation("VACUUM completed successfully");
        }
    }
    
    // Log new usage
    var usageResult = await _storage.GetStorageUsageAsync();
    if (usageResult.IsSuccess)
    {
        _logger.LogInformation("New storage usage: {Percent:F1}%", 
            usageResult.Value.UsagePercent);
    }
    
    return Result.Success(deletedCount);
}
```

## Configuration

### appsettings.json

```json
{
  "Storage": {
    "StorageLimitGb": 50,
    "CleanupTriggerPercent": 90,
    "CleanupTargetPercent": 80,
    "PreserveUserCorrectedEmails": true
  }
}
```

### Registering in DI Container

```csharp
// Program.cs or Startup.cs
services.AddOptions<StorageConfig>()
    .BindConfiguration("Storage")
    .ValidateDataAnnotations()
    .ValidateOnStart();

// Register EmailArchiveService with SQLite database connection
services.AddSingleton<IEmailArchiveService>(sp => 
{
    var connection = sp.GetRequiredService<SqliteConnection>();
    return new EmailArchiveService(connection);
});
```

## Common Patterns

### Pattern 1: Store Both Feature and Archive

```csharp
public async Task<Result<bool>> ProcessAndStoreEmailAsync(Email email)
{
    // Always store feature vector (required for ML)
    var feature = ExtractFeatures(email);
    var featureResult = await _storage.StoreFeatureAsync(feature);
    
    if (!featureResult.IsSuccess)
    {
        return Result.Failure(featureResult.Error);
    }
    
    // Optionally store full email archive (capacity permitting)
    var archive = CreateArchiveEntry(email);
    var archiveResult = await _storage.StoreArchiveAsync(archive);
    
    // Archive failure is non-fatal (quota may be exceeded)
    if (!archiveResult.IsSuccess && archiveResult.Error is not QuotaExceededError)
    {
        _logger.LogWarning("Archive storage failed for {EmailId}: {Error}", 
            email.Id, archiveResult.Error.Message);
    }
    
    return Result.Success(true);
}
```

### Pattern 2: Background Storage Monitoring

```csharp
public class StorageMonitorService : BackgroundService
{
    private readonly IEmailArchiveService _storage;
    private readonly ILogger<StorageMonitorService> _logger;
    
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            await MonitorAndCleanupAsync(stoppingToken);
            
            // Check every hour
            await Task.Delay(TimeSpan.FromHours(1), stoppingToken);
        }
    }
    
    private async Task MonitorAndCleanupAsync(CancellationToken ct)
    {
        var shouldCleanupResult = await _storage.ShouldTriggerCleanupAsync(ct);
        
        if (shouldCleanupResult.IsSuccess && shouldCleanupResult.Value)
        {
            await _storage.ExecuteCleanupAsync(targetPercent: 80, ct);
        }
    }
}
```

### Pattern 3: User Correction Priority

```csharp
public async Task<Result<bool>> MarkUserCorrectionAsync(string emailId)
{
    // Get existing feature
    var featureResult = await _storage.GetFeatureAsync(emailId);
    
    if (!featureResult.IsSuccess || featureResult.Value == null)
    {
        return Result.Failure(new ValidationError("Feature not found"));
    }
    
    var feature = featureResult.Value;
    feature.UserCorrected = true;
    
    // Update feature (re-store with UserCorrected flag)
    await _storage.StoreFeatureAsync(feature);
    
    // Also update archive if exists
    var archiveResult = await _storage.GetArchiveAsync(emailId);
    if (archiveResult.IsSuccess && archiveResult.Value != null)
    {
        var archive = archiveResult.Value;
        archive.UserCorrected = true;
        await _storage.StoreArchiveAsync(archive);
    }
    
    _logger.LogInformation("Marked {EmailId} as user-corrected (high retention priority)", emailId);
    return Result.Success(true);
}
```

## Performance Considerations

### Batch Operations

- **Always use batch methods** for bulk operations (archive scanning, reprocessing)
- Batch size: 500-1000 rows per transaction for optimal performance
- Single-row operations are fine for real-time email processing

### Storage Monitoring

- Check storage usage after batch operations
- Run background monitoring every 1 hour
- VACUUM is expensive - only run when >1000 archives deleted

### Indexing

- Indexes are created automatically on table creation
- Key indexes: ReceivedDate (cleanup), UserCorrected (priority retention), FeatureSchemaVersion (compatibility)

## Testing

### Unit Tests

```csharp
[Fact]
public async Task StoreFeature_ValidFeature_ReturnsSuccess()
{
    // Arrange
    var storage = new SqliteStorageProvider(":memory:", "test-password");
    await storage.InitAsync();
    
    var feature = new EmailFeatureVector
    {
        EmailId = "test-123",
        SenderDomain = "example.com",
        FeatureSchemaVersion = FeatureSchema.CurrentVersion,
        ExtractedAt = DateTime.UtcNow,
        // ... other required fields
    };
    
    // Act
    var result = await storage.StoreFeatureAsync(feature);
    
    // Assert
    Assert.True(result.IsSuccess);
    
    var retrieved = await storage.GetFeatureAsync("test-123");
    Assert.NotNull(retrieved.Value);
    Assert.Equal("example.com", retrieved.Value.SenderDomain);
}
```

### Integration Tests

```csharp
[Fact]
public async Task ExecuteCleanup_ExceedsQuota_DeletesOldestArchives()
{
    // Arrange: Fill database to 95% capacity
    // Act: Execute cleanup to 80% target
    // Assert: Oldest non-corrected archives deleted, features preserved
}
```

## Troubleshooting

### Issue: QuotaExceededError when storing archives

**Solution**: This is expected behavior. Feature vectors are still stored. Either:
1. Increase storage limit in appsettings.json
2. Run manual cleanup: `await _storage.ExecuteCleanupAsync()`
3. Accept that archives are optional - features are sufficient for training

### Issue: Slow batch inserts

**Solution**: 
- Verify batch size is 500-1000 rows
- Ensure operations are within a transaction
- Check database isn't locked by another process

### Issue: Storage usage not decreasing after cleanup

**Solution**: Run VACUUM to reclaim space:
```csharp
await _storage.VacuumDatabaseAsync();
```

## Next Steps

- Integrate with feature extraction pipeline (see FEATURE_ENGINEERING.md)
- Implement training workflow (see MODEL_TRAINING_PIPELINE.md)
- Add monitoring dashboard for storage metrics
- Configure automated cleanup schedule
