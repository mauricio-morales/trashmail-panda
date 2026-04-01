# Data Model: Attachment Metadata for ML Email Features

**Feature**: `066-attachment-ml-features`  
**Phase**: 1 — Design  
**Date**: 2026-03-30

---

## Entities Modified

### 1. `EmailFeatureVector` — 9 New Fields

**File**: `src/Providers/Storage/TrashMailPanda.Providers.Storage/Models/EmailFeatureVector.cs`

**Change type**: Additive — 9 new properties, no existing properties removed or renamed.

| Property | C# Type | SQLite Column | Column Type | EF Default | Nullable | Notes |
|----------|---------|---------------|-------------|------------|----------|-------|
| `AttachmentCount` | `int` | `attachment_count` | `INTEGER` | `0` | No | Total number of true (non-inline) attachment parts |
| `TotalAttachmentSizeLog` | `float` | `total_attachment_size_log` | `REAL` | `0` | No | `log10(totalBytes + 1)`; 0 when no attachments |
| `HasDocAttachments` | `int` | `has_doc_attachments` | `INTEGER` | `0` | No | 1 if any attachment is classified as Document |
| `HasImageAttachments` | `int` | `has_image_attachments` | `INTEGER` | `0` | No | 1 if any attachment is classified as Image |
| `HasAudioAttachments` | `int` | `has_audio_attachments` | `INTEGER` | `0` | No | 1 if any attachment is classified as Audio |
| `HasVideoAttachments` | `int` | `has_video_attachments` | `INTEGER` | `0` | No | 1 if any attachment is classified as Video |
| `HasXmlAttachments` | `int` | `has_xml_attachments` | `INTEGER` | `0` | No | 1 if any attachment is classified as XML |
| `HasBinaryAttachments` | `int` | `has_binary_attachments` | `INTEGER` | `0` | No | 1 if any attachment is classified as Binary |
| `HasOtherAttachments` | `int` | `has_other_attachments` | `INTEGER` | `0` | No | 1 if any attachment is classified as Other |

**Placement in file**: Insert after the existing `HasAttachments` property (line ~75 in current file), keeping attachment-related fields grouped.

**Existing field unchanged**: `HasAttachments` (`int`, `has_attachments`) remains. After this feature it will be correctly populated (currently always 0 due to METADATA format — fixed by RES-001).

---

### 2. `FeatureSchema` — Version Increment

**File**: `src/Providers/Storage/TrashMailPanda.Providers.Storage/Models/FeatureSchema.cs`

| Field | Old Value | New Value |
|-------|-----------|-----------|
| `CurrentVersion` | `1` | `2` |

**Impact**: `GetAllFeaturesAsync(FeatureSchema.CurrentVersion)` in `ModelTrainingPipeline` will only return rows with `feature_schema_version = 2`, excluding pre-feature rows until the re-scan completes.

---

## New Type: `AttachmentMimeClassifier`

**File**: `src/Providers/Email/TrashMailPanda.Providers.Email/Services/AttachmentMimeClassifier.cs`

**Visibility**: `internal static` (used only within the Email provider project)

### Responsibility

Single-responsibility classifier: maps a MIME type string to one or more `AttachmentCategory` flags. Used exclusively by `GmailTrainingDataService.BuildFeatureVector` to populate the seven type flags on `EmailFeatureVector`.

### `AttachmentCategory` Enum

```csharp
[Flags]
internal enum AttachmentCategory
{
    None     = 0,
    Document = 1 << 0,
    Image    = 1 << 1,
    Audio    = 1 << 2,
    Video    = 1 << 3,
    Xml      = 1 << 4,
    Binary   = 1 << 5,
    Other    = 1 << 6
}
```

**Note**: `AttachmentCategory` is declared in its own file (`AttachmentCategory.cs`) per Principle IV (one public type per file; here `internal` type per file is the same discipline).

### `AttachmentMimeClassifier` API

```csharp
internal static class AttachmentMimeClassifier
{
    // Classify a single MIME type → AttachmentCategory flag(s)
    internal static AttachmentCategory Classify(string? mimeType, string? fileName = null);

    // Aggregate a list of EmailAttachment into feature flag booleans
    // Returns (count, totalSizeLog, hasDoc, hasImage, hasAudio, hasVideo, hasXml, hasBin, hasOther)
    internal static AttachmentFeatureSummary Summarize(IReadOnlyList<EmailAttachment> attachments);

    // log10(bytes + 1) helper; exposed for testing
    internal static float ComputeSizeLog(long totalBytes);
}
```

### `AttachmentFeatureSummary` Record

```csharp
internal record AttachmentFeatureSummary(
    int Count,
    float TotalSizeLog,
    int HasDocuments,
    int HasImages,
    int HasAudio,
    int HasVideo,
    int HasXml,
    int HasBinaries,
    int HasOther
)
{
    internal static AttachmentFeatureSummary Empty { get; } = new(0, 0f, 0, 0, 0, 0, 0, 0, 0);
}
```

**Note**: `AttachmentFeatureSummary` is declared in its own file (`AttachmentFeatureSummary.cs`).

### Classification Logic

```text
Classify(mimeType, fileName):
  mimeType = (mimeType ?? "").Trim().ToLowerInvariant()
  fileName  = (fileName  ?? "").Trim().ToLowerInvariant()

  if mimeType starts with "image/"  → Image
  if mimeType starts with "audio/"  → Audio
  if mimeType starts with "video/"  → Video

  if mimeType in XML_TYPES           → Xml
  if mimeType in DOCUMENT_TYPES      → Document
  if mimeType in BINARY_TYPES        → Binary
  if mimeType == "application/octet-stream":
     if fileName ends with any of BINARY_EXTENSIONS → Binary
     else                                           → Other

  default → Other

XML_TYPES = { application/xml, text/xml, application/xhtml+xml,
              application/atom+xml, application/rss+xml,
              application/soap+xml, application/mathml+xml }

DOCUMENT_TYPES = { application/pdf, application/msword,
                   application/vnd.ms-excel, application/vnd.ms-powerpoint,
                   application/rtf, text/richtext, text/plain, text/csv,
                   text/html,
                   application/vnd.openxmlformats-officedocument.* (prefix),
                   application/vnd.oasis.opendocument.*            (prefix),
                   application/x-iwork-*                           (prefix) }

BINARY_TYPES = { application/zip, application/x-zip-compressed,
                 application/x-rar-compressed, application/x-tar,
                 application/gzip, application/x-gzip,
                 application/x-7z-compressed, application/x-bzip2,
                 application/x-bzip, application/x-msdownload,
                 application/x-executable, application/x-msdos-program,
                 application/x-apple-diskimage,
                 application/vnd.ms-cab-compressed }

BINARY_EXTENSIONS = { .exe, .msi, .dll, .dmg, .iso, .bin, .deb, .rpm,
                      .appimage, .pkg, .cab }
```

---

## Interface Changes

### `IEmailArchiveService` — New Method

**File**: `src/Providers/Storage/TrashMailPanda.Providers.Storage/IEmailArchiveService.cs`

```csharp
/// <summary>
/// Returns true if any feature rows exist with a schema version older than
/// <paramref name="currentVersion"/>, indicating that a full re-scan is required.
/// </summary>
Task<Result<bool>> HasOutdatedFeaturesAsync(int currentVersion, CancellationToken ct = default);
```

**Implementation in `EmailArchiveService`**: Executes `context.EmailFeatures.AnyAsync(f => f.FeatureSchemaVersion < currentVersion, ct)` wrapped in `Result.Success` / `Result.Failure` pattern.

---

## Database Schema Change

**Migration**: `20260330000000_AddAttachmentMlFeatures`

```sql
-- Up
ALTER TABLE email_features ADD COLUMN attachment_count          INTEGER NOT NULL DEFAULT 0;
ALTER TABLE email_features ADD COLUMN total_attachment_size_log REAL    NOT NULL DEFAULT 0;
ALTER TABLE email_features ADD COLUMN has_doc_attachments       INTEGER NOT NULL DEFAULT 0;
ALTER TABLE email_features ADD COLUMN has_image_attachments     INTEGER NOT NULL DEFAULT 0;
ALTER TABLE email_features ADD COLUMN has_audio_attachments     INTEGER NOT NULL DEFAULT 0;
ALTER TABLE email_features ADD COLUMN has_video_attachments     INTEGER NOT NULL DEFAULT 0;
ALTER TABLE email_features ADD COLUMN has_xml_attachments       INTEGER NOT NULL DEFAULT 0;
ALTER TABLE email_features ADD COLUMN has_binary_attachments    INTEGER NOT NULL DEFAULT 0;
ALTER TABLE email_features ADD COLUMN has_other_attachments     INTEGER NOT NULL DEFAULT 0;

-- Down
-- SQLite does not support DROP COLUMN in older versions; EF Core handles this
-- via table recreation in the Down() method.
```

**No indices needed**: These columns are aggregated scalars, not used as filter predicates in queries.

---

## `ActionTrainingInput` — 9 New Float Properties

**File**: `src/Providers/ML/TrashMailPanda.Providers.ML/Models/ActionTrainingInput.cs`

Add these after existing `HasAttachments` property:

| Property | ML.NET Column Name | Maps From |
|----------|--------------------|-----------|
| `AttachmentCount` | `AttachmentCount` | `v.AttachmentCount` (cast to float) |
| `TotalAttachmentSizeLog` | `TotalAttachmentSizeLog` | `v.TotalAttachmentSizeLog` |
| `HasDocAttachments` | `HasDocAttachments` | `v.HasDocAttachments` (cast to float) |
| `HasImageAttachments` | `HasImageAttachments` | `v.HasImageAttachments` (cast to float) |
| `HasAudioAttachments` | `HasAudioAttachments` | `v.HasAudioAttachments` (cast to float) |
| `HasVideoAttachments` | `HasVideoAttachments` | `v.HasVideoAttachments` (cast to float) |
| `HasXmlAttachments` | `HasXmlAttachments` | `v.HasXmlAttachments` (cast to float) |
| `HasBinaryAttachments` | `HasBinaryAttachments` | `v.HasBinaryAttachments` (cast to float) |
| `HasOtherAttachments` | `HasOtherAttachments` | `v.HasOtherAttachments` (cast to float) |

All 9 must be added to `NumericFeatureColumnNames` in `FeaturePipelineBuilder` and mapped in both `MapToTrainingInput` methods.

---

## State Transitions

### Feature Row Lifecycle (Schema Version)

```
[Email scanned before 066]
  feature_schema_version = 1
  has_attachments = 0 (broken)
  attachment_count = 0 (column doesn't exist yet)
         │
         ▼  [EF Migration runs on startup]
  feature_schema_version = 1
  attachment_count = 0 (new column, DEFAULT 0)
  ... all 9 new columns = 0 ...
         │
         ▼  [HasOutdatedFeaturesAsync detects version 1 rows]
         │
         ▼  [RunInitialScanAsync re-fetches all emails with format=FULL]
  feature_schema_version = 2
  has_attachments = CORRECT VALUE
  attachment_count = CORRECT VALUE
  ... all 9 new columns = CORRECT VALUES ...

[Incremental sync after re-scan]
  new email arrives → BuildFeatureVector with format=FULL
  feature_schema_version = 2
  all attachment columns populated correctly
```

### Re-scan Trigger in `ConsoleStartupOrchestrator`

```
MigrateAsync() ────────────────────────► columns added (DEFAULT 0)
     │
InitializeStorageProvider() ──────────► DB ready
     │
InitializeGmailProvider() ────────────► Gmail API ready
     │
HasOutdatedFeaturesAsync(version=2) ──► TRUE? ──► display warning + run full scan
                                    └──► FALSE? ─► skip (normal path / fresh install)
     │
[Continue startup: incremental sync, ML model init, TUI ready]
```

---

## Validation Rules

| Rule | Source | Enforcement |
|------|--------|-------------|
| `AttachmentCount >= 0` | Logical | `Summarize()` returns 0 if attachment list is empty |
| `TotalAttachmentSizeLog >= 0` | Math | `log10(x + 1)` with `x >= 0` always non-negative |
| Type flags are 0 or 1 only | FR-007 | `Summarize()` uses `Any()` → `bool` → `? 1 : 0` |
| `HasAttachments == 1` when `AttachmentCount > 0` | FR-009 | `BuildFeatureVector`: set `HasAttachments = attachments.Count > 0 ? 1 : 0` |
| Inline parts excluded | Spec edge case | `CollectAttachments` gate: `!string.IsNullOrEmpty(part.Filename) \|\| part.Body?.AttachmentId != null` |
| Corrupted part size → excluded from total | FR-008 | `EmailAttachment.Size` defaults to 0 via `part.Body?.Size ?? 0`; 0-size parts contribute 0 to total |
| Unknown MIME type → `has_other_attachments` | FR-006 / edge case | `Classify` default branch returns `Other` |
