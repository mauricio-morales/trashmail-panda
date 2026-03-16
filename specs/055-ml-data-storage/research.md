# Research: ML Data Storage System

**Feature**: #55 — ML Data Storage  
**Date**: 2026-03-14  
**Status**: Complete

## Research Questions

This document consolidates research findings for key technical decisions in the ML data storage implementation.

---

## R1: Schema Migration Strategy

**Question**: Should we continue using the existing `CREATE TABLE IF NOT EXISTS` pattern or implement a formal versioned migration system?

**Decision**: **Continue with existing pattern, add schema version tracking**

**Rationale**:
- Current `SqliteStorageProvider.CreateSchemaAsync()` uses `CREATE TABLE IF NOT EXISTS` which is simple and works for additive changes
- For this feature, we're adding 4 new tables (`email_features`, `email_archive`, `storage_quota`, `feature_schema_versions`) which are purely additive
- Adding a `schema_version` table to track current database version allows future migrations while maintaining simplicity
- Breaking schema changes (column renames, type changes) can be handled via ALTER TABLE with version checks
- The CLAUDE.md already states "schema changes MUST go through the migration system" which we interpret as version-tracked changes within CreateSchemaAsync

**Implementation**:
```csharp
// In CreateSchemaAsync(), add:
CREATE TABLE IF NOT EXISTS schema_version (
    version INTEGER PRIMARY KEY,
    applied_at TEXT NOT NULL,
    description TEXT NOT NULL
);

// After creating new tables, record the version:
INSERT OR IGNORE INTO schema_version (version, applied_at, description) 
VALUES (5, datetime('now'), 'Add ML storage tables');
```

**Alternatives Considered**:
- **Formal migration framework** (e.g., FluentMigrator, DbUp): Rejected because it adds significant complexity and external dependencies for a single-database desktop application. Overkill for our needs.
- **Entity Framework Core Migrations**: Rejected because project doesn't use EF Core; uses raw ADO.NET for full control over encryption and performance.

---

## R2: Storage Monitoring Implementation

**Question**: How should we efficiently track storage usage and trigger cleanup when approaching the configured limit?

**Decision**: **Combine database PRAGMA page_count with file system monitoring**

**Rationale**:
- SQLite provides `PRAGMA page_count` and `PRAGMA page_size` to calculate database size without file I/O
- Database size = page_count × page_size (accurate to the byte)
- For breakdown by table, use `SELECT SUM(pgsize) FROM dbstat WHERE name = 'table_name'` (requires SQLITE_ENABLE_DBSTAT_VTAB, which is enabled in standard builds)
- File system monitoring (FileInfo.Length) is adequate for overall size checks
- Check storage on: (1) after each batch insert, (2) scheduled background task (hourly), (3) on app startup

**Implementation**:
```csharp
public async Task<Result<StorageUsage>> GetStorageUsageAsync()
{
    var pageCount = await ExecuteScalarAsync<long>("PRAGMA page_count");
    var pageSize = await ExecuteScalarAsync<long>("PRAGMA page_size");
    var totalBytes = pageCount * pageSize;
    
    var featuresSize = await ExecuteScalarAsync<long>(
        "SELECT SUM(pgsize) FROM dbstat WHERE name = 'email_features'");
    var archiveSize = await ExecuteScalarAsync<long>(
        "SELECT SUM(pgsize) FROM dbstat WHERE name = 'email_archive'");
    
    return Result.Success(new StorageUsage {
        TotalBytes = totalBytes,
        FeatureBytes = featuresSize,
        ArchiveBytes = archiveSize,
        ...
    });
}
```

**Alternatives Considered**:
- **File system only**: Rejected because it doesn't provide table-level breakdown needed for intelligent cleanup
- **Counting rows × average size**: Rejected because it's inaccurate and doesn't account for indexes, BLOB storage overhead, or fragmentation

---

## R3: Automatic Cleanup Strategy

**Question**: What is the most effective strategy for reclaiming storage when approaching the configured limit?

**Decision**: **Multi-phase cleanup: (1) DELETE oldest full email archives, (2) VACUUM to reclaim space**

**Rationale**:
- DELETE operations mark rows for deletion but don't immediately reclaim space in SQLite
- VACUUM rebuilds the database file and reclaims freed space, but is expensive (requires exclusive lock, temporary disk space = 2× database size)
- Two-phase approach: DELETE oldest emails first (fast), then VACUUM if still above threshold (slow but effective)
- Cleanup order: (1) non-user-corrected emails oldest-first, (2) user-corrected emails only if still over limit
- Feature vectors are NEVER deleted during cleanup - only full email archives

**Implementation**:
```csharp
public async Task<Result<bool>> ExecuteCleanupAsync(long targetBytes)
{
    // Phase 1: Delete oldest non-corrected email archives
    await ExecuteNonQueryAsync(@"
        DELETE FROM email_archive
        WHERE EmailId IN (
            SELECT ea.EmailId 
            FROM email_archive ea
            LEFT JOIN classification_history ch 
                ON ea.EmailId = ch.email_id AND ch.user_action IS NOT NULL
            WHERE ch.email_id IS NULL  -- Not user-corrected
            ORDER BY ea.ReceivedDate ASC
            LIMIT 1000
        )
    ");
    
    // Check if target reached
    var usage = await GetStorageUsageAsync();
    if (usage.Value.TotalBytes <= targetBytes) 
        return Result.Success(true);
    
    // Phase 2: VACUUM to reclaim space
    await ExecuteNonQueryAsync("VACUUM");
    
    return Result.Success(true);
}
```

**Alternatives Considered**:
- **VACUUM after every DELETE**: Rejected because VACUUM is too expensive to run frequently
- **Auto-vacuum mode**: Rejected because it adds overhead to every transaction and doesn't reclaim as much space as full VACUUM
- **Delete features along with archives**: Rejected because it violates requirement FR-008 (preserve features after email deletion)

---

## R4: BLOB Storage Patterns for Email Content

**Question**: What is the best practice for storing potentially large email bodies (HTML/plaintext) as BLOBs in SQLite with encryption?

**Decision**: **Store as TEXT columns with SQLCipher transparent encryption**

**Rationale**:
- SQLCipher encrypts all database content transparently at the page level
- TEXT columns are appropriate for email bodies since they're character data (not binary)
- SQLite handles TEXT columns efficiently even for large content (up to 1GB per field)
- Storing as TEXT allows potential full-text search (FTS5) in future phases
- Compression is not needed - SQLCipher overhead is minimal, and emails compress poorly (already mostly text)

**Implementation**:
```sql
CREATE TABLE IF NOT EXISTS email_archive (
    EmailId TEXT PRIMARY KEY,
    BodyText TEXT,  -- Plain text body (can be NULL)
    BodyHtml TEXT,  -- HTML body (can be NULL)
    ...
);
```

**Alternatives Considered**:
- **BLOB with manual compression**: Rejected because it adds complexity, and gzip/deflate compression on email text provides minimal benefit (~20-30% for plain text, less for HTML)
- **External file storage**: Rejected because it complicates encryption, backup, and atomicity guarantees that SQLite provides
- **Separate BLOB table**: Rejected because it adds query complexity without measurable performance benefit for our scale (50GB database is well within SQLite's capabilities)

---

## R5: Batch Insert Performance Optimization

**Question**: How should we optimize bulk inserts of feature vectors when processing thousands of archived emails?

**Decision**: **Use parameterized batch inserts within a single transaction**

**Rationale**:
- SQLite is fastest when multiple INSERTs occur within a single transaction (BEGIN...COMMIT)
- Parameterized commands prevent SQL injection and allow prepared statement reuse
- Batch size of 500-1000 rows per transaction balances performance with transaction log size
- For very large batches (>10K rows), split into multiple transactions to avoid excessive memory usage

**Implementation**:
```csharp
public async Task<Result<bool>> StoreFeaturesAsync(
    IEnumerable<EmailFeatureVector> features)
{
    const int batchSize = 500;
    var batches = features.Chunk(batchSize);
    
    foreach (var batch in batches)
    {
        await _connectionLock.WaitAsync();
        try
        {
            using var transaction = _connection.BeginTransaction();
            
            foreach (var feature in batch)
            {
                await InsertFeatureAsync(feature, transaction);
            }
            
            await transaction.CommitAsync();
        }
        finally
        {
            _connectionLock.Release();
        }
    }
    
    return Result.Success(true);
}
```

**Alternatives Considered**:
- **Individual transactions per row**: Rejected because it's 10-100× slower than batched transactions
- **Single transaction for all rows**: Rejected because very large transactions (>10K rows) can cause memory pressure and long lock times
- **Bulk copy APIs**: Rejected because ADO.NET for SQLite doesn't provide SqlBulkCopy equivalent

---

## R6: Index Strategy for Query Performance

**Question**: Which indexes are needed for efficient querying of archives and features during training and cleanup?

**Decision**: **Index on: (1) ReceivedDate for cleanup, (2) EmailId for joins, (3) composite index on (UserCorrected, ReceivedDate) for priority cleanup**

**Rationale**:
- Cleanup queries sort by `ReceivedDate ASC` to find oldest emails → index on ReceivedDate
- Joins between `email_features` and `email_archive` on `EmailId` → EmailId is already PK (automatic index)
- User correction priority requires filtering user-corrected emails → composite index enables efficient "oldest non-corrected" queries
- Training queries fetch all features at once (no filtering) → no separate index needed

**Implementation**:
```sql
-- email_archive indexes
CREATE INDEX IF NOT EXISTS idx_archive_received_date 
    ON email_archive(ReceivedDate);

CREATE INDEX IF NOT EXISTS idx_archive_user_corrected_date 
    ON email_archive(UserCorrected, ReceivedDate);

-- email_features indexes  
CREATE INDEX IF NOT EXISTS idx_features_extracted_at 
    ON email_features(ExtractedAt);

CREATE INDEX IF NOT EXISTS idx_features_schema_version 
    ON email_features(FeatureSchemaVersion);
```

**Alternatives Considered**:
- **Index on every filterable column**: Rejected because it slows down writes and increases database size; only index columns used in WHERE/ORDER BY clauses
- **Full-text search indexes (FTS5)**: Deferred to Phase 2 - not needed for initial ML training which uses TF-IDF features computed in memory
- **Covering indexes**: Rejected because our queries typically need full rows, so covering indexes don't provide significant benefit

---

## R7: Storage Configuration Pattern

**Question**: How should storage limits and retention policies be configured?

**Decision**: **Use appsettings.json with IOptions<T> pattern for configuration**

**Rationale**:
- Existing TrashMail Panda architecture uses `appsettings.json` for configuration
- `IOptions<StorageConfig>` pattern provides compile-time type safety and validation
- DataAnnotations validation ensures invalid configs are caught at startup
- Allows environment-specific overrides (appsettings.Development.json)

**Implementation**:
```csharp
// StorageConfig.cs
public class StorageConfig
{
    [Range(1, 1000)]
    public int StorageLimitGb { get; set; } = 50;
    
    [Range(50, 99)]
    public int CleanupTriggerPercent { get; set; } = 90;
    
    [Range(40, 90)]
    public int CleanupTargetPercent { get; set; } = 80;
    
    public bool PreserveUserCorrectedEmails { get; set; } = true;
}

// appsettings.json
{
  "Storage": {
    "StorageLimitGb": 50,
    "CleanupTriggerPercent": 90,
    "CleanupTargetPercent": 80,
    "PreserveUserCorrectedEmails": true
  }
}

// Registration in DI
services.AddOptions<StorageConfig>()
    .BindConfiguration("Storage")
    .ValidateDataAnnotations()
    .ValidateOnStart();
```

**Alternatives Considered**:
- **Hardcoded constants**: Rejected because different deployment environments need different limits
- **Database configuration**: Rejected because configuration should be declarative and version-controlled, not in user data
- **Environment variables only**: Rejected because structured JSON is more maintainable than flat env vars

---

## Summary

All research questions have been resolved:
1. ✅ Schema migrations: Version-tracked changes within CreateSchemaAsync
2. ✅ Storage monitoring: PRAGMA page_count + dbstat for table breakdown
3. ✅ Cleanup strategy: DELETE oldest archives + VACUUM
4. ✅ BLOB storage: TEXT columns with transparent SQLCipher encryption
5. ✅ Batch inserts: Parameterized batches within transactions (500-1000 rows)
6. ✅ Indexes: ReceivedDate, composite (UserCorrected, ReceivedDate), FeatureSchemaVersion
7. ✅ Configuration: appsettings.json with IOptions<StorageConfig> pattern

Ready to proceed to Phase 1: Design.
