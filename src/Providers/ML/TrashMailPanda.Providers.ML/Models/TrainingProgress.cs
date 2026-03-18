namespace TrashMailPanda.Providers.ML.Models;

/// <summary>
/// Progress update reported by the training pipeline via <see cref="IProgress{T}"/>.
/// </summary>
public sealed class TrainingProgress
{
    public string Phase { get; init; } = string.Empty;
    /// <summary>Overall completion percentage (0–100).</summary>
    public int PercentComplete { get; init; }
    public string Message { get; init; } = string.Empty;

    // Phase name constants
    public const string PhaseLoading = "Loading";
    public const string PhaseBuildingPipeline = "BuildingPipeline";
    public const string PhaseTraining = "Training";
    public const string PhaseEvaluating = "Evaluating";
}
