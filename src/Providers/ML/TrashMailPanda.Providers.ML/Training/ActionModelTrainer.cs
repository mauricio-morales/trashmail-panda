using Microsoft.ML;
using Microsoft.ML.Data;
using Microsoft.ML.Trainers;
using Microsoft.ML.Trainers.LightGbm;
using TrashMailPanda.Providers.ML.Models;

namespace TrashMailPanda.Providers.ML.Training;

/// <summary>
/// Trains a multiclass action classifier using ML.NET.
/// Automatically selects trainer algorithm based on class distribution balance
/// and applies inverse-frequency class weights to mitigate imbalanced data.
/// </summary>
public sealed class ActionModelTrainer
{
    private readonly ILogger<ActionModelTrainer> _logger;

    public ActionModelTrainer(ILogger<ActionModelTrainer> logger)
    {
        _logger = logger;
    }

    /// <summary>
    /// Trains a multiclass action model on the supplied data.
    /// </summary>
    /// <param name="mlContext">Shared MLContext for deterministic seeding.</param>
    /// <param name="trainingData">IDataView containing <see cref="ActionTrainingInput"/> rows.</param>
    /// <param name="dominantClassImbalanceThreshold">
    ///   If the largest class proportion exceeds this ratio, LightGbm is selected;
    ///   otherwise, SdcaMaximumEntropy is used.
    /// </param>
    /// <param name="cancellationToken">Cancellation support.</param>
    public async Task<Result<(ITransformer Model, string Algorithm, MulticlassClassificationMetrics Metrics)>>
        TrainAsync(
            MLContext mlContext,
            IDataView trainingData,
            double dominantClassImbalanceThreshold,
            CancellationToken cancellationToken = default)
    {
        try
        {
            // Collect Label values to determine class distribution
            var labelsResult = await CollectLabelsAsync(trainingData, cancellationToken);
            if (!labelsResult.IsSuccess)
                return Result<(ITransformer Model, string Algorithm, MulticlassClassificationMetrics Metrics)>.Failure(labelsResult.Error);

            var labelCounts = labelsResult.Value;

            if (labelCounts.Count < 2)
            {
                return Result<(ITransformer Model, string Algorithm, MulticlassClassificationMetrics Metrics)>.Failure(
                    new ValidationError($"Training requires at least 2 distinct action classes; found {labelCounts.Count}."));
            }

            // Log class distribution so we can see if data is imbalanced
            var totalSamples = labelCounts.Values.Sum();
            foreach (var (label, count) in labelCounts.OrderByDescending(kv => kv.Value))
            {
                _logger.LogInformation(
                    "Training data class: {Label} = {Count} samples ({Pct:P1})",
                    label, count, (double)count / totalSamples);
            }
            var weightedData = AddInverseFrequencyWeights(mlContext, trainingData, labelCounts);

            // Select trainer based on class imbalance
            var maxClassCount = labelCounts.Values.Max();
            var dominantRatio = (double)maxClassCount / totalSamples;

            string algorithm;
            IEstimator<ITransformer> trainer;
            if (dominantRatio > dominantClassImbalanceThreshold)
            {
                algorithm = "LightGbm";
                trainer = mlContext.MulticlassClassification.Trainers.LightGbm(
                    labelColumnName: "Label",
                    featureColumnName: "Features",
                    exampleWeightColumnName: "Weight");
                _logger.LogInformation(
                    "Selected LightGbm trainer (dominant class ratio {Ratio:P1} > threshold {Threshold:P1})",
                    dominantRatio, dominantClassImbalanceThreshold);
            }
            else
            {
                algorithm = "SdcaMaximumEntropy";
                trainer = mlContext.MulticlassClassification.Trainers.SdcaMaximumEntropy(
                    labelColumnName: "Label",
                    featureColumnName: "Features",
                    exampleWeightColumnName: "Weight");
                _logger.LogInformation(
                    "Selected SdcaMaximumEntropy trainer (dominant class ratio {Ratio:P1} \u2264 threshold {Threshold:P1})",
                    dominantRatio, dominantClassImbalanceThreshold);
            }

            // Split 80/20 for training/validation
            var dataSplit = mlContext.Data.TrainTestSplit(weightedData, testFraction: 0.2);

            // Build pipeline and train
            var pipelineBuilder = new FeaturePipelineBuilder();
            var pipeline = pipelineBuilder.BuildPipeline(mlContext, trainer);

            _logger.LogInformation("Training model with algorithm={Algorithm}, samples={Total}", algorithm, totalSamples);

            var trainedModel = await Task.Run(
                () => pipeline.Fit(dataSplit.TrainSet),
                cancellationToken);

            // Evaluate
            var predictions = trainedModel.Transform(dataSplit.TestSet);
            var metrics = mlContext.MulticlassClassification.Evaluate(
                predictions,
                labelColumnName: "Label",
                predictedLabelColumnName: "PredictedLabel");

            _logger.LogInformation(
                "Training complete: MacroAccuracy={Macro:P2}, MicroAccuracy={Micro:P2}, LogLoss={LogLoss:F4}",
                metrics.MacroAccuracy, metrics.MicroAccuracy, metrics.LogLoss);

            return Result<(ITransformer Model, string Algorithm, MulticlassClassificationMetrics Metrics)>.Success(
                (trainedModel, algorithm, metrics));
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Unexpected error during model training");
            return Result<(ITransformer Model, string Algorithm, MulticlassClassificationMetrics Metrics)>.Failure(
                new StorageError($"Training failed: {ex.Message}", InnerException: ex));
        }
    }

    // ──────────────────────────────────────────────────────────────────────────
    // Private helpers
    // ──────────────────────────────────────────────────────────────────────────

    private static Task<Result<Dictionary<string, long>>> CollectLabelsAsync(
        IDataView dataView,
        CancellationToken cancellationToken)
    {
        return Task.Run(() =>
        {
            try
            {
                var labelColumn = dataView.GetColumn<string>(dataView.Schema["Label"]);
                var counts = new Dictionary<string, long>(StringComparer.OrdinalIgnoreCase);
                foreach (var label in labelColumn)
                {
                    if (string.IsNullOrEmpty(label))
                        continue;
                    counts.TryGetValue(label, out var c);
                    counts[label] = c + 1;
                }
                return Result<Dictionary<string, long>>.Success(counts);
            }
            catch (Exception ex)
            {
                return Result<Dictionary<string, long>>.Failure(
                    new ValidationError($"Failed to read Label column: {ex.Message}"));
            }
        }, cancellationToken);
    }

    private static IDataView AddInverseFrequencyWeights(
        MLContext mlContext,
        IDataView dataView,
        Dictionary<string, long> labelCounts)
    {
        var totalSamples = labelCounts.Values.Sum();
        var classCount = labelCounts.Count;

        // Inverse frequency weight per class = totalSamples / (classCount * classSamples)
        var weights = labelCounts.ToDictionary(
            kvp => kvp.Key,
            kvp => (float)(totalSamples / ((double)classCount * kvp.Value)),
            StringComparer.OrdinalIgnoreCase);

        // Map Label \u2192 Weight using a custom mapping
        return mlContext.Transforms.CustomMapping<LabelRow, WeightRow>(
                (input, output) =>
                {
                    output.Weight = weights.TryGetValue(input.Label, out var w) ? w : 1.0f;
                },
                contractName: null)
            .Fit(dataView)
            .Transform(dataView);
    }

    // Lightweight helper types for the CustomMapping transform
    private sealed class LabelRow
    {
        [ColumnName("Label")]
        public string Label { get; set; } = string.Empty;
    }

    private sealed class WeightRow
    {
        [ColumnName("Weight")]
        public float Weight { get; set; }
    }
}
