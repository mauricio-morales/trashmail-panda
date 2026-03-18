# Data Model: ML.NET Model Training Infrastructure

**Feature**: #59 — ML.NET Model Training Infrastructure  
**Date**: 2026-03-17  
**Status**: Complete  
**Depends on**: #55 data model (email_features, email_archive, schema_version)

---

## Entity Overview

This data model defines the entities introduced by feature #59. It extends the schema from feature #55 by adding model versioning metadata (schema version 6) and the C# types consumed by the ML.NET training pipeline.

---

## 1. Database Tables (Schema Version 6)

### 1.1 ml_models

Stores metadata for every trained model version. Exactly one row per `(ModelType, IsActive=1)` is enforced by a partial unique index.

| Column | Type | Nullable | Description | Validation |
|--------|------|----------|-------------|------------|
| ModelId | TEXT | No | Primary key. Pattern: `"{type}_v{version}_{timestamp}"` e.g. `"act_v3_20260317142305"` | Non-empty |
| ModelType | TEXT | No | Model category | `"action"` or `"label"` |
| Version | INTEGER | No | Monotonic sequence number per ModelType | ≥ 1 |
| TrainingDate | TEXT | No | ISO8601 UTC timestamp when model training completed | Valid datetime |
| Algorithm | TEXT | No | ML.NET trainer identifier | `"SdcaMaximumEntropy"`, `"LightGbm"` |
| FeatureSchemaVersion | INTEGER | No | Value of `FeatureSchema.CurrentVersion` at training time | ≥ 1 |
| TrainingDataCount | INTEGER | No | Count of labeled feature vectors used in training | ≥ 100 |
| Accuracy | REAL | No | Overall classification accuracy | 0.0–1.0 |
| MacroPrecision | REAL | No | Macro-averaged precision across classes/labels | 0.0–1.0 |
| MacroRecall | REAL | No | Macro-averaged recall across classes/labels | 0.0–1.0 |
| MacroF1 | REAL | No | Macro-averaged F1 across classes/labels | 0.0–1.0 |
| PerClassMetricsJson | TEXT | No | JSON dictionary of per-class/per-label `ClassMetrics` | Valid JSON object |
| IsActive | INTEGER | No | 1 = currently active model for this ModelType | 0 or 1 |
| FilePath | TEXT | No | Relative path from repo root to model `.zip` file | Non-empty |
| Notes | TEXT | Yes | Optional notes (quality advisory, trigger reason, rollback info) | Max 500 chars |

**SQL Schema**:
```sql
CREATE TABLE IF NOT EXISTS ml_models (
    ModelId              TEXT    PRIMARY KEY,
    ModelType            TEXT    NOT NULL,
    Version              INTEGER NOT NULL,
    TrainingDate         TEXT    NOT NULL,
    Algorithm            TEXT    NOT NULL,
    FeatureSchemaVersion INTEGER NOT NULL,
    TrainingDataCount    INTEGER NOT NULL,
    Accuracy             REAL    NOT NULL,
    MacroPrecision       REAL    NOT NULL,
    MacroRecall          REAL    NOT NULL,
    MacroF1              REAL    NOT NULL,
    PerClassMetricsJson  TEXT    NOT NULL,
    IsActive             INTEGER NOT NULL DEFAULT 0,
    FilePath             TEXT    NOT NULL,
    Notes                TEXT
);

-- Enforce at most one active model per ModelType
CREATE UNIQUE INDEX IF NOT EXISTS idx_ml_models_active_type
    ON ml_models (ModelType)
    WHERE IsActive = 1;

CREATE INDEX IF NOT EXISTS idx_ml_models_type_version
    ON ml_models (ModelType, Version DESC);
```

---

### 1.2 training_events

Append-only audit log for all training lifecycle events satisfying FR-024.

| Column | Type | Nullable | Description | Validation |
|--------|------|----------|-------------|------------|
| Id | INTEGER | No | Autoincrement primary key | — |
| EventAt | TEXT | No | ISO8601 UTC timestamp | Valid datetime |
| EventType | TEXT | No | Event category | See below |
| ModelType | TEXT | No | `"action"` or `"label"` | — |
| ModelId | TEXT | Yes | Associated model ID (null for `training_started`) | — |
| Details | TEXT | No | JSON payload with event-specific data | Valid JSON |

**EventType values**:
- `"training_started"` — pipeline initiated; Details includes trigger reason, sample count
- `"training_completed"` — model saved; Details includes ModelId, MacroF1, duration
- `"training_failed"` — error during training; Details includes error message, stack summary
- `"rollback"` — model rolled back; Details includes from/to ModelId
- `"pruned"` — old version deleted; Details includes deleted ModelId and FilePath
- `"quality_advisory"` — MacroF1 < 0.70; Details includes threshold and actual value

**SQL Schema**:
```sql
CREATE TABLE IF NOT EXISTS training_events (
    Id        INTEGER PRIMARY KEY AUTOINCREMENT,
    EventAt   TEXT    NOT NULL,
    EventType TEXT    NOT NULL,
    ModelType TEXT    NOT NULL,
    ModelId   TEXT,
    Details   TEXT    NOT NULL
);

CREATE INDEX IF NOT EXISTS idx_training_events_at
    ON training_events (EventAt DESC);
```

---

### 1.3 Schema Version Migration

Feature #59 introduces schema version 6. Applied during `MLModelProvider.InitializeAsync()`:

```sql
INSERT OR IGNORE INTO schema_version (Version, AppliedAt, Description)
VALUES (6, datetime('now'), 'Add ml_models and training_events tables for ML model versioning');
```

---

## 2. ML.NET Input/Output Schemas (C# Classes)

### 2.1 ActionTrainingInput

The ML.NET `IDataView` row schema for action model training. Derived from `EmailFeatureVector` by the `FeaturePipelineBuilder`.

```csharp
namespace TrashMailPanda.Providers.ML.Models;

/// <summary>
/// ML.NET IDataView row schema for action model training.
/// Mapped from EmailFeatureVector; Label is the action ground truth.
/// </summary>
public class ActionTrainingInput
{
    // Numeric features (directly usable by ML.NET)
    public float SenderKnown { get; set; }
    public float ContactStrength { get; set; }
    public float HasListUnsubscribe { get; set; }
    public float HasAttachments { get; set; }
    public float HourReceived { get; set; }
    public float DayOfWeek { get; set; }
    public float EmailSizeLog { get; set; }
    public float SubjectLength { get; set; }
    public float RecipientCount { get; set; }
    public float IsReply { get; set; }
    public float InUserWhitelist { get; set; }
    public float InUserBlacklist { get; set; }
    public float LabelCount { get; set; }
    public float LinkCount { get; set; }
    public float ImageCount { get; set; }
    public float HasTrackingPixel { get; set; }
    public float UnsubscribeLinkInBody { get; set; }
    public float EmailAgeDays { get; set; }
    public float IsInInbox { get; set; }
    public float IsStarred { get; set; }
    public float IsImportant { get; set; }
    public float WasInTrash { get; set; }
    public float WasInSpam { get; set; }
    public float IsArchived { get; set; }
    public float ThreadMessageCount { get; set; }
    public float SenderFrequency { get; set; }

    // Categorical features (ML.NET will one-hot-encode these)
    public string SenderDomain { get; set; } = string.Empty;
    public string SpfResult { get; set; } = string.Empty;
    public string DkimResult { get; set; } = string.Empty;
    public string DmarcResult { get; set; } = string.Empty;

    // Text features (ML.NET will TF-IDF vectorise these)
    public string SubjectText { get; set; } = string.Empty;
    public string BodyTextShort { get; set; } = string.Empty;

    // Class balancing weight (inverse-frequency weight computed by FeaturePipelineBuilder)
    public float Weight { get; set; } = 1.0f;

    // Ground truth label ("Keep" | "Archive" | "Delete" | "Spam")
    public string Label { get; set; } = string.Empty;
}
```

---

### 2.2 ActionPrediction

ML.NET output schema for the action model `PredictionEngine`.

```csharp
namespace TrashMailPanda.Providers.ML.Models;

/// <summary>
/// ML.NET output schema for action model prediction.
/// PredictedLabel contains the winning class; Score contains per-class probabilities.
/// </summary>
public class ActionPrediction
{
    /// <summary>Predicted action: "Keep", "Archive", "Delete", or "Spam".</summary>
    public string PredictedLabel { get; set; } = string.Empty;

    /// <summary>
    /// Per-class probability scores in order: [Keep, Archive, Delete, Spam].
    /// The confidence for the predicted label is Max(Score).
    /// </summary>
    public float[] Score { get; set; } = Array.Empty<float>();

    /// <summary>Confidence score (0.0–1.0) for PredictedLabel. Derived from Score.</summary>
    public float Confidence => Score.Length > 0 ? Score.Max() : 0f;
}
```

---

### 2.3 ModelVersion

Uniquely identifies a trained model version. Maps 1:1 with a row in `ml_models`.

```csharp
namespace TrashMailPanda.Providers.ML.Models;

/// <summary>
/// Represents a single trained model version with its identity and evaluation metrics.
/// Maps 1:1 to a row in the ml_models table.
/// </summary>
public sealed record ModelVersion(
    string ModelId,
    string ModelType,
    int Version,
    DateTime TrainingDate,
    string Algorithm,
    int FeatureSchemaVersion,
    int TrainingDataCount,
    float Accuracy,
    float MacroPrecision,
    float MacroRecall,
    float MacroF1,
    IReadOnlyDictionary<string, ClassMetrics> PerClassMetrics,
    bool IsActive,
    string FilePath,
    string? Notes
);
```

---

### 2.4 ClassMetrics

Per-class evaluation metrics for the action model.

```csharp
namespace TrashMailPanda.Providers.ML.Models;

/// <summary>
/// Evaluation metrics for a single class or label within a model.
/// </summary>
public sealed record ClassMetrics(
    float Precision,
    float Recall,
    float F1Score,
    int SampleCount
);
```

---

### 2.5 TrainingMetricsReport

The aggregate output of a completed training run, displayed to the user.

```csharp
namespace TrashMailPanda.Providers.ML.Models;

/// <summary>
/// Aggregate output of a completed training run. Displayed to the console and
/// stored as JSON in ml_models.PerClassMetricsJson.
/// </summary>
public sealed record TrainingMetricsReport(
    string ModelId,
    string ModelType,
    string Algorithm,
    int TrainingDataCount,
    int ValidationDataCount,
    float Accuracy,
    float MacroPrecision,
    float MacroRecall,
    float MacroF1,
    IReadOnlyDictionary<string, ClassMetrics> PerClassMetrics,
    TimeSpan TrainingDuration,
    /// <summary>True when MacroF1 < configured quality floor (default 0.70).</summary>
    bool IsQualityAdvisory,
    float QualityFloor,
    /// <summary>Null unless IsQualityAdvisory is true.</summary>
    string? QualityAdvisoryMessage,
    /// <summary>Labels excluded from training due to insufficient examples. Always empty for action model.</summary>
    IReadOnlyList<string> ExcludedLabels
);
```

---

### 2.6 TrainingRequest

Input to `IModelTrainingPipeline.TrainActionModelAsync()`.

```csharp
namespace TrashMailPanda.Providers.ML.Models;

/// <summary>
/// Parameters controlling a full training run.
/// </summary>
public sealed record TrainingRequest(
    /// <summary>Fraction of data reserved for validation. Default: 0.2 (20%).</summary>
    float ValidationSplit = 0.2f,
    /// <summary>Override trainer name. Null = use MLModelProviderConfig.ActionTrainerName.</summary>
    string? TrainerNameOverride = null,
    /// <summary>Why this training was triggered ("user_request", "correction_threshold", "schedule").</summary>
    string TriggerReason = "user_request"
);
```

---

### 2.7 IncrementalUpdateRequest

Input to `IModelTrainingPipeline.IncrementalUpdateActionModelAsync()`.

```csharp
namespace TrashMailPanda.Providers.ML.Models;

/// <summary>
/// Parameters for an incremental update. The pipeline fetches all labeled data
/// from IEmailArchiveService, not just the new corrections, because ML.NET
/// does not support warm-start / online learning. The "incremental" aspect
/// is that new corrections are already persisted before this is called.
/// </summary>
public sealed record IncrementalUpdateRequest(
    /// <summary>Minimum new corrections required before update proceeds. Default: 50.</summary>
    int MinNewCorrections = 50,
    /// <summary>Why this update was triggered.</summary>
    string TriggerReason = "correction_threshold"
);
```

---

### 2.8 TrainingProgress

Progress event emitted during training phases for Spectre.Console display.

```csharp
namespace TrashMailPanda.Providers.ML.Models;

/// <summary>
/// Progress event emitted by IModelTrainingPipeline during training.
/// Consumed by the console layer via IProgress&lt;TrainingProgress&gt;.
/// </summary>
public sealed record TrainingProgress(
    /// <summary>Current phase name for display.</summary>
    string Phase,
    /// <summary>Overall progress 0–100.</summary>
    int PercentComplete,
    /// <summary>Human-readable status message.</summary>
    string Message
);
```

---

## 3. Domain Entity Relationships

```text
EmailFeatureVector (from #55)
    │
    │  read by
    ▼
FeaturePipelineBuilder ──► ActionTrainingInput ──► ActionModelTrainer ──► model_v{n}_action.zip
                                                                              │
                                                                              │ metadata stored in
                                                                              ▼
                                                                         ml_models  (SQLite)
                                                                              │
                                                                              │ events appended to
                                                                              ▼
                                                                         training_events (SQLite)

model_v{n}_action.zip ──► ActionClassifier ──► ActionPrediction

ActionPrediction ──► IMLModelProvider (consumed by console triage commands)
```

---

## 4. File System Layout

```text
data/
└── models/
    └── action/
        ├── manifest.json                          # { "active": "act_v3_20260317142305.zip" }
        ├── act_v1_20260315080000.zip              # Older version (prunable)
        ├── act_v2_20260316120000.zip              # Prior version (rollback target)
        └── act_v3_20260317142305.zip              # Active version
```

The `manifest.json` is updated atomically after each training run and rollback. It allows `MLModelProvider.InitializeAsync()` to locate the active model without a database query.
