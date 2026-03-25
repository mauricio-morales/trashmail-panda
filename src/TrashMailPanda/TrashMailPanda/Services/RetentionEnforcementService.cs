using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using TrashMailPanda.Models;
using TrashMailPanda.Providers.Storage;
using TrashMailPanda.Providers.Storage.Models;
using TrashMailPanda.Providers.Storage.Services;
using TrashMailPanda.Shared;
using TrashMailPanda.Shared.Base;
using TrashMailPanda.Shared.Labels;

namespace TrashMailPanda.Services;

/// <summary>
/// Scans <c>email_features</c> for archived emails whose time-bounded retention threshold
/// has elapsed and deletes them from Gmail.
/// <para>
/// IMPORTANT: <c>training_label</c> is NEVER modified. The content-based label is
/// preserved for ML training integrity.
/// </para>
/// </summary>
public sealed class RetentionEnforcementService : IRetentionEnforcementService
{
    private readonly IEmailArchiveService _archiveService;
    private readonly IEmailProvider _emailProvider;
    private readonly IConfigurationService _configService;
    private readonly RetentionEnforcementOptions _options;
    private readonly ILogger<RetentionEnforcementService> _logger;

    public RetentionEnforcementService(
        IEmailArchiveService archiveService,
        IEmailProvider emailProvider,
        IConfigurationService configService,
        IOptions<RetentionEnforcementOptions> options,
        ILogger<RetentionEnforcementService> logger)
    {
        _archiveService = archiveService ?? throw new ArgumentNullException(nameof(archiveService));
        _emailProvider = emailProvider ?? throw new ArgumentNullException(nameof(emailProvider));
        _configService = configService ?? throw new ArgumentNullException(nameof(configService));
        _options = options?.Value ?? throw new ArgumentNullException(nameof(options));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    /// <inheritdoc />
    public async Task<Result<RetentionScanResult>> RunScanAsync(
        CancellationToken cancellationToken = default)
    {
        _logger.LogInformation("RetentionEnforcementService: starting scan");

        // 1. Fetch all archived feature vectors
        var featuresResult = await _archiveService.GetAllFeaturesAsync(null, cancellationToken);
        if (!featuresResult.IsSuccess)
        {
            _logger.LogError("Retention scan failed to fetch features: {Error}",
                featuresResult.Error.Message);
            return Result<RetentionScanResult>.Failure(featuresResult.Error);
        }

        // 2. Filter to time-bounded archived emails with a known received date
        var candidates = featuresResult.Value
            .Where(f =>
                f.IsArchived == 1 &&
                f.ReceivedDateUtc.HasValue &&
                LabelThresholds.IsTimeBounded(f.TrainingLabel ?? string.Empty))
            .ToList();

        var toDelete = new List<string>();
        var skipped = 0;

        foreach (var feature in candidates)
        {
            if (!LabelThresholds.TryGetThreshold(feature.TrainingLabel!, out var thresholdDays))
                continue;

            var elapsed = (DateTime.UtcNow - feature.ReceivedDateUtc!.Value).TotalDays;
            if (elapsed >= thresholdDays)
                toDelete.Add(feature.EmailId);
            else
                skipped++;
        }

        _logger.LogInformation(
            "Retention scan: {Scanned} candidates, {ToDelete} to delete, {Skipped} not yet expired",
            candidates.Count, toDelete.Count, skipped);

        // 3. Delete expired emails from Gmail
        var failedIds = new List<string>();
        var deletedCount = 0;

        foreach (var emailId in toDelete)
        {
            if (cancellationToken.IsCancellationRequested)
                break;

            var deleteResult = await _emailProvider.BatchModifyAsync(new BatchModifyRequest
            {
                EmailIds = [emailId],
                AddLabelIds = ["TRASH"],
                RemoveLabelIds = ["INBOX"],
            });

            if (deleteResult.IsSuccess)
            {
                deletedCount++;
                _logger.LogDebug("Retention scan: deleted {EmailId}", emailId);
            }
            else
            {
                failedIds.Add(emailId);
                _logger.LogWarning("Retention scan: failed to delete {EmailId}: {Error}",
                    emailId, deleteResult.Error.Message);
            }
        }

        // 4. Persist last_scan_utc (even on partial success)
        await PersistLastScanUtcAsync(cancellationToken);

        var scanResult = new RetentionScanResult
        {
            ScannedCount = candidates.Count,
            DeletedCount = deletedCount,
            SkippedCount = skipped,
            FailedIds = failedIds,
            RanAtUtc = DateTime.UtcNow,
        };

        _logger.LogInformation(
            "Retention scan complete: {Deleted} deleted, {Skipped} skipped, {Failed} failed",
            scanResult.DeletedCount, scanResult.SkippedCount, scanResult.FailedIds.Count);

        return Result<RetentionScanResult>.Success(scanResult);
    }

    /// <inheritdoc />
    public async Task<Result<DateTime?>> GetLastScanTimeAsync(
        CancellationToken cancellationToken = default)
    {
        var configResult = await _configService.GetConfigAsync(cancellationToken);
        if (!configResult.IsSuccess)
            return Result<DateTime?>.Failure(configResult.Error);

        var lastScan = configResult.Value.ProcessingSettings?.Retention?.LastScanUtc;
        return Result<DateTime?>.Success(lastScan);
    }

    /// <inheritdoc />
    public async Task<Result<bool>> ShouldPromptAsync(
        CancellationToken cancellationToken = default)
    {
        var lastScanResult = await GetLastScanTimeAsync(cancellationToken);
        if (!lastScanResult.IsSuccess)
            return Result<bool>.Failure(lastScanResult.Error);

        if (lastScanResult.Value is null)
        {
            // Never scanned — always prompt
            return Result<bool>.Success(true);
        }

        var daysSinceLast = (DateTime.UtcNow - lastScanResult.Value.Value).TotalDays;
        var shouldPrompt = daysSinceLast >= _options.PromptThresholdDays;
        return Result<bool>.Success(shouldPrompt);
    }

    // ── Private helpers ───────────────────────────────────────────────────────

    private async Task PersistLastScanUtcAsync(CancellationToken cancellationToken)
    {
        try
        {
            var configResult = await _configService.GetConfigAsync(cancellationToken);
            if (!configResult.IsSuccess)
            {
                _logger.LogWarning("Could not read config to persist last_scan_utc: {Error}",
                    configResult.Error.Message);
                return;
            }

            var config = configResult.Value;
            var settings = config.ProcessingSettings ?? new ProcessingSettings();
            settings.Retention ??= new RetentionSettings();
            settings.Retention.LastScanUtc = DateTime.UtcNow;

            var updateResult = await _configService.UpdateProcessingSettingsAsync(
                settings, cancellationToken);

            if (!updateResult.IsSuccess)
            {
                _logger.LogWarning("Failed to persist last_scan_utc: {Error}",
                    updateResult.Error.Message);
            }
        }
        catch (Exception ex)
        {
            // Non-fatal — the scan itself succeeded; only persistence failed
            _logger.LogWarning(ex, "Exception while persisting last_scan_utc");
        }
    }
}
