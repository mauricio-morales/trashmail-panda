using TrashMailPanda.Shared.Base;
using TrashMailPanda.Providers.ML.Models;
using TrashMailPanda.Providers.ML.Config;
using TrashMailPanda.Providers.Storage.Models;

namespace TrashMailPanda.Providers.ML;

/// <summary>
/// Provider interface for ML-based email action classification and model management.
///
/// Responsibilities:
///   - Classify individual emails and batches for action (Keep/Archive/Delete/Spam)
///   - Query action model version history
///   - Rollback to a prior retained model version
///
/// Training is delegated to <see cref="IModelTrainingPipeline"/>, which is a
/// separate service not exposed through this provider interface.
/// </summary>
public interface IMLModelProvider : TrashMailPanda.Shared.Base.IProvider<MLModelProviderConfig>
{
    // ──────────────────────────────────────────────────────────────────────────
    // Action Classification
    // ──────────────────────────────────────────────────────────────────────────

    /// <summary>
    /// Classify a single email's recommended action: Keep, Archive, Delete, or Spam.
    /// Falls back to rule-based classification when no trained model is loaded.
    /// </summary>
    Task<Result<ActionPrediction>> ClassifyActionAsync(
        EmailFeatureVector input,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Classify a batch of emails for their recommended actions.
    /// Returns one <see cref="ActionPrediction"/> per input in the same order.
    /// </summary>
    Task<Result<IReadOnlyList<ActionPrediction>>> ClassifyActionBatchAsync(
        IEnumerable<EmailFeatureVector> inputs,
        CancellationToken cancellationToken = default);

    // ──────────────────────────────────────────────────────────────────────────
    // Model Version Management
    // ──────────────────────────────────────────────────────────────────────────

    /// <summary>
    /// Return all stored model versions for the specified model type,
    /// ordered by Version descending (newest first).
    /// </summary>
    Task<Result<IReadOnlyList<ModelVersion>>> GetModelVersionsAsync(
        string modelType,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Return the currently active model version for the specified model type.
    /// Returns <see cref="Result{T}"/> failure with a <c>ConfigurationError</c>
    /// when no model has been trained yet.
    /// </summary>
    Task<Result<ModelVersion>> GetActiveModelVersionAsync(
        string modelType,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Roll back to a prior retained model version, making it active immediately.
    /// Returns failure with a <c>ValidationError</c> when the specified modelId
    /// does not exist or is already active.
    /// </summary>
    Task<Result<ModelVersion>> RollbackAsync(
        string modelId,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Returns the current classification mode (ColdStart, Hybrid, or MlPrimary).
    /// </summary>
    Task<Result<ClassificationMode>> GetClassificationModeAsync(
        CancellationToken cancellationToken = default);
}
