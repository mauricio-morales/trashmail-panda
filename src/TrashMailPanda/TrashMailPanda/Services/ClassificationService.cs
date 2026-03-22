using Microsoft.Extensions.Logging;
using TrashMailPanda.Models;
using TrashMailPanda.Providers.ML;
using TrashMailPanda.Providers.ML.Models;
using TrashMailPanda.Providers.Storage.Models;
using TrashMailPanda.Shared.Base;

namespace TrashMailPanda.Services;

/// <summary>
/// Wraps <see cref="IMLModelProvider"/> to provide end-to-end email classification
/// with reasoning source attribution. Handles cold-start fallback transparently.
/// </summary>
public sealed class ClassificationService : IClassificationService
{
    private readonly IMLModelProvider _mlModelProvider;
    private readonly ILogger<ClassificationService> _logger;

    public ClassificationService(IMLModelProvider mlModelProvider, ILogger<ClassificationService> logger)
    {
        _mlModelProvider = mlModelProvider;
        _logger = logger;
    }

    /// <inheritdoc/>
    public async Task<Result<ClassificationResult>> ClassifySingleAsync(
        EmailFeatureVector input,
        CancellationToken cancellationToken = default)
    {
        var batchResult = await ClassifyBatchAsync(
            new List<EmailFeatureVector> { input },
            cancellationToken);

        if (!batchResult.IsSuccess)
            return Result<ClassificationResult>.Failure(batchResult.Error);

        return Result<ClassificationResult>.Success(batchResult.Value[0]);
    }

    /// <inheritdoc/>
    public async Task<Result<IReadOnlyList<ClassificationResult>>> ClassifyBatchAsync(
        IReadOnlyList<EmailFeatureVector> inputs,
        CancellationToken cancellationToken = default)
    {
        if (inputs.Count == 0)
            return Result<IReadOnlyList<ClassificationResult>>.Success(
                Array.Empty<ClassificationResult>());

        // Determine reasoning source once for the entire batch
        var modeResult = await _mlModelProvider.GetClassificationModeAsync(cancellationToken);
        var reasoningSource = (modeResult.IsSuccess && modeResult.Value != ClassificationMode.ColdStart)
            ? ReasoningSource.ML
            : ReasoningSource.RuleBased;

        _logger.LogDebug(
            "Classifying batch of {Count} email(s), mode={Mode}, source={Source}",
            inputs.Count, modeResult.IsSuccess ? modeResult.Value.ToString() : "unknown", reasoningSource);

        var predictionsResult = await _mlModelProvider.ClassifyActionBatchAsync(inputs, cancellationToken);

        if (!predictionsResult.IsSuccess)
        {
            _logger.LogWarning("ML batch classification failed: {Error}", predictionsResult.Error.Message);
            return Result<IReadOnlyList<ClassificationResult>>.Failure(predictionsResult.Error);
        }

        var predictions = predictionsResult.Value;
        var results = new List<ClassificationResult>(inputs.Count);

        for (int i = 0; i < inputs.Count; i++)
        {
            var prediction = predictions[i];
            results.Add(new ClassificationResult
            {
                EmailId = inputs[i].EmailId,
                PredictedAction = prediction.PredictedLabel,
                Confidence = prediction.Confidence,
                ReasoningSource = reasoningSource,
            });
        }

        return Result<IReadOnlyList<ClassificationResult>>.Success(results);
    }

    /// <inheritdoc/>
    public Task<Result<ClassificationMode>> GetClassificationModeAsync(
        CancellationToken cancellationToken = default)
        => _mlModelProvider.GetClassificationModeAsync(cancellationToken);
}
