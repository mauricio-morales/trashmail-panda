using System;
using System.Threading;
using System.Threading.Tasks;
using TrashMailPanda.Shared;
using TrashMailPanda.Shared.Base;

namespace TrashMailPanda.Services;

/// <summary>
/// Interface for orchestrating application startup sequence
/// </summary>
public interface IStartupOrchestrator
{
    /// <summary>
    /// Execute the startup orchestration sequence
    /// </summary>
    /// <param name="cancellationToken">Cancellation token for the operation</param>
    Task<StartupResult> ExecuteStartupAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Get the current startup progress
    /// </summary>
    StartupProgress GetProgress();

    /// <summary>
    /// Event that fires when startup progress changes
    /// </summary>
    event EventHandler<StartupProgressChangedEventArgs>? ProgressChanged;

    /// <summary>
    /// Re-initializes the Gmail provider with stored OAuth credentials
    /// Used after successful OAuth authentication to pick up new tokens
    /// </summary>
    /// <param name="cancellationToken">Cancellation token for the operation</param>
    Task<Result<bool>> ReinitializeGmailProviderAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Re-initializes the Contacts provider with stored OAuth credentials
    /// Used after successful OAuth authentication to pick up new tokens
    /// </summary>
    /// <param name="cancellationToken">Cancellation token for the operation</param>
    Task<Result<bool>> ReinitializeContactsProviderAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Re-initializes the unified Google Services provider with stored OAuth credentials
    /// Used after successful OAuth authentication to pick up new tokens for both Gmail and Contacts
    /// </summary>
    /// <param name="cancellationToken">Cancellation token for the operation</param>
    Task<Result<bool>> ReinitializeGoogleServicesProviderAsync(CancellationToken cancellationToken = default);
}