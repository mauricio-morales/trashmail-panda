# Contract: IRetentionEnforcementService

**Phase 1 output** | **Branch**: `064-archive-then-delete-labels`  
**Location**: `src/TrashMailPanda/TrashMailPanda/Services/IRetentionEnforcementService.cs`

---

## Interface Definition

```csharp
namespace TrashMailPanda.Services;

/// <summary>
/// Enforces time-bounded retention labels by scanning archived emails whose
/// received date has surpassed their label threshold and deleting them from Gmail.
///
/// training_label is NEVER modified by any operation on this interface.
/// </summary>
public interface IRetentionEnforcementService
{
    /// <summary>
    /// Runs a full retention enforcement scan.
    ///
    /// Queries email_features for archived emails with time-bounded labels
    /// (Archive for 30d / 1y / 5y). For each email whose age (computed fresh
    /// from received_date_utc) meets or exceeds the label threshold, issues a
    /// Gmail delete. Persists last_scan_utc on completion.
    ///
    /// Per-email Gmail failures are accumulated in RetentionScanResult.FailedIds
    /// and do not abort the scan.
    /// </summary>
    Task<Result<RetentionScanResult>> RunScanAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Returns the UTC datetime of the last completed scan, or null if the
    /// scan has never been run.
    /// </summary>
    Task<Result<DateTime?>> GetLastScanTimeAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Returns true if (UtcNow - last_scan_utc) >= PromptThresholdDays, or if
    /// the scan has never been run. Used by RetentionStartupCheck to decide
    /// whether to show the startup prompt.
    /// </summary>
    Task<Result<bool>> ShouldPromptAsync(CancellationToken cancellationToken = default);
}
```

---

## Method Contracts

### `RunScanAsync`

| | Detail |
|---|---|
| **Returns on success** | `RetentionScanResult` with counts and any failed IDs |
| **Returns on failure** | `NetworkError` if Gmail API is unavailable; `StorageError` if DB read fails |
| **Side effects** | Deletes emails from Gmail (non-reversible); persists `last_scan_utc` to config |
| **training_label** | **Never touched** — invariant enforced in implementation |
| **Idempotency** | Safe to call multiple times; already-deleted emails produce a non-fatal Gmail 404 (treated as success for that email) |
| **Cancellation** | Honours `CancellationToken`; partial results returned if cancelled mid-scan; `last_scan_utc` is NOT persisted on cancellation |

### `GetLastScanTimeAsync`

| | Detail |
|---|---|
| **Returns on success** | `DateTime?` (UTC) — `null` if never run |
| **Returns on failure** | `StorageError` |
| **Side effects** | None (read-only) |

### `ShouldPromptAsync`

| | Detail |
|---|---|
| **Returns `true`** | Never run OR `(UtcNow - last_scan_utc).TotalDays >= PromptThresholdDays` |
| **Returns `false`** | Scan ran recently (within prompt threshold) |
| **Returns on failure** | `StorageError` — caller should treat failure as `true` (prompt) to be safe |
| **Side effects** | None (read-only) |

---

## Error Types Used

| Error | When |
|---|---|
| `StorageError` | `email_features` query or `app_config` read/write fails |
| `NetworkError` | Gmail API unreachable or returns 5xx |
| `AuthenticationError` | Gmail OAuth token expired and cannot auto-refresh |

---

## DI Registration

```csharp
// Program.cs / host builder
services.AddSingleton<IRetentionEnforcementService, RetentionEnforcementService>();
services.Configure<RetentionEnforcementOptions>(
    configuration.GetSection("RetentionEnforcement"));
```

---

## `RetentionEnforcementOptions` Contract

**Config section**: `RetentionEnforcement` in `appsettings.json`

```json
{
  "RetentionEnforcement": {
    "ScanIntervalDays": 30,
    "PromptThresholdDays": 7
  }
}
```

| Property | Type | Default | Min | Constraint |
|---|---|---|---|---|
| `ScanIntervalDays` | `int` | `30` | `1` | — |
| `PromptThresholdDays` | `int` | `7` | `1` | Must be ≤ `ScanIntervalDays` |
