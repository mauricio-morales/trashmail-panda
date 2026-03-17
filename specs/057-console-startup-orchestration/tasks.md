---
description: "Task list for Console Startup Orchestration & Health Checks implementation"
---

# Tasks: Console Startup Orchestration & Health Checks

**Feature**: 057-console-startup-orchestration  
**Input**: Design documents from `/specs/057-console-startup-orchestration/`  
**Prerequisites**: plan.md, spec.md, research.md, data-model.md, contracts/, quickstart.md

**Tests**: Tests are NOT explicitly requested in the specification, so test tasks are excluded per template guidelines.

**Organization**: Tasks are grouped by user story to enable independent implementation and testing of each story.

## Format: `[ID] [P?] [Story] Description`

- **[P]**: Can run in parallel (different files, no dependencies)
- **[Story]**: Which user story this task belongs to (e.g., US1, US2, US3)
- Include exact file paths in descriptions

---

## Phase 1: Setup (Shared Infrastructure)

**Purpose**: Project initialization and basic structure

- [X] T001 Add Spectre.Console NuGet package (v0.48.0+) to src/TrashMailPanda/TrashMailPanda/TrashMailPanda.csproj
- [X] T002 Create console services directory structure at src/TrashMailPanda/TrashMailPanda/Services/Console/
- [X] T003 Create console models directory structure at src/TrashMailPanda/TrashMailPanda/Models/Console/

---

## Phase 2: Foundational (Blocking Prerequisites)

**Purpose**: Core infrastructure that MUST be complete before ANY user story can be implemented

**⚠️ CRITICAL**: No user story work can begin until this phase is complete

- [X] T004 [P] Create ProviderType enum in src/TrashMailPanda/TrashMailPanda/Models/Console/ProviderType.cs (Required, Optional)
- [X] T005 [P] Create InitializationStatus enum in src/TrashMailPanda/TrashMailPanda/Models/Console/InitializationStatus.cs (NotStarted, Initializing, HealthChecking, Ready, Failed, Timeout)
- [X] T006 [P] Create SequenceStatus enum in src/TrashMailPanda/TrashMailPanda/Models/Console/SequenceStatus.cs (Initializing, Completed, Failed, Cancelled)
- [X] T007 [P] Create HealthStatus enum in src/TrashMailPanda/TrashMailPanda/Models/Console/HealthStatus.cs (Healthy, Degraded, Critical, Unknown)
- [X] T008 [P] Create WizardStep enum in src/TrashMailPanda/TrashMailPanda/Models/Console/WizardStep.cs (Welcome, StorageSetup, GmailSetup, Confirmation, Complete)
- [X] T009 [P] Create ErrorDetailLevel enum in src/TrashMailPanda/TrashMailPanda/Models/Console/ErrorDetailLevel.cs (Minimal, Standard, Verbose)
- [X] T010 [P] Create OperationalMode enum in src/TrashMailPanda/TrashMailPanda/Models/Console/OperationalMode.cs (EmailTriage, BulkOperations, ProviderSettings, UIMode, Exit)
- [X] T011 [P] Create ProviderInitializationState model in src/TrashMailPanda/TrashMailPanda/Models/Console/ProviderInitializationState.cs with properties: ProviderName, ProviderType, Status, StatusMessage, HealthStatus, Error, StartTime, CompletionTime, Duration
- [X] T012 [P] Create StartupSequenceState model in src/TrashMailPanda/TrashMailPanda/Models/Console/StartupSequenceState.cs with properties: ProviderStates, CurrentProviderIndex, OverallStatus, StartTime, CompletionTime, TotalDuration, RequiredProvidersHealthy
- [X] T013 [P] Create ConfigurationWizardState model in src/TrashMailPanda/TrashMailPanda/Models/Console/ConfigurationWizardState.cs with properties: CurrentStep, StorageConfigured, GmailConfigured, Errors
- [X] T014 [P] Create ConsoleDisplayOptions model in src/TrashMailPanda/TrashMailPanda/Models/Console/ConsoleDisplayOptions.cs with properties: ShowTimestamps, ShowDuration, UseColors, StatusRefreshInterval, ErrorDetailLevel
- [X] T015 Add ConsoleDisplayOptions section to appsettings.json with default values (ShowTimestamps: true, ShowDuration: true, UseColors: true, StatusRefreshInterval: 200ms, ErrorDetailLevel: Standard)

**Checkpoint**: Foundation ready - user story implementation can now begin in parallel

---

## Phase 3: User Story 1 - Sequential Provider Initialization on Startup (Priority: P1) 🎯 MVP

**Goal**: Display welcome banner and initialize providers sequentially (Storage → Gmail) with real-time status updates

**Independent Test**: Start application and observe sequential initialization messages for each provider in dependency order

### Implementation for User Story 1

- [X] T016 [P] [US1] Create ConsoleStatusDisplay service in src/TrashMailPanda/TrashMailPanda/Services/Console/ConsoleStatusDisplay.cs with methods: DisplayWelcomeBanner(), DisplayProviderInitializing(), DisplayProviderSuccess(), DisplayProviderFailed(), DisplayHealthCheckStatus()
- [X] T017 [US1] Create ConsoleStartupOrchestrator service in src/TrashMailPanda/TrashMailPanda/Services/Console/ConsoleStartupOrchestrator.cs with InitializeProvidersAsync() method implementing sequential initialization (Storage → Gmail)
- [X] T018 [US1] Implement provider initialization loop in ConsoleStartupOrchestrator with state transitions (NotStarted → Initializing → HealthChecking → Ready/Failed/Timeout)
- [X] T019 [US1] Add timeout handling per provider (30 seconds) using CancellationTokenSource in ConsoleStartupOrchestrator.InitializeProviderAsync()
- [X] T020 [US1] Add Ctrl+C cancellation handling in ConsoleStartupOrchestrator with graceful shutdown and provider cleanup
- [X] T021 [US1] Modify Program.cs to replace Avalonia startup with ConsoleStartupOrchestrator entry point and display welcome banner
- [X] T022 [US1] Implement color-coded status indicators in ConsoleStatusDisplay using Spectre.Console markup (green ✓, bold red ✗, yellow ⚠, blue ℹ, animated ●)
- [X] T023 [US1] Add logging for all state transitions and initialization events using ILogger<ConsoleStartupOrchestrator>

**Checkpoint**: ✅ User Story 1 is fully functional - application starts with console entry point, displays banner, initializes providers sequentially with status updates, handles Ctrl+C gracefully

---

## Phase 4: User Story 2 - First-Time Setup with Configuration Wizard (Priority: P1)

**Goal**: Detect missing configuration on first startup and launch sequential interactive wizard (Storage → Gmail)

**Independent Test**: Delete all configuration, run application, verify wizard completes each provider setup sequentially and automatically triggers initialization

### Implementation for User Story 2

- [X] T024 [P] [US2] Create ConfigurationWizard service in src/TrashMailPanda/TrashMailPanda/Services/Console/ConfigurationWizard.cs with RunAsync() method implementing sequential wizard flow
- [X] T025 [US2] Implement welcome step in ConfigurationWizard.DisplayWelcomeAsync() with setup overview and links to Google Cloud Console
- [X] T026 [US2] Implement Storage setup step in ConfigurationWizard.ConfigureStorageAsync() using Spectre.Console TextPrompt for database path and SelectionPrompt for encryption option
- [X] T027 [US2] Implement Gmail setup step in ConfigurationWizard.ConfigureGmailAsync() with OAuth Client ID/Secret prompts, instructions for Google Cloud Console, and browser OAuth flow integration
- [X] T028 [US2] Implement confirmation step in ConfigurationWizard.DisplayConfirmationAsync() showing summary of configured providers with checkmarks
- [X] T029 [US2] Add configuration persistence in ConfigurationWizard.SaveConfigurationsAsync() using SecureStorageManager for OAuth tokens and appsettings.json for other settings
- [X] T030 [US2] Add automatic transition from wizard completion to provider initialization (call ConsoleStartupOrchestrator.InitializeProvidersAsync() without restart)
- [X] T031 [US2] Add configuration detection logic in ConsoleStartupOrchestrator.CheckConfigurationAsync() to determine if wizard should run
- [X] T032 [US2] Integrate wizard invocation in Program.cs when configuration is missing or incomplete

**Checkpoint**: At this point, User Stories 1 AND 2 should both work - first-time users complete wizard and see automatic initialization

---

## Phase 5: User Story 3 - Provider Health Check During Initialization (Priority: P1)

**Goal**: Perform immediate health check after each provider initialization with color-coded status display (green ✓, bold red ✗, yellow ⚠)

**Independent Test**: Configure providers with various health states and verify health checks occur immediately after each provider's initialization with correct color coding

### Implementation for User Story 3

- [ ] T033 [P] [US3] Add health check invocation in ConsoleStartupOrchestrator.InitializeProviderAsync() immediately after each provider's InitializeAsync() completes
- [ ] T034 [US3] Implement health check result mapping in ConsoleStatusDisplay.DisplayHealthCheckStatus() to map HealthCheckResult.Status to console colors (Healthy=green, Degraded=yellow, Critical=bold red)
- [ ] T035 [US3] Add health check timeout handling (10 seconds per check) in ConsoleStartupOrchestrator.PerformHealthCheckAsync()
- [ ] T036 [US3] Implement OAuth scope validation logic in Gmail provider health check to detect missing scopes with INSUFFICIENT_SCOPES error code
- [ ] T037 [US3] Add scope mismatch error display in ConsoleStatusDisplay.DisplayScopeMismatch() showing required vs. current scopes with re-authorization instructions
- [ ] T038 [US3] Add health check logging for all results (Healthy, Degraded, Critical) with detailed status information using ILogger
- [ ] T039 [US3] Update GoogleConfig in appsettings.json to include RequiredScopes array with gmail.modify scope

**Checkpoint**: All user stories should now show health checks with color-coded indicators immediately after initialization

---

## Phase 6: User Story 4 - Required Provider Failure Handling (Priority: P2)

**Goal**: Halt startup sequence when required providers (Storage, Gmail) fail health checks and display actionable error messages with recovery options

**Independent Test**: Simulate provider failures during initialization and verify process halts with clear recovery options (Reconfigure/Exit)

### Implementation for User Story 4

- [ ] T040 [P] [US4] Create error recovery menu in ConsoleStatusDisplay.DisplayErrorRecoveryMenu() using Spectre.Console SelectionPrompt with options: Reconfigure Provider, Retry Initialization, Exit
- [ ] T041 [US4] Implement failure detection logic in ConsoleStartupOrchestrator.HandleProviderFailureAsync() to halt sequence when required providers fail
- [ ] T042 [US4] Add error detail display in ConsoleStatusDisplay.DisplayProviderError() with three detail levels (Minimal, Standard, Verbose) based on ConsoleDisplayOptions.ErrorDetailLevel
- [ ] T043 [US4] Implement retry logic in ConsoleStartupOrchestrator.RetryProviderInitializationAsync() when user selects "Retry Initialization"
- [ ] T044 [US4] Integrate wizard re-invocation in ConsoleStartupOrchestrator.ReconfigureProviderAsync() when user selects "Reconfigure Provider"
- [ ] T045 [US4] Add provider-specific error messages in ConsoleStatusDisplay for common failure scenarios (AUTH_TOKEN_EXPIRED, DB_LOCKED, NETWORK_ERROR, INSUFFICIENT_SCOPES)
- [ ] T046 [US4] Implement graceful exit handling in Program.cs when user selects "Exit" from error recovery menu

**Checkpoint**: Required provider failures now halt startup with actionable recovery options

---

## Phase 7: User Story 5 - Mode Selection After Successful Startup (Priority: P2)

**Goal**: Display interactive mode selection menu after all required providers complete initialization successfully

**Independent Test**: Complete successful startup and navigate mode selection menu with keyboard input (arrow keys, Enter, Q)

### Implementation for User Story 5

- [ ] T047 [P] [US5] Create ModeSelectionMenu service in src/TrashMailPanda/TrashMailPanda/Services/Console/ModeSelectionMenu.cs with ShowAsync() method
- [ ] T048 [US5] Implement provider status summary display in ModeSelectionMenu.DisplayProviderStatusAsync() showing health status and key metrics for each provider
- [ ] T049 [US5] Implement mode selection prompt in ModeSelectionMenu.PromptForModeAsync() using Spectre.Console SelectionPrompt with operational modes (EmailTriage, BulkOperations, ProviderSettings, UIMode, Exit)
- [ ] T050 [US5] Add mode availability filtering in ModeSelectionMenu.GetAvailableModesAsync() based on provider health status (all current modes require Storage + Gmail)
- [ ] T051 [US5] Implement keyboard navigation handlers in ModeSelectionMenu for arrow keys, Enter (select), Q/Escape (exit)
- [ ] T052 [US5] Add mode selection invocation in ConsoleStartupOrchestrator after successful initialization (when RequiredProvidersHealthy == true)
- [ ] T053 [US5] Implement stub mode handlers in Program.cs for each operational mode (EmailTriage, BulkOperations, ProviderSettings, UIMode, Exit) with "Coming soon" messages

**Checkpoint**: All user stories should now be independently functional - startup completes and mode selection menu displays

---

## Phase 8: Polish & Cross-Cutting Concerns

**Purpose**: Improvements that affect multiple user stories

- [ ] T054 [P] Add comprehensive XML documentation comments to all public methods in ConsoleStartupOrchestrator, ConfigurationWizard, ModeSelectionMenu, ConsoleStatusDisplay
- [ ] T055 [P] Update quickstart.md with screenshots/examples of console output for all user scenarios
- [ ] T056 [P] Update CLAUDE.md and .github/copilot-instructions.md with console startup patterns and Spectre.Console usage examples
- [ ] T057 Add progress indicators (spinners, status displays) for long-running operations using Spectre.Console.Status() and Progress()
- [ ] T058 Implement countdown timer display during timeout scenarios in ConsoleStatusDisplay.DisplayTimeoutWarning()
- [ ] T059 [P] Add null reference validation and defensive coding in all console services (ConsoleStartupOrchestrator, ConfigurationWizard, ModeSelectionMenu)
- [ ] T060 Optimize startup performance by reducing redundant configuration reads in ConsoleStartupOrchestrator
- [ ] T061 Run quickstart.md validation to verify all documented scenarios work correctly
- [ ] T062 Verify dotnet format passes with no warnings across all new console code

---

## Dependencies & Execution Order

### Phase Dependencies

- **Setup (Phase 1)**: No dependencies - can start immediately
- **Foundational (Phase 2)**: Depends on Setup completion - BLOCKS all user stories
- **User Stories (Phase 3-7)**: All depend on Foundational phase completion
  - User Story 1 (Phase 3): Sequential provider initialization - MVP foundation
  - User Story 2 (Phase 4): Configuration wizard - extends US1 with first-time setup
  - User Story 3 (Phase 5): Health checks - extends US1 with validation
  - User Story 4 (Phase 6): Error handling - extends US3 with recovery flows
  - User Story 5 (Phase 7): Mode selection - requires US1 successful completion
- **Polish (Phase 8)**: Depends on all user stories being complete

### User Story Dependencies

- **User Story 1 (P1)**: Can start after Foundational (Phase 2) - No dependencies on other stories - REQUIRED FOR MVP
- **User Story 2 (P1)**: Depends on User Story 1 (needs ConsoleStartupOrchestrator) - REQUIRED FOR MVP
- **User Story 3 (P1)**: Depends on User Story 1 (extends initialization with health checks) - REQUIRED FOR MVP
- **User Story 4 (P2)**: Depends on User Story 3 (handles health check failures) - Can be deferred post-MVP
- **User Story 5 (P2)**: Depends on User Story 1 (requires successful startup) - Can be deferred post-MVP

### Within Each User Story

**User Story 1**:
- T016 (ConsoleStatusDisplay) and T017 (ConsoleStartupOrchestrator) can run in parallel
- T018-T020 depend on T017 (extend orchestrator)
- T021 (Program.cs modification) depends on T017 (orchestrator must exist)
- T022 (color coding) depends on T016 (extends display service)
- T023 (logging) can run in parallel with other tasks

**User Story 2**:
- T024 (wizard service) must complete before T025-T029 (wizard steps)
- T025-T027 (wizard steps) can run in parallel (different methods)
- T028 depends on T025-T027 (confirmation shows what was configured)
- T029 (persistence) depends on T025-T027 (must have data to save)
- T030 depends on T029 and T017 (connects wizard to orchestrator)
- T031-T032 depend on T024 (wizard must exist to invoke it)

**User Story 3**:
- T033 depends on T017 (extends orchestrator from US1)
- T034 depends on T016 (extends status display from US1)
- T035 depends on T033 (timeout extends health check invocation)
- T036-T037 can run in parallel (different providers' health logic)
- T038 can run in parallel with T034-T037
- T039 can run in parallel (config file change)

**User Story 4**:
- T040 depends on T016 (extends display service)
- T041 depends on T017 and T033 (handles failures from orchestrator)
- T042 depends on T040 (error display uses recovery menu)
- T043-T044 depend on T041 (retry/reconfigure options)
- T045 depends on T042 (extends error display)
- T046 depends on T021 (Program.cs integration)

**User Story 5**:
- T047 (menu service) must complete before T048-T051
- T048-T051 can run in parallel (different menu methods)
- T052 depends on T047 and T017 (integrates menu with orchestrator)
- T053 depends on T047 (stub handlers for selected modes)

### Parallel Opportunities

- **Phase 1 Setup**: All 3 tasks can run in parallel (different directories/files)
- **Phase 2 Foundational**: All enum creation tasks (T004-T010) can run in parallel, model creation tasks (T011-T014) can run in parallel after enums
- **Within User Stories**: Tasks marked [P] can run in parallel
- **Cross-Story Parallelism**: After MVP (US1-US3), US4 and US5 can be developed in parallel by different team members

---

## Parallel Example: User Story 1

```bash
# Start in parallel:
Task T016: "Create ConsoleStatusDisplay service"
Task T017: "Create ConsoleStartupOrchestrator service"

# After T017 completes, start in parallel:
Task T018: "Implement provider initialization loop"
Task T019: "Add timeout handling"
Task T020: "Add Ctrl+C cancellation"
Task T023: "Add logging"

# After T016 completes:
Task T022: "Implement color-coded status indicators"
```

---

## Implementation Strategy

### MVP First (User Stories 1-3 Only)

1. Complete Phase 1: Setup
2. Complete Phase 2: Foundational (CRITICAL - blocks all stories)
3. Complete Phase 3: User Story 1 - Sequential initialization
4. Complete Phase 4: User Story 2 - Configuration wizard
5. Complete Phase 5: User Story 3 - Health checks
6. **STOP and VALIDATE**: Test all three P1 stories together
7. Deploy/demo MVP

### Incremental Delivery

1. Complete Setup + Foundational → Foundation ready
2. Add User Story 1 → Test independently → Core startup works
3. Add User Story 2 → Test independently → First-time setup works
4. Add User Story 3 → Test independently → MVP complete (all P1 stories)
5. Add User Story 4 → Test independently → Error recovery works
6. Add User Story 5 → Test independently → Full feature complete

### Parallel Team Strategy

With multiple developers (after Foundational phase):

**Sprint 1 (MVP)**:
- Developer A: User Story 1 (T016-T023)
- Developer B: User Story 2 (T024-T032) - starts after T017 from Dev A
- Developer C: User Story 3 (T033-T039) - starts after T017 from Dev A

**Sprint 2 (Post-MVP)**:
- Developer A: User Story 4 (T040-T046)
- Developer B: User Story 5 (T047-T053)
- Developer C: Polish tasks (T054-T062)

---

## Notes

- All tasks include exact file paths for C# implementation
- [P] tasks = different files, no dependencies - safe for parallel execution
- [Story] label maps task to specific user story for traceability
- Each user story should be independently completable and testable
- MVP scope: User Stories 1-3 (all P1 priority) - provides complete startup + wizard + health checks
- US4 and US5 (P2 priority) add error recovery and mode selection - can be deferred post-MVP
- Commit after each task or logical group for better version control
- Stop at any checkpoint to validate story independently
- Console-first architecture replaces Avalonia UI startup per constitution violation justification
- Spectre.Console is the single source of truth for all console formatting and interaction
- All state management uses the models from Phase 2 (Foundational)
- Tests are OPTIONAL per spec - not included in this task list
