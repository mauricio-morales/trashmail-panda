# Feature Specification: Deprecate & Remove Avalonia UI Code

**Feature Branch**: `063-remove-avalonia-ui`  
**Created**: 2026-03-23  
**Status**: Draft  
**Input**: GitHub Issue #68 — Cleanup: Deprecate & Remove Avalonia UI Code

## User Scenarios & Testing *(mandatory)*

### User Story 1 - Clean Console Application Builds Successfully (Priority: P1)

A developer clones the repository after this cleanup and runs the build. They see a clean build with no references to the removed UI framework. The application launches as a terminal program with colored output, accepts commands, and all provider health checks pass — without any desktop window appearing.

**Why this priority**: The foundation of this feature. If the application cannot build and run cleanly as a console-only tool, nothing else is valid. All provider functionality (Gmail, OpenAI, Storage) must remain fully operational.

**Independent Test**: Can be fully tested by running `dotnet build` followed by `dotnet run` and verifying the application starts as a terminal program with Spectre.Console output and provider status shown.

**Acceptance Scenarios**:

1. **Given** the Avalonia removal is complete, **When** a developer runs the build command, **Then** the build completes without errors and produces a console-only binary.
2. **Given** a clean build, **When** a developer runs the application, **Then** a terminal UI appears (no desktop window), displaying provider status with colored output.
3. **Given** the running console application, **When** a developer interacts with Gmail OAuth, OpenAI config, and storage commands, **Then** all features work as they did before removal.

---

### User Story 2 - Codebase Contains Zero Legacy UI References (Priority: P2)

A developer auditing the codebase (or an automated CI check) searches for references to the removed UI framework and finds none. All MVVM observable/command patterns have also been eliminated.

**Why this priority**: This is the explicit acceptance criterion of the cleanup task. Leftover references can cause build confusion, hidden dependencies, or mislead future contributors about the intended architecture.

**Independent Test**: Can be verified independently by running `grep -r "Avalonia" src/` and `grep -r "ObservableProperty\|RelayCommand" src/` and confirming zero results.

**Acceptance Scenarios**:

1. **Given** the cleanup is complete, **When** searching the `src/` directory for the legacy UI framework name, **Then** zero matches are returned.
2. **Given** the cleanup is complete, **When** searching for MVVM observable property and command patterns, **Then** zero matches are returned.
3. **Given** the project file, **When** inspecting declared package dependencies, **Then** no desktop UI framework packages are listed.

---

### User Story 3 - Application Starts Faster with Smaller Footprint (Priority: P3)

A developer or end user runs the application after the cleanup and notices it starts noticeably faster than the previous desktop version. The published binary is significantly smaller, making distribution and deployment easier.

**Why this priority**: A key motivating benefit of moving to a console architecture — removing desktop UI framework overhead directly reduces startup time and binary size. This validates the architectural decision was worth the cleanup effort.

**Independent Test**: Can be verified by timing `dotnet run` startup to first interactive prompt and comparing published binary sizes before and after.

**Acceptance Scenarios**:

1. **Given** a published console binary, **When** measuring startup time to first interactive prompt, **Then** startup completes in under 500 milliseconds.
2. **Given** the published binary size after cleanup, **When** comparing to the previous desktop version binary, **Then** the new binary is at least 50% smaller.

---

### User Story 4 - All Existing Tests Continue to Pass (Priority: P2)

A developer runs the full test suite after removal and sees the same green test results as before. No tests have been silently removed or broken by the cleanup. Provider tests, unit tests, and security tests all pass.

**Why this priority**: The cleanup must not degrade quality or coverage. Any test failures indicate that shared logic was incorrectly removed along with the UI code.

**Independent Test**: Fully testable by running `dotnet test --configuration Release` and confirming all previously passing tests still pass.

**Acceptance Scenarios**:

1. **Given** the Avalonia removal is complete, **When** running the full test suite, **Then** all tests pass with no regressions.
2. **Given** tests run with coverage, **When** reviewing results, **Then** coverage levels meet or exceed project thresholds defined before cleanup.

---

### Edge Cases

- What happens when a service previously depended on a UI framework type at compile time (e.g., passed `IClassicDesktopStyleApplicationLifetime`)? The service must be refactored so it no longer requires that type; the build must fail if such dependencies remain.
- What if a provider service constructor accepts a UI-framework-specific interface via dependency injection? The DI container registration must be updated — the build should fail, not produce a silent runtime crash.
- What happens to tests that were written to test ViewModel behavior (e.g., `ObservableProperty` setters triggering `PropertyChanged`)? Those tests must be deleted alongside the ViewModels they covered; provider and service tests must be unaffected.
- What if `GlobalUsings.cs` contains using directives that pull in now-removed namespaces? The build must fail with a clear "namespace not found" message, making the residual reference easy to locate and remove.

## Requirements *(mandatory)*

### Functional Requirements

- **FR-001**: All files in `ViewModels/`, `Views/`, `Styles/`, `Theming/`, and `Converters/` directories MUST be deleted from the main application project.
- **FR-002**: `App.axaml`, `App.axaml.cs`, and `ViewLocator.cs` MUST be deleted from the main application project.
- **FR-003**: The main application project file MUST NOT reference any desktop UI framework packages after the cleanup.
- **FR-004**: The main application project file MUST NOT reference `CommunityToolkit.Mvvm` after the cleanup.
- **FR-005**: `Program.cs` MUST use the standard hosted application builder pattern without any desktop UI framework bootstrap code.
- **FR-006**: `GlobalUsings.cs` MUST NOT contain any using directives for the removed UI framework or its related packages.
- **FR-007**: `appsettings.json` MUST NOT contain settings specific to desktop UI (e.g., window dimensions, theme names for a GUI framework).
- **FR-008**: Services that exclusively served the desktop UI layer (`StartupHostedService`, desktop-specific `StartupOrchestrator`) MUST be removed; any reusable logic they contain MUST be migrated to console-appropriate equivalents before removal.
- **FR-009**: Provider-layer services (Gmail, OpenAI, Storage, `ProviderBridgeService`, `ProviderHealthMonitorService`, `SecureTokenDataStore`) MUST be preserved unchanged.
- **FR-010**: The application MUST build successfully with `dotnet build --configuration Release` after all removals.
- **FR-011**: The full test suite MUST pass with `dotnet test --configuration Release` after all removals.
- **FR-012**: Dependency injection registrations in `ServiceCollectionExtensions.cs` MUST be updated to remove references to deleted services and types.

## Success Criteria *(mandatory)*

### Measurable Outcomes

- **SC-001**: A full-text search of the `src/` directory for the legacy UI framework name returns zero matches.
- **SC-002**: A full-text search of the `src/` directory for MVVM observable property and relay command patterns returns zero matches.
- **SC-003**: The application starts and reaches the interactive console prompt in under 500 milliseconds on standard developer hardware.
- **SC-004**: The published, self-contained console binary is at least 50% smaller (by file size) than the equivalent published binary before this cleanup.
- **SC-005**: The project compiles with zero errors and zero warnings after all legacy UI code is removed.
- **SC-006**: The full automated test suite passes with zero regressions after all removals.
- **SC-007**: All provider capabilities (Gmail OAuth, OpenAI configuration, storage operations, provider health checks) remain fully functional in the console application after the cleanup.

## Assumptions

- Issue #62 (Spectre.Console UI Implementation) is complete and all console-facing features are implemented and tested before this cleanup begins.
- The console application already has functional equivalents for all UI-layer features (OAuth flow, provider status display, configuration commands) that were previously handled by the desktop UI.
- Tests that exclusively test deleted ViewModel or View behavior are expected to be removed as part of this cleanup; their deletion does not constitute a coverage regression.
- `GmailOAuthService.cs` is retained and already uses a console-compatible OAuth flow (browser redirect or device code); no new OAuth implementation is required by this feature.
- Services marked as "keep" in the issue scope (`ProviderBridgeService`, `ProviderHealthMonitorService`, `SecureTokenDataStore`, `ServiceCollectionExtensions`) contain no Avalonia or MVVM dependencies and can be preserved without modification.

## Dependencies & Constraints

- **Prerequisite**: Issue #62 (Spectre.Console UI Implementation) must be merged and all console features verified working before this cleanup proceeds.
- **Blocks**: Issue #67 (Documentation & Migration Guide) — documentation updates should reflect the final clean architecture produced by this feature.
- **Constraint**: No end-user functionality may be lost. Every capability available in the desktop UI must have an equivalent console command or flow in place before the UI code is deleted.
