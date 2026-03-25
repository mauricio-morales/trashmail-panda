# Tasks: Archive-Then-Delete Training Labels (064)

**Input**: Design documents from `/specs/064-archive-then-delete-labels/`
**Prerequisites**: plan.md ✅, spec.md ✅, research.md ✅, data-model.md ✅, contracts/IRetentionEnforcementService.md ✅, quickstart.md ✅

## Format: `[ID] [P?] [Story?] Description`

- **[P]**: Can run in parallel (different files, no dependencies on incomplete tasks)
- **[Story]**: User story label (US1, US2, US3)
- Exact file paths included in every task description

---

## Phase 1: Setup

**Purpose**: Baseline validation before any modifications

- [X] T001 Verify solution builds and all existing tests pass: `dotnet build && dotnet test`

---

## Phase 2: Foundational (Blocking Prerequisites)

**Purpose**: Shared infrastructure that ALL three user stories depend on. No user story work begins until this phase completes.

**⚠️ CRITICAL**: `LabelThresholds` is imported by `EmailTriageService`, `BulkOperationService`, AND `RetentionEnforcementService`. Must exist before any US phase starts.

- [X] T002 [P] Create `LabelThresholds` static class with `Archive30d`/`Archive1y`/`Archive5y` constants, `ThresholdsByLabel` dict, `TimeBoundedLabels` set, `TryGetThreshold`, and `IsTimeBounded` in `src/Shared/TrashMailPanda.Shared/Labels/LabelThresholds.cs`
- [X] T003 [P] Create `LabelThresholdsTests` unit test class covering `TryGetThreshold` for all three labels (exact boundary values), `IsTimeBounded` true/false cases, and unknown-label false case in `src/Tests/TrashMailPanda.Tests/Unit/LabelThresholdsTests.cs`
- [X] T004 [P] Create `RetentionSettings` DTO class with `ScanIntervalDays` (default 30) and `PromptThresholdDays` (default 7) in `src/Shared/TrashMailPanda.Shared/RetentionSettings.cs`
- [X] T005 Modify `ProcessingSettings` to add `public RetentionSettings Retention { get; set; } = new();` property in `src/Shared/TrashMailPanda.Shared/ProcessingSettings.cs`

**Checkpoint**: `LabelThresholds` compiles, tests pass — all three user story phases can now start in parallel.

---

## Phase 3: User Story 1 — Age-at-Execution Routing (Priority: P1) 🎯 MVP

**Goal**: When a user confirms a time-bounded label during triage or bulk operations, compute the email's current age from `ReceivedDateUtc`. If age ≥ threshold → execute Delete in Gmail; otherwise → execute Archive. In both cases, `training_label` in `email_features` is written as the content-based label (e.g., `Archive for 30d`).

**Independent Test**: Present the same email with two controlled received dates — one under threshold (expect Archive + `training_label = "Archive for 30d"`) and one over threshold (expect Delete + `training_label = "Archive for 30d"`). Verify `BulkOperationService` previously failing for time-bounded labels now returns success.

### Tests for User Story 1

- [X] T006 [P] [US1] Create `EmailTriageServiceRetentionTests` covering: (a) under-threshold email calls `ApplyArchiveAsync` and stores label as `"Archive for 30d"`, (b) over-threshold email calls `ApplyDeleteAsync`, (c) `training_label` is content label in both cases, (d) null `ReceivedDateUtc` falls back to Archive safely in `src/Tests/TrashMailPanda.Tests/Unit/EmailTriageServiceRetentionTests.cs`

### Implementation for User Story 1

- [X] T007 [US1] Modify `EmailTriageService.ExecuteGmailActionAsync`: add `DateTime? receivedDateUtc` parameter; when `LabelThresholds.TryGetThreshold(action, out var days)` succeeds AND `receivedDateUtc` has value, route to `ApplyDeleteAsync` if `ageDays >= days`, else `ApplyArchiveAsync`; add safe Archive fallback when `receivedDateUtc` is null; keep existing `Keep`/`Archive`/`Delete`/`Spam` switch branches in `src/TrashMailPanda/TrashMailPanda/Services/EmailTriageService.cs`
- [X] T008 [US1] Thread `receivedDateUtc` up the call chain: add `DateTime? receivedDateUtc = null` to `ApplyDecisionAsync` signature and pass it through to `ExecuteGmailActionAsync`; update callers (e.g., `EmailTriageConsoleService`) to pass `featureVector.ReceivedDateUtc` when available in `src/TrashMailPanda/TrashMailPanda/Services/EmailTriageService.cs`
- [X] T009 [US1] Add time-bounded label cases with age-at-execution routing to `BulkOperationService.ExecuteGmailActionAsync` using `LabelThresholds` (same logic as T007); read `ReceivedDateUtc` from `EmailFeatureVector` already in scope in the bulk loop in `src/TrashMailPanda/TrashMailPanda/Services/BulkOperationService.cs`

**Checkpoint**: Unit tests in T006 pass. Manually triage an email with an over-threshold `Archive for 30d` label and confirm a Gmail Delete is issued, while `email_features.training_label` remains `"Archive for 30d"`.

---

## Phase 4: User Story 2 — Retention Enforcement Service (Priority: P2)

**Goal**: A startup-prompted retention scan queries `email_features` for archived emails with time-bounded labels whose `received_date_utc + threshold` has passed and deletes them from Gmail. `training_label` is never modified. Per-email failures accumulate in `RetentionScanResult.FailedIds` and do not abort the scan.

**Independent Test**: Insert `email_features` rows with time-bounded labels and known received dates (some expired, some not). Run the retention scan. Verify: (a) expired emails are deleted in Gmail, (b) non-expired emails are untouched, (c) `training_label` unchanged for all rows, (d) `last_scan_utc` is persisted in config. The startup prompt can be tested in isolation by mocking `GetLastScanTimeAsync` and verifying `ShouldPromptAsync` returns true/false correctly.

### Tests for User Story 2

- [X] T010 [P] [US2] Create `RetentionEnforcementServiceTests` covering: (a) expired email (age ≥ threshold) is sent to Gmail delete, (b) non-expired email is skipped, (c) boundary case: age == threshold → delete, (d) `training_label` is never written, (e) per-email Gmail failure accumulates in `FailedIds` and scan continues, (f) `last_scan_utc` is persisted on success in `src/Tests/TrashMailPanda.Tests/Unit/RetentionEnforcementServiceTests.cs`
- [X] T011 [P] [US2] Create `RetentionStartupCheckTests` covering: (a) `ShouldPromptAsync` returns true → prompt displayed, (b) user confirms → `RunScanAsync` called, (c) user declines → `RunScanAsync` not called, (d) `ShouldPromptAsync` returns false → no prompt in `src/Tests/TrashMailPanda.Tests/Unit/RetentionStartupCheckTests.cs`

### Implementation for User Story 2

- [X] T012 [P] [US2] Create `RetentionScanResult` as a `readonly record struct` with `ScannedCount`, `DeletedCount`, `SkippedCount`, `FailedIds` (`IReadOnlyList<string>`), `RanAtUtc` (UTC), plus derived properties `HasFailures` and `AnyDeleted` in `src/TrashMailPanda/TrashMailPanda/Models/RetentionScanResult.cs`
- [X] T013 [P] [US2] Create `RetentionEnforcementOptions` class with `ScanIntervalDays` (default 30, min 1) and `PromptThresholdDays` (default 7, min 1, must be ≤ `ScanIntervalDays`) in `src/TrashMailPanda/TrashMailPanda/Models/RetentionEnforcementOptions.cs`
- [X] T014 [P] [US2] Create `IRetentionEnforcementService` interface with `RunScanAsync(CancellationToken)`, `GetLastScanTimeAsync(CancellationToken)`, and `ShouldPromptAsync(CancellationToken)` — all returning `Task<Result<T>>`, doc comments note `training_label` is never modified in `src/TrashMailPanda/TrashMailPanda/Services/IRetentionEnforcementService.cs`
- [X] T015 [US2] Implement `RetentionEnforcementService`: in `RunScanAsync`, fetch all features via `IEmailArchiveService.GetAllFeaturesAsync`, filter to `is_archived = 1` AND time-bounded label AND non-null `ReceivedDateUtc`, compute elapsed days per email, enqueue IDs where `elapsed >= threshold`, call `IEmailProvider.BatchModifyAsync` with `TRASH` label per email, accumulate `FailedIds`, persist `last_scan_utc` via `IConfigurationService`, never call `SetTrainingLabelAsync`; implement `GetLastScanTimeAsync` and `ShouldPromptAsync` using config read in `src/TrashMailPanda/TrashMailPanda/Services/RetentionEnforcementService.cs`
- [X] T016 [US2] Implement `RetentionStartupCheck`: call `ShouldPromptAsync`; if true, render the Spectre.Console yellow/cyan confirmation prompt showing days-since-last-scan; if user confirms (Y or Enter), call `RunScanAsync` and display result summary using `AnsiConsole.MarkupLine` with semantic color markup in `src/TrashMailPanda/TrashMailPanda/Startup/RetentionStartupCheck.cs`
- [X] T017 [US2] Register DI bindings in Program.cs host builder: `services.AddSingleton<IRetentionEnforcementService, RetentionEnforcementService>()`, `services.Configure<RetentionEnforcementOptions>(configuration.GetSection("RetentionEnforcement"))`, and wire `RetentionStartupCheck` into the console startup sequence in `src/TrashMailPanda/TrashMailPanda/Program.cs`
- [X] T018 [P] [US2] Create `RetentionEnforcementIntegrationTests` stub with `[Trait("Category", "Integration")]` and `[Fact(Skip = "Requires OAuth - real Gmail credentials needed")]` for: full scan deletes expired emails end-to-end in `src/Tests/TrashMailPanda.Tests/Integration/RetentionEnforcementIntegrationTests.cs`

**Checkpoint**: Unit tests T010–T011 pass. `dotnet build` succeeds. Manually verify a simulated startup sequence prompts for retention scan when `last_scan_utc` is null.

---

## Phase 5: User Story 3 — ML Inference EmailAgeDays Semantics (Priority: P3)

**Goal**: At inference time (classifying a brand-new email not yet stored in the DB), `EmailAgeDays` is computed fresh as `(DateTime.UtcNow - email.ReceivedDateUtc).Days`, not read from any stored value. For existing training rows in `email_features`, the stored `email_age_days` (= age at decision time) is used as-is — no recomputation.

**Independent Test**: Invoke the inference path with a test email whose `ReceivedDateUtc` is set to a known date. Assert that the `EmailAgeDays` value passed to `ClassifyActionAsync` equals `(UtcNow - receivedDate).Days` (within ±1 for timing tolerance), not the value from any stored feature vector. Separately, confirm training data retrieval does not alter `email_age_days` stored in existing rows.

### Tests for User Story 3

- [X] T019 [P] [US3] Write a unit test that invokes the inference path with a controlled `ReceivedDateUtc` and verifies the `EmailAgeDays` feature value passed to `ClassifyActionAsync` matches `(UtcNow - receivedDateUtc).Days` (fresh computation, not a stored value) in `src/Tests/TrashMailPanda.Tests/Unit/EmailClassificationInferenceTests.cs`

### Implementation for User Story 3

- [X] T020 [US3] In the inference path (where `ClassifyActionAsync` is called for a new email before it is stored in `email_features`), compute `EmailAgeDays = (int)(DateTime.UtcNow - email.ReceivedDateUtc ?? DateTime.UtcNow).TotalDays` and inject it into the feature vector immediately before the classifier call; confirm existing training/export paths that read `email_age_days` from stored feature rows are NOT changed in `src/TrashMailPanda/TrashMailPanda/Services/EmailTriageService.cs` (or the classification service that calls `ClassifyActionAsync`)

**Checkpoint**: Unit test T019 passes. Same email content yields same label recommendation regardless of `EmailAgeDays` value (content-driven classification confirmed). `dotnet test --filter Category=Unit` is green.

---

## Phase 6: Polish & Cross-Cutting Concerns

- [X] T021 [P] Add `RetentionEnforcement` section to `appsettings.json` (or `appsettings.Development.json`) with defaults `ScanIntervalDays: 30`, `PromptThresholdDays: 7` in `src/TrashMailPanda/TrashMailPanda/appsettings.json`
- [X] T022 Run full CI validation: `dotnet build --configuration Release && dotnet test && dotnet format --verify-no-changes`; fix any formatting or nullable-reference-type warnings introduced by this feature
- [X] T023 [P] Run the quickstart.md validation scenarios manually: present an email under-threshold, over-threshold, boundary (exact threshold), and null `ReceivedDateUtc`; confirm all arcade scenarios from `specs/064-archive-then-delete-labels/quickstart.md` produce the expected outcomes

---

## Dependencies & Execution Order

### Phase Dependencies

- **Setup (Phase 1)**: No dependencies — start immediately
- **Foundational (Phase 2)**: Depends on Phase 1 — **BLOCKS all user story phases**
- **US1 (Phase 3)**: Depends on Phase 2 (`LabelThresholds`) — independent of US2 and US3
- **US2 (Phase 4)**: Depends on Phase 2 (`LabelThresholds`, `RetentionSettings`, `ProcessingSettings`) — independent of US1 and US3
- **US3 (Phase 5)**: Depends on Phase 2 (`LabelThresholds`) — independent of US1 and US2
- **Polish (Phase 6)**: Depends on all desired user story phases being complete

### User Story Dependencies

- **US1 (P1)**: No inter-story dependencies — starts after Phase 2
- **US2 (P2)**: No inter-story dependencies — starts after Phase 2; `RetentionEnforcementService` calls `IEmailProvider` and `IEmailArchiveService` (existing interfaces, no changes needed)
- **US3 (P3)**: No inter-story dependencies — starts after Phase 2; changes are confined to the inference path in `EmailTriageService`

### Within Each User Story

- Tests should be written before or alongside implementation (FR-011 mandates tests)
- Models/interfaces (`T012`–`T014`) before service implementation (`T015`)
- Service implementation (`T015`) before startup check (`T016`) and DI registration (`T017`)

### Parallel Opportunities

| Parallel Group | Tasks |
|---|---|
| Phase 2 foundations | T002, T003, T004 (T005 after T004) |
| US1 test + impl models | T006, T007 (T008 after T007, T009 independent) |
| US2 models + interface + tests | T010, T011, T012, T013, T014 |
| After US2 impl: startup + integration | T015 → T016, T018 (parallel) |
| US3 | T019, T020 (parallel) |

---

## Implementation Strategy

**MVP scope = Phase 1 + Phase 2 + Phase 3 (US1 only)**

US1 alone delivers immediate value: time-bounded labels finally execute real Gmail operations (Archive < threshold, Delete ≥ threshold) instead of always archiving. This is the highest-impact change and is fully testable in isolation.

US2 (retention scan) and US3 (ML inference semantics) can follow independently once US1 is validated.

---

## Summary

| Phase | Story | Tasks | Key Deliverables |
|---|---|---|---|
| 1 — Setup | — | T001 | Baseline build verification |
| 2 — Foundational | — | T002–T005 | `LabelThresholds`, `RetentionSettings`, `ProcessingSettings` mod |
| 3 — US1 (P1) 🎯 | US1 | T006–T009 | Age-at-execution routing in triage + bulk ops |
| 4 — US2 (P2) | US2 | T010–T018 | `RetentionEnforcementService`, startup check, DI registration |
| 5 — US3 (P3) | US3 | T019–T020 | Fresh `EmailAgeDays` at inference time |
| 6 — Polish | — | T021–T023 | Config defaults, CI pass, quickstart validation |
| **Total** | | **23 tasks** | |
