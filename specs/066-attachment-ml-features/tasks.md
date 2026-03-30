# Tasks: Attachment Metadata for ML Email Features

**Feature**: `066-attachment-ml-features`  
**Input**: Design documents from `/specs/066-attachment-ml-features/`  
**Branch**: `066-attachment-ml-features` | **Date**: 2026-03-30  
**Prerequisites**: plan.md ✅, spec.md ✅, research.md ✅, data-model.md ✅, contracts/ ✅, quickstart.md ✅

## Format: `[ID] [P?] [Story?] Description`

- **[P]**: Can run in parallel (different files, no dependencies on incomplete tasks)
- **[Story]**: Which user story this task belongs to ([US1], [US2], [US3])

---

## Phase 1: Setup

**Purpose**: Baseline verification before modifying any files.

- [ ] T001 Verify `dotnet build` passes clean on branch `066-attachment-ml-features` before any changes

**Checkpoint**: Build is green — safe to begin implementation.

---

## Phase 2: Foundational (Blocking Prerequisites)

**Purpose**: Core infrastructure that MUST be complete before any user story can be implemented. All changes here are shared across US1, US2, and US3.

**⚠️ CRITICAL**: No user story work can begin until this phase is complete.

### Storage Schema

- [ ] T002 Increment `FeatureSchema.CurrentVersion` from `1` to `2` in `src/Providers/Storage/TrashMailPanda.Providers.Storage/Models/FeatureSchema.cs`
- [ ] T003 [P] Add 9 new `init`-only attachment properties (`AttachmentCount`, `TotalAttachmentSizeLog`, `HasDocAttachments`, `HasImageAttachments`, `HasAudioAttachments`, `HasVideoAttachments`, `HasXmlAttachments`, `HasBinaryAttachments`, `HasOtherAttachments`) to `src/Providers/Storage/TrashMailPanda.Providers.Storage/Models/EmailFeatureVector.cs` after existing `HasAttachments` — with `[Column("...")]` attributes matching data-model.md
- [ ] T004 Update `OnModelCreating` in `src/Providers/Storage/TrashMailPanda.Providers.Storage/TrashMailPandaDbContext.cs` to add `.HasDefaultValue(0)` / `.HasDefaultValue(0f)` fluent config for all 9 new columns
- [ ] T005 Create EF Core migration by running `dotnet ef migrations add AddAttachmentMlFeatures --project src/Providers/Storage/TrashMailPanda.Providers.Storage --startup-project src/TrashMailPanda/TrashMailPanda --output-dir Migrations`; verify the generated `Up()` adds all 9 columns with correct `DEFAULT 0` — add missing defaults manually if scaffolded incorrectly

### Storage Service Contract

- [ ] T006 [P] Add `Task<Result<bool>> HasOutdatedFeaturesAsync(int currentVersion, CancellationToken ct = default)` method signature to `src/Providers/Storage/TrashMailPanda.Providers.Storage/IEmailArchiveService.cs` per contracts/attachment-feature-interfaces.md
- [ ] T007 Implement `HasOutdatedFeaturesAsync` in `src/Providers/Storage/TrashMailPanda.Providers.Storage/EmailArchiveService.cs` using EF Core `AnyAsync(f => f.FeatureSchemaVersion < currentVersion, ct)` inside the existing connection-lock pattern; return `Result.Success(false)` when table is empty; return `Result.Failure` on DB error

### MIME Classifier (Email Provider)

- [ ] T008 [P] Create `src/Providers/Email/TrashMailPanda.Providers.Email/Services/AttachmentCategory.cs` — `[Flags] internal enum AttachmentCategory { None = 0, Document = 1<<0, Image = 1<<1, Audio = 1<<2, Video = 1<<3, Xml = 1<<4, Binary = 1<<5, Other = 1<<6 }`
- [ ] T009 [P] Create `src/Providers/Email/TrashMailPanda.Providers.Email/Services/AttachmentFeatureSummary.cs` — `internal record AttachmentFeatureSummary(int Count, float TotalSizeLog, int HasDocuments, int HasImages, int HasAudio, int HasVideo, int HasXml, int HasBinaries, int HasOther)` with `internal static AttachmentFeatureSummary Empty { get; }` returning all-zero instance
- [ ] T010 Create `src/Providers/Email/TrashMailPanda.Providers.Email/Services/AttachmentMimeClassifier.cs` — `internal static` class implementing `Classify(string? mimeType, string? fileName = null)` using `static readonly HashSet<string>` for `XmlTypes`, `DocumentTypes`, `BinaryTypes`, `BinaryExtensions`; implement `Summarize(IReadOnlyList<EmailAttachment> attachments)` returning `AttachmentFeatureSummary`; implement `ComputeSizeLog(long totalBytes)` as `(float)Math.Log10(totalBytes + 1)` — follow taxonomy in data-model.md and research.md (RES-002)
- [ ] T011 Create `src/Tests/TrashMailPanda.Tests/Unit/Email/AttachmentMimeClassifierTests.cs` — 100% branch coverage required by constitution; test `Classify` with: every explicit MIME type in each category, `image/*` wildcard, `audio/*` wildcard, `video/*` wildcard, `application/octet-stream` with/without known binary extensions, null/empty input → Other, `multipart/mixed` → excluded; test `Summarize` with: empty list → `Empty`, single attachment per category, multi-type mix, missing size → excluded from total; test `ComputeSizeLog(0)` returns `0f`, large value, max long

### Gmail Training Data Service

- [ ] T012 In `src/Providers/Email/TrashMailPanda.Providers.Email/Services/GmailTrainingDataService.cs` switch `FetchMessageAsync` from `FormatEnum.Metadata` to `FormatEnum.Full`; set `req.Fields = "id,threadId,internalDate,snippet,sizeEstimate,labelIds,payload/mimeType,payload/headers,payload/parts,payload/filename,payload/body/size"` to exclude `payload/body/data` (avoids large base64 body content per RES-001)
- [ ] T013 In `src/Providers/Email/TrashMailPanda.Providers.Email/Services/GmailTrainingDataService.cs` update `BuildFeatureVector` to: collect attachments via `CollectAttachments(msg.Payload, attachments)` (make `CollectAttachments` accessible if needed), call `AttachmentMimeClassifier.Summarize(attachments)`, and populate all 9 new attachment properties plus fix `HasAttachments = attachments.Count > 0 ? 1 : 0` on the returned `EmailFeatureVector`

**Checkpoint**: Foundation complete — storage schema is migrated, MIME classifier is tested, Gmail service fetches FULL-format messages with attachment data. All user story work can now begin.

---

## Phase 3: User Story 1 — Re-scan Existing Emails with Attachment Data (Priority: P1) 🎯 MVP

**Goal**: On startup, detect schema-version mismatch in the feature store and automatically trigger a full re-scan, displaying a clear status message. All previously scanned email feature rows are upgraded to schema version 2 with attachment data.

**Independent Test**: Deploy updated app against a database with completed prior scan (schema version 1 rows). Start the app. Observe terminal for re-scan status message. After completion, query `email_features` and confirm all rows have `feature_schema_version = 2` and non-null attachment columns (may be 0 for emails without attachments).

### Tests for User Story 1

- [ ] T014 [P] [US1] Create `src/Tests/TrashMailPanda.Tests/Unit/Storage/EmailArchiveServiceAttachmentTests.cs` — test `HasOutdatedFeaturesAsync`: returns `false` when table empty, returns `true` when any row has version < current, returns `false` when all rows at current version, returns `Result.Failure` on DB error; use `[Trait("Category", "Unit")]`
- [ ] T015 [P] [US1] Create `src/Tests/TrashMailPanda.Tests/Unit/Email/GmailTrainingDataServiceAttachmentTests.cs` — test `BuildFeatureVector` attachment path: email with PDF → `HasDocAttachments = 1`, `AttachmentCount = 1`, count and size correct; email with no attachments → all attachment fields = 0, `HasAttachments = 0`; email with mixed types → correct multi-flag combination; inline image (no filename, no attachmentId) → not counted as attachment; use `[Trait("Category", "Unit")]`

### Implementation for User Story 1

- [ ] T016 [US1] Update `src/TrashMailPanda/TrashMailPanda/Services/Console/ConsoleStartupOrchestrator.cs` to inject `IEmailArchiveService` and `IGmailTrainingDataService` (if not already present); after Gmail provider initialization succeeds and before incremental sync, call `HasOutdatedFeaturesAsync(FeatureSchema.CurrentVersion, ct)` — if `result.IsSuccess && result.Value`: display `[yellow]⚠ Attachment features require a full re-scan of your emails.[/]` + `[cyan]→ Re-scanning emails to populate attachment metadata (this may take a few minutes)…[/]`, call `RunInitialScanAsync`, on success display `[green]✓ Re-scan complete — attachment features are now up to date.[/]`, on failure log warning and continue (non-fatal per contracts); if `result.IsFailure`: log warning and skip re-scan without blocking startup

**Checkpoint**: User Story 1 fully functional. Existing-database re-scan scenario works end-to-end.

---

## Phase 4: User Story 2 — Capture Attachment Data for New Emails on Incremental Load (Priority: P1)

**Goal**: Incremental syncs on subsequent app starts process new emails with full attachment metadata, extending the ML training corpus continuously.

**Note**: The core implementation for US2 is provided by the foundational changes in Phase 2 (T012 switches `FetchMessageAsync` to FULL format; T013 updates `BuildFeatureVector`). Both the full-scan path (US1) and incremental-sync path (US2) call `BuildFeatureVector`, so both are automatically enriched.

**Independent Test**: After a completed re-scan (US1 done), start the app again. Verify incremental sync runs. Spot-check a newly processed email with a known attachment: confirm its `email_features` row has correct attachment type flags and non-zero `attachment_count`.

### Tests for User Story 2

- [ ] T017 [P] [US2] Add integration test skeleton (skipped per project convention) to `src/Tests/TrashMailPanda.Tests/Integration/Email/GmailTrainingDataServiceAttachmentIntegrationTests.cs` — `[Fact(Skip = "Requires OAuth - set GMAIL_CLIENT_ID/SECRET env vars")]` — verifies incremental sync produces feature rows with populated attachment columns for at least 10 emails; include `[Trait("Category", "Integration")]`

**Checkpoint**: User Story 2 verified — incremental load uses the same enriched `BuildFeatureVector` code path as full scan.

---

## Phase 5: User Story 3 — ML Training Incorporates Attachment Features (Priority: P2)

**Goal**: After US1 and US2 deliver enriched feature data, ML training can consume all 9 new attachment columns. `FeaturePipelineBuilder`, `ModelTrainingPipeline`, and `IncrementalUpdateService` all register and map the new features.

**Independent Test**: Train a model on the enriched dataset. Verify no `IEstimator` column-not-found errors. Confirm the trained model's feature schema includes attachment columns by inspecting the pipeline output schema.

### Implementation for User Story 3

- [ ] T018 [P] [US3] Add 9 new `public float` attachment properties (`AttachmentCount`, `TotalAttachmentSizeLog`, `HasDocAttachments`, `HasImageAttachments`, `HasAudioAttachments`, `HasVideoAttachments`, `HasXmlAttachments`, `HasBinaryAttachments`, `HasOtherAttachments`) to `src/Providers/ML/TrashMailPanda.Providers.ML/Models/ActionTrainingInput.cs` after existing `HasAttachments` property
- [ ] T019 [US3] Add all 9 new column name strings (`nameof(ActionTrainingInput.AttachmentCount)` etc.) to `NumericFeatureColumnNames` array in `src/Providers/ML/TrashMailPanda.Providers.ML/Training/FeaturePipelineBuilder.cs`
- [ ] T020 [P] [US3] Map 9 new attachment fields from `EmailFeatureVector` to `ActionTrainingInput` in `MapToTrainingInput` in `src/Providers/ML/TrashMailPanda.Providers.ML/Training/ModelTrainingPipeline.cs` (e.g. `AttachmentCount = (float)fv.AttachmentCount`)
- [ ] T021 [P] [US3] Map 9 new attachment fields from `EmailFeatureVector` to `ActionTrainingInput` in `MapToTrainingInput` in `src/Providers/ML/TrashMailPanda.Providers.ML/Training/IncrementalUpdateService.cs` (same mapping pattern as T020)

**Checkpoint**: All three user stories complete. Full pipeline — fetch → extract → store → train — uses attachment features end-to-end.

---

## Phase 6: Polish & Cross-Cutting Concerns

**Purpose**: Final build/test/format validation and quickstart confirmation.

- [ ] T022 [P] Run `dotnet build` and resolve any compilation errors introduced by new properties or interface changes
- [ ] T023 [P] Run `dotnet format` and apply any formatting fixes; confirm `dotnet format --verify-no-changes` exits clean
- [ ] T024 Run `dotnet test --filter Category=Unit` and confirm all new unit tests pass (T011, T014, T015)
- [ ] T025 [P] Manually validate quickstart.md Step 1–7 sequence on a local database with existing schema-version-1 data: confirm re-scan fires, terminal messages appear as specified, and `email_features` rows carry `feature_schema_version = 2` post-scan

---

## Dependencies & Execution Order

### Phase Dependencies

- **Phase 1 (Setup)**: No dependencies — start immediately
- **Phase 2 (Foundational)**: Depends on Phase 1 — **BLOCKS all user stories**
- **Phase 3 (US1)**: Depends on Phase 2 completion
- **Phase 4 (US2)**: Depends on Phase 2 completion — can run in parallel with Phase 3
- **Phase 5 (US3)**: Depends on Phase 2 completion — can run in parallel with Phases 3 and 4
- **Phase 6 (Polish)**: Depends on all desired user stories being complete

### Within Phase 2

| Task | Depends On |
|------|-----------|
| T003 | T002 (FeatureSchema must be incremented to understand scope) |
| T004 | T003 (DbContext fluent config for new columns) |
| T005 | T003, T004 (EF migration requires model + context changes) |
| T006 | — (interface only, no implementation dependency) |
| T007 | T006 (interface must exist before implementing) |
| T010 | T008, T009 (classifier needs both enum and summary record) |
| T011 | T010 (tests require classifier implementation) |
| T013 | T010, T003, T012 (needs classifier, new model properties, FULL format) |

### Within Phase 3

| Task | Depends On |
|------|-----------|
| T014 | T007 (tests need storage implementation) |
| T015 | T013 (tests need updated BuildFeatureVector) |
| T016 | T007, T013 (orchestrator needs storage service + enriched GM training service) |

### Within Phase 5

| Task | Depends On |
|------|-----------|
| T019 | T018 (pipeline builder needs ActionTrainingInput properties to exist) |
| T020 | T018 (mapping needs new properties) |
| T021 | T018 (mapping needs new properties) |

---

## Parallel Execution Opportunities

### Phase 2 Parallelism (after T002)

```
T003 [can start] → T004 → T005
T006            → T007
T008, T009      → T010 → T011
T012            
```

Once T003 + T012 + T010 are done: T013 can proceed.

### Phase 3–5 Parallelism (after Phase 2 checkpoint)

```
Phase 3: T014 [P], T015 [P] → T016
Phase 4: T017 [P]
Phase 5: T018 → T019 [P], T020 [P], T021 [P]
```

Phases 3, 4, and 5 can be assigned to different developers simultaneously.

---

## Implementation Strategy

### MVP First (User Stories 1 + 2 Only — P1 scope)

1. Complete Phase 2: Foundational (all tasks T002–T013)
2. Complete Phase 3: User Story 1 (T014–T016)
3. **STOP and VALIDATE**: Start app against old database; confirm re-scan fires; inspect DB
4. Phase 4 verification is automatic (same code path) — add T017 skeleton
5. Deploy/demo if ready (attachment data is live in ML corpus)

### Incremental Delivery

1. Foundation → US1 → validate → commit `[T002–T016]`
2. US2 verification → commit `[T017]`
3. US3 ML pipeline → validate training → commit `[T018–T021]`
4. Polish → final validation → commit `[T022–T025]`

### Suggested MVP Scope

**Just Phase 2 + Phase 3** delivers the complete attachment-enrichment story:
- Schema migrated ✅
- New emails capture attachment data ✅ (foundational)
- Old emails re-scanned automatically ✅
- ML training corpus enriched ✅

Phase 5 (US3) unlocks the ML model consuming the new features — needed before the next model training run.
