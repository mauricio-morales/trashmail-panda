# Interface Contracts: Attachment Feature Interfaces

**Feature**: `066-attachment-ml-features`  
**Phase**: 1 — Design  
**Date**: 2026-03-30

This document describes the public-facing interface contracts introduced or modified by this feature. For a console TUI application the "contracts" are the C# interfaces and service method signatures that cross subsystem boundaries.

---

## Modified Interface: `IEmailArchiveService`

**File**: `src/Providers/Storage/TrashMailPanda.Providers.Storage/IEmailArchiveService.cs`

### New Method

```csharp
/// <summary>
/// Returns <c>true</c> if the feature store contains any rows whose
/// <c>feature_schema_version</c> is strictly less than <paramref name="currentVersion"/>,
/// indicating that a full re-scan is required to populate new feature columns.
/// Returns <c>false</c> if all rows are current, or if the feature store is empty.
/// </summary>
/// <param name="currentVersion">The version to compare against (typically <see cref="FeatureSchema.CurrentVersion"/>).</param>
/// <param name="ct">Cancellation token.</param>
/// <returns>
///   <see cref="Result{T}"/> wrapping <c>true</c> when outdated rows exist;
///   <see cref="Result{T}"/> wrapping <c>false</c> when all rows are current or no rows exist;
///   <see cref="Result{T}"/> failure on storage error.
/// </returns>
Task<Result<bool>> HasOutdatedFeaturesAsync(int currentVersion, CancellationToken ct = default);
```

**Contracts**:
- Never throws — returns `Result.Failure(new StorageError(...))` on DB error.
- Returns `false` (not failure) when the table is empty.
- Is idempotent and read-only — may be called multiple times without side effects.
- Must complete in <100ms on any table size (uses `EXISTS` query, not full scan).

---

## Internal Type: `AttachmentMimeClassifier`

**File**: `src/Providers/Email/TrashMailPanda.Providers.Email/Services/AttachmentMimeClassifier.cs`  
**Visibility**: `internal static` (not exposed outside the Email provider project)

### Method: `Classify`

```csharp
/// <summary>
/// Classifies a single MIME type string into an <see cref="AttachmentCategory"/> flag.
/// </summary>
/// <param name="mimeType">
///   The MIME type of the attachment part (e.g., "application/pdf", "image/png").
///   Null or empty strings are classified as <see cref="AttachmentCategory.Other"/>.
/// </param>
/// <param name="fileName">
///   Optional filename used to refine "application/octet-stream" classification
///   based on file extension. Null or empty is ignored.
/// </param>
/// <returns>
///   Exactly one <see cref="AttachmentCategory"/> flag value.
///   Never returns <see cref="AttachmentCategory.None"/>.
/// </returns>
internal static AttachmentCategory Classify(string? mimeType, string? fileName = null);
```

### Method: `Summarize`

```csharp
/// <summary>
/// Aggregates a list of email attachments into a single <see cref="AttachmentFeatureSummary"/>
/// suitable for direct assignment to the attachment fields of <see cref="EmailFeatureVector"/>.
/// </summary>
/// <param name="attachments">
///   List of true (non-inline) attachments for a single email.
///   May be empty; must not be null.
/// </param>
/// <returns>
///   <see cref="AttachmentFeatureSummary.Empty"/> when the list is empty.
///   Otherwise a populated summary with all fields correctly set.
/// </returns>
internal static AttachmentFeatureSummary Summarize(IReadOnlyList<EmailAttachment> attachments);
```

**Contracts**:
- Stateless — no instance state, thread-safe by construction.
- `Classify` always returns a non-`None` value.
- `Summarize` never throws; returns `Empty` on empty input.
- `ComputeSizeLog(0)` returns `0f` (not NaN or negative infinity).
- Type flags are always exactly `0` or `1` (never other integers).

---

## Modified Type: `EmailFeatureVector` (Storage Model)

**File**: `src/Providers/Storage/TrashMailPanda.Providers.Storage/Models/EmailFeatureVector.cs`

### New Properties (contract additions — no breaking changes)

All new properties have default values of `0` / `0f` so existing code creating `EmailFeatureVector` instances without the new fields continues to compile and produce valid zero-valued records.

```csharp
// Added after HasAttachments

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

**Backward-compatibility contract**: Existing code constructing `EmailFeatureVector` with object initializers will compile with warnings if using positional initializers, but will not fail — all new properties have their type's default value (`0` / `0f`) when omitted.

---

## Modified Type: `ActionTrainingInput` (ML Model)

**File**: `src/Providers/ML/TrashMailPanda.Providers.ML/Models/ActionTrainingInput.cs`

### New Properties

```csharp
// Added after existing HasAttachments float property

public float AttachmentCount { get; set; }
public float TotalAttachmentSizeLog { get; set; }
public float HasDocAttachments { get; set; }
public float HasImageAttachments { get; set; }
public float HasAudioAttachments { get; set; }
public float HasVideoAttachments { get; set; }
public float HasXmlAttachments { get; set; }
public float HasBinaryAttachments { get; set; }
public float HasOtherAttachments { get; set; }
```

All 9 must be registered in `FeaturePipelineBuilder.NumericFeatureColumnNames`:

```csharp
nameof(ActionTrainingInput.AttachmentCount),
nameof(ActionTrainingInput.TotalAttachmentSizeLog),
nameof(ActionTrainingInput.HasDocAttachments),
nameof(ActionTrainingInput.HasImageAttachments),
nameof(ActionTrainingInput.HasAudioAttachments),
nameof(ActionTrainingInput.HasVideoAttachments),
nameof(ActionTrainingInput.HasXmlAttachments),
nameof(ActionTrainingInput.HasBinaryAttachments),
nameof(ActionTrainingInput.HasOtherAttachments),
```

---

## Startup Orchestration Contract

**Modified type**: `ConsoleStartupOrchestrator`  
**File**: `src/TrashMailPanda/TrashMailPanda/Services/Console/ConsoleStartupOrchestrator.cs`

### Behavioral Contract for Schema Re-scan

After `InitializeGmailProvider()` succeeds and before the incremental sync step:

1. Call `_archiveService.HasOutdatedFeaturesAsync(FeatureSchema.CurrentVersion)`.
2. If `result.IsSuccess && result.Value == true`:
   - Display exactly two lines:
     ```
     [yellow]⚠ Attachment features require a full re-scan of your emails.[/]
     [cyan]→ Re-scanning emails to populate attachment metadata (this may take a few minutes)…[/]
     ```
   - Call `await _trainingDataService.RunInitialScanAsync(accountId, ct, progress: null)`.
   - On success: `[green]✓ Re-scan complete — attachment features are now up to date.[/]`
   - On failure: report error via existing `HandleProviderFailureAsync` or log and continue (non-fatal).
3. If `result.IsSuccess && result.Value == false`: skip silently (no console output).
4. If `result.IsFailure`: log warning, skip re-scan (do not prevent startup).

**Non-blocking**: A re-scan failure must NOT prevent the application from starting. It is logged and the user can manually trigger a re-scan from the triage menu.
