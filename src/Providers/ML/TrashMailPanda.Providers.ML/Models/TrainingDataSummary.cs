namespace TrashMailPanda.Providers.ML.Models;

/// <summary>
/// Summary of available labeled training data returned by
/// <see cref="IModelTrainingPipeline.GetActionTrainingDataSummaryAsync"/>.
/// </summary>
public sealed class TrainingDataSummary
{
    public int Available { get; init; }
    public int Required { get; init; }
    public bool IsReady => Available >= Required;
    /// <summary>How many more labeled emails are needed before training can begin.</summary>
    public int Deficit => Math.Max(0, Required - Available);
}
