# Tasks: Console TUI with Spectre.Console (#060)

**Input**: Design documents from `/specs/060-console-tui-spectre/`  
**Branch**: `060-console-tui-spectre`  
**Prerequisites**: plan.md ✅, spec.md ✅, research.md ✅, data-model.md ✅, contracts/ ✅, quickstart.md ✅

**Organization**: Tasks are grouped by user story to enable independent implementation and testing of each story.

## Format: `[ID] [P?] [Story] Description`

- **[P]**: Can run in parallel (different files, no dependencies on incomplete tasks)
- **[Story]**: Which user story this task belongs to (e.g., US1, US2...)

---

## Phase 1: Setup (Shared Infrastructure)

**Purpose**: Cross-cutting infrastructure that all user stories depend on. Must be complete before any story implementation begins.

- [X] T001 Add `ConsoleColors` static class with semantic markup constants in `src/TrashMailPanda/TrashMailPanda/Services/Console/ConsoleColors.cs`
- [X] T002 [P] Add `TriageMode` enum (`ColdStart`, `AiAssisted`) in `src/TrashMailPanda/TrashMailPanda/Models/Console/TriageMode.cs`
- [X] T003 [P] Add `TriageDecision` record (EmailId, ChosenAction, AiRecommendation?, ConfidenceScore?, IsOverride, DecidedAtUtc) in `src/TrashMailPanda/TrashMailPanda/Models/Console/TriageDecision.cs`
- [X] T004 [P] Add `TriageSessionSummary` record (TotalProcessed, KeepCount, ArchiveCount, DeleteCount, SpamCount, OverrideCount, Elapsed) in `src/TrashMailPanda/TrashMailPanda/Models/Console/TriageSessionSummary.cs`
- [X] T005 [P] Add `EmailTriageSession` class (AccountId, Mode, LabeledCount, LabelingThreshold, ThresholdPromptShownThisSession, SessionProcessedCount, SessionOverrideCount, ActionCounts, StartedAtUtc, CurrentOffset) in `src/TrashMailPanda/TrashMailPanda/Models/Console/EmailTriageSession.cs`
- [X] T006 [P] Add `TriageSessionInfo` record (Mode, LabeledCount, LabelingThreshold, ThresholdAlreadyReached) in `src/TrashMailPanda/TrashMailPanda/Models/Console/TriageSessionInfo.cs`
- [X] T007 [P] Add `KeyBinding` record (Key, Description) in `src/TrashMailPanda/TrashMailPanda/Models/Console/KeyBinding.cs`
- [X] T008 [P] Add `HelpContext` class (ModeTitle, Description?, KeyBindings) in `src/TrashMailPanda/TrashMailPanda/Models/Console/HelpContext.cs`
- [X] T009 [P] Add `BulkOperationCriteria` class (Sender?, Label?, DateFrom?, DateTo?, SizeBytes?, AiConfidenceThreshold?) in `src/TrashMailPanda/TrashMailPanda/Models/Console/BulkOperationCriteria.cs`
- [X] T010 [P] Add `BulkOperationResult` record (SuccessCount, FailedIds) in `src/TrashMailPanda/TrashMailPanda/Models/Console/BulkOperationResult.cs`

**Checkpoint**: All model types and color constants created — foundation ready

---

## Phase 2: Foundational (Storage + Provider Additions)

**Purpose**: Storage schema migration and `IEmailArchiveService` method additions that all user story implementations depend on.

**⚠️ CRITICAL**: No user story work can begin until this phase is complete

- [X] T011 Add `TrainingLabel string?` property to `EmailFeatureVector` entity in `src/Providers/Storage/TrashMailPanda.Providers.Storage/Models/EmailFeatureVector.cs`
- [X] T012 Generate EF Core migration `AddTrainingLabelToEmailFeatures` (`ALTER TABLE email_features ADD COLUMN training_label TEXT NULL`) in `src/Providers/Storage/TrashMailPanda.Providers.Storage/Migrations/`
- [X] T013 Add `SetTrainingLabelAsync(string emailId, string label, bool userCorrected, CancellationToken)` method signature to `IEmailArchiveService` in `src/Providers/Storage/TrashMailPanda.Providers.Storage/IEmailArchiveService.cs`
- [X] T014 [P] Add `CountLabeledAsync(CancellationToken)` method signature to `IEmailArchiveService` in `src/Providers/Storage/TrashMailPanda.Providers.Storage/IEmailArchiveService.cs`
- [X] T015 [P] Add `GetUntriagedAsync(int pageSize, int offset, CancellationToken)` method signature to `IEmailArchiveService` in `src/Providers/Storage/TrashMailPanda.Providers.Storage/IEmailArchiveService.cs`
- [X] T016 Implement `SetTrainingLabelAsync` on `EmailArchiveService` using parameterized SQL (`UPDATE email_features SET training_label = @label [, user_corrected = 1] WHERE email_id = @emailId`) in `src/Providers/Storage/TrashMailPanda.Providers.Storage/EmailArchiveService.cs`
- [X] T017 [P] Implement `CountLabeledAsync` on `EmailArchiveService` (`SELECT COUNT(*) FROM email_features WHERE training_label IS NOT NULL`) in `src/Providers/Storage/TrashMailPanda.Providers.Storage/EmailArchiveService.cs`
- [X] T018 [P] Implement `GetUntriagedAsync` on `EmailArchiveService` (`SELECT … FROM email_features WHERE training_label IS NULL ORDER BY extracted_at DESC LIMIT @pageSize OFFSET @offset`) in `src/Providers/Storage/TrashMailPanda.Providers.Storage/EmailArchiveService.cs`
- [X] T019 Fix `ModelTrainingPipeline.MapToTrainingInput` bug: replace `Label = string.Empty` with `Label = v.TrainingLabel ?? _signalAssigner.AssignSignal(v).Label` in `src/Providers/ML/TrashMailPanda.Providers.ML/Training/ModelTrainingPipeline.cs`
- [X] T020 [P] Apply the same `TrainingLabel` label-resolution fix to `IncrementalUpdateService` in `src/Providers/ML/TrashMailPanda.Providers.ML/Training/IncrementalUpdateService.cs`
- [X] T021 [P] Add `IEmailTriageService` interface (GetSessionInfoAsync, GetNextBatchAsync, GetAiRecommendationAsync, ApplyDecisionAsync) in `src/TrashMailPanda/TrashMailPanda/Services/IEmailTriageService.cs`
- [X] T022 [P] Add `IBulkOperationService` interface (PreviewAsync, ExecuteAsync) in `src/TrashMailPanda/TrashMailPanda/Services/IBulkOperationService.cs`
- [X] T023 [P] Add `IEmailTriageConsoleService` interface (RunAsync) in `src/TrashMailPanda/TrashMailPanda/Services/Console/IEmailTriageConsoleService.cs`
- [X] T024 [P] Add `IBulkOperationConsoleService` interface (RunAsync) in `src/TrashMailPanda/TrashMailPanda/Services/Console/IBulkOperationConsoleService.cs`
- [X] T025 [P] Add `IProviderSettingsConsoleService` interface (RunAsync) in `src/TrashMailPanda/TrashMailPanda/Services/Console/IProviderSettingsConsoleService.cs`
- [X] T026 [P] Add `IConsoleHelpPanel` interface (ShowAsync) in `src/TrashMailPanda/TrashMailPanda/Services/Console/IConsoleHelpPanel.cs`

**Checkpoint**: Migration applied, IEmailArchiveService extended, all interfaces defined — user stories can proceed in parallel

---

## Phase 3: User Story 1 — Cold Start Labeling (Priority: P1) 🎯 MVP

**Goal**: New users with no trained model can manually label emails one by one; progress persists across sessions; threshold prompt fires when minimum labels reached.

**Independent Test**: Run with zero trained models; feed mock untriaged emails; verify: (a) no AI recommendation shown, (b) any of K/A/D/S keys stores label and advances, (c) progress counter increments correctly, (d) threshold prompt appears and offers "Go to Training" / "Continue Labeling", (e) Q exits cleanly. Confirmed `training_label IS NULL` rows decrease with each decision.

### Implementation for User Story 1

- [X] T027 [US1] Implement `EmailTriageService` class skeleton with constructor injection of `IEmailArchiveService`, `IMLModelProvider`, `IEmailProvider`, `IOptions<MLModelProviderConfig>` in `src/TrashMailPanda/TrashMailPanda/Services/EmailTriageService.cs`
- [X] T028 [US1] Implement `EmailTriageService.GetSessionInfoAsync`: call `IMLModelProvider.GetActiveModelVersionAsync` to set `TriageMode`; call `CountLabeledAsync` for label count; read `MinTrainingSamples` from config in `src/TrashMailPanda/TrashMailPanda/Services/EmailTriageService.cs`
- [X] T029 [US1] Implement `EmailTriageService.GetNextBatchAsync`: delegate to `IEmailArchiveService.GetUntriagedAsync` in `src/TrashMailPanda/TrashMailPanda/Services/EmailTriageService.cs`
- [X] T030 [US1] Implement `EmailTriageService.GetAiRecommendationAsync`: return `Success(null)` in `ColdStart` mode; call `IMLModelProvider.ClassifyActionAsync` in `AiAssisted` mode in `src/TrashMailPanda/TrashMailPanda/Services/EmailTriageService.cs`
- [X] T031 [US1] Implement `EmailTriageService.ApplyDecisionAsync`: execute Gmail action via `IEmailProvider` FIRST; on success call `SetTrainingLabelAsync`; set `userCorrected = true` when action differs from AI recommendation in `src/TrashMailPanda/TrashMailPanda/Services/EmailTriageService.cs`
- [X] T032 [US1] Implement `EmailTriageConsoleService` skeleton with `IAnsiConsole` injection (defaulting to `AnsiConsole.Console`), `IEmailTriageService`, `IConsoleHelpPanel` in `src/TrashMailPanda/TrashMailPanda/Services/Console/EmailTriageConsoleService.cs`
- [X] T033 [US1] Implement cold-start email card rendering in `EmailTriageConsoleService`: show sender, subject, date, snippet; show `ConsoleColors.Info` mode notice (no AI suggestions yet); show labeled-count progress indicator ("X / Y labels collected") in `src/TrashMailPanda/TrashMailPanda/Services/Console/EmailTriageConsoleService.cs`
- [X] T034 [US1] Implement K/A/D/S keypress handling and Q/Esc exit in cold-start mode; show `ConsoleColors.Error` on action failure without counting label; advance to next email on success in `src/TrashMailPanda/TrashMailPanda/Services/Console/EmailTriageConsoleService.cs`
- [X] T035 [US1] Implement threshold prompt (cyan prompt offering "1-T: Go to Training" / "2-C: Continue Labeling") that fires once per session when `LabeledCount >= LabelingThreshold`; set `ThresholdPromptShownThisSession = true` after display in `src/TrashMailPanda/TrashMailPanda/Services/Console/EmailTriageConsoleService.cs`
- [X] T036 [US1] Implement session summary display (`TriageSessionSummary`) showing total processed, per-action counts, override count, elapsed time in green/white table when session ends (queue exhausted or user exits) in `src/TrashMailPanda/TrashMailPanda/Services/Console/EmailTriageConsoleService.cs`
- [X] T037 [US1] Write unit tests for `EmailTriageService` (cold-start path): mock `IMLModelProvider` returning failure, verify `GetSessionInfoAsync` returns `ColdStart` mode; verify `ApplyDecisionAsync` dual-write order (Gmail first, then label); verify no label stored on Gmail failure in `src/Tests/TrashMailPanda.Tests/Unit/Services/EmailTriageServiceTests.cs`
- [X] T038 [P] [US1] Write unit tests for `EmailTriageConsoleService` (cold-start UI): mock `IEmailTriageService`; verify no AI recommendation rendered in cold-start; verify progress counter increments; verify threshold prompt shown exactly once per session in `src/Tests/TrashMailPanda.Tests/Unit/Services/EmailTriageConsoleServiceTests.cs`

**Checkpoint**: Cold-start labeling session fully functional — users can build training data without AI

---

## Phase 4: User Story 2 — AI-Assisted Triage (Priority: P1)

**Goal**: Users with a trained model see AI recommendations and confidence scores; accept or override with single keystroke; every decision stored as training signal; session summary on completion.

**Independent Test**: Pre-load a trained model stub; inject mock emails with pre-computed classifications; verify: AI recommendation displayed with color-coded confidence score; Enter/Y accepts; K/A/D/S overrides; each decision calls `ApplyDecisionAsync` with correct `aiRecommendation` and `isOverride` flag; session summary shown when queue exhausted.

### Implementation for User Story 2

- [X] T039 [US2] Implement AI-assisted email card rendering in `EmailTriageConsoleService`: display AI recommendation in `ConsoleColors.AiRecommendation`; show confidence score color-coded (≥80% green, 50–79% yellow, <50% red); show Accept (`Enter`/`Y`) alongside K/A/D/S override keys in `src/TrashMailPanda/TrashMailPanda/Services/Console/EmailTriageConsoleService.cs`
- [X] T040 [US2] Implement `Enter`/`Y` accept-recommendation keypress: call `ApplyDecisionAsync` with `chosenAction = aiRecommendation` (IsOverride = false); advance to next email on success in `src/TrashMailPanda/TrashMailPanda/Services/Console/EmailTriageConsoleService.cs`
- [X] T041 [US2] Implement K/A/D/S override keypress in AI-assisted mode: call `ApplyDecisionAsync` with the user's chosen action and `aiRecommendation` set; mark IsOverride = true when action differs from recommendation in `src/TrashMailPanda/TrashMailPanda/Services/Console/EmailTriageConsoleService.cs`
- [X] T042 [US2] Implement skip-on-network-error path: display bold red error with description from `Result.Error`; allow user to press R to retry or S to skip (skip does not store a training signal) in `src/TrashMailPanda/TrashMailPanda/Services/Console/EmailTriageConsoleService.cs`
- [X] T043 [US2] Write unit tests for `EmailTriageService` (AI-assisted path): mock `IMLModelProvider` returning a successful `ActionPrediction`; verify `GetAiRecommendationAsync` returns prediction; verify `ApplyDecisionAsync` sets `IsOverride = true` when user action differs from AI recommendation in `src/Tests/TrashMailPanda.Tests/Unit/Services/EmailTriageServiceTests.cs`
- [X] T044 [P] [US2] Write unit tests for `EmailTriageConsoleService` (AI-assisted UI): mock `IEmailTriageService.GetAiRecommendationAsync` returning a prediction; verify recommendation and confidence score are rendered; verify IsOverride flag correctness for accept vs override keystrokes in `src/Tests/TrashMailPanda.Tests/Unit/Services/EmailTriageConsoleServiceTests.cs`

**Checkpoint**: AI-assisted triage fully functional; both triage modes complete; US1 + US2 deliver full Email Triage feature

---

## Phase 5: User Story 3 — Training Mode Completion (Priority: P2)

**Goal**: Multi-phase live progress bars; per-class metrics table; quality advisory below threshold; explicit save confirmation.

**Independent Test**: Run training against a known stub dataset; verify all pipeline phases shown sequentially; metrics table shows per-class accuracy/precision/recall/F1; quality advisory fires when F1 < threshold; save confirmation required before model activates.

### Implementation for User Story 3

- [X] T045 [US3] Implement multi-phase progress bar rendering in the existing Training mode console service: label each phase (data loading, pipeline building, training, evaluation) with distinct colors (`ConsoleColors.Highlight` active, `ConsoleColors.Success` complete, `ConsoleColors.Warning` slow, `ConsoleColors.Error` failed); ensure updates ≤2s in `src/TrashMailPanda/TrashMailPanda/Services/Console/`
- [X] T046 [US3] Implement per-class metrics table rendering using Spectre.Console `Table`: rows per class (Keep/Archive/Delete/Spam), columns Accuracy/Precision/Recall/F1; numeric values in `ConsoleColors.Metric` (magenta) in `src/TrashMailPanda/TrashMailPanda/Services/Console/`
- [X] T047 [US3] Implement quality advisory: when overall F1 < `MLModelProviderConfig.MinF1Threshold`, display `ConsoleColors.Warning` advisory message recommending more labeled data; quantify current vs needed label count in `src/TrashMailPanda/TrashMailPanda/Services/Console/`
- [X] T048 [US3] Implement save confirmation prompt: display `ConsoleColors.PromptOption` prompt; require `Y`/`Enter` to save; `N`/`Esc` discards; green success confirmation on save in `src/TrashMailPanda/TrashMailPanda/Services/Console/`
- [X] T049 [P] [US3] Write unit tests for training mode console service: verify phase progression calls in sequence; verify metrics table rendered with magenta values; verify quality advisory appears for low-F1 result; verify save prompt is required in `src/Tests/TrashMailPanda.Tests/Unit/Services/`

**Checkpoint**: Training mode displays complete progress, metrics, advisories, and save confirmation

---

## Phase 6: User Story 4 — Provider Settings Mode (Priority: P2)

**Goal**: Users can reconfigure Gmail credentials via OAuth re-authorization, view storage usage stats, and adjust storage limit — all within the running application.

**Independent Test**: Navigate to Provider Settings; mock `IGoogleOAuthHandler` and `IEmailArchiveService`; verify: config status shown (configured/not configured); OAuth re-auth flow delegates to `ConfigurationWizard`; storage stats render correctly; limit change persists; each error path shows bold red message with no partial state saved.

### Implementation for User Story 6

- [X] T050 [US4] Implement `ProviderSettingsConsoleService.RunAsync`: show settings menu with current Gmail config status and storage stats; delegate Gmail re-auth to existing `ConfigurationWizard`; delegate storage queries to `IEmailArchiveService.GetStorageUsageAsync` in `src/TrashMailPanda/TrashMailPanda/Services/Console/ProviderSettingsConsoleService.cs`
- [X] T051 [US4] Implement Gmail re-authorization path: call `IGoogleOAuthHandler.AuthenticateAsync`; show `ConsoleColors.Success` green confirmation on success; show `ConsoleColors.Error` bold red on failure in `src/TrashMailPanda/TrashMailPanda/Services/Console/ProviderSettingsConsoleService.cs`
- [X] T052 [US4] Implement storage stats display: show current bytes used, email archive count, feature vector count, configured storage limit; use `ConsoleColors.Info` for labels and `ConsoleColors.Metric` for values in `src/TrashMailPanda/TrashMailPanda/Services/Console/ProviderSettingsConsoleService.cs`
- [X] T053 [US4] Implement storage limit adjustment: prompt user for new limit value; validate input; persist via storage provider config; show `ConsoleColors.Success` on save or `ConsoleColors.Error` on failure in `src/TrashMailPanda/TrashMailPanda/Services/Console/ProviderSettingsConsoleService.cs`
- [X] T054 [P] [US4] Write unit tests for `ProviderSettingsConsoleService`: mock `IGoogleOAuthHandler`, `IEmailArchiveService`; verify storage stats rendered; verify Gmail re-auth delegates to OAuth handler; verify storage limit update persists; verify no partial state on failure in `src/Tests/TrashMailPanda.Tests/Unit/Services/ProviderSettingsConsoleServiceTests.cs`

**Checkpoint**: Provider Settings fully functional; users can recover from expired tokens and manage storage

---

## Phase 7: User Story 5 — Help System (Priority: P3)

**Goal**: Context-aware help panel accessible via `?`/`F1` from any mode; shows current mode key bindings; dismisses cleanly; main menu shows app description + version.

**Independent Test**: Press `?` in each mode; verify mode-specific key bindings shown; press dismiss key; verify return to previous state with no side effects.

### Implementation for User Story 5

- [X] T055 [US5] Implement `ConsoleHelpPanel.ShowAsync`: render `HelpContext.ModeTitle`, optional description, and key bindings as a Spectre.Console `Panel` with `Table`; block until `?`/`F1`/`Esc` pressed; on main menu context include app description and version in `src/TrashMailPanda/TrashMailPanda/Services/Console/ConsoleHelpPanel.cs`
- [X] T056 [P] [US5] Define `HelpContext` instances for each mode (Main Menu, Email Triage Cold Start, Email Triage AI, Training, Bulk Operations, Provider Settings) following key-bindings.md contract; wire `?`/`F1` key capture in each console service's run loop in `src/TrashMailPanda/TrashMailPanda/Services/Console/`
- [X] T057 [P] [US5] Write unit tests for `ConsoleHelpPanel`: mock `IAnsiConsole`; verify `ShowAsync` renders mode title and key bindings; verify dismissal on `Esc` in `src/Tests/TrashMailPanda.Tests/Unit/Services/ConsoleHelpPanelTests.cs`

**Checkpoint**: Help system accessible from all modes; context-aware per mode

---

## Phase 8: User Story 6 — Bulk Operations Mode (Priority: P2)

**Goal**: Users can bulk-delete, bulk-archive, or bulk-label emails matching defined criteria (sender, label, date range, size, AI confidence). Preview before execution. Partial failures reported.

**Independent Test**: Navigate to Bulk Operations; define criteria; verify preview lists matching emails; confirm execution; verify `IBulkOperationService.ExecuteAsync` called with correct email IDs; verify `FailedIds` reported in bold red; verify partial success counted correctly.

### Implementation for User Story 8

- [X] T058 [US6] Implement `BulkOperationService.PreviewAsync`: query `IEmailArchiveService` with `BulkOperationCriteria` filters; return matching `EmailFeatureVector` list without executing any actions in `src/TrashMailPanda/TrashMailPanda/Services/BulkOperationService.cs`
- [X] T059 [US6] Implement `BulkOperationService.ExecuteAsync`: iterate email IDs; call `IEmailProvider.BatchModifyAsync`/`ReportSpamAsync` FIRST; on success call `SetTrainingLabelAsync`; collect `FailedIds`; return `BulkOperationResult` in `src/TrashMailPanda/TrashMailPanda/Services/BulkOperationService.cs`
- [X] T060 [US6] Implement `BulkOperationConsoleService.RunAsync`: criteria builder wizard (Spectre.Console prompts for sender, label, date range, size, confidence threshold); call `PreviewAsync`; show preview count + sample rows; require `Y`/`Enter` confirmation before `ExecuteAsync`; show success count in green and failed IDs in bold red in `src/TrashMailPanda/TrashMailPanda/Services/Console/BulkOperationConsoleService.cs`
- [X] T061 [P] [US6] Write unit tests for `BulkOperationService`: mock `IEmailArchiveService` and `IEmailProvider`; verify Gmail action executed before label storage; verify partial failure collects `FailedIds` without aborting batch in `src/Tests/TrashMailPanda.Tests/Unit/Services/BulkOperationServiceTests.cs`
- [X] T062 [P] [US6] Write unit tests for `BulkOperationConsoleService`: mock `IBulkOperationService`; verify preview step shown before execution; verify confirmation required; verify failed IDs rendered in bold red in `src/Tests/TrashMailPanda.Tests/Unit/Services/BulkOperationConsoleServiceTests.cs`

**Checkpoint**: Bulk Operations mode fully functional with preview + confirmation + partial failure reporting

---

## Phase 9: Polish & Cross-Cutting Concerns

**Purpose**: Wire all new services into DI, migrate existing raw markup strings to `ConsoleColors` constants, enforce zero hardcoded markup outside `ConsoleColors.cs`, and validate end-to-end quality gates.

- [X] T063 Register all new services in DI container in `Program.cs`: `EmailTriageService → IEmailTriageService`, `BulkOperationService → IBulkOperationService`, `EmailTriageConsoleService → IEmailTriageConsoleService`, `BulkOperationConsoleService → IBulkOperationConsoleService`, `ProviderSettingsConsoleService → IProviderSettingsConsoleService`, `ConsoleHelpPanel → IConsoleHelpPanel` in `src/TrashMailPanda/TrashMailPanda/Program.cs`
- [X] T064 Replace stub mode dispatch in `Program.cs` / `ModeSelectionMenu.cs` with calls to `IEmailTriageConsoleService.RunAsync`, `IBulkOperationConsoleService.RunAsync`, and `IProviderSettingsConsoleService.RunAsync` (removing all "Coming soon" stubs) in `src/TrashMailPanda/TrashMailPanda/Program.cs`
- [X] T065 [P] Audit existing console services (`ConsoleStartupOrchestrator`, `ConsoleStatusDisplay`, `ConfigurationWizard`, `ModeSelectionMenu`) for raw markup strings; replace with `ConsoleColors.*` constants so zero hardcoded `[green]`/`[red]`/`[cyan]`/etc. strings exist outside `ConsoleColors.cs` in `src/TrashMailPanda/TrashMailPanda/Services/Console/`
- [X] T066 [P] Verify ANSI degradation: run on a terminal with `NO_COLOR=1` or a terminal that strips ANSI; confirm no raw markup tags appear in output (Spectre.Console handles this automatically — add a test asserting the setting is respected) in `src/Tests/TrashMailPanda.Tests/Unit/`
- [X] T067 [P] Run `dotnet build && dotnet test && dotnet format --verify-no-changes` and resolve any remaining compilation errors, nullable warnings, or formatting violations; confirm test coverage ≥90% globally and ≥95% for `EmailTriageService` and `EmailTriageConsoleService`

**Checkpoint**: All four modes fully implemented; no stubs remain; color scheme enforced globally; all CI gates pass

---

## Dependencies (story completion order)

```
Phase 1 (Setup: models + ConsoleColors)
  └─► Phase 2 (Foundational: migration + interfaces)
        ├─► Phase 3 (US1: Cold-Start Labeling)  🎯 MVP — deliverable on its own
        │     └─► Phase 4 (US2: AI-Assisted Triage)  — extends US1's EmailTriageService
        ├─► Phase 5 (US3: Training Completion)  — independent, depends only on Phase 2
        ├─► Phase 6 (US4: Provider Settings)    — independent, depends only on Phase 2
        ├─► Phase 7 (US5: Help System)          — independent, can start after Phase 1
        └─► Phase 8 (US6: Bulk Operations)      — independent, depends only on Phase 2
              └─► Phase 9 (Polish → DI wiring + color audit)  — depends on all above
```

**Phases 3–8 can begin in parallel after Phase 2 is complete.**

---

## Parallel Execution Examples (per story)

### US1 — Cold-Start Labeling (story internal parallelism)
- `T027–T031` (EmailTriageService impl) runs in parallel with `T037` (business logic tests)
- `T032–T036` (ConsoleService impl) runs in parallel with `T038` (presenter tests)

### US2 — AI-Assisted Triage
- `T039–T042` (console rendering) runs in parallel with `T043` (business logic tests)
- `T044` (presenter tests) can start once `T039` is complete

### US3, US4, US5, US6 — completely independent after Phase 2
- All four phases (5–8) can run in parallel across different developer streams

---

## Implementation Strategy

**MVP scope (suggest starting here)**: Phases 1 → 2 → 3 → 4 delivers the full Email Triage
experience (both cold-start and AI-assisted). These are the P1 stories and the product's core
value path. All other phases (5–8) are P2/P3 and can follow.

**Key constraints**:
- `ConsoleColors.cs` (T001) must be created BEFORE any console output is written — it is the single source of truth for all markup strings
- Storage migration (T011–T012) must be applied before any `SetTrainingLabelAsync` / `GetUntriagedAsync` calls are tested against a real DB
- Gmail action ALWAYS executes before training label storage (T031, T059) — enforced in both business logic services
- `IAnsiConsole` must be injected (not `AnsiConsole.Console` used directly) in all new console services for testability

---

## Summary

| Phase | Stories | Tasks | Parallel Opportunities |
|-------|---------|-------|------------------------|
| Phase 1 — Setup | N/A (cross-cutting) | T001–T010 (10 tasks) | T002–T010 all parallel |
| Phase 2 — Foundational | N/A (blocking prereqs) | T011–T026 (16 tasks) | T014–T018, T021–T026 parallel groups |
| Phase 3 — US1 Cold-Start | US1 (P1) 🎯 | T027–T038 (12 tasks) | T037–T038 parallel to impl |
| Phase 4 — US2 AI Triage | US2 (P1) | T039–T044 (6 tasks) | T043–T044 parallel to impl |
| Phase 5 — US3 Training | US3 (P2) | T045–T049 (5 tasks) | T049 parallel |
| Phase 6 — US4 Settings | US4 (P2) | T050–T054 (5 tasks) | T054 parallel |
| Phase 7 — US5 Help | US5 (P3) | T055–T057 (3 tasks) | T056–T057 parallel |
| Phase 8 — US6 Bulk Ops | US6 (P2) | T058–T062 (5 tasks) | T061–T062 parallel |
| Phase 9 — Polish | N/A | T063–T067 (5 tasks) | T065–T067 parallel |
| **Total** | **6 user stories** | **67 tasks** | **~30 parallel opportunities** |

**MVP**: Complete Phases 1–4 (T001–T044, 44 tasks) → full Email Triage (cold-start + AI-assisted)
