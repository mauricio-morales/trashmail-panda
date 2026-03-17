# Data Model: Console Startup Orchestration

**Feature**: 057-console-startup-orchestration  
**Date**: March 16, 2026

## Core Entities

### 1. ProviderInitializationState

Represents the runtime state of a single provider during startup.

**Fields**:
- `ProviderName` (string): Display name (e.g., "Storage", "Gmail", "OpenAI")
- `ProviderType` (ProviderType enum): Required | Optional
- `Status` (InitializationStatus enum): NotStarted | Initializing | HealthChecking | Ready | Failed | Timeout
- `StatusMessage` (string?): Current operation description (e.g., "Authenticating with Gmail...")  
- `HealthStatus` (HealthStatus enum?): Healthy | Degraded | Critical | Unknown (null if not yet checked)
- `Error` (ProviderError?): Error details if Status == Failed
- `StartTime` (DateTime?): When initialization began
- `CompletionTime` (DateTime?): When initialization finished (success or failure)
- `Duration` (TimeSpan?): CompletionTime - StartTime

**Validation Rules**:
- ProviderName must not be null or whitespace
- Status transitions must follow: NotStarted → Initializing → HealthChecking → Ready|Failed
- Error must be non-null when Status == Failed
- StartTime must be set when Status != NotStarted
- CompletionTime must be set when Status == Ready|Failed|Timeout

**State Transitions**:
```
NotStarted 
    → Initializing (when InitializeAsync starts)
    → HealthChecking (when InitializeAsync succeeds, HealthCheckAsync starts)
    → Ready (when HealthCheckAsync succeeds)
    OR
    → Failed (when InitializeAsync or HealthCheckAsync fails)
    OR
    → Timeout (when CancellationToken triggers timeout)
```

**Relationships**:
- Part of `StartupSequenceState.ProviderStates` collection
- Maps 1:1 to `IProvider<TConfig>` instance in DI container

---

### 2. StartupSequenceState

Represents the overall startup orchestration progress across all providers.

**Fields**:
- `ProviderStates` (List\<ProviderInitializationState\>): Ordered list (Storage, Gmail)
- `CurrentProviderIndex` (int): Index of provider currently initializing (0-based)
- `OverallStatus` (SequenceStatus enum): Initializing | Completed | Failed | Cancelled
- `StartTime` (DateTime): When startup sequence began
- `CompletionTime` (DateTime?): When startup sequence finished
- `TotalDuration` (TimeSpan?): CompletionTime - StartTime
- `RequiredProvidersHealthy` (bool): True if Storage AND Gmail are both Ready with Healthy status

**Validation Rules**:
- ProviderStates must contain exactly 2 entries in order: Storage, Gmail
- CurrentProviderIndex must be >= 0 and < ProviderStates.Count
- OverallStatus == Completed requires all providers in Ready state (both are required)
- OverallStatus == Failed if any provider is Failed
- CompletionTime must be >= StartTime

**State Transitions**:
```
Initializing (CurrentProviderIndex = 0, 1, 2)
    → Completed (all Required providers Ready)
    OR
    → Failed (any Required provider Failed or Timeout)
    OR
    → Cancelled (user pressed Ctrl+C)
```

**Business Logic**:
- `IsReadyForModeSelection`: RequiredProvidersHealthy == true
- `FailedProviders`: ProviderStates.Where(p => p.Status == Failed)
- `NextProvider()`: Increment CurrentProviderIndex, transition to Initializing

---

### 3. OperationalMode (Enum)

Represents available modes in post-startup mode selection menu.

**Values**:
- `EmailTriage` (requires: Storage, Gmail): Main email triage operations
- `BulkOperations` (requires: Storage, Gmail): Bulk email actions (delete, archive, label)
- `ProviderSettings` (requires: Storage): Reconfigure providers
- `UIMode` (requires: Storage, Gmail): Launch Avalonia UI application (future)
- `Exit`: Graceful shutdown

**Note**: AI-powered modes (AIClassification, etc.) will be added when AI providers are integrated in future releases.

**Validation Rules**:
- Mode availability checked against provider health before display
- All current modes require Storage and Gmail providers to be healthy
- `UIMode` displays warning if Avalonia UI not available in current build

---

### 4. ConfigurationWizardState

Represents progress through first-time setup wizard.

**Fields**:
- `CurrentStep` (WizardStep enum): Welcome | StorageSetup | GmailSetup | Confirmation | Complete
- `StorageConfigured` (bool): True if Storage provider configuration saved
- `GmailConfigured` (bool): True if Gmail OAuth completed
- `Errors` (List\<string\>): Validation errors from current step

**State Transitions**:
```
Welcome 
    → StorageSetup 
    → GmailSetup (only if StorageSetup succeeded)
    → Confirmation (show summary of configured providers)
    → Complete (save all configurations, exit wizard)
```

**Validation Rules**:
- Cannot proceed to next step if current step has validation errors
- StorageSetup must succeed before GmailSetup (dependency)
- Confirmation step displays summary: "Storage ✓, Gmail ✓"

---

### 5. ConsoleDisplayOptions

Configuration for Spectre.Console rendering.

**Fields**:
- `ShowTimestamps` (bool): Display timestamps for each status message (default: true)
- `ShowDuration` (bool): Display duration after each provider completion (default: true)
- `UseColors` (bool): Enable ANSI color codes (default: true, auto-detect terminal support)
- `StatusRefreshInterval` (TimeSpan): How often to update progress spinners (default: 200ms)
- `ErrorDetailLevel` (ErrorDetailLevel enum): Minimal | Standard | Verbose (default: Standard)

**Validation Rules**:
- StatusRefreshInterval must be >= 100ms and <= 1000ms
- ErrorDetailLevel determines what exception details to show:
  - Minimal: Error message only
  - Standard: Message + error category + error code
  - Verbose: Full exception with stack trace

---

## Domain Enums

### ProviderType
- `Required`: Startup fails if provider fails (Storage, Gmail)
- `Optional`: Startup continues if provider fails (OpenAI)

### InitializationStatus  
- `NotStarted`: Provider has not begun initialization
- `Initializing`: InitializeAsync() is in progress
- `HealthChecking`: HealthCheckAsync() is in progress
- `Ready`: Provider initialized and healthy
- `Failed`: Initialization or health check failed
- `Timeout`: Operation exceeded timeout limit

### SequenceStatus
- `Initializing`: Startup sequence in progress
- `Completed`: All required providers initialized successfully
- `Failed`: One or more required providers failed
- `Cancelled`: User cancelled startup (Ctrl+C)

### WizardStep
- `Welcome`: Display welcome message and instructions
- `StorageSetup`: Configure storage provider (database path, encryption)
- `GmailSetup`: Configure Gmail provider (OAuth flow)
- `Confirmation`: Display summary of configured providers  
- `Complete`: Save configurations and exit wizard

### ErrorDetailLevel
- `Minimal`: Error message only
- `Standard`: Message + category + error code
- `Verbose`: Full exception with stack trace

---

## Entity Relationships

```
StartupSequenceState (1)
    ├── ProviderStates (2): ProviderInitializationState[]
    │   ├── [0] Storage (Required)
    │   └── [1] Gmail (Required)
    └── Uses → ConsoleDisplayOptions (1)

ConfigurationWizardState (1)
    └── Uses → ConsoleDisplayOptions (1)

OperationalMode (enum)
    └── Filtered by → StartupSequenceState.ProviderStates.HealthStatus
```

---

## Data Flow

### Startup Flow
1. **Create StartupSequenceState** with 2 ProviderInitializationState entries (NotStarted)
2. **For each provider** in sequence:
   - Update state: NotStarted → Initializing
   - Display status: "Initializing [Provider]..."
   - Call `IProvider.InitializeAsync(config)`
   - Update state: Initializing → HealthChecking
   - Call `IProvider.HealthCheckAsync()`
   - Update state: HealthChecking → Ready|Failed|Timeout
   - If provider Failed → transition OverallStatus to Failed, halt (both are required)
3. **Check StartupSequenceState.IsReadyForModeSelection**
   - If true → display `ModeSelectionMenu`
   - If false → display error recovery menu

### Configuration Wizard Flow
1. **Create ConfigurationWizardState** at Welcome step
2. **For each step** in sequence:
   - Display instructions with helpful links for current step
   - Google setup includes: Link to Google Cloud Console, steps to create OAuth credentials
   - Collect user input via Spectre.Console prompts
   - Validate input (Client ID format, Client Secret format, etc.)
   - If valid → mark as configured, advance to next step
   - If invalid → add to Errors list, show helpful message, retry current step
3. **At Confirmation step**:
   - Display summary of all configured providers
   - Prompt: "Save configuration and continue?" (Yes/No)
   - If Yes → persist configurations via `SecureStorageManager`, **automatically trigger** `ConsoleStartupOrchestrator.InitializeProvidersAsync()`
   - If No → return to Welcome step
4. **After save**:
   - Wizard transitions to normal startup flow (no restart needed)
   - `StartupSequenceState` is created with fresh provider states
   - Providers initialize sequentially using saved credentials

### Mode Selection Flow
1. **Load OperationalMode enum values**
2. **Verify all required providers** are healthy (Storage, Gmail)
3. **Display SelectionPrompt** with available modes
4. **User selects mode** → transition to that operational mode
5. **If Exit selected** → graceful shutdown with provider cleanup

**Note**: AI-powered modes will be added when AI providers are integrated.

---

## Persistence

**Note**: This feature does NOT introduce new database tables. Configuration persistence uses existing `SecureStorageManager` (OS keychain) and `appsettings.json`.

**Stored Data**:
- **Google OAuth Tokens**: OS keychain via `SecureStorageManager` (existing)
- **Google Client ID/Secret**: OS keychain via `SecureStorageManager` (for re-authorization)
- **Google Required Scopes**: `appsettings.json` → `GoogleConfig.RequiredScopes` (string array)
  - Example: `["https://www.googleapis.com/auth/gmail.modify", "https://www.googleapis.com/auth/contacts.readonly"]`
  - Used during health checks to detect scope mismatches
- **Database Path**: `appsettings.json` → `StorageConfig.DatabasePath`
- **Display Options**: `appsettings.json` → `ConsoleDisplayOptions` section (new)

**Example appsettings.json**:
```json
{
  "ConsoleDisplayOptions": {
    "ShowTimestamps": true,
    "ShowDuration": true,
    "UseColors": true,
    "StatusRefreshInterval": "00:00:00.200",
    "ErrorDetailLevel": "Standard"
  },
  "StorageConfig": {
    "DatabasePath": "./data/app.db"
  }
}
```

---

## Cross-Cutting Concerns

### Thread Safety
- All state entities are NOT thread-safe (single-threaded sequential startup)
- No concurrent access: providers initialize one at a time
- Updates to ProviderInitializationState occur on main thread only

### Logging
- Every state transition logged via `ILogger<ConsoleStartupOrchestrator>`
- Example: "Provider Gmail state: Initializing → HealthChecking"
- Errors logged with full exception details (separate from console display)

### Cancellation
- Global `CancellationTokenSource` for Ctrl+C handling
- Per-provider `CancellationTokenSource` for timeouts (30s each)
- Linked token sources: `CancellationTokenSource.CreateLinkedTokenSource(global, timeout)`
