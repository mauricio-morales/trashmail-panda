// Contract: IModelTrainingPipeline
// Feature: #59 — ML.NET Model Training Infrastructure
// Date: 2026-03-17
// Type: Service Interface (not a provider — no IProvider<TConfig> lifecycle)
//
// Overview:
//   Orchestrates the action model training lifecycle — data loading, feature
//   pipeline construction, model training, evaluation, versioning, and pruning.
//
//   Label model training is deferred to a future feature using an LLM mini model
//   (e.g. gpt-4o-mini) — see GitHub issue #77.
//
//   This is a long-running service (not a provider) because training can take
//   minutes and does not fit the sub-second HealthCheckAsync contract of IProvider.
//   It is registered as a scoped/transient service and injected into console
//   command handlers that initiate training.
//
//   All methods:
//     - Accept IProgress<TrainingProgress>? for Spectre.Console feedback
//     - Accept CancellationToken for user-initiated cancellation
//     - Return Result<T> — never throw
//     - Do NOT activate a partial model if cancelled or failed

namespace TrashMailPanda.Shared;

using TrashMailPanda.Providers.ML.Models;

/// <summary>
/// Orchestrates action model training: data loading → feature pipeline → training →
/// evaluation → versioning. Separate from <see cref="IMLModelProvider"/> because
/// training is a batch operation, not a real-time classification concern.
///
/// Label model training is deferred — see GitHub issue #77.
/// </summary>
public interface IModelTrainingPipeline
{
    // ──────────────────────────────────────────────────────────────────────────
    // Full Training
    // ──────────────────────────────────────────────────────────────────────────

    /// <summary>
    /// Train a new action classification model (Keep / Archive / Delete / Spam)
    /// from all available labeled feature vectors in storage.
    ///
    /// Phases (reported via <paramref name="progress"/>):
    ///   1. Loading feature vectors (0–20%)
    ///   2. Building ML.NET feature pipeline (20–30%)
    ///   3. Training SdcaMaximumEntropy / LightGbm model (30–80%)
    ///   4. Evaluating on validation split (80–100%)
    ///
    /// Returns <see cref="Result.Failure"/> with <c>ValidationError</c> when:
    ///   - Fewer than MinTrainingSamples labeled vectors are available
    ///   - Fewer than 2 distinct action classes are present
    ///   - Stored feature schema version does not match expected version
    /// </summary>
    /// <remarks>
    /// SC-001: With 500+ labeled emails, must complete in &lt;2 min.
    /// SC-007: 10,000 vectors &lt;2 min; 100,000 vectors &lt;5 min.
    /// The active model is only replaced after a successful atomic file move + DB commit.
    /// </remarks>
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
    ///
    /// Note: ML.NET SdcaMaximumEntropy and LightGbm do not support warm-start.
    /// This performs a full retrain on the merged dataset, not a true incremental
    /// update. The "incremental" aspect is that new corrections are already
    /// persisted in storage before this call.
    ///
    /// Returns <see cref="Result.Failure"/> with <c>ValidationError</c> when
    /// fewer than <see cref="IncrementalUpdateRequest.MinNewCorrections"/> new
    /// corrections exist since the last training run.
    /// </summary>
    /// <remarks>
    /// SC-005: With 50–200 new corrections, must complete in &lt;30 s.
    /// </remarks>
    Task<Result<TrainingMetricsReport>> IncrementalUpdateActionModelAsync(
        IncrementalUpdateRequest request,
        IProgress<TrainingProgress>? progress = null,
        CancellationToken cancellationToken = default);

    // ──────────────────────────────────────────────────────────────────────────
    // Data Availability Checks
    // ──────────────────────────────────────────────────────────────────────────

    /// <summary>
    /// Returns how many labeled feature vectors are available for action model training,
    /// and how many more are needed to reach MinTrainingSamples.
    /// Used by the console layer to display readiness status.
    /// </summary>
    Task<Result<TrainingDataSummary>> GetActionTrainingDataSummaryAsync(
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Returns true if automatic retraining should be triggered based on:
    ///   - 50+ new user corrections accumulated since last training, OR
    ///   - 7 days elapsed since last training run
    /// </summary>
    Task<Result<bool>> ShouldRetrainAsync(
        CancellationToken cancellationToken = default);

    // ──────────────────────────────────────────────────────────────────────────
    // Version Management
    // ──────────────────────────────────────────────────────────────────────────

    /// <summary>
    /// Delete model versions beyond the retention limit (MaxModelVersions, default 5).
    /// The currently active model is never pruned.
    /// Deletes the model .zip files and records a "pruned" entry in training_events.
    /// Preserves the ml_models metadata row (sets FilePath to empty, notes deletion).
    /// </summary>
    Task<Result<int>> PruneOldModelsAsync(
        string modelType,
        CancellationToken cancellationToken = default);
}
