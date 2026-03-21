# Implementation Plan: Console TUI with Spectre.Console

**Branch**: `060-console-tui-spectre` | **Date**: 2026-03-19 | **Spec**: [spec.md](./spec.md)
**Input**: Feature specification from `/specs/060-console-tui-spectre/spec.md`

## Summary

Complete the TrashMail Panda console TUI by implementing the three remaining mode stubs (Email
Triage, Bulk Operations, Provider Settings), a centralized `ConsoleColors` constant class, and a
context-aware help system. A significant portion of the console infrastructure is already
implemented (startup orchestration, training mode, mode selection menu, configuration wizard). This
feature fills the remaining gaps identified in the spec.

**Technical approach**: Add two distinct service layers:

1. **Business logic** (`Services/`): `EmailTriageService` and `BulkOperationService` — UI-agnostic, no
   Spectre.Console dependency. These orchestrate mode detection via `IMLModelProvider.GetActiveModelVersionAsync`,
   source the untriaged email queue (`training_label IS NULL`) from `IEmailArchiveService`, and dual-write
   triage decisions — (a) Gmail action via `IEmailProvider`, then (b) `IEmailArchiveService.SetTrainingLabelAsync`
   on success.

2. **TUI presenters** (`Services/Console/`): `EmailTriageConsoleService`, `BulkOperationConsoleService`,
   `ProviderSettingsConsoleService`, and `ConsoleHelpPanel` — thin wrappers that render via `IAnsiConsole`
   and delegate all business logic to `IEmailTriageService` / `IBulkOperationService`. Any future UI
   (web, Avalonia, MCP) can consume the business logic interfaces without touching Spectre.Console.

Also add `TrainingLabel string?` to the existing `EmailFeatureVector` entity (lightweight column migration)
for cross-session stateless resume — untriaged emails are identified by `training_label IS NULL`.

## Technical Context

**Language/Version**: .NET 9.0 / C# 12 (nullable reference types enabled)  
**Primary Dependencies**: Spectre.Console 0.48.0, Microsoft.Extensions.Hosting/DI/Logging v9.0.8, CommunityToolkit.Mvvm 8.2.1, TrashMailPanda.Providers.ML, TrashMailPanda.Providers.Email, TrashMailPanda.Providers.Storage  
**Storage**: SQLite via EF Core (`TrashMailPandaDbContext`); `email_features` column migration (`training_label TEXT NULL`); `IEmailArchiveService` for training signals + queue (add `SetTrainingLabelAsync`, `CountLabeledAsync`, `GetUntriagedAsync`)  
**Testing**: xUnit + Moq; `[Trait("Category", "Unit")]`; IAnsiConsole injected for console output assertions  
**Target Platform**: Cross-platform console (macOS, Windows, Linux)  
**Project Type**: Console application (TUI) — console-first architecture  
**Performance Goals**: Triage decision ready in <100ms UI rendering; no blocking waits between email cards; training progress updates every ≤2s  
**Constraints**: No per-email API round-trips during rendering (batch ListAsync); `IAnsiConsole` injection required for testability; zero raw Spectre.Console markup strings outside `ConsoleColors.cs`  
**Scale/Scope**: Single-user local app; triage sessions over batches of ~50 emails per page; up to thousands of emails total

## Constitution Check

*GATE: Must pass before Phase 0 research. Re-check after Phase 1 design.*

| Principle | Status | Notes |
|-----------|--------|-------|
| **I. Provider-Agnostic Architecture** | ✅ PASS | Business logic services (`IEmailTriageService`, `IBulkOperationService`) depend on `IEmailProvider`, `IMLModelProvider`, `IEmailArchiveService`. TUI presenters (`IEmailTriageConsoleService`, `IBulkOperationConsoleService`) depend only on the business logic services + `IAnsiConsole` — no concrete provider references in either layer. |
| **II. Result Pattern (NON-NEGOTIABLE)** | ✅ PASS | All `IEmailTriageConsoleService`, `IBulkOperationConsoleService`, `IProviderSettingsConsoleService`, `IEmailArchiveService` additions return `Result<T>` |
| **III. Security First (NON-NEGOTIABLE)** | ✅ PASS | No token or email content logging; all DB operations parameterized via EF Core; OAuth re-auth delegates to existing `ConfigurationWizard` + `IGoogleOAuthHandler` |
| **IV. MVVM with CommunityToolkit.Mvvm** | ⚠️ SCOPED | Principle IV applies to the Avalonia UI layer. Console TUI architecture deliberately does not use MVVM (architecture shift documented in `docs/architecture/ARCHITECTURE_SHIFT_TO_LOCAL_ML.md`). No violation — console services use DI + Result pattern, not MVVM. |
| **V. One Public Type Per File** | ✅ PASS | All new files: one public class/interface/record each |
| **VI. Strict Null Safety** | ✅ PASS | All new entities, models, and service types carry explicit nullability annotations; nullable parameters clearly marked `?` |
| **VII. Test Coverage & Quality Gates** | ✅ PASS | Unit tests required for all new services; `EmailTriageConsoleService` is P1 → 95% coverage target; 90% global minimum maintained |

**Post-design re-check** (after Phase 1): All gates still pass. `email_features.training_label`
migration uses EF Core parameterized queries (Principle III). `IEmailArchiveService.SetTrainingLabelAsync`
follows the Result pattern (Principle II). `ConsoleColors` static class with const strings is a
single public type per file (Principle V). Dual-write order (Gmail action first, label storage
second) ensures no false training signals on network failure (Principle III).

## Project Structure

### Documentation (this feature)

```text
specs/060-console-tui-spectre/
├── plan.md              # This file
├── research.md          # Phase 0 — all decisions resolved ✅
├── data-model.md        # Phase 1 — entities, relationships, migration ✅
├── quickstart.md        # Phase 1 — implementation guide and test patterns ✅
├── contracts/
│   ├── key-bindings.md  # User-facing keyboard interaction contract ✅
│   └── service-interfaces.md  # C# service interface contracts ✅
└── tasks.md             # Phase 2 output (generated by /speckit.tasks — NOT created here)
```

### Source Code (repository root)

```text
src/TrashMailPanda/TrashMailPanda/
├── Services/
│   ├── EmailTriageService.cs               # NEW — business logic (mode detection, queue, dual-write) (P1)
│   ├── IEmailTriageService.cs              # NEW — UI-agnostic interface for triage
│   ├── BulkOperationService.cs             # NEW — business logic (bulk Gmail + label storage) (P2)
│   └── IBulkOperationService.cs            # NEW — UI-agnostic interface for bulk ops
├── Services/Console/
│   ├── ConsoleColors.cs                    # NEW — centralized color markup constants (FR-022)
│   ├── EmailTriageConsoleService.cs        # NEW — thin TUI presenter over IEmailTriageService (P1, FR-001..FR-011)
│   ├── BulkOperationConsoleService.cs      # NEW — thin TUI presenter over IBulkOperationService (P2, FR-025)
│   ├── ProviderSettingsConsoleService.cs   # NEW — Provider Settings mode (P2, FR-016..FR-018)
│   ├── ConsoleHelpPanel.cs                 # NEW — Help system (P3, FR-023)
│   ├── ConsoleStartupOrchestrator.cs       # EXISTING
│   ├── ConsoleStatusDisplay.cs             # EXISTING
│   ├── ConfigurationWizard.cs              # EXISTING
│   └── ModeSelectionMenu.cs                # EXISTING
├── Models/Console/
│   ├── EmailTriageSession.cs               # NEW — in-memory session state
│   ├── TriageDecision.cs                   # NEW — per-email decision record
│   ├── TriageSessionSummary.cs             # NEW — end-of-session statistics
│   ├── KeyBinding.cs                       # NEW — key+description for help system
│   ├── HelpContext.cs                      # NEW — mode-specific help data
│   ├── BulkOperationCriteria.cs            # NEW — bulk operation filter parameters
│   ├── TriageMode.cs                       # NEW — ColdStart | AiAssisted enum
│   └── [existing: OperationalMode.cs, ConsoleDisplayOptions.cs, etc.]
└── Program.cs                              # MODIFY — wire new services, replace stubs

src/Providers/Storage/TrashMailPanda.Providers.Storage/
├── Migrations/[N]_AddTrainingLabelToEmailFeatures.cs # NEW — ALTER email_features ADD training_label
├── Models/EmailFeatureVector.cs                       # MODIFY — add TrainingLabel string? property
└── IEmailArchiveService.cs                            # MODIFY — add SetTrainingLabelAsync, CountLabeledAsync, GetUntriagedAsync

src/Tests/TrashMailPanda.Tests/Unit/Services/
├── EmailTriageServiceTests.cs              # NEW — unit tests for business logic (95% target)
├── EmailTriageConsoleServiceTests.cs       # NEW — unit tests for TUI presenter (mocks IEmailTriageService)
├── BulkOperationServiceTests.cs            # NEW — unit tests for bulk operation logic
├── BulkOperationConsoleServiceTests.cs     # NEW — unit tests for bulk ops TUI presenter
├── ProviderSettingsConsoleServiceTests.cs  # NEW — unit tests for settings
└── ConsoleHelpPanelTests.cs               # NEW — unit tests for help panel
```

**Structure Decision**: Two-layer pattern — business logic services in `Services/`, TUI presenters
in `Services/Console/`. TUI presenters mock only `IEmailTriageService` / `IBulkOperationService`
in tests (not individual providers), keeping presenter unit tests simple and fast. Business logic
service unit tests mock `IEmailProvider`, `IMLModelProvider`, `IEmailArchiveService` directly.
Storage additions go to `src/Providers/Storage/` (matching provider pattern). Tests in existing
`src/Tests/TrashMailPanda.Tests/Unit/Services/` directory.

## Complexity Tracking

> **Complexity justification for Principle IV (MVVM) scope note**:
> 
> The console TUI architecture does not use MVVM (`ObservableObject`, `RelayCommand`, `ObservableProperty`).
> This is an intentional deviation justified in `docs/architecture/ARCHITECTURE_SHIFT_TO_LOCAL_ML.md`:
> the architecture shift explicitly moves from Avalonia desktop MVVM to a console-first model.
> 
> Simpler alternative rejected: adding MVVM to console services would require Avalonia as a
> runtime dependency in the console code path — defeating the "lightweight, scriptable interface"
> goal and adding hundreds of KB to the binary. The console TUI follows a direct service +
> DI + Result pattern, which is idiomatic for console/CLI applications.
> 
> The MVVM principle in the constitution is scoped to the UI layer. The Avalonia UI stub
> will remain and continue to use MVVM if/when the Avalonia mode is re-enabled.

| Violation | Why Needed | Simpler Alternative Rejected Because |
|-----------|------------|-------------------------------------|
| [e.g., 4th project] | [current need] | [why 3 projects insufficient] |
| [e.g., Repository pattern] | [specific problem] | [why direct DB access insufficient] |
