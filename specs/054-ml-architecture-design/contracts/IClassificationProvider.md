# Contract: IClassificationProvider

**Feature**: #54 — ML Architecture Design  
**Date**: 2026-03-14  
**Type**: Provider Interface

## Overview

`IClassificationProvider` replaces `ILLMProvider.ClassifyEmailsAsync()` for email classification. It follows the existing `IProvider<TConfig>` pattern and returns `Result<T>` for all operations.

The **primary use case** is archive reclamation: classifying existing archived emails and recommending which to delete to reclaim storage. Incoming email classification is a secondary, steady-state workflow.

## Interface Definition

```csharp
namespace TrashMailPanda.Shared;

/// <summary>
/// Provider interface for ML-based email classification.
/// Supports both action classification (keep/archive/delete/spam)
/// and folder/tag prediction.
///
/// PRIMARY USE CASE: Archive reclamation — classify archived emails
/// and recommend bulk deletions to reclaim storage.
/// Works with any email provider (Gmail, IMAP, Outlook, etc.).
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
    /// Triage archived emails for deletion recommendations.
    /// Returns grouped recommendations with confidence scores and storage reclaim estimates.
    /// This is the primary workflow for archive reclamation.
    /// </summary>
    Task<Result<ArchiveTriageOutput>> TriageArchiveAsync(
        ArchiveTriageInput input,
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

## Archive Triage Types

```csharp
namespace TrashMailPanda.Shared;

/// <summary>
/// Input for archive triage — a batch of archived emails to classify for deletion.
/// </summary>
public class ArchiveTriageInput
{
    /// <summary>Archived emails to triage (emails not in Inbox/Trash/Spam).</summary>
    public IReadOnlyList<EmailClassificationInput> ArchivedEmails { get; init; } = Array.Empty<EmailClassificationInput>();

    /// <summary>Current user rules for whitelist/blacklist override.</summary>
    public UserRules UserRulesSnapshot { get; init; } = new();

    /// <summary>Minimum deletion confidence to include in recommendations. Default: 0.6</summary>
    [Range(0.0, 1.0)]
    public double MinDeletionConfidence { get; init; } = 0.6;
}

/// <summary>
/// Output of archive triage — grouped deletion recommendations.
/// </summary>
public class ArchiveTriageOutput
{
    /// <summary>Grouped recommendations for bulk deletion decisions.</summary>
    public IReadOnlyList<TriageBulkGroup> Groups { get; init; } = Array.Empty<TriageBulkGroup>();

    /// <summary>Total emails triaged in this batch.</summary>
    public int TotalTriaged { get; init; }

    /// <summary>Total emails recommended for deletion.</summary>
    public int TotalRecommendedForDeletion { get; init; }

    /// <summary>Total storage reclaimable if all recommendations accepted.</summary>
    public long TotalReclaimableBytes { get; init; }
}

/// <summary>
/// A group of similar emails recommended for the same action.
/// Presented to the user as a single bulk decision.
/// </summary>
public class TriageBulkGroup
{
    /// <summary>Group identifier for batch operations.</summary>
    public string GroupKey { get; init; } = string.Empty;

    /// <summary>Human-readable group label (e.g., "Newsletters from marketing.example.com — 47 emails").</summary>
    public string Label { get; init; } = string.Empty;

    /// <summary>Human-readable reason for the recommendation.</summary>
    public string Reason { get; init; } = string.Empty;

    /// <summary>Recommended action for this group.</summary>
    public string RecommendedAction { get; init; } = "review";

    /// <summary>Average deletion confidence across emails in this group.</summary>
    public double AverageConfidence { get; init; }

    /// <summary>Number of emails in this group.</summary>
    public int EmailCount { get; init; }

    /// <summary>Total storage that would be freed by deleting this group.</summary>
    public long StorageReclaimBytes { get; init; }

    /// <summary>Human-readable storage string (e.g., "142 MB").</summary>
    public string StorageReclaimDisplay { get; init; } = string.Empty;

    /// <summary>Email IDs in this group.</summary>
    public IReadOnlyList<string> EmailIds { get; init; } = Array.Empty<string>();
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
| TriageArchive with archived emails | Returns grouped deletion recommendations sorted by confidence |
| TriageArchive with whitelisted emails | Whitelisted emails excluded from deletion recommendations |
| TriageArchive with confidence below MinDeletionConfidence | Emails below threshold excluded from recommendations |

## Error Types

| Error | When | Transient | User Action Required |
|-------|------|-----------|---------------------|
| `InitializationError` | Model file corrupted/missing | No | Retrain model |
| `ConfigurationError` | Invalid config (bad paths) | No | Fix configuration |
| `ValidationError` | Invalid feature schema version mismatch | No | Retrain with current schema |
