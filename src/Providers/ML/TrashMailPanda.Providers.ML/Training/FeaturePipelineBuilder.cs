using Microsoft.ML;
using Microsoft.ML.Data;
using Microsoft.ML.Transforms;
using TrashMailPanda.Providers.ML.Models;

namespace TrashMailPanda.Providers.ML.Training;

/// <summary>
/// Builds the ML.NET feature transformation pipeline for the action classifier.
/// Pre-processes categorical strings (one-hot), text (featurize), and numeric features
/// (normalize), then concatenates everything into a single "Features" vector.
/// </summary>
public sealed class FeaturePipelineBuilder
{
    // ──────────────────────────────────────────────────────────────────────────
    // Float numeric feature columns (mapped directly from EmailFeatureVector)
    // ──────────────────────────────────────────────────────────────────────────
    private static readonly string[] NumericFeatureColumnNames =
    [
        nameof(ActionTrainingInput.SenderKnown),
        nameof(ActionTrainingInput.ContactStrength),
        nameof(ActionTrainingInput.HasListUnsubscribe),
        nameof(ActionTrainingInput.HasAttachments),
        nameof(ActionTrainingInput.HourReceived),
        nameof(ActionTrainingInput.DayOfWeek),
        nameof(ActionTrainingInput.EmailSizeLog),
        nameof(ActionTrainingInput.SubjectLength),
        nameof(ActionTrainingInput.RecipientCount),
        nameof(ActionTrainingInput.IsReply),
        nameof(ActionTrainingInput.InUserWhitelist),
        nameof(ActionTrainingInput.InUserBlacklist),
        nameof(ActionTrainingInput.LabelCount),
        nameof(ActionTrainingInput.LinkCount),
        nameof(ActionTrainingInput.ImageCount),
        nameof(ActionTrainingInput.HasTrackingPixel),
        nameof(ActionTrainingInput.UnsubscribeLinkInBody),
        nameof(ActionTrainingInput.EmailAgeDays),
        nameof(ActionTrainingInput.IsInInbox),
        nameof(ActionTrainingInput.IsStarred),
        nameof(ActionTrainingInput.IsImportant),
        nameof(ActionTrainingInput.WasInTrash),
        nameof(ActionTrainingInput.WasInSpam),
        nameof(ActionTrainingInput.IsArchived),
        nameof(ActionTrainingInput.ThreadMessageCount),
        nameof(ActionTrainingInput.SenderFrequency),
        nameof(ActionTrainingInput.IsReplied),
        nameof(ActionTrainingInput.IsForwarded),
    ];

    // Intermediate column names created by encoding/featurizing steps
    private const string SenderDomainEncoded = "SenderDomainEncoded";
    private const string SpfResultEncoded = "SpfResultEncoded";
    private const string DkimResultEncoded = "DkimResultEncoded";
    private const string DmarcResultEncoded = "DmarcResultEncoded";
    private const string SubjectTextFeaturized = "SubjectTextFeaturized";
    private const string BodyTextShortFeaturized = "BodyTextShortFeaturized";

    /// <summary>
    /// All column names that are concatenated into the "Features" vector.
    /// 28 float numerics + 4 categorical-encoded + 2 text-featurized = 34 columns.
    /// </summary>
    public static readonly string[] FeatureColumnNames =
    [
        .. NumericFeatureColumnNames,
        SenderDomainEncoded,
        SpfResultEncoded,
        DkimResultEncoded,
        DmarcResultEncoded,
        SubjectTextFeaturized,
        BodyTextShortFeaturized,
    ];

    /// <summary>
    /// Builds a complete ML.NET estimator pipeline ending with the specified trainer.
    /// Pipeline steps:
    ///   1. OneHotEncoding for categorical string columns
    ///   2. FeaturizeText for free-text columns
    ///   3. NormalizeMeanVariance on numeric float columns
    ///   4. MapValueToKey on the Label column
    ///   5. Concatenate all intermediate features into "Features"
    ///   6. Append the provided trainer estimator
    /// </summary>
    public IEstimator<ITransformer> BuildPipeline(
        MLContext mlContext,
        IEstimator<ITransformer> trainer)
    {
        // Step 1: Encode categorical columns
        var categoricalPipeline = mlContext.Transforms.Categorical.OneHotEncoding(
            new[]
            {
                new InputOutputColumnPair(SenderDomainEncoded, nameof(ActionTrainingInput.SenderDomain)),
                new InputOutputColumnPair(SpfResultEncoded, nameof(ActionTrainingInput.SpfResult)),
                new InputOutputColumnPair(DkimResultEncoded, nameof(ActionTrainingInput.DkimResult)),
                new InputOutputColumnPair(DmarcResultEncoded, nameof(ActionTrainingInput.DmarcResult)),
            });

        // Step 2: Featurize text columns
        var textPipeline = mlContext.Transforms.Text.FeaturizeText(
                SubjectTextFeaturized, nameof(ActionTrainingInput.SubjectText))
            .Append(mlContext.Transforms.Text.FeaturizeText(
                BodyTextShortFeaturized, nameof(ActionTrainingInput.BodyTextShort)));

        // Step 3: Normalize numeric floats
        var normalizePipeline = mlContext.Transforms.NormalizeMeanVariance(
            NumericFeatureColumnNames
                .Select(col => new InputOutputColumnPair(col, col))
                .ToArray());

        // Step 4: Key-map the Label column
        var labelPipeline = mlContext.Transforms.Conversion.MapValueToKey("Label");

        // Step 5: Concatenate all encoded/featurized/normalized columns into "Features"
        var concatPipeline = mlContext.Transforms.Concatenate("Features", FeatureColumnNames);

        // Build full pipeline chain
        return categoricalPipeline
            .Append(textPipeline)
            .Append(normalizePipeline)
            .Append(labelPipeline)
            .Append(concatPipeline)
            .Append(trainer)
            .Append(mlContext.Transforms.Conversion.MapKeyToValue("PredictedLabel"));
    }
}
