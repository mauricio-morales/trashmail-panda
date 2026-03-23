# Tasks: Runtime Classification with User Feedback Loop

**Feature**: 062-runtime-classification-feedback  
**Branch**: `062-runtime-classification-feedback`  
**Input**: Design documents from `/specs/062-runtime-classification-feedback/`  
**Generated**: 2026-03-23

## Format: `[ID] [P?] [Story?] Description with file path`

- **[P]**: Can run in parallel (different files, no inter-dependencies on incomplete tasks)
- **[Story]**: Which user story this task maps to (US1–US5)
- Setup and Foundational phases have NO story label
- Polish phase has NO story label

---

## Phase 1: Setup

**Purpose**: Confirm clean build baseline; no new NuGet packages or projects are needed (ML.NET, Spectre.Console, xUnit, Moq, Google.Apis.Gmail.v1 all already in place).

- [ ] T001 Confirm clean build baseline by running `dotnet build` in project root and resolving any pre-existing compilation errors in `src/TrashMailPanda/TrashMailPanda/TrashMailPanda.csproj`

**Checkpoint**: Green build — no pre-existing errors polluting the implementation work

---

## Phase 2: Foundational (Blocking Prerequisites)

**Purpose**: Shared types that MUST exist before US1 and US3 can be implemented. US2 and US5 are also unblocked after this phase.

**⚠️ CRITICAL**: US1 and US3 cannot begin until T002 and T003 are complete.

- [ ] T002 [P] Create `AutoApplyConfig` sealed class (Enabled: bool = false, ConfidenceThreshold: float [Range(0.50, 1.00)] = 0.95f) in `src/TrashMailPanda/TrashMailPanda/Models/Console/AutoApplyConfig.cs`
- [ ] T003 Add `AutoApply` property of type `AutoApplyConfig` (default: `new()`) to `ProcessingSettings` in `src/TrashMailPanda/TrashMailPanda/Models/ProcessingSettings.cs`

**Checkpoint**: Foundation ready — `AutoApplyConfig` is resolvable and `ProcessingSettings` carries the auto-apply config; all user story phases can now begin

---

## Phase 3: User Story 1 — Confidence-Based Auto-Apply (Priority: P1) 🎯 MVP

**Goal**: Emails classified with ≥95% confidence are automatically actioned without user confirmation; emails below the threshold are presented for manual review. The session ends with a summary of auto-applied vs. manually reviewed counts.

**Independent Test**: Run AI-assisted triage mode with a loaded model. Verify that emails with mock confidence 97% are auto-applied (no user prompt shown, training label stored), emails at 80% are queued for manual review, redundant actions (e.g., "Archive" on already-archived email) skip the Gmail API call but store the training label, and the end-of-session summary displays auto-applied and manual counts.

### Implementation for User Story 1

- [ ] T004 [P] [US1] Create `AutoApplyLogEntry` record (EmailId, SenderDomain, Subject, AppliedAction, Confidence, AppliedAtUtc, WasRedundant, Undone, UndoneToAction?) in `src/TrashMailPanda/TrashMailPanda/Models/Console/AutoApplyLogEntry.cs`
- [ ] T005 [P] [US1] Create `AutoApplySessionSummary` record (TotalAutoApplied, TotalManuallyReviewed, TotalRedundant, TotalUndone, PerActionCounts) in `src/TrashMailPanda/TrashMailPanda/Models/Console/AutoApplySessionSummary.cs`
- [ ] T006 [P] [US1] Extend `EmailTriageSession` with `AutoApplyLog` (List\<AutoApplyLogEntry\>), `AutoAppliedCount` (int), and `RollingDecisions` (Queue\<(string predicted, string actual, bool isOverride)\> capped at 100) in `src/TrashMailPanda/TrashMailPanda/Models/Console/EmailTriageSession.cs`
- [ ] T007 [P] [US1] Extend `TriageSessionSummary` record with `AutoAppliedCount` (int) and `ManuallyReviewedCount` (int) fields in `src/TrashMailPanda/TrashMailPanda/Models/Console/TriageSessionSummary.cs`
- [ ] T008 [P] [US1] Create `IAutoApplyService` interface (GetConfigAsync, SaveConfigAsync, ShouldAutoApply, IsActionRedundant, LogAutoApply, GetSessionLog, GetSessionSummary, ResetSession) per `contracts/IAutoApplyService.md` in `src/TrashMailPanda/TrashMailPanda/Services/IAutoApplyService.cs`
- [ ] T009 [US1] Implement `AutoApplyService` with threshold evaluation (confidence ≥ threshold AND enabled), redundancy detection (action matches IsArchived/IsInInbox/WasInTrash feature flags), in-memory session log, and `IConfigurationService` persistence in `src/TrashMailPanda/TrashMailPanda/Services/AutoApplyService.cs`
- [ ] T010 [US1] Register `IAutoApplyService` as singleton in `src/TrashMailPanda/TrashMailPanda/Services/ServiceCollectionExtensions.cs`
- [ ] T011 [US1] Integrate auto-apply decision point into `RunAsync` per-email loop: call `ShouldAutoApply`, branch to auto-apply (redundancy check → skip Gmail or apply, LogAutoApply, RecordDecision) or manual review; add session summary display at loop end in `src/TrashMailPanda/TrashMailPanda/Services/Console/EmailTriageConsoleService.cs`
- [ ] T012 [US1] Write unit tests for threshold boundary (94.9% → manual, 95.0% → auto), auto-apply disabled config, redundancy detection for each action category, and `GetSessionSummary` aggregate counts in `src/Tests/TrashMailPanda.Tests/Unit/AutoApplyServiceTests.cs`

**Checkpoint**: US1 fully functional — auto-apply processes high-confidence emails, session summary appears at end, existing manual triage flow unchanged for sub-threshold emails

---

## Phase 4: User Story 2 — Bootstrap Starred/Important → Keep (Priority: P1)

**Goal**: During the initial Gmail bootstrap scan, emails that are Starred or marked Important are automatically labeled "Keep" as training data—filling the gap left by the existing `TrainingSignalAssigner` which handles folder-based signals (Trash→Delete) but not attribute-based signals (Starred/Important→Keep). Archived-only emails remain unlabeled.

**Independent Test**: Run the bootstrap scan against a Gmail account. Verify: Starred and Important emails have `training_label = 'Keep'`, emails only in Archive remain unlabeled, re-running the scan produces no duplicate labels (idempotent), and existing user corrections are not overwritten.

### Implementation for User Story 2

- [ ] T013 [US2] Add idempotent post-scan `IsStarred`/`IsImportant` → `'Keep'` label inference step (SQL: `UPDATE email_features SET training_label='Keep', user_corrected=0 WHERE (is_starred=1 OR is_important=1) AND training_label IS NULL`) to `RunInitialScanAsync`, including a one-time migration execution on startup for existing installs in `src/Providers/Email/TrashMailPanda.Providers.Email/Services/GmailTrainingDataService.cs`

**Checkpoint**: US2 complete — bootstrap seeding now covers Starred/Important→Keep in addition to existing Trash→Delete; idempotency verified; existing `TrainingSignalAssigner` untouched

---

## Phase 5: User Story 3 — Model Quality Monitoring & Retraining Suggestions (Priority: P2)

**Goal**: The system continuously tracks a rolling accuracy window (last 100 decisions) and per-action correction rates. At the start of each triage batch it proactively surfaces: Info warnings (≥50 corrections → suggest retrain with 'T' shortcut), Warning banners (accuracy <70%), and Critical banners (accuracy <50% → auto-disable auto-apply). Users should never need to request these checks manually.

**Independent Test**: Simulate a correction stream that diverges from model predictions. Verify: Info warning appears when corrections ≥50, Warning banner when rolling accuracy drops to 68%, Critical banner when accuracy drops to 45% and `AutoApplyEnabled` is automatically set to false and persisted, retrain prompt shows with 'T' shortcut and triggers `ModelTrainingPipeline.IncrementalUpdateActionModelAsync`.

### Implementation for User Story 3

- [ ] T014 [P] [US3] Create `QualityWarningSeverity` enum (Info, Warning, Critical) in `src/TrashMailPanda/TrashMailPanda/Models/Console/QualityWarningSeverity.cs`
- [ ] T015 [P] [US3] Create `ActionCategoryMetrics` record (Action, TotalRecommended, TotalAccepted, CorrectionRate, CorrectedTo Dictionary\<string, int\>) in `src/TrashMailPanda/TrashMailPanda/Models/Console/ActionCategoryMetrics.cs`
- [ ] T016 [P] [US3] Create `ModelQualityMetrics` record (OverallAccuracy, RollingAccuracy, RollingWindowSize, TotalDecisions, TotalCorrections, CorrectionsSinceLastTraining, PerActionMetrics, CalculatedAtUtc) in `src/TrashMailPanda/TrashMailPanda/Models/Console/ModelQualityMetrics.cs`
- [ ] T017 [P] [US3] Create `QualityWarning` record (Severity, Message, RollingAccuracy, CorrectionsSinceTraining, RecommendedAction, ProblematicActions?, AutoApplyDisabled) in `src/TrashMailPanda/TrashMailPanda/Models/Console/QualityWarning.cs`
- [ ] T018 [US3] Create `IModelQualityMonitor` interface (RecordDecision, GetMetricsAsync, CheckForWarningAsync, GetCorrectionsSinceLastTrainingAsync, ResetSession) per `contracts/IModelQualityMonitor.md` in `src/TrashMailPanda/TrashMailPanda/Services/IModelQualityMonitor.cs`
- [ ] T019 [US3] Implement `ModelQualityMonitor` with rolling `Queue<(string, string, bool)>` capped at 100, single DB aggregation query `(SELECT training_label, COUNT(*) FROM email_features WHERE user_corrected=1 GROUP BY training_label)` for per-action metrics, warning threshold evaluation (Critical <50%, Warning <70%, Info ≥50 corrections), and dismissal tracking (suppress until +25 corrections or new session) in `src/TrashMailPanda/TrashMailPanda/Services/ModelQualityMonitor.cs`
- [ ] T020 [US3] Register `IModelQualityMonitor` as singleton in `src/TrashMailPanda/TrashMailPanda/Services/ServiceCollectionExtensions.cs`
- [ ] T021 [US3] Add per-batch quality warning banner rendering (Critical = bold red panel, Warning = yellow, Info/retrain = cyan), retrain 'T' keystroke handler calling `ModelTrainingPipeline.IncrementalUpdateActionModelAsync`, and Critical auto-disable path (`autoApplyConfig.Enabled = false` + `SaveConfigAsync`) in `src/TrashMailPanda/TrashMailPanda/Services/Console/EmailTriageConsoleService.cs`
- [ ] T022 [US3] Write unit tests for rolling window accuracy calculation, 70%/50% warning threshold transitions, per-action correction rate aggregation, >40% correction rate targeted recommendation, and dismissal suppression logic in `src/Tests/TrashMailPanda.Tests/Unit/ModelQualityMonitorTests.cs`

**Checkpoint**: US3 complete — quality degradation surfaces automatically at batch boundaries; Critical path auto-disables auto-apply and persists the change; retrain can be triggered in-session

---

## Phase 6: User Story 4 — Per-Action Performance Tracking (Priority: P2)

**Goal**: Users can press 'M' at any point during triage to view a full model performance dashboard: per-action accuracy table (action, total classified, accepted, correction rate) and a confusion summary showing what each action was corrected to.

**Independent Test**: After triaging 50+ emails in AI-assisted mode, press 'M' and verify: a Spectre.Console table appears with a row per action (Keep, Archive, Delete, Spam) showing total recommended, accepted, and correction rate; a confusion summary section shows "Archive→Delete: N times" rows for any non-zero corrections.

### Implementation for User Story 4

- [ ] T023 [US4] Add model stats display keyed to 'M' keystroke: render a Spectre.Console `Table` with per-action rows (action, total, accepted, correction rate) and a confusion summary section ("Archive→Delete: 15 times") using `IModelQualityMonitor.GetMetricsAsync()` in `src/TrashMailPanda/TrashMailPanda/Services/Console/EmailTriageConsoleService.cs`

**Checkpoint**: US4 complete — performance dashboard accessible on demand; per-action table and confusion summary render correctly; US3 quality monitor unchanged

---

## Phase 7: User Story 5 — Auto-Apply Review and Undo (Priority: P3)

**Goal**: Users can press 'R' to open a review table of all auto-applied decisions from the current session, then select an entry and press 'U' to undo it — reversing the Gmail action (e.g., move back to Inbox) and storing the user's correction as a high-value training signal (`UserCorrected=1`).

**Independent Test**: Enable auto-apply, process a batch of 10+ emails above the threshold, press 'R' to open the review table (verify all entries show sender, subject, action, confidence, status). Select an entry, press 'U', confirm the Gmail action is reversed via `IEmailProvider.BatchModifyAsync`, the training label is updated to the corrected action via `IEmailArchiveService.SetTrainingLabelAsync`, `UserCorrected=1`, and the review table entry status updates to "↩ Undone".

### Implementation for User Story 5

- [ ] T024 [P] [US5] Create `IAutoApplyUndoService` interface (UndoAsync: emailId, originalAction, correctedAction → Result\<bool\>) per `contracts/IAutoApplyUndoService.md` in `src/TrashMailPanda/TrashMailPanda/Services/IAutoApplyUndoService.cs`
- [ ] T025 [US5] Implement `AutoApplyUndoService` with Gmail reversal mapping (Delete→add INBOX/remove TRASH, Archive→add INBOX, Spam→add INBOX/remove SPAM, Keep→no-op), dual-write via `IEmailProvider.BatchModifyAsync` then `IEmailArchiveService.SetTrainingLabelAsync(userCorrected: true)`, and Result.Failure if Gmail API call fails (training label not updated) in `src/TrashMailPanda/TrashMailPanda/Services/AutoApplyUndoService.cs`
- [ ] T026 [US5] Register `IAutoApplyUndoService` with scoped lifetime in `src/TrashMailPanda/TrashMailPanda/Services/ServiceCollectionExtensions.cs`
- [ ] T027 [US5] Add review ('R') and undo ('U') menu: render auto-apply review table (columns: #, Sender, Subject, Action, Confidence, Status), handle 'U' keystrokes to call `IAutoApplyUndoService.UndoAsync` and call `IModelQualityMonitor.RecordDecision` with `isOverride: true`, update session log entry `Undone = true` in `src/TrashMailPanda/TrashMailPanda/Services/Console/EmailTriageConsoleService.cs`
- [ ] T028 [US5] Write unit tests for Gmail reversal mapping (each action), dual-write ordering, failure isolation (Gmail fails → no training label update), and `UndoneToAction` recording in `src/Tests/TrashMailPanda.Tests/Unit/AutoApplyUndoServiceTests.cs`

**Checkpoint**: US5 complete — review table shows current session auto-applied decisions; undo reverses Gmail state and stores correction; failure path aborts dual-write correctly

---

## Phase 8: Polish & Cross-Cutting Concerns

**Purpose**: Validate compilation, test coverage, and code style across all new and modified files.

- [ ] T029 Run `dotnet build -c Release` and resolve any nullable reference warnings or type resolution errors introduced by new files in project root
- [ ] T030 [P] Run `dotnet test --filter Category=Unit` and confirm all three new test suites (AutoApplyServiceTests, ModelQualityMonitorTests, AutoApplyUndoServiceTests) pass with no failures
- [ ] T031 [P] Run `dotnet format --verify-no-changes` and fix any style violations in new or modified files in project root

---

## Dependencies

```
Phase 2 (T002–T003)
  └─ Phase 3 / US1 (T004–T012)   ← requires AutoApplyConfig, ProcessingSettings
  └─ Phase 5 / US3 (T014–T022)   ← requires AutoApplyConfig (used in CheckForWarningAsync)
  └─ Phase 7 / US5 (T024–T028)   ← requires session log from US1 (T006, T009, T011)

Phase 3 / US1
  └─ Phase 7 / US5                ← AutoApplyLogEntry + session log built in US1

Phase 5 / US3 (T019)
  └─ Phase 6 / US4 (T023)         ← per-action metrics provided by ModelQualityMonitor

Phase 4 / US2 (T013) — independent of all other phases (modifies GmailTrainingDataService only)
Phase 6 / US4 (T023) — requires US3 complete (T019 must be done)
Phase 7 / US5 (T024–T028) — requires US1 complete (T009, T011 must be done)
Phase 8 — requires all previous phases complete
```

**Story completion order** (forced by dependencies):
1. Phase 2 (Foundational)
2. US1 + US2 in parallel ← highest priority P1 stories, US2 has no dependency on US1
3. US3 (after foundational done; depends on AutoApplyConfig from Phase 2)
4. US4 (after US3's ModelQualityMonitor is implemented)
5. US5 (after US1's session log is wired in EmailTriageConsoleService)

---

## Parallel Execution Examples

### Parallel batch 1 — Phase 2 (start here)
```
T002 AutoApplyConfig.cs
```
Then T003 (ProcessingSettings)

### Parallel batch 2 — US1 model files + interface (after Phase 2)
```
T004 AutoApplyLogEntry.cs
T005 AutoApplySessionSummary.cs
T006 EmailTriageSession.cs (extend)
T007 TriageSessionSummary.cs (extend)
T008 IAutoApplyService.cs
```

### Parallel batch 3 — US2 and US1 implementation
```
T009 AutoApplyService.cs           ← US1 (after T004, T005, T008)
T013 GmailTrainingDataService.cs   ← US2 fully independent
```

### Parallel batch 4 — US3 model files (after Phase 2)
```
T014 QualityWarningSeverity.cs
T015 ActionCategoryMetrics.cs
T016 ModelQualityMetrics.cs
T017 QualityWarning.cs
```

### Parallel batch 5 — Polish
```
T030 dotnet test
T031 dotnet format
```

---

## Implementation Strategy

**MVP**: Complete Phase 2 + Phase 3 (US1) first. This delivers the highest-impact user-facing change (auto-apply for high-confidence emails) independently of all other stories. Phase 3 alone reduces triage time for large inboxes dramatically.

**Incremental delivery**:
1. **MVP (US1)**: Auto-apply with session summary and unit tests — shippable alone
2. **+US2**: Bootstrap Starred/Important labeling — one-file change, safe to ship alongside US1
3. **+US3**: Quality monitoring and retraining suggestions — ships quality guardrails for auto-apply
4. **+US4**: Per-action dashboard — additive display feature, no logic changes
5. **+US5**: Review and undo — completes the full trust/control story for auto-apply users

**Test Strategy** (from spec):

| Area | Test File | Key Scenarios |
|------|-----------|---------------|
| `AutoApplyService` | `AutoApplyServiceTests.cs` | Threshold boundary (94.9% manual, 95.0% auto), disabled config, redundancy per action, session summary counts |
| `ModelQualityMonitor` | `ModelQualityMonitorTests.cs` | Rolling window, 70%/50% thresholds, per-action metrics, >40% targeted warning, dismissal suppression |
| `AutoApplyUndoService` | `AutoApplyUndoServiceTests.cs` | Gmail reversal mapping, dual-write ordering, API failure isolation |

---

## Summary

| Metric | Value |
|--------|-------|
| Total tasks | 31 |
| US1 (P1) tasks | 9 (T004–T012) |
| US2 (P1) tasks | 1 (T013) |
| US3 (P2) tasks | 9 (T014–T022) |
| US4 (P2) tasks | 1 (T023) |
| US5 (P3) tasks | 5 (T024–T028) |
| Setup + Foundational tasks | 3 (T001–T003) |
| Polish tasks | 3 (T029–T031) |
| New files | 13 (6 service, 6 model, 1 enum) |
| Modified files | 6 (EmailTriageConsoleService, EmailTriageSession, TriageSessionSummary, ServiceCollectionExtensions, ProcessingSettings, GmailTrainingDataService) |
| New test files | 3 |
| Parallel task batches | 5 primary batches |
| Suggested MVP scope | Phase 2 + Phase 3 (US1 only) |
