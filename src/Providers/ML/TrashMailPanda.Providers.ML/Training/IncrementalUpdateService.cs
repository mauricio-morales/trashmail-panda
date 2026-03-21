using Microsoft.Extensions.Logging;
using Microsoft.ML;
using TrashMailPanda.Providers.ML.Models;
using TrashMailPanda.Providers.ML.Versioning;
using TrashMailPanda.Providers.Storage;
using TrashMailPanda.Providers.Storage.Models;

namespace TrashMailPanda.Providers.ML.Training;

/// <summary>
/// Handles incremental model updates when sufficient new user corrections have accumulated since
/// the last full training run.  Logic mirrors <see cref="ModelTrainingPipeline.TrainActionModelAsync"/>
/// but operates on a merged + deduplicated feature set rather than a full data reload.
/// </summary>
public sealed class IncrementalUpdateService
{
    private const string ActionModelType = "action";

    private readonly IEmailArchiveService _archiveService;
    private readonly ModelVersionRepository _versionRepository;
    private readonly ModelVersionPruner _pruner;
    private readonly ActionModelTrainer _trainer;
    private readonly MLModelProviderConfig _config;
    private readonly ILogger<IncrementalUpdateService> _logger;

    public IncrementalUpdateService(
        IEmailArchiveService archiveService,
        ModelVersionRepository versionRepository,
        ModelVersionPruner pruner,
        ActionModelTrainer trainer,
        MLModelProviderConfig config,
        ILogger<IncrementalUpdateService> logger)
    {
        _archiveService = archiveService;
        _versionRepository = versionRepository;
        _pruner = pruner;
        _trainer = trainer;
        _config = config;
        _logger = logger;
    }

    // ──────────────────────────────────────────────────────────────────────────
    // T024 — UpdateAsync
    // ──────────────────────────────────────────────────────────────────────────

    /// <summary>
    /// Attempts an incremental model update.  Returns <see cref="ValidationError"/> when fewer
    /// than <see cref="IncrementalUpdateRequest.MinNewCorrections"/> corrections have been made
    /// since the last training date.
    /// </summary>
    public async Task<Result<TrainingMetricsReport>> UpdateAsync(
        IncrementalUpdateRequest request,
        IProgress<TrainingProgress>? progress = null,
        CancellationToken cancellationToken = default)
    {
        var startTime = DateTime.UtcNow;

        // ── Step 1: determine last training date ──────────────────────────────
        var lastTrainingUtc = await ResolveLastTrainingDateAsync(request);

        // ── Step 2: load all feature vectors ─────────────────────────────────
        Report(progress, TrainingProgress.PhaseLoading, 0, "Loading feature vectors…");
        var featuresResult = await _archiveService
            .GetAllFeaturesAsync(FeatureSchema.CurrentVersion, cancellationToken);
        if (!featuresResult.IsSuccess)
            return Result<TrainingMetricsReport>.Failure(featuresResult.Error);

        var allVectors = featuresResult.Value.ToList();

        // ── Step 3: count new corrections since last training ─────────────────
        var newCorrections = allVectors.Count(
            v => v.UserCorrected == 1 && v.ExtractedAt > lastTrainingUtc);

        if (newCorrections < request.MinNewCorrections)
        {
            return Result<TrainingMetricsReport>.Failure(new ValidationError(
                $"Insufficient new corrections: {newCorrections} of {request.MinNewCorrections} required. " +
                $"Wait for more user feedback before updating."));
        }

        // ── Step 4: merge + deduplicate (prefer UserCorrected=1) ─────────────
        var merged = allVectors
            .GroupBy(v => v.EmailId)
            .Select(g => g.OrderByDescending(v => v.UserCorrected).First())
            .ToList();

        // ── Step 5: train on merged dataset ──────────────────────────────────
        Report(progress, TrainingProgress.PhaseBuildingPipeline, 20, "Building ML pipeline…");
        cancellationToken.ThrowIfCancellationRequested();

        var mlContext = new MLContext(seed: 42);
        var dataView = mlContext.Data.LoadFromEnumerable(merged.Select(MapToTrainingInput));

        Report(progress, TrainingProgress.PhaseTraining, 30, "Training model on merged dataset…");
        var trainResult = await _trainer.TrainAsync(
            mlContext, dataView, _config.DominantClassImbalanceThreshold, cancellationToken);

        if (!trainResult.IsSuccess)
            return Result<TrainingMetricsReport>.Failure(trainResult.Error);

        var (trainedModel, algorithm, metrics) = trainResult.Value;
        Report(progress, TrainingProgress.PhaseEvaluating, 80, "Evaluating and persisting model…");
        cancellationToken.ThrowIfCancellationRequested();

        // ── Step 6: persist new version ───────────────────────────────────────
        var newVersionNum = await GetNextVersionNumberAsync();
        var timestamp = DateTime.UtcNow.ToString("yyyyMMddHHmmss");
        var modelId = $"model_v{newVersionNum}_{timestamp}";

        var modelDir = _config.ModelDirectory;
        Directory.CreateDirectory(modelDir);
        var finalPath = Path.Combine(modelDir, $"{modelId}.zip");
        var tempPath = finalPath + ".tmp";

        mlContext.Model.Save(trainedModel, dataView.Schema, tempPath);
        cancellationToken.ThrowIfCancellationRequested();

        File.Move(tempPath, finalPath, overwrite: false);

        var modelVersion = new ModelVersion
        {
            ModelId = modelId,
            ModelType = ActionModelType,
            Version = newVersionNum,
            TrainingDate = DateTime.UtcNow.ToString("o"),
            Algorithm = algorithm,
            FeatureSchemaVersion = FeatureSchema.CurrentVersion,
            TrainingDataCount = merged.Count,
            Accuracy = metrics.MacroAccuracy,
            MacroPrecision = metrics.MacroPrecision(),
            MacroRecall = metrics.MacroRecall(),
            MacroF1 = metrics.MacroF1(),
            PerClassMetricsJson = "{}",
            IsActive = false,
            FilePath = finalPath,
        };

        var insertResult = await _versionRepository.InsertVersionAsync(modelVersion);
        if (!insertResult.IsSuccess)
        {
            TryDeleteFile(finalPath);
            return Result<TrainingMetricsReport>.Failure(insertResult.Error);
        }

        var activateResult = await _versionRepository.SetActiveAsync(modelId, ActionModelType);
        if (!activateResult.IsSuccess)
            return Result<TrainingMetricsReport>.Failure(activateResult.Error);

        // Pruning is non-fatal
        var pruneResult = await _pruner.PruneAsync(ActionModelType, _config.MaxModelVersions, cancellationToken);
        if (!pruneResult.IsSuccess)
            _logger.LogWarning("Pruning failed after incremental update: {Error}", pruneResult.Error.Message);
        else
            _logger.LogInformation("Pruned {Count} old model(s) after incremental update", pruneResult.Value);

        await _versionRepository.AppendEventAsync(
            "incremental_update_completed",
            ActionModelType,
            modelId,
            $@"{{""reason"":""{request.TriggerReason}"",""newCorrections"":{newCorrections},""totalVectors"":{merged.Count},""algorithm"":""{algorithm}"",""f1"":{metrics.MacroF1():F4}}}");

        Report(progress, "Completed", 100, "Incremental update complete!");

        var duration = DateTime.UtcNow - startTime;
        var report = new TrainingMetricsReport
        {
            ModelId = modelId,
            Algorithm = algorithm,
            TrainingDataCount = merged.Count,
            Accuracy = (float)metrics.MacroAccuracy,
            MacroPrecision = (float)metrics.MacroPrecision(),
            MacroRecall = (float)metrics.MacroRecall(),
            MacroF1 = (float)metrics.MacroF1(),
            IsQualityAdvisory = metrics.MacroF1() < 0.70,
            TrainingDuration = duration,
        };

        _logger.LogInformation(
            "Incremental update complete: modelId={ModelId} version={Version} algorithm={Algorithm} f1={F1:F4} newCorrections={NewCorrections}",
            modelId, newVersionNum, algorithm, report.MacroF1, newCorrections);

        return Result<TrainingMetricsReport>.Success(report);
    }

    // ──────────────────────────────────────────────────────────────────────────
    // Private helpers
    // ──────────────────────────────────────────────────────────────────────────

    private async Task<DateTime> ResolveLastTrainingDateAsync(IncrementalUpdateRequest request)
    {
        if (request.LastTrainingDate.HasValue)
            return request.LastTrainingDate.Value.UtcDateTime;

        var activeResult = await _versionRepository.GetActiveVersionAsync(ActionModelType);
        if (activeResult.IsSuccess &&
            DateTime.TryParse(activeResult.Value.TrainingDate, null,
                System.Globalization.DateTimeStyles.RoundtripKind, out var parsed))
        {
            return parsed;
        }

        return DateTime.MinValue; // No prior model — all corrections count
    }

    private async Task<int> GetNextVersionNumberAsync()
    {
        var versionsResult = await _versionRepository.GetVersionsAsync(ActionModelType);
        if (!versionsResult.IsSuccess || versionsResult.Value.Count == 0)
            return 1;
        return versionsResult.Value.Max(v => v.Version) + 1;
    }

    private static void Report(IProgress<TrainingProgress>? progress, string phase, int pct, string message)
    {
        progress?.Report(new TrainingProgress
        {
            Phase = phase,
            PercentComplete = pct,
            Message = message,
        });
    }

    private static void TryDeleteFile(string? path)
    {
        if (path is null) return;
        try { File.Delete(path); }
        catch { /* best-effort cleanup */ }
    }

    private static ActionTrainingInput MapToTrainingInput(EmailFeatureVector v) => new()
    {
        SenderKnown = v.SenderKnown,
        ContactStrength = v.ContactStrength,
        HasListUnsubscribe = v.HasListUnsubscribe,
        HasAttachments = v.HasAttachments,
        HourReceived = v.HourReceived,
        DayOfWeek = v.DayOfWeek,
        EmailSizeLog = v.EmailSizeLog,
        SubjectLength = v.SubjectLength,
        RecipientCount = v.RecipientCount,
        IsReply = v.IsReply,
        InUserWhitelist = v.InUserWhitelist,
        InUserBlacklist = v.InUserBlacklist,
        LabelCount = v.LabelCount,
        LinkCount = v.LinkCount,
        ImageCount = v.ImageCount,
        HasTrackingPixel = v.HasTrackingPixel,
        UnsubscribeLinkInBody = v.UnsubscribeLinkInBody,
        EmailAgeDays = v.EmailAgeDays,
        IsInInbox = v.IsInInbox,
        IsStarred = v.IsStarred,
        IsImportant = v.IsImportant,
        WasInTrash = v.WasInTrash,
        WasInSpam = v.WasInSpam,
        IsArchived = v.IsArchived,
        ThreadMessageCount = v.ThreadMessageCount,
        SenderFrequency = v.SenderFrequency,
        IsReplied = v.IsReplied,
        IsForwarded = v.IsForwarded,
        SenderDomain = v.SenderDomain,
        SpfResult = v.SpfResult,
        DkimResult = v.DkimResult,
        DmarcResult = v.DmarcResult,
        SubjectText = v.SubjectText ?? string.Empty,
        BodyTextShort = v.BodyTextShort ?? string.Empty,
        Weight = 1.0f,
        Label = v.TrainingLabel ?? InferLabelFromFlags(v),
    };

    /// <summary>
    /// Infers a training label from boolean feature flags when no explicit TrainingLabel is set.
    /// Priority: Spam > Delete > Archive > Keep.
    /// </summary>
    private static string InferLabelFromFlags(EmailFeatureVector v)
    {
        if (v.WasInSpam == 1) return "Spam";
        if (v.WasInTrash == 1) return "Delete";
        if (v.IsArchived == 1) return "Archive";
        return "Keep";
    }
}
