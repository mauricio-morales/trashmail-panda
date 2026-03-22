using TrashMailPanda.Providers.ML.Classification;
using TrashMailPanda.Providers.ML.Models;
using TrashMailPanda.Providers.ML.Versioning;
using TrashMailPanda.Providers.Storage.Models;
using TrashMailPanda.Shared.Base;

namespace TrashMailPanda.Providers.ML;

/// <summary>
/// Production implementation of <see cref="IMLModelProvider"/>.
/// Coordinates model loading (ActionClassifier), version management
/// (ModelVersionRepository), and falls back to rule-based classification
/// when no trained model has been loaded.
/// </summary>
public sealed class MLModelProvider : BaseProvider<MLModelProviderConfig>, IMLModelProvider
{
    private const string ActionModelType = "action";

    private readonly ActionClassifier _classifier;
    private readonly ModelVersionRepository _versionRepository;
    private readonly MLModelProviderConfig _config;

    public MLModelProvider(
        ActionClassifier classifier,
        ModelVersionRepository versionRepository,
        MLModelProviderConfig config,
        ILogger<MLModelProvider> logger)
        : base(logger)
    {
        _classifier = classifier;
        _versionRepository = versionRepository;
        _config = config;
    }

    // ──────────────────────────────────────────────────────────────────────────
    // BaseProvider identity
    // ──────────────────────────────────────────────────────────────────────────

    public override string Name => "MLModelProvider";
    public override string Version => "1.0.0";

    // ──────────────────────────────────────────────────────────────────────────
    // BaseProvider lifecycle
    // ──────────────────────────────────────────────────────────────────────────

    protected override async Task<Result<bool>> PerformInitializationAsync(
        MLModelProviderConfig config,
        CancellationToken cancellationToken)
    {
        try
        {
            // Ensure model directory exists
            Directory.CreateDirectory(config.ModelDirectory);
            Logger.LogInformation("MLModelProvider initializing. Model directory: {ModelDir}", config.ModelDirectory);

            // Attempt to load the active model; a cold-start (no trained model) is acceptable
            var activeResult = await _versionRepository.GetActiveVersionAsync(ActionModelType);
            if (!activeResult.IsSuccess)
            {
                Logger.LogWarning(
                    "MLModelProvider: No active action model found in DB (cold start) — rule-based fallback active. Error: {Error}",
                    activeResult.Error.Message);
                return Result<bool>.Success(true); // cold start is not a fatal error
            }

            var active = activeResult.Value;
            Logger.LogInformation(
                "MLModelProvider: Active model found in DB: modelId={ModelId} version={Version} algorithm={Algorithm} " +
                "trainedOn={TrainingDate} samples={TrainingDataCount} accuracy={Accuracy:P2} f1={MacroF1:F4} filePath={FilePath}",
                active.ModelId, active.Version, active.Algorithm, active.TrainingDate,
                active.TrainingDataCount, active.Accuracy, active.MacroF1, active.FilePath);

            var filePath = active.FilePath;
            if (string.IsNullOrEmpty(filePath) || !File.Exists(filePath))
            {
                Logger.LogWarning(
                    "MLModelProvider: Active model file not found on disk: {Path} — running in cold-start mode", filePath);
                return Result<bool>.Success(true);
            }

            var loadResult = await _classifier.LoadModelAsync(filePath, cancellationToken);
            if (!loadResult.IsSuccess)
            {
                Logger.LogWarning(
                    "MLModelProvider: Could not load active model from {Path}: {Error} — cold-start fallback active",
                    filePath, loadResult.Error.Message);
                // Non-fatal — still returns success so app can start
            }
            else
            {
                Logger.LogInformation(
                    "MLModelProvider: Model loaded successfully into classifier. ModelId={ModelId}, Path={Path}",
                    active.ModelId, filePath);
            }

            return Result<bool>.Success(true);
        }
        catch (Exception ex)
        {
            return Result<bool>.Failure(ex.ToProviderError("MLModelProvider initialization failed"));
        }
    }

    protected override Task<Result<bool>> PerformShutdownAsync(CancellationToken cancellationToken)
    {
        _classifier.UnloadModel();
        return Task.FromResult(Result<bool>.Success(true));
    }

    protected override Task<Result<HealthCheckResult>> PerformHealthCheckAsync(
        CancellationToken cancellationToken)
    {
        if (_classifier.IsLoaded)
        {
            return Task.FromResult(Result<HealthCheckResult>.Success(
                HealthCheckResult.Healthy($"Action model loaded: {_classifier.LoadedModelId}")));
        }

        return Task.FromResult(Result<HealthCheckResult>.Success(
            HealthCheckResult.Degraded(
                "No action model loaded yet — running in cold-start (rule-based fallback) mode")));
    }

    // ──────────────────────────────────────────────────────────────────────────
    // T016 — Classification
    // ──────────────────────────────────────────────────────────────────────────

    /// <inheritdoc/>
    public Task<Result<ActionPrediction>> ClassifyActionAsync(
        EmailFeatureVector input,
        CancellationToken cancellationToken = default)
    {
        if (!_classifier.IsLoaded)
        {
            // Rule-based fallback: default to "Keep"
            Logger.LogDebug(
                "ClassifyAction [{EmailId}]: classifier not loaded (cold-start) — returning fallback Keep/50%",
                input.EmailId);
            return Task.FromResult(Result<ActionPrediction>.Success(new ActionPrediction
            {
                PredictedLabel = "Keep",
                Confidence = 0.5f,
            }));
        }

        Logger.LogDebug(
            "ClassifyAction [{EmailId}]: using loaded model {ModelId}",
            input.EmailId, _classifier.LoadedModelId);
        var result = _classifier.Classify(input);
        if (result.IsSuccess)
        {
            Logger.LogDebug(
                "ClassifyAction [{EmailId}]: prediction={Label} confidence={Confidence:P0}",
                input.EmailId, result.Value.PredictedLabel, result.Value.Confidence);
        }
        return Task.FromResult(result);
    }

    /// <inheritdoc/>
    public async Task<Result<IReadOnlyList<ActionPrediction>>> ClassifyActionBatchAsync(
        IEnumerable<EmailFeatureVector> inputs,
        CancellationToken cancellationToken = default)
    {
        var results = new List<ActionPrediction>();
        foreach (var input in inputs)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var result = await ClassifyActionAsync(input, cancellationToken);
            if (!result.IsSuccess)
                return Result<IReadOnlyList<ActionPrediction>>.Failure(result.Error);
            results.Add(result.Value);
        }
        return Result<IReadOnlyList<ActionPrediction>>.Success(results);
    }

    // ──────────────────────────────────────────────────────────────────────────
    // T030 — Version queries
    // ──────────────────────────────────────────────────────────────────────────

    /// <inheritdoc/>
    public Task<Result<IReadOnlyList<ModelVersion>>> GetModelVersionsAsync(
        string modelType,
        CancellationToken cancellationToken = default)
        => _versionRepository.GetVersionsAsync(modelType);

    /// <inheritdoc/>
    public async Task<Result<ModelVersion>> GetActiveModelVersionAsync(
        string modelType,
        CancellationToken cancellationToken = default)
    {
        var result = await _versionRepository.GetActiveVersionAsync(modelType);
        if (!result.IsSuccess)
        {
            // Re-raise with a more user-friendly message if no model has been trained
            return Result<ModelVersion>.Failure(
                new ConfigurationError("No action model has been trained yet"));
        }
        return result;
    }

    // ──────────────────────────────────────────────────────────────────────────
    // T029 — Rollback
    // ──────────────────────────────────────────────────────────────────────────

    /// <inheritdoc/>
    public async Task<Result<ModelVersion>> RollbackAsync(
        string modelId,
        CancellationToken cancellationToken = default)
    {
        try
        {
            // Resolve target version
            var versionsResult = await _versionRepository.GetVersionsAsync(ActionModelType);
            if (!versionsResult.IsSuccess)
                return Result<ModelVersion>.Failure(versionsResult.Error);

            var target = versionsResult.Value.FirstOrDefault(v => v.ModelId == modelId);
            if (target is null)
            {
                return Result<ModelVersion>.Failure(new ValidationError(
                    $"Model version '{modelId}' not found in retained versions."));
            }

            if (target.IsActive)
            {
                return Result<ModelVersion>.Failure(new ValidationError(
                    $"Model version '{modelId}' is already the active model."));
            }

            // Activate in DB
            var activateResult = await _versionRepository.SetActiveAsync(modelId, ActionModelType);
            if (!activateResult.IsSuccess)
                return Result<ModelVersion>.Failure(activateResult.Error);

            // Hot-swap in-process model
            if (!string.IsNullOrEmpty(target.FilePath) && File.Exists(target.FilePath))
            {
                var loadResult = await _classifier.LoadModelAsync(target.FilePath, cancellationToken);
                if (!loadResult.IsSuccess)
                {
                    Logger.LogWarning(
                        "Rolled back DB record but could not load model file: {Error}",
                        loadResult.Error.Message);
                }
            }
            else
            {
                Logger.LogWarning(
                    "Rollback target model file missing on disk: {Path}", target.FilePath);
                _classifier.UnloadModel();
            }

            // Audit event
            var currentActiveIdBeforeRollback = versionsResult.Value
                .FirstOrDefault(v => v.IsActive)?.ModelId ?? "unknown";
            await _versionRepository.AppendEventAsync(
                "rollback", ActionModelType, modelId,
                $@"{{""from"":""{currentActiveIdBeforeRollback}"",""to"":""{modelId}""}}");

            // Return updated version record
            var updatedResult = await _versionRepository.GetActiveVersionAsync(ActionModelType);
            return updatedResult;
        }
        catch (Exception ex)
        {
            Logger.LogError(ex, "Rollback to '{ModelId}' failed", modelId);
            return Result<ModelVersion>.Failure(ex.ToProviderError("Rollback failed"));
        }
    }

    // ──────────────────────────────────────────────────────────────────────────
    // T016 — Classification mode
    // ──────────────────────────────────────────────────────────────────────────

    /// <inheritdoc/>
    public Task<Result<ClassificationMode>> GetClassificationModeAsync(
        CancellationToken cancellationToken = default)
    {
        var mode = _classifier.IsLoaded ? ClassificationMode.MlPrimary : ClassificationMode.ColdStart;
        Logger.LogDebug(
            "GetClassificationMode: mode={Mode} (classifier loaded={IsLoaded}, modelId={ModelId})",
            mode, _classifier.IsLoaded, _classifier.LoadedModelId ?? "none");
        return Task.FromResult(Result<ClassificationMode>.Success(mode));
    }
}
