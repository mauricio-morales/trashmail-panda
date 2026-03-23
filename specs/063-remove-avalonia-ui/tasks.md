# Tasks: Deprecate & Remove Avalonia UI Code

**Input**: Design documents from `/specs/063-remove-avalonia-ui/`
**Branch**: `063-remove-avalonia-ui`
**Prerequisites**: Issue #62 (Spectre.Console TUI) merged; console equivalents for all UI features verified working

**Format**: `- [ ] [TaskID] [P?] [Story?] Description with file path`
- **[P]**: Parallelizable (different files, no mutual dependencies)
- **[Story]**: User story label — US1, US2, US3, US4

---

## Phase 1: Setup

**Purpose**: Record baseline metrics before any changes so post-cleanup improvements can be objectively measured.

- [X] T0- [ ] T001 Record baseline before changes: run `dotnet test --configuration Release` and note passing test count; run `dotnet publish src/TrashMailPanda/TrashMailPanda/TrashMailPanda.csproj -c Release -r osx-x64 --self-contained -o /tmp/baseline-build && du -sh /tmp/baseline-build/` and store the binary size; run `time dotnet run --project src/TrashMailPanda/TrashMailPanda/TrashMailPanda.csproj` and record startup wall time

---

## Phase 2: Foundational – Delete Physical UI Files

**Purpose**: Remove all committed Avalonia source files from the repository. The existing `<Compile Remove="..."/>` guards in `.csproj` keep the build green throughout this phase.

**⚠️ CRITICAL**: All deletions in this phase are safe to perform because `EnableAvaloniaUI=false` (the default) already excludes these files from compilation. Do NOT modify `.csproj` files yet.

- [X] T0- [ ] T002 [P] Delete the entire `src/TrashMailPanda/TrashMailPanda/Views/` directory (MainWindow, EmailDashboard, ProviderStatusDashboard, GmailSetupDialog, OpenAISetupDialog, WelcomeWizardWindow, and Controls/ProviderStatusCard — all `.axaml` and `.axaml.cs` files)
- [X] T0- [ ] T003 [P] Delete the entire `src/TrashMailPanda/TrashMailPanda/ViewModels/` directory (ViewModelBase.cs, MainWindowViewModel.cs, EmailDashboardViewModel.cs, ProviderStatusDashboardViewModel.cs, ProviderStatusCardViewModel.cs, GmailSetupViewModel.cs, OpenAISetupViewModel.cs, WelcomeWizardViewModel.cs)
- [X] T0- [ ] T004 [P] Delete the entire `src/TrashMailPanda/TrashMailPanda/Styles/` directory (all Avalonia XAML resource dictionaries)
- [X] T0- [ ] T005 [P] Delete the entire `src/TrashMailPanda/TrashMailPanda/Theming/` directory (ProfessionalColors.cs imports `Avalonia.Media.Color`)
- [X] T0- [ ] T006 [P] Delete the entire `src/TrashMailPanda/TrashMailPanda/Converters/` directory (StatusConverters.cs uses Avalonia `IValueConverter`)
- [X] T0- [ ] T007 [P] Delete `src/TrashMailPanda/TrashMailPanda/App.axaml`, `src/TrashMailPanda/TrashMailPanda/App.axaml.cs`, and `src/TrashMailPanda/TrashMailPanda/ViewLocator.cs`
- [X] T0- [ ] T008 [P] Delete `src/TrashMailPanda/TrashMailPanda/.avalonia-build-tasks/` directory and `src/TrashMailPanda/TrashMailPanda/app.manifest` (Windows WinExe manifest — irrelevant for `OutputType=Exe`)
- [X] T0- [ ] T009 [P] Delete `src/TrashMailPanda/TrashMailPanda/Services/StartupHostedService.cs` (dead code — `AddStartupOrchestration()` is never called; was the hosted service for the Avalonia startup path)
- [X] T0- [ ] T010 [P] Delete `src/Tests/TrashMailPanda.Tests/ViewModels/ProviderStatusCardViewModelTests.cs`, `src/Tests/TrashMailPanda.Tests/ViewModels/ProviderStatusDashboardViewModelTests.cs`, and `src/Tests/TrashMailPanda.Tests/Converters/StatusConvertersTests.cs` (tests exclusively cover deleted code — per spec Assumption, this is a non-regression deletion)
- [X] T0- [ ] T011 Run `dotnet build --configuration Release` and confirm zero errors — the `<Compile Remove>` guards still in `.csproj` must keep the build green before csproj editing begins

**Checkpoint**: All physical UI files deleted from disk. Build still passes via existing compile guards. Commit: `chore: delete physical Avalonia UI source files and ViewModel tests`

---

## Phase 3: User Story 1 – Clean Console Application Builds Successfully (Priority: P1) 🎯 MVP

**Goal**: Strip all `EnableAvaloniaUI` conditional scaffolding from project files so the default build is unconditionally clean with no desktop UI framework references.

**Independent Test**: `dotnet build --configuration Release` completes with zero errors and zero new warnings after both `.csproj` files are modified. `dotnet run --project src/TrashMailPanda` launches the Spectre.Console TUI without a desktop window.

- [X] T0- [ ] T012 [US1] Strip all `EnableAvaloniaUI` conditional blocks from `src/TrashMailPanda/TrashMailPanda/TrashMailPanda.csproj`: (1) remove `EnableAvaloniaUI` default property declaration, (2) delete the `PropertyGroup Condition="'$(EnableAvaloniaUI)' == 'true'"` WinExe/BuiltInComInteropSupport/ApplicationManifest/AVALONIA_UI block, (3) simplify the `PropertyGroup Condition="'$(EnableAvaloniaUI)' != 'true'"` into an unconditional `<OutputType>Exe</OutputType>` block, (4) delete the `ItemGroup Condition="'$(EnableAvaloniaUI)' != 'true'"` `<Compile Remove>` block (files no longer exist), (5) delete the `ItemGroup Condition="'$(EnableAvaloniaUI)' == 'true'"` AvaloniaResource block, (6) delete the `ItemGroup Condition="'$(EnableAvaloniaUI)' == 'true'"` Avalonia NuGet packages block (Avalonia, CommunityToolkit.Mvvm, etc.)
- [X] T0- [ ] T013 [P] [US1] Strip Avalonia references from `src/Tests/TrashMailPanda.Tests/TrashMailPanda.Tests.csproj`: (1) delete the `EnableAvaloniaUI` property declaration, (2) delete the `ItemGroup Condition` exclusion block for `Converters\**` and `ViewModels\**`, (3) delete the unconditional `<PackageReference Include="Avalonia" Version="11.3.4" />`
- [X] T0- [ ] T014 [US1] Run `dotnet build --configuration Release` and confirm zero errors with the unconditional clean project files; run `dotnet run --project src/TrashMailPanda/TrashMailPanda/TrashMailPanda.csproj` and confirm the Spectre.Console TUI appears with no desktop window

**Checkpoint**: US1 complete — application builds and runs as a clean console-only binary. Commit: `chore: strip EnableAvaloniaUI conditional scaffolding from .csproj files`

---

## Phase 4: User Story 2 – Codebase Contains Zero Legacy UI References (Priority: P2)

**Goal**: Eliminate all remaining Avalonia or MVVM pattern references from service code and models so that `grep -r "Avalonia" src/` and `grep -r "ObservableProperty\|RelayCommand" src/` return zero results.

**Independent Test**: `grep -r "Avalonia" src/ --include="*.cs" --include="*.csproj"` returns zero matches. `grep -r "ObservableProperty\|RelayCommand\|ObservableObject\|CommunityToolkit" src/ --include="*.cs"` returns zero matches.

- [X] T0- [ ] T015 [P] [US2] Remove all `#if AVALONIA_UI` blocks from `src/TrashMailPanda/TrashMailPanda/Services/ServiceCollectionExtensions.cs`: (1) delete the `#if AVALONIA_UI … using TrashMailPanda.ViewModels; … #endif` block at the top of the file, (2) delete the `#if AVALONIA_UI … services.AddViewModels(); … #endif` call-site inside `AddTrashMailPandaServices`, (3) delete the entire `#if AVALONIA_UI … private static IServiceCollection AddViewModels(…) { … } … #endif` method body
- [X] T0- [ ] T016 [P] [US2] Remove the `UIMode` enum value and its XML-doc comment from `src/TrashMailPanda/TrashMailPanda/Models/Console/OperationalMode.cs`
- [X] T0- [ ] T017 [P] [US2] Remove the `case OperationalMode.UIMode:` branch and its body from `src/TrashMailPanda/TrashMailPanda/Services/ApplicationOrchestrator.cs` (this branch emits `[dim]This mode will launch the Avalonia desktop UI.[/]` — requires T016 to compile)
- [X] T0- [ ] T018 [P] [US2] Remove the `UIMode` menu entry tuple from `GetAvailableModesAsync()` in `src/TrashMailPanda/TrashMailPanda/Services/Console/ModeSelectionMenu.cs` (the 4-line `(OperationalMode.UIMode, "🖥️ …", gmailHealthy)` entry — requires T016 to compile)
- [X] T0- [ ] T019 [US2] Run `dotnet build --configuration Release` and confirm zero errors; run `grep -r "Avalonia" src/ --include="*.cs" --include="*.csproj"` and `grep -r "ObservableProperty\|RelayCommand\|ObservableObject\|CommunityToolkit" src/ --include="*.cs"` and confirm both return zero matches (SC-001, SC-002)

**Checkpoint**: US2 complete — codebase contains zero legacy UI references. Commit: `chore: remove #if AVALONIA_UI service blocks and UIMode enum value`

---

## Phase 5: User Story 4 – All Existing Tests Continue to Pass (Priority: P2)

**Goal**: Run the full automated test suite and verify zero regressions from any removal in Phases 2–4.

**Independent Test**: `dotnet test --configuration Release` exits with zero failing tests. Coverage meets or exceeds pre-cleanup thresholds (90% global, 95% provider, 100% security).

- [X] T0- [ ] T020 [US4] Run `dotnet test --configuration Release` and confirm all previously passing tests still pass (SC-006); any failure indicates shared logic was incorrectly removed alongside UI code and must be restored
- [X] T0- [ ] T021 [US4] Run `dotnet test --collect:"XPlat Code Coverage" --configuration Release` and verify coverage thresholds: ≥ 90% global, ≥ 95% provider implementations, 100% security components (R-008)

**Checkpoint**: US4 complete — test suite passes with no regressions and coverage maintained. Commit: `chore: verify test suite after Avalonia removal`

---

## Phase 6: User Story 3 – Application Starts Faster with Smaller Footprint (Priority: P3)

**Goal**: Confirm that removing Avalonia packages produces a materially smaller self-contained binary and measurably faster startup, validating the architectural decision.

**Independent Test**: Published `osx-x64` self-contained binary is ≥ 50% smaller than the pre-cleanup baseline recorded in T001. Startup time to first Spectre.Console prompt is < 500 ms (SC-003, SC-004).

- [X] T0- [ ] T022 [P] [US3] Run `dotnet publish src/TrashMailPanda/TrashMailPanda/TrashMailPanda.csproj -c Release -r osx-x64 --self-contained -o /tmp/trashmail-console && du -sh /tmp/trashmail-console/`; compare to baseline from T001 and confirm ≥ 50% size reduction (SC-004)
- [X] T0- [ ] T023 [P] [US3] Run `time dotnet run --project src/TrashMailPanda/TrashMailPanda/TrashMailPanda.csproj` and confirm wall-clock time to first Spectre.Console interactive prompt is < 500 ms (SC-003)

**Checkpoint**: US3 complete — binary size and startup time targets confirmed.

---

## Phase 7: Polish & Final Verification

**Purpose**: End-to-end clean-state audit confirming all success criteria are met before merging.

- [X] T0- [ ] T024 [P] Run `grep -r "Avalonia" src/ --include="*.cs" --include="*.csproj" --include="*.axaml"` and confirm zero matches (SC-001); if any match is found, trace to the owning file and remove the residual reference
- [X] T0- [ ] T025 [P] Run `grep -r "ObservableProperty\|RelayCommand\|ObservableObject\|CommunityToolkit" src/ --include="*.cs"` and confirm zero matches (SC-002); if any match is found, trace to the owning ViewModel/service and remove
- [X] T0- [ ] T026 Run `dotnet format --verify-no-changes` and confirm zero formatting changes required (SC-005); run `dotnet format` first if needed, then re-verify
- [X] T0- [ ] T027 Smoke test: run `dotnet run --project src/TrashMailPanda/TrashMailPanda/TrashMailPanda.csproj`, verify the Spectre.Console TUI appears, provider health status is displayed with colored output, and all provider capabilities (Gmail OAuth, OpenAI config, storage operations) are accessible from the console menu (SC-007)

**Checkpoint**: All success criteria verified. Final commit: `chore: complete Avalonia UI removal — all SC criteria met`

---

## Dependencies

```
T001 (baseline)
  └─► T002–T010 [parallel deletions]
        └─► T011 (build check)
              └─► T012, T013 [parallel csproj edits]
                    └─► T014 (build + smoke check)  ← US1 ✅
                          ├─► T015, T016 [parallel service edits]
                          │     └─► T016 ──► T017, T018 [parallel, depend on T016]
                          │               └─► T019 (build + grep check)  ← US2 ✅
                          ├─► T020 ──► T021 (test suite)  ← US4 ✅
                          └─► T022, T023 [parallel, after csproj clean]  ← US3 ✅
T024–T027 [final audit, all after T019 + T020]
```

### Parallel Execution Within Each Phase

**Phase 2** (deletions — all parallel after T001):
```
T002 │ T003 │ T004 │ T005 │ T006 │ T007 │ T008 │ T009 │ T010
                     └─── all merge ──► T011
```

**Phase 3** (csproj edits — T012 and T013 parallel after T011):
```
T012 (main csproj) │ T013 (tests csproj)
        └─── both merge ──► T014
```

**Phase 4** (service edits — T015 and T016 parallel after T014; T017 and T018 parallel after T016):
```
T015 (ServiceCollectionExtensions) │ T016 (OperationalMode)
                                         └─► T017 (ApplicationOrchestrator) │ T018 (ModeSelectionMenu)
                                                both merge ──► T019
```

**Phase 7** (all parallel after T019 + T020):
```
T024 │ T025 │ T026 │ T027
```

---

## Implementation Strategy

**MVP Scope (US1 only)**: Complete Phases 1–3 (T001–T014) for a clean, verified console build with no Avalonia compile-time dependencies. This alone delivers a smaller, faster application.

**Full Scope**: Complete all phases to satisfy all four user stories, pass all tests, and verify performance claims.

**Suggested commit sequence**:
1. After T011: `chore: delete physical Avalonia UI source files and ViewModel tests`
2. After T014: `chore: strip EnableAvaloniaUI conditional scaffolding from .csproj files`
3. After T019: `chore: remove #if AVALONIA_UI service blocks and UIMode enum value`
4. After T021: `chore: verify test suite green after Avalonia removal`
5. After T027: `chore: complete Avalonia UI removal — all SC criteria met (closes #68)`

---

## Format Validation

| Requirement | Status |
|-------------|--------|
| All tasks have `- [ ]` checkbox | ✅ |
| All tasks have T### ID | ✅ |
| Parallelizable tasks marked `[P]` | ✅ |
| User story phases have `[US#]` labels | ✅ |
| Setup/Foundational phases have no story label | ✅ |
| All tasks have file paths or explicit verification commands | ✅ |

**Total tasks**: 27 (T001–T027)  
**Tasks per user story**: US1 = 3, US2 = 5, US3 = 2, US4 = 2  
**Parallel opportunities**: 8 (T002–T010 in Phase 2; T012+T013; T015+T016; T017+T018; T022+T023; T024–T027)  
**MVP scope**: T001–T014 (14 tasks)
