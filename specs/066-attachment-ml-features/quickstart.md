# Quickstart: Attachment Metadata for ML Email Features

**Feature**: `066-attachment-ml-features`  
**Branch**: `066-attachment-ml-features`  
**Date**: 2026-03-30

This guide describes the end-to-end implementation flow for developers working on this feature.

---

## What This Feature Adds

Nine new columns on `email_features` that capture attachment presence and type breakdown for every email in the ML training corpus. Combined with a startup schema-version check and automatic full re-scan, these fields give the ML model better signal for classifying emails like invoices (PDFs), marketing campaigns (images), and notification-only emails (no attachments).

---

## Prerequisites

- Feature branch `066-attachment-ml-features` checked out
- `dotnet build` passing on `main` before starting
- Existing local database at `data/app.db` (or a fresh one — both paths work)

---

## Step 1 — Increment `FeatureSchema.CurrentVersion`

**File**: `src/Providers/Storage/TrashMailPanda.Providers.Storage/Models/FeatureSchema.cs`

Change `CurrentVersion` from `1` to `2`.

```csharp
// Before
public const int CurrentVersion = 1;

// After
public const int CurrentVersion = 2;
```

This single change causes:
- `GetAllFeaturesAsync(FeatureSchema.CurrentVersion)` to exclude all version-1 rows from ML training
- `HasOutdatedFeaturesAsync(2)` to return `true` on any database that has version-1 rows
- `BuildFeatureVector` to stamp `FeatureSchemaVersion = 2` on newly extracted rows

---

## Step 2 — Add New Properties to `EmailFeatureVector`

**File**: `src/Providers/Storage/TrashMailPanda.Providers.Storage/Models/EmailFeatureVector.cs`

Insert 9 new `init`-only properties in the attachment-fields block (after existing `HasAttachments`):

```csharp
[Column("attachment_count")]
public int AttachmentCount { get; init; }

[Column("total_attachment_size_log")]
public float TotalAttachmentSizeLog { get; init; }

[Column("has_doc_attachments")]
public int HasDocAttachments { get; init; }

[Column("has_image_attachments")]
public int HasImageAttachments { get; init; }

[Column("has_audio_attachments")]
public int HasAudioAttachments { get; init; }

[Column("has_video_attachments")]
public int HasVideoAttachments { get; init; }

[Column("has_xml_attachments")]
public int HasXmlAttachments { get; init; }

[Column("has_binary_attachments")]
public int HasBinaryAttachments { get; init; }

[Column("has_other_attachments")]
public int HasOtherAttachments { get; init; }
```

Also update `TrashMailPandaDbContext.OnModelCreating` to add `HasDefaultValue(0)` for each new column (required for the migration DEFAULT to be reflected in EF metadata).

---

## Step 3 — Create EF Core Migration

Run this command to scaffold the migration:

```bash
cd /Users/mmorales/Dev/trashmail-panda
dotnet ef migrations add AddAttachmentMlFeatures \
  --project src/Providers/Storage/TrashMailPanda.Providers.Storage \
  --startup-project src/TrashMailPanda/TrashMailPanda \
  --output-dir Migrations
```

Verify the generated `Up()` method adds all 9 columns with `DEFAULT 0`. The migration file will be named `YYYYMMDDHHMMSS_AddAttachmentMlFeatures.cs`.

If the scaffolded migration is missing defaults, add them manually:

```csharp
migrationBuilder.AddColumn<int>(
    name: "attachment_count",
    table: "email_features",
    type: "INTEGER",
    nullable: false,
    defaultValue: 0);
// ... repeat for the other 8 columns
```

---

## Step 4 — Add `AttachmentMimeClassifier` and Supporting Types

Create three new files in `src/Providers/Email/TrashMailPanda.Providers.Email/Services/`:

### 4a. `AttachmentCategory.cs`

```csharp
namespace TrashMailPanda.Providers.Email.Services;

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

### 4b. `AttachmentFeatureSummary.cs`

```csharp
namespace TrashMailPanda.Providers.Email.Services;

internal record AttachmentFeatureSummary(
    int Count,
    float TotalSizeLog,
    int HasDocuments,
    int HasImages,
    int HasAudio,
    int HasVideo,
    int HasXml,
    int HasBinaries,
    int HasOther)
{
    internal static AttachmentFeatureSummary Empty { get; } =
        new(0, 0f, 0, 0, 0, 0, 0, 0, 0);
}
```

### 4c. `AttachmentMimeClassifier.cs`

Implement using the MIME taxonomy from `data-model.md`. Key patterns:

```csharp
internal static AttachmentCategory Classify(string? mimeType, string? fileName = null)
{
    var mime = (mimeType ?? string.Empty).Trim().ToLowerInvariant();

    if (mime.StartsWith("image/",  StringComparison.Ordinal)) return AttachmentCategory.Image;
    if (mime.StartsWith("audio/",  StringComparison.Ordinal)) return AttachmentCategory.Audio;
    if (mime.StartsWith("video/",  StringComparison.Ordinal)) return AttachmentCategory.Video;
    if (XmlTypes.Contains(mime))                              return AttachmentCategory.Xml;
    if (mime.StartsWith("application/vnd.openxmlformats-officedocument.", StringComparison.Ordinal))
                                                              return AttachmentCategory.Document;
    if (mime.StartsWith("application/vnd.oasis.opendocument.", StringComparison.Ordinal))
                                                              return AttachmentCategory.Document;
    if (mime.StartsWith("application/x-iwork-", StringComparison.Ordinal))
                                                              return AttachmentCategory.Document;
    if (DocumentTypes.Contains(mime))                         return AttachmentCategory.Document;
    if (BinaryTypes.Contains(mime))                           return AttachmentCategory.Binary;
    if (mime == "application/octet-stream")
    {
        var ext = Path.GetExtension((fileName ?? string.Empty).ToLowerInvariant());
        if (BinaryExtensions.Contains(ext))                   return AttachmentCategory.Binary;
    }
    return AttachmentCategory.Other;
}
```

Use `static readonly HashSet<string>` for `XmlTypes`, `DocumentTypes`, `BinaryTypes`, and `BinaryExtensions` to ensure O(1) lookup with no per-call allocations.

---

## Step 5 — Update `GmailTrainingDataService`

**File**: `src/Providers/Email/TrashMailPanda.Providers.Email/Services/GmailTrainingDataService.cs`

### 5a. Switch `FetchMessageAsync` to `format=FULL` with Fields restriction

```csharp
// Before
req.Format = UsersResource.MessagesResource.GetRequest.FormatEnum.Metadata;
req.MetadataHeaders = new Google.Apis.Util.Repeatable<string>(new[] { "Subject", "From", "To", "Date" });

// After
req.Format = UsersResource.MessagesResource.GetRequest.FormatEnum.Full;
req.Fields = "id,threadId,internalDate,snippet,sizeEstimate,labelIds," +
             "payload/mimeType,payload/headers,payload/parts,payload/filename,payload/body/size";
```

The `Fields` restriction excludes `payload/body/data` (base64 message body content) which can be hundreds of KB per email. Attachment metadata (`size`, `mimeType`, `filename`) is in `payload/parts`, not `payload/body/data`.

### 5b. Update `BuildFeatureVector` to extract attachment features

After constructing the base feature vector, add:

```csharp
var attachments = new List<EmailAttachment>();
CollectAttachments(msg.Payload, attachments);  // reuse existing static method
var summary = AttachmentMimeClassifier.Summarize(attachments);
```

Then assign to the feature vector:
```csharp
HasAttachments           = attachments.Count > 0 ? 1 : 0,
AttachmentCount          = summary.Count,
TotalAttachmentSizeLog   = summary.TotalSizeLog,
HasDocAttachments        = summary.HasDocuments,
HasImageAttachments      = summary.HasImages,
HasAudioAttachments      = summary.HasAudio,
HasVideoAttachments      = summary.HasVideo,
HasXmlAttachments        = summary.HasXml,
HasBinaryAttachments     = summary.HasBinaries,
HasOtherAttachments      = summary.HasOther,
```

`CollectAttachments` is currently a private static method in `GmailEmailProvider`. It must either be made `internal static` and referenced from `GmailTrainingDataService`, or duplicated (unlikely) — the cleanest approach is to move it to a shared internal `GmailAttachmentHelper` static class accessible to both. Alternatively, copy it into `GmailTrainingDataService` since it's small.

---

## Step 6 — Add `HasOutdatedFeaturesAsync` to Storage

**`IEmailArchiveService.cs`**: Add the method signature (see `contracts/attachment-feature-interfaces.md`).

**`EmailArchiveService.cs`**: Implement using EF Core:

```csharp
public async Task<Result<bool>> HasOutdatedFeaturesAsync(
    int currentVersion, CancellationToken ct = default)
{
    try
    {
        await _connectionLock.WaitAsync(ct);
        try
        {
            var hasOutdated = await _context.EmailFeatures
                .AnyAsync(f => f.FeatureSchemaVersion < currentVersion, ct);
            return Result.Success(hasOutdated);
        }
        finally
        {
            _connectionLock.Release();
        }
    }
    catch (Exception ex)
    {
        return Result.Failure(new StorageError($"Failed to check feature schema version: {ex.Message}"));
    }
}
```

---

## Step 7 — Update `ConsoleStartupOrchestrator`

**File**: `src/TrashMailPanda/TrashMailPanda/Services/Console/ConsoleStartupOrchestrator.cs`

After Gmail provider initialization succeeds (around line 280 in current file), inject the schema-version check:

```csharp
// Schema version re-scan check
var outdatedResult = await _archiveService.HasOutdatedFeaturesAsync(FeatureSchema.CurrentVersion, ct);
if (outdatedResult.IsSuccess && outdatedResult.Value)
{
    AnsiConsole.MarkupLine("[yellow]⚠ Attachment features require a full re-scan of your emails.[/]");
    AnsiConsole.MarkupLine("[cyan]→ Re-scanning emails to populate attachment metadata (this may take a few minutes)…[/]");

    var rescanResult = await _trainingDataService.RunInitialScanAsync(_accountId, ct, progress: null);
    if (rescanResult.IsSuccess)
    {
        AnsiConsole.MarkupLine("[green]✓ Re-scan complete — attachment features are now up to date.[/]");
    }
    else
    {
        _logger.LogWarning("Attachment re-scan failed: {Error}", rescanResult.Error?.Message);
        AnsiConsole.MarkupLine("[yellow]⚠ Re-scan incomplete — attachment features will be populated on next startup.[/]");
    }
}
```

Ensure `_archiveService` (`IEmailArchiveService`) and `_trainingDataService` (`IGmailTrainingDataService`) are already injected via constructor — verify in the constructor signature.

---

## Step 8 — Update ML Pipeline

### 8a. `ActionTrainingInput.cs`

Add 9 `float` properties after `HasAttachments` (see `contracts/attachment-feature-interfaces.md`).

### 8b. `FeaturePipelineBuilder.cs`

Add 9 column names to `NumericFeatureColumnNames` array.

### 8c. `ModelTrainingPipeline.cs` and `IncrementalUpdateService.cs`

In both `MapToTrainingInput` private static methods, add the 9 field mappings:

```csharp
AttachmentCount          = v.AttachmentCount,
TotalAttachmentSizeLog   = v.TotalAttachmentSizeLog,
HasDocAttachments        = v.HasDocAttachments,
HasImageAttachments      = v.HasImageAttachments,
HasAudioAttachments      = v.HasAudioAttachments,
HasVideoAttachments      = v.HasVideoAttachments,
HasXmlAttachments        = v.HasXmlAttachments,
HasBinaryAttachments     = v.HasBinaryAttachments,
HasOtherAttachments      = v.HasOtherAttachments,
```

---

## Step 9 — Write Tests

### `AttachmentMimeClassifierTests.cs`

Minimum test cases (aim for 100% coverage of `AttachmentMimeClassifier`):

| Input MIME | Input Filename | Expected Category |
|------------|---------------|-------------------|
| `application/pdf` | - | Document |
| `application/vnd.openxmlformats-officedocument.wordprocessingml.document` | - | Document |
| `image/png` | - | Image |
| `image/svg+xml` | - | Image (prefix wins) |
| `audio/mpeg` | - | Audio |
| `video/mp4` | - | Video |
| `application/xml` | - | Xml |
| `text/xml` | - | Xml |
| `application/zip` | - | Binary |
| `application/octet-stream` | `setup.exe` | Binary |
| `application/octet-stream` | `data.bin` | Binary |
| `application/octet-stream` | `report.pdf` | Document (type wins, not binary ext) |
| `application/octet-stream` | `` (empty) | Other |
| `application/json` | - | Other |
| `null` | - | Other |
| `` (empty) | - | Other |
| `message/rfc822` | - | Other |
| `text/plain` | - | Document |
| `text/html` | - | Document |
| `application/x-7z-compressed` | - | Binary |

Summarize edge cases:
- Empty list → `Empty` summary
- Mixed PDF + MP3 → `HasDocuments=1, HasAudio=1`
- 3 attachments, unknown MIME each → `Count=3, HasOther=1`
- `Size=0` on all parts → `TotalSizeLog=0`
- Single 1000-byte PDF → `TotalSizeLog ≈ log10(1001) ≈ 3.0`

---

## Step 10 — Verify End-to-End

```bash
# Build
dotnet build

# Run unit tests
dotnet test --filter Category=Unit

# Run the app against existing database
dotnet run --project src/TrashMailPanda

# Expected output on first run with old database:
# ⚠ Attachment features require a full re-scan of your emails.
# → Re-scanning emails to populate attachment metadata (this may take a few minutes)…
# ✓ Re-scan complete — attachment features are now up to date.

# Verify schema version (using SQLite CLI or DB browser)
# SELECT COUNT(*), feature_schema_version FROM email_features GROUP BY feature_schema_version;
# Expected: all rows at version 2 after re-scan
```

---

## Common Pitfalls

| Pitfall | Fix |
|---------|-----|
| `payload.Parts` is null even with `format=FULL` | This is valid — simple plain-text emails with no MIME structure have a flat payload. Return empty attachment list. |
| `CollectAttachments` not accessible from `GmailTrainingDataService` | Make it `internal static` and move to a shared helper, or copy it to the `GmailTrainingDataService`. |
| `Fields` selector missing nested `payload/parts/parts` | Gmail MIME trees can be nested (e.g., multipart/mixed wrapping multipart/alternative). Use `payload/parts` as the selector — the API returns the full parts subtree when `parts` is selected at any level. |
| EF Core migration missing `defaultValue` | Manually add `defaultValue: 0` to each `AddColumn` call in `Up()`; EF Core SQLite provider sometimes omits it for value types. |
| Both `MapToTrainingInput` methods not updated | Search for `private static ActionTrainingInput MapToTrainingInput` — there are two; update both. |
