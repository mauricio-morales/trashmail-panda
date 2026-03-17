# Research: Console Startup Orchestration & Health Checks

**Feature**: 057-console-startup-orchestration  
**Date**: March 16, 2026  
**Status**: Research Complete

## Research Findings

### 1. Console Formatting Library Selection

**Decision**: Use Spectre.Console for color-coded status display and interactive menus

**Rationale**:
- Native ANSI color support with automatic fallback for terminals without color
- Built-in progress indicators, status spinners, and tree views for real-time updates
- Interactive prompt library (SelectionPrompt, TextPrompt) for configuration wizard
- Table and panel rendering for structured status display
- Cross-platform (Windows/macOS/Linux) with consistent rendering
- Mature package with active maintenance and good .NET integration
- Supports markup syntax for bold/color combinations (e.g., `[bold red]Error[/]`)

**Alternatives Considered**:
1. **System.Console with manual ANSI codes**:
   - Rejected: Low-level API requires manual color code management (`\x1b[31m`)
   - No built-in support for interactive menus or progress indicators
   - Platform-specific behavior differences difficult to handle

2. **Colorful.Console**:
   - Rejected: Simpler API but lacks interactive components (menus, prompts)
   - No progress indicators or status spinners for startup visualization
   - Less active maintenance compared to Spectre.Console

3. **Terminal.Gui (gui.cs)**:
   - Rejected: Full TUI framework is overkill for sequential startup display
   - Heavier dependency with more complex event loop
   - Not needed since we only need status output + simple menu selection

**Implementation Impact**:
- Add `Spectre.Console` NuGet package to TrashMailPanda.csproj
- Status display uses `AnsiConsole.Status()` for real-time provider initialization
- Error messages use `[bold red]` markup for maximum visibility
- Configuration wizard uses `SelectionPrompt<T>` for menu-driven setup

---

### 2. Provider Initialization Flow Architecture

**Decision**: Sequential single-threaded initialization with immediate health checks after each provider

**Rationale**:
- Storage provider MUST initialize first (required by Gmail for credential storage)
- Gmail provider depends on Storage for OAuth token persistence - cannot initialize in parallel
- Sequential flow provides deterministic error messages (users see exactly which provider failed)
- Simpler debugging: no race conditions, clear linear progression
- Matches user mental model from feature spec: "initialize Storage, then Gmail"
- Health checks immediately after initialization ensure failures are caught before attempting next provider
- OpenAI and other AI providers deferred to future implementation (not critical for core email operations)

**Alternatives Considered**:
1. **Parallel provider initialization with dependency graph**:
   - Rejected: Added complexity of dependency resolution for minimal time savings
   - Storage must complete before Gmail/OpenAI anyway (credential storage dependency)
   - Race conditions make error messages non-deterministic
   - User confusion: "Which provider is initializing right now?"

2. **Health checks after all providers initialized**:
   - Rejected: Wastes time initializing dependent providers when storage has already failed
   - Example: No point initializing Gmail if Storage health check will fail later
   - Poor UX: User waits 30 seconds for all initializations, then sees "Storage failed" at the end

3. **Async initialization with Task.WhenAll**:
   - Rejected: Same issues as parallel initialization above
   - Constitution Check evaluated this - rejected because Storage is mandatory dependency

**Implementation Impact**:
- `ConsoleStartupOrchestrator.InitializeProvidersAsync()` uses sequential `await`
- Order: Storage → Gmail (hardcoded in orchestrator)
- Health check called immediately after each `InitializeAsync()` completes
- Failure of Storage or Gmail halts sequence and displays error/reconfigure prompt
- OpenAI and other AI providers can be added to the sequence in future releases when needed

---

### 3. Configuration Wizard Interaction Patterns

**Decision**: Use Spectre.Console's `SelectionPrompt` and `TextPrompt` for guided setup

**Rationale**:
- Matches sequential initialization order: wizard prompts for Storage, then Gmail
- `SelectionPrompt<T>` provides arrow-key navigation for yes/no and menu choices
- `TextPrompt<T>` handles text input with validation
- Built-in retry logic when validation fails
- Keyboard-only interaction (no mouse required) - works over SSH
- Clear visual feedback with highlighted selections
- OpenAI configuration can be added to wizard in future releases if needed

**Alternatives Considered**:
1. **Console.ReadLine with manual parsing**:
   - Rejected: No input validation, poor UX for yes/no choices
   - Users must type exact text (error-prone)
   - No visual feedback for current selection

2. **Interactive terminal UI with panels and forms**:
   - Rejected: Overkill for sequential setup flow
   - Requires terminal size management and redraw logic
   - Complexity not justified for one-time setup

**Implementation Impact**:
- `ConfigurationWizard.RunAsync()` uses SelectionPrompt for provider selection
- OAuth setup displays instructions then waits for browser completion
- Wizard can be re-invoked from error recovery menu
- AI provider setup (OpenAI, etc.) deferred to future implementation

---

### 4. Mode Selection Menu Architecture

**Decision**: Display interactive menu after successful startup using `SelectionPrompt<OperationalMode>`

**Rationale**:
- Post-initialization menu provides clear entry point to application features
- Keyboard navigation matches wizard interaction patterns (consistency)
- Modes can be dynamically enabled/disabled based on provider availability
- Example: "AI Classification" mode only shown if OpenAI provider is healthy
- Clear exit option (press 'Q' or select "Exit") for graceful shutdown
- Menu can display provider status summary before showing mode choices

**Alternatives Considered**:
1. **Command-line arguments for mode selection**:
   - Rejected: Requires restart to change modes
   - Poor discoverability: users don't know what modes are available
   - No way to show mode availability based on provider health

2. **Automatic mode detection based on configuration**:
   - Rejected: Users want explicit control over which mode to enter
   - No clear way to change modes without restarting app

3. **Avalonia UI window for mode selection**:
   - Rejected: Breaks console-first architecture from this feature
   - Requires graphics context (defeats headless operation goal)
   - Can still be offered AS a mode option: "Launch UI Mode"

**Implementation Impact**:
- `ModeSelectionMenu.ShowAsync()` displays available modes after startup
- Modes enum: `EmailTriage`, `BulkOperations`, `ProviderSettings`, `UIMode`, `Exit`
- AI-powered modes (AIClassification, etc.) will be added in future releases when AI providers are integrated
- Selecting "UIMode" would launch Avalonia UI (future enhancement)

---

### 5. Error Display and Recovery Patterns

**Decision**: Bold red error messages with full exception details, followed by recovery prompts

**Rationale**:
- Per technical requirements: "connector errors must be displayed in bold red with full error details"
- Users need visibility into WHY a provider failed (not just "failed")
- Recovery prompts provide actionable next steps: Reconfigure / Retry / Exit
- Error context includes: provider name, error category (Auth/Network/Config), timestamp
- Supports copy-paste of error messages for troubleshooting (plain text format)

**Alternatives Considered**:
1. **Simple error messages without stack traces**:
   - Rejected: Users can't diagnose root cause without details
   - Support burden: "What went wrong?" requires multiple back-and-forth exchanges

2. **Error codes only**:
   - Rejected: Requires users to look up error codes in documentation
   - Poor UX for non-technical users

3. **Logging errors to file instead of console**:
   - Rejected: Users won't know to check log file
   - Startup failures should be immediately visible

**Implementation Impact**:
- Error display uses `AnsiConsole.MarkupLine("[bold red]ERROR: {message}[/]")`
- Full exception details rendered with `Write(exception)` method
- Recovery menu uses `SelectionPrompt` with options: "Reconfigure [Provider]", "Retry", "Exit"
- Errors logged to console AND to file via existing ILogger infrastructure
- **OAuth Scope Mismatch**: New error code `INSUFFICIENT_SCOPES` with `MissingScopes` array
  - Displayed when token valid but missing required scopes (e.g., app upgraded to need Google Contacts API)
  - User must re-authorize to grant new scopes (Google OAuth policy - no silent upgrades)

---

### 6. Provider Health Check Integration

**Decision**: Reuse existing `IProvider<TConfig>.HealthCheckAsync()` method from provider architecture

**Rationale**:
- Constitution Principle I (Provider-Agnostic Architecture): "Every provider MUST implement IProvider<TConfig> with HealthCheckAsync"
- No need to create console-specific health check logic
- Provider health check returns `Result<HealthCheckResult>` with detailed status
- HealthCheckResult includes: Status (Healthy/Degraded/Critical), Issues list, Duration
- Existing provider implementations (Gmail, Storage, OpenAI) already implement health checks

**Alternatives Considered**:
1. **Create new console-specific health check interface**:
   - Rejected: Violates Provider-Agnostic Architecture principle
   - Duplication of existing health check logic
   - Maintenance burden: two health check pathways to keep in sync

2. **Skip health checks - assume InitializeAsync success means healthy**:
   - Rejected: Initialization success doesn't guarantee operational health
   - Example: Gmail could initialize with expired OAuth tokens (false success)

**Implementation Impact**:
- `ConsoleStartupOrchestrator` calls `provider.HealthCheckAsync()` after each initialization
- HealthCheckResult mapped to console color: Healthy=green, Degraded=yellow, Critical=red
- Health check timeout: 10 seconds per provider (configurable)
- Timeout handling: Display warning and treat as Degraded status
- **OAuth Scope Validation** (for Google Provider):
  - Health check validates token has ALL required scopes from `GoogleConfig.RequiredScopes`
  - If scope mismatch detected: `ErrorCode = "INSUFFICIENT_SCOPES"` with `MissingScopes` list
  - Example scenario: App v1.0 uses Gmail API only, v2.0 adds Google Contacts API
  - User with v1.0 token upgrading to v2.0 will see scope mismatch error
  - User must re-authorize to grant new scopes (Google OAuth policy - no silent scope upgrades)
  - Required scopes configured in `appsettings.json`: `GoogleConfig.RequiredScopes: ["gmail.modify", "contacts.readonly"]`

---

### 7. Timeout and Cancellation Strategy

**Decision**: Individual 30-second timeout per provider initialization with cancellation tokens

**Rationale**:
- Per spec FR-014: "30 seconds per provider" (not total startup time)
- Allows slow network operations (OAuth redirects) without failing prematurely
- CancellationTokenSource per provider prevents one slow provider from blocking others
- User can press Ctrl+C to cancel startup at any time
- Timeout includes both InitializeAsync and HealthCheckAsync durations

**Alternatives Considered**:
1. **Global startup timeout (e.g., 60 seconds for all providers)**:
   - Rejected: Unfair to later providers (OpenAI gets less time if Storage is slow)
   - User doesn't know which provider caused timeout

2. **No timeouts - wait indefinitely**:
   - Rejected: Hanging startup is poor UX (appears frozen)
   - Network failures could block forever without timeout

3. **Shorter timeouts (10 seconds)**:
   - Rejected: OAuth flows can take 15-20 seconds for user to complete in browser
   - False failures during normal operation

**Implementation Impact**:
- `CancellationTokenSource` with 30-second timeout per provider
- Display countdown timer during initialization: "Initializing Gmail... (25s remaining)"
- Timeout triggers error recovery menu: "Gmail initialization timed out - Reconfigure / Retry / Exit"
- Global Ctrl+C handler for user-initiated cancellation

---

## Research Summary

All technical decisions documented above support the sequential, single-threaded console startup architecture. Key choices:

1. **Spectre.Console**: Provides rich formatting and interactive components
2. **Sequential Initialization**: Storage → Gmail (dependency order, AI providers deferred)
3. **Immediate Health Checks**: After each provider initialization completes
4. **Interactive Wizard**: Spectre.Console prompts for first-time setup
5. **Mode Selection Menu**: Post-startup entry point to application features
6. **Bold Red Errors**: Maximum visibility per technical requirements
7. **Existing Provider Infrastructure**: Reuse IProvider<TConfig> architecture
8. **30-Second Per-Provider Timeout**: Individual timeouts with cancellation support

**OpenAI Decision**: AI providers (OpenAI, local ML) are deferred to future implementation. The console startup system is designed to be extensible - additional providers can be added to the initialization sequence when needed without architectural changes.

No further clarifications needed - ready for Phase 1 (Design & Contracts).
