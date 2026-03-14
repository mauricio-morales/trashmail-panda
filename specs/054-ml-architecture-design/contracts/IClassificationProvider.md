# Contract: IClassificationProvider

**Feature**: #54 — ML Architecture Design  
**Date**: 2026-03-14  
**Type**: Provider Interface

## Overview

`IClassificationProvider` replaces `ILLMProvider.ClassifyEmailsAsync()` for email classification. It follows the existing `IProvider<TConfig>` pattern and returns `Result<T>` for all operations.

## Interface Definition

```csharp
namespace TrashMailPanda.Shared;

/// <summary>
/// Provider interface for ML-based email classification.
/// Supports both action classification (keep/archive/delete/spam)
/// and label prediction (Gmail labels).
/// </summary>
public interface IClassificationProvider : IProvider<ClassificationProviderConfig>
{
    /// <summary>
    /// Classify a batch of emails using the active ML model.
    /// Falls back to rule-based classification when no trained model exists.
    /// </summary>
    Task<Result<ClassifyOutput>> ClassifyAsync(
        ClassifyInput input,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Get information about the currently active model.
    /// Returns model version, accuracy metrics, and training date.
    /// </summary>
    Task<Result<ModelInfo>> GetModelInfoAsync(
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Get the current classification mode based on training data availability.
    /// </summary>
    Task<Result<ClassificationMode>> GetClassificationModeAsync(
        CancellationToken cancellationToken = default);
}
```

## Configuration

```csharp
namespace TrashMailPanda.Shared;

public class ClassificationProviderConfig : BaseProviderConfig
{
    /// <summary>
    /// Directory path for storing trained model files.
    /// Default: "data/models/"
    /// </summary>
    [Required]
    public string ModelDirectory { get; set; } = "data/models/";

    /// <summary>
    /// Maximum number of model versions to retain for rollback.
    /// Default: 5
    /// </summary>
    [Range(1, 20)]
    public int MaxModelVersions { get; set; } = 5;

    /// <summary>
    /// Minimum labeled emails required before training first model.
    /// Default: 100
    /// </summary>
    [Range(10, 10000)]
    public int MinTrainingSamples { get; set; } = 100;

    /// <summary>
    /// Confidence threshold below which classification is marked as "Unknown".
    /// Default: 0.5
    /// </summary>
    [Range(0.1, 0.99)]
    public double ConfidenceThreshold { get; set; } = 0.5;
}
```

## Response Types

```csharp
namespace TrashMailPanda.Shared;

public record ModelInfo(
    string Version,
    string ModelType,
    string TrainerName,
    int FeatureSchemaVersion,
    int TrainingSampleCount,
    float Accuracy,
    float MacroF1,
    DateTime CreatedAt,
    bool IsActive);

public enum ClassificationMode
{
    /// <summary>No training data. Using user rules only.</summary>
    ColdStart,

    /// <summary>100-500 labels. ML model + rule fallback.</summary>
    Hybrid,

    /// <summary>500+ labels. ML model with rule overrides.</summary>
    MlPrimary
}
```

## Behavior Contract

| Scenario | Expected Behavior |
|----------|-------------------|
| No trained model exists | Returns rule-based classifications (AlwaysKeep/AutoTrash match or Unknown) |
| Model exists, email matches whitelist | Returns classification from rules (rule override takes precedence) |
| Model exists, normal email | Returns ML classification with confidence score |
| Confidence below threshold | Classification set to `Unknown`, confidence preserved |
| Model file missing/corrupted | Returns `Result.Failure(InitializationError)`, attempts rollback to previous version |
| Empty input batch | Returns `Result.Success` with empty items list |

## Error Types

| Error | When | Transient | User Action Required |
|-------|------|-----------|---------------------|
| `InitializationError` | Model file corrupted/missing | No | Retrain model |
| `ConfigurationError` | Invalid config (bad paths) | No | Fix configuration |
| `ValidationError` | Invalid feature schema version mismatch | No | Retrain with current schema |
