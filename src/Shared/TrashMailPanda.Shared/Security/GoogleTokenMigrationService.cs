using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using TrashMailPanda.Shared.Base;

namespace TrashMailPanda.Shared.Security;

/// <summary>
/// Service for migrating Google OAuth tokens from legacy "gmail_" prefix to unified "google_" prefix
/// This ensures backward compatibility while moving to the unified Google Services provider architecture
/// </summary>
public class GoogleTokenMigrationService : IGoogleTokenMigrationService
{
    private readonly ISecureStorageManager _secureStorageManager;
    private readonly ISecurityAuditLogger _securityAuditLogger;
    private readonly ILogger<GoogleTokenMigrationService> _logger;

    /// <summary>
    /// Token types that need to be migrated from gmail_ to google_ prefix
    /// </summary>
    private static readonly string[] TokenTypesToMigrate =
    {
        "access_token",
        "refresh_token",
        "token_expiry",
        "token_issued_utc",
        "token_type",
        "scopes"
    };

    public GoogleTokenMigrationService(
        ISecureStorageManager secureStorageManager,
        ISecurityAuditLogger securityAuditLogger,
        ILogger<GoogleTokenMigrationService> logger)
    {
        _secureStorageManager = secureStorageManager ?? throw new ArgumentNullException(nameof(secureStorageManager));
        _securityAuditLogger = securityAuditLogger ?? throw new ArgumentNullException(nameof(securityAuditLogger));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    /// <summary>
    /// Checks if migration is needed by looking for tokens with "gmail_" prefix
    /// </summary>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>True if migration is needed</returns>
    public async Task<Result<bool>> IsMigrationNeededAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            _logger.LogDebug("Checking if Google token migration is needed");

            foreach (var tokenType in TokenTypesToMigrate)
            {
                var legacyKey = $"gmail_{tokenType}";
                var credentialExistsResult = await _secureStorageManager.CredentialExistsAsync(legacyKey);

                if (credentialExistsResult.IsSuccess && credentialExistsResult.Value && await CredentialHasValue(legacyKey))
                {
                    _logger.LogInformation("Found legacy Gmail token: {TokenType} - migration needed", tokenType);
                    return Result<bool>.Success(true);
                }
            }

            _logger.LogDebug("No legacy Gmail tokens found - migration not needed");
            return Result<bool>.Success(false);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error checking if Google token migration is needed");
            return Result<bool>.Failure(ex.ToProviderError("Migration check failed"));
        }
    }

    /// <summary>
    /// Migrates tokens from "gmail_" prefix to "google_" prefix
    /// </summary>
    /// <param name="preserveLegacyTokens">Whether to keep original gmail_ tokens after migration</param>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>Migration result with details</returns>
    public async Task<Result<GoogleTokenMigrationResult>> MigrateTokensAsync(
        bool preserveLegacyTokens = false,
        CancellationToken cancellationToken = default)
    {
        try
        {
            _logger.LogInformation("Starting Google token migration from gmail_ to google_ prefix");

            var migrationResult = new GoogleTokenMigrationResult
            {
                StartTime = DateTime.UtcNow,
                PreserveLegacyTokens = preserveLegacyTokens
            };

            var migrationTasks = new List<Task<TokenMigrationDetail>>();

            // Migrate each token type
            foreach (var tokenType in TokenTypesToMigrate)
            {
                migrationTasks.Add(MigrateTokenAsync(tokenType, preserveLegacyTokens, cancellationToken));
            }

            var migrationDetails = await Task.WhenAll(migrationTasks);
            migrationResult.TokenMigrations.AddRange(migrationDetails);

            // Calculate results
            migrationResult.EndTime = DateTime.UtcNow;
            migrationResult.TokensMigrated = migrationResult.TokenMigrations.Count(tm => tm.IsSuccessful);
            migrationResult.TokensSkipped = migrationResult.TokenMigrations.Count(tm => tm.Skipped);
            migrationResult.TokensFailed = migrationResult.TokenMigrations.Count(tm => !tm.IsSuccessful && !tm.Skipped);
            migrationResult.IsSuccessful = migrationResult.TokensFailed == 0;

            // Log audit event
            await _securityAuditLogger.LogCredentialOperationAsync(new CredentialOperationEvent
            {
                Operation = "GoogleTokenMigration",
                CredentialKey = "gmail_to_google_migration",
                Success = migrationResult.IsSuccessful,
                ErrorMessage = migrationResult.IsSuccessful ? null : $"Failed to migrate {migrationResult.TokensFailed} tokens",
                UserContext = $"Google Token Migration Service - Migrated: {migrationResult.TokensMigrated}, Skipped: {migrationResult.TokensSkipped}, Failed: {migrationResult.TokensFailed}"
            });

            if (migrationResult.IsSuccessful)
            {
                _logger.LogInformation("Google token migration completed successfully: {Migrated} migrated, {Skipped} skipped, {Failed} failed",
                    migrationResult.TokensMigrated, migrationResult.TokensSkipped, migrationResult.TokensFailed);
            }
            else
            {
                _logger.LogWarning("Google token migration completed with errors: {Migrated} migrated, {Skipped} skipped, {Failed} failed",
                    migrationResult.TokensMigrated, migrationResult.TokensSkipped, migrationResult.TokensFailed);
            }

            return Result<GoogleTokenMigrationResult>.Success(migrationResult);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Google token migration failed");
            return Result<GoogleTokenMigrationResult>.Failure(ex.ToProviderError("Token migration failed"));
        }
    }

    /// <summary>
    /// Cleans up legacy "gmail_" prefixed tokens after successful migration
    /// </summary>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>Cleanup result</returns>
    public async Task<Result<int>> CleanupLegacyTokensAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            _logger.LogInformation("Starting cleanup of legacy Gmail tokens");

            var cleanupCount = 0;
            var errors = new List<string>();

            foreach (var tokenType in TokenTypesToMigrate)
            {
                var legacyKey = $"gmail_{tokenType}";

                try
                {
                    var credentialExistsResult = await _secureStorageManager.CredentialExistsAsync(legacyKey);
                    if (credentialExistsResult.IsSuccess && credentialExistsResult.Value)
                    {
                        await _secureStorageManager.RemoveCredentialAsync(legacyKey);
                        cleanupCount++;

                        await _securityAuditLogger.LogCredentialOperationAsync(new CredentialOperationEvent
                        {
                            Operation = "Delete",
                            CredentialKey = legacyKey,
                            Success = true,
                            UserContext = "Google Token Migration Service - Cleanup"
                        });

                        _logger.LogDebug("Removed legacy Gmail token: {TokenType}", tokenType);
                    }
                }
                catch (Exception ex)
                {
                    var errorMessage = $"Failed to remove legacy token {tokenType}: {ex.Message}";
                    errors.Add(errorMessage);
                    _logger.LogError(ex, "Failed to remove legacy Gmail token: {TokenType}", tokenType);

                    await _securityAuditLogger.LogCredentialOperationAsync(new CredentialOperationEvent
                    {
                        Operation = "Delete",
                        CredentialKey = legacyKey,
                        Success = false,
                        ErrorMessage = errorMessage,
                        UserContext = "Google Token Migration Service - Cleanup"
                    });
                }
            }

            if (errors.Any())
            {
                var combinedErrors = string.Join("; ", errors);
                _logger.LogWarning("Legacy token cleanup completed with {CleanupCount} tokens removed and {ErrorCount} errors: {Errors}",
                    cleanupCount, errors.Count, combinedErrors);
                return Result<int>.Failure(new ProcessingError($"Cleanup completed with errors: {combinedErrors}"));
            }

            _logger.LogInformation("Legacy Gmail token cleanup completed successfully: {CleanupCount} tokens removed", cleanupCount);
            return Result<int>.Success(cleanupCount);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Legacy Gmail token cleanup failed");
            return Result<int>.Failure(ex.ToProviderError("Legacy token cleanup failed"));
        }
    }

    #region Private Helper Methods

    /// <summary>
    /// Migrates a single token from gmail_ to google_ prefix
    /// </summary>
    private async Task<TokenMigrationDetail> MigrateTokenAsync(
        string tokenType,
        bool preserveLegacyTokens,
        CancellationToken cancellationToken)
    {
        var legacyKey = $"gmail_{tokenType}";
        var newKey = $"google_{tokenType}";

        var detail = new TokenMigrationDetail
        {
            TokenType = tokenType,
            LegacyKey = legacyKey,
            NewKey = newKey,
            PreserveLegacyToken = preserveLegacyTokens
        };

        try
        {
            // Check if legacy token exists and has a value
            var legacyExistsResult = await _secureStorageManager.CredentialExistsAsync(legacyKey);
            if (!legacyExistsResult.IsSuccess || !legacyExistsResult.Value || !await CredentialHasValue(legacyKey))
            {
                detail.Skipped = true;
                detail.SkipReason = "Legacy token does not exist or has no value";
                _logger.LogDebug("Skipping migration for {TokenType}: legacy token does not exist", tokenType);
                return detail;
            }

            // Check if new token already exists - don't overwrite
            var newExistsResult = await _secureStorageManager.CredentialExistsAsync(newKey);
            if (newExistsResult.IsSuccess && newExistsResult.Value && await CredentialHasValue(newKey))
            {
                detail.Skipped = true;
                detail.SkipReason = "Target token already exists - not overwriting";
                _logger.LogDebug("Skipping migration for {TokenType}: target token already exists", tokenType);
                return detail;
            }

            // Retrieve legacy token value
            var legacyValueResult = await _secureStorageManager.RetrieveCredentialAsync(legacyKey);
            if (!legacyValueResult.IsSuccess)
            {
                detail.IsSuccessful = false;
                detail.ErrorMessage = $"Failed to retrieve legacy token: {legacyValueResult.ErrorMessage}";
                return detail;
            }

            // Store token with new key
            var storeResult = await _secureStorageManager.StoreCredentialAsync(newKey, legacyValueResult.Value ?? string.Empty);
            if (!storeResult.IsSuccess)
            {
                detail.IsSuccessful = false;
                detail.ErrorMessage = $"Failed to store token with new key: {storeResult.ErrorMessage}";
                return detail;
            }

            // Remove legacy token if not preserving
            if (!preserveLegacyTokens)
            {
                await _secureStorageManager.RemoveCredentialAsync(legacyKey);
                detail.LegacyTokenRemoved = true;
            }

            detail.IsSuccessful = true;
            _logger.LogDebug("Successfully migrated token: {TokenType}", tokenType);

            return detail;
        }
        catch (Exception ex)
        {
            detail.IsSuccessful = false;
            detail.ErrorMessage = ex.Message;
            _logger.LogError(ex, "Error migrating token: {TokenType}", tokenType);
            return detail;
        }
    }

    /// <summary>
    /// Checks if a credential exists and has a non-empty value
    /// </summary>
    private async Task<bool> CredentialHasValue(string key)
    {
        try
        {
            var result = await _secureStorageManager.RetrieveCredentialAsync(key);
            return result.IsSuccess && !string.IsNullOrEmpty(result.Value);
        }
        catch
        {
            return false;
        }
    }

    #endregion
}

/// <summary>
/// Interface for Google token migration service
/// </summary>
public interface IGoogleTokenMigrationService
{
    /// <summary>
    /// Checks if migration is needed
    /// </summary>
    Task<Result<bool>> IsMigrationNeededAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Migrates tokens from gmail_ to google_ prefix
    /// </summary>
    Task<Result<GoogleTokenMigrationResult>> MigrateTokensAsync(bool preserveLegacyTokens = false, CancellationToken cancellationToken = default);

    /// <summary>
    /// Cleans up legacy tokens after migration
    /// </summary>
    Task<Result<int>> CleanupLegacyTokensAsync(CancellationToken cancellationToken = default);
}

/// <summary>
/// Result of Google token migration operation
/// </summary>
public class GoogleTokenMigrationResult
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
    /// Whether the overall migration was successful
    /// </summary>
    public bool IsSuccessful { get; set; }

    /// <summary>
    /// Number of tokens successfully migrated
    /// </summary>
    public int TokensMigrated { get; set; }

    /// <summary>
    /// Number of tokens skipped (already exist, no legacy token, etc.)
    /// </summary>
    public int TokensSkipped { get; set; }

    /// <summary>
    /// Number of tokens that failed to migrate
    /// </summary>
    public int TokensFailed { get; set; }

    /// <summary>
    /// Whether legacy tokens were preserved
    /// </summary>
    public bool PreserveLegacyTokens { get; set; }

    /// <summary>
    /// Details for each token migration attempt
    /// </summary>
    public List<TokenMigrationDetail> TokenMigrations { get; set; } = new();

    /// <summary>
    /// Duration of the migration operation
    /// </summary>
    public TimeSpan Duration => EndTime?.Subtract(StartTime) ?? TimeSpan.Zero;
}

/// <summary>
/// Details of a single token migration
/// </summary>
public class TokenMigrationDetail
{
    /// <summary>
    /// Type of token being migrated (e.g., "access_token")
    /// </summary>
    public string TokenType { get; set; } = string.Empty;

    /// <summary>
    /// Legacy storage key (gmail_ prefix)
    /// </summary>
    public string LegacyKey { get; set; } = string.Empty;

    /// <summary>
    /// New storage key (google_ prefix)
    /// </summary>
    public string NewKey { get; set; } = string.Empty;

    /// <summary>
    /// Whether the migration was successful
    /// </summary>
    public bool IsSuccessful { get; set; }

    /// <summary>
    /// Whether the migration was skipped
    /// </summary>
    public bool Skipped { get; set; }

    /// <summary>
    /// Reason for skipping migration
    /// </summary>
    public string? SkipReason { get; set; }

    /// <summary>
    /// Error message if migration failed
    /// </summary>
    public string? ErrorMessage { get; set; }

    /// <summary>
    /// Whether legacy token should be preserved
    /// </summary>
    public bool PreserveLegacyToken { get; set; }

    /// <summary>
    /// Whether legacy token was actually removed
    /// </summary>
    public bool LegacyTokenRemoved { get; set; }
}