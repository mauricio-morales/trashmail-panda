# Research: Remove Avalonia UI — Phase 0 Findings

**Branch**: `063-remove-avalonia-ui` | **Date**: 2026-03-23

All unknowns from the Technical Context are resolved below. No items remain as
NEEDS CLARIFICATION.

---

## R-001: Current Avalonia Compile Guard — How Is It Structured?

**Decision**: The project already uses an `EnableAvaloniaUI` MSBuild property as an **opt-in
flag** (default `false`). When `false`, all Avalonia source directories are excluded via
`<Compile Remove="…" />` items. The Avalonia NuGet packages are in a conditional `ItemGroup`
that only activates when `EnableAvaloniaUI=true`.

**Rationale**: This means the current default build is already "Avalonia-free" at the compiler
level. However, all the physical UI source files still exist in the repository — the cleanup
must delete them and strip the now-unnecessary conditional scaffolding from `.csproj`.

**Alternatives considered**:
- Keeping the flag as a permanent opt-in gate → Rejected. The spec explicitly requires zero
  Avalonia references and a permanently clean repository. A lingering opt-in gate still means
  the physical files are committed and `grep` would still find them.

---

## R-002: Which Physical Files Must Be Deleted?

**Decision** — Delete the following from `src/TrashMailPanda/TrashMailPanda/`:

| Path | Reason |
|------|--------|
| `Views/` (all files) | Avalonia `.axaml` + code-behind; 7 files across Controls subdirectory |
| `ViewModels/` (all files) | 8 ViewModel files using `ObservableProperty` / `RelayCommand` |
| `Styles/` (all files) | Avalonia XAML resource styles |
| `Theming/ProfessionalColors.cs` | Directly imports `Avalonia.Media.Color` |
| `Converters/StatusConverters.cs` | Avalonia data converter types |
| `App.axaml` + `App.axaml.cs` | Avalonia application entry points |
| `ViewLocator.cs` | Avalonia view-locator pattern |
| `.avalonia-build-tasks/` | Auto-generated Avalonia build artifact directory |
| `app.manifest` | Windows `WinExe` manifest; irrelevant for `OutputType=Exe` |
| `Services/StartupHostedService.cs` | See R-004 below |

**Delete from `src/Tests/TrashMailPanda.Tests/`**:

| Path | Reason |
|------|--------|
| `ViewModels/ProviderStatusCardViewModelTests.cs` | Tests the deleted ViewModel |
| `ViewModels/ProviderStatusDashboardViewModelTests.cs` | Tests the deleted ViewModel |
| `Converters/StatusConvertersTests.cs` | Tests the deleted converter |

**Rationale**: These files are currently excluded from compilation but are still committed.
Physical deletion satisfies FR-001 through FR-004, SC-001, and SC-002.

---

## R-003: Which Services Have Hard Avalonia Type Dependencies?

**Decision**: **No services in `src/TrashMailPanda/TrashMailPanda/Services/`** directly import
or use Avalonia types. All Avalonia mentions in services are: (a) comments, (b) string literals,
or (c) `#if AVALONIA_UI` preprocessor guards.

**Evidence**:
- `ServiceCollectionExtensions.cs` — has `#if AVALONIA_UI … using …ViewModels` and
  `AddViewModels()` method. Both are inside `#if AVALONIA_UI` blocks. Deletion of the blocks
  is sufficient (no new code required).
- `ApplicationOrchestrator.cs` — has one `case OperationalMode.UIMode:` branch that emits
  a console message. Removal of the `UIMode` enum value and its switch case is required.
- `IApplicationOrchestrator.cs` and `IClassificationService.cs` — only XML-doc comments
  mentioning Avalonia. No code dependency; update comments during cleanup.

**Alternatives considered**: Moving service cleanup to a separate PR → Rejected. The scope
is minimal (4 targeted changes) and leaving `#if AVALONIA_UI` blocks makes future `grep`
audits fail SC-001.

---

## R-004: Should `StartupHostedService` and `StartupOrchestrator` Be Removed?

**Decision**: 

- **`StartupHostedService`** — **DELETE**. `AddStartupOrchestration()` (the method that
  registers it) is never called from `AddTrashMailPandaServices()` or `Program.cs`. It is
  dead code and was the hosted service that triggered the Avalonia startup path.

- **`StartupOrchestrator` + `IStartupOrchestrator`** — **RETAIN**. Despite the spec's
  mention of "desktop-specific StartupOrchestrator," code inspection shows this class has
  zero Avalonia imports and is actively used by `ApplicationService` via `IStartupOrchestrator`.
  Its console-appropriate equivalent (`ConsoleStartupOrchestrator`) already exists and is used
  by `ApplicationOrchestrator` for the TUI flow. Merging the two orchestrators is a separate
  refactoring concern outside this cleanup's scope.

**Rationale**: Deleting `StartupOrchestrator` would require refactoring `ApplicationService`
and would risk breaking the provider health check flow — violating the spec's constraint that
"no end-user functionality may be lost."

**Alternatives considered**: Merge `StartupOrchestrator` into `ConsoleStartupOrchestrator`
as part of this cleanup → Deferred. Out of scope for a pure deletion/cleanup task; create a
follow-up issue instead.

---

## R-005: What Changes Are Required in `.csproj` Files?

**Decision — `TrashMailPanda.csproj`** changes:

1. Remove the `EnableAvaloniaUI` property declaration block.
2. Remove the `PropertyGroup Condition="'$(EnableAvaloniaUI)' == 'true'"` block
   (WinExe, BuiltInComInteropSupport, ApplicationManifest, AVALONIA_UI define).
3. Simplify the console-only `PropertyGroup` — remove the `Condition` attribute so it
   becomes the unconditional default.
4. Remove the `ItemGroup Condition="'$(EnableAvaloniaUI)' != 'true'"` exclusion block
   (the physical files will be gone; exclusions are pointless).
5. Remove the `ItemGroup Condition="'$(EnableAvaloniaUI)' == 'true'"` Avalonia package
   block entirely (Avalonia, CommunityToolkit.Mvvm, etc.).
6. Remove the `ItemGroup Condition="'$(EnableAvaloniaUI)' == 'true'"` AvaloniaResource block.

**Decision — `TrashMailPanda.Tests.csproj`** changes:

1. Remove the `EnableAvaloniaUI` property declaration.
2. Remove the `ItemGroup Condition` exclusion block for `Converters\** ` and `ViewModels\**`.
3. Remove the unconditional `<PackageReference Include="Avalonia" Version="11.3.4" />`.

---

## R-006: Does `OperationalMode.UIMode` Have Any Console Handling?

**Decision**: `UIMode` appears in exactly two places:
1. `Models/Console/OperationalMode.cs` — enum value definition.
2. `Services/ApplicationOrchestrator.cs:302` — a `case OperationalMode.UIMode:` that emits
   `[dim]This mode will launch the Avalonia desktop UI.[/]` and returns immediately.

After deleting `UIMode` from the enum, the case in `ApplicationOrchestrator` will produce a
compile error — both must be changed atomically. The `ModeSelectionMenu` must also be checked
to ensure it doesn't offer `UIMode` as a menu option.

**Rationale**: Removing a console menu option that says "launch desktop UI" is consistent with
the console-first architecture and causes zero user functionality loss.

---

## R-007: Does Removing CommunityToolkit.Mvvm Affect Any Non-UI Code?

**Decision**: **No**. `CommunityToolkit.Mvvm` is declared only in the Avalonia-conditional
`ItemGroup` of the main `.csproj`. No non-UI service, provider, or shared project imports
anything from `CommunityToolkit.Mvvm`. Removing the package reference has zero impact on
provider or service compilation.

**Evidence**: `grep -r "CommunityToolkit" src/ --include="*.cs"` returns only ViewModel files
(all within the conditionally excluded directories). No results in `Services/`, `Providers/`,
or `Shared/`.

---

## R-008: Test Coverage Impact

**Decision**: Deleting `ProviderStatusCardViewModelTests.cs`,
`ProviderStatusDashboardViewModelTests.cs`, and `StatusConvertersTests.cs` is a **non-regression
deletion** — they exclusively tested code that is also being deleted. Per the spec Assumption:
"Tests that exclusively test deleted ViewModel or View behavior are expected to be removed as
part of this cleanup; their deletion does not constitute a coverage regression."

All remaining provider tests, service tests, security tests, storage tests, and ML tests are
unaffected. Coverage requirements (90% global, 95% provider, 100% security) must be verified
with a post-deletion `dotnet test --collect:"XPlat Code Coverage"` run.

---

## R-009: Binary Size & Startup Time Projections

**Decision**: The claims of SC-003 (< 500 ms startup) and SC-004 (≥ 50% binary size reduction)
are considered achievable based on the following evidence:

- Avalonia 11 packages (core + Desktop + Fluent + Fonts) collectively pull in ~40 MB of runtime
  assets including Skia rendering dlls. The console binary has no rendering subsystem.
- The current default build (`EnableAvaloniaUI=false`) already excludes Avalonia at compile time.
  Once the package references are removed entirely, `dotnet publish --self-contained` will
  produce a significantly smaller output because linker trimming will remove unused assemblies.
- Console startup via `Microsoft.Extensions.Hosting` is typically 100–300 ms on modern hardware
  for this service count.

**Note**: Formal measurement is a post-implementation validation step (see `quickstart.md`).

---

## Summary of All Resolutions

| ID | Item | Resolution |
|----|------|-----------|
| R-001 | Compile guard structure | Remove EnableAvaloniaUI flag scaffolding entirely |
| R-002 | Physical files to delete | 14 source files/dirs + 1 service + 3 test files |
| R-003 | Service Avalonia deps | Only comments + #if blocks; targeted removals, no refactoring |
| R-004 | StartupHostedService fate | Delete; StartupOrchestrator retained (no Avalonia deps) |
| R-005 | .csproj changes | Strip all conditional blocks; remove Avalonia + CommunityToolkit packages |
| R-006 | UIMode enum value | Delete UIMode + its case in ApplicationOrchestrator + ModeSelectionMenu check |
| R-007 | CommunityToolkit impact | Zero impact on non-UI code |
| R-008 | Test coverage | Deletions are non-regressions; re-verify coverage post-cleanup |
| R-009 | Binary size / startup | SC-003 / SC-004 projections achievable; validate after publish |
