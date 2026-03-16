using Microsoft.EntityFrameworkCore;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using TrashMailPanda.Shared.Base;
using TrashMailPanda.Providers.Storage.Models;

namespace TrashMailPanda.Providers.Storage;

/// <summary>
/// Entity Framework Core implementation of IEmailArchiveService.
/// Provides ML training data storage with encrypted SQLCipher database.
/// </summary>
public class EmailArchiveService : IEmailArchiveService, IDisposable
{
    private readonly TrashMailPandaDbContext _context;
    private readonly SemaphoreSlim _connectionLock;
    private readonly bool _ownsLock;

    public EmailArchiveService(TrashMailPandaDbContext context, SemaphoreSlim connectionLock)
    {
        _context = context ?? throw new ArgumentNullException(nameof(context));
        _connectionLock = connectionLock ?? throw new ArgumentNullException(nameof(connectionLock));
        _ownsLock = false;
    }

    /// <summary>
    /// Constructor for testing - creates its own connection lock.
    /// </summary>
    public EmailArchiveService(TrashMailPandaDbContext context)
    {
        _context = context ?? throw new ArgumentNullException(nameof(context));
        _connectionLock = new SemaphoreSlim(1, 1);
        _ownsLock = true;
    }

    public void Dispose()
    {
        if (_ownsLock)
        {
            _connectionLock?.Dispose();
        }
    }

    // ============================================================
    // Feature Vector Storage
    // ============================================================

    public async Task<Result<bool>> StoreFeatureAsync(
        EmailFeatureVector feature,
        CancellationToken cancellationToken = default)
    {
        if (feature == null)
            return Result<bool>.Failure(new ValidationError("Feature vector cannot be null"));

        if (string.IsNullOrWhiteSpace(feature.EmailId))
            return Result<bool>.Failure(new ValidationError("EmailId is required"));

        try
        {
            await _connectionLock.WaitAsync(cancellationToken);
            try
            {
                var existing = await _context.EmailFeatures.FindAsync(new object[] { feature.EmailId }, cancellationToken);
                if (existing != null)
                {
                    // Update existing
                    _context.Entry(existing).CurrentValues.SetValues(feature);
                }
                else
                {
                    // Insert new
                    await _context.EmailFeatures.AddAsync(feature, cancellationToken);
                }

                await _context.SaveChangesAsync(cancellationToken);
                return Result<bool>.Success(true);
            }
            finally
            {
                _connectionLock.Release();
            }
        }
        catch (Exception ex)
        {
            return Result<bool>.Failure(new StorageError($"Failed to store feature for {feature.EmailId}", ex.Message, ex));
        }
    }

    public async Task<Result<int>> StoreFeaturesBatchAsync(
        IEnumerable<EmailFeatureVector> features,
        CancellationToken cancellationToken = default)
    {
        if (features == null)
            return Result<int>.Failure(new ValidationError("Features collection cannot be null"));

        var featureList = features.ToList();
        if (featureList.Count == 0)
            return Result<int>.Success(0);

        // Validate all features before starting transaction
        foreach (var feature in featureList)
        {
            if (string.IsNullOrWhiteSpace(feature.EmailId))
                return Result<int>.Failure(new ValidationError("All features must have valid EmailId"));
        }

        try
        {
            await _connectionLock.WaitAsync(cancellationToken);
            try
            {
                // Process in batches of 500 rows per research.md R5
                const int batchSize = 500;
                int totalStored = 0;

                for (int i = 0; i < featureList.Count; i += batchSize)
                {
                    var batch = featureList.Skip(i).Take(batchSize).ToList();

                    using var transaction = await _context.Database.BeginTransactionAsync(cancellationToken);
                    try
                    {
                        foreach (var feature in batch)
                        {
                            var existing = await _context.EmailFeatures.FindAsync(new object[] { feature.EmailId }, cancellationToken);
                            if (existing != null)
                            {
                                _context.Entry(existing).CurrentValues.SetValues(feature);
                            }
                            else
                            {
                                await _context.EmailFeatures.AddAsync(feature, cancellationToken);
                            }
                            totalStored++;
                        }

                        await _context.SaveChangesAsync(cancellationToken);
                        await transaction.CommitAsync(cancellationToken);
                    }
                    catch (Exception ex)
                    {
                        await transaction.RollbackAsync(cancellationToken);
                        return Result<int>.Failure(new StorageError(
                            $"Transaction failed during batch storage: {ex.Message}"));
                    }
                }

                return Result<int>.Success(totalStored);
            }
            finally
            {
                _connectionLock.Release();
            }
        }
        catch (Exception ex)
        {
            return Result<int>.Failure(new StorageError($"Failed to store feature batch", ex.Message, ex));
        }
        finally
        {
            // T046: Add monitoring hook after successful batch operation (outside lock)
            // Update storage usage statistics (best-effort, don't fail batch on monitoring error)
            _ = await GetStorageUsageAsync();
        }
    }

    public async Task<Result<EmailFeatureVector?>> GetFeatureAsync(
        string emailId,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(emailId))
            return Result<EmailFeatureVector?>.Failure(new ValidationError("EmailId is required"));

        try
        {
            await _connectionLock.WaitAsync(cancellationToken);
            try
            {
                var feature = await _context.EmailFeatures.FindAsync(new object[] { emailId }, cancellationToken);
                return Result<EmailFeatureVector?>.Success(feature);
            }
            finally
            {
                _connectionLock.Release();
            }
        }
        catch (Exception ex)
        {
            return Result<EmailFeatureVector?>.Failure(new StorageError($"Failed to retrieve feature for {emailId}", ex.Message, ex));
        }
    }

    public async Task<Result<IEnumerable<EmailFeatureVector>>> GetAllFeaturesAsync(
        int? schemaVersion = null,
        CancellationToken cancellationToken = default)
    {
        try
        {
            await _connectionLock.WaitAsync(cancellationToken);
            try
            {
                var query = _context.EmailFeatures.AsQueryable();

                if (schemaVersion.HasValue)
                    query = query.Where(f => f.FeatureSchemaVersion == schemaVersion.Value);

                var features = await query.ToListAsync(cancellationToken);
                return Result<IEnumerable<EmailFeatureVector>>.Success(features);
            }
            finally
            {
                _connectionLock.Release();
            }
        }
        catch (Exception ex)
        {
            return Result<IEnumerable<EmailFeatureVector>>.Failure(new StorageError("Failed to retrieve features", ex.Message, ex));
        }
    }

    // ============================================================
    // Email Archive Storage
    // ============================================================

    public async Task<Result<bool>> StoreArchiveAsync(
        EmailArchiveEntry archive,
        CancellationToken cancellationToken = default)
    {
        if (archive == null)
            return Result<bool>.Failure(new ValidationError("Archive entry cannot be null"));

        if (string.IsNullOrWhiteSpace(archive.EmailId))
            return Result<bool>.Failure(new ValidationError("EmailId is required"));

        // Validate at least one body field is provided
        if (string.IsNullOrWhiteSpace(archive.BodyText) && string.IsNullOrWhiteSpace(archive.BodyHtml))
            return Result<bool>.Failure(new ValidationError("At least one of BodyText or BodyHtml must be provided"));

        try
        {
            await _connectionLock.WaitAsync(cancellationToken);
            try
            {
                var existing = await _context.EmailArchives.FindAsync(new object[] { archive.EmailId }, cancellationToken);
                if (existing != null)
                {
                    _context.Entry(existing).CurrentValues.SetValues(archive);
                }
                else
                {
                    await _context.EmailArchives.AddAsync(archive, cancellationToken);
                }

                await _context.SaveChangesAsync(cancellationToken);
                return Result<bool>.Success(true);
            }
            finally
            {
                _connectionLock.Release();
            }
        }
        catch (Exception ex)
        {
            return Result<bool>.Failure(new StorageError($"Failed to store archive for {archive.EmailId}", ex.Message, ex));
        }
    }

    public async Task<Result<int>> StoreArchivesBatchAsync(
        IEnumerable<EmailArchiveEntry> archives,
        CancellationToken cancellationToken = default)
    {
        if (archives == null)
            return Result<int>.Failure(new ValidationError("Archives collection cannot be null"));

        var archiveList = archives.ToList();
        if (archiveList.Count == 0)
            return Result<int>.Success(0);

        // Validate all archives before starting transaction
        foreach (var archive in archiveList)
        {
            if (string.IsNullOrWhiteSpace(archive.EmailId))
                return Result<int>.Failure(new ValidationError("All archives must have valid EmailId"));

            if (string.IsNullOrWhiteSpace(archive.BodyText) && string.IsNullOrWhiteSpace(archive.BodyHtml))
                return Result<int>.Failure(new ValidationError($"Archive {archive.EmailId} must have at least one of BodyText or BodyHtml"));
        }

        try
        {
            await _connectionLock.WaitAsync(cancellationToken);
            try
            {
                var totalStored = 0;
                const int batchSize = 500; // Per research.md R5

                // Process in batches
                for (int i = 0; i < archiveList.Count; i += batchSize)
                {
                    var batch = archiveList.Skip(i).Take(batchSize).ToList();

                    using var transaction = await _context.Database.BeginTransactionAsync(cancellationToken);
                    try
                    {
                        foreach (var archive in batch)
                        {
                            var existing = await _context.EmailArchives.FindAsync(new object[] { archive.EmailId }, cancellationToken);
                            if (existing != null)
                            {
                                _context.Entry(existing).CurrentValues.SetValues(archive);
                            }
                            else
                            {
                                await _context.EmailArchives.AddAsync(archive, cancellationToken);
                            }
                            totalStored++;
                        }

                        await _context.SaveChangesAsync(cancellationToken);
                        await transaction.CommitAsync(cancellationToken);
                    }
                    catch (Exception ex)
                    {
                        await transaction.RollbackAsync(cancellationToken);
                        return Result<int>.Failure(new StorageError("Batch archive storage failed", ex.Message, ex));
                    }
                }

                return Result<int>.Success(totalStored);
            }
            finally
            {
                _connectionLock.Release();
            }
        }
        catch (Exception ex)
        {
            return Result<int>.Failure(new StorageError("Failed to acquire lock for batch archive storage", ex.Message, ex));
        }
        finally
        {
            // T046: Add monitoring hook after successful batch operation (outside lock)
            // Update storage usage statistics (best-effort, don't fail batch on monitoring error)
            _ = await GetStorageUsageAsync();
        }
    }

    public async Task<Result<EmailArchiveEntry?>> GetArchiveAsync(
        string emailId,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(emailId))
            return Result<EmailArchiveEntry?>.Failure(new ValidationError("EmailId is required"));

        try
        {
            await _connectionLock.WaitAsync(cancellationToken);
            try
            {
                var archive = await _context.EmailArchives.FindAsync(new object[] { emailId }, cancellationToken);
                return Result<EmailArchiveEntry?>.Success(archive);
            }
            finally
            {
                _connectionLock.Release();
            }
        }
        catch (Exception ex)
        {
            return Result<EmailArchiveEntry?>.Failure(new StorageError($"Failed to retrieve archive for {emailId}", ex.Message, ex));
        }
    }

    public async Task<Result<bool>> DeleteArchiveAsync(
        string emailId,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(emailId))
            return Result<bool>.Failure(new ValidationError("EmailId is required"));

        try
        {
            await _connectionLock.WaitAsync(cancellationToken);
            try
            {
                var archive = await _context.EmailArchives.FindAsync(new object[] { emailId }, cancellationToken);
                if (archive != null)
                {
                    _context.EmailArchives.Remove(archive);
                    await _context.SaveChangesAsync(cancellationToken);
                    return Result<bool>.Success(true);
                }
                return Result<bool>.Success(false);
            }
            finally
            {
                _connectionLock.Release();
            }
        }
        catch (Exception ex)
        {
            return Result<bool>.Failure(new StorageError($"Failed to delete archive for {emailId}", ex.Message, ex));
        }
    }

    // ============================================================
    // Storage Monitoring & Quota
    // ============================================================

    public async Task<Result<StorageQuota>> GetStorageUsageAsync(
        CancellationToken cancellationToken = default)
    {
        try
        {
            await _connectionLock.WaitAsync(cancellationToken);
            try
            {
                // Step 1: Get or create StorageQuota record
                var quota = await _context.StorageQuotas.FindAsync(new object[] { 1 }, cancellationToken);

                // If no quota record exists, create default 50GB limit
                if (quota == null)
                {
                    quota = new StorageQuota
                    {
                        Id = 1,
                        LimitBytes = StorageQuota.DefaultLimitBytes,
                        CurrentBytes = 0,
                        FeatureBytes = 0,
                        ArchiveBytes = 0,
                        FeatureCount = 0,
                        ArchiveCount = 0,
                        UserCorrectedCount = 0,
                        LastCleanupAt = null,
                        LastMonitoredAt = DateTime.UtcNow
                    };
                    await _context.StorageQuotas.AddAsync(quota, cancellationToken);
                    await _context.SaveChangesAsync(cancellationToken);
                    
                    // Re-fetch to ensure we have the tracked entity
                    quota = await _context.StorageQuotas.FindAsync(new object[] { 1 }, cancellationToken);
                }

                // Step 2: Calculate actual storage usage using PRAGMA page_count
                long pageCount = 0;
                long pageSize = 4096; // Default

                var connection = _context.Database.GetDbConnection();
                var wasOpen = connection.State == System.Data.ConnectionState.Open;
                
                if (!wasOpen)
                    await connection.OpenAsync(cancellationToken);

                try
                {
                    using (var command = connection.CreateCommand())
                    {
                        command.CommandText = "PRAGMA page_count";
                        var pcResult = await command.ExecuteScalarAsync(cancellationToken);
                        if (pcResult != null)
                            pageCount = Convert.ToInt64(pcResult);

                        command.CommandText = "PRAGMA page_size";
                        var psResult = await command.ExecuteScalarAsync(cancellationToken);
                        if (psResult != null)
                            pageSize = Convert.ToInt64(psResult);
                    }

                    var totalDatabaseBytes = pageCount * pageSize;

                    // Step 3: Get per-table sizes using dbstat (if available)
                    long featureBytes = 0;
                    long archiveBytes = 0;

                    try
                    {
                        using (var command = connection.CreateCommand())
                        {
                            command.CommandText = "SELECT name, SUM(pgsize) FROM dbstat WHERE name IN ('email_features', 'email_archive') GROUP BY name";
                            using (var reader = await command.ExecuteReaderAsync(cancellationToken))
                            {
                                while (await reader.ReadAsync(cancellationToken))
                                {
                                    var tableName = reader.GetString(0);
                                    var tableSize = reader.GetInt64(1);

                                    if (tableName == "email_features")
                                        featureBytes = tableSize;
                                    else if (tableName == "email_archive")
                                        archiveBytes = tableSize;
                                }
                            }
                        }
                    }
                    catch
                    {
                        // dbstat might not be available, use estimates based on counts
                        featureBytes = quota.FeatureBytes;
                        archiveBytes = quota.ArchiveBytes;
                    }

                    // Step 4: Get record counts using LINQ
                    var featureCount = await _context.EmailFeatures.CountAsync(cancellationToken);
                    var archiveCount = await _context.EmailArchives.CountAsync(cancellationToken);
                    var userCorrectedCount = await _context.EmailArchives.CountAsync(a => a.UserCorrected == 1, cancellationToken);

                    // Step 5: Update quota record with fresh data
                    quota!.CurrentBytes = totalDatabaseBytes;
                    quota.FeatureBytes = featureBytes;
                    quota.ArchiveBytes = archiveBytes;
                    quota.FeatureCount = featureCount;
                    quota.ArchiveCount = archiveCount;
                    quota.UserCorrectedCount = userCorrectedCount;
                    quota.LastMonitoredAt = DateTime.UtcNow;

                    await _context.SaveChangesAsync(cancellationToken);

                    return Result<StorageQuota>.Success(quota);
                }
                finally
                {
                    // Don't close the connection as EF Core manages it
                }
            }
            finally
            {
                _connectionLock.Release();
            }
        }
        catch (Exception ex)
        {
            return Result<StorageQuota>.Failure(new StorageError("Failed to get storage usage", ex.Message, ex));
        }
    }

    public async Task<Result<bool>> UpdateStorageLimitAsync(
        long limitBytes,
        CancellationToken cancellationToken = default)
    {
        if (limitBytes <= 0)
            return Result<bool>.Failure(new ValidationError("Storage limit must be greater than zero"));

        try
        {
            await _connectionLock.WaitAsync(cancellationToken);
            try
            {
                var quota = await _context.StorageQuotas.FindAsync(new object[] { 1 }, cancellationToken);

                if (quota == null)
                {
                    // Create default quota record first
                    quota = new StorageQuota
                    {
                        Id = 1,
                        LimitBytes = limitBytes,
                        CurrentBytes = 0,
                        FeatureBytes = 0,
                        ArchiveBytes = 0,
                        FeatureCount = 0,
                        ArchiveCount = 0,
                        UserCorrectedCount = 0,
                        LastMonitoredAt = DateTime.UtcNow
                    };
                    await _context.StorageQuotas.AddAsync(quota, cancellationToken);
                }
                else
                {
                    quota.LimitBytes = limitBytes;
                }

                await _context.SaveChangesAsync(cancellationToken);
                return Result<bool>.Success(true);
            }
            finally
            {
                _connectionLock.Release();
            }
        }
        catch (Exception ex)
        {
            return Result<bool>.Failure(new StorageError("Failed to update storage limit", ex.Message, ex));
        }
    }

    public async Task<Result<bool>> ShouldTriggerCleanupAsync(
        CancellationToken cancellationToken = default)
    {
        try
        {
            // Get current storage usage
            var usageResult = await GetStorageUsageAsync(cancellationToken);
            if (!usageResult.IsSuccess)
                return Result<bool>.Failure(usageResult.Error);

            var quota = usageResult.Value!;

            // Calculate usage percentage
            if (quota.LimitBytes == 0)
                return Result<bool>.Success(false);

            var usagePercent = (double)quota.CurrentBytes / quota.LimitBytes * 100.0;

            // Trigger cleanup if usage >= 90% (per spec.md FR-005)
            const double cleanupThreshold = 90.0;
            return Result<bool>.Success(usagePercent >= cleanupThreshold);
        }
        catch (Exception ex)
        {
            return Result<bool>.Failure(new StorageError("Failed to check cleanup trigger", ex.Message, ex));
        }
    }

    // ============================================================
    // Automatic Cleanup
    // ============================================================

    public async Task<Result<int>> ExecuteCleanupAsync(
        int targetPercent = 80,
        CancellationToken cancellationToken = default)
    {
        if (targetPercent <= 0 || targetPercent > 100)
            return Result<int>.Failure(new ValidationError("Target percent must be between 1 and 100"));

        try
        {
            // Step 1: Get current storage usage (outside the lock)
            var usageResult = await GetStorageUsageAsync(cancellationToken);
            if (!usageResult.IsSuccess)
                return Result<int>.Failure(usageResult.Error);

            var quota = usageResult.Value!;
            var targetBytes = (long)(quota.LimitBytes * (targetPercent / 100.0));

            // Update LastCleanupAt timestamp regardless of whether cleanup is needed
            await _connectionLock.WaitAsync(cancellationToken);
            try
            {
                var quotaRecord = await _context.StorageQuotas.FindAsync(new object[] { 1 }, cancellationToken);
                if (quotaRecord != null)
                {
                    quotaRecord.LastCleanupAt = DateTime.UtcNow;
                    await _context.SaveChangesAsync(cancellationToken);
                }
            }
            finally
            {
                _connectionLock.Release();
            }

            // Determine if cleanup is needed and calculate archives to delete
            int archivesToDelete;
            
            // For in-memory databases or unreliable byte data, use count-based cleanup
            // Use count-based if CurrentBytes OR ArchiveBytes is 0 (unreliable)
            bool useCountBased = (quota.CurrentBytes == 0 || quota.ArchiveBytes == 0) && quota.ArchiveCount > 0;
            
            if (useCountBased)
            {
                // Count-based cleanup: Use percentage of archive count
                // This works reliably for in-memory databases where PRAGMA/dbstat may return 0
                var targetArchiveCount = (int)(quota.ArchiveCount * (targetPercent / 100.0));
                archivesToDelete = (int)(quota.ArchiveCount - targetArchiveCount);
            }
            else
            {
                // Byte-based cleanup: Use actual storage bytes from PRAGMA/dbstat
                // Only used when both CurrentBytes and ArchiveBytes are reliable (non-zero)
                var bytesToFree = Math.Max(0, quota.CurrentBytes - targetBytes);
                
                // Estimate bytes per archive
                long avgBytesPerArchive = quota.ArchiveCount > 0 && quota.ArchiveBytes > 0
                    ? quota.ArchiveBytes / quota.ArchiveCount
                    : 5120L; // Default 5KB estimate
                    
                archivesToDelete = (int)Math.Ceiling((double)bytesToFree / avgBytesPerArchive);
            }
            
            // No cleanup needed if archivesToDelete is 0 or negative
            if (archivesToDelete <= 0)
                return Result<int>.Success(0);

            // Step 2: Acquire lock for deletion operations
            await _connectionLock.WaitAsync(cancellationToken);
            try
            {
                int totalDeleted = 0;
                const int batchSize = 1000;

                // Phase 1: Delete non-corrected archives (UserCorrected = 0)
                var nonCorrectedCount = await _context.EmailArchives.CountAsync(a => a.UserCorrected == 0, cancellationToken);

                // Delete non-corrected archives first (oldest to newest)
                int nonCorrectedDeleted = 0;
                if (nonCorrectedCount > 0)
                {
                    var toDeleteFromNonCorrected = Math.Min(archivesToDelete, nonCorrectedCount);

                    while (nonCorrectedDeleted < toDeleteFromNonCorrected && !cancellationToken.IsCancellationRequested)
                    {
                        var batchToDelete = Math.Min(batchSize, toDeleteFromNonCorrected - nonCorrectedDeleted);

                        var oldestArchives = await _context.EmailArchives
                            .Where(a => a.UserCorrected == 0)
                            .OrderBy(a => a.ArchivedAt)
                            .Take(batchToDelete)
                            .ToListAsync(cancellationToken);

                        if (oldestArchives.Count == 0)
                            break;

                        _context.EmailArchives.RemoveRange(oldestArchives);
                        await _context.SaveChangesAsync(cancellationToken);

                        nonCorrectedDeleted += oldestArchives.Count;
                    }

                    totalDeleted += nonCorrectedDeleted;
                }

                // Phase 2: If needed, delete user-corrected archives (max 5% for 95% retention rate)
                if (totalDeleted < archivesToDelete)
                {
                    var correctedCount = await _context.EmailArchives.CountAsync(a => a.UserCorrected == 1, cancellationToken);

                    int correctedDeleted = 0;
                    var remainingToDelete = archivesToDelete - totalDeleted;
                    
                    // SC-004: Respect 95% retention rate for user-corrected emails (delete max 5%)
                    var maxUserCorrectedToDelete = (int)Math.Ceiling(correctedCount * 0.05);
                    var toDeleteFromCorrected = Math.Min(Math.Min(remainingToDelete, correctedCount), maxUserCorrectedToDelete);

                    while (correctedDeleted < toDeleteFromCorrected && !cancellationToken.IsCancellationRequested)
                    {
                        var batchToDelete = Math.Min(batchSize, toDeleteFromCorrected - correctedDeleted);

                        var oldestCorrected = await _context.EmailArchives
                            .Where(a => a.UserCorrected == 1)
                            .OrderBy(a => a.ArchivedAt)
                            .Take(batchToDelete)
                            .ToListAsync(cancellationToken);

                        if (oldestCorrected.Count == 0)
                            break;

                        _context.EmailArchives.RemoveRange(oldestCorrected);
                        await _context.SaveChangesAsync(cancellationToken);

                        correctedDeleted += oldestCorrected.Count;
                    }

                    totalDeleted += correctedDeleted;
                }

                // Step 3: Execute VACUUM to reclaim disk space (skip for in-memory databases)
                if (totalDeleted > 0)
                {
                    try
                    {
                        await _context.Database.ExecuteSqlRawAsync("VACUUM", cancellationToken);
                    }
                    catch
                    {
                        // VACUUM may fail on in-memory databases or during tests - ignore
                    }
                }

                return Result<int>.Success(totalDeleted);
            }
            finally
            {
                _connectionLock.Release();
            }
        }
        catch (Exception ex)
        {
            return Result<int>.Failure(new StorageError("Failed to execute cleanup", ex.Message, ex));
        }
        finally
        {
            // Step 5: Refresh storage usage after cleanup (outside lock)
            _ = await GetStorageUsageAsync(cancellationToken);
        }
    }
}
