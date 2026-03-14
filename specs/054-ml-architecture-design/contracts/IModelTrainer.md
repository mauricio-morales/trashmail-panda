# Contract: IModelTrainer

**Feature**: #54 — ML Architecture Design  
**Date**: 2026-03-14  
**Type**: Service Interface

## Overview

`IModelTrainer` handles ML model training lifecycle — from loading training data through feature extraction, model training, evaluation, and persistence. It is a long-running service, not a provider.

## Interface Definition

```csharp
namespace TrashMailPanda.Shared;

/// <summary>
/// Manages ML model training lifecycle: data loading, training, evaluation, and persistence.
/// </summary>
public interface IModelTrainer
{
    /// <summary>
    /// Train a new model using available labeled data.
    /// For initial training, bootstraps from mailbox folder placement:
    /// Trash/Spam → "delete" labels, Starred/Flagged/Inbox → "keep" labels.
    /// Works identically across Gmail, IMAP, Outlook, and other providers.
    /// Stores model file and records metadata in ml_models table.
    /// </summary>
    Task<Result<TrainingResult>> TrainAsync(
        TrainingConfig config,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Evaluate an existing model against a validation dataset.
    /// Returns per-class metrics without modifying any state.
    /// </summary>
    Task<Result<ModelMetrics>> EvaluateAsync(
        string modelId,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Roll back to a previous model version.
    /// Sets the specified version as active, deactivates current.
    /// </summary>
    Task<Result<bool>> RollbackAsync(
        string targetModelId,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Check if automatic retraining is needed based on correction count and schedule.
    /// </summary>
    Task<Result<bool>> ShouldRetrainAsync(
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Prune old model versions, keeping only MaxModelVersions most recent.
    /// </summary>
    Task<Result<int>> PruneOldModelsAsync(
        CancellationToken cancellationToken = default);
}
```

## Configuration & Types

```csharp
namespace TrashMailPanda.Shared;

public class TrainingConfig
{
    /// <summary>Model type to train.</summary>
    public string ModelType { get; init; } = "action";

    /// <summary>ML.NET trainer to use.</summary>
    public string TrainerName { get; init; } = "SdcaMaximumEntropy";

    /// <summary>Train/validation split ratio (0.0-1.0).</summary>
    [Range(0.1, 0.5)]
    public double ValidationSplit { get; init; } = 0.2;

    /// <summary>Reason for this training run.</summary>
    public string TriggerReason { get; init; } = "user_request";
}

public record TrainingResult(
    string ModelId,
    string ModelType,
    int SampleCount,
    float Accuracy,
    float MacroF1,
    TimeSpan Duration,
    string ModelFilePath);

public record ModelMetrics(
    float Accuracy,
    float MacroF1,
    float MicroF1,
    IReadOnlyDictionary<string, ClassMetrics> PerClassMetrics);

public record ClassMetrics(
    float Precision,
    float Recall,
    float F1Score,
    int SupportCount);
```

## Behavior Contract

| Scenario | Expected Behavior |
|----------|-------------------|
| < MinTrainingSamples labeled | Returns `Result.Failure(ValidationError("Insufficient training data"))` |
| Training succeeds | Saves model .zip, records metadata, sets as active, returns TrainingResult |
| Training accuracy regression (< previous model) | Saves model but does NOT set as active; includes warning in result |
| Rollback to non-existent version | Returns `Result.Failure(ValidationError("Model version not found"))` |
| ShouldRetrain check: 50+ corrections | Returns `Result.Success(true)` |
| ShouldRetrain check: 7 days since last training | Returns `Result.Success(true)` |
| Prune with 3 models, max=5 | Returns `Result.Success(0)` (nothing to prune) |
| Cancellation during training | Cleans up partial state, records failed training event |

## Retraining Triggers

| Trigger | Threshold | Priority |
|---------|-----------|----------|
| User correction count | 50+ unused corrections | High |
| Schedule | 7 days since last training | Medium |
| User request | Manual "retrain now" command | Immediate |
| Feature schema change | New SchemaVersion detected | Required |
