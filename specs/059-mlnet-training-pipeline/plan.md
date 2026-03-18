# Implementation Plan: ML.NET Model Training Infrastructure

**Branch**: `059-mlnet-training-pipeline` | **Date**: 2026-03-17 | **Spec**: [spec.md](spec.md)  
**Input**: Feature specification from `/specs/059-mlnet-training-pipeline/spec.md`

## Summary

Implement an ML.NET-based training pipeline for local email action classification. The **action classifier** assigns exactly one of Keep / Archive / Delete / Spam to each email with a confidence score. The trainer is **selected automatically** based on class imbalance in the training data (`LightGbm` when the dominant class exceeds 80% of samples, `SdcaMaximumEntropy` otherwise), with inverse-frequency class weighting to handle cold-start imbalance. The pipeline consumes `EmailFeatureVector` records stored by feature #55, supports versioning with 5-version retention, real-time Spectre.Console progress output, rollback, and incremental updates (implemented as full retrain on the merged dataset, because ML.NET's tabular trainers do not support online/warm-start learning). Label suggestion is deferred to a future feature using an LLM mini model — see GitHub issue #77.

## Technical Context

**Language/Version**: .NET 9.0 / C# 12  
**Primary Dependencies**:
- `Microsoft.ML` 4.x — core training/inference (IDataView, IEstimator, PredictionEngine)
- `Microsoft.ML.FastTree` 4.x — LightGBM trainer (automatically selected when class imbalance is severe; not user-configurable)
- `Spectre.Console` 0.48.0 — already in main app; used for per-phase progress
- `Microsoft.Extensions.DependencyInjection.Abstractions` + `Microsoft.Extensions.Logging.Abstractions` — DI/logging
- Existing: `IEmailArchiveService` (spec #55), `EmailFeatureVector` (spec #55), `IProvider<TConfig>` (Shared), `Result<T>` (Shared)

**Storage**:
- SQLite (SQLCipher) via existing `data/app.db` — new `ml_models` table (schema version 6)
- File system: `{TrashMailPanda data dir}/models/action/` for `.zip` model files; base directory derived automatically from `StorageProviderConfig.GetOsDefaultPath()` (same OS-standard path used by the SQLite database — no user configuration)

**Testing**: xUnit + Moq + coverlet  
**Target Platform**: macOS / Linux / Windows console  
**Project Type**: provider library + console integration  
**Performance Goals**:
- Action model training (500 labeled emails): overall F1 ≥ 0.80, completes in < 2 min
- Training on 10,000 vectors: < 2 min; on 100,000 vectors: < 5 min
- Single email action classification: < 10 ms after model loaded
- Batch 100 emails: < 100 ms
- Incremental update (50–200 new corrections): < 30 s
- Rollback to prior version: < 5 s

**Constraints**: Fully offline; no external API calls; SQLCipher encryption at rest; partial-training crash must not activate broken model  
**Scale/Scope**: Up to 100,000 `EmailFeatureVector` rows; up to 5 versions per model type; up to ~50 distinct Gmail label targets

## Constitution Check

*GATE: Must pass before Phase 0 research. Re-check after Phase 1 design.*

| Principle | Status | Notes |
|-----------|--------|-------|
| I. Provider-Agnostic Architecture | ✅ PASS | `IMLModelProvider` implements `IProvider<MLModelProviderConfig>`; consumes provider-agnostic `EmailFeatureVector` |
| II. Result Pattern | ✅ PASS | Every public method returns `Result<T>` or `Result`; no exceptions thrown from provider or service code |
| III. Security First | ✅ PASS | No API keys; local inference only; model metadata stored in encrypted SQLCipher DB; no email content logged |
| IV. MVVM (N/A — console feature) | ✅ N/A | Console-only; Spectre.Console used for progress output; no UI binding required |
| V. One Public Type Per File | ✅ PASS | Every interface, class, record, and enum in its own file |
| VI. Strict Null Safety | ✅ PASS | `<Nullable>enable</Nullable>` in new project; all return types and parameters annotated |
| VII. Test Coverage | ✅ PASS | 95% coverage target for `MLModelProvider`; unit tests for all trainer and versioning logic |

## Project Structure

### Documentation (this feature)

```text
specs/059-mlnet-training-pipeline/
├── plan.md              # This file
├── research.md          # Phase 0 output
├── data-model.md        # Phase 1 output
├── quickstart.md        # Phase 1 output
├── contracts/
│   ├── IMLModelProvider.cs
│   ├── IModelTrainingPipeline.cs
│   └── IActionClassifier.cs
└── tasks.md             # Phase 2 output (/speckit.tasks — NOT created here)
```

### Source Code (repository root)

```text
src/
├── Providers/
│   └── ML/
│       └── TrashMailPanda.Providers.ML/
│           ├── TrashMailPanda.Providers.ML.csproj
│           ├── GlobalUsings.cs
│           ├── MLModelProvider.cs                  # IMLModelProvider implementation
│           ├── Training/
│           │   ├── ModelTrainingPipeline.cs        # IModelTrainingPipeline implementation
│           │   ├── ActionModelTrainer.cs           # SdcaMaximumEntropy / LightGBM action trainer
│           │   ├── FeaturePipelineBuilder.cs       # ML.NET IEstimator pipeline construction
│           │   └── IncrementalUpdateService.cs     # Full-retrain-with-new-data incremental update
│           ├── Classification/
│           │   └── ActionClassifier.cs             # IActionClassifier — loads & runs action model
│           ├── Versioning/
│           │   ├── ModelVersionRepository.cs       # CRUD for ml_models table
│           │   └── ModelVersionPruner.cs           # Automatic pruning to MaxModelVersions
│           ├── Models/
│           │   ├── ActionTrainingInput.cs          # ML.NET IDataView input schema (action)
│           │   ├── ActionPrediction.cs             # ML.NET output schema (action)
│           │   ├── ModelVersion.cs                 # Versioning record
│           │   ├── TrainingMetricsReport.cs        # Evaluation output
│           │   └── IncrementalUpdateRequest.cs     # Input to incremental training
│           └── Config/
│               └── MLModelProviderConfig.cs        # DataAnnotations-validated config
├── Shared/
│   └── TrashMailPanda.Shared/
│       ├── IMLModelProvider.cs                     # NEW — primary provider interface
│       ├── IModelTrainingPipeline.cs               # NEW — training service interface
│       └── IActionClassifier.cs                    # NEW — action classification interface
└── Tests/
    └── TrashMailPanda.Tests/
        ├── Unit/
        │   └── ML/
        │       ├── ActionModelTrainerTests.cs
        │       ├── ModelVersionRepositoryTests.cs
        │       ├── FeaturePipelineBuilderTests.cs
        │       └── IncrementalUpdateServiceTests.cs
        └── Integration/
            └── ML/
                └── MLModelProviderIntegrationTests.cs
```

**Structure Decision**: New project `TrashMailPanda.Providers.ML` under `src/Providers/ML/`, following the existing pattern for `Email`, `LLM`, and `Storage` providers. Public interfaces live in `TrashMailPanda.Shared` per established convention. This isolates ML.NET NuGet dependencies from other providers and keeps clean separation of concerns.

## Complexity Tracking

> No constitution violations — no entries required.
