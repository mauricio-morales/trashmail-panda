using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using TrashMailPanda.Shared;
using TrashMailPanda.Shared.Base;

namespace TrashMailPanda.Providers.Storage.Services;

/// <summary>
/// Domain service for managing user-defined email filtering rules.
/// Handles business logic for rule creation, updates, and validation.
/// </summary>
public interface IUserRulesService
{
    /// <summary>
    /// Retrieves all user-defined filtering rules.
    /// </summary>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>
    /// Success: UserRules with all configured filters
    /// Failure: StorageError if database operation fails
    /// </returns>
    Task<Result<UserRules>> GetUserRulesAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Updates user filtering rules.
    /// Validates rules before persisting to ensure consistency.
    /// </summary>
    /// <param name="rules">The updated rules</param>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>
    /// Success: true if updated successfully
    /// Failure: ValidationError if rules are invalid, StorageError if database operation fails
    /// </returns>
    Task<Result<bool>> UpdateUserRulesAsync(UserRules rules, CancellationToken cancellationToken = default);

    /// <summary>
    /// Adds a sender to the always-keep list.
    /// </summary>
    /// <param name="sender">Email address to always keep</param>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>
    /// Success: true if added successfully
    /// Failure: ValidationError if sender is invalid, StorageError if operation fails
    /// </returns>
    Task<Result<bool>> AddAlwaysKeepSenderAsync(string sender, CancellationToken cancellationToken = default);

    /// <summary>
    /// Adds a sender to the auto-trash list.
    /// </summary>
    /// <param name="sender">Email address to auto-trash</param>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>
    /// Success: true if added successfully
    /// Failure: ValidationError if sender is invalid, StorageError if operation fails
    /// </returns>
    Task<Result<bool>> AddAutoTrashSenderAsync(string sender, CancellationToken cancellationToken = default);

    /// <summary>
    /// Removes a sender from all rule lists.
    /// </summary>
    /// <param name="sender">Email address to remove</param>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>
    /// Success: true if removed, false if not found
    /// Failure: StorageError if operation fails
    /// </returns>
    Task<Result<bool>> RemoveSenderAsync(string sender, CancellationToken cancellationToken = default);
}
