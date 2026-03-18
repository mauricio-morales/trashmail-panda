using TrashMailPanda.Shared.Base;
using TrashMailPanda.Providers.ML.Models;

namespace TrashMailPanda.Providers.ML;

/// <summary>
/// Orchestrates action model training: data loading → feature pipeline → training →
/// evaluation → versioning. Separate from <see cref="IMLModelProvider"/> because
/// training is a batch operation, not a real-time classification concern.
/// </summary>
public interface IModelTrainingPipeline
{
    // ──────────────────────────────────────────────────────────────────────────
    // Full Training
    // ──────────────────────────────────────────────────────────────────────────

    /// <summary>
    /// Train a new action classification model (Keep / Archive / Delete / Spam)
    /// from all available labeled feature vectors in storage.
    /// </summary>
    Task<Result<TrainingMetricsReport>> TrainActionModelAsync(
        TrainingRequest request,
        IProgress<TrainingProgress>? progress = null,
        CancellationToken cancellationToken = default);

    // ──────────────────────────────────────────────────────────────────────────
    // Incremental Updates
    // ──────────────────────────────────────────────────────────────────────────

    /// <summary>
    /// Update the action model by retraining on all data including recently
    /// accumulated user corrections.
    /// </summary>
    Task<Result<TrainingMetricsReport>> IncrementalUpdateActionModelAsync(
        IncrementalUpdateRequest request,
        IProgress<TrainingProgress>? progress = null,
        CancellationToken cancellationToken = default);

    // ──────────────────────────────────────────────────────────────────────────
    // Data Availability Checks
    // ──────────────────────────────────────────────────────────────────────────

    /// <summary>
    /// Returns how many labeled feature vectors are available for training
    /// and how many more are needed.
    /// </summary>
    Task<Result<TrainingDataSummary>> GetActionTrainingDataSummaryAsync(
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Returns true when sufficient new corrections have accumulated to warrant
    /// an incremental update, or when the model is older than 7 days.
    /// </summary>
    Task<Result<bool>> ShouldRetrainAsync(
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Prune old model versions beyond the retention window for the specified model type.
    /// </summary>
    Task<Result<int>> PruneOldModelsAsync(
        string modelType,
        CancellationToken cancellationToken = default);
}
