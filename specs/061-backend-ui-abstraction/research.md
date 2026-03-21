# Research: Backend Refactoring for UI Abstraction

**Feature**: 061-backend-ui-abstraction
**Date**: 2026-03-21

## Research Task 1: IClassificationService vs Existing Services

### Question
How should `IClassificationService` relate to the existing `IEmailTriageService` and `IMLModelProvider`? Wrap, rename, or extend?

### Findings

**Existing architecture**:
- `IMLModelProvider.ClassifyActionAsync(EmailFeatureVector)` → `ActionPrediction` — raw ML inference
- `IMLModelProvider.ClassifyActionBatchAsync(IEnumerable<EmailFeatureVector>)` → `IReadOnlyList<ActionPrediction>` — batch ML inference
- `IEmailTriageService.GetAiRecommendationAsync(EmailFeatureVector)` → `ActionPrediction?` — calls `IMLModelProvider` + null in cold-start

**Spec requirement**: `IClassificationService.ClassifyBatchAsync(batch)` → `Result<IReadOnlyList<ClassificationResult>>` with action, confidence, and reasoning source (ML/rule-based).

### Decision: Thin wrapper over IMLModelProvider
- `IClassificationService` wraps `IMLModelProvider` to add:
  1. `ClassificationResult` record (richer than `ActionPrediction` — includes `ReasoningSource` enum)
  2. Empty-input guard (returns empty list, no ML call)
  3. Facade for both single and batch classification
- `IClassificationService` does NOT replace `IEmailTriageService`. Triage orchestration (fetch → present → decide → dual-write) stays in `IEmailTriageService`.
- `IClassificationService` does NOT own label storage or Gmail actions — it is purely "given features, predict action".
- The `EmailTriageService` can optionally be updated to call `IClassificationService.ClassifySingleAsync` instead of calling `IMLModelProvider` directly, consolidating the cold-start fallback logic.

### Alternatives Considered
1. **Rename IEmailTriageService to IClassificationService**: Rejected — triage has broader scope (session management, dual-write, re-triage) that doesn't fit a "classification" name.
2. **Merge IMLModelProvider into IClassificationService**: Rejected — IMLModelProvider is a proper provider with lifecycle, versioning, and rollback; classification service is a stateless coordinator.
3. **Put IClassificationService in Shared project**: Rejected — it depends on `ActionPrediction` and `EmailFeatureVector` from ML/Storage providers; circular dependency. Stays in main project.

---

## Research Task 2: UI-Agnostic Event Pattern in .NET

### Question
What is the best event pattern for application-level events without System.Reactive?

### Findings

**Spec constraint**: Standard .NET `event`/`EventArgs` pattern, no `IObservable<T>`.

**Options evaluated**:

| Pattern | Pros | Cons |
|---------|------|------|
| `event EventHandler<T>` | Standard .NET, familiar, no deps | Multiple events need multiple event fields |
| Single `event Action<ApplicationEvent>` | Unified stream, one subscriber point | Non-standard event delegate |
| `event EventHandler<ApplicationEventArgs>` with discriminated record | Unified + standard delegate | Subscriber must pattern-match |

### Decision: Single unified event with discriminated ApplicationEvent base
```csharp
public event EventHandler<ApplicationEventArgs>? ApplicationEventRaised;
```
Where `ApplicationEventArgs` wraps an `ApplicationEvent` base record, and concrete
event types inherit from it:
- `ModeSelectionRequestedEvent`
- `BatchProgressEvent { ProcessedCount, TotalCount, EstimatedSecondsRemaining }`
- `ProviderStatusChangedEvent { ProviderName, NewHealthState }`
- `ApplicationWorkflowCompletedEvent { ExitCode }`

**Benefits**:
- Standard `EventHandler<T>` delegate — compatible with existing BaseProvider event patterns
- Subscribers can pattern-match: `if (e.Event is BatchProgressEvent bp) { ... }`
- Adding new event types doesn't change the interface signature
- Thread-safe: copy delegate before invoking (same as BaseProvider pattern)
- Exception isolation: try/catch around each subscriber invocation

### Alternatives Considered
1. **System.Reactive IObservable<ApplicationEvent>**: Rejected per spec assumption — avoids NuGet dep.
2. **Channels (System.Threading.Channels)**: Rejected — harder to fan out to multiple subscribers.
3. **Separate event per type**: Rejected — forces interface changes for each new event type.

---

## Research Task 3: MSBuild Conditional Avalonia Exclusion

### Question
How to exclude Avalonia XAML, Views, ViewModels, and packages from the default build without deleting files?

### Findings

**Current state**:
- `TrashMailPanda.csproj` has `OutputType=WinExe`, Avalonia package refs, and `<AvaloniaResource>` items
- 11 `.axaml` files + 6 `.axaml.cs` code-behind files + 8 ViewModel files
- ViewModels reference `CommunityToolkit.Mvvm` (which is also useful for console; keep it)

**MSBuild approach**:
```xml
<PropertyGroup>
  <EnableAvaloniaUI Condition="'$(EnableAvaloniaUI)' == ''">false</EnableAvaloniaUI>
</PropertyGroup>

<!-- Avalonia packages only when enabled -->
<ItemGroup Condition="'$(EnableAvaloniaUI)' == 'true'">
  <PackageReference Include="Avalonia" Version="11.3.4" />
  <PackageReference Include="Avalonia.Desktop" Version="11.3.4" />
  <PackageReference Include="Avalonia.Themes.Fluent" Version="11.3.4" />
  <PackageReference Include="Avalonia.Fonts.Inter" Version="11.3.4" />
  <PackageReference Include="Avalonia.Diagnostics" Version="11.3.4" ... />
</ItemGroup>

<!-- Exclude Avalonia source files when not enabled -->
<ItemGroup Condition="'$(EnableAvaloniaUI)' != 'true'">
  <Compile Remove="Views\**" />
  <Compile Remove="ViewModels\**" />
  <Compile Remove="App.axaml.cs" />
  <None Remove="Views\**" />
  <None Remove="Styles\**" />
  <AvaloniaResource Remove="**\*.axaml" />
</ItemGroup>

<!-- Change OutputType to Exe when no Avalonia (no need for WinExe) -->
<PropertyGroup Condition="'$(EnableAvaloniaUI)' != 'true'">
  <OutputType>Exe</OutputType>
  <EnableDefaultAvaloniaItems>false</EnableDefaultAvaloniaItems>
</PropertyGroup>
```

### Decision: MSBuild property `EnableAvaloniaUI` (default: false)
- Avalonia packages are conditionally included
- Views/, ViewModels/, Styles/, App.axaml files are excluded from compilation
- OutputType changes to `Exe` (no WinExe needed for console app)
- `EnableDefaultAvaloniaItems` set to false when Avalonia is disabled
- Re-enable with: `dotnet build /p:EnableAvaloniaUI=true`
- CommunityToolkit.Mvvm stays always-included (useful for future observable patterns)

### Alternatives Considered
1. **Separate Avalonia project**: Rejected — too much restructuring for this feature; spec says build exclusion only.
2. **`#if AVALONIA` preprocessor directives**: Rejected — too invasive, touches every file.
3. **Remove from .sln**: Rejected — Avalonia is in the same project, not separate.

---

## Research Task 4: IApplicationOrchestrator vs Existing Program.cs

### Question
What workflow logic from Program.cs should move to IApplicationOrchestrator, and what stays?

### Findings

**Current Program.cs responsibilities**:
1. Host builder configuration (DI, logging, config) — stays in Program.cs
2. Service resolution (GetRequiredService) — stays in Program.cs
3. Welcome banner display — stays in Program.cs (or moves to orchestrator)
4. Setup wizard check + run — moves to orchestrator
5. Provider initialization via ConsoleStartupOrchestrator — orchestrator delegates
6. Training data sync (HandleStartupTrainingSyncAsync) — moves to orchestrator
7. Mode selection loop (while running) — moves to orchestrator
8. Mode dispatch (HandleModeSelectionAsync) — moves to orchestrator
9. Ctrl+C handling — orchestrator accepts CancellationToken; Program.cs wires it

### Decision: Program.cs becomes a thin host-builder + orchestrator launcher

**Program.cs (after refactoring)**:
```
Main → Build host → Resolve IApplicationOrchestrator → await orchestrator.RunAsync(ct) → return exit code
```

**IApplicationOrchestrator.RunAsync(ct)**:
1. Emit `ProviderSetupRequired` or proceed based on setup check
2. Delegate to `ConsoleStartupOrchestrator.InitializeProvidersAsync`
3. Emit `ProviderStatusChangedEvent` for each provider result
4. Run startup training sync
5. Enter mode selection loop:
   - Emit `ModeSelectionRequestedEvent`
   - Dispatch to `IEmailTriageConsoleService`, `IBulkOperationConsoleService`, etc.
   - Emit `BatchProgressEvent` during triage
6. Emit `ApplicationWorkflowCompletedEvent` with exit code

**Key**: The orchestrator depends on all the same console services Program.cs currently resolves.
The Console*Service classes remain the rendering layer; the orchestrator drives the sequence.

### Alternatives Considered
1. **Keep all logic in Program.cs**: Rejected — untestable static method; spec requires `IApplicationOrchestrator`.
2. **Make orchestrator unaware of console services**: Rejected — over-abstraction; orchestrator coordinates, console services render.

---

## Research Task 5: Refactoring Existing Components

### Question
Which existing components need refactoring vs. creation from scratch?

### Findings

**Components to KEEP as-is** (no changes):
- `IEmailTriageService` / `EmailTriageService` — already UI-agnostic, well-tested
- `IBulkOperationService` / `BulkOperationService` — already UI-agnostic
- `IMLModelProvider` / `MLModelProvider` — already a proper provider
- `IEmailTriageConsoleService` / `EmailTriageConsoleService` — pure rendering
- `IBulkOperationConsoleService` / `BulkOperationConsoleService` — pure rendering
- `IProviderSettingsConsoleService` / `ProviderSettingsConsoleService` — pure rendering
- `ModeSelectionMenu` — pure rendering, used by orchestrator
- `ConsoleStartupOrchestrator` — delegated to by `ApplicationOrchestrator`
- `ConsoleStatusDisplay` — banner, status display
- `ConfigurationWizard` — first-time setup flow
- `TrainingConsoleService` — training flow rendering
- `GmailTrainingScanCommand` — training data sync logic
- All ViewModels — preserved for Avalonia re-enablement

**Components to CREATE (new)**:
- `IClassificationService` — interface
- `ClassificationService` — wraps `IMLModelProvider`
- `IApplicationOrchestrator` — interface
- `ApplicationOrchestrator` — extracted from Program.cs loop
- `ClassificationResult` — immutable record
- `ApplicationEvent` hierarchy — base + 4 concrete event types
- `ApplicationEventArgs` — EventArgs wrapper
- `ConsoleEventRenderer` — subscribes to orchestrator events → Spectre output

**Components to MODIFY (refactoring)**:
- `Program.cs` — strip workflow loop, delegate to `IApplicationOrchestrator`
- `ServiceCollectionExtensions.cs` — register `IClassificationService`, `IApplicationOrchestrator`, `ConsoleEventRenderer`
- `TrashMailPanda.csproj` — add `EnableAvaloniaUI` MSBuild conditional
- `HandleStartupTrainingSyncAsync` — move from Program.cs to `ApplicationOrchestrator` (or a helper method it calls)
- `HandleModeSelectionAsync` — move from Program.cs to `ApplicationOrchestrator`

**Risk assessment**: The refactoring of Program.cs is the highest-risk change. The new services are additive and low-risk.
