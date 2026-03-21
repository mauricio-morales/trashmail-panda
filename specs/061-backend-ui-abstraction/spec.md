# Feature Specification: Backend Refactoring for UI Abstraction

**Feature Branch**: `061-backend-ui-abstraction`  
**Created**: 2026-03-21  
**Status**: Draft  
**GitHub Issue**: [#63](https://github.com/mauricio-morales/trashmail-panda/issues/63)  
**Dependencies**: GitHub Issue #61 (IClassificationService contract)

## Context & Current State

The application currently has business logic partially embedded in Avalonia ViewModels
(e.g., `EmailDashboardViewModel`, `ProviderStatusDashboardViewModel`) and an Avalonia
`App.axaml.cs` startup path that runs even though the console-first architecture (spec 057)
already provides a working console startup.

**What already exists** (not in scope for this feature):
- `IEmailTriageService` / `EmailTriageService` — UI-agnostic triage coordination (done in spec 060)
- `ConsoleStartupOrchestrator` — console startup replaces Avalonia startup (done in spec 057)
- `IMLModelProvider` — ML classification provider (done in spec 059)

**What this feature adds**:
- `IClassificationService` — named contract for the end-to-end email classification workflow
- `IApplicationOrchestrator` — drives the full application workflow (mode selection → triage → bulk ops → settings → exit)
- UI-agnostic progress and status event system
- Avalonia project excluded from the default build (kept for future re-enablement)

## User Scenarios & Testing *(mandatory)*

### User Story 1 - Classification Service Contract (Priority: P1)

A developer building either the Console TUI or a future Avalonia UI depends on a single, stable
`IClassificationService` interface to drive the email classification workflow, without importing
any UI-specific types. Both UI layers call the same service and receive the same domain results.

**Why this priority**: Foundation for all other stories and for issue #61. Without a stable
contract, both UI layers must duplicate or hard-code their classification coordination.

**Independent Test**: Register a mock `IClassificationService` in the DI container and verify
that `ClassifyBatchAsync` returns an `ActionPrediction` list without requiring any Avalonia or
console reference in the test project.

**Acceptance Scenarios**:

1. **Given** a batch of `EmailFeatureVector` objects, **when** `ClassifyBatchAsync` is called on `IClassificationService`, **then** it returns `Result<IReadOnlyList<ClassificationResult>>` with one result per input, each containing the predicted action, confidence, and reasoning source (ML model or rule-based fallback).
2. **Given** no trained ML model is loaded, **when** `ClassifyBatchAsync` is called, **then** results are returned using the rule-based cold-start fallback without throwing an exception.
3. **Given** a single email, **when** `ClassifySingleAsync` is called, **then** the result matches what `ClassifyBatchAsync` would return for a one-item batch.
4. **Given** the service is injected via DI into both `ConsoleTriageCommand` and a hypothetical `AvaloniaTriageView`, **then** both compile and run without referencing each other or any UI-specific namespace.

---

### User Story 2 - Application Orchestrator (Priority: P1)

A developer or operator starts the application from the console and navigates through the full
workflow (startup → mode selection → email triage → exit) using `IApplicationOrchestrator`, without
any knowledge of which underlying UI is rendering each step.

**Why this priority**: Without `IApplicationOrchestrator`, the Console TUI modes (triage,
bulk operations, settings) remain uncoordinated scripts in `Program.cs` and cannot be
driven programmatically or tested in isolation.

**Independent Test**: Call `IApplicationOrchestrator.RunAsync()` in a headless integration
test (using a mock `IConsoleRenderer`) and confirm the full triage-then-exit workflow
reaches the `ApplicationWorkflowCompleted` event with `ExitCode = 0`.

**Acceptance Scenarios**:

1. **Given** all required providers are healthy, **when** `RunAsync` is called, **then** the orchestrator raises a `ModeSelectionRequested` event and waits for a mode selection before proceeding.
2. **Given** the user selects "Email Triage" mode, **when** the orchestrator processes that selection, **then** it calls `IClassificationService` to begin the triage workflow and emits progress events for each classified batch.
3. **Given** the user selects "Exit", **when** the orchestrator processes that selection, **then** it performs a graceful shutdown of all providers and emits `ApplicationWorkflowCompleted` with `ExitCode = 0`.
4. **Given** a required provider is unhealthy at startup, **when** `RunAsync` is called, **then** the orchestrator emits a `ProviderSetupRequired` event rather than proceeding to mode selection.

---

### User Story 3 - UI-Agnostic Progress Events (Priority: P1)

Any UI layer (console or Avalonia) subscribes to a single progress/status event stream from
`IApplicationOrchestrator` and renders it independently, without the service knowing which UI
is listening.

**Why this priority**: Without a UI-agnostic event system, progress updates are hard-coded with
`AnsiConsole` calls inside business service methods, preventing Avalonia UI re-enablement and
making unit testing impossible.

**Independent Test**: Subscribe to `IApplicationOrchestrator.Progress` in a no-UI test, call
`ClassifyBatchAsync` on 10 emails, and assert that at least one `BatchProgressEvent` is received
per logical batch without any AnsiConsole import in the service assembly.

**Acceptance Scenarios**:

1. **Given** a long-running classification batch, **when** classification progresses, **then** the orchestrator emits `BatchProgressEvent` records containing `ProcessedCount`, `TotalCount`, and `EstimatedSecondsRemaining`.
2. **Given** a provider status change occurs, **when** the change is detected, **then** the orchestrator emits a `ProviderStatusChangedEvent` with the provider name and new health state.
3. **Given** two concurrent UI subscribers (one console renderer, one no-op test sink), **when** events are emitted, **then** both subscribers receive the same events and neither blocks the other.

---

### User Story 4 - Avalonia UI Excluded from Default Build (Priority: P2)

A developer runs `dotnet build` or `dotnet run` from the project root and the Avalonia UI
project is excluded by default, keeping the build fast and free of graphical-context
dependencies while preserving all Avalonia source files for future re-enablement.

**Why this priority**: P2 because the console already works without disabling Avalonia; this is
about build hygiene and prevents accidental Avalonia dependencies sneaking into console services.

**Independent Test**: Run `dotnet build` in a Docker container (no display) and confirm it
succeeds. Then add `/p:EnableAvaloniaUI=true` and confirm the Avalonia project also builds.

**Acceptance Scenarios**:

1. **Given** no MSBuild property override, **when** `dotnet build` is run, **then** the Avalonia project files are excluded from compilation and the build succeeds in a headless environment.
2. **Given** `/p:EnableAvaloniaUI=true`, **when** `dotnet build` is run, **then** the Avalonia project is included and its Views/ViewModels compile without errors.
3. **Given** the Avalonia project is excluded, **when** `dotnet run` starts the application, **then** the console TUI starts normally with no references to Avalonia assemblies in the running process.

---

### Edge Cases

- What happens when `IClassificationService` is called with an empty input list? → Returns `Result.Success` with an empty list (no error, no ML call).
- What happens when a UI subscriber to progress events throws an exception? → The orchestrator catches and logs it; other subscribers continue to receive events.
- What if Avalonia project is re-enabled but `ConsoleStartupOrchestrator` is already registered? → Both registrations are guarded by a build flag; only one startup path is active at a time.
- What happens if `IApplicationOrchestrator.RunAsync` is called while already running? → Returns `Result.Failure` with an `InvalidOperationError` immediately without starting a second workflow.

## Requirements *(mandatory)*

### Functional Requirements

- **FR-001**: System MUST expose `IClassificationService` with at minimum `ClassifySingleAsync` and `ClassifyBatchAsync` methods, both returning `Result<T>` types.
- **FR-002**: `IClassificationService` MUST NOT import or reference any Avalonia, Spectre.Console, or console-rendering type.
- **FR-003**: System MUST expose `IApplicationOrchestrator` with a `RunAsync` method that drives the full application workflow.
- **FR-004**: `IApplicationOrchestrator` MUST emit a typed progress event stream (`IObservable<ApplicationEvent>` or equivalent .NET event pattern) that UI layers subscribe to.
- **FR-005**: All classification logic currently embedded in ViewModels MUST be moved to `IClassificationService` or `IEmailTriageService`; ViewModels MUST NOT contain domain classification calls.
- **FR-006**: The Avalonia project MUST be conditionally excluded from the build by default via an MSBuild property (e.g., `EnableAvaloniaUI`), defaulting to `false`.
- **FR-007**: All existing Avalonia source files (Views, ViewModels, styles, XAML) MUST be preserved as-is; only build inclusion is changed.
- **FR-008**: `IApplicationOrchestrator` MUST raise specific events: `ModeSelectionRequested`, `BatchProgressEvent`, `ProviderStatusChangedEvent`, `ApplicationWorkflowCompleted`.
- **FR-009**: `IApplicationOrchestrator` MUST support `CancellationToken` for graceful shutdown via Ctrl+C.
- **FR-010**: `IClassificationService` registration in DI MUST be independent of which UI assembly starts the application.

### Key Entities

- **IClassificationService**: End-to-end classification contract; coordinates `IMLModelProvider` and `IEmailArchiveService`; returns typed `ClassificationResult` records.
- **IApplicationOrchestrator**: Workflow driver; owns the application mode state machine; emits `ApplicationEvent` records.
- **ApplicationEvent**: Discriminated union (or base class) for all events: `BatchProgressEvent`, `ProviderStatusChangedEvent`, `ModeSelectionRequestedEvent`, `ApplicationWorkflowCompletedEvent`.
- **ClassificationResult**: Immutable record containing `EmailId`, `PredictedAction`, `Confidence`, `ReasoningSource` (ML or RuleBased).

## Success Criteria *(mandatory)*

### Measurable Outcomes

- **SC-001**: Zero Avalonia or console-rendering references exist in `IClassificationService` and `IApplicationOrchestrator` implementations (verified by assembly dependency scan).
- **SC-002**: `dotnet build` succeeds in a Linux Docker container without a display server, with zero errors and zero warnings related to Avalonia or graphics context.
- **SC-003**: Unit tests for `IClassificationService` and `IApplicationOrchestrator` run without any real provider, real database, or real UI, reaching 90% line coverage for the new service layer.
- **SC-004**: The full triage workflow (fetch → classify 10 emails → label → exit) completes end-to-end from the console in under 5 seconds on a machine with a pre-loaded ML model.
- **SC-005**: Enabling Avalonia (`/p:EnableAvaloniaUI=true`) produces a buildable project with no compilation errors in the Avalonia-specific code.

## Assumptions

- GitHub issue #61's `IClassificationService` contract will be used as the authoritative interface definition; if it diverges from `IEmailTriageService`, the two are reconciled rather than duplicated.
- The existing `IEmailTriageService` satisfies the triage sub-domain of `IClassificationService`; this feature may rename, extend, or wrap it rather than replace it.
- "Deprecate Avalonia" means build exclusion only; no source deletion, no `[Obsolete]` attribute flood.
- The UI-agnostic event system uses standard .NET `event`/`EventArgs` pattern (not `System.Reactive`) to avoid pulling in additional NuGet dependencies.
- ViewModels that purely handle UI binding state (no domain logic) are out of scope; only ViewModels that directly call classification or email providers need refactoring.

**Acceptance Scenarios**:

1. **Given** [initial state], **When** [action], **Then** [expected outcome]
2. **Given** [initial state], **When** [action], **Then** [expected outcome]

---

### User Story 2 - [Brief Title] (Priority: P2)

[Describe this user journey in plain language]

**Why this priority**: [Explain the value and why it has this priority level]

**Independent Test**: [Describe how this can be tested independently]

**Acceptance Scenarios**:

1. **Given** [initial state], **When** [action], **Then** [expected outcome]

---

### User Story 3 - [Brief Title] (Priority: P3)

[Describe this user journey in plain language]

**Why this priority**: [Explain the value and why it has this priority level]

**Independent Test**: [Describe how this can be tested independently]

**Acceptance Scenarios**:

1. **Given** [initial state], **When** [action], **Then** [expected outcome]

---

[Add more user stories as needed, each with an assigned priority]

### Edge Cases

<!--
  ACTION REQUIRED: The content in this section represents placeholders.
  Fill them out with the right edge cases.
-->

- What happens when [boundary condition]?
- How does system handle [error scenario]?

## Requirements *(mandatory)*

<!--
  ACTION REQUIRED: The content in this section represents placeholders.
  Fill them out with the right functional requirements.
-->

### Functional Requirements

- **FR-001**: System MUST [specific capability, e.g., "allow users to create accounts"]
- **FR-002**: System MUST [specific capability, e.g., "validate email addresses"]  
- **FR-003**: Users MUST be able to [key interaction, e.g., "reset their password"]
- **FR-004**: System MUST [data requirement, e.g., "persist user preferences"]
- **FR-005**: System MUST [behavior, e.g., "log all security events"]

*Example of marking unclear requirements:*

- **FR-006**: System MUST authenticate users via [NEEDS CLARIFICATION: auth method not specified - email/password, SSO, OAuth?]
- **FR-007**: System MUST retain user data for [NEEDS CLARIFICATION: retention period not specified]

### Key Entities *(include if feature involves data)*

- **[Entity 1]**: [What it represents, key attributes without implementation]
- **[Entity 2]**: [What it represents, relationships to other entities]

## Success Criteria *(mandatory)*

<!--
  ACTION REQUIRED: Define measurable success criteria.
  These must be technology-agnostic and measurable.
-->

### Measurable Outcomes

- **SC-001**: [Measurable metric, e.g., "Users can complete account creation in under 2 minutes"]
- **SC-002**: [Measurable metric, e.g., "System handles 1000 concurrent users without degradation"]
- **SC-003**: [User satisfaction metric, e.g., "90% of users successfully complete primary task on first attempt"]
- **SC-004**: [Business metric, e.g., "Reduce support tickets related to [X] by 50%"]
