using System;
using System.Collections.Generic;
using System.Linq.Expressions;
using System.Threading;
using System.Threading.Tasks;
using TrashMailPanda.Shared.Base;

namespace TrashMailPanda.Providers.Storage;

/// <summary>
/// Low-level data access repository interface.
/// Manages EF Core DbContext and database concurrency with singleton semaphore.
/// All operations are thread-safe and follow the Result&lt;T&gt; pattern.
/// </summary>
/// <remarks>
/// This interface provides generic CRUD operations for all entity types.
/// Concurrency is managed through a singleton SemaphoreSlim injected via DI.
/// Use this interface for all direct database access to ensure proper locking.
/// </remarks>
public interface IStorageRepository : IDisposable
{
    // ============================================================
    // Generic CRUD Operations
    // ============================================================

    /// <summary>
    /// Retrieves an entity by its primary key.
    /// </summary>
    /// <typeparam name="T">The entity type</typeparam>
    /// <param name="id">The primary key value</param>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>
    /// Success: Entity if found, null if not found
    /// Failure: StorageError if database operation fails
    /// </returns>
    Task<Result<T?>> GetByIdAsync<T>(object id, CancellationToken cancellationToken = default) where T : class;

    /// <summary>
    /// Retrieves all entities of the specified type.
    /// </summary>
    /// <typeparam name="T">The entity type</typeparam>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>
    /// Success: Collection of all entities (may be empty)
    /// Failure: StorageError if database operation fails
    /// </returns>
    Task<Result<IEnumerable<T>>> GetAllAsync<T>(CancellationToken cancellationToken = default) where T : class;

    /// <summary>
    /// Queries entities using a predicate filter.
    /// </summary>
    /// <typeparam name="T">The entity type</typeparam>
    /// <param name="predicate">Filter expression</param>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>
    /// Success: Collection of matching entities (may be empty)
    /// Failure: StorageError if database operation fails
    /// </returns>
    Task<Result<IEnumerable<T>>> QueryAsync<T>(
        Expression<Func<T, bool>> predicate,
        CancellationToken cancellationToken = default) where T : class;

    /// <summary>
    /// Adds a new entity to the database.
    /// </summary>
    /// <typeparam name="T">The entity type</typeparam>
    /// <param name="entity">The entity to add</param>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>
    /// Success: true if added successfully
    /// Failure: ValidationError if entity is invalid, StorageError if database operation fails
    /// </returns>
    Task<Result<bool>> AddAsync<T>(T entity, CancellationToken cancellationToken = default) where T : class;

    /// <summary>
    /// Updates an existing entity in the database.
    /// </summary>
    /// <typeparam name="T">The entity type</typeparam>
    /// <param name="entity">The entity to update</param>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>
    /// Success: true if updated successfully
    /// Failure: ValidationError if entity is invalid, StorageError if database operation fails
    /// </returns>
    Task<Result<bool>> UpdateAsync<T>(T entity, CancellationToken cancellationToken = default) where T : class;

    /// <summary>
    /// Deletes an entity by its primary key.
    /// </summary>
    /// <typeparam name="T">The entity type</typeparam>
    /// <param name="id">The primary key value</param>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>
    /// Success: true if deleted, false if not found
    /// Failure: StorageError if database operation fails
    /// </returns>
    Task<Result<bool>> DeleteAsync<T>(object id, CancellationToken cancellationToken = default) where T : class;

    // ============================================================
    // Batch Operations
    // ============================================================

    /// <summary>
    /// Adds multiple entities in a single transaction.
    /// </summary>
    /// <typeparam name="T">The entity type</typeparam>
    /// <param name="entities">The entities to add</param>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>
    /// Success: Number of entities added
    /// Failure: ValidationError if any entity is invalid, StorageError if database operation fails
    /// </returns>
    Task<Result<int>> AddRangeAsync<T>(
        IEnumerable<T> entities,
        CancellationToken cancellationToken = default) where T : class;

    /// <summary>
    /// Updates multiple entities in a single transaction.
    /// </summary>
    /// <typeparam name="T">The entity type</typeparam>
    /// <param name="entities">The entities to update</param>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>
    /// Success: Number of entities updated
    /// Failure: ValidationError if any entity is invalid, StorageError if database operation fails
    /// </returns>
    Task<Result<int>> UpdateRangeAsync<T>(
        IEnumerable<T> entities,
        CancellationToken cancellationToken = default) where T : class;

    /// <summary>
    /// Deletes multiple entities in a single transaction.
    /// </summary>
    /// <typeparam name="T">The entity type</typeparam>
    /// <param name="entities">The entities to delete</param>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>
    /// Success: Number of entities deleted
    /// Failure: StorageError if database operation fails
    /// </returns>
    Task<Result<int>> DeleteRangeAsync<T>(
        IEnumerable<T> entities,
        CancellationToken cancellationToken = default) where T : class;

    // ============================================================
    // Transaction Support
    // ============================================================

    /// <summary>
    /// Executes an operation within a database transaction.
    /// Automatically commits on success or rolls back on failure.
    /// </summary>
    /// <typeparam name="TResult">The result type</typeparam>
    /// <param name="operation">The operation to execute</param>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>
    /// Success: Result of the operation
    /// Failure: StorageError if transaction fails
    /// </returns>
    Task<Result<TResult>> ExecuteTransactionAsync<TResult>(
        Func<Task<Result<TResult>>> operation,
        CancellationToken cancellationToken = default);

    // ============================================================
    // Raw SQL Support
    // ============================================================

    /// <summary>
    /// Executes a raw SQL query and returns results.
    /// Use parameterized queries to prevent SQL injection.
    /// </summary>
    /// <typeparam name="T">The result type</typeparam>
    /// <param name="sql">The SQL query</param>
    /// <param name="parameters">Query parameters</param>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>
    /// Success: Query results
    /// Failure: StorageError if query execution fails
    /// </returns>
    Task<Result<IEnumerable<T>>> ExecuteSqlQueryAsync<T>(
        string sql,
        object[] parameters,
        CancellationToken cancellationToken = default) where T : class;

    /// <summary>
    /// Executes a raw SQL command (INSERT, UPDATE, DELETE).
    /// Use parameterized commands to prevent SQL injection.
    /// </summary>
    /// <param name="sql">The SQL command</param>
    /// <param name="parameters">Command parameters</param>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>
    /// Success: Number of rows affected
    /// Failure: StorageError if command execution fails
    /// </returns>
    Task<Result<int>> ExecuteSqlCommandAsync(
        string sql,
        object[] parameters,
        CancellationToken cancellationToken = default);
}
