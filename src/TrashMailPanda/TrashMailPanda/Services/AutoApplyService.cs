using Microsoft.Extensions.Logging;
using TrashMailPanda.Models;
using TrashMailPanda.Models.Console;
using TrashMailPanda.Providers.Storage.Models;
using TrashMailPanda.Providers.Storage.Services;
using TrashMailPanda.Shared;
using TrashMailPanda.Shared.Base;

namespace TrashMailPanda.Services;

/// <summary>
/// Evaluates whether an AI-classified email should be auto-applied (confidence ≥ threshold)
/// or presented for manual review. Manages the auto-apply session log and config persistence
/// via <see cref="IConfigurationService"/> (app_config SQLite KV table).
/// </summary>
public sealed class AutoApplyService : IAutoApplyService
{
    private readonly IConfigurationService _configService;
    private readonly ILogger<AutoApplyService> _logger;

    private readonly List<AutoApplyLogEntry> _sessionLog = [];

    public AutoApplyService(IConfigurationService configService, ILogger<AutoApplyService> logger)
    {
        _configService = configService ?? throw new ArgumentNullException(nameof(configService));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    /// <inheritdoc/>
    public async Task<Result<AutoApplyConfig>> GetConfigAsync(CancellationToken ct = default)
    {
        var result = await _configService.GetConfigAsync(ct);
        if (!result.IsSuccess)
            return Result<AutoApplyConfig>.Failure(result.Error);

        var settings = result.Value.ProcessingSettings ?? new ProcessingSettings();
        return Result<AutoApplyConfig>.Success(new AutoApplyConfig
        {
            Enabled = settings.AutoApply.Enabled,
            ConfidenceThreshold = settings.AutoApply.ConfidenceThreshold,
        });
    }

    /// <inheritdoc/>
    public async Task<Result<bool>> SaveConfigAsync(AutoApplyConfig config, CancellationToken ct = default)
    {
        if (config is null)
            return Result<bool>.Failure(new ValidationError("AutoApplyConfig cannot be null."));

        var getResult = await _configService.GetConfigAsync(ct);
        if (!getResult.IsSuccess)
            return Result<bool>.Failure(getResult.Error);

        var appConfig = getResult.Value;
        var processingSettings = appConfig.ProcessingSettings ?? new ProcessingSettings();
        processingSettings.AutoApply = new AutoApplySettings
        {
            Enabled = config.Enabled,
            ConfidenceThreshold = config.ConfidenceThreshold,
        };

        var saveResult = await _configService.UpdateProcessingSettingsAsync(processingSettings, ct);
        if (saveResult.IsSuccess)
            _logger.LogInformation(
                "AutoApply config saved: Enabled={Enabled}, Threshold={Threshold:P0}",
                config.Enabled, config.ConfidenceThreshold);

        return saveResult;
    }

    /// <inheritdoc/>
    public bool ShouldAutoApply(AutoApplyConfig config, ClassificationResult classification)
    {
        if (config is null || classification is null)
            return false;
        if (!config.Enabled)
            return false;
        return classification.Confidence >= config.ConfidenceThreshold;
    }

    /// <inheritdoc/>
    public bool IsActionRedundant(string recommendedAction, EmailFeatureVector feature)
    {
        if (string.IsNullOrEmpty(recommendedAction) || feature is null)
            return false;

        return recommendedAction switch
        {
            "Archive" => feature.IsArchived == 1 && feature.IsInInbox == 0,
            "Keep" => feature.IsInInbox == 1,
            "Delete" => feature.WasInTrash == 1,
            "Spam" => feature.WasInSpam == 1,
            _ => false,
        };
    }

    /// <inheritdoc/>
    public void LogAutoApply(AutoApplyLogEntry entry)
    {
        if (entry is null)
            return;
        _sessionLog.Add(entry);
    }

    /// <inheritdoc/>
    public IReadOnlyList<AutoApplyLogEntry> GetSessionLog() => _sessionLog.AsReadOnly();

    /// <inheritdoc/>
    public AutoApplySessionSummary GetSessionSummary(int totalManuallyReviewed)
    {
        var perAction = _sessionLog
            .GroupBy(e => e.AppliedAction, StringComparer.Ordinal)
            .ToDictionary(g => g.Key, g => g.Count(), StringComparer.Ordinal);

        return new AutoApplySessionSummary(
            TotalAutoApplied: _sessionLog.Count,
            TotalManuallyReviewed: totalManuallyReviewed,
            TotalRedundant: _sessionLog.Count(e => e.WasRedundant),
            TotalUndone: _sessionLog.Count(e => e.Undone),
            PerActionCounts: perAction);
    }

    /// <inheritdoc/>
    public void ResetSession() => _sessionLog.Clear();
}
