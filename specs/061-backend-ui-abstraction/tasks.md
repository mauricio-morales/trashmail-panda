# Tasks: Backend Refactoring for UI Abstraction

**Input**: Design documents from `/specs/061-backend-ui-abstraction/`
**Prerequisites**: plan.md ✅, spec.md ✅, research.md ✅, data-model.md ✅, contracts/ ✅, quickstart.md ✅

**Tests**: Not explicitly requested in the feature specification. Test tasks are omitted.

**Organization**: Tasks are grouped by user story. US1–US3 are all P1 priority; US4 is P2.

## Format: `[ID] [P?] [Story] Description`

- **[P]**: Can run in parallel with other [P] tasks (different files, no dependencies on each other)
- **[Story]**: Which user story (US1, US2, US3, US4)
- Exact file paths included in every task description

---

## Phase 1: Setup

**Purpose**: Establish baseline build before any changes

- [ ] T001 Verify the solution builds cleanly with `dotnet build` and note any pre-existing warnings

---

## Phase 2: User Story 1 — Classification Service Contract (Priority: P1) 🎯 MVP

**Goal**: Expose `IClassificationService` with `ClassifySingleAsync` and `ClassifyBatchAsync`, both returning `Result<T>`, with no UI references. Wraps `IMLModelProvider` and provides rule-based cold-start fallback.

**Independent Test**: Register a mock `IClassificationService` in DI and verify `ClassifyBatchAsync` returns an `ActionPrediction` list without requiring any Avalonia or Spectre.Console reference in the test project.

### Implementation for User Story 1

- [ ] T002 [P] [US1] Create `ReasoningSource` enum (`ML`, `RuleBased`) in `src/TrashMailPanda/TrashMailPanda/Models/ReasoningSource.cs`
- [ ] T003 [P] [US1] Create `ClassificationResult` sealed record (`EmailId`, `PredictedAction`, `Confidence`, `ReasoningSource`) in `src/TrashMailPanda/TrashMailPanda/Models/ClassificationResult.cs`
- [ ] T004 [US1] Create `IClassificationService` interface with `ClassifySingleAsync`, `ClassifyBatchAsync`, and `GetClassificationModeAsync` in `src/TrashMailPanda/TrashMailPanda/Services/IClassificationService.cs` — use contract from `specs/061-backend-ui-abstraction/contracts/IClassificationService.cs`
- [ ] T005 [US1] Implement `ClassificationService` wrapping `IMLModelProvider`: delegate to `ClassifyActionAsync`/`ClassifyActionBatchAsync`, map `ActionPrediction` → `ClassificationResult`, handle empty input (return empty list), cold-start fallback (rule-based with `ReasoningSource.RuleBased`), in `src/TrashMailPanda/TrashMailPanda/Services/ClassificationService.cs`
- [ ] T006 [US1] Register `IClassificationService` → `ClassificationService` as singleton in `src/TrashMailPanda/TrashMailPanda/Services/ServiceCollectionExtensions.cs` inside `AddApplicationServices()`

**Checkpoint**: `IClassificationService` is injectable and functional. `ClassifyBatchAsync` returns results with reasoning attribution. No Avalonia or console references in the service.

---

## Phase 3: User Story 2 — Application Orchestrator (Priority: P1)

**Goal**: Extract the workflow loop from `Program.cs` into a testable `IApplicationOrchestrator` that drives startup → mode selection → dispatch → exit, emitting typed `ApplicationEvent` records.

**Independent Test**: Call `IApplicationOrchestrator.RunAsync()` in a headless test with mock console services and confirm `ApplicationWorkflowCompletedEvent` is raised with `ExitCode = 0`.

### Models for User Story 2

- [ ] T007 [US2] Create abstract base `ApplicationEvent` record (`Timestamp`, `EventType`) in `src/TrashMailPanda/TrashMailPanda/Models/ApplicationEvent.cs`
- [ ] T008 [P] [US2] Create `ApplicationEventArgs` sealed class (wraps `ApplicationEvent`) in `src/TrashMailPanda/TrashMailPanda/Models/ApplicationEventArgs.cs`
- [ ] T009 [P] [US2] Create `ModeSelectionRequestedEvent` sealed record (`AvailableModes: IReadOnlyList<OperationalMode>`) in `src/TrashMailPanda/TrashMailPanda/Models/ModeSelectionRequestedEvent.cs`
- [ ] T010 [P] [US2] Create `BatchProgressEvent` sealed record (`ProcessedCount`, `TotalCount`, `EstimatedSecondsRemaining`) in `src/TrashMailPanda/TrashMailPanda/Models/BatchProgressEvent.cs`
- [ ] T011 [P] [US2] Create `ProviderStatusChangedEvent` sealed record (`ProviderName`, `IsHealthy`, `StatusMessage?`) in `src/TrashMailPanda/TrashMailPanda/Models/ProviderStatusChangedEvent.cs` — note: distinct from existing `ProviderStatusChangedEventArgs` in Services namespace
- [ ] T012 [P] [US2] Create `ApplicationWorkflowCompletedEvent` sealed record (`ExitCode`, `Reason`) in `src/TrashMailPanda/TrashMailPanda/Models/ApplicationWorkflowCompletedEvent.cs`

### Services for User Story 2

- [ ] T013 [US2] Create `IApplicationOrchestrator` interface with `RunAsync(CancellationToken)`, `IsRunning`, and `ApplicationEventRaised` event in `src/TrashMailPanda/TrashMailPanda/Services/IApplicationOrchestrator.cs` — use contract from `specs/061-backend-ui-abstraction/contracts/IApplicationOrchestrator.cs`
- [ ] T014 [US2] Implement `ApplicationOrchestrator` extracting the following from `Program.cs`: setup wizard check, `ConsoleStartupOrchestrator.InitializeProvidersAsync()`, `HandleStartupTrainingSyncAsync`, mode-selection loop via `ModeSelectionMenu`, mode dispatch to `IEmailTriageConsoleService`/`IBulkOperationConsoleService`/`IProviderSettingsConsoleService`/`TrainingConsoleService`, graceful shutdown on cancellation, duplicate-run guard (`IsRunning`), and event emission with try/catch per subscriber — in `src/TrashMailPanda/TrashMailPanda/Services/ApplicationOrchestrator.cs`
- [ ] T015 [US2] Refactor `Program.cs` to: keep host builder + DI + Serilog setup, remove `HandleStartupTrainingSyncAsync` and `HandleModeSelectionAsync` methods (moved to orchestrator), resolve `IApplicationOrchestrator`, call `await orchestrator.RunAsync(ct)`, return exit code — in `src/TrashMailPanda/TrashMailPanda/Program.cs`
- [ ] T016 [US2] Register `IApplicationOrchestrator` → `ApplicationOrchestrator` as singleton in `src/TrashMailPanda/TrashMailPanda/Services/ServiceCollectionExtensions.cs`

**Checkpoint**: `Program.cs` is a thin launcher. `ApplicationOrchestrator` drives the full workflow. Events fire for mode selection, provider status, and workflow completion. Application runs identically to before from the user's perspective.

---

## Phase 4: User Story 3 — UI-Agnostic Progress Events (Priority: P1)

**Goal**: Console TUI subscribes to `IApplicationOrchestrator.ApplicationEventRaised` via a dedicated renderer and outputs progress via Spectre.Console, without the orchestrator knowing which UI is listening.

**Independent Test**: Subscribe to `ApplicationEventRaised` from a no-UI test sink, invoke the triage workflow on 10 emails, and assert at least one `BatchProgressEvent` is received without any `AnsiConsole` import in the service assembly.

### Implementation for User Story 3

- [ ] T017 [US3] Implement `ConsoleEventRenderer` that subscribes to `IApplicationOrchestrator.ApplicationEventRaised` and pattern-matches on event types to render with Spectre.Console: `ModeSelectionRequestedEvent` → log available modes, `BatchProgressEvent` → update progress display, `ProviderStatusChangedEvent` → show health status, `ApplicationWorkflowCompletedEvent` → exit summary — in `src/TrashMailPanda/TrashMailPanda/Services/Console/ConsoleEventRenderer.cs`
- [ ] T018 [US3] Register `ConsoleEventRenderer` as singleton in `src/TrashMailPanda/TrashMailPanda/Services/ServiceCollectionExtensions.cs`
- [ ] T019 [US3] Wire `ConsoleEventRenderer` subscription to orchestrator events: resolve both from DI in `Program.cs` and call the renderer's subscribe method before `orchestrator.RunAsync(ct)` — in `src/TrashMailPanda/TrashMailPanda/Program.cs`

**Checkpoint**: Progress events flow from orchestrator → renderer → terminal output. The orchestrator has zero Spectre.Console references. Multiple subscribers can coexist without blocking.

---

## Phase 5: User Story 4 — Avalonia UI Excluded from Default Build (Priority: P2)

**Goal**: `dotnet build` succeeds in headless environments without Avalonia. Re-enable with `/p:EnableAvaloniaUI=true`.

**Independent Test**: Run `dotnet build` in a Docker container with no display and confirm success. Then build with `/p:EnableAvaloniaUI=true`.

### Implementation for User Story 4

- [ ] T020 [US4] Add `EnableAvaloniaUI` MSBuild property (default `false`) to `src/TrashMailPanda/TrashMailPanda/TrashMailPanda.csproj`: wrap Avalonia package refs (`Avalonia`, `Avalonia.Desktop`, `Avalonia.Themes.Fluent`, `Avalonia.Fonts.Inter`, `Avalonia.Diagnostics`) in `<ItemGroup Condition="'$(EnableAvaloniaUI)' == 'true'">`, exclude `Views/**`, `ViewModels/**`, `App.axaml.cs`, `Styles/**`, `*.axaml` from compilation when disabled, set `OutputType` to `Exe` when disabled (instead of `WinExe`), set `EnableDefaultAvaloniaItems` to `false` when disabled
- [ ] T021 [US4] Add conditional `#if`-style guard or MSBuild condition to exclude ViewModel DI registrations in `src/TrashMailPanda/TrashMailPanda/Services/ServiceCollectionExtensions.cs` when `EnableAvaloniaUI` is not set — use a `DefineConstants` condition in the csproj (`AVALONIA_UI`) so relevant registrations compile out
- [ ] T022 [US4] Verify `dotnet build` succeeds with Avalonia excluded and `dotnet build /p:EnableAvaloniaUI=true` still compiles all Avalonia Views/ViewModels

**Checkpoint**: Default `dotnet build` produces a console-only app. Avalonia source files are preserved, not deleted. Opt-in re-enablement works.

---

## Phase 6: Polish & Cross-Cutting Concerns

**Purpose**: Validation, cleanup, and cross-story verification

- [ ] T023 [P] Verify zero Avalonia or Spectre.Console references exist in `IClassificationService`, `ClassificationService`, `IApplicationOrchestrator`, and `ApplicationOrchestrator` source files (assembly dependency scan)
- [ ] T024 [P] Ensure `dotnet format --verify-no-changes` passes for all new and modified files
- [ ] T025 Run end-to-end quickstart.md validation: resolve `IClassificationService` and `IApplicationOrchestrator` from DI, invoke `ClassifyBatchAsync` with empty input, verify `Result.Success` with empty list, invoke `RunAsync` and confirm `ApplicationWorkflowCompletedEvent` emission

---

## Dependencies & Execution Order

### Phase Dependencies

- **Setup (Phase 1)**: No dependencies — start immediately
- **US1 (Phase 2)**: Depends on Setup (Phase 1) — can start once build verified
- **US2 (Phase 3)**: Depends on Setup (Phase 1) — can start in parallel with US1 (models are independent); T014 implementation will reference `IClassificationService` from US1 for triage dispatch
- **US3 (Phase 4)**: Depends on US2 (Phase 3) — requires `IApplicationOrchestrator` and `ApplicationEvent` types
- **US4 (Phase 5)**: Depends on Setup (Phase 1) — fully independent of US1/US2/US3, can run in parallel
- **Polish (Phase 6)**: Depends on all user stories being complete

### User Story Dependencies

```
US1 (Classification Service) ──┐
                                ├──→ US3 (Progress Events) ──→ Polish
US2 (Application Orchestrator) ─┘
                                             
US4 (Avalonia Exclusion) ─────────────────────────────────→ Polish
```

- **US1** and **US2**: Can start in parallel. US2's `ApplicationOrchestrator` calls `IClassificationService` (from US1), so T014 should start after T005 completes.
- **US3**: Depends on US2 completion (needs orchestrator + event types).
- **US4**: Fully independent — can be done at any time after Setup.

### Within Each User Story

- Models/records before interfaces
- Interfaces before implementations
- Implementations before DI registration
- DI registration before Program.cs wiring

### Parallel Opportunities

**Within US1**: T002 (ReasoningSource) and T003 (ClassificationResult) can run in parallel — different files, no dependencies.

**Within US2**: After T007 (ApplicationEvent base), tasks T008–T012 can ALL run in parallel — each is a separate file inheriting from the same base.

**Cross-Story**: US1 and US4 can run fully in parallel. US1 and US2 models (T002–T003 and T007–T012) can run in parallel.

---

## Parallel Example: User Story 2

```bash
# Step 1: Create base record (sequential — blocks everything below)
T007: ApplicationEvent.cs

# Step 2: Create all concrete event types + wrapper (parallel — 5 files at once)
T008: ApplicationEventArgs.cs       ⎤
T009: ModeSelectionRequestedEvent.cs ⎥
T010: BatchProgressEvent.cs          ⎥ All parallel
T011: ProviderStatusChangedEvent.cs  ⎥
T012: ApplicationWorkflowCompletedEvent.cs ⎦

# Step 3: Interface → Implementation → DI → Program.cs (sequential chain)
T013 → T014 → T015 → T016
```

---

## Implementation Strategy

### MVP First (User Story 1 Only)

1. Complete Phase 1: Setup
2. Complete Phase 2: US1 — Classification Service Contract
3. **STOP and VALIDATE**: Verify `IClassificationService` is injectable and returns `ClassificationResult` with correct `ReasoningSource`
4. This alone satisfies GitHub Issue #61's contract requirement

### Incremental Delivery

1. **US1** → Classification contract usable by any caller → Deploy/Validate
2. **US2** → Program.cs refactored, orchestrator testable → Deploy/Validate
3. **US3** → Progress rendering decoupled from business logic → Deploy/Validate
4. **US4** → Build hygiene, headless CI compat → Deploy/Validate
5. Each story adds value without breaking previous stories

### Files Created (14 new)

| File | Story |
|------|-------|
| `Models/ReasoningSource.cs` | US1 |
| `Models/ClassificationResult.cs` | US1 |
| `Services/IClassificationService.cs` | US1 |
| `Services/ClassificationService.cs` | US1 |
| `Models/ApplicationEvent.cs` | US2 |
| `Models/ApplicationEventArgs.cs` | US2 |
| `Models/ModeSelectionRequestedEvent.cs` | US2 |
| `Models/BatchProgressEvent.cs` | US2 |
| `Models/ProviderStatusChangedEvent.cs` | US2 |
| `Models/ApplicationWorkflowCompletedEvent.cs` | US2 |
| `Services/IApplicationOrchestrator.cs` | US2 |
| `Services/ApplicationOrchestrator.cs` | US2 |
| `Services/Console/ConsoleEventRenderer.cs` | US3 |

### Files Modified (3 existing)

| File | Stories |
|------|---------|
| `Services/ServiceCollectionExtensions.cs` | US1, US2, US3, US4 |
| `Program.cs` | US2, US3 |
| `TrashMailPanda.csproj` | US4 |

---

## Notes

- [P] tasks = different files, no dependencies on each other
- [Story] label maps each task to a specific user story for traceability
- Each user story is independently completable and testable
- Commit after each phase or logical group of tasks
- Stop at any checkpoint to validate the story independently
- All new services use `Result<T>` return types — never throw from providers/services
- One public type per file — enforced across all new files
- `ApplicationOrchestrator` delegates to existing console services — it coordinates, doesn't re-implement
- Event hierarchy uses standard .NET `event EventHandler<T>` — no System.Reactive dependency
