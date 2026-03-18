using System;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using TrashMailPanda.Providers.Storage.Models;
using TrashMailPanda.Shared.Base;

namespace TrashMailPanda.Providers.Storage;

/// <summary>
/// SQLite-backed repository for scan progress tracking.
/// Enforces the constraint that at most one InProgress/PausedStorageFull scan exists per account.
/// </summary>
public sealed class ScanProgressRepository : IScanProgressRepository
{
    private readonly TrashMailPandaDbContext _context;
    private readonly SemaphoreSlim _databaseLock;

    public ScanProgressRepository(TrashMailPandaDbContext context, SemaphoreSlim databaseLock)
    {
        _context = context ?? throw new ArgumentNullException(nameof(context));
        _databaseLock = databaseLock ?? throw new ArgumentNullException(nameof(databaseLock));
    }

    /// <inheritdoc />
    public async Task<Result<ScanProgressEntity?>> GetLatestAsync(
        string accountId,
        CancellationToken cancellationToken = default)
    {
        await _databaseLock.WaitAsync(cancellationToken);
        try
        {
            var entity = await _context.ScanProgress
                .Where(s => s.AccountId == accountId)
                .OrderByDescending(s => s.UpdatedAt)
                .FirstOrDefaultAsync(cancellationToken);

            return Result<ScanProgressEntity?>.Success(entity);
        }
        catch (Exception ex)
        {
            return Result<ScanProgressEntity?>.Failure(new StorageError("Failed to load scan progress", ex.Message, ex));
        }
        finally
        {
            _databaseLock.Release();
        }
    }

    /// <inheritdoc />
    public async Task<Result<ScanProgressEntity?>> GetActiveAsync(
        string accountId,
        CancellationToken cancellationToken = default)
    {
        await _databaseLock.WaitAsync(cancellationToken);
        try
        {
            var entity = await _context.ScanProgress
                .Where(s => s.AccountId == accountId &&
                            (s.Status == "InProgress" ||
                             s.Status == "PausedStorageFull" ||
                             s.Status == "Interrupted" ||
                             s.Status == "Completed"))
                .OrderByDescending(s => s.UpdatedAt)
                .FirstOrDefaultAsync(cancellationToken);

            return Result<ScanProgressEntity?>.Success(entity);
        }
        catch (Exception ex)
        {
            return Result<ScanProgressEntity?>.Failure(new StorageError("Failed to load scan progress", ex.Message, ex));
        }
        finally
        {
            _databaseLock.Release();
        }
    }

    /// <inheritdoc />
    public async Task<Result<ScanProgressEntity>> CreateAsync(
        ScanProgressEntity entity,
        CancellationToken cancellationToken = default)
    {
        await _databaseLock.WaitAsync(cancellationToken);
        try
        {
            entity.Status = "InProgress";
            entity.StartedAt = DateTime.UtcNow;
            entity.UpdatedAt = DateTime.UtcNow;

            _context.ScanProgress.Add(entity);
            await _context.SaveChangesAsync(cancellationToken);

            return Result<ScanProgressEntity>.Success(entity);
        }
        catch (Exception ex)
        {
            return Result<ScanProgressEntity>.Failure(new StorageError("Failed to create scan progress", ex.Message, ex));
        }
        finally
        {
            _databaseLock.Release();
        }
    }

    /// <inheritdoc />
    public async Task UpdateFolderProgressAsync(
        int id,
        string folderProgressJson,
        CancellationToken cancellationToken = default)
    {
        await _databaseLock.WaitAsync(cancellationToken);
        try
        {
            var entity = await _context.ScanProgress.FindAsync([id], cancellationToken);
            if (entity is null) return;

            entity.FolderProgressJson = folderProgressJson;
            entity.UpdatedAt = DateTime.UtcNow;
            await _context.SaveChangesAsync(cancellationToken);
        }
        finally
        {
            _databaseLock.Release();
        }
    }

    /// <inheritdoc />
    public async Task SaveHistoryIdAsync(int id, ulong historyId, CancellationToken cancellationToken = default)
    {
        await _databaseLock.WaitAsync(cancellationToken);
        try
        {
            var entity = await _context.ScanProgress.FindAsync([id], cancellationToken);
            if (entity is null) return;

            entity.HistoryId = historyId;
            entity.UpdatedAt = DateTime.UtcNow;
            await _context.SaveChangesAsync(cancellationToken);
        }
        finally
        {
            _databaseLock.Release();
        }
    }

    /// <inheritdoc />
    public async Task MarkCompletedAsync(int id, CancellationToken cancellationToken = default)
    {
        await _databaseLock.WaitAsync(cancellationToken);
        try
        {
            var entity = await _context.ScanProgress.FindAsync([id], cancellationToken);
            if (entity is null) return;

            entity.Status = "Completed";
            entity.CompletedAt = DateTime.UtcNow;
            entity.UpdatedAt = DateTime.UtcNow;
            await _context.SaveChangesAsync(cancellationToken);
        }
        finally
        {
            _databaseLock.Release();
        }
    }

    /// <inheritdoc />
    public async Task MarkInterruptedAsync(int id, CancellationToken cancellationToken = default)
    {
        await _databaseLock.WaitAsync(cancellationToken);
        try
        {
            var entity = await _context.ScanProgress.FindAsync([id], cancellationToken);
            if (entity is null) return;

            entity.Status = "Interrupted";
            entity.UpdatedAt = DateTime.UtcNow;
            await _context.SaveChangesAsync(cancellationToken);
        }
        finally
        {
            _databaseLock.Release();
        }
    }
}
