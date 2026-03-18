namespace TrashMailPanda.Providers.ML.Models;

/// <summary>
/// Immutable record representing a trained model version stored in ml_models.
/// </summary>
public sealed record ModelVersion
{
    public string ModelId { get; init; } = string.Empty;
    public string ModelType { get; init; } = string.Empty;
    public int Version { get; init; }
    public string TrainingDate { get; init; } = string.Empty;
    public string Algorithm { get; init; } = string.Empty;
    public int FeatureSchemaVersion { get; init; }
    public int TrainingDataCount { get; init; }
    public double Accuracy { get; init; }
    public double MacroPrecision { get; init; }
    public double MacroRecall { get; init; }
    public double MacroF1 { get; init; }
    public string PerClassMetricsJson { get; init; } = string.Empty;
    public bool IsActive { get; init; }
    public string FilePath { get; init; } = string.Empty;
    public string? Notes { get; init; }
}
