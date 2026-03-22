# Implementation Plan: Backend Refactoring for UI Abstraction

**Branch**: `061-backend-ui-abstraction` | **Date**: 2026-03-21 | **Spec**: [spec.md](spec.md)
**Input**: Feature specification from `/specs/061-backend-ui-abstraction/spec.md`
**GitHub Issue**: [#63](https://github.com/mauricio-morales/trashmail-panda/issues/63)

## Summary

Introduce `IClassificationService` and `IApplicationOrchestrator` as UI-agnostic service
contracts, extract the application workflow loop from `Program.cs` into a testable
orchestrator, add a typed event system for progress/status reporting, and conditionally
exclude the Avalonia project from the default build. The existing `IEmailTriageService`,
`IBulkOperationService`, console TUI services, and ML provider remain intact — this feature
wraps and coordinates them without replacing them.

## Technical Context

**Language/Version**: C# 12 / .NET 9.0 (`net9.0`)
**Primary Dependencies**: Microsoft.Extensions.Hosting/DI/Logging, Spectre.Console 0.48, CommunityToolkit.Mvvm 8.2, Avalonia 11.3 (conditionally), ML.NET, Serilog
**Storage**: SQLite with SQLCipher via EF Core + Microsoft.Data.Sqlite
**Testing**: xUnit + Moq + coverlet (via `dotnet test`)
**Target Platform**: macOS (primary dev), Windows, Linux (console-first TUI)
**Project Type**: Desktop/CLI console application with optional GUI
**Performance Goals**: Full triage workflow (fetch → classify 10 emails → label → exit) < 5s with pre-loaded model
**Constraints**: No additional NuGet dependencies (no System.Reactive); standard .NET `event`/`EventArgs` pattern
**Scale/Scope**: 7 existing projects in solution; this feature adds 0 new projects — new types go into existing projects

## Constitution Check

*GATE: Must pass before Phase 0 research. Re-check after Phase 1 design.*

| # | Principle | Status | Notes |
|---|-----------|--------|-------|
| I | Provider-Agnostic Architecture | ✅ PASS | `IClassificationService` wraps `IMLModelProvider` without direct vendor coupling. New interfaces registered via DI. |
| II | Result Pattern (NON-NEGOTIABLE) | ✅ PASS | All new service methods return `Result<T>`. No exceptions thrown from services. |
| III | Security First (NON-NEGOTIABLE) | ✅ PASS | No new credential handling. Existing token storage unchanged. No sensitive data in events. |
| IV | MVVM with CommunityToolkit | ✅ PASS | ViewModels are preserved as-is. No domain logic is added to ViewModels; logic goes to services. |
| V | One Public Type Per File | ✅ PASS | Each new interface, class, record, and enum gets its own file. |
| VI | Strict Null Safety | ✅ PASS | All new types use explicit nullable annotations. |
| VII | Test Coverage & Quality Gates | ✅ PASS | 90%+ coverage target for new service layer. Unit tests use mocks; no real providers. |

**Gate Result**: ALL PASS — proceed to Phase 0.

## Project Structure

### Documentation (this feature)

```text
specs/061-backend-ui-abstraction/
├── plan.md              # This file
├── research.md          # Phase 0 output
├── data-model.md        # Phase 1 output
├── quickstart.md        # Phase 1 output
├── contracts/           # Phase 1 output
│   ├── IClassificationService.cs
│   ├── IApplicationOrchestrator.cs
│   └── ApplicationEvent.cs
└── tasks.md             # Phase 2 output (speckit.tasks)
```

### Source Code (repository root)

```text
src/
├── Shared/TrashMailPanda.Shared/
│   └── Base/
│       └── (no changes — Result<T>, IProvider<T> already here)
│
├── TrashMailPanda/TrashMailPanda/
│   ├── Models/
│   │   └── ApplicationEvent.cs              # NEW: base record + discriminated event types
│   │   └── ClassificationResult.cs          # NEW: immutable classification output record
│   │
│   ├── Services/
│   │   ├── IClassificationService.cs        # NEW: end-to-end classification contract
│   │   ├── ClassificationService.cs         # NEW: wraps IMLModelProvider + cold-start fallback
│   │   ├── IApplicationOrchestrator.cs      # NEW: workflow driver contract
│   │   ├── ApplicationOrchestrator.cs       # NEW: extracted from Program.cs main loop
│   │   └── ServiceCollectionExtensions.cs   # MODIFIED: register new services
│   │
│   ├── Services/Console/
│   │   ├── ConsoleEventRenderer.cs          # NEW: subscribes to ApplicationEvents → Spectre output
│   │   └── (existing console services unchanged)
│   │
│   ├── Program.cs                           # MODIFIED: delegates to IApplicationOrchestrator
│   │
│   ├── ViewModels/                          # PRESERVED: no domain logic changes
│   ├── Views/                               # PRESERVED: conditionally excluded from build
│   ├── Styles/                              # PRESERVED: conditionally excluded from build
│   └── TrashMailPanda.csproj                # MODIFIED: MSBuild conditional for Avalonia
│
├── Tests/TrashMailPanda.Tests/
│   └── Unit/
│       ├── ClassificationServiceTests.cs    # NEW
│       ├── ApplicationOrchestratorTests.cs  # NEW
│       └── ConsoleEventRendererTests.cs     # NEW
```

**Structure Decision**: All new types are added to the existing `TrashMailPanda` main project
(services, models) and test project. No new .csproj files. The Avalonia build exclusion is
achieved via MSBuild `Condition` attributes in the existing `.csproj`, not a separate project.

## Complexity Tracking

> No constitution violations — no complexity justification needed.

## Constitution Re-Check (Post Phase 1)

| # | Principle | Status | Notes |
|---|-----------|--------|-------|
| I | Provider-Agnostic Architecture | ✅ PASS | All new contracts resolve dependencies via DI. No vendor coupling. |
| II | Result Pattern (NON-NEGOTIABLE) | ✅ PASS | Every contract method returns `Result<T>`. Zero exceptions from services. |
| III | Security First (NON-NEGOTIABLE) | ✅ PASS | Events carry no sensitive data. No credential changes. |
| IV | MVVM with CommunityToolkit | ✅ PASS | ViewModels preserved as-is; Avalonia conditionally excluded. |
| V | One Public Type Per File | ✅ PASS | Contracts show event hierarchy together for readability; implementation splits each type into its own file. |
| VI | Strict Null Safety | ✅ PASS | All `?` annotations explicit in contracts. |
| VII | Test Coverage & Quality Gates | ✅ PASS | Test files planned for all new services; 90% target. |

**Post-Phase-1 Gate Result**: ALL PASS.

## Existing Components Impact Analysis

### KEEP AS-IS (no changes)
- `IEmailTriageService` / `EmailTriageService` — already UI-agnostic
- `IBulkOperationService` / `BulkOperationService` — already UI-agnostic
- `IMLModelProvider` / `MLModelProvider` — proper provider with lifecycle
- All Console*Service classes — pure rendering layer
- `ModeSelectionMenu`, `ConsoleStartupOrchestrator`, `ConsoleStatusDisplay` — existing delegation targets
- `ConfigurationWizard`, `TrainingConsoleService`, `GmailTrainingScanCommand` — existing services
- All ViewModels — preserved for Avalonia re-enablement
- All Views/Styles/XAML — preserved (conditionally excluded from build)

### CREATE (new types)
- `IClassificationService` → wraps `IMLModelProvider`
- `ClassificationService` → implementation
- `IApplicationOrchestrator` → workflow driver contract
- `ApplicationOrchestrator` → extracted from Program.cs main loop
- `ClassificationResult` → immutable classification output
- `ReasoningSource` → enum (ML / RuleBased)
- `ApplicationEvent` → abstract base record
- `ModeSelectionRequestedEvent` → concrete event
- `BatchProgressEvent` → concrete event
- `ProviderStatusChangedEvent` → concrete event (models namespace, distinct from existing `ProviderStatusChangedEventArgs` in Services)
- `ApplicationWorkflowCompletedEvent` → concrete event
- `ApplicationEventArgs` → EventArgs wrapper
- `ConsoleEventRenderer` → event subscriber that renders via Spectre.Console

### MODIFY (refactoring)
- `Program.cs` → simplify to host-builder + `orchestrator.RunAsync(ct)`
- `ServiceCollectionExtensions.cs` → register `IClassificationService`, `IApplicationOrchestrator`, `ConsoleEventRenderer`
- `TrashMailPanda.csproj` → add `EnableAvaloniaUI` MSBuild conditional property + conditional item groups
