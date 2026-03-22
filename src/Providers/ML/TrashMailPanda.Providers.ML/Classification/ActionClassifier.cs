using TrashMailPanda.Providers.ML.Models;
using TrashMailPanda.Providers.Storage.Models;

namespace TrashMailPanda.Providers.ML.Classification;

/// <summary>
/// Concrete implementation of <see cref="IActionClassifier"/>.
/// Wraps a ML.NET <c>PredictionEngine</c> that is NOT thread-safe.
/// Callers are responsible for single-threaded access.
/// </summary>
public sealed class ActionClassifier : IActionClassifier, IDisposable
{
    private readonly MLContext _mlContext;
    private readonly ILogger<ActionClassifier> _logger;

    // Mutable state guarded by caller's single-thread requirement
    private PredictionEngine<ActionTrainingInput, ActionPrediction>? _engine;
    private ITransformer? _model;
    private string? _loadedModelId;
    private bool _disposed;

    public bool IsLoaded => _engine is not null;
    public string? LoadedModelId => _loadedModelId;

    public ActionClassifier(MLContext mlContext, ILogger<ActionClassifier> logger)
    {
        _mlContext = mlContext;
        _logger = logger;
    }

    // ──────────────────────────────────────────────────────────────────────────
    // Lifecycle
    // ──────────────────────────────────────────────────────────────────────────

    /// <inheritdoc/>
    public async Task<Result<bool>> LoadModelAsync(
        string modelFilePath,
        CancellationToken cancellationToken = default)
    {
        try
        {
            if (!File.Exists(modelFilePath))
            {
                return Result<bool>.Failure(new ConfigurationError(
                    $"Model file not found: {modelFilePath}"));
            }

            // Load from disk on a background thread (avoids blocking the caller)
            var model = await Task.Run(() =>
            {
                _mlContext.Model.Load(modelFilePath, out _);
                // Re-load into variable we can capture
                return _mlContext.Model.Load(modelFilePath, out _);
            }, cancellationToken);

            // Dispose old engine before replacing
            DisposeEngine();

            _model = model;
            _engine = _mlContext.Model.CreatePredictionEngine<ActionTrainingInput, ActionPrediction>(model);

            // Use the file name (without extension) as a human-readable model ID if none provided
            _loadedModelId = Path.GetFileNameWithoutExtension(modelFilePath);

            _logger.LogInformation("Action classifier loaded: {ModelId} from {Path}", _loadedModelId, modelFilePath);
            return Result<bool>.Success(true);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to load action classifier from {Path}", modelFilePath);
            return Result<bool>.Failure(new InitializationError(
                $"Failed to load model: {ex.Message}", InnerException: ex));
        }
    }

    /// <inheritdoc/>
    public void UnloadModel()
    {
        DisposeEngine();
        _loadedModelId = null;
        _logger.LogInformation("Action classifier model unloaded");
    }

    // ──────────────────────────────────────────────────────────────────────────
    // Classification (synchronous per IActionClassifier contract)
    // ──────────────────────────────────────────────────────────────────────────

    /// <inheritdoc/>
    public Result<ActionPrediction> Classify(EmailFeatureVector input)
    {
        if (_engine is null)
        {
            return Result<ActionPrediction>.Failure(new InitializationError(
                "Action classifier model not loaded"));
        }

        try
        {
            var trainingInput = MapToTrainingInput(input);
            var prediction = _engine.Predict(trainingInput);
            prediction.Confidence = prediction.Score?.Length > 0 ? prediction.Score.Max() : 0f;

            // Log per-class scores so we can diagnose whether the model is biased
            if (prediction.Score is { Length: > 0 })
            {
                var scoresStr = string.Join(", ", prediction.Score.Select((s, i) => $"[{i}]={s:F3}"));
                _logger.LogDebug(
                    "Classifier [{EmailId}] model={ModelId} → label={Label} confidence={Confidence:P0} scores=[{Scores}]",
                    input.EmailId, _loadedModelId, prediction.PredictedLabel, prediction.Confidence, scoresStr);
            }
            else
            {
                _logger.LogDebug(
                    "Classifier [{EmailId}] model={ModelId} → label={Label} confidence={Confidence:P0} (no score array)",
                    input.EmailId, _loadedModelId, prediction.PredictedLabel, prediction.Confidence);
            }

            return Result<ActionPrediction>.Success(prediction);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Classification failed for EmailId={EmailId}", input.EmailId);
            return Result<ActionPrediction>.Failure(new ProcessingError(
                $"Classification failed: {ex.Message}", InnerException: ex));
        }
    }

    /// <inheritdoc/>
    public Result<IReadOnlyList<ActionPrediction>> ClassifyBatch(
        IEnumerable<EmailFeatureVector> inputs)
    {
        if (_engine is null)
        {
            return Result<IReadOnlyList<ActionPrediction>>.Failure(new InitializationError(
                "Action classifier model not loaded"));
        }

        try
        {
            var results = new List<ActionPrediction>();
            foreach (var input in inputs)
            {
                var single = Classify(input);
                if (!single.IsSuccess)
                    return Result<IReadOnlyList<ActionPrediction>>.Failure(single.Error);
                results.Add(single.Value);
            }
            return Result<IReadOnlyList<ActionPrediction>>.Success(results);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Batch classification failed");
            return Result<IReadOnlyList<ActionPrediction>>.Failure(new ProcessingError(
                $"Batch classification failed: {ex.Message}", InnerException: ex));
        }
    }

    // ──────────────────────────────────────────────────────────────────────────
    // Mapping
    // ──────────────────────────────────────────────────────────────────────────

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
        Weight = 1.0f,   // Weight not used at inference time; set a neutral value
        Label = string.Empty, // No ground truth label at inference time
    };

    // ──────────────────────────────────────────────────────────────────────────
    // Disposal
    // ──────────────────────────────────────────────────────────────────────────

    private void DisposeEngine()
    {
        if (_engine is IDisposable disposableEngine)
            disposableEngine.Dispose();
        _engine = null;
        _model = null;
    }

    public void Dispose()
    {
        if (_disposed)
            return;
        DisposeEngine();
        _disposed = true;
    }
}
