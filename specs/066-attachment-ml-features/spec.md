# Feature Specification: Attachment Metadata for ML Email Features

**Feature Branch**: `066-attachment-ml-features`  
**Created**: 2026-03-30  
**Status**: Draft  
**Input**: User description: "Add attachment metadata to ML email features — track whether emails have attachments, total size of attachments, and type categories (docs, images, audio, video, XML, binaries, other). Trigger a full data reload on next app start to populate attachment info for existing emails, and update incremental load to capture attachment info for new emails."

## User Scenarios & Testing *(mandatory)*

### User Story 1 - Re-scan Existing Emails with Attachment Data (Priority: P1)

A user who has already completed the initial email scan upgrades to this version. On the next app start, the system detects that the stored feature data does not include attachment metadata (the schema version has changed) and automatically re-runs a full data load over all previously scanned emails. The user sees a clear status message explaining why the re-scan is happening. When it completes, every email's record in the ML feature store includes the new attachment fields.

**Why this priority**: Without backfilling the existing data, the ML model cannot be trained on attachment features for any of the historically scanned emails — which represents the bulk of the training corpus. All other stories depend on this data being available.

**Independent Test**: Can be fully tested by deploying the updated app against a database that has completed a prior scan, starting the app, and verifying via database inspection that all feature rows contain populated attachment metadata columns.

**Acceptance Scenarios**:

1. **Given** the app has feature rows from a previous schema version, **When** the app starts, **Then** the system automatically triggers a full re-scan without requiring any user action
2. **Given** the full re-scan is in progress, **When** the user observes the terminal, **Then** a status message clearly states the re-scan is running to capture new attachment data
3. **Given** the full re-scan completes successfully, **When** querying the feature store, **Then** all email feature rows have been updated with attachment metadata and carry the new schema version

---

### User Story 2 - Capture Attachment Data for New Emails on Incremental Load (Priority: P1)

After the re-scan is complete, the app runs incremental syncs on subsequent starts to process emails received since the last run. Each newly discovered email has its attachment metadata extracted and stored alongside all other feature fields, so the ML corpus stays current.

**Why this priority**: Tied in priority with US1 — once the historical backfill is done, ongoing accuracy depends on new emails being enriched with the same attachment features.

**Independent Test**: Can be fully tested by starting the app after a completed re-scan, receiving or simulating a new email that has attachments, triggering an incremental sync, and verifying the resulting feature row contains correct attachment metadata.

**Acceptance Scenarios**:

1. **Given** a new email with one PDF and one image arrives, **When** the incremental sync processes it, **Then** the feature row records: has attachments = true, attachment count = 2, total size, has-document = true, has-image = true, all other type flags = false
2. **Given** a new email with no attachments arrives, **When** the incremental sync processes it, **Then** the feature row records: has attachments = false, attachment count = 0, size = 0, all type flags = false
3. **Given** a new email with a ZIP archive and an executable arrive, **When** the incremental sync processes it, **Then** the feature row records has-binary = true

---

### User Story 3 - ML Training Incorporates Attachment Features (Priority: P2)

When the ML model is trained after US1 and US2 are complete, it can use the full set of attachment features as inputs. Emails with attachments of certain types (e.g., invoices as PDFs, notification emails without any attachments, marketing emails with images) get better-differentiated signal, improving classification accuracy.

**Why this priority**: This is the end-goal that justifies the feature, but it is achieved automatically once the data is in place — no separate user-facing action is required beyond US1 and US2.

**Independent Test**: Can be tested by training a model on the enriched dataset and verifying that the training pipeline accepts and uses all new attachment feature columns without errors.

**Acceptance Scenarios**:

1. **Given** the feature store contains rows with attachment metadata, **When** the ML training pipeline runs, **Then** all new attachment feature columns are included in the training input without errors
2. **Given** an email with a PDF attachment is classified, **When** the trained model evaluates it, **Then** the classification considers the attachment presence and type signals

---

### Edge Cases

- **Inline images vs. real attachments**: Some emails embed images directly in the body (inline/base64) rather than as true attachments. These should be counted as body content, not as attachment records.  
- **Corrupted or missing attachment metadata**: If the email source cannot provide attachment size or MIME type for a part, the system should record what is available and default missing numeric fields to 0 and type flags to false — the row must still be stored.
- **Very large numbers of attachments**: An email with dozens of attachments should still result in a single feature row; individual attachment counts and type flags are aggregated.
- **Unknown MIME types**: A MIME type not matching any known category must be counted in the "other" type flag, not silently dropped.
- **Emails with only inline images and no true attachments**: Has-attachments flag must remain false; the inline image is already captured via the `image_count` body feature.
- **Re-scan interrupted mid-way**: If the app is closed during the re-scan, the next startup must resume (or restart) safely, without creating duplicate feature rows or corrupting existing ones.
- **Re-scan on a machine with no prior scan**: If no prior feature data exists at all, the re-scan code path and the normal first-time scan code path must produce the same outcomes.

## Requirements *(mandatory)*

### Functional Requirements

- **FR-001**: The feature store schema MUST be extended to capture: attachment presence (boolean), total attachment count, total attachment size, and seven mutually-exclusive-but-not-exhaustive type flags — documents, images, audio, video, XML, binaries, and other.
- **FR-002**: The schema version identifier MUST be incremented so that feature rows produced before this change are distinguishable from rows produced after it.
- **FR-003**: On every app startup, the system MUST detect when the stored feature rows do not match the current schema version and, when detected, MUST automatically trigger a full re-scan of all previously synced emails to repopulate the feature store with attachment-enriched rows.
- **FR-004**: The full re-scan triggered by FR-003 MUST display a user-visible status message explaining why the re-scan is occurring (not just that it is occurring).
- **FR-005**: The feature extraction logic used during both the initial/full scan and the incremental sync MUST extract attachment metadata for every email processed, populating all new attachment fields.
- **FR-006**: The attachment type classification MUST map common MIME types to the seven categories as follows:
  - **Documents**: `application/pdf`, `application/msword`, Office Open XML types (docx, xlsx, pptx), plain text, CSV, RTF
  - **Images**: all `image/*` MIME types
  - **Audio**: all `audio/*` MIME types
  - **Video**: all `video/*` MIME types
  - **XML**: `application/xml`, `text/xml`, `application/xhtml+xml`, and similar structured-markup types
  - **Binaries**: compressed archives (zip, rar, tar, gz, 7z), executables, disk images, and similar binary payloads
  - **Other**: any MIME type that does not match the above categories
- **FR-007**: An email MAY match multiple type flags simultaneously (e.g., a mix of a PDF and an MP3 sets both has-document and has-audio to true).
- **FR-008**: If attachment size information is unavailable for a part, that part MUST be excluded from the total size calculation; the total MUST default to 0 if no size information is available for any part.
- **FR-009**: The existing `has_attachments` boolean field in the feature store MUST remain populated; it MUST NOT be removed or repurposed.
- **FR-010**: The ML training pipeline MUST accept and use all new attachment feature columns without errors after the schema migration.

### Key Entities

- **Email Feature Vector**: The per-email ML training record. Gains nine new fields: `attachment_count` (integer), `total_attachment_size_log` (floating-point log-scale of total attachment bytes), `has_doc_attachments` (0/1), `has_image_attachments` (0/1), `has_audio_attachments` (0/1), `has_video_attachments` (0/1), `has_xml_attachments` (0/1), `has_binary_attachments` (0/1), `has_other_attachments` (0/1).
- **Feature Schema Version**: A version counter stamped on every feature row. Incrementing this value causes the training pipeline to exclude older rows and triggers the automatic re-scan on next startup.
- **Attachment Part**: A single file within an email's payload. Has a MIME type, an optional filename, and an optional byte size. Multiple parts per email are aggregated into the feature vector.

## Success Criteria *(mandatory)*

### Measurable Outcomes

- **SC-001**: After the automatic re-scan completes, 100% of previously scanned email feature rows carry the new schema version and have all nine new attachment fields populated with non-null values.
- **SC-002**: New emails processed by the incremental sync have all nine new attachment fields populated correctly — verified by spot-checking at least 10 emails with known attachment content.
- **SC-003**: The re-scan completes without user intervention beyond starting the app — zero manual steps required.
- **SC-004**: The ML training pipeline runs to completion without errors on a feature set that includes the new attachment columns.
- **SC-005**: Emails with no attachments consistently record `attachment_count = 0`, `total_attachment_size_log = 0`, and all seven type flags as false.
- **SC-006**: The re-scan progress message is visible in the terminal and distinguishable from the normal startup incremental sync message.

## Assumptions

- The Gmail API provides attachment part MIME types and sizes as part of the message payload structure already fetched during feature extraction (no additional API calls are required for most emails).
- Inline images encoded directly in the email body (base64 parts with `Content-Disposition: inline`) are excluded from attachment counting and are already handled by the existing `image_count` body feature.
- The seven type categories cover the vast majority of business email attachments; the "other" bucket handles the remainder.
- The numeric total attachment size will be stored as a log-scale value (consistent with how `email_size_log` is stored) to normalize the distribution for ML training.
- There is no need to store individual attachment filenames or per-attachment breakdown — only the aggregated per-email counts and type flags are needed for this feature.
- The re-scan behavior introduced here reuses the same full-scan code path; it is not a new, separate scan mode.
- Backwards compatibility: the new database columns use defaults so that existing code reading feature rows that pre-date this migration does not crash.
