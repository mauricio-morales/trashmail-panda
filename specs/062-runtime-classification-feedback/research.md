# Research: Runtime Classification with User Feedback Loop

**Feature**: 062-runtime-classification-feedback  
**Date**: 2026-03-21

## R1: What existing infrastructure already covers spec 062 requirements?

### Decision: Significant existing infrastructure — scope only the gaps

### Rationale

Thorough codebase exploration revealed that ~60% of the spec's underlying infrastructure is already built. The remaining work is concentrated in four new service areas (auto-apply, quality monitoring, bootstrap Starred/Important signals, review/undo) and their console UI integration.

### Existing Infrastructure (REUSE AS-IS)

| Component | Location | Covers |
|-----------|----------|--------|
| `IClassificationService` + `ClassificationService` | `Services/` | ML inference with confidence scores (single + batch) |
| `IEmailTriageService` + `EmailTriageService` | `Services/` | Fetch → recommend → dual-write (Gmail action + training label) |
| `EmailTriageConsoleService` | `Services/Console/` | Cold-start + AI-assisted triage loop with confidence color coding |
| `ITrainingSignalAssigner` + `TrainingSignalAssigner` | `Providers/Email/Services/` | 8-rule Trash→Delete, Spam→AutoDelete signal inference |
| `GmailTrainingDataService` | `Providers/Email/Services/` | Full initial+incremental scan with resumable progress, rate limiting |
| `IncrementalUpdateService` | `Providers/ML/Training/` | Retrain trigger at ≥50 corrections |
| `ModelTrainingPipeline` | `Providers/ML/Training/` | Full training, incremental training, readiness checks |
| `IEmailArchiveService` | `Providers/Storage/` | `SetTrainingLabelAsync`, `CountLabeledAsync`, feature vector storage |
| `EmailFeatureVector` | `Providers/Storage/Models/` | 38 features + `TrainingLabel` + `UserCorrected` + `IsStarred` + `IsImportant` |
| `ScanProgressEntity` + `IScanProgressRepository` | `Providers/Storage/` | Per-folder scan cursors, checkpoint/resume, idempotency |
| `IApplicationOrchestrator` + events | `Services/` + `Models/` | Event-driven architecture for UI updates |
| `EmailTriageSession` | `Models/Console/` | In-memory session state with action counts, override tracking |
| `TriageDecision` | `Models/Console/` | Per-email decision record with override flag |
| `TriageSessionSummary` | `Models/Console/` | End-of-session aggregate stats |
| `MLModelProviderConfig` | `Providers/ML/Config/` | Model config with quality advisory threshold (0.70 F1) |

### Confirmed Gaps (IMPLEMENT IN THIS SPEC)

1. **Auto-apply service + config** — No confidence threshold comparison or auto-apply decision logic exists
2. **Auto-apply session log** — No ephemeral tracking of which emails were auto-applied
3. **Quality monitoring service** — No rolling accuracy, per-action metrics, or proactive warnings
4. **Bootstrap Starred/Important → Keep** — `TrainingSignalAssigner` only handles folder-based signals; `IsStarred` and `IsImportant` are extracted as features but never converted to training labels
5. **Console UI extensions** — No auto-apply flow, quality warnings, or review/undo UI
6. **Gmail action reversal** — No undo capability for auto-applied actions

### Alternatives Considered

- **Inline all logic in EmailTriageConsoleService**: Rejected — violates separation of concerns and makes the triage loop untestable
- **Persist auto-apply session log to DB**: Rejected — spec says ephemeral (in-memory only), and persisting creates unnecessary schema changes

---

## R2: Where should auto-apply configuration live?

### Decision: New `AutoApplyConfig` nested under `ProcessingSettings`, persisted via existing `IConfigurationService` / `app_config` SQLite KV table

### Rationale

The codebase already has a clean separation:
- **`ISecureStorageManager`** — encrypted OS keychain for secrets (tokens, API keys). NOT appropriate for simple user preferences.
- **`AppConfig` + `IConfigurationService`** — simple `app_config` SQLite KV table for user settings (`ConnectionState`, `ProcessingSettings`, `UISettings`). Each group is JSON-serialized into one row.

Auto-apply is a user-facing triage preference (enabled flag + confidence threshold). It belongs in `ProcessingSettings` alongside existing settings like `BatchSize` and `AutoProcessNewEmails`. This:
- Reuses the existing persistence pattern (no new storage mechanism)
- Keeps secrets in keychain and preferences in SQLite where they belong
- Auto-apply settings persist across sessions (FR-023) via `IConfigurationService.UpdateProcessingSettingsAsync`

### Config Shape

Extend `ProcessingSettings` with auto-apply fields:

```csharp
// In ProcessingSettings.cs (existing file)
public bool AutoApplyEnabled { get; set; } = false;  // FR-002: disabled by default

[Range(0.50, 1.00)]
public float AutoApplyConfidenceThreshold { get; set; } = 0.95f;  // FR-001: 95% default
```

Alternatively, a nested `AutoApplyConfig` record on `ProcessingSettings`:

```csharp
public sealed class AutoApplyConfig
{
    public bool Enabled { get; set; } = false;
    [Range(0.50, 1.00)]
    public float ConfidenceThreshold { get; set; } = 0.95f;
}

// In ProcessingSettings:
public AutoApplyConfig AutoApply { get; set; } = new();
```

### Alternatives Considered

- **Store in `ISecureStorageManager`**: Rejected — that's for encrypted secrets (tokens, API keys), not simple user preferences. Over-secured and wrong abstraction.
- **Add to `MLModelProviderConfig`**: Rejected — auto-apply is a user preference, not a model parameter
- **Store in `appsettings.json`**: Rejected — user modifies at runtime; needs persistence in SQLite
- **New standalone config table**: Rejected — `app_config` KV table already exists and handles this pattern

---

## R3: How should auto-apply integrate into the existing triage loop?

### Decision: New `IAutoApplyService` injected into `EmailTriageConsoleService`, evaluated per-email before user prompt

### Rationale

The triage loop in `EmailTriageConsoleService` currently:
1. Fetches a batch of untriaged emails
2. For each email: fetches AI recommendation → displays to user → waits for keystroke → applies decision

Auto-apply inserts a decision point after step 2's AI recommendation:
- If confidence ≥ threshold AND auto-apply enabled → auto-apply (skip user prompt)
- Special case: if recommended action matches current email state (e.g., "Archive" for already-archived) → skip Gmail API call but still store training label
- Track auto-applied decisions in session log
- Show batch summary at end

The `IAutoApplyService` owns the decision logic; the console service calls it and either auto-applies or presents for manual review. This keeps the service testable independently.

### Alternatives Considered

- **Batch-level auto-apply (process all at once)**: Rejected — per-email evaluation is needed for the "matches current state" logic
- **Separate auto-apply command/mode**: Rejected — auto-apply should be transparent within the existing triage mode

---

## R4: How should quality monitoring track metrics?

### Decision: In-memory rolling window in `IModelQualityMonitor`, seeded from DB at session start

### Rationale

Quality metrics need two data sources:
1. **Rolling accuracy** (last N=100 decisions): In-memory during session, derived from `TriageDecision.IsOverride`
2. **Per-action metrics**: Aggregated from all `email_features WHERE user_corrected = 1` in the DB

The monitor calculates metrics on-demand (not continuously), triggered at:
- Start of each new batch (check if warning needed)
- End of session (for summary display)
- When user requests stats explicitly

This avoids background timers and keeps the system simple. The existing `EmailTriageSession` already tracks `SessionOverrideCount` — we extend it to track per-action overrides.

### Alternatives Considered

- **Persistent metrics table**: Rejected — over-engineering; metrics can be computed from existing data
- **Background monitoring thread**: Rejected — unnecessary complexity; batch-boundary checks are sufficient

---

## R5: What's the best approach for the bootstrap Starred/Important gap?

### Decision: Add a post-scan label inference step in `GmailTrainingDataService` that converts IsStarred/IsImportant features to "Keep" training labels, and run the same SQL as a migration for existing installs

### Rationale

The current flow:
1. `GmailTrainingDataService.RunInitialScanAsync()` scans folders and stores features
2. `TrainingSignalAssigner` assigns signals per folder (Trash→Delete, Spam→AutoDelete, etc.)
3. Features are stored with `IsStarred` and `IsImportant` columns populated

Gap: No step converts `IsStarred=1` or `IsImportant=1` to `TrainingLabel = "Keep"`.

Solution: Add a post-scan step in the bootstrap flow that runs:
```sql
UPDATE email_features
SET training_label = 'Keep', user_corrected = 0
WHERE (is_starred = 1 OR is_important = 1)
  AND training_label IS NULL;
```

**Migration for existing installs:**
Because the initial scan may have already run for current users, this SQL must also be executed as a one-time migration step to ensure all existing emails with Starred/Important are labeled correctly. Integrate this into the next database migration or provide a one-off script for users to run after updating.

This preserves:
- Existing `TrainingSignalAssigner` logic (no modification needed)
- User corrections (only sets label if currently NULL)
- Archive exclusion (FR-008: archived emails without Starred/Important remain unlabeled)

### Alternatives Considered

- **Modify TrainingSignalAssigner 8-rule table**: Rejected — the assigner is folder-based; Starred/Important are cross-folder attributes
- **New pre-processing filter in scan loop**: Rejected — post-scan SQL is simpler and idempotent

---

## R6: How should undo work for auto-applied Gmail actions?

### Decision: Undo reverses the Gmail action via existing `IEmailProvider` batch modify, then stores correction

### Rationale

Auto-apply executes Gmail actions through the existing dual-write pattern in `IEmailTriageService.ApplyDecisionAsync`. Undo needs to:

1. Reverse the Gmail action:
   - Delete → move back to Inbox (`BatchModifyAsync` add INBOX, remove TRASH)
   - Archive → move back to Inbox (`BatchModifyAsync` add INBOX)
   - Spam → move back to Inbox (`BatchModifyAsync` add INBOX, remove SPAM)
   - Keep → no reversal needed (email stays in Inbox)
2. Update training label to the user's corrected action
3. Mark as `UserCorrected = 1` (high-value correction)

The existing `IEmailProvider` already has `BatchModifyAsync` for label manipulation, so no new Gmail API integration is needed.

Session log entries are ephemeral (in-memory). Once the session ends, the undo window closes. This is acceptable because:
- Gmail Trash has a 30-day grace period
- Users can always manually re-triage in the next session

### Alternatives Considered

- **Persistent undo log**: Rejected — spec explicitly says session-level ephemeral
- **Gmail undo API**: Doesn't exist; label manipulation is the correct approach
