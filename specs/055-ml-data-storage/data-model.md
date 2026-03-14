# Data Model: ML Data Storage System

**Feature**: #55 — ML Data Storage  
**Date**: 2026-03-14  
**Status**: Complete

## Entity Overview

This data model defines the storage schema for ML feature vectors, full email archives, and storage quota management. All tables are stored in the existing SQLCipher-encrypted SQLite database at `data/app.db`.

## Database Tables

### 1. email_features

Stores lightweight feature vectors extracted from each processed email. Used for ML model training and inference. Features persist even after full email is deleted.

| Column | Type | Nullable | Description | Validation |
|--------|------|----------|-------------|------------|
| EmailId | TEXT | No | Primary key, provider-assigned message ID (opaque) | Non-empty string |
| SenderDomain | TEXT | No | Extracted domain from sender email address | Non-empty |
| SenderKnown | INTEGER | No | Boolean: sender in contacts (0=false, 1=true) | 0 or 1 |
| ContactStrength | INTEGER | No | Contact relationship strength | 0=None, 1=Weak, 2=Strong |
| SpfResult | TEXT | No | SPF authentication result | "pass", "fail", "neutral", "none" |
| DkimResult | TEXT | No | DKIM authentication result | "pass", "fail", "neutral", "none" |
| DmarcResult | TEXT | No | DMARC authentication result | "pass", "fail", "neutral", "none" |
| HasListUnsubscribe | INTEGER | No | Boolean: List-Unsubscribe header present | 0 or 1 |
| HasAttachments | INTEGER | No | Boolean: email contains attachments | 0 or 1 |
| HourReceived | INTEGER | No | Hour of day when received | 0-23 |
| DayOfWeek | INTEGER | No | Day of week when received | 0=Sunday, 6=Saturday |
| EmailSizeLog | REAL | No | log10(email size in bytes) | >= 0.0 |
| SubjectLength | INTEGER | No | Character count of subject line | >= 0 |
| RecipientCount | INTEGER | No | Total To + Cc recipients | >= 0 |
| IsReply | INTEGER | No | Boolean: subject starts with "Re:" | 0 or 1 |
| InUserWhitelist | INTEGER | No | Boolean: matches AlwaysKeep rules | 0 or 1 |
| InUserBlacklist | INTEGER | No | Boolean: matches AutoTrash rules | 0 or 1 |
| LabelCount | INTEGER | No | Number of folders/labels on email | >= 0 |
| LinkCount | INTEGER | No | Count of links in body HTML | >= 0 |
| ImageCount | INTEGER | No | Count of images in body HTML | >= 0 |
| HasTrackingPixel | INTEGER | No | Boolean: 1x1 pixel image detected | 0 or 1 |
| UnsubscribeLinkInBody | INTEGER | No | Boolean: unsubscribe link pattern found | 0 or 1 |
| EmailAgeDays | INTEGER | No | Days since email received (relative to extraction) | >= 0 |
| IsInInbox | INTEGER | No | Boolean: email in Inbox folder (strong keep signal) | 0 or 1 |
| IsStarred | INTEGER | No | Boolean: email starred/flagged (strong keep signal) | 0 or 1 |
| IsImportant | INTEGER | No | Boolean: marked important (keep signal) | 0 or 1 |
| WasInTrash | INTEGER | No | Boolean: source folder is Trash (strong delete signal) | 0 or 1 |
| WasInSpam | INTEGER | No | Boolean: source folder is Spam (strong delete signal) | 0 or 1 |
| IsArchived | INTEGER | No | Boolean: not in Inbox/Trash/Spam (triage target) | 0 or 1 |
| ThreadMessageCount | INTEGER | No | Number of messages in thread/conversation | >= 1 |
| SenderFrequency | INTEGER | No | Total emails from this sender domain in corpus | >= 1 |
| SubjectText | TEXT | Yes | Raw subject for TF-IDF (null for headerless emails) | Max 1000 chars |
| BodyTextShort | TEXT | Yes | First 500 chars of body text for TF-IDF | Max 500 chars |
| TopicClusterId | INTEGER | Yes | LDA topic cluster ID (null until Phase 2) | >= 0 if set |
| TopicDistributionJson | TEXT | Yes | JSON topic probabilities (null until Phase 2) | Valid JSON array |
| SenderCategory | TEXT | Yes | Sender domain category (null until Phase 2) | Predefined categories |
| SemanticEmbeddingJson | TEXT | Yes | Dense embedding vector (null until Phase 3) | Valid JSON array |
| FeatureSchemaVersion | INTEGER | No | Schema version for compatibility checks | >= 1 |
| ExtractedAt | TEXT | No | ISO8601 timestamp when features extracted | Valid datetime |
| UserCorrected | INTEGER | No | Boolean: user corrected classification (retention priority) | 0 or 1 |

**Indexes**:
```sql
CREATE INDEX IF NOT EXISTS idx_features_extracted_at 
    ON email_features(ExtractedAt);

CREATE INDEX IF NOT EXISTS idx_features_schema_version 
    ON email_features(FeatureSchemaVersion);

CREATE INDEX IF NOT EXISTS idx_features_user_corrected 
    ON email_features(UserCorrected);
```

**SQL Schema**:
```sql
CREATE TABLE IF NOT EXISTS email_features (
    EmailId TEXT PRIMARY KEY,
    SenderDomain TEXT NOT NULL,
    SenderKnown INTEGER NOT NULL,
    ContactStrength INTEGER NOT NULL,
    SpfResult TEXT NOT NULL,
    DkimResult TEXT NOT NULL,
    DmarcResult TEXT NOT NULL,
    HasListUnsubscribe INTEGER NOT NULL,
    HasAttachments INTEGER NOT NULL,
    HourReceived INTEGER NOT NULL,
    DayOfWeek INTEGER NOT NULL,
    EmailSizeLog REAL NOT NULL,
    SubjectLength INTEGER NOT NULL,
    RecipientCount INTEGER NOT NULL,
    IsReply INTEGER NOT NULL,
    InUserWhitelist INTEGER NOT NULL,
    InUserBlacklist INTEGER NOT NULL,
    LabelCount INTEGER NOT NULL,
    LinkCount INTEGER NOT NULL,
    ImageCount INTEGER NOT NULL,
    HasTrackingPixel INTEGER NOT NULL,
    UnsubscribeLinkInBody INTEGER NOT NULL,
    EmailAgeDays INTEGER NOT NULL,
    IsInInbox INTEGER NOT NULL,
    IsStarred INTEGER NOT NULL,
    IsImportant INTEGER NOT NULL,
    WasInTrash INTEGER NOT NULL,
    WasInSpam INTEGER NOT NULL,
    IsArchived INTEGER NOT NULL,
    ThreadMessageCount INTEGER NOT NULL,
    SenderFrequency INTEGER NOT NULL,
    SubjectText TEXT,
    BodyTextShort TEXT,
    TopicClusterId INTEGER,
    TopicDistributionJson TEXT,
    SenderCategory TEXT,
    SemanticEmbeddingJson TEXT,
    FeatureSchemaVersion INTEGER NOT NULL,
    ExtractedAt TEXT NOT NULL,
    UserCorrected INTEGER NOT NULL DEFAULT 0
);
```

**Relationships**:
- 1:1 with `classification_history` via EmailId (existing table)
- 1:0..1 with `email_archive` via EmailId (optional full email storage)

---

### 2. email_archive

Stores complete email data for feature regeneration when extraction logic changes. Storage is optional based on capacity.

| Column | Type | Nullable | Description | Validation |
|--------|------|----------|-------------|------------|
| EmailId | TEXT | No | Primary key, provider-assigned message ID | Non-empty string |
| ThreadId | TEXT | Yes | Conversation/thread ID | Null if provider lacks threading |
| ProviderType | TEXT | No | Email provider identifier | "gmail", "imap", "outlook" |
| HeadersJson | TEXT | No | JSON-serialized headers dictionary | Valid JSON object |
| BodyText | TEXT | Yes | Plain text email body | At least one of BodyText/BodyHtml required |
| BodyHtml | TEXT | Yes | Sanitized HTML email body | At least one of BodyText/BodyHtml required |
| FolderTagsJson | TEXT | No | JSON array of canonical folder/tag names | Valid JSON array |
| SizeEstimate | INTEGER | No | Email size in bytes | > 0 |
| ReceivedDate | TEXT | No | ISO8601 original received timestamp | Valid datetime |
| ArchivedAt | TEXT | No | ISO8601 local archive timestamp | Valid datetime |
| Snippet | TEXT | Yes | Email preview text | Max 200 chars |
| SourceFolder | TEXT | No | Canonical source folder | "inbox", "archive", "trash", "spam", "sent", "drafts" |
| UserCorrected | INTEGER | No | Boolean: user corrected classification (retention priority) | 0 or 1 |

**Indexes**:
```sql
CREATE INDEX IF NOT EXISTS idx_archive_received_date 
    ON email_archive(ReceivedDate);

CREATE INDEX IF NOT EXISTS idx_archive_user_corrected_date 
    ON email_archive(UserCorrected, ReceivedDate);

CREATE INDEX IF NOT EXISTS idx_archive_source_folder 
    ON email_archive(SourceFolder);
```

**SQL Schema**:
```sql
CREATE TABLE IF NOT EXISTS email_archive (
    EmailId TEXT PRIMARY KEY,
    ThreadId TEXT,
    ProviderType TEXT NOT NULL,
    HeadersJson TEXT NOT NULL,
    BodyText TEXT,
    BodyHtml TEXT,
    FolderTagsJson TEXT NOT NULL,
    SizeEstimate INTEGER NOT NULL,
    ReceivedDate TEXT NOT NULL,
    ArchivedAt TEXT NOT NULL,
    Snippet TEXT,
    SourceFolder TEXT NOT NULL,
    UserCorrected INTEGER NOT NULL DEFAULT 0,
    FOREIGN KEY (EmailId) REFERENCES email_features(EmailId) ON DELETE CASCADE
);
```

**Relationships**:
- 1:1 with `email_features` via EmailId
- Foreign key constraint ensures features exist before archive

---

### 3. storage_quota

Tracks storage usage and cleanup history. Single-row configuration table.

| Column | Type | Nullable | Description | Validation |
|--------|------|----------|-------------|------------|
| Id | INTEGER | No | Primary key (always 1) | = 1 |
| LimitBytes | INTEGER | No | Configured storage limit in bytes | > 0 |
| CurrentBytes | INTEGER | No | Current database size in bytes | >= 0 |
| FeatureBytes | INTEGER | No | Space used by email_features table | >= 0 |
| ArchiveBytes | INTEGER | No | Space used by email_archive table | >= 0 |
| FeatureCount | INTEGER | No | Total stored feature vectors | >= 0 |
| ArchiveCount | INTEGER | No | Total stored full email archives | >= 0 |
| UserCorrectedCount | INTEGER | No | Count of user-corrected emails | >= 0 |
| LastCleanupAt | TEXT | Yes | ISO8601 timestamp of last cleanup | Valid datetime or NULL |
| LastMonitoredAt | TEXT | No | ISO8601 timestamp of last monitoring check | Valid datetime |

**SQL Schema**:
```sql
CREATE TABLE IF NOT EXISTS storage_quota (
    Id INTEGER PRIMARY KEY CHECK (Id = 1),
    LimitBytes INTEGER NOT NULL,
    CurrentBytes INTEGER NOT NULL,
    FeatureBytes INTEGER NOT NULL,
    ArchiveBytes INTEGER NOT NULL,
    FeatureCount INTEGER NOT NULL,
    ArchiveCount INTEGER NOT NULL,
    UserCorrectedCount INTEGER NOT NULL,
    LastCleanupAt TEXT,
    LastMonitoredAt TEXT NOT NULL
);
```

**Usage**:
```csharp
// Initialize on first use
INSERT OR IGNORE INTO storage_quota (Id, LimitBytes, CurrentBytes, ...)
VALUES (1, 53687091200, 0, ...); -- 50GB default

// Update on monitoring
UPDATE storage_quota
SET CurrentBytes = @current, FeatureBytes = @features, ...
WHERE Id = 1;
```

---

### 4. schema_version

Tracks database schema versions for migration management.

| Column | Type | Nullable | Description | Validation |
|--------|------|----------|-------------|------------|
| Version | INTEGER | No | Primary key, schema version number | >= 1 |
| AppliedAt | TEXT | No | ISO8601 timestamp when version applied | Valid datetime |
| Description | TEXT | No | Human-readable description of changes | Non-empty |

**SQL Schema**:
```sql
CREATE TABLE IF NOT EXISTS schema_version (
    Version INTEGER PRIMARY KEY,
    AppliedAt TEXT NOT NULL,
    Description TEXT NOT NULL
);
```

**Initial Version**:
```sql
INSERT OR IGNORE INTO schema_version (Version, AppliedAt, Description)
VALUES (5, datetime('now'), 'Add ML storage tables (email_features, email_archive, storage_quota)');
```

---

## Domain Models (C# Classes)

### EmailFeatureVector.cs

```csharp
using System;

namespace TrashMailPanda.Providers.Storage.Models;

/// <summary>
/// Represents extracted feature vector for ML model training and inference.
/// Lightweight representation persists even after full email is deleted.
/// </summary>
public class EmailFeatureVector
{
    // Identity
    public string EmailId { get; set; } = string.Empty;
    
    // Sender features
    public string SenderDomain { get; set; } = string.Empty;
    public bool SenderKnown { get; set; }
    public int ContactStrength { get; set; }
    
    // Authentication features
    public string SpfResult { get; set; } = "none";
    public string DkimResult { get; set; } = "none";
    public string DmarcResult { get; set; } = "none";
    
    // Structural features
    public bool HasListUnsubscribe { get; set; }
    public bool HasAttachments { get; set; }
    public int HourReceived { get; set; }
    public int DayOfWeek { get; set; }
    public float EmailSizeLog { get; set; }
    public int SubjectLength { get; set; }
    public int RecipientCount { get; set; }
    public bool IsReply { get; set; }
    
    // User rule features
    public bool InUserWhitelist { get; set; }
    public bool InUserBlacklist { get; set; }
    
    // Content features
    public int LabelCount { get; set; }
    public int LinkCount { get; set; }
    public int ImageCount { get; set; }
    public bool HasTrackingPixel { get; set; }
    public bool UnsubscribeLinkInBody { get; set; }
    
    // Archive-specific features
    public int EmailAgeDays { get; set; }
    public bool IsInInbox { get; set; }
    public bool IsStarred { get; set; }
    public bool IsImportant { get; set; }
    public bool WasInTrash { get; set; }
    public bool WasInSpam { get; set; }
    public bool IsArchived { get; set; }
    
    // Behavioral features
    public int ThreadMessageCount { get; set; }
    public int SenderFrequency { get; set; }
    
    // Text features (nullable)
    public string? SubjectText { get; set; }
    public string? BodyTextShort { get; set; }
    
    // Advanced features (Phase 2/3)
    public int? TopicClusterId { get; set; }
    public string? TopicDistributionJson { get; set; }
    public string? SenderCategory { get; set; }
    public string? SemanticEmbeddingJson { get; set; }
    
    // Metadata
    public int FeatureSchemaVersion { get; set; } = 1;
    public DateTime ExtractedAt { get; set; }
    public bool UserCorrected { get; set; }
}
```

### EmailArchiveEntry.cs

```csharp
using System;

namespace TrashMailPanda.Providers.Storage.Models;

/// <summary>
/// Represents complete email data stored for feature regeneration.
/// Optional storage based on capacity constraints.
/// </summary>
public class EmailArchiveEntry
{
    public string EmailId { get; set; } = string.Empty;
    public string? ThreadId { get; set; }
    public string ProviderType { get; set; } = string.Empty;
    public string HeadersJson { get; set; } = "{}";
    public string? BodyText { get; set; }
    public string? BodyHtml { get; set; }
    public string FolderTagsJson { get; set; } = "[]";
    public long SizeEstimate { get; set; }
    public DateTime ReceivedDate { get; set; }
    public DateTime ArchivedAt { get; set; }
    public string? Snippet { get; set; }
    public string SourceFolder { get; set; } = string.Empty;
    public bool UserCorrected { get; set; }
}
```

### StorageQuota.cs

```csharp
using System;

namespace TrashMailPanda.Providers.Storage.Models;

/// <summary>
/// Tracks storage usage and cleanup history.
/// Single-row configuration entity.
/// </summary>
public class StorageQuota
{
    public int Id { get; set; } = 1;
    public long LimitBytes { get; set; }
    public long CurrentBytes { get; set; }
    public long FeatureBytes { get; set; }
    public long ArchiveBytes { get; set; }
    public int FeatureCount { get; set; }
    public int ArchiveCount { get; set; }
    public int UserCorrectedCount { get; set; }
    public DateTime? LastCleanupAt { get; set; }
    public DateTime LastMonitoredAt { get; set; }
    
    /// <summary>
    /// Percentage of limit currently used (0-100)
    /// </summary>
    public double UsagePercent => LimitBytes > 0 
        ? (CurrentBytes / (double)LimitBytes) * 100 
        : 0;
}
```

### FeatureSchema.cs

```csharp
namespace TrashMailPanda.Providers.Storage.Models;

/// <summary>
/// Constants for feature schema versioning.
/// </summary>
public static class FeatureSchema
{
    public const int CurrentVersion = 1;
    
    public static bool IsCompatible(int schemaVersion)
    {
        return schemaVersion == CurrentVersion;
    }
}
```

---

## Migration Plan

### Version 5: Add ML Storage Tables

**Applied in**: `SqliteStorageProvider.CreateSchemaAsync()`

**Changes**:
1. Create `schema_version` table (if not exists)
2. Create `email_features` table with all indexes
3. Create `email_archive` table with all indexes
4. Create `storage_quota` table
5. Initialize `storage_quota` with default values (50GB limit)
6. Record version 5 in `schema_version`

**Backward Compatibility**: Safe to apply to existing databases - all changes are additive (CREATE TABLE IF NOT EXISTS). No data migration required.

---

## Storage Estimates

| Item | Size per Row | Count for 50GB |
|------|--------------|----------------|
| Feature vector | ~5-10 KB | ~5-10 million |
| Full email archive | ~50-100 KB | ~500K-1M |
| Mixed (80% features, 20% archives) | ~15 KB avg | ~3.3 million emails |

**Cleanup thresholds** (default):
- Trigger cleanup at 90% (45GB)
- Target usage after cleanup: 80% (40GB)
- Delete oldest non-corrected archives first
- VACUUM to reclaim space if still over target

---

## Validation Rules

Enforced at service layer (EmailArchiveService):

1. **email_features**:
   - EmailId must be non-empty
   - FeatureSchemaVersion must match FeatureSchema.CurrentVersion
   - Boolean fields stored as 0/1 integers
   - Temporal fields (HourReceived 0-23, DayOfWeek 0-6)

2. **email_archive**:
   - At least one of BodyText or BodyHtml must be present (warning if both null)
   - SourceFolder must be canonical: "inbox", "archive", "trash", "spam", "sent", "drafts"
   - ProviderType must be recognized: "gmail", "imap", "outlook"
   - HeadersJson must be valid JSON object
   - FolderTagsJson must be valid JSON array

3. **storage_quota**:
   - Only one row allowed (Id = 1)
   - LimitBytes > 0
   - CurrentBytes <= LimitBytes (soft limit, can temporarily exceed during cleanup)
