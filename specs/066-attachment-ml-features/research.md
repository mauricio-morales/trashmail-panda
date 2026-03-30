# Research: Attachment Metadata for ML Email Features

**Feature**: `066-attachment-ml-features`  
**Phase**: 0 — Research  
**Date**: 2026-03-30

---

## RES-001 — Gmail API Format for Attachment Metadata

**Question**: Can `format=METADATA` return `payload.Parts` (with filenames, MIME types, and sizes) needed to extract attachment metadata?

**Decision**: Switch `FetchMessageAsync` in `GmailTrainingDataService` to `format=FULL`.

**Rationale**: `format=METADATA` only returns top-level fields (`id`, `threadId`, `labelIds`, `snippet`, `internalDate`, `sizeEstimate`) plus the filtered request headers. The `payload.Parts` tree — which contains per-part `mimeType`, `filename`, and `Body.Size` — is **not populated** at METADATA level. This is confirmed by code inspection: `BuildFeatureVector` currently leaves `HasAttachments` at 0 because `msg.Payload?.Parts` is always null. `format=FULL` is the only standard Gmail API format that populates the full MIME parts tree with size information.

**Alternatives considered**:

- *`format=MINIMAL`*: Even less data than METADATA; no headers and no parts. Rejected — worse than current state.
- *Separate second API call per email*: Fetch METADATA for all features, then fetch FULL only for attachment fields. Rejected — doubles quota usage per email, complicates the flow, and adds latency for large scans; the spec explicitly states "no additional API calls are required for most emails" (even though the existing assumption about METADATA was incorrect, adding a second call is clearly out of scope).
- *`Fields` parameter with `format=FULL`*: Use `req.Fields = "id,threadId,labelIds,internalDate,snippet,sizeEstimate,payload/mimeType,payload/headers,payload/parts"` to exclude `payload/body/data` (base64 content) from the response, keeping bandwidth manageable. **This is the recommended implementation** — same single API call, but responses exclude the potentially large message body bytes.
- *Gmail People/History API*: Not applicable — History API doesn't provide attachment metadata.

**Implementation note**: The `FetchMessageAsync` private method in `GmailTrainingDataService` currently hardcodes `FormatEnum.Metadata`. It must be changed to `FormatEnum.Full` with a `Fields` restriction that includes `payload/parts` but excludes `payload/body/data` to avoid large base64-encoded body responses for the ~10k-email scan.

---

## RES-002 — MIME Type Taxonomy: 7-Category Classification

**Question**: How should the seven attachment categories map to MIME type strings? Is there authoritative guidance on edge cases (e.g., Office formats, structured XML subtypes)?

**Decision**: Implement `AttachmentMimeClassifier` as a `static` class with a `Classify(string mimeType)` method returning a `AttachmentCategory` flags enum, following the taxonomy defined in FR-006.

**Rationale**: The seven categories cover >98% of observed business email attachments. A simple prefix-match plus known-type hashtable is O(1) per call, allocation-free when using `string.StartsWith` comparisons, and trivially testable with a comprehensive fixture of MIME strings.

**Category mapping** (resolved from FR-006 plus IANA registration review):

| Category | MIME Type Patterns |
|----------|-------------------|
| **Documents** | `application/pdf`, `application/msword`, `application/vnd.ms-excel`, `application/vnd.ms-powerpoint`, `application/vnd.openxmlformats-officedocument.*`, `text/plain`, `text/csv`, `application/rtf`, `text/richtext`, `application/vnd.oasis.opendocument.*` (ODF), `application/x-iwork-*` (Apple iWork) |
| **Images** | `image/*` (all) |
| **Audio** | `audio/*` (all) |
| **Video** | `video/*` (all) |
| **XML** | `application/xml`, `text/xml`, `application/xhtml+xml`, `application/atom+xml`, `application/rss+xml`, `application/soap+xml`, `application/mathml+xml` |
| **Binaries** | `application/zip`, `application/x-zip-compressed`, `application/x-rar-compressed`, `application/x-tar`, `application/gzip`, `application/x-gzip`, `application/x-7z-compressed`, `application/x-bzip2`, `application/x-bzip`, `application/x-msdownload`, `application/x-executable`, `application/x-msdos-program`, `application/octet-stream` (when filename indicates .exe/.bin/.dmg/.iso), `application/x-apple-diskimage`, `application/vnd.ms-cab-compressed` |
| **Other** | Anything not matched by the above |

**Edge cases resolved**:

- `application/octet-stream` without a filename → classify as **Other** (cannot determine type from MIME alone; filename-based refinement is optional optimization, deferred to a future iteration)
- `application/octet-stream` with a filename having a known binary extension (`.exe`, `.msi`, `.dmg`, `.iso`, `.bin`) → classify as **Binary**
- `multipart/*` parts (e.g., `multipart/mixed`, `multipart/alternative`) → these are structural MIME containers, not leaf attachments; they must be **excluded** from classification (the recursive `CollectAttachments` walker already skips non-leaf parts)
- `text/html` as an attachment (not inline) → **Documents** (HTML is authored content)
- `application/json` → **Other** (structured data but not XML; no user-facing document value in the "documents" sense)
- `message/rfc822` (embedded email) → **Other** (not one of the named categories)
- Empty or null MIME type → default to **Other**; do not throw

**Alternatives considered**:

- *Third-party MIME detection library*: Adds a NuGet dependency for a function that needs only ~30 type mappings. Rejected — unnecessary dependency for deterministic lookup.
- *Regex-based matching*: Slower and harder to read than `StartsWith` / exact match. Rejected.
- *Database-driven MIME table*: Over-engineered for a fixed 7-category taxonomy. Rejected.

---

## RES-003 — Schema Version Detection at Startup

**Question**: How should the app detect at startup that existing feature rows need re-extraction? Where in the startup flow should this check live?

**Decision**: Add `Task<Result<bool>> HasOutdatedFeaturesAsync(int currentVersion, CancellationToken ct)` to `IEmailArchiveService` / `EmailArchiveService`. Call this from `ConsoleStartupOrchestrator` after EF Core migrations succeed and the Gmail provider is ready, but before the normal incremental sync.

**Rationale**: The detection query is a simple `SELECT EXISTS(SELECT 1 FROM email_features WHERE feature_schema_version < @version)` — a sub-millisecond indexed read. Placing it in `EmailArchiveService` keeps the storage layer as the owner of schema-version queries (consistent with `GetAllFeaturesAsync(schemaVersion:)` already there). The `ConsoleStartupOrchestrator` is the correct caller because it already orchestrates the ordered startup sequence and has access to Spectre.Console for status messages; no new service layer is needed.

**Re-scan trigger sequence** (within `ConsoleStartupOrchestrator`):
1. EF Core `MigrateAsync()` runs (existing step — adds new columns with DEFAULT 0)
2. Storage, Gmail providers initialize (existing steps)
3. **[NEW]** `HasOutdatedFeaturesAsync(FeatureSchema.CurrentVersion)` — if `true`:
   a. Display: `[yellow]⚠ Attachment features require a full re-scan of your emails.[/]` + `[cyan]→ Re-scanning emails to capture attachment metadata…[/]`
   b. Call `GmailTrainingDataService.RunInitialScanAsync(...)` (existing method — full scan path)
   c. On completion: `[green]✓ Re-scan complete. Attachment features are now up to date.[/]`
4. Continue with normal startup incremental sync (existing step)

**Alternative considered**:

- *Detect by checking `FeatureSchema` table rows*: The `FeatureSchema` model exists but is not persisted in a separate table — it's a static constant. Cannot use it for detection. Rejected.
- *Check during ML training only*: `ModelTrainingPipeline` already filters by `GetAllFeaturesAsync(FeatureSchema.CurrentVersion)` — old-version rows are excluded from training. But this means the re-scan never happens automatically; user would never know why their training set shrank. Rejected — violates FR-003/FR-004.
- *Always re-scan on version bump regardless of existing rows*: Safe but wastes time when the database is empty (fresh install). `EXISTS` check avoids unnecessary re-scans.

---

## RES-004 — `total_attachment_size_log` Storage Format

**Question**: Should total attachment size be stored as raw bytes, log-scale, or normalized?

**Decision**: Store as `float` using `log10(totalBytes + 1)` — matching the existing `email_size_log` pattern.

**Rationale**: `EmailFeatureVector.EmailSizeLog` already uses `log10` to normalize the email size distribution for ML training. Attachment sizes follow the same heavy-tailed distribution (a ZIP can be 50 MB while a text file attachment is 1 KB). Using `+1` handles the zero-attachment case without NaN. The spec explicitly calls for log-scale storage in the Key Entities section.

**Implementation**: In `AttachmentMimeClassifier.ComputeSizeLog(long totalBytes)` → `(float)Math.Log10(totalBytes + 1)`.

---

## RES-005 — Inline Images vs. True Attachments

**Question**: How does `CollectAttachments` in `GmailEmailProvider` distinguish inline images from true attachments?

**Decision**: A part is a true attachment if and only if `!string.IsNullOrEmpty(part.Filename) || part.Body?.AttachmentId != null`. Parts with `Content-Disposition: inline` and no filename or attachment ID are body content and will naturally be excluded by the existing `CollectAttachments` walker logic.

**Rationale**: The Gmail API marks inline images with `part.Body.AttachmentId = null` if they are embedded base64 in the part body. True attachments always have an `AttachmentId` (for deferred download) or a non-empty `Filename`. This matches the spec's inline-image edge case requirement exactly.

**Note for `GmailTrainingDataService`**: `BuildFeatureVector` will call `CollectAttachments` (already in `GmailEmailProvider`) on the FULL-format `msg.Payload`, producing a `List<EmailAttachment>` that it then passes to `AttachmentMimeClassifier`. No new walking logic needed in `GmailTrainingDataService`.

---

## RES-006 — EF Core Migration Pattern for 9 New Columns

**Question**: How should the EF Core migration add 9 columns with no-null defaults to an existing large table?

**Decision**: Single migration `AddAttachmentMlFeatures` adds all 9 columns via `AddColumn<int>` / `AddColumn<float>` with `defaultValue: 0` / `defaultValue: 0f`. A `migrationBuilder.Sql(...)` statement sets `feature_schema_version = 1` for all existing rows (no-op since they already have version 1), making the migration idempotent. No data backfill in the migration itself — backfill happens via the re-scan (FR-003).

**Rationale**: EF Core SQLite `AddColumn` with a`DEFAULT` is applied instantly regardless of table size (SQLite optimizes constant-default column additions since SQLite 3.38.0 in 2022). No row-level UPDATE needed in the migration. Existing code reading rows without the new columns will see default 0 values, satisfying the backwards-compatibility assumption in the spec.

**Migration file naming**: `20260330000000_AddAttachmentMlFeatures.cs` — follows the project convention `YYYYMMDDHHmmss_PascalCaseDescription`.

---

## RES-007 — Re-scan Interrupted Mid-way Safety

**Question**: What happens if `RunInitialScanAsync` is interrupted (app close during re-scan)?

**Decision**: No special resumption logic needed. `StoreFeaturesBatchAsync` preserves `TrainingLabel` and `UserCorrected` on UPDATE (existing behavior). A partial re-scan leaves some rows at version 1 and some at version 2. On the next startup, `HasOutdatedFeaturesAsync` will detect version-1 rows and trigger the re-scan again. The scan processes all emails; rows already at version 2 will simply be updated again (idempotent upsert).

**Rationale**: The existing `StoreFeaturesBatchAsync` uses an INSERT OR REPLACE pattern that updates existing rows without losing user corrections. Re-running the full scan multiple times converges to the same final state. No deduplication issue.

---

## RES-008 — `ActionTrainingInput` Extension Pattern

**Question**: How are new float fields added to the ML training pipeline?

**Decision**: Add 9 `float` properties to `ActionTrainingInput`. Update `NumericFeatureColumnNames` in `FeaturePipelineBuilder` to include all 9. Update both `MapToTrainingInput` static methods (in `ModelTrainingPipeline` and `IncrementalUpdateService`) to map from `EmailFeatureVector`.

**Rationale**: There are two duplicate `MapToTrainingInput` private static methods — one in each class. Both must be updated in sync. No mapper abstraction exists; the duplication is pre-existing technical debt (out of scope to fix here, per implementation discipline). New properties follow the existing `float` + `[ColumnName]` attribute pattern where column name differs from property name. `AttachmentCount` and `TotalAttachmentSizeLog` are continuous; the seven type flags are binary 0/1 floats treated as continuous by `NormalizeMeanVariance` (standard approach — no one-hot needed for binary features).
