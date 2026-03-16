using System.Threading;
using System.Threading.Tasks;
using TrashMailPanda.Shared;
using TrashMailPanda.Shared.Base;

namespace TrashMailPanda.Providers.Storage.Services;

/// <summary>
/// Domain service for managing application configuration.
/// Handles connection state, processing settings, and UI preferences.
/// </summary>
public interface IConfigurationService
{
    /// <summary>
    /// Retrieves the complete application configuration.
    /// </summary>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>
    /// Success: AppConfig with all configuration settings
    /// Failure: StorageError if database operation fails
    /// </returns>
    Task<Result<AppConfig>> GetConfigAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Updates the complete application configuration.
    /// </summary>
    /// <param name="config">The updated configuration</param>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>
    /// Success: true if updated successfully
    /// Failure: ValidationError if config is invalid, StorageError if database operation fails
    /// </returns>
    Task<Result<bool>> UpdateConfigAsync(AppConfig config, CancellationToken cancellationToken = default);

    /// <summary>
    /// Updates only the connection state.
    /// </summary>
    /// <param name="connectionState">The updated connection state</param>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>
    /// Success: true if updated successfully
    /// Failure: ValidationError if state is invalid, StorageError if database operation fails
    /// </returns>
    Task<Result<bool>> UpdateConnectionStateAsync(
        ConnectionState connectionState,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Updates only the processing settings.
    /// </summary>
    /// <param name="settings">The updated processing settings</param>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>
    /// Success: true if updated successfully
    /// Failure: ValidationError if settings are invalid, StorageError if database operation fails
    /// </returns>
    Task<Result<bool>> UpdateProcessingSettingsAsync(
        ProcessingSettings settings,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Updates only the UI settings.
    /// </summary>
    /// <param name="settings">The updated UI settings</param>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>
    /// Success: true if updated successfully
    /// Failure: ValidationError if settings are invalid, StorageError if database operation fails
    /// </returns>
    Task<Result<bool>> UpdateUISettingsAsync(
        UISettings settings,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Resets configuration to default values.
    /// </summary>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>
    /// Success: true if reset successfully
    /// Failure: StorageError if database operation fails
    /// </returns>
    Task<Result<bool>> ResetToDefaultsAsync(CancellationToken cancellationToken = default);
}
