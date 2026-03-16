using Microsoft.EntityFrameworkCore;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Linq.Expressions;
using System.Threading;
using System.Threading.Tasks;
using TrashMailPanda.Shared.Base;

namespace TrashMailPanda.Providers.Storage;

/// <summary>
/// Thread-safe SQLite storage repository using EF Core.
/// Manages database concurrency with singleton semaphore injected via DI.
/// All database operations are protected by the shared semaphore lock.
/// </summary>
public class SqliteStorageRepository : IStorageRepository
{
    private readonly TrashMailPandaDbContext _context;
    private readonly SemaphoreSlim _databaseLock;
    private bool _disposed;

    /// <summary>
    /// Initializes a new instance of SqliteStorageRepository.
    /// </summary>
    /// <param name="context">The EF Core database context</param>
    /// <param name="databaseLock">Singleton semaphore for database concurrency control</param>
    public SqliteStorageRepository(
        TrashMailPandaDbContext context,
        SemaphoreSlim databaseLock)
    {
        _context = context ?? throw new ArgumentNullException(nameof(context));
        _databaseLock = databaseLock ?? throw new ArgumentNullException(nameof(databaseLock));
    }

    // ============================================================
    // Generic CRUD Operations
    // ============================================================

    public async Task<Result<T?>> GetByIdAsync<T>(object id, CancellationToken cancellationToken = default) where T : class
    {
        if (id == null)
            return Result<T?>.Failure(new ValidationError("Id cannot be null"));

        try
        {
            await _databaseLock.WaitAsync(cancellationToken);
            try
            {
                var entity = await _context.Set<T>().FindAsync(new[] { id }, cancellationToken);
                return Result<T?>.Success(entity);
            }
            finally
            {
                _databaseLock.Release();
            }
        }
        catch (Exception ex)
        {
            return Result<T?>.Failure(new StorageError($"Failed to retrieve {typeof(T).Name}", ex.Message, ex));
        }
    }

    public async Task<Result<IEnumerable<T>>> GetAllAsync<T>(CancellationToken cancellationToken = default) where T : class
    {
        try
        {
            await _databaseLock.WaitAsync(cancellationToken);
            try
            {
                var entities = await _context.Set<T>().ToListAsync(cancellationToken);
                return Result<IEnumerable<T>>.Success(entities);
            }
            finally
            {
                _databaseLock.Release();
            }
        }
        catch (Exception ex)
        {
            return Result<IEnumerable<T>>.Failure(new StorageError($"Failed to retrieve all {typeof(T).Name}", ex.Message, ex));
        }
    }

    public async Task<Result<IEnumerable<T>>> QueryAsync<T>(
        Expression<Func<T, bool>> predicate,
        CancellationToken cancellationToken = default) where T : class
    {
        if (predicate == null)
            return Result<IEnumerable<T>>.Failure(new ValidationError("Predicate cannot be null"));

        try
        {
            await _databaseLock.WaitAsync(cancellationToken);
            try
            {
                var entities = await _context.Set<T>().Where(predicate).ToListAsync(cancellationToken);
                return Result<IEnumerable<T>>.Success(entities);
            }
            finally
            {
                _databaseLock.Release();
            }
        }
        catch (Exception ex)
        {
            return Result<IEnumerable<T>>.Failure(new StorageError($"Failed to query {typeof(T).Name}", ex.Message, ex));
        }
    }

    public async Task<Result<bool>> AddAsync<T>(T entity, CancellationToken cancellationToken = default) where T : class
    {
        if (entity == null)
            return Result<bool>.Failure(new ValidationError("Entity cannot be null"));

        try
        {
            await _databaseLock.WaitAsync(cancellationToken);
            try
            {
                await _context.Set<T>().AddAsync(entity, cancellationToken);
                await _context.SaveChangesAsync(cancellationToken);
                return Result<bool>.Success(true);
            }
            finally
            {
                _databaseLock.Release();
            }
        }
        catch (Exception ex)
        {
            return Result<bool>.Failure(new StorageError($"Failed to add {typeof(T).Name}", ex.Message, ex));
        }
    }

    public async Task<Result<bool>> UpdateAsync<T>(T entity, CancellationToken cancellationToken = default) where T : class
    {
        if (entity == null)
            return Result<bool>.Failure(new ValidationError("Entity cannot be null"));

        try
        {
            await _databaseLock.WaitAsync(cancellationToken);
            try
            {
                _context.Set<T>().Update(entity);
                await _context.SaveChangesAsync(cancellationToken);
                return Result<bool>.Success(true);
            }
            finally
            {
                _databaseLock.Release();
            }
        }
        catch (Exception ex)
        {
            return Result<bool>.Failure(new StorageError($"Failed to update {typeof(T).Name}", ex.Message, ex));
        }
    }

    public async Task<Result<bool>> DeleteAsync<T>(object id, CancellationToken cancellationToken = default) where T : class
    {
        if (id == null)
            return Result<bool>.Failure(new ValidationError("Id cannot be null"));

        try
        {
            await _databaseLock.WaitAsync(cancellationToken);
            try
            {
                var entity = await _context.Set<T>().FindAsync(new[] { id }, cancellationToken);
                if (entity == null)
                    return Result<bool>.Success(false);

                _context.Set<T>().Remove(entity);
                await _context.SaveChangesAsync(cancellationToken);
                return Result<bool>.Success(true);
            }
            finally
            {
                _databaseLock.Release();
            }
        }
        catch (Exception ex)
        {
            return Result<bool>.Failure(new StorageError($"Failed to delete {typeof(T).Name}", ex.Message, ex));
        }
    }

    // ============================================================
    // Batch Operations
    // ============================================================

    public async Task<Result<int>> AddRangeAsync<T>(
        IEnumerable<T> entities,
        CancellationToken cancellationToken = default) where T : class
    {
        if (entities == null)
            return Result<int>.Failure(new ValidationError("Entities collection cannot be null"));

        var entityList = entities.ToList();
        if (entityList.Count == 0)
            return Result<int>.Success(0);

        try
        {
            await _databaseLock.WaitAsync(cancellationToken);
            try
            {
                await _context.Set<T>().AddRangeAsync(entityList, cancellationToken);
                await _context.SaveChangesAsync(cancellationToken);
                return Result<int>.Success(entityList.Count);
            }
            finally
            {
                _databaseLock.Release();
            }
        }
        catch (Exception ex)
        {
            return Result<int>.Failure(new StorageError($"Failed to add {typeof(T).Name} batch", ex.Message, ex));
        }
    }

    public async Task<Result<int>> UpdateRangeAsync<T>(
        IEnumerable<T> entities,
        CancellationToken cancellationToken = default) where T : class
    {
        if (entities == null)
            return Result<int>.Failure(new ValidationError("Entities collection cannot be null"));

        var entityList = entities.ToList();
        if (entityList.Count == 0)
            return Result<int>.Success(0);

        try
        {
            await _databaseLock.WaitAsync(cancellationToken);
            try
            {
                _context.Set<T>().UpdateRange(entityList);
                await _context.SaveChangesAsync(cancellationToken);
                return Result<int>.Success(entityList.Count);
            }
            finally
            {
                _databaseLock.Release();
            }
        }
        catch (Exception ex)
        {
            return Result<int>.Failure(new StorageError($"Failed to update {typeof(T).Name} batch", ex.Message, ex));
        }
    }

    public async Task<Result<int>> DeleteRangeAsync<T>(
        IEnumerable<T> entities,
        CancellationToken cancellationToken = default) where T : class
    {
        if (entities == null)
            return Result<int>.Failure(new ValidationError("Entities collection cannot be null"));

        var entityList = entities.ToList();
        if (entityList.Count == 0)
            return Result<int>.Success(0);

        try
        {
            await _databaseLock.WaitAsync(cancellationToken);
            try
            {
                _context.Set<T>().RemoveRange(entityList);
                await _context.SaveChangesAsync(cancellationToken);
                return Result<int>.Success(entityList.Count);
            }
            finally
            {
                _databaseLock.Release();
            }
        }
        catch (Exception ex)
        {
            return Result<int>.Failure(new StorageError($"Failed to delete {typeof(T).Name} batch", ex.Message, ex));
        }
    }

    // ============================================================
    // Transaction Support
    // ============================================================

    public async Task<Result<TResult>> ExecuteTransactionAsync<TResult>(
        Func<Task<Result<TResult>>> operation,
        CancellationToken cancellationToken = default)
    {
        if (operation == null)
            return Result<TResult>.Failure(new ValidationError("Operation cannot be null"));

        try
        {
            await _databaseLock.WaitAsync(cancellationToken);
            try
            {
                using var transaction = await _context.Database.BeginTransactionAsync(cancellationToken);
                try
                {
                    var result = await operation();
                    if (!result.IsSuccess)
                    {
                        await transaction.RollbackAsync(cancellationToken);
                        return result;
                    }

                    await transaction.CommitAsync(cancellationToken);
                    return result;
                }
                catch (Exception ex)
                {
                    await transaction.RollbackAsync(cancellationToken);
                    return Result<TResult>.Failure(new StorageError("Transaction failed", ex.Message, ex));
                }
            }
            finally
            {
                _databaseLock.Release();
            }
        }
        catch (Exception ex)
        {
            return Result<TResult>.Failure(new StorageError("Failed to execute transaction", ex.Message, ex));
        }
    }

    // ============================================================
    // Raw SQL Support
    // ============================================================

    public async Task<Result<IEnumerable<T>>> ExecuteSqlQueryAsync<T>(
        string sql,
        object[] parameters,
        CancellationToken cancellationToken = default) where T : class
    {
        if (string.IsNullOrWhiteSpace(sql))
            return Result<IEnumerable<T>>.Failure(new ValidationError("SQL query cannot be null or empty"));

        try
        {
            await _databaseLock.WaitAsync(cancellationToken);
            try
            {
                var results = await _context.Set<T>()
                    .FromSqlRaw(sql, parameters ?? Array.Empty<object>())
                    .ToListAsync(cancellationToken);
                return Result<IEnumerable<T>>.Success(results);
            }
            finally
            {
                _databaseLock.Release();
            }
        }
        catch (Exception ex)
        {
            return Result<IEnumerable<T>>.Failure(new StorageError("Failed to execute SQL query", ex.Message, ex));
        }
    }

    public async Task<Result<int>> ExecuteSqlCommandAsync(
        string sql,
        object[] parameters,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(sql))
            return Result<int>.Failure(new ValidationError("SQL command cannot be null or empty"));

        try
        {
            await _databaseLock.WaitAsync(cancellationToken);
            try
            {
                var rowsAffected = await _context.Database.ExecuteSqlRawAsync(
                    sql,
                    parameters ?? Array.Empty<object>(),
                    cancellationToken);
                return Result<int>.Success(rowsAffected);
            }
            finally
            {
                _databaseLock.Release();
            }
        }
        catch (Exception ex)
        {
            return Result<int>.Failure(new StorageError("Failed to execute SQL command", ex.Message, ex));
        }
    }

    // ============================================================
    // IDisposable Implementation
    // ============================================================

    public void Dispose()
    {
        if (_disposed)
            return;

        // Note: We don't dispose the semaphore because it's a singleton
        // injected via DI and managed by the DI container

        _disposed = true;
        GC.SuppressFinalize(this);
    }
}
