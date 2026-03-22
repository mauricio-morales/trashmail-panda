# Implementation Plan: Runtime Classification with User Feedback Loop

**Branch**: `062-runtime-classification-feedback` | **Date**: 2026-03-21 | **Spec**: [spec.md](spec.md)  
**Input**: Feature specification from `/specs/062-runtime-classification-feedback/spec.md`

## Summary

Add confidence-based auto-apply, model quality monitoring, bootstrap Starred/Important→Keep signals, and auto-apply review/undo to the existing email triage infrastructure. ~60% of the underlying infrastructure is already implemented (classification service, triage service, training pipeline, scan infrastructure). This plan scopes only the remaining gaps: 3 new services (`IAutoApplyService`, `IModelQualityMonitor`, `IAutoApplyUndoService`), 7 new model types, extensions to the console triage loop, and a post-scan bootstrap enhancement for Starred/Important emails.

## Technical Context

**Language/Version**: C# 12 / .NET 9.0 (nullable reference types enabled)  
**Primary Dependencies**: Spectre.Console (TUI), Microsoft.Extensions.DI/Logging, ML.NET, Google.Apis.Gmail.v1, Polly  
**Storage**: SQLite with SQLCipher encryption (existing `email_features` table, `IEmailArchiveService`)  
**Testing**: xUnit + Moq + coverlet (90% coverage target, 95% for providers)  
**Target Platform**: Cross-platform console (macOS, Linux, Windows)  
**Project Type**: Console-first TUI desktop app  
**Performance Goals**: Auto-apply evaluation <10ms per email; quality metric computation <500ms  
**Constraints**: No new database schema changes; auto-apply session log is ephemeral (in-memory only)  
**Scale/Scope**: Single-user local app; handle batches of 25-50 emails per triage page

## Constitution Check

*GATE: Must pass before Phase 0 research. Re-check after Phase 1 design.*

### I. Provider-Agnostic Architecture — PASS
- New services (`IAutoApplyService`, `IModelQualityMonitor`, `IAutoApplyUndoService`) are interfaces with DI registration
- No direct dependency on Gmail API — delegates to existing `IEmailProvider` and `IEmailArchiveService`
- Auto-apply config persisted via existing `ISecureStorageManager` abstraction

### II. Result Pattern (NON-NEGOTIABLE) — PASS
- All async service methods return `Result<T>`
- `IAutoApplyService.GetConfigAsync()` → `Result<AutoApplyConfig>`
- `IModelQualityMonitor.GetMetricsAsync()` → `Result<ModelQualityMetrics>`
- `IAutoApplyUndoService.UndoAsync()` → `Result<bool>`
- Synchronous evaluation methods (`ShouldAutoApply`, `IsActionRedundant`, `RecordDecision`) are pure logic — no exceptions

### III. Security First (NON-NEGOTIABLE) — PASS
- Auto-apply config (threshold, enabled flag) persisted via `ISecureStorageManager` (OS keychain)
- No new sensitive data introduced — email content stays in existing encrypted storage
- No logging of email content or subject lines in quality metrics
- Parameterized queries for all DB aggregation in `ModelQualityMonitor`

### IV. MVVM with CommunityToolkit.Mvvm — N/A
- This feature is console-only (Spectre.Console). No Avalonia UI changes.
- Console output uses Spectre.Console semantic color markup (`[green]`, `[red]`, `[yellow]`, `[cyan]`)

### V. One Public Type Per File — PASS
- 7 new model files (one public record/class each)
- 3 new service interfaces + 3 implementations (6 files)
- 1 new enum file

### VI. Strict Null Safety — PASS
- `QualityWarning?` nullable return from `CheckForWarningAsync` (null = no warning)
- `AutoApplyLogEntry.UndoneToAction` explicitly `string?`
- All new types use explicit nullability annotations

### VII. Test Coverage & Quality Gates — PASS
- Unit tests for all 3 new services (threshold boundary, rolling window, undo mapping)
- Integration tests for console triage loop with auto-apply
- Existing `email_features` schema unchanged — no migration tests needed

## Project Structure

### Documentation (this feature)

```text
specs/062-runtime-classification-feedback/
├── plan.md              # This file
├── research.md          # Phase 0: codebase investigation + design decisions
├── data-model.md        # Phase 1: new + extended entity definitions
├── quickstart.md        # Phase 1: implementation overview + integration guide
├── contracts/           # Phase 1: service interface contracts
│   ├── IAutoApplyService.md
│   ├── IModelQualityMonitor.md
│   ├── IAutoApplyUndoService.md
│   └── ConsoleUIExtensions.md
├── checklists/
│   └── requirements.md  # Requirements checklist
└── tasks.md             # Phase 2 output (/speckit.tasks command)
```

### Source Code (repository root)

```text
src/TrashMailPanda/TrashMailPanda/
├── Models/
│   └── Console/
│       ├── AutoApplyConfig.cs              # NEW: user settings
│       ├── AutoApplyLogEntry.cs            # NEW: session log entry
│       ├── AutoApplySessionSummary.cs      # NEW: session aggregate stats
│       ├── ModelQualityMetrics.cs          # NEW: quality snapshot
│       ├── ActionCategoryMetrics.cs        # NEW: per-action breakdown
│       ├── QualityWarning.cs               # NEW: proactive warning
│       ├── QualityWarningSeverity.cs       # NEW: enum (Info/Warning/Critical)
│       ├── EmailTriageSession.cs           # MODIFY: add auto-apply tracking fields
│       └── TriageSessionSummary.cs         # MODIFY: add auto-apply counts
├── Services/
│   ├── IAutoApplyService.cs                # NEW: interface
│   ├── AutoApplyService.cs                 # NEW: implementation
│   ├── IModelQualityMonitor.cs             # NEW: interface
│   ├── ModelQualityMonitor.cs              # NEW: implementation
│   ├── IAutoApplyUndoService.cs            # NEW: interface
│   ├── AutoApplyUndoService.cs             # NEW: implementation
│   ├── ServiceCollectionExtensions.cs      # MODIFY: register new services
│   └── Console/
│       └── EmailTriageConsoleService.cs    # MODIFY: auto-apply flow, warnings, review/undo
└── ...

src/Providers/Email/TrashMailPanda.Providers.Email/
└── Services/
    └── GmailTrainingDataService.cs         # MODIFY: post-scan Starred/Important→Keep

src/Tests/
└── TrashMailPanda.Tests/
    └── Unit/
        ├── AutoApplyServiceTests.cs        # NEW
        ├── ModelQualityMonitorTests.cs     # NEW
        └── AutoApplyUndoServiceTests.cs    # NEW
```

**Structure Decision**: Follows established project layout. All new services go in `src/TrashMailPanda/TrashMailPanda/Services/` with corresponding model types in `Models/Console/`. No new projects — everything fits within the existing solution structure.

## Implementation Status Assessment

### Already Implemented (REUSE AS-IS)

| Component | Status | Notes |
|-----------|--------|-------|
| `IClassificationService` + `ClassificationService` | ✅ Complete | Single + batch ML inference with confidence |
| `IEmailTriageService` + `EmailTriageService` | ✅ Complete | Dual-write pattern (Gmail + training label) |
| `EmailTriageConsoleService` | ✅ Complete | Cold-start + AI-assisted triage loop, confidence colors |
| `ITrainingSignalAssigner` + `TrainingSignalAssigner` | ✅ Complete | 8-rule Trash→Delete, Spam→AutoDelete |
| `GmailTrainingDataService` | ✅ Complete | Initial + incremental scan, resumable, rate-limited |
| `IncrementalUpdateService` | ✅ Complete | Retrain trigger at ≥50 corrections |
| `ModelTrainingPipeline` | ✅ Complete | Full + incremental training, readiness checks |
| `IEmailArchiveService` | ✅ Complete | Training labels, untriaged queue, correction storage |
| `EmailFeatureVector` | ✅ Complete | 38 features + TrainingLabel + UserCorrected + IsStarred + IsImportant |
| `ScanProgressEntity` + `IScanProgressRepository` | ✅ Complete | Per-folder scan cursors, checkpoint/resume |
| `IApplicationOrchestrator` + event system | ✅ Complete | Event-driven UI updates |
| `EmailTriageSession` | ✅ Complete | Session state with action counts, override tracking |

### Gaps to Implement (THIS SPEC)

| Gap | Priority | FR Coverage | New Files |
|-----|----------|-------------|-----------|
| Auto-apply service + config | P1 | FR-001,002,003,004,023,024 | 5 files (interface, impl, 3 models) |
| Auto-apply in console triage loop | P1 | FR-001,003,004 | Modify EmailTriageConsoleService |
| Auto-disable on low accuracy | P1 | FR-005,025 | Part of IModelQualityMonitor |
| Bootstrap Starred/Important→Keep | P1 | FR-006,007,008 | Modify GmailTrainingDataService |
| Model quality monitoring service | P2 | FR-012,013,014,015,016,025,026 | 5 files (interface, impl, 3 models) |
| Quality warnings in console UI | P2 | FR-014,025,026 | Modify EmailTriageConsoleService |
| Per-action performance display | P2 | FR-015,016 | Part of quality monitor + console |
| Auto-apply review/undo | P3 | FR-017,018,019 | 3 files (interface, impl, 1 model) |

### Already Covered (NO WORK NEEDED)

| Requirement | Already Covered By |
|-------------|-------------------|
| FR-006: Scan Trash→Delete | `TrainingSignalAssigner` Rule 1 (Spam) + Rule 4 (Trash) |
| FR-009: Idempotent scan | `ScanProgressEntity` with per-folder cursors + upsert semantics |
| FR-010: Preserve partial results | `GmailTrainingDataService` checkpoint at batch commit |
| FR-011: Bootstrap progress display | `IProgress<ScanProgressUpdate>` already wired |
| FR-020: Cold-start fallback | `MLModelProvider` returns rule-based "Keep/50%" when no model |
| FR-021: Gmail API failure handling | `EmailTriageService.ApplyDecisionAsync` dual-write pattern |
| FR-022: Rate limit handling | `GmailTrainingDataService` exponential backoff + `IGmailRateLimitHandler` |

## Complexity Tracking

No constitution violations. All new code follows existing patterns:
- Services behind interfaces with DI registration
- Result<T> returns on all async methods
- One public type per file
- Spectre.Console semantic colors for console output
- No new database schema
