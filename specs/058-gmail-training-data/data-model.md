# Data Model: Gmail Training Data

**Feature**: #58 — Gmail Provider Extension for Training Data
**Date**: 2026-03-17
**Status**: Complete
**Depends On**: Spec #55 (ML Data Storage) — existing `email_features`, `email_archive`, `storage_quota` tables

---

## Entity Overview

This feature introduces four new database tables for storing training email records, Gmail label taxonomy, email-label associations, and scan progress cursors. It also extends the existing `email_features` table with two new engagement flag columns.

All tables live in the existing SQLCipher-encrypted SQLite database managed by `TrashMailPandaDbContext`. All new entities follow EF Core conventions already established in the project.

---

## New Tables

### 1. `training_emails`

Stores training email records imported from Gmail training scans. Each row represents one email retrieved from Spam, Trash, Archive, or Inbox during a training data scan. Records are updated on incremental scans to reflect state changes.

#### C# Entity: `TrainingEmailEntity`

```csharp
// src/Providers/Storage/TrashMailPanda.Providers.Storage/Models/TrainingEmailEntity.cs
public class TrainingEmailEntity
{
    [Required][StringLength(500)]
    public string EmailId { get; set; } = string.Empty;    // PK — Gmail message ID

    [Required][StringLength(320)]
    public string AccountId { get; set; } = string.Empty;  // Authenticated user email

    [Required][StringLength(500)]
    public string ThreadId { get; set; } = string.Empty;
    // Gmail threadId — used for local back-correction of IsReplied/IsForwarded

    [Required][StringLength(20)]
    public string FolderOrigin { get; set; } = string.Empty; // "Spam", "Trash", "Archive", "Inbox", "Sent"

    public bool IsRead { get; set; }
    public bool IsReplied { get; set; }
    // Initially false. Back-corrected to true when any SENT message in the same ThreadId is encountered.

    public bool IsForwarded { get; set; }
    // Initially false. Back-corrected to true when a SENT message in the same ThreadId
    // has SubjectPrefix matching "Fwd:", "FW:", or "Fw:".

    [StringLength(10)]
    public string? SubjectPrefix { get; set; }
    // First 10 chars of subject — stored only for SENT messages to enable local IsForwarded matching
    // without loading full subjects in bulk back-correction queries. Null for non-SENT messages.

    [Required][StringLength(20)]
    public string ClassificationSignal { get; set; } = string.Empty;
    // Enum stored as string: "AutoDelete", "AutoArchive", "LowConfidence", "Excluded"

    public float SignalConfidence { get; set; }  // 0.0–1.0; 0 for Excluded

    public bool IsValid { get; set; } = true;
    // Set to false when a state change moves email to Excluded category (FR-016)

    [StringLength(2000)]
    public string? RawLabelIds { get; set; }
    // JSON array of Gmail label IDs at time of last scan (e.g., ["Label_123", "UNREAD"])
    // SENT messages have ["SENT"] here — used by back-correction SQL predicate

    public DateTime LastSeenAt { get; set; }   // Timestamp of last scan that touched this email
    public DateTime ImportedAt { get; set; }   // Timestamp of initial import (immutable)
    public DateTime UpdatedAt { get; set; }    // Timestamp of last state update

    // Navigation
    public ICollection<LabelAssociationEntity> LabelAssociations { get; set; } = [];
}
```

> **Engagement back-correction**: After each batch is written, a two-step SQL UPDATE resolves `IsReplied` and `IsForwarded` for all records whose `ThreadId` matches a newly stored SENT-label message. `ClassificationSignal` and `IsValid` are then re-derived in the same transaction. See [research.md](research.md#1-isreplied--isforwarded-detection--local-back-correction-strategy) for the full SQL and rationale.

#### Table Schema

| Column | Type | Nullable | Description | Validation |
|--------|------|----------|-------------|------------|
| EmailId | TEXT | No | Primary key, Gmail message ID | Non-empty, max 500 |
| AccountId | TEXT | No | Authenticated user email (for multi-account) | Non-empty, max 320 |
| ThreadId | TEXT | No | Gmail thread ID for local back-correction | Non-empty, max 500 |
| FolderOrigin | TEXT | No | Source folder at import time | "Spam", "Trash", "Archive", "Inbox", "Sent" |
| IsRead | INTEGER | No | Boolean: email was read at scan time | 0 or 1 |
| IsReplied | INTEGER | No | Boolean: user replied — locally resolved from thread | 0 or 1, default 0 |
| IsForwarded | INTEGER | No | Boolean: user forwarded — locally resolved from thread | 0 or 1, default 0 |
| SubjectPrefix | TEXT | Yes | First 10 chars of subject (SENT messages only, for Fwd: matching) | Nullable, max 10 |
| ClassificationSignal | TEXT | No | Training signal derived from signal rules | "AutoDelete", "AutoArchive", "LowConfidence", "Excluded" |
| SignalConfidence | REAL | No | Numeric confidence (0.0 for Excluded) | 0.0–1.0 |
| IsValid | INTEGER | No | Boolean: false when signal invalidated by state change | 0 or 1, default 1 |
| RawLabelIds | TEXT | Yes | JSON array of Gmail label IDs at last scan | Valid JSON or NULL |
| LastSeenAt | TEXT | No | ISO8601: timestamp of most recent scan that touched this email | Valid datetime |
| ImportedAt | TEXT | No | ISO8601: timestamp of first import | Valid datetime |
| UpdatedAt | TEXT | No | ISO8601: timestamp of last state update | Valid datetime |

**Indexes**:
```sql
CREATE INDEX IF NOT EXISTS idx_training_emails_account
    ON training_emails(AccountId);

CREATE INDEX IF NOT EXISTS idx_training_emails_signal
    ON training_emails(ClassificationSignal, IsValid);

CREATE INDEX IF NOT EXISTS idx_training_emails_last_seen
    ON training_emails(LastSeenAt);

CREATE INDEX IF NOT EXISTS idx_training_emails_thread
    ON training_emails(ThreadId);
-- Required for O(1) back-correction: finding all emails in a thread when a SENT message arrives
```

**SQL Schema**:
```sql
CREATE TABLE IF NOT EXISTS training_emails (
    EmailId TEXT PRIMARY KEY,
    AccountId TEXT NOT NULL,
    ThreadId TEXT NOT NULL,
    FolderOrigin TEXT NOT NULL,
    IsRead INTEGER NOT NULL DEFAULT 0,
    IsReplied INTEGER NOT NULL DEFAULT 0,
    IsForwarded INTEGER NOT NULL DEFAULT 0,
    SubjectPrefix TEXT,
    ClassificationSignal TEXT NOT NULL,
    SignalConfidence REAL NOT NULL DEFAULT 0.0,
    IsValid INTEGER NOT NULL DEFAULT 1,
    RawLabelIds TEXT,
    LastSeenAt TEXT NOT NULL,
    ImportedAt TEXT NOT NULL,
    UpdatedAt TEXT NOT NULL
);
```

**Upsert Semantics**: On incremental scans, rows are upserted by `EmailId`. All columns except `ImportedAt` are updated to reflect current state. `IsValid` is set to `false` when a state change moves the email to `Excluded`.

---

### 2. `label_taxonomy`

Stores the user's complete Gmail label catalog, including user-created and system labels. Updated at the beginning of each scan.

#### C# Entity: `LabelTaxonomyEntity`

```csharp
// src/Providers/Storage/TrashMailPanda.Providers.Storage/Models/LabelTaxonomyEntity.cs
public class LabelTaxonomyEntity
{
    [Required][StringLength(500)]
    public string LabelId { get; set; } = string.Empty;    // PK — Gmail label ID (e.g., "Label_123")

    [Required][StringLength(320)]
    public string AccountId { get; set; } = string.Empty;  // Authenticated user email

    [Required][StringLength(200)]
    public string Name { get; set; } = string.Empty;       // Display name (e.g., "Receipts")

    [StringLength(50)]
    public string? Color { get; set; }                     // Gmail label color hex or name (nullable)

    [Required][StringLength(10)]
    public string LabelType { get; set; } = "User";        // "User" or "System"

    public int UsageCount { get; set; }                    // Count of training emails bearing this label

    public DateTime CreatedAt { get; set; }
    public DateTime UpdatedAt { get; set; }

    // Navigation
    public ICollection<LabelAssociationEntity> LabelAssociations { get; set; } = [];
}
```

#### Table Schema

| Column | Type | Nullable | Description | Validation |
|--------|------|----------|-------------|------------|
| LabelId | TEXT | No | Primary key, Gmail label ID | Non-empty, max 500 |
| AccountId | TEXT | No | Authenticated user email | Non-empty, max 320 |
| Name | TEXT | No | Label display name | Non-empty, max 200 |
| Color | TEXT | Yes | Gmail label color | Nullable, max 50 |
| LabelType | TEXT | No | "User" or "System" | One of two values |
| UsageCount | INTEGER | No | Count of training emails with this label | >= 0, default 0 |
| CreatedAt | TEXT | No | ISO8601 first-seen timestamp | Valid datetime |
| UpdatedAt | TEXT | No | ISO8601 last-updated timestamp | Valid datetime |

**Indexes**:
```sql
CREATE INDEX IF NOT EXISTS idx_label_taxonomy_account
    ON label_taxonomy(AccountId, LabelType);

CREATE INDEX IF NOT EXISTS idx_label_taxonomy_usage
    ON label_taxonomy(UsageCount DESC);
```

**SQL Schema**:
```sql
CREATE TABLE IF NOT EXISTS label_taxonomy (
    LabelId TEXT PRIMARY KEY,
    AccountId TEXT NOT NULL,
    Name TEXT NOT NULL,
    Color TEXT,
    LabelType TEXT NOT NULL DEFAULT 'User',
    UsageCount INTEGER NOT NULL DEFAULT 0,
    CreatedAt TEXT NOT NULL,
    UpdatedAt TEXT NOT NULL
);
```

**Upsert Semantics**: Labels are upserted by `LabelId`. `UsageCount` is recalculated from `label_associations` after each scan, not incremented in place, to avoid drift.

---

### 3. `label_associations`

Links training emails to labels. User-created labels are recorded as positive training signals; system labels are recorded as context features. A single email may have multiple associations.

#### C# Entity: `LabelAssociationEntity`

```csharp
// src/Providers/Storage/TrashMailPanda.Providers.Storage/Models/LabelAssociationEntity.cs
public class LabelAssociationEntity
{
    public int Id { get; set; }                            // PK (auto-increment)

    [Required][StringLength(500)]
    public string EmailId { get; set; } = string.Empty;    // FK → training_emails.EmailId

    [Required][StringLength(500)]
    public string LabelId { get; set; } = string.Empty;    // FK → label_taxonomy.LabelId

    public bool IsTrainingSignal { get; set; }             // true: user label → positive training signal
    public bool IsContextFeature { get; set; }             // true: system label → feature only

    public DateTime CreatedAt { get; set; }

    // Navigation
    public TrainingEmailEntity? TrainingEmail { get; set; }
    public LabelTaxonomyEntity? LabelTaxonomy { get; set; }
}
```

#### Table Schema

| Column | Type | Nullable | Description | Validation |
|--------|------|----------|-------------|------------|
| Id | INTEGER | No | Primary key (auto-increment) | Positive integer |
| EmailId | TEXT | No | FK → training_emails.EmailId | Non-empty |
| LabelId | TEXT | No | FK → label_taxonomy.LabelId | Non-empty |
| IsTrainingSignal | INTEGER | No | Boolean: user label (positive training signal) | 0 or 1 |
| IsContextFeature | INTEGER | No | Boolean: system label (context feature) | 0 or 1 |
| CreatedAt | TEXT | No | ISO8601 creation timestamp | Valid datetime |

**Unique Constraint**: `(EmailId, LabelId)` — one association per email-label pair.

**Indexes**:
```sql
CREATE INDEX IF NOT EXISTS idx_label_assoc_email
    ON label_associations(EmailId);

CREATE INDEX IF NOT EXISTS idx_label_assoc_label
    ON label_associations(LabelId, IsTrainingSignal);

CREATE UNIQUE INDEX IF NOT EXISTS idx_label_assoc_unique
    ON label_associations(EmailId, LabelId);
```

**SQL Schema**:
```sql
CREATE TABLE IF NOT EXISTS label_associations (
    Id INTEGER PRIMARY KEY AUTOINCREMENT,
    EmailId TEXT NOT NULL,
    LabelId TEXT NOT NULL,
    IsTrainingSignal INTEGER NOT NULL DEFAULT 0,
    IsContextFeature INTEGER NOT NULL DEFAULT 0,
    CreatedAt TEXT NOT NULL,
    FOREIGN KEY (EmailId) REFERENCES training_emails(EmailId) ON DELETE CASCADE,
    FOREIGN KEY (LabelId) REFERENCES label_taxonomy(LabelId) ON DELETE CASCADE
);
```

**Upsert Semantics**: On incremental scans, associations are reconciled per email: new labels are inserted, removed labels are deleted, so the stored label set exactly matches the current Gmail label set.

---

### 4. `scan_progress`

Tracks the state of training data scans. Supports crash recovery, multi-session resumability, and incremental change detection. At most one active scan per account at any time.

#### C# Entity: `ScanProgressEntity`

```csharp
// src/Providers/Storage/TrashMailPanda.Providers.Storage/Models/ScanProgressEntity.cs
public class ScanProgressEntity
{
    public int Id { get; set; }                            // PK (auto-increment)

    [Required][StringLength(320)]
    public string AccountId { get; set; } = string.Empty;

    [Required][StringLength(20)]
    public string ScanType { get; set; } = "Initial";     // "Initial" or "Incremental"

    [Required][StringLength(20)]
    public string Status { get; set; } = "InProgress";
    // Overall status: "InProgress", "Completed", "Interrupted", "PausedStorageFull"

    [StringLength(4000)]
    public string? FolderProgressJson { get; set; }
    // Per-folder cursor state. JSON object keyed by folder name.
    // Example:
    // {
    //   "Spam":    { "status": "Completed",         "processedCount": 450,  "pageToken": null },
    //   "Trash":   { "status": "Completed",         "processedCount": 1200, "pageToken": null },
    //   "Sent":    { "status": "InProgress",        "processedCount": 800,  "pageToken": "NEXT_TOKEN" },
    //   "Archive": { "status": "NotStarted",        "processedCount": 0,    "pageToken": null },
    //   "Inbox":   { "status": "NotStarted",        "processedCount": 0,    "pageToken": null }
    // }
    // Folder statuses: "NotStarted", "InProgress", "Recovering", "PausedStorageFull", "Completed"
    // Populated with the processing order: Spam → Trash → Sent → Archive → Inbox
    // Updated atomically with each batch commit (same SQLite transaction as the batch write).

    public ulong? HistoryId { get; set; }
    // Gmail historyId saved at completion of a full scan.
    // Used by subsequent incremental scans via users.history.list.
    // Null until the first complete scan finishes.

    public int ProcessedCount { get; set; }                // Total emails processed across all folders
    public int? TotalEstimate { get; set; }                // Estimated total (nullable; from Gmail count API)

    [StringLength(500)]
    public string? LastProcessedEmailId { get; set; }      // EmailId of last successfully committed email (for display)

    public DateTime StartedAt { get; set; }                // Timestamp of scan initiation
    public DateTime UpdatedAt { get; set; }                // Timestamp of last checkpoint commit
    public DateTime? CompletedAt { get; set; }             // Null while scan is active
}
```

#### Table Schema

| Column | Type | Nullable | Description | Validation |
|--------|------|----------|-------------|------------|
| Id | INTEGER | No | Primary key (auto-increment) | Positive integer |
| AccountId | TEXT | No | Authenticated user email | Non-empty, max 320 |
| ScanType | TEXT | No | "Initial" or "Incremental" | One of two values |
| Status | TEXT | No | Overall scan status | "InProgress", "Completed", "Interrupted", "PausedStorageFull" |
| FolderProgressJson | TEXT | Yes | Per-folder cursor: status + pageToken + processedCount | Nullable, valid JSON |
| HistoryId | INTEGER | Yes | Gmail historyId at scan completion (for incremental scans) | Nullable, unsigned |
| ProcessedCount | INTEGER | No | Total emails processed across all folders | >= 0 |
| TotalEstimate | INTEGER | Yes | Estimated total email count | Nullable, >= 0 |
| LastProcessedEmailId | TEXT | Yes | EmailId of last committed email (progress display) | Nullable, max 500 |
| StartedAt | TEXT | No | ISO8601 scan start time | Valid datetime |
| UpdatedAt | TEXT | No | ISO8601 last checkpoint time | Valid datetime |
| CompletedAt | TEXT | Yes | ISO8601 completion time; null while active | Nullable, valid datetime |

**Indexes**:
```sql
CREATE INDEX IF NOT EXISTS idx_scan_progress_account_status
    ON scan_progress(AccountId, Status);
```

**SQL Schema**:
```sql
CREATE TABLE IF NOT EXISTS scan_progress (
    Id INTEGER PRIMARY KEY AUTOINCREMENT,
    AccountId TEXT NOT NULL,
    ScanType TEXT NOT NULL DEFAULT 'Initial',
    Status TEXT NOT NULL DEFAULT 'InProgress',
    FolderProgressJson TEXT,
    HistoryId INTEGER,
    ProcessedCount INTEGER NOT NULL DEFAULT 0,
    TotalEstimate INTEGER,
    LastProcessedEmailId TEXT,
    StartedAt TEXT NOT NULL,
    UpdatedAt TEXT NOT NULL,
    CompletedAt TEXT
);
```

**Resume Protocol** (on app start):
1. Query for any `scan_progress` row with `Status IN ('InProgress', 'PausedStorageFull', 'Interrupted')` for the current `AccountId`.
2. If found: display the in-progress scan to the user (folders completed, current folder, count so far). Offer to resume (default) or cancel.
3. If resuming: parse `FolderProgressJson`, skip completed folders, restart the active folder from its saved `pageToken`. If `pageToken` is stale (Gmail 400/410), clear it and set folder status to `"Recovering"` — re-scan from the beginning of that folder using upsert semantics.
4. If no in-progress scan: start a fresh scan with `FolderProgressJson` initialized to all-`"NotStarted"` in the defined order.

**Checkpoint Protocol** (per batch, within a single SQLite transaction):
1. Upsert all `TrainingEmailEntity` rows for the current page.
2. Run back-correction SQL for engagement flags.
3. Update `FolderProgressJson` (new `pageToken`, incremented `processedCount`).
4. Update `ScanProgressEntity.ProcessedCount`, `LastProcessedEmailId`, `UpdatedAt`.
5. Commit. If the app crashes before commit, the transaction rolls back and the folder's `pageToken` is unchanged — the same page is re-fetched and re-processed safely on next run.

**Active Scan Constraint**: At most one `Status` of `'InProgress'` or `'PausedStorageFull'` per `AccountId`. Any new scan request detects the existing row and resumes rather than creating a duplicate.

---

## Modified Tables

### `email_features` — Add Engagement Flag Columns

Two new columns are added to the existing `email_features` table to extend the canonical flag model per FR-017.

| Column | Type | Nullable | Description | Validation |
|--------|------|----------|-------------|------------|
| IsReplied | INTEGER | No | Boolean: user replied from this thread (0=false, 1=true) | 0 or 1, default 0 |
| IsForwarded | INTEGER | No | Boolean: user forwarded from this thread (0=false, 1=true) | 0 or 1, default 0 |

**Migration SQL**:
```sql
ALTER TABLE email_features ADD COLUMN IsReplied INTEGER NOT NULL DEFAULT 0;
ALTER TABLE email_features ADD COLUMN IsForwarded INTEGER NOT NULL DEFAULT 0;
```

**EF Core Migration Name**: `AddGmailTrainingDataSchema`

This migration:
1. Creates tables: `training_emails`, `label_taxonomy`, `label_associations`, `scan_progress`
2. Adds columns `IsReplied`, `IsForwarded` to `email_features`
3. Is backward-safe: all new columns/tables have safe defaults; no existing data is altered

---

## Entity Relationships

```
TrainingEmailEntity (1) ──── (N) LabelAssociationEntity (N) ──── (1) LabelTaxonomyEntity
       │                                                                        │
  EmailId (PK)                                                           LabelId (PK)
  ThreadId  ◄─── back-correction key                                     AccountId
  AccountId                                                               Name
  FolderOrigin                                                            LabelType
  ClassificationSignal                                                    UsageCount
  IsReplied ◄─── resolved locally from SENT messages in same ThreadId
  IsForwarded ◄─ resolved locally from SENT+Fwd: in same ThreadId
  SubjectPrefix   (stored on SENT messages only; used in back-correction SQL)

ScanProgressEntity (standalone, scoped by AccountId)
  Id (PK)
  AccountId
  ScanType (Initial | Incremental)
  Status (InProgress | Completed | Interrupted)
  PageToken (resumable initial scan)
  HistoryId (incremental baseline)
```

---

## Value Objects / Enums

### `ClassificationSignal` enum

```csharp
// src/Providers/Email/TrashMailPanda.Providers.Email/Models/ClassificationSignal.cs
public enum ClassificationSignal
{
    AutoDelete = 1,      // Spam or Trash (not engaged)
    AutoArchive = 2,     // Archive + Unread + not engaged
    LowConfidence = 3,   // Trash + engaged, or Inbox + Unread
    Excluded = 4         // Archive + Read, Inbox + Read, Archive + engaged
}
```

### `ScanType` and `ScanStatus` — stored as strings in the DB

```csharp
// src/Providers/Email/TrashMailPanda.Providers.Email/Models/ScanType.cs
public static class ScanType
{
    public const string Initial = "Initial";
    public const string Incremental = "Incremental";
}

// src/Providers/Email/TrashMailPanda.Providers.Email/Models/ScanStatus.cs
public static class ScanStatus
{
    public const string InProgress = "InProgress";
    public const string Completed = "Completed";
    public const string Interrupted = "Interrupted";
}
```

### `TrainingSignalResult` — value object returned by `ITrainingSignalAssigner`

```csharp
// src/Providers/Email/TrashMailPanda.Providers.Email/Models/TrainingSignalResult.cs
public record TrainingSignalResult(
    ClassificationSignal Signal,
    float Confidence    // 0.0 for Excluded
);
```

### `EngagementFlags` — returned by `IGmailEngagementDetector`

```csharp
// src/Providers/Email/TrashMailPanda.Providers.Email/Models/EngagementFlags.cs
public record EngagementFlags(
    bool IsReplied,
    bool IsForwarded
);
```

### `ScanSummary` — returned by `IGmailTrainingDataService`

```csharp
// src/Providers/Email/TrashMailPanda.Providers.Email/Models/ScanSummary.cs
public record ScanSummary(
    int TotalProcessed,
    int AutoDeleteCount,
    int AutoArchiveCount,
    int LowConfidenceCount,
    int ExcludedCount,
    int LabelsImported,
    TimeSpan Duration
);
```

---

## Database Path: `StorageProviderConfig` Fix (FR-019, FR-020)

The existing `StorageProviderConfig.DatabasePath` default `"./data/app.db"` is replaced:

```csharp
// src/TrashMailPanda/TrashMailPanda/Services/StorageProviderConfig.cs
public class StorageProviderConfig
{
    public string DatabasePath { get; set; } = GetOsDefaultPath();

    public static string GetOsDefaultPath()
    {
        var appData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        return Path.Combine(appData, "TrashMailPanda", "app.db");
    }

    public string EncryptionKey { get; set; } = string.Empty;
    public bool EnableWAL { get; set; } = true;
    public int CommandTimeoutSeconds { get; set; } = 30;
}
```

`SqliteStorageProvider.InitializeAsync` is updated to:
1. Check secure storage for key `storage_database_path` (wizard-saved path).
2. If found and non-empty, override `DatabasePath` with the wizard path.
3. Call `Directory.CreateDirectory(Path.GetDirectoryName(DatabasePath)!)` before opening the connection.

The `appsettings.json` `DatabasePath` entry is removed so config files cannot accidentally direct writes to relative paths.
