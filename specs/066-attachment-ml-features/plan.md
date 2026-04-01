# Implementation Plan: Attachment Metadata for ML Email Features

**Branch**: `066-attachment-ml-features` | **Date**: 2026-03-30 | **Spec**: [spec.md](spec.md)
**Input**: Feature specification from `/specs/066-attachment-ml-features/spec.md`

## Summary

Extend the ML feature store (`email_features` table) with nine new attachment-metadata columns: attachment count, total attachment size (log-scale), and seven boolean type flags (document, image, audio, video, XML, binary, other). Increment `FeatureSchema.CurrentVersion` from 1 to 2 so rows without the new columns are identified as outdated. Add a startup check in `ConsoleStartupOrchestrator` that detects outdated rows and automatically re-triggers a full Gmail re-scan with a Spectre.Console status message explaining why. Switch feature extraction in `GmailTrainingDataService` from `format=METADATA` to `format=FULL` (required to access `payload.Parts` for attachment MIME types and sizes). Introduce a new `AttachmentMimeClassifier` static class implementing the seven-category MIME taxonomy specified in FR-006. Update `ActionTrainingInput`, `FeaturePipelineBuilder`, and both `MapToTrainingInput` methods in `ModelTrainingPipeline` and `IncrementalUpdateService` so the ML training pipeline accepts the new columns.

## Technical Context

**Language/Version**: .NET 9.0, C# 12 (nullable reference types enabled)  
**Primary Dependencies**: Google.Apis.Gmail.v1, ML.NET (Microsoft.ML), Microsoft.Data.Sqlite + SQLitePCLRaw.bundle_e_sqlcipher, EF Core with custom Migrations, Spectre.Console, Microsoft.Extensions.Hosting/DI/Logging, Polly  
**Storage**: SQLite (SQLCipher-encrypted); schema changes via EF Core migration system; `email_features` table managed by `TrashMailPandaDbContext`  
**Testing**: xUnit + Moq + coverlet; traits `[Trait("Category", "Unit")]` / `[Trait("Category", "Integration")]`  
**Target Platform**: Cross-platform desktop (macOS, Windows, Linux)  
**Project Type**: Console TUI application  
**Performance Goals**: Full re-scan of ~10k emails; attachment extraction must keep per-email latency comparable to existing METADATA fetch; `StoreFeaturesBatchAsync` handles 1000 vectors in <5s (existing baseline)  
**Constraints**: Gmail API quota (150 units/user/second); `format=FULL` responses are larger than `METADATA` but METADATA does NOT expose `payload.Parts` — FULL is required; no additional per-email API round-trip acceptable  
**Scale/Scope**: ~10k emails per user; 9 new feature columns; FeatureSchema version 1 → 2

## Constitution Check

*GATE: Must pass before Phase 0 research. Re-check after Phase 1 design.*

| Principle | Status | Notes |
|-----------|--------|-------|
| I. Provider-Agnostic Architecture | ✅ PASS | `GmailTrainingDataService` is behind `IGmailTrainingDataService`; `AttachmentMimeClassifier` is pure domain logic with no provider coupling |
| II. Result Pattern (NON-NEGOTIABLE) | ✅ PASS | All new methods return `Result<T>`; `HasOutdatedFeaturesAsync` returns `Result<bool>`; no exceptions thrown from service code |
| III. Security First (NON-NEGOTIABLE) | ✅ PASS | Attachment MIME types and sizes are not sensitive credentials; no new token storage; all DB queries parameterized via EF Core |
| IV. One Public Type Per File | ✅ PASS | `AttachmentMimeClassifier`, new migration class, and `AttachmentFeatures` record each get their own file |
| V. Strict Null Safety | ✅ PASS | New fields are non-nullable value types (`int`, `float`); EF Core column defaults set to 0 |
| VI. Test Coverage & Quality Gates | ✅ PASS | Unit tests required for `AttachmentMimeClassifier` (100% coverage — it's the core classification logic), `BuildFeatureVector` attachment path, and startup re-scan detection |

**No constitution violations.** No Complexity Tracking entry needed.

## Project Structure

### Documentation (this feature)

```text
specs/066-attachment-ml-features/
├── plan.md              # This file
├── research.md          # Phase 0 output
├── data-model.md        # Phase 1 output
├── quickstart.md        # Phase 1 output
├── contracts/           # Phase 1 output
│   └── attachment-feature-interfaces.md
└── tasks.md             # Phase 2 output (/speckit.tasks — NOT created here)
```

### Source Code (repository root)

```text
src/
├── Providers/
│   ├── Email/
│   │   └── TrashMailPanda.Providers.Email/
│   │       └── Services/
│   │           ├── GmailTrainingDataService.cs    [MODIFY] Switch FetchMessageAsync to FULL format;
│   │           │                                           update BuildFeatureVector to extract attachment features
│   │           └── AttachmentMimeClassifier.cs    [NEW]    Static pure-function MIME → 7-category classifier
│   ├── ML/
│   │   └── TrashMailPanda.Providers.ML/
│   │       ├── Models/
│   │       │   └── ActionTrainingInput.cs         [MODIFY] Add 9 new float attachment fields (+ AttachmentCount, TotalAttachmentSizeLog)
│   │       └── Training/
│   │           ├── FeaturePipelineBuilder.cs      [MODIFY] Add 9 new column names to NumericFeatureColumnNames
│   │           ├── ModelTrainingPipeline.cs       [MODIFY] MapToTrainingInput: map 9 new fields
│   │           └── IncrementalUpdateService.cs    [MODIFY] MapToTrainingInput: map 9 new fields
│   └── Storage/
│       └── TrashMailPanda.Providers.Storage/
│           ├── Models/
│           │   ├── EmailFeatureVector.cs          [MODIFY] Add 9 new properties (int/float)
│           │   └── FeatureSchema.cs               [MODIFY] CurrentVersion: 1 → 2
│           ├── EmailArchiveService.cs             [MODIFY] Add HasOutdatedFeaturesAsync()
│           ├── IEmailArchiveService.cs            [MODIFY] Add HasOutdatedFeaturesAsync() to interface
│           ├── TrashMailPandaDbContext.cs         [MODIFY] Fluent config: column defaults for 9 new columns
│           └── Migrations/
│               └── 20260330000000_AddAttachmentMlFeatures.cs  [NEW] EF Core migration
└── TrashMailPanda/
    └── TrashMailPanda/
        └── Services/
            └── Console/
                └── ConsoleStartupOrchestrator.cs  [MODIFY] Add schema-version check + re-scan trigger after migrations

src/Tests/
└── TrashMailPanda.Tests/
    └── Unit/
        ├── Email/
        │   └── AttachmentMimeClassifierTests.cs   [NEW]
        └── Storage/
            └── EmailArchiveServiceAttachmentTests.cs  [NEW]
```

**Structure Decision**: Single-project layout — changes span Email, ML, and Storage provider projects plus the main console host. All under the existing `src/` tree; no new projects added.
