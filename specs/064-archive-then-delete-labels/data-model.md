# Data Model: Archive-Then-Delete Training Labels (064)

**Phase 1 output** | **Branch**: `064-archive-then-delete-labels`

---

## Entities

### 1. `LabelThresholds` (new — static helper)

**Location**: `src/Shared/TrashMailPanda.Shared/Labels/LabelThresholds.cs`  
**Type**: Static class

| Member | Type | Value | Description |
|---|---|---|---|
| `Archive30d` | `const string` | `"Archive for 30d"` | Label key |
| `Archive1y` | `const string` | `"Archive for 1y"` | Label key |
| `Archive5y` | `const string` | `"Archive for 5y"` | Label key |
| `ThresholdsByLabel` | `IReadOnlyDictionary<string, int>` | See below | Lookup: label → threshold days |
| `TimeBoundedLabels` | `IReadOnlySet<string>` | `{Archive30d, Archive1y, Archive5y}` | Convenience set for `Contains` checks |
| `TryGetThreshold(string, out int)` | `bool` | — | Returns threshold days for a time-bounded label; false for non-time-bounded labels |
| `IsTimeBounded(string)` | `bool` | — | Returns true iff label is in `TimeBoundedLabels` |

**Threshold map**:

| Label | Days |
|---|---|
| `Archive for 30d` | 30 |
| `Archive for 1y` | 365 |
| `Archive for 5y` | 1825 |

**Validation rules**:
- Constants are the authoritative values; all three consuming sites (`EmailTriageService`, `BulkOperationService`, `RetentionEnforcementService`) import from this class — no local copies.

---

### 2. `RetentionScanResult` (new — result record)

**Location**: `src/TrashMailPanda/TrashMailPanda/Models/RetentionScanResult.cs`  
**Type**: `readonly record struct`

| Field | Type | Nullable | Description |
|---|---|---|---|
| `ScannedCount` | `int` | no | Total emails examined (had a time-bounded label and `is_archived = 1`) |
| `DeletedCount` | `int` | no | Emails successfully deleted from Gmail (age ≥ threshold) |
| `SkippedCount` | `int` | no | Emails under threshold at scan time (not yet expired) |
| `FailedIds` | `IReadOnlyList<string>` | no | Gmail IDs where delete failed |
| `RanAtUtc` | `DateTime` | no | UTC timestamp when the scan completed |

**Derived properties**:
- `bool HasFailures` → `FailedIds.Count > 0`
- `bool AnyDeleted` → `DeletedCount > 0`

**Validation rules**:
- `ScannedCount == DeletedCount + SkippedCount + FailedIds.Count` (invariant)
- `RanAtUtc` must be in UTC

---

### 3. `IRetentionEnforcementService` (new — interface)

**Location**: `src/TrashMailPanda/TrashMailPanda/Services/IRetentionEnforcementService.cs`  
**Type**: Interface

| Method | Returns | Description |
|---|---|---|
| `RunScanAsync(CancellationToken)` | `Task<Result<RetentionScanResult>>` | Executes a full retention scan; persists `last_scan_utc` on success |
| `GetLastScanTimeAsync(CancellationToken)` | `Task<Result<DateTime?>>` | Reads `last_scan_utc` from config; returns `null` if never run |
| `ShouldPromptAsync(CancellationToken)` | `Task<Result<bool>>` | Returns true if elapsed since last scan ≥ `PromptThresholdDays` |

---

### 4. `RetentionEnforcementService` (new — implementation)

**Location**: `src/TrashMailPanda/TrashMailPanda/Services/RetentionEnforcementService.cs`  
**Type**: `class` implementing `IRetentionEnforcementService`

**Constructor dependencies** (DI-injected):
- `IEmailArchiveService _archiveService` — queries time-bounded archived feature vectors
- `IEmailProvider _emailProvider` — executes Gmail deletes
- `IConfigurationService _configService` — reads/writes `last_scan_utc`
- `IOptions<RetentionEnforcementOptions> _options` — configurable thresholds/intervals
- `ILogger<RetentionEnforcementService> _logger`

**Scan algorithm** (in `RunScanAsync`):
1. Fetch all feature vectors where `training_label IN ('Archive for 30d', 'Archive for 1y', 'Archive for 5y')` AND `is_archived = 1`
2. For each vector with a non-null `ReceivedDateUtc`:
   a. Compute `elapsedDays = (DateTime.UtcNow - ReceivedDateUtc.Value).TotalDays`
   b. If `LabelThresholds.TryGetThreshold(TrainingLabel, out var threshold)` AND `elapsedDays >= threshold` → enqueue for delete
3. For each enqueued email ID: call `IEmailProvider.BatchModifyAsync` with `AddLabelIds = ["TRASH"], RemoveLabelIds = ["INBOX"]`
4. On per-email failure: log warning, add to `FailedIds`, continue
5. Do **NOT** call `SetTrainingLabelAsync` at any point — `training_label` is never modified
6. Persist `last_scan_utc = DateTime.UtcNow` to config on completion (even partial success)
7. Return `RetentionScanResult`

---

### 5. `RetentionEnforcementOptions` (new — configuration POCO)

**Location**: `src/TrashMailPanda/TrashMailPanda/Models/RetentionEnforcementOptions.cs`  
**Type**: `class` (used with `IOptions<T>`)

| Property | Type | Default | Description |
|---|---|---|---|
| `ScanIntervalDays` | `int` | `30` | Target interval between scans (one calendar month) |
| `PromptThresholdDays` | `int` | `7` | Elapsed days since last scan before startup prompt appears |

**Validation**: `ScanIntervalDays >= 1`; `PromptThresholdDays >= 1`; `PromptThresholdDays <= ScanIntervalDays`

---

### 6. `RetentionStartupCheck` (new — startup step)

**Location**: `src/TrashMailPanda/TrashMailPanda/Startup/RetentionStartupCheck.cs`  
**Type**: `class`

**Responsibility**: Called during the console startup sequence. Checks `IRetentionEnforcementService.ShouldPromptAsync()`; if `true`, renders a Spectre.Console confirmation prompt and, if the user confirms, invokes `RunScanAsync`.

**Prompt template**:
```
[yellow]⚠  Retention scan last ran X days ago (threshold: Y days).[/]
[cyan]→[/] Scan archived emails and delete any past their retention window? [Y/n]
```

---

## Modified Entities

### 7. `ProcessingSettings` (modify — add retention settings)

**Location**: `src/Shared/TrashMailPanda.Shared/ProcessingSettings.cs`

**Addition** (new property):

```csharp
/// Retention enforcement settings: scan interval and prompt threshold.
/// Persisted with the rest of ProcessingSettings in the app_config KV table.
public RetentionSettings Retention { get; set; } = new();
```

**New nested DTO** `RetentionSettings` (separate file: `src/Shared/TrashMailPanda.Shared/RetentionSettings.cs`):

| Property | Type | Default |
|---|---|---|
| `ScanIntervalDays` | `int` | `30` |
| `PromptThresholdDays` | `int` | `7` |

> **Note**: `RetentionEnforcementOptions` is populated from this value via `IOptions<T>` binding in DI registration. No duplicate source of truth — `appsettings.json` overrides are also supported.

---

## Existing Entities (no schema change)

### `EmailFeatureVector` — relevant fields

| Column | Type | Role in this feature |
|---|---|---|
| `email_id` | `TEXT PK` | Join key for Gmail delete |
| `training_label` | `TEXT?` | Content-based label; **NEVER written by retention scan** |
| `is_archived` | `INT` | Filter: scan only queries `is_archived = 1` |
| `received_date_utc` | `TEXT (DateTime?)` | Source of truth for age computation; indexed |
| `email_age_days` | `INT` | Age at feature-store time (= decision time); used for training only — not used for threshold checks |

**No migration required.** All columns exist in schema v5.

---

## Config Table (no schema change)

The existing `app_config` key-value table (managed by `IConfigurationService` via `AppConfig` / `ProcessingSettings` JSON blob) gains one new field through `RetentionSettings`:

| Logical key | Stored in | Description |
|---|---|---|
| `last_scan_utc` | `app_config` JSON blob via `RetentionSettings` → `ProcessingSettings` | UTC datetime of last completed scan; `null` = never run |

---

## State Transitions

```
EmailFeatureVector.training_label = "Archive for 30d"
EmailFeatureVector.is_archived = 1
EmailFeatureVector.received_date_utc = T₀

On action execution (triage/bulk):
  elapsedDays = (UtcNow - T₀).Days
  if elapsedDays < 30  → Gmail: Archive (remove INBOX)
  if elapsedDays >= 30 → Gmail: Delete (add TRASH, remove INBOX)
  training_label stays "Archive for 30d" in both cases

On retention scan:
  same threshold check
  if elapsedDays >= 30 → Gmail: Delete (add TRASH)
  training_label is NEVER modified
```
