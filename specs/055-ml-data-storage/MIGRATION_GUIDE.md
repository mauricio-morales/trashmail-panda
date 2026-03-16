# Migration Guide: ML Data Storage System (Schema Version 5)

**Feature**: #55 — ML Data Storage  
**Schema Version**: 5  
**Date**: March 15, 2026

## Overview

This guide documents the database schema changes introduced in schema version 5 for the ML data storage system. This version adds support for email feature vector storage, complete email archives, and automated storage management.

## What's New in Schema Version 5

### New Tables

#### 1. `email_features` ✨ NEW
Stores 38-feature vectors extracted from emails for ML training.

**Primary Key**: `EmailId` (TEXT)

**Key Columns**:
- Authentication features: `SpfResult`, `DkimResult`, `DmarcResult`
- Sender features: `SenderDomain`, `SenderKnown`, `ContactStrength`
- Content features: `SubjectLength`, `LinkCount`, `ImageCount`, `HasTrackingPixel`
- Behavioral features: `ThreadMessageCount`, `SenderFrequency`, `EmailAgeDays`
- User feedback: `UserCorrected` (0/1 flag for ML training prioritization)

#### 2. `email_archive` ✨ NEW  
Stores complete email content for compliance and regeneration.

**Primary Key**: `EmailId` (TEXT)  
**Foreign Key**: `EmailId` → `email_features.EmailId` (CASCADE DELETE)

**Key Columns**:
- `HeadersJson`: Complete email headers as JSON
- `BodyText`, `BodyHtml`: Email content
- `FolderTagsJson`: Provider folder/label assignments
- `ArchivedAt`: Timestamp for cleanup ordering
- `UserCorrected`: Protected from automatic cleanup

#### 3. `storage_quota` ✨ NEW
Tracks storage usage and manages cleanup thresholds.

**Singleton Row**: `Id = 1 CHECK (Id = 1)`

**Key Columns**:
- `LimitBytes`: Configurable storage limit (default 50GB)
- `CurrentBytes`, `FeatureBytes`, `ArchiveBytes`: Usage tracking
- `UserCorrectedCount`: Count of protected emails
- `LastCleanupAt`, `LastMonitoredAt`: Maintenance timestamps

## Migration Process

### Automatic Migration

The migration runs automatically on first application start after upgrading:

```csharp
// Handled by EmailArchiveService initialization
var service = new EmailArchiveService(connection);
// Migration_001_MLStorage.Up() executes automatically
```

### Manual Migration (if needed)

```csharp
using TrashMailPanda.Providers.Storage.Migrations;

var connection = new SqliteConnection("Data Source=app.db");
await connection.OpenAsync();

var migration = new Migration_001_MLStorage();
await migration.UpAsync(connection);
```

### Verification

```sql
-- Check migration was applied
SELECT version, description, appliedAt 
FROM schema_migrations 
WHERE version = 5;

-- Expected output:
-- version | description                    | appliedAt
-- 5       | ML Storage System - Add email_ | 2026-03-15T...

-- Verify table structure
SELECT name FROM sqlite_master 
WHERE type='table' AND name LIKE 'email_%';

-- Expected: email_features, email_archive
```

## Breaking Changes

### ⚠️ None

This is an **additive migration** - no existing tables are modified. All changes are backward compatible.

## New API Endpoints

### Feature Storage

```csharp
// Store single feature
await archiveService.StoreFeatureAsync(feature);

// Batch store features
var count = await archiveService.StoreFeaturesBatchAsync(features);

// Retrieve for ML training
var allFeatures = await archiveService.GetAllFeaturesAsync();
```

### Archive Storage

```csharp
// Store complete email
await archiveService.StoreArchiveAsync(archive);

// Batch store archives
var count = await archiveService.StoreArchivesBatchAsync(archives);

// Retrieve archive
var archive = await archiveService.GetArchiveAsync(emailId);
```

### Storage Management

```csharp
// Monitor usage
var quota = await archiveService.GetStorageUsageAsync();
Console.WriteLine($"Using {quota.CurrentBytes / quota.LimitBytes:P} of storage");

// Check if cleanup needed
var shouldCleanup = await archiveService.ShouldTriggerCleanupAsync();

// Execute cleanup
var deleted = await archiveService.ExecuteCleanupAsync(targetPercent: 80);
```

## Data Retention Policy

### Feature Vectors
- **Never automatically deleted**
- Required for ML model training
- Minimal storage footprint (~2KB per email)

### Email Archives
- **Subject to automatic cleanup** when storage exceeds 90% capacity
- Cleanup deletes oldest archives first
- Preserves user-corrected emails (95% retention rate guaranteed)
- Feature vectors remain after archive deletion

### User-Corrected Emails
Special protection for training data quality:
- Marked with `UserCorrected = 1`
- Deleted **only as last resort** during cleanup
- Minimum 95% retention rate (spec.md SC-004)

## Configuration

### Dependency Injection

```csharp
// appsettings.json
{
  "Storage": {
    "ConnectionString": "Data Source=app.db",
    "MaxStorageBytes": 53687091200, // 50GB
    "CleanupTriggerPercent": 90,
    "CleanupTargetPercent": 80
  }
}

// Program.cs
services.AddSingleton<SqliteConnection>(sp => 
{
    var config = sp.GetRequiredService<IOptions<StorageConfig>>().Value;
    var connection = new SqliteConnection(config.ConnectionString);
    connection.Open();
    return connection;
});

services.AddSingleton<IEmailArchiveService>(sp =>
{
    var connection = sp.GetRequiredService<SqliteConnection>();
    return new EmailArchiveService(connection);
});
```

## Performance Characteristics

### Feature Storage
- Single insert: <100ms (spec.md PC-001)
- Batch insert: 1000 vectors in <5 seconds

### Batch Retrieval
- 1000 feature vectors: <500ms (spec.md PC-002)
- Full archive retrieval: <2 seconds for 10K emails

### Storage Monitoring
- Usage calculation: <100ms using SQLite `dbstat` virtual table
- Cleanup execution: ~1s per 1000 emails deleted

## Rollback Procedure

### To Revert Schema Version 5

```csharp
var migration = new Migration_001_MLStorage();
await migration.DownAsync(connection);
```

**WARNING**: This will **permanently delete** all ML training data:
- All feature vectors in `email_features`
- All email archives in `email_archive`  
- All storage quota metadata

**Backup first**:
```bash
cp app.db app.db.backup-$(date +%Y%m%d)
```

## Troubleshooting

### Migration Fails with "table already exists"

**Cause**: Migration ran partially in previous attempt

**Solution**:
```sql
-- Check which tables exist
SELECT name FROM sqlite_master WHERE type='table';

-- If email_features exists but migration_history doesn't:
DELETE FROM schema_migrations WHERE version = 5;

-- Re-run migration
```

### Storage Quota Shows Incorrect Usage

**Cause**: Manual database modifications outside EmailArchiveService

**Solution**:
```csharp
// Recalculate from actual database size
var usage = await archiveService.GetStorageUsageAsync();
// This triggers automatic recalculation using dbstat
```

### Cleanup Not Triggering

**Cause**: `LastCleanupAt` timestamp prevents too-frequent cleanups

**Solution**:
```sql
-- Check last cleanup time
SELECT LastCleanupAt FROM storage_quota WHERE Id = 1;

-- Force cleanup eligibility (development only)
UPDATE storage_quota SET LastCleanupAt = NULL WHERE Id = 1;
```

## Support

- **Documentation**: [`specs/055-ml-data-storage/`](.)
- **Quickstart**: [`quickstart.md`](./quickstart.md)
- **Schema Details**: [`plan.md`](./plan.md) - Database Schema section
- **GitHub Issue**: [#55](https://github.com/mauricio-morales/trashmail-panda/pull/70)

## Changelog

### Version 5 (2026-03-15)
- ✨ Added `email_features` table with 38-feature vector storage
- ✨ Added `email_archive` table for complete email content
- ✨ Added `storage_quota` table for usage monitoring
- ✨ Implemented automatic cleanup with user correction preservation
- ✨ Added foreign key cascade from email_archive → email_features

---

**Next Steps**: See [quickstart.md](./quickstart.md) for code examples.
