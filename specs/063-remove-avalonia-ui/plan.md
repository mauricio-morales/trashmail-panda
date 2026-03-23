# Implementation Plan: Deprecate & Remove Avalonia UI Code

**Branch**: `063-remove-avalonia-ui` | **Date**: 2026-03-23 | **Spec**: [spec.md](spec.md)  
**Input**: Feature specification from `/specs/063-remove-avalonia-ui/spec.md`

## Summary

Remove all Avalonia UI framework code, CommunityToolkit.Mvvm patterns, and desktop UI
infrastructure from the TrashMail Panda codebase. The console-first architecture (Spectre.Console
TUI, IApplicationOrchestrator, ConsoleStartupOrchestrator) is already in place; this cleanup
permanently deletes the conditionally-excluded UI layer files, strips the opt-in
`EnableAvaloniaUI` build flag, and ensures the default build produces a clean, smaller console
binary with zero legacy desktop UI references.

## Technical Context

**Language/Version**: C# 12 / .NET 9.0  
**Primary Dependencies**: Spectre.Console 0.48, Microsoft.Extensions.Hosting 9.0,  
  Microsoft.Extensions.DependencyInjection 9.0, Serilog, Google.Apis.Gmail.v1, ML.NET 4.0  
**Storage**: SQLite + SQLCipher (encrypted), ADO.NET via `SqliteConnection`, EF Core optional  
**Testing**: xUnit 2.9, Moq 4.20, coverlet — `dotnet test --configuration Release`  
**Target Platform**: macOS / Linux / Windows — console executable (`net9.0`, `OutputType=Exe`)  
**Project Type**: Console CLI application  
**Performance Goals**: App startup to first interactive prompt < 500 ms; published binary ≥ 50%
  smaller than prior desktop build (SC-003, SC-004)  
**Constraints**: Zero Avalonia package references; zero CommunityToolkit.Mvvm references; zero
  `grep` matches for "Avalonia" or "ObservableProperty|RelayCommand" in `src/`; zero build
  warnings introduced by this cleanup  
**Scale/Scope**: ~19 files deleted, ~5 files modified, ~3 test files deleted across one project

## Constitution Check

*GATE: Must pass before Phase 0 research. Re-check after Phase 1 design.*

| # | Principle | Status | Notes |
|---|-----------|--------|-------|
| I | Provider-Agnostic Architecture | ✅ PASS | No provider interfaces change. Gmail, LLM, Storage, ML providers all preserved. |
| II | Result Pattern (NON-NEGOTIABLE) | ✅ PASS | No provider methods modified; deletions only. |
| III | Security First (NON-NEGOTIABLE) | ✅ PASS | No token storage, encryption, or audit logging changes. |
| V | One Public Type Per File | ✅ PASS | No new source files. |
| VI | Strict Null Safety | ✅ PASS | Nullable=enable remains on all projects. |
| VII | Test Coverage & Quality Gates | ✅ PASS | ViewModel/Converter tests deleted alongside their subjects (not a regression). All other tests preserved. |

**Gate decision**: PASS — All active principles are satisfied. Principle IV (MVVM/CommunityToolkit.Mvvm) is being removed from the codebase entirely; it is not applicable to this feature.

## Project Structure

### Documentation (this feature)

```text
specs/063-remove-avalonia-ui/
├── plan.md              # This file (/speckit.plan command output)
├── research.md          # Phase 0 output (/speckit.plan command)
├── data-model.md        # Phase 1 output (/speckit.plan command)
├── quickstart.md        # Phase 1 output (/speckit.plan command)
└── tasks.md             # Phase 2 output (/speckit.tasks command - NOT created by /speckit.plan)
```

*(No contracts/ directory — this feature has no external public API surface.)*

### Source Code Changes (repository root)

```text
src/TrashMailPanda/TrashMailPanda/
├── [DELETE] Views/                        # All .axaml + .axaml.cs view files
├── [DELETE] ViewModels/                   # All 7 ViewModel files
├── [DELETE] Styles/                       # All Avalonia style resource files
├── [DELETE] Theming/                      # ProfessionalColors.cs (uses Avalonia.Media)
├── [DELETE] Converters/                   # StatusConverters.cs (uses Avalonia.Data)
├── [DELETE] App.axaml
├── [DELETE] App.axaml.cs
├── [DELETE] ViewLocator.cs
├── [DELETE] .avalonia-build-tasks/        # Avalonia build task artifacts
├── [DELETE] app.manifest                  # Windows WinExe manifest (console only)
├── [DELETE] Services/StartupHostedService.cs   # Legacy; AddStartupOrchestration() never called
├── [MODIFY] TrashMailPanda.csproj              # Strip EnableAvaloniaUI conditionals + packages
├── [MODIFY] Services/ServiceCollectionExtensions.cs  # Remove #if AVALONIA_UI blocks
├── [MODIFY] Models/Console/OperationalMode.cs        # Remove UIMode enum value
└── [MODIFY] Services/ApplicationOrchestrator.cs      # Remove UIMode case branch

src/Tests/TrashMailPanda.Tests/
├── [DELETE] ViewModels/ProviderStatusCardViewModelTests.cs
├── [DELETE] ViewModels/ProviderStatusDashboardViewModelTests.cs
├── [DELETE] Converters/StatusConvertersTests.cs
└── [MODIFY] TrashMailPanda.Tests.csproj    # Remove Avalonia package + conditional excludes

# Services kept unchanged (no Avalonia dependencies):
# StartupOrchestrator.cs       — used by ApplicationService via IStartupOrchestrator
# IStartupOrchestrator.cs      — interface still consumed
# All Services/Console/        — already console-only
# All Providers/               — untouched
# All Shared/                  — untouched
```

**Structure Decision**: Single-project cleanup. The existing console architecture under
`Services/Console/` is complete and production-ready. Only the physically-present-but-excluded
UI layer is removed. The `StartupOrchestrator` is retained because it has no Avalonia
dependencies and is actively used by `ApplicationService`; the spec note about "desktop-specific
StartupOrchestrator" refers to the now-deleted `StartupHostedService` pattern.

## Complexity Tracking

> No constitution violations. Nothing to justify.
