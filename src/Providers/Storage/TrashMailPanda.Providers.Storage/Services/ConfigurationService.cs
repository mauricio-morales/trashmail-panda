using System;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using TrashMailPanda.Providers.Storage.Models;
using TrashMailPanda.Shared;
using TrashMailPanda.Shared.Base;

namespace TrashMailPanda.Providers.Storage.Services;

/// <summary>
/// Domain service implementation for managing application configuration.
/// </summary>
public class ConfigurationService : IConfigurationService
{
    private const string AppConfigKey = "AppConfig";

    private readonly IStorageRepository _repository;
    private readonly ILogger<ConfigurationService> _logger;

    public ConfigurationService(IStorageRepository repository, ILogger<ConfigurationService> logger)
    {
        _repository = repository ?? throw new ArgumentNullException(nameof(repository));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    public async Task<Result<AppConfig>> GetConfigAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            _logger.LogDebug("Retrieving application configuration");

            var entityResult = await _repository.GetByIdAsync<AppConfigEntity>(AppConfigKey, cancellationToken);

            if (!entityResult.IsSuccess)
            {
                return Result<AppConfig>.Failure(entityResult.Error);
            }

            AppConfig config;
            if (entityResult.Value == null)
            {
                // No config exists yet - create and persist default
                _logger.LogInformation("No application configuration found, creating defaults");
                config = new AppConfig
                {
                    ConnectionState = new ConnectionState(),
                    ProcessingSettings = new ProcessingSettings(),
                    UISettings = new UISettings()
                };

                var entity = new AppConfigEntity { Key = AppConfigKey, Value = JsonSerializer.Serialize(config) };
                var addResult = await _repository.AddAsync(entity, cancellationToken);
                if (!addResult.IsSuccess)
                {
                    return Result<AppConfig>.Failure(addResult.Error);
                }
            }
            else
            {
                config = JsonSerializer.Deserialize<AppConfig>(entityResult.Value.Value)
                    ?? new AppConfig
                    {
                        ConnectionState = new ConnectionState(),
                        ProcessingSettings = new ProcessingSettings(),
                        UISettings = new UISettings()
                    };
            }

            _logger.LogDebug("Retrieved application configuration");

            return Result<AppConfig>.Success(config);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to retrieve application configuration");
            return Result<AppConfig>.Failure(new StorageError($"Failed to retrieve configuration: {ex.Message}"));
        }
    }

    public async Task<Result<bool>> UpdateConfigAsync(AppConfig config, CancellationToken cancellationToken = default)
    {
        try
        {
            if (config == null)
            {
                return Result<bool>.Failure(new ValidationError("Configuration cannot be null"));
            }

            _logger.LogDebug("Updating application configuration");

            // Validate configuration
            var validationResult = ValidateConfig(config);
            if (!validationResult.IsSuccess)
            {
                return validationResult;
            }

            // Upsert the serialised AppConfig under the fixed key
            var json = JsonSerializer.Serialize(config);
            var entityResult = await _repository.GetByIdAsync<AppConfigEntity>(AppConfigKey, cancellationToken);

            if (!entityResult.IsSuccess)
            {
                return Result<bool>.Failure(entityResult.Error);
            }

            Result<bool> updateResult;
            if (entityResult.Value == null)
            {
                var entity = new AppConfigEntity { Key = AppConfigKey, Value = json };
                updateResult = await _repository.AddAsync(entity, cancellationToken);
            }
            else
            {
                entityResult.Value.Value = json;
                updateResult = await _repository.UpdateAsync(entityResult.Value, cancellationToken);
            }

            if (!updateResult.IsSuccess)
            {
                return Result<bool>.Failure(updateResult.Error);
            }

            _logger.LogInformation("Updated application configuration");

            return Result<bool>.Success(true);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to update application configuration");
            return Result<bool>.Failure(new StorageError($"Failed to update configuration: {ex.Message}"));
        }
    }

    public async Task<Result<bool>> UpdateConnectionStateAsync(
        ConnectionState connectionState,
        CancellationToken cancellationToken = default)
    {
        try
        {
            if (connectionState == null)
            {
                return Result<bool>.Failure(new ValidationError("Connection state cannot be null"));
            }

            _logger.LogDebug("Updating connection state");

            var configResult = await GetConfigAsync(cancellationToken);
            if (!configResult.IsSuccess)
            {
                return Result<bool>.Failure(configResult.Error);
            }

            var config = configResult.Value;
            config.ConnectionState = connectionState;

            return await UpdateConfigAsync(config, cancellationToken);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to update connection state");
            return Result<bool>.Failure(new StorageError($"Failed to update connection state: {ex.Message}"));
        }
    }

    public async Task<Result<bool>> UpdateProcessingSettingsAsync(
        ProcessingSettings settings,
        CancellationToken cancellationToken = default)
    {
        try
        {
            if (settings == null)
            {
                return Result<bool>.Failure(new ValidationError("Processing settings cannot be null"));
            }

            _logger.LogDebug("Updating processing settings");

            var configResult = await GetConfigAsync(cancellationToken);
            if (!configResult.IsSuccess)
            {
                return Result<bool>.Failure(configResult.Error);
            }

            var config = configResult.Value;
            config.ProcessingSettings = settings;

            return await UpdateConfigAsync(config, cancellationToken);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to update processing settings");
            return Result<bool>.Failure(new StorageError($"Failed to update processing settings: {ex.Message}"));
        }
    }

    public async Task<Result<bool>> UpdateUISettingsAsync(
        UISettings settings,
        CancellationToken cancellationToken = default)
    {
        try
        {
            if (settings == null)
            {
                return Result<bool>.Failure(new ValidationError("UI settings cannot be null"));
            }

            _logger.LogDebug("Updating UI settings");

            var configResult = await GetConfigAsync(cancellationToken);
            if (!configResult.IsSuccess)
            {
                return Result<bool>.Failure(configResult.Error);
            }

            var config = configResult.Value;
            config.UISettings = settings;

            return await UpdateConfigAsync(config, cancellationToken);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to update UI settings");
            return Result<bool>.Failure(new StorageError($"Failed to update UI settings: {ex.Message}"));
        }
    }

    public async Task<Result<bool>> ResetToDefaultsAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            _logger.LogInformation("Resetting configuration to defaults");

            var defaultConfig = new AppConfig
            {
                ConnectionState = new ConnectionState(),
                ProcessingSettings = new ProcessingSettings(),
                UISettings = new UISettings()
            };

            return await UpdateConfigAsync(defaultConfig, cancellationToken);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to reset configuration to defaults");
            return Result<bool>.Failure(new StorageError($"Failed to reset to defaults: {ex.Message}"));
        }
    }

    private Result<bool> ValidateConfig(AppConfig config)
    {
        // Basic validation - can be extended based on requirements
        if (config.ConnectionState == null)
        {
            return Result<bool>.Failure(new ValidationError("ConnectionState cannot be null"));
        }

        if (config.ProcessingSettings == null)
        {
            return Result<bool>.Failure(new ValidationError("ProcessingSettings cannot be null"));
        }

        if (config.UISettings == null)
        {
            return Result<bool>.Failure(new ValidationError("UISettings cannot be null"));
        }

        return Result<bool>.Success(true);
    }
}
