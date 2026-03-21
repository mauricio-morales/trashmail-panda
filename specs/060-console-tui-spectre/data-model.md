# Data Model: Console TUI with Spectre.Console (#060)

**Feature**: Console TUI — Email Triage, Bulk Ops, Provider Settings, Color Scheme, Help System  
**Date**: 2026-03-19  
**Based on**: [research.md](./research.md), [spec.md](./spec.md)

---

## New Entities Overview

| Entity | Location | Purpose |
|--------|----------|---------|
| `EmailTriageSession` | Console/Models | In-memory session tracking |
| `TriageDecision` | Console/Models | Per-email triage decision record |
| `TriageSessionSummary` | Console/Models | End-of-session statistics |
| `KeyBinding` | Console/Models | Key + description pair for help system |
| `HelpContext` | Console/Models | Mode-specific key binding collection |
| `BulkOperationCriteria` | Console/Models | Filter parameters for bulk operations |
| `ConsoleColors` | Console/Services | Semantic color markup constants |

**Modified existing entity**: `EmailFeatureVector` — add `TrainingLabel string?` column to
`email_features` table (see §1 below).

---

## 1. EmailFeatureVector — TrainingLabel Addition (Existing Entity Modified)

**File**: `src/Providers/Storage/TrashMailPanda.Providers.Storage/Models/EmailFeatureVector.cs`  
**Table**: `email_features` (existing) — **ADD** one nullable column  
**Purpose**: Store the explicit user triage decision as ground-truth training label.

Add to the existing `EmailFeatureVector` class (after the existing `UserCorrected` field):

```csharp
/// <summary>
/// Explicit triage decision stored by the triage service after a successful Gmail action.
/// One of: "Keep", "Archive", "Delete", "Spam".
/// NULL means the email has not been manually triaged yet; the training pipeline infers
/// the label from boolean feature flags (WasInSpam, WasInTrash, IsInInbox, IsArchived)
/// via ITrainingSignalAssigner.
/// Non-null values take precedence over any inference at training time.
/// </summary>
public string? TrainingLabel { get; set; }
```

**Constraints**:
- When non-null: must be one of `"Keep"`, `"Archive"`, `"Delete"`, `"Spam"`
- `NULL` = untriaged (query `WHERE training_label IS NULL` to get the triage queue)
- `UserCorrected` (existing field) is orthogonal: set to `1` when user overrides an AI
  recommendation, regardless of which action was chosen
- Both can be set in the same row: a user-corrected vector has
  `TrainingLabel = "Archive"` AND `UserCorrected = 1`

**Training pipeline impact**: Update `MapToTrainingInput` in `ModelTrainingPipeline` and
`IncrementalUpdateService` from:
```csharp
Label = string.Empty, // Ground-truth label populated by user action pipeline
```
to:
```csharp
Label = v.TrainingLabel ?? _signalAssigner.AssignSignal(v).Label,
```
This fixes an existing bug where training labels were always empty strings.

**Migration**: Add `[N]_AddTrainingLabelToEmailFeatures` — `ALTER TABLE email_features ADD
COLUMN training_label TEXT NULL;` (see Database Migration Summary below).

---

## 2. EmailTriageSession (In-Memory Session State)

**File**: `src/TrashMailPanda/TrashMailPanda/Models/Console/EmailTriageSession.cs`  
**Purpose**: Runtime state for the current triage session; not persisted — the session queue
is derived from `email_features WHERE training_label IS NULL` and the labeled count is loaded
once from `IEmailArchiveService.CountLabeledAsync` at session start.

```csharp
namespace TrashMailPanda.Models.Console;

public enum TriageMode { ColdStart, AiAssisted }

public sealed class EmailTriageSession
{
    public required string AccountId { get; init; }
    public TriageMode Mode { get; set; }

    /// <summary>
    /// Cumulative labeled count loaded from DB at session start, then incremented in-memory.
    /// Source: COUNT(*) FROM email_features WHERE training_label IS NOT NULL.
    /// </summary>
    public int LabeledCount { get; set; }

    /// <summary>Minimum labeled samples required to trigger training threshold prompt.</summary>
    public int LabelingThreshold { get; init; }

    /// <summary>True once threshold was crossed and threshold prompt was already shown this session.</summary>
    public bool ThresholdPromptShownThisSession { get; set; }

    /// <summary>Count of emails processed in the current session only (not cumulative).</summary>
    public int SessionProcessedCount { get; set; }

    /// <summary>Override count for the current session only.</summary>
    public int SessionOverrideCount { get; set; }

    /// <summary>Per-action counts for the current session: Keep, Archive, Delete, Spam.</summary>
    public Dictionary<string, int> ActionCounts { get; } = new(StringComparer.Ordinal)
    {
        ["Keep"] = 0, ["Archive"] = 0, ["Delete"] = 0, ["Spam"] = 0
    };

    public DateTime StartedAtUtc { get; } = DateTime.UtcNow;

    /// <summary>SQL offset for the current page of untriaged emails (local DB paging).</summary>
    public int CurrentOffset { get; set; }
}
```

**Validation rules**:
- `LabeledCount` ≥ 0 at all times
- `LabelingThreshold` > 0 (loaded from `MLModelProviderConfig.MinTrainingSamples`)
- `ThresholdPromptShownThisSession` is reset to `false` on each new session start
- `CurrentOffset` is NOT persisted; on restart the session simply re-queries from offset 0
  (emails already triaged are excluded by `WHERE training_label IS NULL`)

---

## 3. TriageDecision (Per-Decision Record)

**File**: `src/TrashMailPanda/TrashMailPanda/Models/Console/TriageDecision.cs`  
**Purpose**: Captures a single triage action decision for audit and training signal storage.

```csharp
namespace TrashMailPanda.Models.Console;

public sealed record TriageDecision(
    string EmailId,
    string ChosenAction,                 // "Keep", "Archive", "Delete", "Spam"
    string? AiRecommendation,            // null in ColdStart mode
    float? ConfidenceScore,              // null in ColdStart mode
    bool IsOverride,                     // true when user chose differently from AI
    DateTime DecidedAtUtc
);
```

**Validation rules**:
- `ChosenAction` must be one of: `"Keep"`, `"Archive"`, `"Delete"`, `"Spam"`
- `IsOverride` is always `false` in `ColdStart` mode (no AI recommendation to override)
- `AiRecommendation` and `ConfidenceScore` are always both null or both non-null

---

## 4. TriageSessionSummary (End-of-Session Report)

**File**: `src/TrashMailPanda/TrashMailPanda/Models/Console/TriageSessionSummary.cs`  
**Purpose**: Aggregates session statistics for display when a triage batch completes (FR-011).

```csharp
namespace TrashMailPanda.Models.Console;

public sealed record TriageSessionSummary(
    int TotalProcessed,
    int KeepCount,
    int ArchiveCount,
    int DeleteCount,
    int SpamCount,
    int OverrideCount,
    TimeSpan Elapsed
);
```

---

## 5. KeyBinding (Help System Component)

**File**: `src/TrashMailPanda/TrashMailPanda/Models/Console/KeyBinding.cs`  
**Purpose**: Represents a single key-to-description mapping for the help panel (FR-023).

```csharp
namespace TrashMailPanda.Models.Console;

public sealed record KeyBinding(string Key, string Description);
```

---

## 6. HelpContext (Mode-Specific Help Data)

**File**: `src/TrashMailPanda/TrashMailPanda/Models/Console/HelpContext.cs`  
**Purpose**: Bundles the mode title, optional description, and key binding list for rendering
by `ConsoleHelpPanel`.

```csharp
namespace TrashMailPanda.Models.Console;

public sealed class HelpContext
{
    public required string ModeTitle { get; init; }
    public string? Description { get; init; }
    public required IReadOnlyList<KeyBinding> KeyBindings { get; init; }

    // Static factory helpers per mode (built by each console service)
    public static HelpContext ForEmailTriage(TriageMode mode) => mode == TriageMode.ColdStart
        ? new HelpContext
          {
              ModeTitle = "Email Triage — Cold Start Labeling",
              Description = "Label emails to build training data. No AI suggestions yet.",
              KeyBindings = [
                  new("K", "Keep — leave in inbox"),
                  new("A", "Archive — move out of inbox"),
                  new("D", "Delete — move to trash"),
                  new("S", "Spam — report as spam"),
                  new("Q / Esc", "Return to main menu"),
                  new("?", "Show this help panel"),
              ]
          }
        : new HelpContext
          {
              ModeTitle = "Email Triage — AI-Assisted",
              Description = "Review AI recommendations. Accept or override per email.",
              KeyBindings = [
                  new("Enter / Y", "Accept AI recommendation"),
                  new("K", "Keep — override with Keep"),
                  new("A", "Archive — override with Archive"),
                  new("D", "Delete — override with Delete"),
                  new("S", "Spam — override with Spam"),
                  new("Q / Esc", "Return to main menu"),
                  new("?", "Show this help panel"),
              ]
          };

    public static HelpContext ForMainMenu() => new()
    {
        ModeTitle = "Main Menu",
        Description = "TrashMail Panda — AI-powered Gmail triage console.",
        KeyBindings = [
            new("↑ / ↓", "Navigate menu"),
            new("Enter", "Select mode"),
            new("Q / Esc", "Exit application"),
            new("?", "Show this help panel"),
        ]
    };

    public static HelpContext ForBulkOperations() => new()
    {
        ModeTitle = "Bulk Operations",
        KeyBindings = [
            new("↑ / ↓", "Navigate options"),
            new("Enter", "Select / confirm"),
            new("Esc", "Back / cancel"),
            new("?", "Show this help panel"),
        ]
    };

    public static HelpContext ForProviderSettings() => new()
    {
        ModeTitle = "Provider Settings",
        KeyBindings = [
            new("↑ / ↓", "Navigate options"),
            new("Enter", "Select"),
            new("Esc", "Back to main menu"),
            new("?", "Show this help panel"),
        ]
    };
}
```

---

## 7. BulkOperationCriteria (Bulk Operation Filters)

**File**: `src/TrashMailPanda/TrashMailPanda/Models/Console/BulkOperationCriteria.cs`  
**Purpose**: Encapsulates user-specified filter parameters for selecting emails in bulk (FR-025).

```csharp
namespace TrashMailPanda.Models.Console;

public sealed record BulkOperationCriteria(
    string? SenderFilter,                // e.g. "@newsletter.com" or exact address
    string? LabelFilter,                 // Gmail label name
    DateTime? DateRangeStart,
    DateTime? DateRangeEnd,
    long? MaxSizeBytes,
    float? MinConfidenceThreshold,       // Only emails where AI confidence ≥ threshold
    string TargetAction                  // "Archive", "Delete", "Label"
);
```

**Validation rules**:
- At least one filter must be non-null (cannot bulk-operate on all email with no criteria)
- `TargetAction` must be one of: `"Archive"`, `"Delete"`, `"Label"`
- `MinConfidenceThreshold` ∈ [0.0, 1.0] if provided
- `DateRangeEnd` ≥ `DateRangeStart` if both provided

---

## 8. ConsoleColors (Semantic Color Markup Constants)

**File**: `src/TrashMailPanda/TrashMailPanda/Services/Console/ConsoleColors.cs`  
**Purpose**: Centralizes all Spectre.Console markup tokens so no raw markup strings appear
outside this file (FR-022, SC-007).

```csharp
namespace TrashMailPanda.Services.Console;

/// <summary>
/// Semantic Spectre.Console markup string constants for consistent coloring across
/// all console output. Use these constants instead of raw markup strings.
/// </summary>
public static class ConsoleColors
{
    // ── Semantic tokens ─────────────────────────────────────────────────────

    /// <summary>Bold red prefix for error indicators (✗).</summary>
    public const string Error = "[bold red]";

    /// <summary>Red for error body text.</summary>
    public const string ErrorText = "[red]";

    /// <summary>Green for success confirmations.</summary>
    public const string Success = "[green]";

    /// <summary>Yellow for warnings and advisories.</summary>
    public const string Warning = "[yellow]";

    /// <summary>Blue for informational messages.</summary>
    public const string Info = "[blue]";

    /// <summary>Magenta for training metric values (precision, recall, F1).</summary>
    public const string Metric = "[magenta]";

    /// <summary>Cyan for highlights, prompts, and action hints.</summary>
    public const string Highlight = "[cyan]";

    /// <summary>Cyan for key binding hints (same as Highlight; semantic alias).</summary>
    public const string ActionHint = "[cyan]";

    /// <summary>Dim for secondary/supporting text.</summary>
    public const string Dim = "[dim]";

    /// <summary>Closes any open markup tag.</summary>
    public const string Close = "[/]";

    // ── Composite helpers ────────────────────────────────────────────────────

    /// <summary>Bold/italic for AI recommendation display.</summary>
    public const string AiRecommendation = "[bold cyan]";

    /// <summary>Bold yellow for threshold prompt options.</summary>
    public const string PromptOption = "[bold yellow]";
}
```

**Usage pattern** (from research.md §7):
```csharp
_console.MarkupLine(
    $"{ConsoleColors.Error}✗{ConsoleColors.Close} {ConsoleColors.ErrorText}{Markup.Escape(message)}{ConsoleColors.Close}");
```

---

## New Method: IEmailArchiveService.SetTrainingLabelAsync

**File**: `src/Providers/Storage/TrashMailPanda.Providers.Storage/IEmailArchiveService.cs` (addition)  
**Purpose**: Atomically sets `training_label` and optionally `UserCorrected = 1` on an existing
`email_features` row. Called by the triage service ONLY after a successful Gmail action.

```csharp
// New methods to add to IEmailArchiveService

/// <summary>
/// Sets the explicit training label for the given email's feature vector.
/// Also sets UserCorrected = 1 when the user overrode an AI recommendation.
/// No-op if no feature vector exists for the email ID (returns Success(false)).
/// MUST only be called after the corresponding Gmail action has succeeded.
/// </summary>
Task<Result<bool>> SetTrainingLabelAsync(
    string emailId,
    string label,              // "Keep", "Archive", "Delete", "Spam"
    bool userCorrected,        // true when user overrode AI recommendation
    CancellationToken ct = default);

/// <summary>
/// Returns the count of feature vectors with an explicit training label.
/// Used to seed EmailTriageSession.LabeledCount at session start.
/// </summary>
Task<Result<int>> CountLabeledAsync(CancellationToken ct = default);

/// <summary>
/// Returns a page of feature vectors with training_label IS NULL (untriaged queue).
/// Ordered by ExtractedAt descending (most recently scanned first).
/// </summary>
Task<Result<IReadOnlyList<EmailFeatureVector>>> GetUntriagedAsync(
    int pageSize,
    int offset,
    CancellationToken ct = default);
```

---

## Entity Relationships

```
EmailFeatureVector (email_features table — MODIFIED)
  └── training_label IS NULL  → untriaged queue (source for triage session)
  └── training_label NOT NULL → explicit user decision (ground truth for ML training)
  └── UserCorrected = 1       → user overrode AI recommendation (retention + retrain signal)
  └── EmailId                 → maps to EmailArchiveEntry.EmailId (optional snippet enrichment)

EmailTriageSession (in-memory, per triage session)
  └── LabeledCount sourced from: COUNT(*) WHERE training_label IS NOT NULL (at session start)
  └── LabelingThreshold sourced from MLModelProviderConfig.MinTrainingSamples
  └── CurrentOffset for local DB paging of untriaged queue

TriageDecision (ephemeral per-decision, not persisted directly)
  └── On IsOverride = true: calls SetTrainingLabelAsync(emailId, label, userCorrected: true)
  └── On IsOverride = false: calls SetTrainingLabelAsync(emailId, label, userCorrected: false)
  └── Gmail action MUST succeed before SetTrainingLabelAsync is called

BulkOperationCriteria (ephemeral; built from user prompts, not persisted)
  └── Passed to IEmailProvider.ListAsync as ListOptions
```

---

## Database Migration Summary

**Migration name**: `AddTrainingLabelToEmailFeatures`  
**Migration number**: Next sequential migration in the project  
**Changes**:
```sql
-- Add nullable training_label column to existing email_features table
ALTER TABLE email_features ADD COLUMN training_label TEXT NULL;
```

**Rollback**: SQLite does not support `DROP COLUMN` natively; rollback requires table recreation
or simply treating the column as unused. Migrations are append-only in this project.

**Data preservation**: All existing `email_features` rows retain their data unmodified.
`training_label` defaults to `NULL` for all existing rows (correct: they are all "untriaged"
until explicitly labeled by a triage session).
