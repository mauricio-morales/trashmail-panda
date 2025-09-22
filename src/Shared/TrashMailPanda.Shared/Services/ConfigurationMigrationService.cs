using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using TrashMailPanda.Shared.Base;

namespace TrashMailPanda.Shared.Services;

/// <summary>
/// Service for migrating configurations from legacy individual provider configs to unified GoogleServicesProviderConfig
/// Provides smooth transition from separate Gmail and Contacts configurations to unified Google Services configuration
/// </summary>
public class ConfigurationMigrationService : IConfigurationMigrationService
{
    private readonly IConfiguration _configuration;
    private readonly ILogger<ConfigurationMigrationService> _logger;

    public ConfigurationMigrationService(
        IConfiguration configuration,
        ILogger<ConfigurationMigrationService> logger)
    {
        _configuration = configuration ?? throw new ArgumentNullException(nameof(configuration));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    /// <summary>
    /// Checks if configuration migration is needed by detecting legacy provider configurations
    /// </summary>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>True if migration is needed</returns>
    public async Task<Result<bool>> IsMigrationNeededAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            await Task.CompletedTask; // Async for future extensibility

            _logger.LogDebug("Checking if configuration migration is needed");

            // Check if GoogleServicesProvider config exists and is complete
            var googleServicesSection = _configuration.GetSection("GoogleServicesProvider");
            if (!googleServicesSection.Exists())
            {
                _logger.LogInformation("GoogleServicesProvider configuration not found - migration needed");
                return Result<bool>.Success(true);
            }

            // Check if ClientId is configured
            var clientId = googleServicesSection["ClientId"];
            if (string.IsNullOrEmpty(clientId))
            {
                // Check if legacy EmailProvider has configuration
                var emailProviderSection = _configuration.GetSection("EmailProvider");
                var legacyClientId = emailProviderSection["ClientId"];

                if (!string.IsNullOrEmpty(legacyClientId))
                {
                    _logger.LogInformation("Found legacy EmailProvider configuration - migration needed");
                    return Result<bool>.Success(true);
                }
            }

            _logger.LogDebug("Configuration migration not needed");
            return Result<bool>.Success(false);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error checking if configuration migration is needed");
            return Result<bool>.Failure(ex.ToProviderError("Configuration migration check failed"));
        }
    }

    /// <summary>
    /// Creates a GoogleServicesProviderConfig by migrating from legacy configurations
    /// </summary>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>Migrated configuration result</returns>
    public async Task<Result<ConfigurationMigrationResult>> MigrateConfigurationAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            await Task.CompletedTask; // Async for future extensibility

            _logger.LogInformation("Starting configuration migration from legacy providers to GoogleServicesProvider");

            var migrationResult = new ConfigurationMigrationResult
            {
                StartTime = DateTime.UtcNow
            };

            // Get legacy configurations
            var emailProviderSection = _configuration.GetSection("EmailProvider");
            var contactsProviderSection = _configuration.GetSection("ContactsProvider");

            // Build recommendations for GoogleServicesProvider configuration
            var recommendations = new List<string>();

            if (emailProviderSection.Exists())
            {
                var clientId = emailProviderSection["ClientId"];
                var clientSecret = emailProviderSection["ClientSecret"];
                var redirectUri = emailProviderSection["RedirectUri"];
                var timeoutSeconds = emailProviderSection["TimeoutSeconds"];

                if (!string.IsNullOrEmpty(clientId))
                {
                    recommendations.Add($"Set GoogleServicesProvider:ClientId to the same value as EmailProvider:ClientId");
                    migrationResult.FoundLegacyEmailConfig = true;
                }

                if (!string.IsNullOrEmpty(clientSecret))
                {
                    recommendations.Add($"Set GoogleServicesProvider:ClientSecret to the same value as EmailProvider:ClientSecret");
                }

                if (!string.IsNullOrEmpty(redirectUri))
                {
                    recommendations.Add($"Set GoogleServicesProvider:RedirectUri to '{redirectUri}'");
                }

                if (!string.IsNullOrEmpty(timeoutSeconds))
                {
                    recommendations.Add($"Set GoogleServicesProvider:TimeoutSeconds to {timeoutSeconds}");
                }
            }

            if (contactsProviderSection.Exists())
            {
                var isEnabled = contactsProviderSection["IsEnabled"];
                var timeoutSeconds = contactsProviderSection["TimeoutSeconds"];
                var maxRetryAttempts = contactsProviderSection["MaxRetryAttempts"];

                if (!string.IsNullOrEmpty(isEnabled))
                {
                    recommendations.Add($"Set GoogleServicesProvider:EnableContacts to {isEnabled}");
                    migrationResult.FoundLegacyContactsConfig = true;
                }

                if (!string.IsNullOrEmpty(timeoutSeconds))
                {
                    recommendations.Add($"Consider setting GoogleServicesProvider:ContactsRequestTimeout to match ContactsProvider timeout");
                }

                if (!string.IsNullOrEmpty(maxRetryAttempts))
                {
                    recommendations.Add($"Set GoogleServicesProvider:ContactsMaxRetries to {maxRetryAttempts}");
                }
            }

            migrationResult.EndTime = DateTime.UtcNow;
            migrationResult.IsSuccessful = true;
            migrationResult.Recommendations = recommendations;

            if (recommendations.Count > 0)
            {
                _logger.LogInformation("Configuration migration analysis completed: {RecommendationCount} recommendations generated",
                    recommendations.Count);

                foreach (var recommendation in recommendations)
                {
                    _logger.LogInformation("Migration recommendation: {Recommendation}", recommendation);
                }
            }
            else
            {
                _logger.LogInformation("No legacy configuration found to migrate");
            }

            return Result<ConfigurationMigrationResult>.Success(migrationResult);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Configuration migration failed");
            return Result<ConfigurationMigrationResult>.Failure(ex.ToProviderError("Configuration migration failed"));
        }
    }

    /// <summary>
    /// Validates that the GoogleServicesProviderConfig is properly configured
    /// </summary>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>Validation result</returns>
    public async Task<Result<bool>> ValidateConfigurationAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            await Task.CompletedTask; // Async for future extensibility

            _logger.LogDebug("Validating GoogleServicesProvider configuration");

            var googleServicesSection = _configuration.GetSection("GoogleServicesProvider");
            if (!googleServicesSection.Exists())
            {
                return Result<bool>.Failure(new ValidationError("GoogleServicesProvider configuration section not found"));
            }

            var errors = new List<string>();

            // Validate required OAuth settings
            var clientId = googleServicesSection["ClientId"];
            var clientSecret = googleServicesSection["ClientSecret"];
            var redirectUri = googleServicesSection["RedirectUri"];

            if (string.IsNullOrEmpty(clientId))
            {
                errors.Add("ClientId is required but not configured");
            }

            if (string.IsNullOrEmpty(clientSecret))
            {
                errors.Add("ClientSecret is required but not configured");
            }

            if (string.IsNullOrEmpty(redirectUri))
            {
                errors.Add("RedirectUri is required but not configured");
            }

            // Validate feature flags
            var enableGmail = googleServicesSection.GetValue<bool?>("EnableGmail");
            var enableContacts = googleServicesSection.GetValue<bool?>("EnableContacts");

            if (enableGmail != true && enableContacts != true)
            {
                errors.Add("At least one Google service (Gmail or Contacts) must be enabled");
            }

            // Validate timeout settings
            var timeoutSeconds = googleServicesSection.GetValue<int?>("TimeoutSeconds");
            if (timeoutSeconds.HasValue && timeoutSeconds.Value <= 0)
            {
                errors.Add("TimeoutSeconds must be greater than 0");
            }

            if (errors.Count > 0)
            {
                var combinedErrors = string.Join("; ", errors);
                _logger.LogWarning("GoogleServicesProvider configuration validation failed: {Errors}", combinedErrors);
                return Result<bool>.Failure(new ValidationError($"Configuration validation failed: {combinedErrors}"));
            }

            _logger.LogDebug("GoogleServicesProvider configuration validation passed");
            return Result<bool>.Success(true);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Configuration validation failed");
            return Result<bool>.Failure(ex.ToProviderError("Configuration validation failed"));
        }
    }
}

/// <summary>
/// Interface for configuration migration service
/// </summary>
public interface IConfigurationMigrationService
{
    /// <summary>
    /// Checks if configuration migration is needed
    /// </summary>
    Task<Result<bool>> IsMigrationNeededAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Migrates configuration from legacy providers to unified provider
    /// </summary>
    Task<Result<ConfigurationMigrationResult>> MigrateConfigurationAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Validates the unified provider configuration
    /// </summary>
    Task<Result<bool>> ValidateConfigurationAsync(CancellationToken cancellationToken = default);
}

/// <summary>
/// Result of configuration migration operation
/// </summary>
public class ConfigurationMigrationResult
{
    /// <summary>
    /// When the migration started
    /// </summary>
    public DateTime StartTime { get; set; }

    /// <summary>
    /// When the migration ended
    /// </summary>
    public DateTime? EndTime { get; set; }

    /// <summary>
    /// Whether the migration was successful
    /// </summary>
    public bool IsSuccessful { get; set; }

    /// <summary>
    /// Whether legacy email provider config was found
    /// </summary>
    public bool FoundLegacyEmailConfig { get; set; }

    /// <summary>
    /// Whether legacy contacts provider config was found
    /// </summary>
    public bool FoundLegacyContactsConfig { get; set; }

    /// <summary>
    /// Configuration migration recommendations
    /// </summary>
    public List<string> Recommendations { get; set; } = new();

    /// <summary>
    /// Duration of the migration operation
    /// </summary>
    public TimeSpan Duration => EndTime?.Subtract(StartTime) ?? TimeSpan.Zero;
}