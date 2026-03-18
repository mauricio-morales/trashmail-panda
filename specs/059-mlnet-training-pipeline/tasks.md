# Tasks: ML.NET Model Training Infrastructure

**Feature**: #59 — ML.NET Model Training Infrastructure
**Branch**: `059-mlnet-training-pipeline`
**Input**: Design documents from `/specs/059-mlnet-training-pipeline/`

## Format: `[ID] [P?] [Story] Description`

- **[P]**: Can run in parallel (different files, no active dependencies)
- **[Story]**: User story label: US1, US2, US3, US4
- Exact file paths required in every task description

---

## Phase 1: Setup

**Purpose**: Scaffold the new ML provider project and wire it into the solution.

- [ ] T001 Create `src/Providers/ML/TrashMailPanda.Providers.ML/TrashMailPanda.Providers.ML.csproj` targeting .NET 9.0 with `<Nullable>enable</Nullable>`, NuGet references `Microsoft.ML` 4.x and `Microsoft.ML.FastTree` 4.x and `Microsoft.Extensions.DependencyInjection.Abstractions` and `Microsoft.Extensions.Logging.Abstractions`, and a `<ProjectReference>` to `TrashMailPanda.Shared`
- [ ] T002 Add `TrashMailPanda.Providers.ML` project to `TrashMailPanda.sln` via `dotnet sln add src/Providers/ML/TrashMailPanda.Providers.ML/TrashMailPanda.Providers.ML.csproj`
- [ ] T003 [P] Create `src/Providers/ML/TrashMailPanda.Providers.ML/GlobalUsings.cs` with `global using` directives for `TrashMailPanda.Shared`, `TrashMailPanda.Shared.Base`, `TrashMailPanda.Providers.ML.Models`, `TrashMailPanda.Providers.ML.Config`, `Microsoft.ML`, `Microsoft.ML.Data`, `Microsoft.Extensions.Logging`, `Spectre.Console`

**Checkpoint**: `dotnet build src/Providers/ML/TrashMailPanda.Providers.ML` succeeds (empty project).

---

## Phase 2: Foundational (Blocking Prerequisites)

**Purpose**: Public interfaces (Shared), data model classes, provider config, DB schema migration, and main-app project reference. Every user story depends on these being complete.

**⚠️ CRITICAL**: No user story work can begin until this phase is complete.

- [ ] T004 [P] Create `src/Shared/TrashMailPanda.Shared/IMLModelProvider.cs` — transcribe from `specs/059-mlnet-training-pipeline/contracts/IMLModelProvider.cs`; expose `ClassifyActionAsync(EmailFeatureVector, CancellationToken)`, `ClassifyActionBatchAsync(IEnumerable<EmailFeatureVector>, CancellationToken)`, `GetModelVersionsAsync(string modelType, CancellationToken)`, `GetActiveModelVersionAsync(string modelType, CancellationToken)`, `RollbackAsync(string modelId, CancellationToken)`, `GetClassificationModeAsync(CancellationToken)`; all return `Result<T>` and never throw
- [ ] T005 [P] Create `src/Shared/TrashMailPanda.Shared/IModelTrainingPipeline.cs` — transcribe from `specs/059-mlnet-training-pipeline/contracts/IModelTrainingPipeline.cs`; expose `TrainActionModelAsync(TrainingRequest, IProgress<TrainingProgress>?, CancellationToken)`, `IncrementalUpdateActionModelAsync(IncrementalUpdateRequest, IProgress<TrainingProgress>?, CancellationToken)`, `GetActionTrainingDataSummaryAsync(CancellationToken)`, `ShouldRetrainAsync(CancellationToken)`, `PruneOldModelsAsync(string modelType, CancellationToken)`
- [ ] T006 [P] Create `src/Shared/TrashMailPanda.Shared/IActionClassifier.cs` — transcribe from `specs/059-mlnet-training-pipeline/contracts/IActionClassifier.cs`; expose `IsLoaded`, `LoadedModelId`, `LoadModelAsync(string modelFilePath, CancellationToken)`, `UnloadModel()`, `ClassifySingleAsync(EmailFeatureVector, CancellationToken)`, `ClassifyBatchAsync(IEnumerable<EmailFeatureVector>, CancellationToken)`
- [ ] T007 [P] Create nine ML model classes (one public type per file, `namespace TrashMailPanda.Providers.ML.Models`) in `src/Providers/ML/TrashMailPanda.Providers.ML/Models/`:
  - `ActionTrainingInput.cs` — 38 `float` ML.NET feature properties (`SenderKnown`, `ContactStrength`, `HasListUnsubscribe`, `HasAttachments`, `HourReceived`, `DayOfWeek`, `EmailSizeLog`, `SubjectLength`, `RecipientCount`, `IsReply`, and the remaining 28 domain features from `specs/059-mlnet-training-pipeline/data-model.md`), plus `string Label` (action ground truth: "Keep"/"Archive"/"Delete"/"Spam"); use `[LoadColumn]` / `[ColumnName]` attributes where required by ML.NET
  - `ActionPrediction.cs` — `string PredictedLabel`, `float[] Score`, `float Confidence` (max score value, normalized to [0,1]); apply `[ColumnName("PredictedLabel")]` and `[ColumnName("Score")]`
  - `ModelVersion.cs` — immutable record with properties matching `ml_models` table: `ModelId`, `ModelType`, `Version`, `TrainingDate`, `Algorithm`, `FeatureSchemaVersion`, `TrainingDataCount`, `Accuracy`, `MacroPrecision`, `MacroRecall`, `MacroF1`, `PerClassMetricsJson`, `IsActive`, `FilePath`, `Notes`
  - `TrainingMetricsReport.cs` — `string ModelId`, `string Algorithm`, `int TrainingDataCount`, `float Accuracy`, `float MacroPrecision`, `float MacroRecall`, `float MacroF1`, `IReadOnlyDictionary<string, ClassMetrics> PerClassMetrics`, `bool IsQualityAdvisory` (true when MacroF1 < threshold), `TimeSpan TrainingDuration`; nested `ClassMetrics` record with `Precision`, `Recall`, `F1`
  - `IncrementalUpdateRequest.cs` — `int MinNewCorrections` (default 50), `string TriggerReason`, `DateTimeOffset? LastTrainingDate`
  - `TrainingRequest.cs` — `string TriggerReason`, `bool ForceRetrain` (bypass minimum sample check)
  - `TrainingProgress.cs` — `string Phase`, `int PercentComplete` (0–100), `string Message`; define static phase constants: `PhaseLoading = "Loading"`, `PhaseBuildingPipeline = "BuildingPipeline"`, `PhaseTraining = "Training"`, `PhaseEvaluating = "Evaluating"`
  - `TrainingDataSummary.cs` — `int Available`, `int Required`, `bool IsReady`, `int Deficit` (computed as `Math.Max(0, Required - Available)`)
  - `ClassificationMode.cs` — `enum ClassificationMode { ColdStart, Hybrid, MlPrimary }`
- [ ] T008 [P] Create `src/Providers/ML/TrashMailPanda.Providers.ML/Config/MLModelProviderConfig.cs` — `namespace TrashMailPanda.Providers.ML.Config`; DataAnnotations-validated properties: `[Range(1,20)] int MaxModelVersions = 5`, `[Range(10,int.MaxValue)] int MinTrainingSamples = 100`, `[Range(2,4)] int MinDistinctClasses = 2`, `[Range(0.0,1.0)] double QualityAdvisoryF1Threshold = 0.70`, `[Range(0.5,1.0)] double DominantClassImbalanceThreshold = 0.80`
- [ ] T009 Apply schema version 6 migration in `src/Providers/Storage/TrashMailPanda.Providers.Storage/SqliteStorageProvider.cs` — add `ml_models` and `training_events` DDL (exact SQL from `specs/059-mlnet-training-pipeline/data-model.md` §1.1 and §1.2) to the `migrate()` method's version switch/case for version 6; insert `schema_version` row 6 after both tables; add `SqlitePCLRaw` nuget if needed; follow the existing migration pattern already used for versions 1–5
- [ ] T010 Add `<ProjectReference Include="../../../Providers/ML/TrashMailPanda.Providers.ML/TrashMailPanda.Providers.ML.csproj" />` to `src/TrashMailPanda/TrashMailPanda/TrashMailPanda.csproj` so the main app can resolve `IMLModelProvider` and `IModelTrainingPipeline`

**Checkpoint**: `dotnet build` (solution-wide) passes — all interfaces compile, model classes compile, schema migration compiles.

---

## Phase 3: User Story 1 — Train Email Action Classification Model (Priority: P1) 🎯 MVP

**Goal**: A user with ≥ 100 labeled `EmailFeatureVector` records stored by feature #55 can initiate action model training from the console, receive a trained Keep/Archive/Delete/Spam classifier stored on disk and versioned in the DB, and classify individual or batches of emails via `IMLModelProvider`.

**Independent Test**: Feed 200 pre-labeled `EmailFeatureVector` records (at least 2 distinct action labels) into `IModelTrainingPipeline.TrainActionModelAsync`; verify `Result.IsSuccess` is true, `TrainingMetricsReport` is returned, model `.zip` file exists on disk at the path recorded in `ml_models.FilePath`, one row with `IsActive=1` exists in `ml_models`, and `IMLModelProvider.ClassifyActionAsync` returns a prediction with a non-null `PredictedLabel` and `Confidence` in [0,1].

- [ ] T011 [P] [US1] Implement `FeaturePipelineBuilder` in `src/Providers/ML/TrashMailPanda.Providers.ML/Training/FeaturePipelineBuilder.cs` — `BuildPipeline(MLContext mlContext, IEstimator<ITransformer> trainer)` returns composite `IEstimator<ITransformer>`; steps: (1) `NormalizeMeanVariance` on all 38 float feature columns, (2) `Concatenate("Features", [all feature column names])`, (3) key-map `Label` column via `MapValueToKey`, (4) append the provided trainer estimator; expose static `string[] FeatureColumnNames` listing all 38 column names for use in tests and the trainer; no Spectre.Console dependencies
- [ ] T012 [P] [US1] Implement `ActionModelTrainer` in `src/Providers/ML/TrashMailPanda.Providers.ML/Training/ActionModelTrainer.cs` — `TrainAsync(MLContext mlContext, IDataView data, float dominantClassImbalanceThreshold, CancellationToken ct)` returns `Result<(ITransformer Model, string Algorithm, MulticlassClassificationMetrics Metrics)>`; logic: (1) compute `dominantClassRatio = maxClassCount / totalCount` across label column, (2) select `LightGbm` trainer if ratio > threshold else `SdcaMaximumEntropy`, (3) compute inverse-frequency class weights and add `ExampleWeightColumnName` column to data view, (4) split 80/20 train/validation, (5) delegate to `FeaturePipelineBuilder.BuildPipeline` and call `Fit()`, (6) evaluate on validation split; return `ValidationError` with descriptive message when < 2 distinct action classes exist
- [ ] T013 [US1] Implement `ModelVersionRepository` in `src/Providers/ML/TrashMailPanda.Providers.ML/Versioning/ModelVersionRepository.cs` — depends on `SqliteConnection` (injected); methods: `InsertVersionAsync(ModelVersion version)` → `Result<bool>`, `SetActiveAsync(string newModelId, string modelType)` → `Result<bool>` (single SQLite transaction: set `IsActive=0` for old active row, set `IsActive=1` for new row), `GetVersionsAsync(string modelType)` → `Result<IReadOnlyList<ModelVersion>>` ordered by `Version DESC`, `GetActiveVersionAsync(string modelType)` → `Result<ModelVersion>` (returns `ConfigurationError` when no active row), `AppendEventAsync(string eventType, string modelType, string? modelId, string detailsJson)` → `Result<bool>`; all use parameterized SQL; none throw exceptions
- [ ] T014 [US1] Implement `ActionClassifier` in `src/Providers/ML/TrashMailPanda.Providers.ML/Classification/ActionClassifier.cs` — implements `IActionClassifier`; fields: `MLContext _mlContext`, `PredictionEngine<ActionTrainingInput,ActionPrediction>? _engine`, `string? _loadedModelId`; `LoadModelAsync`: calls `mlContext.Model.Load(filePath, out _)`, creates `PredictionEngine`, updates `_loadedModelId`; `UnloadModel`: disposes engine, sets null; `ClassifySingleAsync`: maps `EmailFeatureVector` → `ActionTrainingInput`, calls `_engine.Predict()`, returns `ActionPrediction` with `Confidence = Score.Max()`; `ClassifyBatchAsync`: iterates over inputs calling `ClassifySingleAsync`; returns `InitializationError` with message `"Action classifier model not loaded"` when engine is null; thread-safety note: callers must ensure single-threaded access (documented in XML summary)
- [ ] T015 [US1] Implement `ModelTrainingPipeline` in `src/Providers/ML/TrashMailPanda.Providers.ML/Training/ModelTrainingPipeline.cs` — implements `IModelTrainingPipeline`; constructor injects `IEmailArchiveService`, `ModelVersionRepository`, `ModelVersionPruner`, `ActionModelTrainer`, `FeaturePipelineBuilder`, `MLModelProviderConfig`, `ILogger<ModelTrainingPipeline>`; `TrainActionModelAsync` orchestrates: (1) load vectors via `IEmailArchiveService.GetAllFeaturesAsync(FeatureSchema.CurrentVersion)`, validate count ≥ `MinTrainingSamples` and ≥ 2 distinct labels (return `ValidationError` with count message on failure), validate schema version (return `ValidationError` on mismatch), report `TrainingProgress` at 0%, (2) map `EmailFeatureVector` list to `ActionTrainingInput` and create `MLContext.Data.LoadFromEnumerable()`, report 20%, (3) invoke `ActionModelTrainer.TrainAsync`, report 30–80% during Fit (use `AnsiConsole.Status()` spinner if no `IProgress` is provided, otherwise report 80% after Fit), (4) write model to temp file path via `mlContext.Model.Save()`, on `CancellationToken.IsCancellationRequested` delete temp file and return `OperationCancelledError`, (5) `File.Move(tempPath, finalVersionedPath, overwrite: false)`, (6) insert `ModelVersion` via `ModelVersionRepository.InsertVersionAsync`, call `SetActiveAsync`, append `training_completed` event, report 100%; on any exception delete temp file, append `training_failed` event, return `Result.Failure`; never set `IsActive=1` before step 6 completes
- [ ] T016 [US1] Implement `MLModelProvider` in `src/Providers/ML/TrashMailPanda.Providers.ML/MLModelProvider.cs` — implements `IMLModelProvider` + `IProvider<MLModelProviderConfig>`; constructor injects `ActionClassifier`, `ModelVersionRepository`, `IModelTrainingPipeline`, `MLModelProviderConfig`, `ILogger<MLModelProvider>`; `InitializeAsync`: resolves model directory from `StorageProviderConfig.GetOsDefaultPath()`, runs schema migration check, loads active model via `ModelVersionRepository.GetActiveVersionAsync` → `ActionClassifier.LoadModelAsync` (logs warning and continues if no model exists yet); `ClassifyActionAsync`: delegates to `ActionClassifier.ClassifySingleAsync` when loaded, falls back to `Result.Success(new ActionPrediction { PredictedLabel = "Keep", Confidence = 0.5f })` when not loaded (rule-based fallback); `ClassifyActionBatchAsync`: iterates `ClassifyActionAsync` for each input; `GetClassificationModeAsync`: returns `ColdStart` when no model loaded, `MlPrimary` when model loaded (Hybrid logic for future ML phases); `RollbackAsync`: validates target model exists, `ModelVersionRepository.SetActiveAsync`, `ActionClassifier.LoadModelAsync(newModelFilePath)`, append `rollback` event; `GetModelVersionsAsync` / `GetActiveModelVersionAsync`: delegates to `ModelVersionRepository`; `HealthCheckAsync`: returns healthy when model file exists and `ActionClassifier.IsLoaded`, returns degraded (not failed) when no model trained yet
- [ ] T017 [P] [US1] Register services in DI container in `src/TrashMailPanda/TrashMailPanda/Program.cs`: `services.AddSingleton<IMLModelProvider, MLModelProvider>()`, `services.AddTransient<IModelTrainingPipeline, ModelTrainingPipeline>()`, `services.AddSingleton<ActionClassifier>()`, `services.AddSingleton<ModelVersionRepository>()`, `services.AddSingleton<ModelVersionPruner>()`, `services.AddSingleton<ActionModelTrainer>()`, `services.AddSingleton<FeaturePipelineBuilder>()`, `services.AddOptions<MLModelProviderConfig>().ValidateDataAnnotations()`
- [ ] T018 [P] [US1] Unit tests for `FeaturePipelineBuilder` in `src/Tests/TrashMailPanda.Tests/Unit/ML/FeaturePipelineBuilderTests.cs` — `[Trait("Category","Unit")]`; create synthetic `IDataView` with one row; call `BuildPipeline(mlContext, sdcaTrainer)`; verify the returned estimator chain contains `NormalizeMeanVariance`, `ColumnConcatenatingEstimator` with all 38 feature names, and `ValueToKeyMappingEstimator` for the Label column; verify `FeatureColumnNames.Length == 38`
- [ ] T019 [P] [US1] Unit tests for `ActionModelTrainer` in `src/Tests/TrashMailPanda.Tests/Unit/ML/ActionModelTrainerTests.cs` — `[Trait("Category","Unit")]`; verify `LightGbm` is selected when dominant class ratio > 0.80 (e.g. 90 Keep, 5 Archive, 3 Delete, 2 Spam); verify `SdcaMaximumEntropy` is selected when ratio ≤ 0.80 (balanced set); verify `Result.Failure` with `ValidationError` and helpful message when only 1 distinct class exists; verify class weights are inverse-frequency values (majority class weight < minority class weight); use small in-memory synthetic datasets so Fit() runs in < 2s

**Checkpoint**: After T019, US1 is independently functional — training produces a model, model classifies emails, all acceptance scenarios in spec §US1 are satisfiable.

---

## Phase 4: User Story 2 — View Model Training Progress and Metrics (Priority: P2)

**Goal**: A user initiating a training run sees incremental Spectre.Console progress updates for all four training phases and, on completion, a per-class metrics table; models with overall F1 < 0.70 display a `[yellow]⚠[/]` quality advisory.

**Independent Test**: Trigger `IModelTrainingPipeline.TrainActionModelAsync` with an `IProgress<TrainingProgress>` test subscriber; verify at least four distinct `Phase` values are reported; verify the returned `TrainingMetricsReport` contains `PerClassMetrics` entries for Keep, Archive, Delete, Spam; verify `IsQualityAdvisory = true` when MacroF1 is set to 0.65 in a mock report.

- [ ] T020 [US2] Implement `TrainingConsoleService` in `src/TrashMailPanda/TrashMailPanda/Services/TrainingConsoleService.cs` — `RunTrainingAsync(CancellationToken ct)` method: creates `AnsiConsole.Progress()` with four tasks matching `TrainingProgress` phase constants (Loading 0–20%, BuildingPipeline 20–30%, Training 30–80%, Evaluating 80–100%); wraps `IModelTrainingPipeline.TrainActionModelAsync` call with an `IProgress<TrainingProgress>` callback that updates the corresponding Spectre.Console progress task; uses `AnsiConsole.Status()` spinner with `[cyan]→ Training model...[/]` message during the blocking Fit phase (reported as `PhaseTraining`); on success renders `TrainingMetricsReport` via a Spectre.Console `Table`; on failure renders `[bold red]✗ Training failed:[/] [red]{error.Message}[/]`
- [ ] T021 [US2] Implement quality advisory and metrics table rendering in `TrainingConsoleService` in `src/TrashMailPanda/TrashMailPanda/Services/TrainingConsoleService.cs` — `RenderMetricsReport(TrainingMetricsReport report)` private method: builds Spectre.Console `Table` with columns Class / Precision / Recall / F1, adds one row per entry in `PerClassMetrics` dict, adds a footer row for macro averages; when `report.IsQualityAdvisory == true` prepends `AnsiConsole.MarkupLine("[yellow]⚠ Quality advisory: overall F1 = {value:F2} (below 0.70 threshold). Consider collecting more labeled data.[/]")`; when quality is acceptable prepends `AnsiConsole.MarkupLine("[green]✓ Model quality: overall F1 = {value:F2}[/]")`
- [ ] T022 [US2] Wire `--train-action` console verb to `TrainingConsoleService` in `src/TrashMailPanda/TrashMailPanda/Program.cs` — add argument parsing or menu option that instantiates `TrainingConsoleService` from DI and calls `RunTrainingAsync(cancellationToken)`; follow the existing console command pattern in Program.cs
- [ ] T023 [P] [US2] Unit tests for quality advisory rendering in `src/Tests/TrashMailPanda.Tests/Unit/ML/TrainingConsoleServiceTests.cs` — `[Trait("Category","Unit")]`; mock `IModelTrainingPipeline`; verify `[yellow]⚠` markup is included in output when `MacroF1 = 0.65`; verify `[green]✓` markup is included when `MacroF1 = 0.82`; verify all four `TrainingProgress.Phase` constant values are handled without throwing; capture `AnsiConsole` output via `AnsiConsole.Record()`

**Checkpoint**: After T023, US2 is independently verifiable — console shows live progress and a formatted metrics table.

---

## Phase 5: User Story 3 — Incrementally Update Models with New Corrections (Priority: P3)

**Goal**: After initial training, when ≥ 50 new user corrections have accumulated, `IncrementalUpdateActionModelAsync` triggers a full retrain on the merged dataset without requiring the user to initiate a full training run; requests with < 50 new corrections are declined with a helpful message.

**Independent Test**: Train a baseline model (phase 3 complete); mock `IEmailArchiveService` to return 60 vectors with `UserCorrected=1` timestamped after last training; call `IncrementalUpdateActionModelAsync`; verify a new `ModelVersion` is created with `Version = previousVersion + 1` and `IsActive=1`; repeat with only 30 corrections and verify `Result.Failure` with `ValidationError` and a message including the correction count.

- [ ] T024 [US3] Implement `IncrementalUpdateService` in `src/Providers/ML/TrashMailPanda.Providers.ML/Training/IncrementalUpdateService.cs` — `UpdateAsync(IncrementalUpdateRequest request, IProgress<TrainingProgress>? progress, CancellationToken ct)` returns `Result<TrainingMetricsReport>`; logic: (1) query `ModelVersionRepository.GetActiveVersionAsync` to get `LastTrainingDate`, (2) count new user corrections via `IEmailArchiveService` since that date, (3) return `ValidationError` with message `"Insufficient new corrections: {count} of {request.MinNewCorrections} required. Wait for more user feedback before updating."` when count < MinNewCorrections, (4) load all feature vectors and merge with new corrections (deduplicating by EmailId), (5) delegate full retrain to `ActionModelTrainer.TrainAsync` on merged dataset, (6) persist new version via `ModelVersionRepository`
- [ ] T025 [US3] Wire `IncrementalUpdateActionModelAsync` in `ModelTrainingPipeline` to delegate to `IncrementalUpdateService` in `src/Providers/ML/TrashMailPanda.Providers.ML/Training/ModelTrainingPipeline.cs`; implement `ShouldRetrainAsync` in same file — returns `true` when (≥ 50 corrections since last training date) OR (TrainingDate > 7 days ago and any labeled data exists); implement `GetActionTrainingDataSummaryAsync` — queries `IEmailArchiveService` for total labeled vector count and returns `TrainingDataSummary` with `Available`, `Required = MinTrainingSamples`, `IsReady`, `Deficit`
- [ ] T026 [P] [US3] Unit tests for `IncrementalUpdateService` in `src/Tests/TrashMailPanda.Tests/Unit/ML/IncrementalUpdateServiceTests.cs` — `[Trait("Category","Unit")]`; mock `IEmailArchiveService` and `ModelVersionRepository`; verify `ValidationError` is returned when mock returns 30 new corrections (< 50); verify `TrainAsync` is invoked on `ActionModelTrainer` with merged dataset when mock returns 60 new corrections; verify returned `TrainingMetricsReport.ModelId` reflects the new version ID; verify `ShouldRetrainAsync` returns `true` when 50+ corrections accumulated

**Checkpoint**: After T026, US3 is independently verifiable — incremental updates trigger only when sufficient corrections are available.

---

## Phase 6: User Story 4 — Store, Version, and Manage Trained Models (Priority: P3)

**Goal**: Multiple training runs are stored as separate versioned model files; users can list all versions, roll back to a prior version within the retention window, and models beyond the retention limit (5) are automatically pruned with metadata preserved.

**Independent Test**: Train 6 model versions sequentially (using small synthetic datasets); call `GetModelVersionsAsync`; verify exactly 5 versions remain (oldest deleted), each with a `FilePath` that exists on disk except the pruned one; call `RollbackAsync(secondNewestModelId)`; verify it becomes `IsActive=1`; verify the formerly active model is now `IsActive=0`.

- [ ] T027 [US4] Implement `ModelVersionPruner` in `src/Providers/ML/TrashMailPanda.Providers.ML/Versioning/ModelVersionPruner.cs` — `PruneAsync(string modelType, int maxVersions, CancellationToken ct)` returns `Result<int>` (count pruned); logic: (1) `GetVersionsAsync` ordered by `Version DESC`, (2) skip the first `maxVersions` entries, (3) for each excess version: skip if `IsActive=1` (safeguard), call `File.Delete(resolvedFilePath)` ignoring `FileNotFoundException`, update `ml_models` row to set `FilePath = ''` and append note `"Pruned on {DateTime.UtcNow}"`, (4) append `pruned` event via `ModelVersionRepository.AppendEventAsync` with `Details` JSON containing deleted `ModelId` and original `FilePath`; return count of deleted files
- [ ] T028 [US4] Wire `PruneOldModelsAsync` in `ModelTrainingPipeline` at `src/Providers/ML/TrashMailPanda.Providers.ML/Training/ModelTrainingPipeline.cs` — call `ModelVersionPruner.PruneAsync(modelType, config.MaxModelVersions, ct)` immediately after `ModelVersionRepository.SetActiveAsync` succeeds; log pruned count; pruning failure is non-fatal (log warning, do not fail the training result)
- [ ] T029 [US4] Implement `RollbackAsync` on `MLModelProvider` in `src/Providers/ML/TrashMailPanda.Providers.ML/MLModelProvider.cs` — validate `modelId` exists via `ModelVersionRepository.GetVersionsAsync`; return `ValidationError` when not found or already active; call `ModelVersionRepository.SetActiveAsync(modelId, modelType)` in a transaction; resolve physical file path for new active model; call `ActionClassifier.LoadModelAsync(filePath)` to hot-swap the in-process model; append `rollback` audit event with `Details = { "from": oldModelId, "to": modelId }`; return updated `ModelVersion`
- [ ] T030 [US4] Implement `GetModelVersionsAsync` and `GetActiveModelVersionAsync` on `MLModelProvider` in `src/Providers/ML/TrashMailPanda.Providers.ML/MLModelProvider.cs` — both delegate directly to `ModelVersionRepository.GetVersionsAsync` and `GetActiveVersionAsync` respectively; `GetActiveModelVersionAsync` returns `ConfigurationError` with message `"No action model has been trained yet"` when repository returns no active row
- [ ] T031 [P] [US4] Unit tests for `ModelVersionRepository` in `src/Tests/TrashMailPanda.Tests/Unit/ML/ModelVersionRepositoryTests.cs` — `[Trait("Category","Unit")]`; use in-memory SQLite (`Data Source=:memory:`), create schema before each test; verify `InsertVersionAsync` round-trips all fields; verify `SetActiveAsync` sets exactly one `IsActive=1` row per `ModelType` (partial unique index); verify `GetVersionsAsync` returns newest-first order; verify `AppendEventAsync` writes the correct `EventType` and `Details` JSON; verify `GetActiveVersionAsync` returns `ConfigurationError` when table is empty
- [ ] T032 [P] [US4] Integration test for `MLModelProvider` in `src/Tests/TrashMailPanda.Tests/Integration/ML/MLModelProviderIntegrationTests.cs` — `[Trait("Category","Integration")] [Fact(Skip="Requires ML.NET full training — run manually: dotnet test --filter FullyQualifiedName~MLModelProviderIntegrationTests")]`; full round-trip: initialize provider against temp directory, train with synthetic 150-vector dataset, assert `GetModelVersionsAsync` returns 1 version, train again and assert 2 versions, rollback to first and assert `IsActive=1`, train 4 more times and assert only 5 versions retained

**Checkpoint**: After T032, US4 is independently verifiable — listing, rollback, and automatic pruning all work correctly.

---

## Phase 7: Polish & Cross-Cutting Concerns

**Purpose**: Crash-safety validation, edge-case tests (FR-012, FR-016, FR-023), build and format verification, coverage confirmation.

- [ ] T033 [P] Add crash-safety unit test in `src/Tests/TrashMailPanda.Tests/Unit/ML/ModelTrainingPipelineTests.cs` — cancel training via `CancellationTokenSource.Cancel()` after the temp file write step (use a mock `ActionModelTrainer` that blocks on a `TaskCompletionSource` until signal, then cancel); assert temp file does not exist after cancellation; assert `ml_models` table contains no new `IsActive=1` row; assert previous `ModelVersion.IsActive` is unchanged
- [ ] T034 [P] Add minimum-data validation test in `src/Tests/TrashMailPanda.Tests/Unit/ML/ModelTrainingPipelineTests.cs` — mock `IEmailArchiveService` returning 40 vectors; call `TrainActionModelAsync`; assert `Result.IsFailure` with `ValidationError`; assert error `Message` contains the string `"40"` (available) and `"100"` (required) per FR-016
- [ ] T035 [P] Add schema version mismatch test in `src/Tests/TrashMailPanda.Tests/Unit/ML/ModelTrainingPipelineTests.cs` — mock `IEmailArchiveService.GetAllFeaturesAsync` returning vectors with `FeatureSchemaVersion = FeatureSchema.CurrentVersion - 1`; call `TrainActionModelAsync`; assert `Result.IsFailure` with `ValidationError` message containing `"schema version"` per FR-012
- [ ] T036 Run `dotnet build TrashMailPanda.sln` and fix any compilation errors in all new files under `src/Providers/ML/TrashMailPanda.Providers.ML/` and `src/Shared/TrashMailPanda.Shared/`
- [ ] T037 [P] Run `dotnet format TrashMailPanda.sln --verify-no-changes`; fix any style violations in `src/Providers/ML/TrashMailPanda.Providers.ML/`, `src/Shared/TrashMailPanda.Shared/`, and `src/TrashMailPanda/TrashMailPanda/Services/TrainingConsoleService.cs`
- [ ] T038 [P] Run `dotnet test --filter Category=Unit` and verify all unit tests pass; run `dotnet test --collect:"XPlat Code Coverage"` and confirm coverage ≥ 95% for `MLModelProvider`, `ModelTrainingPipeline`, `ActionModelTrainer`, `ModelVersionRepository` as specified in FR-007 of the spec

---

## Dependency Graph

```
Phase 1: Setup        → Phase 2: Foundational
Phase 2: Foundational → Phase 3: US1 (P1)
Phase 3: US1         → Phase 4: US2 (P2)
Phase 3: US1         → Phase 5: US3 (P3)   [runs in parallel with Phase 4]
Phase 3: US1         → Phase 6: US4 (P3)   [runs in parallel with Phase 4]
Phase 4 + 5 + 6      → Phase 7: Polish
```

**User story dependency summary**:

| Story | Depends On | Blocks |
|-------|-----------|--------|
| US1 (P1) — Core training | Foundational phase | US2, US3, US4 |
| US2 (P2) — Progress display | US1 (IModelTrainingPipeline) | — |
| US3 (P3) — Incremental update | US1 (ModelTrainingPipeline, ActionModelTrainer) | — |
| US4 (P3) — Version management | US1 (ModelVersionRepository) | — |

---

## Parallel Execution Per Phase

**Phase 2** (all parallel after T001/T002):
```
T004, T005, T006, T007, T008 → all parallel (different files, no cross-deps)
T009 → independent (storage provider migration)
T010 → independent (csproj update)
```

**Phase 3** (after Phase 2):
```
T011 (FeaturePipelineBuilder), T012 (ActionModelTrainer)  → parallel pair
T013 (ModelVersionRepository), T014 (ActionClassifier)    → parallel pair
T015 (ModelTrainingPipeline)  → after T011, T012, T013, T014
T016 (MLModelProvider)        → after T014, T015
T017, T018, T019              → parallel after T016
```

**Phase 5 and Phase 6** → can run in parallel with each other after Phase 3 completes.

---

## Implementation Strategy

**MVP Scope** (Phases 1–3 only):

Delivers a fully functional action classifier: install the ML project, define interfaces, apply the schema migration, train an action model, classify individual and batched emails, and list one model version. All five acceptance scenarios in US1 are satisfied. No progress display, no incremental updates, no rollback UI — all deferred to phases 4–6.

**Recommended delivery commits**:

| Commit | Phases | Message |
|--------|--------|---------|
| 1 | 1–2 | `feat: scaffold TrashMailPanda.Providers.ML and schema v6 migration` |
| 2 | 3 | `feat: implement action model training and classification (US1)` |
| 3 | 4 | `feat: add real-time training progress and metrics display (US2)` |
| 4 | 5–6 | `feat: add incremental model updates and version management (US3, US4)` |
| 5 | 7 | `chore: crash safety tests, build validation, and coverage check` |

---

## Summary

| Metric | Count |
|--------|-------|
| Total tasks | 38 |
| Phase 1 (Setup) | 3 |
| Phase 2 (Foundational) | 7 |
| Phase 3 — US1 (P1) | 9 |
| Phase 4 — US2 (P2) | 4 |
| Phase 5 — US3 (P3) | 3 |
| Phase 6 — US4 (P3) | 6 |
| Phase 7 (Polish) | 6 |
| Tasks with [P] (parallelizable) | 22 |
| Integration tests | 1 (T032, skip by default) |
| Unit test tasks | 9 (T018, T019, T023, T026, T031, T033, T034, T035) |

**Suggested MVP**: Phases 1–3 (T001–T019) deliver a fully working action classifier.
