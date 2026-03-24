# Research: Archive-Then-Delete Training Labels (064)

**Phase 0 output** | **Branch**: `064-archive-then-delete-labels`

## Research Questions Resolved

All NEEDS CLARIFICATION items from Technical Context are resolved here.

---

### Q1: Does `received_date_utc` already exist in the `email_features` schema?

**Decision**: Yes — it exists and is indexed.

**Evidence**:
- `EmailFeatureVector.ReceivedDateUtc` is `[Column("received_date_utc")]` of type `DateTime?` in
  `src/Providers/Storage/TrashMailPanda.Providers.Storage/Models/EmailFeatureVector.cs`
- Migration `20260321201502_AddReceivedDateUtcToEmailFeatures` added the column and
  created `idx_email_features_received_date_utc`
- The latest snapshot (`TrashMailPandaDbContextModelSnapshot.cs`) confirms the column is present

**Rationale**: No schema migration required for feature #064. Querying by
`received_date_utc` for the retention scan is already efficient.

---

### Q2: What is the exact threshold-to-label mapping to codify?

**Decision**:

| Label (string) | Threshold (days) |
|---|---|
| `Archive for 30d` | 30 |
| `Archive for 1y` | 365 |
| `Archive for 5y` | 1825 |

**Evidence**: Defined in spec.md table (§Overview). Labels confirmed present in
`EmailTriageConsoleService.cs` (lines 677–681) and `EmailTriageService.cs` (lines 269–271).

**Implementation**: A new static `LabelThresholds` class in `TrashMailPanda.Shared/Labels/`
exposes a `TryGetThreshold(string label, out int days)` helper. Three consuming sites share it:
`EmailTriageService`, `BulkOperationService`, and `RetentionEnforcementService`.

---

### Q3: How should age-at-execution routing be implemented in `ExecuteGmailActionAsync`?

**Decision**: Add a `received_date_utc` parameter (as `DateTime?`) to the private
`ExecuteGmailActionAsync` helpers in both `EmailTriageService` and `BulkOperationService`.
When the action is one of the three time-bounded labels:
1. Compute `currentAgeDays = (DateTime.UtcNow - receivedDate).TotalDays`
2. If `currentAgeDays >= threshold` → call `ApplyDeleteAsync`; else call `ApplyArchiveAsync`
3. Return the Gmail result to the caller

The `training_label` is **always** the content-based label passed by the caller — it is set by
`SetTrainingLabelAsync` in the caller's code and is independent of which physical action ran.

**Alternatives considered**:
- *Wrap at the call site* (pass age from the console service): Rejected — the action service
  already owns the mapping logic; spreading threshold logic into the UI layer violates separation.
- *Always delete and re-archive at enforcement time only*: Rejected — emails over threshold would
  remain in Archive until the next retention scan; the spec requires immediate Delete at triage time.

**Where to get `ReceivedDateUtc` in `EmailTriageService`**:
- `ApplyDecisionAsync` already receives `emailId`; the feature vector is already loaded upstream
  by the triage queue. Caller passes `ReceivedDateUtc` via a new overload (or it is passed from
  the feature record that is already available in `EmailTriageConsoleService`).
- Simplest path: add `DateTime? receivedDateUtc = null` to `ApplyDecisionAsync` signature and
  thread it through to `ExecuteGmailActionAsync`.

---

### Q4: Does `BulkOperationService.ExecuteGmailActionAsync` handle time-bounded labels today?

**Decision**: No — it falls through to the `_` (unknown action) failure branch.

**Evidence**: `BulkOperationService` lines ~157–173 only handle `Keep`, `Archive`, `Delete`,
`Spam`. The time-bounded labels are missing, so any bulk operation with those labels currently
returns `ValidationError("Unknown bulk action")`.

**Implementation**: Add the three time-bounded cases to `BulkOperationService.ExecuteGmailActionAsync`
using the same age-at-execution logic. The caller (`BulkOperationService.ExecuteAsync`) already
has the feature vector in scope (it iterates `IEnumerable<EmailFeatureVector>`), so
`receivedDateUtc` can be read from there.

---

### Q5: Where should `IRetentionEnforcementService` and its background service live?

**Decision**: Both go in `src/TrashMailPanda/TrashMailPanda/Services/`.

**Rationale**:
- This mirrors the placement of other services (`EmailTriageService`, `BulkOperationService`,
  `ProviderHealthMonitorService`).
- It is a console-app concern, not a provider concern, so it does not belong in
  `src/Providers/`.
- The background service requires `IHostedService`/`BackgroundService`, which is only referenced
  from the main app host.

---

### Q6: What query does the retention scan execute?

**Decision**: The scan selects from `email_features` WHERE:
```sql
training_label IN ('Archive for 30d', 'Archive for 1y', 'Archive for 5y')
AND is_archived = 1
AND received_date_utc IS NOT NULL
AND received_date_utc <= :cutoffDate   -- cutoff = NOW() - threshold_days
```
Each label is queried separately so the threshold is applied per-label. Alternatively, fetch all
three sets in a single pass and compute threshold in C#.

**Preferred approach**: Single query with `IN` filter, then compute age in C#:
```csharp
var elapsed = (DateTime.UtcNow - feature.ReceivedDateUtc.Value).TotalDays;
var threshold = LabelThresholds.Get(feature.TrainingLabel!);
if (elapsed >= threshold) → enqueue for delete
```
This avoids any dynamic SQL parameter count issues and keeps the threshold logic in one place.

**Alternatives considered**:
- *Add a new `IEmailArchiveService` method* (e.g., `GetExpiredRetentionFeaturesAsync`): Considered
  and deferred. The scan can use the existing `GetAllFeaturesAsync` filtered to time-bounded labels
  for the MVP. If performance warrants it (> 10k archived emails), a dedicated query can be added
  to `IEmailArchiveService` in a follow-up.
- *EF Core LINQ expression*: Considered; current `IEmailArchiveService` wraps raw SQL patterns.
  The scan will use `GetAllFeaturesAsync` + in-memory filter for simplicity; the index on
  `received_date_utc` ensures the fetch is fast.

---

### Q7: How should the retention scan handle per-email Gmail failures?

**Decision**: Continue processing remaining emails on individual failure; accumulate failed IDs;
surface a `RetentionScanResult` with counts and failure list; do NOT modify `training_label` for
any email (success or failure).

**Rationale**: Matches the existing pattern in `BulkOperationService.ExecuteAsync` (accumulates
`failedIds`, logs warnings, returns a result with success/fail counts). Consistent error handling
across the codebase.

---

### Q8: What is the retention scan trigger / schedule?

**Decision**: The scan is user-prompted at startup, not a continuous background timer.

**Design**:
- `RetentionEnforcementService` persists `last_scan_utc` to a config key in the SQLite
  `config` table (key: `retention_last_scan_utc`).
- On app start, `ConsoleStartupOrchestrator` (or a startup step) checks the elapsed time since
  `last_scan_utc`. Two configurable thresholds govern behaviour:

  | Config Key | Default | Meaning |
  |---|---|---|
  | `RetentionEnforcement:ScanIntervalDays` | `30` | Target interval between scans (monthly) |
  | `RetentionEnforcement:PromptThresholdDays` | `7` | If elapsed ≥ this, prompt user at startup |

- If elapsed ≥ `PromptThresholdDays` (default 7 days), the startup sequence shows a
  Spectre.Console confirmation prompt:
  ```
  [yellow]⚠  Retention scan last ran X days ago.[/]
  [cyan]→[/] Run archive retention scan now? [Y/n]
  ```
  If the user confirms (or presses Enter), the scan runs immediately and `last_scan_utc` is updated.
  If the user declines, startup continues normally.
- `IRetentionEnforcementService.RunScanAsync(CancellationToken)` is also invokable directly
  for tests or a future TUI command (e.g., `s` = scan in the main menu).

**Why not a PeriodicTimer / BackgroundService timer**:
TrashMail Panda is an interactive console app, not a system daemon. The app may be run
infrequently (once a week, once a month). A background timer that fires once per day would only
work during the session and would silently never run between sessions. The startup-prompt pattern
is the correct trigger for an app with unpredictable uptime.

**Migration path to monthly automatic**:
Once the app is stable and running reliably, `PromptThresholdDays` can be increased to 30 days
(matching `ScanIntervalDays`) so the prompt only fires when a full monthly cycle has elapsed —
effectively making it auto-remind monthly. A future enhancement can add a `--auto-scan` flag
for daemon-mode deployments.

**Alternatives considered**:
- *PeriodicTimer with 24 h interval*: Rejected — only fires within the current process lifetime;
  useless for an app opened weekly or monthly.
- *Always scan on startup (no prompt)*: Rejected — could result in unexpected Gmail deletions
  if the user opens the app quickly to check something; explicit user confirmation is safer.
- *Cron expression / OS scheduler*: Out of scope for MVP; noted as future enhancement.

---

### Q9: Does the ML model need changes for P3 (EmailAgeDays semantics)?

**Decision**: No model architecture changes. The rules for `EmailAgeDays` differ by context:

| Context | Rule | Rationale |
|---|---|---|
| **Training** (existing `email_features` rows) | Use `email_age_days` **as stored** — do not recompute | The stored value = age at the moment the user made the triage decision. That's the correct signal: the model learns what action people take for a given email type *given how old it was at decision time*. |
| **Inference** (brand-new email, not yet in DB) | Compute fresh: `(DateTime.UtcNow - receivedDate).Days` | No stored value exists yet; the suggestion is about to be made based on the email's age right now. |

**Evidence**: `EmailTriageConsoleService` line 145–148 computes fresh age for display/classification of in-session emails:
```csharp
var currentAgeDays = (int)(DateTime.UtcNow - feature.ReceivedDateUtc).TotalDays;
```
This is correct for inference. For training, `EmailFeatureVector.EmailAgeDays` is already
populated at extraction time and must not be overwritten before the vector is passed to the trainer.

**Impact on `GmailTrainingDataService`**: No change needed — it already stores `EmailAgeDays`
at scan time (feature extraction time ≈ decision time for re-scanned emails). Do NOT re-derive
the value from `ReceivedDateUtc` when building the training dataset.

**Alternatives considered**: Recomputing fresh for all training rows — rejected; this would replace
"age at decision time" with "age today", losing the user-intent signal entirely and making the
model unable to learn "this type of email is typically archived when it's 2 years old".

---

## Dependencies & Integration Notes

| Dependency | Status | Notes |
|---|---|---|
| `received_date_utc` column | ✅ Exists (schema v5) | Indexed; populated from Gmail scan |
| `IEmailArchiveService.SetTrainingLabelAsync` | ✅ Exists | No changes needed |
| `IEmailProvider.BatchModifyAsync` | ✅ Exists | Used for Archive/Delete routing |
| `LabelThresholds` static class | 🆕 New | Shared across 3 consuming sites |
| `IRetentionEnforcementService` | 🆕 New | Interface + BackgroundService implementation |
| `RetentionScanResult` record | 🆕 New | Result of one scan pass |
| Spec #059 (`ClassifyActionAsync`) | ⏳ In progress | ML model predicts time-bounded labels; `EmailAgeDays` recomputed by caller before passing vector |
