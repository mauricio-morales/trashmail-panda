# Implementation Plan: Console Startup Orchestration & Health Checks

**Branch**: `057-console-startup-orchestration` | **Date**: March 16, 2026 | **Spec**: [spec.md](spec.md)
**Input**: Feature specification from `/specs/057-console-startup-orchestration/spec.md`

**Note**: This template is filled in by the `/speckit.plan` command. See `.specify/templates/plan-template.md` for the execution workflow.

## Summary

Implement a console-based sequential startup orchestration system that initializes providers in dependency order (Storage → Gmail), displays real-time status with color-coded indicators using Spectre.Console, performs health checks immediately after each provider initialization, and provides an interactive configuration wizard for first-time setup. The system replaces all Avalonia UI startup code with a single-threaded console application that displays mode selection menu after successful initialization.

## Technical Context

**Language/Version**: C# 12 / .NET 9.0  
**Primary Dependencies**: Avalonia UI 11 (to be replaced with console), CommunityToolkit.Mvvm (UI only), Microsoft.Extensions.Hosting/DI/Logging, Spectre.Console (NEW - for console formatting), Google.Apis.Gmail.v1, Polly  
**Storage**: SQLite with SQLCipher encryption via Microsoft.Data.Sqlite  
**Testing**: xUnit + Moq + coverlet (90% coverage target)  
**Target Platform**: Cross-platform desktop console (Windows/macOS/Linux)  
**Project Type**: Desktop application (console mode for startup, with potential UI mode selection)  
**Performance Goals**: <10 seconds total startup time for 2 sequential provider initializations, <30 seconds per provider timeout  
**Constraints**: Single-threaded sequential initialization (no parallelism), console ANSI color support required, OAuth flows via system browser  
**Scale/Scope**: 2 providers (Storage, Gmail), ~4 operational modes in mode selection menu (AI features deferred to future)

## Constitution Check

*GATE: Must pass before Phase 0 research. Re-check after Phase 1 design.*

**Initial Check (Pre-Phase 0)**: ✅ PASSED

| Principle | Status | Notes |
|-----------|---------|-------|
| I. Provider-Agnostic Architecture | ✅ PASS | Reuses existing IProvider<TConfig> interface, no provider-specific dependencies |
| II. Result Pattern (NON-NEGOTIABLE) | ✅ PASS | All provider methods already return Result<T>, console orchestration will inspect IsSuccess |
| III. Security First (NON-NEGOTIABLE) | ✅ PASS | No changes to credential storage; uses existing SecureStorageManager and SecurityAuditLogger |
| IV. MVVM with CommunityToolkit.Mvvm | ⚠️ VIOLATION (JUSTIFIED) | Console application cannot use MVVM/Avalonia UI - replacing Program.cs Main() with console startup |
| V. One Public Type Per File | ✅ PASS | Will follow for all new console classes (ConsoleOrchestrator, ConfigurationWizard, etc.) |
| VI. Strict Null Safety | ✅ PASS | All new code will use nullable reference types with `<Nullable>enable</Nullable>` |
| VII. Test Coverage & Quality Gates | ✅ PASS | Target 90% coverage for new console orchestration code, xUnit tests with integration tests skipped |

**MVVM Violation Justification**: This feature replaces the Avalonia UI startup flow (App.axaml.cs, StartWithClassicDesktopLifetime) with a console-based startup for improved debuggability and headless operation support. The UI application mode can be launched after successful console initialization via mode selection. This is an architectural shift from "UI-first" to "console-first with optional UI mode", which is simpler than maintaining dual startup paths.

---

**Post-Phase 1 Re-Evaluation**: ✅ PASSED

| Principle | Design Compliance | Evidence |
|-----------|------------------|----------|
| I. Provider-Agnostic Architecture | ✅ VERIFIED | data-model.md shows no provider-specific code; ConsoleStartupOrchestrator uses IProvider<TConfig> only |
| II. Result Pattern | ✅ VERIFIED | research.md confirms all provider calls use `result.IsSuccess` inspection; no try/catch in orchestration |
| III. Security First | ✅ VERIFIED | quickstart.md shows credentials stored via SecureStorageManager (OS keychain); no plaintext logging |
| IV. MVVM with CommunityToolkit.Mvvm | ⚠️ VIOLATION (JUSTIFIED) | Violation remains justified - console architecture documented in research.md with clear rationale |
| V. One Public Type Per File | ✅ VERIFIED | data-model.md entities map to separate files; plan.md Project Structure shows 1:1 mapping |
| VI. Strict Null Safety | ✅ VERIFIED | data-model.md uses nullable annotations (string?, DateTime?); validation rules enforce non-null constraints |
| VII. Test Coverage & Quality Gates | ✅ VERIFIED | quickstart.md includes test strategy; plan.md confirms 90% coverage target with xUnit |

**Design Changes Since Initial Check**: None - all principles remain satisfied with same justified MVVM violation.

**Gate Status**: ✅ APPROVED TO PROCEED - Design satisfies all constitutional requirements

## Project Structure

### Documentation (this feature)

```text
specs/057-console-startup-orchestration/
├── plan.md              # This file (/speckit.plan command output)
├── research.md          # Phase 0 output (/speckit.plan command)
├── data-model.md        # Phase 1 output (/speckit.plan command)
├── quickstart.md        # Phase 1 output (/speckit.plan command)
├── contracts/           # Phase 1 output (/speckit.plan command)
│   └── console-commands.md  # Console command contracts and menu options
└── tasks.md             # Phase 2 output (/speckit.tasks command - NOT created by /speckit.plan)
```

### Source Code (repository root)

```text
src/TrashMailPanda/TrashMailPanda/
├── Program.cs           # MODIFIED: Replace Avalonia startup with console orchestration entry point
├── App.axaml.cs         # DEPRECATED: Avalonia UI startup code to be bypassed
├── Services/
│   ├── Console/         # NEW: Console-specific orchestration services
│   │   ├── ConsoleStartupOrchestrator.cs     # Sequential provider initialization
│   │   ├── ConsoleStatusDisplay.cs           # Spectre.Console status rendering
│   │   ├── ConfigurationWizard.cs            # First-time setup wizard
│   │   └── ModeSelectionMenu.cs              # Post-startup mode selection
│   ├── StartupOrchestrator.cs                # DEPRECATED: Avalonia-specific orchestrator
│   └── ProviderStatusService.cs              # REUSED: Existing provider status tracking
├── Models/
│   └── Console/         # NEW: Console-specific models
│       ├── ProviderInitializationState.cs    # State machine for each provider
│       └── StartupSequenceState.cs           # Overall startup progress tracking
└── TrashMailPanda.csproj                     # MODIFIED: Add Spectre.Console package reference

src/Shared/TrashMailPanda.Shared/
├── Base/
│   ├── IProvider.cs     # REUSED: Existing provider interface with InitializeAsync, HealthCheckAsync
│   └── BaseProvider.cs  # REUSED: Existing provider base class
└── Extensions/
    └── ProviderHealthChecks.cs               # REUSED: Existing health check infrastructure

tests/TrashMailPanda.Tests/
└── Console/             # NEW: Console orchestration tests
    ├── ConsoleStartupOrchestratorTests.cs
    ├── ConfigurationWizardTests.cs
    └── ModeSelectionMenuTests.cs
```

**Structure Decision**: Console services are isolated in `Services/Console/` directory to separate them from deprecated Avalonia UI services. Program.cs becomes the console application entry point, directly invoking ConsoleStartupOrchestrator instead of Avalonia's BuildAvaloniaApp(). The existing provider infrastructure (IProvider, BaseProvider, health checks) is reused without modification per Provider-Agnostic Architecture principle.

## Complexity Tracking

| Violation | Why Needed | Simpler Alternative Rejected Because |
|-----------|------------|-------------------------------------|
| MVVM Principle (Console instead of Avalonia UI) | Console startup provides deterministic, scriptable, and debuggable initialization flow essential for headless deployments and CI/CD testing | Avalonia UI startup hides provider initialization details behind visual elements, requires X11/graphics context, and cannot run in Docker containers or SSH sessions where users need visibility into startup failures |
