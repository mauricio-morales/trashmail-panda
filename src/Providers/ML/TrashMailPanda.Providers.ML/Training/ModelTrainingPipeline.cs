using System.Text.Json;
using TrashMailPanda.Providers.ML.Classification;
using TrashMailPanda.Providers.ML.Models;
using TrashMailPanda.Providers.ML.Versioning;
using TrashMailPanda.Providers.Storage;
using TrashMailPanda.Providers.Storage.Models;

namespace TrashMailPanda.Providers.ML.Training;

/// <summary>
/// Orchestrates the full training pipeline:
///   data loading → validation → preprocessing → training → evaluation →
///   crash-safe file persistence → DB versioning.
/// </summary>
public sealed class ModelTrainingPipeline : IModelTrainingPipeline
{
    private const string ActionModelType = "action";

    private readonly IEmailArchiveService _archiveService;
    private readonly ModelVersionRepository _versionRepository;
    private readonly ModelVersionPruner _pruner;
    private readonly ActionModelTrainer _trainer;
    private readonly FeaturePipelineBuilder _pipelineBuilder;
    private readonly MLModelProviderConfig _config;
    private readonly IncrementalUpdateService _incrementalUpdateService;
    private readonly ILogger<ModelTrainingPipeline> _logger;

    // Model directory is resolved once and cached
    private string? _modelDirectory;

    public ModelTrainingPipeline(
        IEmailArchiveService archiveService,
        ModelVersionRepository versionRepository,
        ModelVersionPruner pruner,
        ActionModelTrainer trainer,
        FeaturePipelineBuilder pipelineBuilder,
        MLModelProviderConfig config,
        IncrementalUpdateService incrementalUpdateService,
        ILogger<ModelTrainingPipeline> logger)
    {
        _archiveService = archiveService;
        _versionRepository = versionRepository;
        _pruner = pruner;
        _trainer = trainer;
        _pipelineBuilder = pipelineBuilder;
        _config = config;
        _incrementalUpdateService = incrementalUpdateService;
        _logger = logger;
    }

    // ──────────────────────────────────────────────────────────────────────────
    // T015 core — full training
    // ──────────────────────────────────────────────────────────────────────────

    /// <inheritdoc/>
    public async Task<Result<TrainingMetricsReport>> TrainActionModelAsync(
        TrainingRequest request,
        IProgress<TrainingProgress>? progress = null,
        CancellationToken cancellationToken = default)
    {
        var startTime = DateTime.UtcNow;
        string? tempFilePath = null;
        await AppendEventAsync("training_started", null, $@"{{""reason"":""{request.TriggerReason}""}}");

        try
        {
            // Step 1 — Load and validate feature vectors
            Report(progress, TrainingProgress.PhaseLoading, 0, "Loading training data…");

            var featuresResult = await _archiveService
                .GetAllFeaturesAsync(FeatureSchema.CurrentVersion, cancellationToken);
            if (!featuresResult.IsSuccess)
                return Result<TrainingMetricsReport>.Failure(featuresResult.Error);

            var vectors = featuresResult.Value.ToList();

            // Validate minimum sample count
            if (!request.ForceRetrain && vectors.Count < _config.MinTrainingSamples)
            {
                return Result<TrainingMetricsReport>.Failure(new ValidationError(
                    $"Insufficient training data: {vectors.Count} of {_config.MinTrainingSamples} " +
                    $"labeled emails available. Collect {_config.MinTrainingSamples - vectors.Count} more."));
            }

            // Validate schema version
            var wrongVersion = vectors.FirstOrDefault(
                v => v.FeatureSchemaVersion != FeatureSchema.CurrentVersion);
            if (wrongVersion is not null)
            {
                return Result<TrainingMetricsReport>.Failure(new ValidationError(
                    $"Feature schema version mismatch: expected {FeatureSchema.CurrentVersion}, " +
                    $"found {wrongVersion.FeatureSchemaVersion}. Re-extract features before training."));
            }

            // Step 2 — Map to IDataView
            Report(progress, TrainingProgress.PhaseBuildingPipeline, 20, "Building ML pipeline…");
            cancellationToken.ThrowIfCancellationRequested();

            var mlContext = new MLContext(seed: 42);
            var trainingInputs = vectors.Select(MapToTrainingInput);
            var dataView = mlContext.Data.LoadFromEnumerable(trainingInputs);

            // Step 3 — Train
            Report(progress, TrainingProgress.PhaseTraining, 30, "Training model (this may take a while)…");

            var trainResult = await _trainer.TrainAsync(
                mlContext, dataView, _config.DominantClassImbalanceThreshold, cancellationToken);

            if (!trainResult.IsSuccess)
                return Result<TrainingMetricsReport>.Failure(trainResult.Error);

            var trainedModel = trainResult.Value.Model;
            var algorithm = trainResult.Value.Algorithm;
            var metrics = trainResult.Value.Metrics;
            Report(progress, TrainingProgress.PhaseEvaluating, 80, "Evaluating model quality…");
            cancellationToken.ThrowIfCancellationRequested();

            // Step 4 — Persist model (write to temp, then atomic move)
            var modelDir = GetModelDirectory();
            Directory.CreateDirectory(modelDir);

            var newVersion = await GetNextVersionNumberAsync(ActionModelType);
            var timestamp = DateTime.UtcNow.ToString("yyyyMMddHHmmss");
            var modelId = $"model_v{newVersion}_{timestamp}";
            var finalPath = Path.Combine(modelDir, $"{modelId}.zip");
            tempFilePath = finalPath + ".tmp";

            mlContext.Model.Save(trainedModel, dataView.Schema, tempFilePath);

            cancellationToken.ThrowIfCancellationRequested();

            // Atomic rename
            File.Move(tempFilePath, finalPath, overwrite: false);
            tempFilePath = null; // successfully moved — no cleanup needed

            // Step 5 — Insert version and activate
            var perClassJson = BuildPerClassJson(metrics);
            var modelVersion = new ModelVersion
            {
                ModelId = modelId,
                ModelType = ActionModelType,
                Version = newVersion,
                TrainingDate = DateTime.UtcNow.ToString("o"),
                Algorithm = algorithm,
                FeatureSchemaVersion = FeatureSchema.CurrentVersion,
                TrainingDataCount = vectors.Count,
                Accuracy = metrics.MacroAccuracy,
                MacroPrecision = metrics.MacroPrecision(),
                MacroRecall = metrics.MacroRecall(),
                MacroF1 = metrics.MacroF1(),
                PerClassMetricsJson = perClassJson,
                IsActive = false, // SetActiveAsync will set this
                FilePath = finalPath,
            };

            var insertResult = await _versionRepository.InsertVersionAsync(modelVersion);
            if (!insertResult.IsSuccess)
                return Result<TrainingMetricsReport>.Failure(insertResult.Error);

            var activateResult = await _versionRepository.SetActiveAsync(modelId, ActionModelType);
            if (!activateResult.IsSuccess)
                return Result<TrainingMetricsReport>.Failure(activateResult.Error);

            // Step 6 — Prune and report
            await PruneWithWarnOnFailure(ActionModelType, cancellationToken);
            await AppendEventAsync("training_completed", modelId,
                $@"{{""version"":{newVersion},""algorithm"":""{algorithm}"",""f1"":{metrics.MacroF1():F4}}}");

            Report(progress, "Completed", 100, "Training complete!");

            var report = BuildReport(modelId, algorithm, vectors.Count, metrics, DateTime.UtcNow - startTime);
            _logger.LogInformation(
                "Training complete: modelId={ModelId} version={Version} algorithm={Algorithm} f1={F1:F4}",
                modelId, newVersion, algorithm, report.MacroF1);

            return Result<TrainingMetricsReport>.Success(report);
        }
        catch (OperationCanceledException)
        {
            DeleteTempFile(tempFilePath);
            await AppendEventAsync("training_cancelled", null,
                $@"{{""reason"":""{request.TriggerReason}""}}");
            throw;
        }
        catch (Exception ex)
        {
            DeleteTempFile(tempFilePath);
            await AppendEventAsync("training_failed", null,
                $@"{{""error"":""{ex.Message.Replace("\"", "\\\"")}"",""reason"":""{request.TriggerReason}""}}");
            _logger.LogError(ex, "Training pipeline failed");
            return Result<TrainingMetricsReport>.Failure(
                new StorageError($"Training failed: {ex.Message}", InnerException: ex));
        }
    }

    // ──────────────────────────────────────────────────────────────────────────
    // T025 — IncrementalUpdateActionModelAsync (delegates to IncrementalUpdateService)
    // ──────────────────────────────────────────────────────────────────────────

    /// <inheritdoc/>
    public Task<Result<TrainingMetricsReport>> IncrementalUpdateActionModelAsync(
        IncrementalUpdateRequest request,
        IProgress<TrainingProgress>? progress = null,
        CancellationToken cancellationToken = default)
        => _incrementalUpdateService.UpdateAsync(request, progress, cancellationToken);

    // ──────────────────────────────────────────────────────────────────────────
    // T025 — ShouldRetrainAsync
    // ──────────────────────────────────────────────────────────────────────────

    /// <inheritdoc/>
    public async Task<Result<bool>> ShouldRetrainAsync(CancellationToken cancellationToken = default)
    {
        var activeResult = await _versionRepository.GetActiveVersionAsync(ActionModelType);
        if (!activeResult.IsSuccess)
        {
            // No model ever trained — retrain unconditionally
            return Result<bool>.Success(true);
        }

        var active = activeResult.Value;

        // Retrain if model is older than 7 days and some labeled data exists
        if (DateTime.TryParse(active.TrainingDate, out var trainingDate))
        {
            if (DateTime.UtcNow - trainingDate > TimeSpan.FromDays(7))
            {
                var summaryResult = await GetActionTrainingDataSummaryAsync(cancellationToken);
                if (summaryResult.IsSuccess && summaryResult.Value.Available > 0)
                    return Result<bool>.Success(true);
            }
        }

        // Retrain if ≥ 50 corrections accumulated since last training
        // (count computed lazily via archive service — corrections are indicated by UserCorrected=1)
        var featuresResult = await _archiveService.GetAllFeaturesAsync(
            FeatureSchema.CurrentVersion, cancellationToken);
        if (!featuresResult.IsSuccess)
            return Result<bool>.Failure(featuresResult.Error);

        var correctionsSince = featuresResult.Value.Count(
            v => v.UserCorrected == 1 &&
                 v.ExtractedAt > (DateTime.TryParse(active.TrainingDate, out var td) ? td : DateTime.MinValue));

        return Result<bool>.Success(correctionsSince >= 50);
    }

    // ──────────────────────────────────────────────────────────────────────────
    // T025 — GetActionTrainingDataSummaryAsync
    // ──────────────────────────────────────────────────────────────────────────

    /// <inheritdoc/>
    public async Task<Result<TrainingDataSummary>> GetActionTrainingDataSummaryAsync(
        CancellationToken cancellationToken = default)
    {
        var featuresResult = await _archiveService
            .GetAllFeaturesAsync(FeatureSchema.CurrentVersion, cancellationToken);
        if (!featuresResult.IsSuccess)
            return Result<TrainingDataSummary>.Failure(featuresResult.Error);

        var available = featuresResult.Value.Count();
        return Result<TrainingDataSummary>.Success(new TrainingDataSummary
        {
            Available = available,
            Required = _config.MinTrainingSamples,
        });
    }

    // ──────────────────────────────────────────────────────────────────────────
    // T028 — PruneOldModelsAsync (callable from MLModelProvider)
    // ──────────────────────────────────────────────────────────────────────────

    /// <inheritdoc/>
    public Task<Result<int>> PruneOldModelsAsync(
        string modelType,
        CancellationToken cancellationToken = default)
        => _pruner.PruneAsync(modelType, _config.MaxModelVersions, cancellationToken);

    // ──────────────────────────────────────────────────────────────────────────
    // Private helpers
    // ──────────────────────────────────────────────────────────────────────────

    private string GetModelDirectory()
    {
        if (_modelDirectory is not null)
            return _modelDirectory;

        _modelDirectory = _config.ModelDirectory;
        return _modelDirectory;
    }

    private async Task<int> GetNextVersionNumberAsync(string modelType)
    {
        var versionsResult = await _versionRepository.GetVersionsAsync(modelType);
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

    private static void DeleteTempFile(string? path)
    {
        if (path is null) return;
        try { File.Delete(path); }
        catch { /* best-effort */ }
    }

    private async Task PruneWithWarnOnFailure(string modelType, CancellationToken cancellationToken)
    {
        var pruneResult = await _pruner.PruneAsync(modelType, _config.MaxModelVersions, cancellationToken);
        if (!pruneResult.IsSuccess)
        {
            _logger.LogWarning(
                "Model pruning failed (non-fatal): {Error}", pruneResult.Error.Message);
        }
        else if (pruneResult.Value > 0)
        {
            _logger.LogInformation("Pruned {Count} old model version(s)", pruneResult.Value);
        }
    }

    private async Task AppendEventAsync(string eventType, string? modelId, string detailsJson)
    {
        var result = await _versionRepository.AppendEventAsync(
            eventType, ActionModelType, modelId, detailsJson);
        if (!result.IsSuccess)
            _logger.LogDebug("Could not append event {EventType}: {Error}", eventType, result.Error.Message);
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
        Label = string.Empty,
    };

    private TrainingMetricsReport BuildReport(
        string modelId, string algorithm, int count,
        Microsoft.ML.Data.MulticlassClassificationMetrics metrics,
        TimeSpan duration)
    {
        var perClass = new Dictionary<string, ClassMetrics>();
        if (metrics.PerClassLogLoss is not null)
        {
            // ML.NET doesn't expose per-class F1 directly; use macro averages as approximation
            // Per-class breakdown will be computed in post-processing if needed.
        }

        return new TrainingMetricsReport
        {
            ModelId = modelId,
            Algorithm = algorithm,
            TrainingDataCount = count,
            Accuracy = (float)metrics.MacroAccuracy,
            MacroPrecision = (float)metrics.MacroPrecision(),
            MacroRecall = (float)metrics.MacroRecall(),
            MacroF1 = (float)metrics.MacroF1(),
            PerClassMetrics = perClass,
            IsQualityAdvisory = metrics.MacroF1() < _config.QualityAdvisoryF1Threshold,
            TrainingDuration = duration,
        };
    }

    private static string BuildPerClassJson(
        Microsoft.ML.Data.MulticlassClassificationMetrics metrics)
    {
        try
        {
            var data = new
            {
                macro_accuracy = metrics.MacroAccuracy,
                micro_accuracy = metrics.MicroAccuracy,
                log_loss = metrics.LogLoss,
            };
            return JsonSerializer.Serialize(data);
        }
        catch
        {
            return "{}";
        }
    }
}
