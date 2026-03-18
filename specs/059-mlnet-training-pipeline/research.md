# Research: ML.NET Model Training Infrastructure

**Feature**: #59 — ML.NET Model Training Infrastructure  
**Date**: 2026-03-17  
**Status**: Complete  
**Depends on**: #54 (ML architecture design), #55 (ML data storage)

---

## R1: ML.NET Trainer Selection — Action Model (Multi-Class)

**Decision**: Trainer is selected **automatically** by the pipeline at training time based on observed class imbalance — no user configuration required.

**Rationale**:
- `SdcaMaximumEntropy` (Stochastic Dual Coordinate Ascent with Maximum Entropy) converges fast on tabular datasets of the expected size (100–100,000 rows with ~40 features), produces calibrated probability estimates usable as confidence scores, and ships in the core `Microsoft.ML` package — no extra NuGet dependency.
- `LightGbm` achieves better accuracy on severely imbalanced datasets (e.g., >80% of samples in a single class at cold-start) but requires the `Microsoft.ML.FastTree` package and is heavier.
- The pipeline measures `dominantClassRatio = maxClassCount / totalSamples` after loading the training data. If `dominantClassRatio > 0.80`, `LightGbm` is selected automatically; otherwise `SdcaMaximumEntropy` is used. This decision is logged internally (stored in `ml_models.Algorithm` and emitted as a `TrainingProgress` message) but is never exposed as a user setting. Non-technical users are never asked to configure or understand this choice.

**Alternatives considered**:
- `LbfgsMaximumEntropy`: Competitive accuracy, but slower convergence than SDCA on large datasets and not a material improvement for this use case.
- `FastForest`: Good for non-linear interactions but no native probability estimation; not suitable for confidence-scored output.
- TorchSharp / ONNX Runtime: Require Python/external training; violate offline and all-.NET constraints.

---

## R3: Class Balancing Strategy

**Decision**: Inverse-frequency class weighting via `ExampleWeightColumnName` for the action model.

**Rationale**:
- `SdcaMaximumEntropy` honours a per-row weight column. Inverse-frequency weighting computes `weight(class) = total_samples / (n_classes × count(class))`, amplifying minority classes (Delete, Spam) without synthesising data.
- This satisfies FR-003 (action class balancing) with zero additional NuGet dependencies.

**Alternatives considered**:
- SMOTE oversampling: Not available natively in ML.NET; would require custom `IDataTransformer` implementation — unjustified complexity for this corpus size.
- `ClassWeights` parameter on `LightGbm`: Available but trainer-specific; using the row-weight column approach keeps the balancing strategy trainer-agnostic.

---

## R4: Incremental Learning Strategy

**Decision**: Full retrain on all historical data combined with new corrections ("retrain from scratch on merged dataset"), not true online/warm-start learning.

**Rationale**:
- `SdcaMaximumEntropy` in ML.NET 4.x does not expose online or warm-start learning APIs. The trained `ITransformer` is immutable; there is no way to update weights incrementally without re-running the full estimator.
- For the expected corpus sizes (50–200 new corrections added to a 500–5,000 base), full retraining completes well within the 30-second budget (benchmark: ~15s for 5,000 features on commodity hardware).
- At scale (50,000+ features), the model reaches diminishing returns from new corrections anyway, and users tolerate a longer periodic retrain cadence.
- The "incremental update" user story (US-4) is satisfied by this approach: new corrections are merged into the full training set, training is re-run, and the resulting model has a new version identifier.

**Alternatives considered**:
- `LightGbm` resumable training: LightGBM supports "continued training" from a saved model, providing approximate warm-start. This would be a viable future optimisation for very large corpora (>50K rows) but introduces version-compatibility risk with saved model files and is unnecessary for current scale targets.
- Online SGD with running average: Could be implemented manually but requires maintaining gradient state across sessions — substantial complexity with no measurable user benefit at current scale.

---

## R5: Feature Schema Version Compatibility

**Decision**: Reject training if any stored `EmailFeatureVector.FeatureSchemaVersion` does not match the pipeline's `FeatureSchema.CurrentVersion`.

**Rationale**:
- Training on mixed feature schemas produces silently incorrect models (features for schema V1 may map to wrong column positions in a V2 pipeline). A hard rejection with a diagnostic message (as required by FR-012) is safer than attempting schema migration during training.
- `IEmailArchiveService.GetAllFeaturesAsync(schemaVersion: FeatureSchema.CurrentVersion)` already filters by version — the training pipeline calls this API to load only compatible vectors.
- If insufficient same-version vectors exist, training is declined with a message indicating how many vectors need re-extraction.

**Alternatives considered**:
- Silent filtering (train on schema-compatible subset only): Risky — a user could have thousands of vectors at an old schema and only a handful at the new one, silently producing a poor model.
- Schema migration at training time: Feasible but adds a transformation layer that could introduce errors; deferred to a future feature.

---

## R6: Progress Reporting During ML.NET Training

**Decision**: Phase-based `IProgress<TrainingProgress>` callbacks with Spectre.Console `AnsiConsole.Progress()` display.

**Rationale**:
- ML.NET's `Fit()` method is synchronous and blocking; it does not emit progress events during a single training run. Progress can only be reported between discrete pipeline stages.
- Four phases are reported: (1) Loading feature vectors, (2) Building feature pipeline, (3) Training, (4) Evaluating. Each phase completion is a `TrainingProgress` event with a percentage and message.
- The console layer (not the training pipeline itself) subscribes to `IProgress<TrainingProgress>` and renders updates via `AnsiConsole.MarkupLine()`. This keeps the training pipeline free of Spectre.Console dependencies and testable in isolation.
- `AnsiConsole.Status()` spinner is used during the blocking `Fit()` call so the console does not appear frozen (FR-013 / US-3).

**Alternatives considered**:
- Wrapping `Fit()` in a `Task.Run()` with polling: Adds unnecessary threading complexity; ML.NET is already CPU-bound during training — no UI thread is blocked in a console app.
- ML.NET `ICrossValSummaryResults` callbacks: These exist for cross-validation but not for single-run fitting.

---

## R7: Model File Naming and Storage Layout

**Decision**: Model files are stored under `{TrashMailPanda data dir}/models/action/model_v{version}_{yyyyMMddHHmmss}.zip`, alongside a `manifest.json` per model type. The base directory is derived automatically from `StorageProviderConfig.GetOsDefaultPath()` — the same OS-standard path already used for the SQLite database — so no separate configuration is required.

**Rationale**:
- The SQLite database already lives at the OS-appropriate location via `StorageProviderConfig.GetOsDefaultPath()` (e.g. `~/Library/Application Support/TrashMailPanda/app.db` on macOS). The model directory is computed as `Path.Combine(Path.GetDirectoryName(databasePath)!, "models", "action")`, placing models at `{same base}/models/action/`. This keeps all application data co-located without any user-facing path configuration.
- Using subdirectories per model type (`action/` vs `label/`) prevents naming collisions and makes pruning scoped — deleting old action versions does not touch label versions.
- `manifest.json` contains the active model filename, enabling the application to find the current model without a database query at startup.
- The `.zip` extension is ML.NET's native model serialisation format from `mlContext.Model.Save()`.
- Version numbers in filenames are monotonically increasing integers (not dates), enabling correct `ORDER BY` without date parsing. Timestamps in the filename are redundant with metadata but aid manual inspection.

**Alternatives considered**:
- Single flat directory with `action_v{n}.zip`: Simpler, but is less clean if label model storage is added in a future feature (see issue #77).
- Database blob storage: Unnecessary for local files; file system is sufficient and keeps model files portable.
- Separate `ModelDirectory` config property: Rejected — requires user configuration for a detail users should never have to think about. Deriving the path from the existing DB location is zero-config and keeps all data in one place.

---

## R8: Crash Safety — Partial Training Must Not Activate Broken Model

**Decision**: Train to a temporary file path, evaluate, then atomically move to the final versioned path. `ml_models.IsActive` is only updated after the file is confirmed present and metrics are recorded.

**Rationale**:
- FR-023 requires that an interrupted training run must not replace the active model. A two-phase commit (write-to-temp, then rename) ensures the active model file is replaced only when training and evaluation complete successfully.
- CancellationToken propagation to the training pipeline ensures clean cancellation. If cancelled before the atomic move, the temp file is deleted and the active model remains unchanged.
- The `ml_models` table update (set new version `IsActive = 1`, old version `IsActive = 0`) runs in a single SQLite transaction after the file move, ensuring metadata consistency.

**Alternatives considered**:
- Copy-on-write with backup: More complex; the atomic rename achieves the same durability guarantee with less code.

---

## R9: ml_models Database Schema (Schema Version 6)

**Decision**: Single `ml_models` table in `data/app.db` (schema version 6 migration), with separate audit log in `training_events`.

**Rationale**:
- Keeps model metadata in the existing encrypted database, consistent with the storage architecture from spec #55.
- `training_events` is a separate append-only table satisfying FR-024 (audit log) without polluting the versioning table with verbose event data.
- Schema version 6 is the next sequential migration after version 5 (introduced in spec #55).

**Schema**:
```sql
-- Schema version 6: Add ML model versioning and training audit tables

CREATE TABLE IF NOT EXISTS ml_models (
    ModelId     TEXT PRIMARY KEY,          -- UUID, e.g. "act_v3_20260317142305"
    ModelType   TEXT NOT NULL,             -- "action" (label model deferred — see issue #77)
    Version     INTEGER NOT NULL,          -- Monotonic version number per ModelType
    TrainingDate TEXT NOT NULL,            -- ISO8601 UTC
    Algorithm   TEXT NOT NULL,             -- "SdcaMaximumEntropy" | "LightGbm"
    FeatureSchemaVersion INTEGER NOT NULL, -- FeatureSchema.CurrentVersion at time of training
    TrainingDataCount INTEGER NOT NULL,    -- Number of labeled vectors used
    Accuracy    REAL NOT NULL,             -- Overall accuracy (0.0-1.0)
    MacroPrecision REAL NOT NULL,
    MacroRecall REAL NOT NULL,
    MacroF1     REAL NOT NULL,
    PerClassMetricsJson TEXT NOT NULL,     -- JSON: { "Keep": { precision, recall, f1 }, ... }
    IsActive    INTEGER NOT NULL DEFAULT 0, -- 0 | 1 (exactly one active per ModelType)
    FilePath    TEXT NOT NULL,             -- Relative: "data/models/action/model_v3_20260317142305.zip"
    Notes       TEXT                       -- Optional rollback/trigger reason
);

CREATE UNIQUE INDEX IF NOT EXISTS idx_ml_models_active_type
    ON ml_models (ModelType)
    WHERE IsActive = 1;

CREATE INDEX IF NOT EXISTS idx_ml_models_type_version
    ON ml_models (ModelType, Version DESC);

CREATE TABLE IF NOT EXISTS training_events (
    Id          INTEGER PRIMARY KEY AUTOINCREMENT,
    EventAt     TEXT NOT NULL,             -- ISO8601 UTC
    EventType   TEXT NOT NULL,             -- "training_started" | "training_completed" | "training_failed" | "rollback" | "pruned"
    ModelType   TEXT NOT NULL,             -- "action" (label model deferred — see issue #77)
    ModelId     TEXT,                      -- NULL for training_started events
    Details     TEXT NOT NULL              -- JSON payload with event-specific fields
);

CREATE INDEX IF NOT EXISTS idx_training_events_at
    ON training_events (EventAt DESC);

INSERT OR IGNORE INTO schema_version (Version, AppliedAt, Description)
VALUES (6, datetime('now'), 'Add ml_models and training_events tables');
```

---

## R10: Minimum Training Data Validation

**Decision**: Reject training with a `ValidationError` when fewer than `MinTrainingSamples` (default 100) labeled feature vectors are available.

**Rationale**:
- FR-016 requires a clear decline message with remaining count. The `Result.Failure(new ValidationError(...))` includes `Message = $"Insufficient training data: {available} of {required} labeled emails available."` and a `RequiresUserIntervention = true` flag.
- The "degenerate dataset" edge case (all same label) is blocked by requiring at least 2 distinct action classes to be present in the training data.

---

## R11: Quality Advisory Threshold

**Decision**: Emit `IsQualityAdvisory = true` in `TrainingMetricsReport` when `MacroF1 < 0.70`. Display with `[yellow]⚠[/]` Spectre.Console markup. Do not block activation.

**Rationale**:
- FR-022 specifies advisory (not blocking). Users may choose to use a below-threshold model if they have limited training data and no better option.
- The advisory is stored in `ml_models.Notes` as `"quality_advisory: MacroF1={value}"` to support audit queries.

---

## Summary of Decisions

| Question | Decision |
|----------|----------|
| Action model trainer | Auto-selected: `LightGbm` if dominant class >80% of samples, otherwise `SdcaMaximumEntropy`; never user-configurable |
| Label model trainer | Deferred — see GitHub issue #77; future approach is LLM mini model |
| Class balancing | Inverse-frequency row weights via `ExampleWeightColumnName` |
| Incremental learning | Full retrain on merged dataset (ML.NET lacks warm-start for these trainers) |
| Schema version mismatch | Hard reject with diagnostic error |
| Progress reporting | `IProgress<TrainingProgress>` + Spectre.Console `AnsiConsole.Status()` spinner |
| Model file storage | `{TrashMailPanda data dir}/models/action/model_v{n}_{ts}.zip` derived from DB base path; temp/rename crash safety |
| Metadata storage | `ml_models` + `training_events` tables, schema version 6 |
| Minimum data | 100 labeled emails; reject with `ValidationError` + remaining count |
| Quality advisory | MacroF1 < 0.70 → `[yellow]⚠[/]` advisory, not blocking |
