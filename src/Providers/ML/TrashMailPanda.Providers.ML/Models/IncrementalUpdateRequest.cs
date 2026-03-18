namespace TrashMailPanda.Providers.ML.Models;

/// <summary>
/// Input to <see cref="IModelTrainingPipeline.IncrementalUpdateActionModelAsync"/>.
/// </summary>
public sealed class IncrementalUpdateRequest
{
    /// <summary>Minimum number of new user corrections required to trigger an update (default 50).</summary>
    public int MinNewCorrections { get; init; } = 50;
    public string TriggerReason { get; init; } = string.Empty;
    public DateTimeOffset? LastTrainingDate { get; init; }
}
