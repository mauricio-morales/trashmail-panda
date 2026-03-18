using Microsoft.ML.Data;

namespace TrashMailPanda.Providers.ML.Models;

/// <summary>
/// ML.NET output schema for action model prediction.
/// PredictedLabel contains the winning class; Score contains per-class probabilities.
/// </summary>
public class ActionPrediction
{
    /// <summary>Predicted action: "Keep", "Archive", "Delete", or "Spam".</summary>
    [ColumnName("PredictedLabel")]
    public string PredictedLabel { get; set; } = string.Empty;

    /// <summary>
    /// Per-class probability scores in order: [Keep, Archive, Delete, Spam].
    /// </summary>
    [ColumnName("Score")]
    public float[] Score { get; set; } = Array.Empty<float>();

    /// <summary>
    /// Confidence score (max score value), normalized to [0, 1].
    /// </summary>
    public float Confidence { get; set; }
}
