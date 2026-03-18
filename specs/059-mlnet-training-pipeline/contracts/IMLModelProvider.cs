// Contract: IMLModelProvider
// Feature: #59 — ML.NET Model Training Infrastructure
// Date: 2026-03-17
// Type: Provider Interface (extends IProvider<TConfig>)
//
// Overview:
//   Primary provider interface for ML-based email action classification.
//   Exposes action classification, model version queries, and rollback.
//   Implements IProvider<MLModelProviderConfig> to participate in the existing
//   provider lifecycle (InitializeAsync, HealthCheckAsync, etc.).
//
//   Classification methods are synchronous internally (PredictionEngine<T> is
//   single-threaded by design), so they are wrapped in Task for consistency
//   with the provider interface contract.
//
//   Label suggestion is deferred to a future feature using an LLM mini model
//   (e.g. gpt-4o-mini) — see GitHub issue #77.

namespace TrashMailPanda.Shared;

using TrashMailPanda.Shared.Base;
using TrashMailPanda.Providers.ML.Models;

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
///
/// Label suggestion is out of scope for this provider — see GitHub issue #77.
/// </summary>
public interface IMLModelProvider : IProvider<MLModelProviderConfig>
{
    // ──────────────────────────────────────────────────────────────────────────
    // Action Classification
    // ──────────────────────────────────────────────────────────────────────────

    /// <summary>
    /// Classify a single email's recommended action: Keep, Archive, Delete, or Spam.
    /// Returns a confidence score (0.0–1.0) alongside the predicted label.
    /// Falls back to rule-based classification when no trained action model is loaded.
    /// </summary>
    /// <remarks>
    /// SC-003: Must complete in &lt;10 ms (combined action + label classification).
    /// </remarks>
    Task<Result<ActionPrediction>> ClassifyActionAsync(
        EmailFeatureVector input,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Classify a batch of emails for their recommended actions.
    /// Returns one <see cref="ActionPrediction"/> per input in the same order.
    /// </summary>
    /// <remarks>
    /// SC-004: Batch of 100 emails must complete in &lt;100 ms.
    /// </remarks>
    Task<Result<IReadOnlyList<ActionPrediction>>> ClassifyActionBatchAsync(
        IEnumerable<EmailFeatureVector> inputs,
        CancellationToken cancellationToken = default);

    // ──────────────────────────────────────────────────────────────────────────
    // Model Version Management
    // ──────────────────────────────────────────────────────────────────────────

    /// <summary>
    /// Return all stored model versions for the specified model type,
    /// ordered by Version descending (newest first).
    /// Includes the active model and all retained older versions.
    /// </summary>
    Task<Result<IReadOnlyList<ModelVersion>>> GetModelVersionsAsync(
        string modelType,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Return the currently active model version for the specified model type.
    /// Returns <see cref="Result.Failure"/> with a <c>ConfigurationError</c>
    /// when no model has been trained yet.
    /// </summary>
    Task<Result<ModelVersion>> GetActiveModelVersionAsync(
        string modelType,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Roll back to a prior retained model version, making it active immediately.
    /// The previously active version remains in the version list but IsActive = 0.
    /// Returns <see cref="Result.Failure"/> with a <c>ValidationError</c> when
    /// the specified modelId does not exist or is already active.
    /// </summary>
    /// <remarks>
    /// SC-006: Must complete in &lt;5 s.
    /// </remarks>
    Task<Result<ModelVersion>> RollbackAsync(
        string modelId,
        CancellationToken cancellationToken = default);

    // ──────────────────────────────────────────────────────────────────────────
    // Classification Mode
    // ──────────────────────────────────────────────────────────────────────────

    /// <summary>
    /// Returns the current classification mode based on available training data
    /// and loaded model state: ColdStart, Hybrid, or MlPrimary.
    /// Used by the console layer to display appropriate guidance.
    /// </summary>
    Task<Result<ClassificationMode>> GetClassificationModeAsync(
        CancellationToken cancellationToken = default);
}
