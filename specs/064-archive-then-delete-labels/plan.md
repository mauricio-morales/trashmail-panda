# Implementation Plan: Archive-Then-Delete Training Labels (064)

**Branch**: `064-archive-then-delete-labels` | **Date**: 2026-03-24 | **Spec**: [spec.md](./spec.md)
**Input**: Feature specification from `/specs/064-archive-then-delete-labels/spec.md`

## Summary

Three existing time-bounded triage labels — `Archive for 30d`, `Archive for 1y`, `Archive for 5y` — are purely cosmetic today: they all execute a plain Archive. This feature gives them real semantics across three integration points:

1. **Action execution** (P1): When a user confirms a time-bounded label, compute the email's current age from `ReceivedDateUtc`. If age ≥ threshold → execute Delete; otherwise → Archive. Either way, the `training_label` stored in `email_features` is always the content-based label (e.g. `Archive for 30d`), never `Delete`.
2. **Retention enforcement** (P2): A new background service (`RetentionEnforcementService`) periodically queries `email_features` for archived emails whose `training_label` is a time-bounded variant and whose `received_date_utc` + threshold has elapsed, then deletes them from Gmail without modifying `training_label`.
3. **ML training** (P3): `EmailAgeDays` semantics differ by context — for **existing training rows**, the stored `email_age_days` (= age at decision time) is correct as-is and must not be recomputed; for **brand-new emails at inference time** (not yet in the DB), `EmailAgeDays` is computed fresh from `ReceivedDateUtc` immediately before calling `ClassifyActionAsync`.

No schema migration is required: `received_date_utc` already exists and is indexed; `training_label` already accepts the three new values; `IEmailArchiveService.SetTrainingLabelAsync` already persists them.

## Technical Context

**Language/Version**: C# 12 / .NET 9.0  
**Primary Dependencies**: Spectre.Console (TUI), Microsoft.Extensions.Hosting (BackgroundService), Google.Apis.Gmail.v1 (IEmailProvider), Microsoft.EntityFrameworkCore + SQLitePCLRaw.bundle_e_sqlcipher (storage), xUnit + Moq (testing)  
**Storage**: SQLite / SQLCipher — `email_features` table (schema v5); `received_date_utc` column already present and indexed; no schema change needed  
**Testing**: xUnit with `[Trait("Category","Unit")]` and `[Trait("Category","Integration")]`; integration tests skip by default (require live Gmail OAuth)  
**Target Platform**: Cross-platform console app (.NET 9.0, macOS / Linux / Windows)  
**Project Type**: Console application with background hosted services  
**Performance Goals**: Retention scan processes up to 1,000 emails per batch; each individual Gmail delete ≤ 500ms; full scan completes in ≤ 30s for a typical mailbox; startup prompt check (read `last_scan_utc`) ≤ 50ms  
**Constraints**: `training_label` MUST NOT be modified by the retention scan; for action-execution threshold checks and inference on new emails, current email age MUST be computed fresh from `received_date_utc` (not from the stored `email_age_days` snapshot); for training, `email_age_days` is used as-is (= age at decision time); scan must continue on per-email failures (no full-batch abort); scan is user-prompted at startup (not a continuous timer); `last_scan_utc` persisted in SQLite `config` table  
**Scale/Scope**: Affects two production services (`EmailTriageService`, `BulkOperationService`) and adds one new background service; no new database tables

## Constitution Check

*GATE: Must pass before Phase 0 research. Re-check after Phase 1 design.*

| # | Principle | Status | Notes |
|---|-----------|--------|-------|
| I | Provider-Agnostic Architecture | ✅ PASS | Gmail deletes routed through `IEmailProvider`; storage via `IEmailArchiveService`; new service injected via DI |
| II | Result Pattern | ✅ PASS | All new methods return `Result<T>`; no exceptions thrown from service or provider code |
| III | Security First | ✅ PASS | No new credential paths; scan uses existing authenticated `IEmailProvider`; no new data logged |
| IV | One Public Type Per File | ✅ PASS | `RetentionEnforcementService`, `IRetentionEnforcementService`, `RetentionScanResult`, `LabelThresholds` each in their own file |
| V | Strict Null Safety | ✅ PASS | All new types use explicit nullable annotations; `ReceivedDateUtc` is `DateTime?` and null is handled gracefully (skip email) |
| VI | Test Coverage | ✅ PASS | Unit tests for threshold logic, action routing, and scan behavior; integration tests gated with `Skip` |

**Post-Phase-1 re-check**: ✅ All gates still pass — design introduces one new background service and modifies two existing method bodies; no principle violations.

## Project Structure

### Documentation (this feature)

```text
specs/064-archive-then-delete-labels/
├── plan.md              # This file
├── research.md          # Phase 0 output
├── data-model.md        # Phase 1 output
├── quickstart.md        # Phase 1 output
├── contracts/
│   └── IRetentionEnforcementService.md   # Phase 1 output
└── tasks.md             # Phase 2 output (/speckit.tasks)
```

### Source Code (repository root)

```text
src/
├── TrashMailPanda/TrashMailPanda/
│   ├── Services/
│   │   ├── EmailTriageService.cs                          # MODIFY: ExecuteGmailActionAsync — age-at-execution routing
│   │   ├── BulkOperationService.cs                        # MODIFY: ExecuteGmailActionAsync — age-at-execution routing
│   │   ├── RetentionEnforcementService.cs                 # NEW: scan logic + last_scan_utc persistence
│   │   └── IRetentionEnforcementService.cs                # NEW: public interface
│   ├── Models/
│   │   └── RetentionScanResult.cs                         # NEW: scan result record
│   └── Startup/
│       └── RetentionStartupCheck.cs                       # NEW: startup prompt logic (reads last_scan_utc, prompts user)
├── Shared/TrashMailPanda.Shared/
│   └── Labels/
│       └── LabelThresholds.cs                             # NEW: static threshold lookup

src/Tests/TrashMailPanda.Tests/
├── Unit/
│   ├── RetentionEnforcementServiceTests.cs               # NEW: unit tests for scan logic
│   ├── LabelThresholdsTests.cs                           # NEW: unit tests for threshold map
│   ├── RetentionStartupCheckTests.cs                     # NEW: unit tests for startup prompt logic
│   └── EmailTriageServiceRetentionTests.cs               # NEW: unit tests for action routing
└── Integration/
    └── RetentionEnforcementIntegrationTests.cs           # NEW: integration tests (skipped by default)
```

**Structure Decision**: All new code lands in the existing single-solution structure. `LabelThresholds` goes in `Shared` (used by both `EmailTriageService`, `BulkOperationService`, and `RetentionEnforcementService`). `RetentionEnforcementService` and its interface land in the main `TrashMailPanda` console project alongside existing services. No new projects are introduced.

## Complexity Tracking

> **No violations — all constitution gates pass without exceptions.**

| Violation | Why Needed | Simpler Alternative Rejected Because |
|-----------|------------|-------------------------------------|
| [e.g., 4th project] | [current need] | [why 3 projects insufficient] |
| [e.g., Repository pattern] | [specific problem] | [why direct DB access insufficient] |
