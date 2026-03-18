namespace TrashMailPanda.Providers.ML.Models;

/// <summary>
/// Report produced after training or incremental update, containing evaluation metrics.
/// </summary>
public sealed class TrainingMetricsReport
{
    public string ModelId { get; init; } = string.Empty;
    public string Algorithm { get; init; } = string.Empty;
    public int TrainingDataCount { get; init; }
    public float Accuracy { get; init; }
    public float MacroPrecision { get; init; }
    public float MacroRecall { get; init; }
    public float MacroF1 { get; init; }
    public IReadOnlyDictionary<string, ClassMetrics> PerClassMetrics { get; init; }
        = new Dictionary<string, ClassMetrics>();
    /// <summary>True when MacroF1 is below the quality advisory threshold (default 0.70).</summary>
    public bool IsQualityAdvisory { get; init; }
    public TimeSpan TrainingDuration { get; init; }
}

/// <summary>Per-class precision, recall, and F1 score.</summary>
public sealed record ClassMetrics(float Precision, float Recall, float F1);
