namespace TrashMailPanda.Providers.ML.Models;

/// <summary>
/// Input to <see cref="IModelTrainingPipeline.TrainActionModelAsync"/>.
/// </summary>
public sealed class TrainingRequest
{
    public string TriggerReason { get; init; } = string.Empty;
    /// <summary>When true, bypasses the minimum sample count check.</summary>
    public bool ForceRetrain { get; init; }
}
