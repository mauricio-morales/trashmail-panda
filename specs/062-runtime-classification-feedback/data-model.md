# Data Model: Runtime Classification with User Feedback Loop

**Feature**: 062-runtime-classification-feedback  
**Date**: 2026-03-21

## Existing Entities (No Changes Needed)

### EmailFeatureVector (email_features table)
Already has all required columns:
- `TrainingLabel` (string?) — set by triage decisions
- `UserCorrected` (int 0|1) — marks user overrides
- `IsStarred` (int 0|1) — used for bootstrap Keep inference
- `IsImportant` (int 0|1) — used for bootstrap Keep inference
- `IsArchived` (int 0|1) — state detection for auto-apply
- `IsInInbox` (int 0|1) — state detection for auto-apply
- `WasInTrash` (int 0|1) — state detection for auto-apply
- 38 total feature columns

### TriageDecision (existing record)
```csharp
record TriageDecision(
    string EmailId,
    string ChosenAction,
    string? AiRecommendation,
    float? ConfidenceScore,
    bool IsOverride,
    DateTime DecidedAtUtc
);
```
No changes needed — already captures all decision metadata.

### EmailTriageSession (existing class)
In-memory session state. Extend with auto-apply tracking (see below).

### TriageSessionSummary (existing record)
Extend with auto-apply counts (see below).

---

## New Entities

### 1. AutoApplyConfig

**Purpose**: User-configurable auto-apply settings, persisted across sessions.

| Field | Type | Default | Validation | Description |
|-------|------|---------|------------|-------------|
| `Enabled` | `bool` | `false` | — | FR-002: disabled by default |
| `ConfidenceThreshold` | `float` | `0.95f` | `[Range(0.50, 1.00)]` | FR-001: minimum confidence for auto-apply |

**Storage**: Persisted as a nested object on `ProcessingSettings` via `IConfigurationService.UpdateProcessingSettingsAsync()`. Stored in the `app_config` SQLite KV table as JSON under the `"ProcessingSettings"` key. **NOT** in `ISecureStorageManager` — that's reserved for encrypted secrets (tokens, API keys).

**State transitions**:
- `Enabled: false` → User explicitly enables → `Enabled: true`
- `Enabled: true` → Accuracy drops below 50% → `Enabled: false` (FR-005, FR-025)
- `Enabled: true` → User explicitly disables → `Enabled: false`

---

### 2. AutoApplyLogEntry

**Purpose**: Ephemeral per-session record of an auto-applied decision. Not persisted to DB.

| Field | Type | Nullable | Description |
|-------|------|----------|-------------|
| `EmailId` | `string` | No | Email that was auto-applied |
| `SenderDomain` | `string` | No | For display in review list |
| `Subject` | `string` | No | For display in review list |
| `AppliedAction` | `string` | No | "Keep", "Archive", "Delete", "Spam" |
| `Confidence` | `float` | No | Model confidence score |
| `AppliedAtUtc` | `DateTime` | No | When auto-applied |
| `WasRedundant` | `bool` | No | True if action matched email's current state (FR-024) |
| `Undone` | `bool` | No | True if user subsequently undid this decision |
| `UndoneToAction` | `string?` | Yes | Action the user corrected to (if undone) |

---

### 3. AutoApplySessionSummary

**Purpose**: Aggregate auto-apply stats for end-of-session display (FR-003).

| Field | Type | Description |
|-------|------|-------------|
| `TotalAutoApplied` | `int` | Total emails auto-applied this session |
| `TotalManuallyReviewed` | `int` | Total emails presented for manual confirmation |
| `TotalRedundant` | `int` | Auto-applied where action matched current state |
| `TotalUndone` | `int` | Auto-applied decisions subsequently undone |
| `PerActionCounts` | `Dictionary<string, int>` | Auto-apply count per action category |

---

### 4. ModelQualityMetrics

**Purpose**: Snapshot of model performance derived from user decisions.

| Field | Type | Description |
|-------|------|-------------|
| `OverallAccuracy` | `float` | Fraction of AI recommendations accepted (not overridden) |
| `RollingAccuracy` | `float` | Accuracy over last N decisions (default N=100) |
| `RollingWindowSize` | `int` | Current window size (may be <100 early in session) |
| `TotalDecisions` | `int` | Total AI-assisted decisions tracked |
| `TotalCorrections` | `int` | Total user overrides |
| `CorrectionsSinceLastTraining` | `int` | Corrections since last model training date |
| `PerActionMetrics` | `Dictionary<string, ActionCategoryMetrics>` | Per-action breakdown |
| `CalculatedAtUtc` | `DateTime` | When metrics were computed |

---

### 5. ActionCategoryMetrics

**Purpose**: Per-action performance breakdown (FR-015, FR-016).

| Field | Type | Description |
|-------|------|-------------|
| `Action` | `string` | "Keep", "Archive", "Delete", "Spam" |
| `TotalRecommended` | `int` | Times model recommended this action |
| `TotalAccepted` | `int` | Times user accepted this recommendation |
| `CorrectionRate` | `float` | Fraction of recommendations overridden |
| `CorrectedTo` | `Dictionary<string, int>` | Confusion: what users corrected this to (FR-016) |

---

### 6. QualityWarning

**Purpose**: Proactive warning to surface to user when quality degrades (FR-014, FR-025, FR-026).

| Field | Type | Description |
|-------|------|-------------|
| `Severity` | `QualityWarningSeverity` | `Info`, `Warning`, `Critical` |
| `Message` | `string` | Human-readable warning text |
| `RollingAccuracy` | `float` | Current accuracy |
| `CorrectionsSinceTraining` | `int` | Correction count |
| `RecommendedAction` | `string` | "Retrain now", "Review problem categories", etc. |
| `ProblematicActions` | `List<string>?` | Actions with >40% correction rate (FR-026) |
| `AutoApplyDisabled` | `bool` | True if auto-apply was auto-disabled (FR-025) |

**Severity thresholds**:
- `Info`: ≥50 corrections since training → suggest retrain (FR-013)
- `Warning`: Rolling accuracy <70% (FR-014)
- `Critical`: Rolling accuracy <50% → auto-disable auto-apply (FR-025)

---

## Extended Entities (Minimal Changes to Existing Types)

### EmailTriageSession — Add Fields

| New Field | Type | Default | Description |
|-----------|------|---------|-------------|
| `AutoApplyLog` | `List<AutoApplyLogEntry>` | `[]` | Ephemeral session log (FR-017) |
| `AutoAppliedCount` | `int` | `0` | Running count of auto-applied emails |
| `RollingDecisions` | `Queue<(string predicted, string actual, bool isOverride)>` | `Queue(100)` | Sliding window for rolling accuracy |

### TriageSessionSummary — Add Fields

| New Field | Type | Description |
|-----------|------|-------------|
| `AutoAppliedCount` | `int` | FR-003: count of auto-applied actions |
| `ManuallyReviewedCount` | `int` | FR-003: count of manually reviewed |

---

## Relationships

```
AutoApplyConfig ─── persisted via ──→ IConfigurationService (app_config SQLite KV table)
       │
       │ read at session start
       ▼
EmailTriageSession ◆── AutoApplyLog ──→ List<AutoApplyLogEntry>
       │                                        │
       │ per-email decision                     │ undo reverses
       ▼                                        ▼
IEmailTriageService.ApplyDecisionAsync    IEmailProvider.BatchModifyAsync
       │                                        │
       │ stores                                 │ reverses Gmail state
       ▼                                        │
EmailFeatureVector.TrainingLabel ◄──────────────┘
       │
       │ aggregated by
       ▼
IModelQualityMonitor ──→ ModelQualityMetrics
       │                        │
       │ triggers               │ per-action
       ▼                        ▼
QualityWarning           ActionCategoryMetrics
```
