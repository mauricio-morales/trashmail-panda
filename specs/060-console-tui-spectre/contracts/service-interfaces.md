# Console Service Interface Contract

**Feature**: Console TUI (#060) — C# Service Interfaces  
**Date**: 2026-03-19  
**Type**: Internal software contract (C# interfaces)

This document defines the C# interfaces that the console TUI layer depends on.
These are the primary seams for unit testing and future extension.

---

## Architecture: Two-Layer Service Design

Business logic is deliberately separated from TUI presentation so that a future UI
(web API, Avalonia desktop, MCP tool) can depend on the same core services without
rewriting any orchestration logic and without any dependency on Spectre.Console.

```
┌────────────────────────────────────────────────────────────────────────┐
│  PRESENTATION LAYER  (TrashMailPanda/Services/Console/)              │
│                                                                      │
│  IEmailTriageConsoleService   IBulkOperationConsoleService           │
│  IProviderSettingsConsoleService   IConsoleHelpPanel                 │
│  └─ depends on IAnsiConsole (Spectre.Console)                        │
│  └─ depends on business logic interfaces (below)                    │
│  └─ renders cards, captures keypresses, shows progress bars          │
└────────────────────────────────────────────────────────────────────────┘
                          │ depends on │
┌────────────────────────────────────────────────────────────────────────┐
│  BUSINESS LOGIC LAYER  (TrashMailPanda/Services/)                    │
│                                                                      │
│  IEmailTriageService          IBulkOperationService                  │
│  └─ NO IAnsiConsole dependency                                       │
│  └─ NO Spectre.Console reference                                     │
│  └─ returns pure data (TriageDecision, TriageSessionInfo, etc.)      │
│  └─ reusable by any future UI (web, Avalonia, MCP)                   │
└────────────────────────────────────────────────────────────────────────┘
                          │ depends on │
┌────────────────────────────────────────────────────────────────────────┐
│  PROVIDER LAYER  (TrashMailPanda.Providers.*)                        │
│                                                                      │
│  IEmailProvider   IMLModelProvider   IEmailArchiveService            │
└────────────────────────────────────────────────────────────────────────┘
```

**Future UI substitution**: A web API controller, Avalonia ViewModel, or MCP tool would
create its own presenter implementing its own interface, depending on `IEmailTriageService`
and `IBulkOperationService`. No business logic is rewritten.

**Extraction path**: `IEmailTriageService` and `IBulkOperationService` live in
`TrashMailPanda/Services/` today. When a second UI project is created, move them to
`TrashMailPanda.Shared` — only namespace changes required.

---

## Business Logic Interfaces

_No `IAnsiConsole` dependency. No Spectre.Console reference. Pure data in, pure data out._

### IEmailTriageService

```csharp
namespace TrashMailPanda.Services;

/// <summary>
/// UI-agnostic triage business logic.
/// Detects cold-start vs AI-assisted mode, sources the untriaged email queue,
/// fetches AI recommendations, and executes dual-write decisions (Gmail + training label).
/// No rendering dependency — consumable by any UI layer.
/// </summary>
public interface IEmailTriageService
{
    /// <summary>
    /// Returns current session state: triage mode, cumulative labeled count,
    /// and whether the training threshold has been reached.
    /// Called once at session start.
    /// </summary>
    Task<Result<TriageSessionInfo>> GetSessionInfoAsync(
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Returns a page of untriaged EmailFeatureVectors (training_label IS NULL),
    /// ordered by ExtractedAt descending.
    /// </summary>
    Task<Result<IReadOnlyList<EmailFeatureVector>>> GetNextBatchAsync(
        int pageSize,
        int offset,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Returns the AI-predicted action for the given feature vector.
    /// Returns Success(null) in ColdStart mode (no trained model).
    /// Returns Success(prediction) in AiAssisted mode.
    /// </summary>
    Task<Result<ActionPrediction?>> GetAiRecommendationAsync(
        EmailFeatureVector feature,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Executes the user's triage decision:
    ///   1. Applies Gmail action (BatchModifyAsync or ReportSpamAsync) FIRST.
    ///   2. On Gmail success: persists training_label via SetTrainingLabelAsync.
    ///   3. On Gmail failure: returns Failure — training_label is NOT stored.
    /// </summary>
    /// <param name="emailId">Gmail message ID.</param>
    /// <param name="chosenAction">"Keep", "Archive", "Delete", or "Spam".</param>
    /// <param name="aiRecommendation">AI-suggested action, or null in ColdStart mode.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>
    /// Success: the recorded TriageDecision (contains IsOverride flag).
    /// Failure: NetworkError, AuthenticationError if Gmail action fails.
    /// </returns>
    Task<Result<TriageDecision>> ApplyDecisionAsync(
        string emailId,
        string chosenAction,
        string? aiRecommendation,
        CancellationToken cancellationToken = default);
}
```

**Value object returned by `GetSessionInfoAsync`**:

```csharp
namespace TrashMailPanda.Models;

/// <summary>Snapshot of triage session state at startup.</summary>
public sealed record TriageSessionInfo(
    TriageMode Mode,              // ColdStart | AiAssisted
    int LabeledCount,             // COUNT(*) WHERE training_label IS NOT NULL
    int LabelingThreshold,        // From MLModelProviderConfig.MinTrainingSamples
    bool ThresholdAlreadyReached  // LabeledCount >= LabelingThreshold
);
```

---

### IBulkOperationService

```csharp
namespace TrashMailPanda.Services;

/// <summary>
/// UI-agnostic bulk operation business logic.
/// Filters emails from the local DB or Gmail, previews the scope, and executes
/// batch Gmail actions + training label storage.
/// No rendering dependency — consumable by any UI layer.
/// </summary>
public interface IBulkOperationService
{
    /// <summary>
    /// Returns a preview of emails matching the given criteria, without executing any actions.
    /// </summary>
    Task<Result<IReadOnlyList<EmailFeatureVector>>> PreviewAsync(
        BulkOperationCriteria criteria,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Executes the bulk action on the given email IDs.
    /// Each email: Gmail action first, then SetTrainingLabelAsync on success.
    /// Failures are collected and returned but do not stop the batch.
    /// </summary>
    /// <returns>
    /// Success: count of emails successfully acted upon.
    /// Value includes partial success (some may have failed — check FailedIds).
    /// </returns>
    Task<Result<BulkOperationResult>> ExecuteAsync(
        IReadOnlyList<string> emailIds,
        string action,             // "Archive", "Delete", "Label"
        CancellationToken cancellationToken = default);
}

public sealed record BulkOperationResult(
    int SuccessCount,
    IReadOnlyList<string> FailedIds
);
```

---

## Presentation Interfaces (TUI)

_Thin presenters over the business logic interfaces. Depend on `IAnsiConsole` and Spectre.Console.
Not reusable outside the console TUI — that is intentional._

## IEmailTriageConsoleService

```csharp
namespace TrashMailPanda.Services.Console;

/// <summary>
/// Thin TUI presenter for the Email Triage workflow.
/// Renders email cards, captures keypresses, and delegates all business logic
/// (mode detection, Gmail action execution, training label storage) to IEmailTriageService.
/// No direct dependency on IEmailProvider, IMLModelProvider, or IEmailArchiveService —
/// those are wired inside IEmailTriageService.
/// </summary>
public interface IEmailTriageConsoleService
{
    /// <summary>
    /// Runs a triage session until the user exits or the batch is exhausted.
    /// Handles both ColdStart and AiAssisted modes transparently.
    /// </summary>
    /// <param name="accountId">Gmail account identifier (e.g. "me").</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>
    /// Success: the session summary for display.
    /// Failure: NetworkError, AuthenticationError if provider communication fails.
    /// </returns>
    Task<Result<TriageSessionSummary>> RunAsync(
        string accountId,
        CancellationToken cancellationToken = default);
}
```

---

## IBulkOperationConsoleService

```csharp
namespace TrashMailPanda.Services.Console;

/// <summary>
/// Thin TUI presenter for the Bulk Operations workflow.
/// Guides the user through criteria selection and preview via Spectre.Console prompts,
/// then delegates execution to IBulkOperationService.
/// No direct dependency on IEmailProvider or IEmailArchiveService.
/// </summary>
public interface IBulkOperationConsoleService
{
    /// <summary>
    /// Runs the bulk operations wizard.
    /// </summary>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>
    /// Success: count of emails acted upon.
    /// Failure: NetworkError, ValidationError if criteria are invalid.
    /// </returns>
    Task<Result<int>> RunAsync(CancellationToken cancellationToken = default);
}
```

---

## IProviderSettingsConsoleService

```csharp
namespace TrashMailPanda.Services.Console;

/// <summary>
/// Orchestrates the Provider Settings console workflow.
/// Allows re-authorization of Gmail, storage usage display, and storage limit adjustment.
/// </summary>
public interface IProviderSettingsConsoleService
{
    /// <summary>
    /// Runs the provider settings menu loop.
    /// </summary>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>
    /// Success: true when user exits settings cleanly.
    /// Failure: AuthenticationError if re-authorization fails unrecoverably.
    /// </returns>
    Task<Result<bool>> RunAsync(CancellationToken cancellationToken = default);
}
```

---

## IConsoleHelpPanel

```csharp
namespace TrashMailPanda.Services.Console;

/// <summary>
/// Renders a context-specific help panel and blocks until the user dismisses it.
/// </summary>
public interface IConsoleHelpPanel
{
    /// <summary>
    /// Displays the help panel for the given context and waits for dismissal.
    /// </summary>
    /// <param name="context">Mode-specific key bindings and description.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    Task ShowAsync(HelpContext context, CancellationToken cancellationToken = default);
}
```

---

## IEmailArchiveService — Additions

The following methods are added to the existing `IEmailArchiveService` interface:

```csharp
// Additions to existing IEmailArchiveService

/// <summary>
/// Sets the explicit training label for the given email's feature vector.
/// Also sets UserCorrected = 1 when the user overrode an AI recommendation.
/// No-op (returns Success(false)) if no feature vector exists for the email ID.
/// MUST only be called after the corresponding Gmail action has succeeded.
/// </summary>
Task<Result<bool>> SetTrainingLabelAsync(
    string emailId,
    string label,              // "Keep", "Archive", "Delete", "Spam"
    bool userCorrected,        // true when user overrode AI recommendation
    CancellationToken cancellationToken = default);

/// <summary>
/// Returns the count of feature vectors with an explicit training label.
/// Used to seed EmailTriageSession.LabeledCount at session start.
/// </summary>
Task<Result<int>> CountLabeledAsync(CancellationToken cancellationToken = default);

/// <summary>
/// Returns a page of feature vectors with training_label IS NULL (untriaged queue).
/// Ordered by ExtractedAt descending (most recently scanned first).
/// </summary>
Task<Result<IReadOnlyList<EmailFeatureVector>>> GetUntriagedAsync(
    int pageSize,
    int offset,
    CancellationToken cancellationToken = default);
```

---

## Invariants (All Interfaces)

1. **Result pattern**: All async methods return `Result<T>` — never throw.
2. **Cancellation**: All methods accept `CancellationToken` and respect it promptly.
3. **Layer boundary**: Business logic interfaces (`IEmailTriageService`, `IBulkOperationService`)
   have NO dependency on `IAnsiConsole` or any Spectre.Console type. This is enforced at
   compile-time by keeping them in a namespace/assembly with no Spectre.Console reference.
4. **IAnsiConsole injection**: All console service implementations accept `IAnsiConsole?`
   constructor parameter defaulting to `AnsiConsole.Console` for testability.
5. **No business logic in callers**: `Program.cs` only calls `RunAsync()` on the console
   presenter. The console presenter only calls service methods on `IEmailTriageService` /
   `IBulkOperationService`. Orchestration logic lives exactly one layer deep.
