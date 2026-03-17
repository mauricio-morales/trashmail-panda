---
description: "Task list for Gmail Provider Extension for Training Data"
---

# Tasks: Gmail Provider Extension for Training Data

**Feature**: `058-gmail-training-data`  
**Input**: Design documents from `/specs/058-gmail-training-data/`  
**Prerequisites**: spec.md ✓, data-model.md ✓, research.md ✓

**Tests**: Not explicitly requested in the feature specification — test tasks are excluded per task generation rules.

---

## Format: `[ID] [P?] [Story] Description`

- **[P]**: Can run in parallel (different files, no dependencies on incomplete tasks)
- **[Story]**: Which user story this task belongs to (US1–US6)
- Exact file paths are included in all task descriptions

---

## Phase 1: Setup

**Purpose**: Remove stale data files and tighten repository hygiene before any new training data writes.

- [X] T001 Remove `data/app.db` and `data/transmail.db` from git tracking (`git rm --cached`) and add `data/*.db` to `.gitignore` at repository root

---

## Phase 2: Foundational (Blocking Prerequisites)

**Purpose**: Database path correctness (US5) + EF entities + migration + value objects.  
All must complete before ANY user story can write training data.

**⚠️ CRITICAL**: No user story work can begin until this phase is complete.

### Database Path Correctness (FR-019, FR-020, FR-021)

- [X] T002 Replace `StorageProviderConfig.DatabasePath` default `"./data/app.db"` with `GetOsDefaultPath()` using `Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData)` combined with `"TrashMailPanda/app.db"` suffix in `src/TrashMailPanda/TrashMailPanda/Services/StorageProviderConfig.cs`
- [X] T003 Update `SqliteStorageProvider.InitializeAsync` to: (1) check secure storage key `storage_database_path` and use it when non-empty, (2) call `Directory.CreateDirectory(Path.GetDirectoryName(DatabasePath)!)` before opening the connection in `src/Providers/Storage/TrashMailPanda.Providers.Storage/SqliteStorageProvider.cs`
- [X] T004 Remove the `DatabasePath` entry from `appsettings.json` so config files cannot introduce relative fallback paths in `src/TrashMailPanda/TrashMailPanda/appsettings.json`

### Value Objects (all parallelizable)

- [X] T005 [P] Create `ClassificationSignal` enum (`AutoDelete=1`, `AutoArchive=2`, `LowConfidence=3`, `Excluded=4`) in `src/Providers/Email/TrashMailPanda.Providers.Email/Models/ClassificationSignal.cs`
- [X] T006 [P] Create `ScanType` static class with `Initial` and `Incremental` string constants in `src/Providers/Email/TrashMailPanda.Providers.Email/Models/ScanType.cs`
- [X] T007 [P] Create `ScanStatus` static class with `InProgress`, `Completed`, `Interrupted`, `PausedStorageFull`, `Recovering`, `NotStarted` string constants in `src/Providers/Email/TrashMailPanda.Providers.Email/Models/ScanStatus.cs`
- [X] T008 [P] Create `TrainingSignalResult` record (`ClassificationSignal Signal`, `float Confidence`) in `src/Providers/Email/TrashMailPanda.Providers.Email/Models/TrainingSignalResult.cs`
- [X] T009 [P] Create `EngagementFlags` record (`bool IsReplied`, `bool IsForwarded`) in `src/Providers/Email/TrashMailPanda.Providers.Email/Models/EngagementFlags.cs`
- [X] T010 [P] Create `ScanSummary` record (`int TotalProcessed`, `int AutoDeleteCount`, `int AutoArchiveCount`, `int LowConfidenceCount`, `int ExcludedCount`, `int LabelsImported`, `TimeSpan Duration`) in `src/Providers/Email/TrashMailPanda.Providers.Email/Models/ScanSummary.cs`

### EF Core Entities (all parallelizable)

- [X] T011 [P] Create `TrainingEmailEntity` with all columns, `[Required]`/`[StringLength]` annotations, and `ICollection<LabelAssociationEntity>` navigation per data-model.md in `src/Providers/Storage/TrashMailPanda.Providers.Storage/Models/TrainingEmailEntity.cs`
- [X] T012 [P] Create `LabelTaxonomyEntity` with all columns and `ICollection<LabelAssociationEntity>` navigation per data-model.md in `src/Providers/Storage/TrashMailPanda.Providers.Storage/Models/LabelTaxonomyEntity.cs`
- [X] T013 [P] Create `LabelAssociationEntity` with `Id` auto-increment PK, `EmailId`/`LabelId` FKs, `IsTrainingSignal`, `IsContextFeature`, and both navigation properties per data-model.md in `src/Providers/Storage/TrashMailPanda.Providers.Storage/Models/LabelAssociationEntity.cs`
- [X] T014 [P] Create `ScanProgressEntity` with `FolderProgressJson`, `HistoryId`, `ScanType`, `Status`, checkpoint fields, and resume-protocol fields per data-model.md in `src/Providers/Storage/TrashMailPanda.Providers.Storage/Models/ScanProgressEntity.cs`
- [X] T015 Add `IsReplied` (`int`, not null, default 0) and `IsForwarded` (`int`, not null, default 0) properties to `EmailFeatureVector` in `src/Providers/Storage/TrashMailPanda.Providers.Storage/Models/EmailFeatureVector.cs`

### EF Core Context and Migration

- [X] T016 Register `DbSet<TrainingEmailEntity>`, `DbSet<LabelTaxonomyEntity>`, `DbSet<LabelAssociationEntity>`, `DbSet<ScanProgressEntity>` in `TrashMailPandaDbContext`; configure the `(EmailId, LabelId)` unique constraint on `label_associations` and all indexes from data-model.md in `src/Providers/Storage/TrashMailPanda.Providers.Storage/TrashMailPandaDbContext.cs`
- [X] T017 Run `dotnet ef migrations add AddGmailTrainingDataSchema` to generate the EF Core migration that creates `training_emails`, `label_taxonomy`, `label_associations`, `scan_progress` tables and adds `IsReplied`/`IsForwarded` columns to `email_features` with `DEFAULT 0` (backward-safe)

### Documentation

- [X] T018 Update the canonical flag table in `docs/architecture/ML_ARCHITECTURE.md` to add `IsReplied` and `IsForwarded` with their detection method (local thread-based back-correction) and signal strength (⭐⭐⭐⭐⭐)

**Checkpoint**: Foundation ready — all user story implementation can now begin.

---

## Phase 3: User Story 1 — Initial Training Data Collection (Priority: P1) 🎯 MVP

**Goal**: Scan Gmail Spam, Trash, Archive, and Inbox folders; assign classification signals per the 8-rule table; checkpoint-commit each batch atomically to SQLite.

**Independent Test**: Initiate a fresh scan, observe emails retrieved from Spam, Trash, and Archive, confirm correct `ClassificationSignal` values are stored per the rule table (Spam → `AutoDelete 0.95`, Archive+Unread → `AutoArchive 0.85`, etc.), and verify no duplicate rows appear on a re-run.

### Service Interfaces (parallelizable)

- [X] T019 [P] [US1] Create `ITrainingSignalAssigner` interface (`AssignSignal(string folder, bool isRead, EngagementFlags engagement) → TrainingSignalResult`) in `src/Providers/Email/TrashMailPanda.Providers.Email/Services/ITrainingSignalAssigner.cs`
- [X] T020 [P] [US1] Create `IGmailTrainingDataService` interface (`RunInitialScanAsync(string accountId, CancellationToken ct) → Result<ScanSummary>`, `RunIncrementalScanAsync(string accountId, CancellationToken ct) → Result<ScanSummary>`) in `src/Providers/Email/TrashMailPanda.Providers.Email/Services/IGmailTrainingDataService.cs`
- [X] T021 [P] [US1] Create `ITrainingEmailRepository` interface (`UpsertBatchAsync`, `RunBackCorrectionAsync`, `ReDeriveSignalsForThreadsAsync`, `GetByEmailIdAsync`) in `src/Providers/Storage/TrashMailPanda.Providers.Storage/ITrainingEmailRepository.cs`

### Implementation

- [X] T022 [US1] Implement `TrainingSignalAssigner` with the priority-ordered 8-rule table from research.md: (1) Spam→AutoDelete 0.95 (engagement ignored), (2) Archive+engaged→Excluded, (3) Trash+engaged→LowConfidence 0.30, (4) Trash→AutoDelete 0.90, (5) Archive+Unread→AutoArchive 0.85, (6) Archive+Read→Excluded, (7) Inbox+Unread→LowConfidence 0.20, (8) Inbox+Read→Excluded in `src/Providers/Email/TrashMailPanda.Providers.Email/Services/TrainingSignalAssigner.cs`
- [X] T023 [US1] Implement `TrainingEmailRepository` with batch upsert (`ON CONFLICT(EmailId) DO UPDATE`) preserving `ImportedAt`, updating all other columns to reflect current state in `src/Providers/Storage/TrashMailPanda.Providers.Storage/TrainingEmailRepository.cs`
- [X] T024 [US1] Implement `GmailTrainingDataService.RunInitialScanAsync` — scan folders in signal-value order (Spam → Trash → Sent → Archive → Inbox), fetch 100 messages/page via `messages.list`, build `TrainingEmailEntity` from each message, call `ITrainingEmailRepository.UpsertBatchAsync` per page, add 50ms inter-page delay in `src/Providers/Email/TrashMailPanda.Providers.Email/Services/GmailTrainingDataService.cs`
- [X] T025 [US1] Add Spectre.Console live progress display — show current folder, processed count, and per-signal running totals as the scan progresses in `src/TrashMailPanda/TrashMailPanda/Services/GmailTrainingScanCommand.cs`
- [X] T026 [US1] Register `ITrainingSignalAssigner → TrainingSignalAssigner`, `IGmailTrainingDataService → GmailTrainingDataService`, `ITrainingEmailRepository → TrainingEmailRepository` in the DI container in `src/TrashMailPanda/TrashMailPanda/Services/ServiceCollectionExtensions.cs`

**Checkpoint**: User Story 1 complete — initial scan collects and stores email signals independently.

---

## Phase 4: User Story 2 — Engagement Signals Protect Valuable Emails (Priority: P2)

**Goal**: Detect replied-to and forwarded emails via local thread-based back-correction (zero extra API calls); update `ClassificationSignal` and `IsValid` atomically per engagement override rules.

**Independent Test**: Import a set of emails including sent-folder messages in the same threads; verify `IsReplied=true` is set on all non-SENT emails sharing a `ThreadId` with a SENT message, `Archive+replied→Excluded`, `Trash+replied→LowConfidence`, and Spam signals are preserved regardless of engagement.

- [X] T027 [P] [US2] Create `IGmailEngagementDetector` interface (`RunBackCorrectionAsync(IEnumerable<string> threadIds, DbContext ctx, DateTime now) → Task<int>`) in `src/Providers/Email/TrashMailPanda.Providers.Email/Services/IGmailEngagementDetector.cs`
- [X] T028 [P] [US2] Add `SubjectPrefix` capture logic — when building `TrainingEmailEntity` for SENT-folder messages only, populate `SubjectPrefix` with the first 10 characters of the subject line to enable local `IsForwarded` matching in `src/Providers/Email/TrashMailPanda.Providers.Email/Services/GmailTrainingDataService.cs`
- [X] T029 [US2] Implement back-correction SQL in `TrainingEmailRepository.RunBackCorrectionAsync` — two-step UPDATE: (1) set `IsReplied=1` on all non-SENT emails sharing `ThreadId` with any newly stored SENT message, (2) set `IsForwarded=1` on non-SENT emails whose thread has a SENT message with `SubjectPrefix IN ('Fwd:', 'FW:', 'Fw:')`, both updates scoped by `AccountId` in `src/Providers/Storage/TrashMailPanda.Providers.Storage/TrainingEmailRepository.cs`
- [X] T030 [US2] Implement `TrainingEmailRepository.ReDeriveSignalsForThreadsAsync` — after back-correction, re-apply signal rules using `ITrainingSignalAssigner` for all rows where `IsReplied` or `IsForwarded` changed; update `ClassificationSignal`, `SignalConfidence`, and `IsValid` (set `IsValid=false` when new signal is `Excluded`) atomically in `src/Providers/Storage/TrashMailPanda.Providers.Storage/TrainingEmailRepository.cs`
- [X] T031 [US2] Integrate back-correction into `GmailTrainingDataService` checkpoint protocol — after `UpsertBatchAsync`, call `RunBackCorrectionAsync` then `ReDeriveSignalsForThreadsAsync` within the same SQLite transaction before committing in `src/Providers/Email/TrashMailPanda.Providers.Email/Services/GmailTrainingDataService.cs`

**Checkpoint**: User Story 2 complete — engagement flags are resolved locally and override classification signals correctly.

---

## Phase 5: User Story 3 — Existing Gmail Labels Inform the Classifier (Priority: P3)

**Goal**: Import the user's full label taxonomy; associate each email's user-created labels as positive training signals and system labels as context features; support multi-label emails.

**Independent Test**: Import a user's label list; verify user-created labels have `LabelType="User"` and `IsTrainingSignal=true` on associations; system labels (`INBOX`, `UNREAD`, etc.) have `LabelType="System"` and `IsContextFeature=true`; an email with 3 labels has 3 association rows.

### Repository Interfaces (parallelizable)

- [X] T032 [P] [US3] Create `ILabelTaxonomyRepository` interface (`UpsertBatchAsync`, `GetAllLabelsAsync`, `UpdateUsageCountsAsync`, `GetLabelStatisticsAsync`) in `src/Providers/Storage/TrashMailPanda.Providers.Storage/ILabelTaxonomyRepository.cs`
- [X] T033 [P] [US3] Create `ILabelAssociationRepository` interface (`ReconcileAssociationsAsync(string emailId, IEnumerable<string> currentLabelIds)`, `GetByEmailIdAsync`) in `src/Providers/Storage/TrashMailPanda.Providers.Storage/ILabelAssociationRepository.cs`

### Implementation

- [X] T034 [US3] Implement `LabelTaxonomyRepository` — define known Gmail system label ID set (`INBOX`, `SENT`, `TRASH`, `SPAM`, `STARRED`, `IMPORTANT`, `UNREAD`, `DRAFT`, `CATEGORY_*`); classify each label using Gmail API `type` field with system-ID-set fallback; upsert by `LabelId` in `src/Providers/Storage/TrashMailPanda.Providers.Storage/LabelTaxonomyRepository.cs`
- [X] T035 [US3] Implement `LabelAssociationRepository.ReconcileAssociationsAsync` — for a given email, insert associations for labels not yet recorded, delete associations for labels no longer present, setting `IsTrainingSignal=true` for user labels and `IsContextFeature=true` for system labels in `src/Providers/Storage/TrashMailPanda.Providers.Storage/LabelAssociationRepository.cs`
- [X] T036 [US3] Call `users.labels.list` at the start of each scan in `GmailTrainingDataService` and upsert the full taxonomy via `ILabelTaxonomyRepository.UpsertBatchAsync` before beginning folder traversal in `src/Providers/Email/TrashMailPanda.Providers.Email/Services/GmailTrainingDataService.cs`
- [X] T037 [US3] Call `ILabelAssociationRepository.ReconcileAssociationsAsync` for each email after upsert in the batch checkpoint — reconcile within the same SQLite transaction in `src/Providers/Email/TrashMailPanda.Providers.Email/Services/GmailTrainingDataService.cs`
- [X] T038 [US3] Register `ILabelTaxonomyRepository → LabelTaxonomyRepository`, `ILabelAssociationRepository → LabelAssociationRepository` in the DI container in `src/TrashMailPanda/TrashMailPanda/Services/ServiceCollectionExtensions.cs`

**Checkpoint**: User Story 3 complete — label taxonomy imported and all email-label associations are stored correctly.

---

## Phase 6: User Story 4 — Large Email History Fetched Without Disrupting Gmail Access (Priority: P4)

**Goal**: Per-folder cursor tracking, Polly exponential-backoff retry, scan resumability across sessions, pageToken expiry fallback, storage pressure detection, and incremental History API scans.

**Independent Test**: Simulate 10,000+ emails across folders with an injected 429 response mid-batch; verify the scan pauses, retries with backoff, resumes from the committed cursor (not the beginning), and produces no duplicate records.

### Repository Interface

- [X] T039 [P] [US4] Create `IScanProgressRepository` interface (`GetActiveAsync(string accountId)`, `CreateAsync`, `UpdateFolderProgressAsync`, `SaveHistoryIdAsync`, `MarkCompletedAsync`, `MarkInterruptedAsync`) in `src/Providers/Storage/TrashMailPanda.Providers.Storage/IScanProgressRepository.cs`

### Implementation

- [X] T040 [US4] Implement `ScanProgressRepository` — serialize/deserialize `FolderProgressJson` using `System.Text.Json`, enforce the active-scan constraint (only one `InProgress`/`PausedStorageFull` row per account), and expose atomic folder-state updates in `src/Providers/Storage/TrashMailPanda.Providers.Storage/ScanProgressRepository.cs`
- [X] T041 [US4] Integrate `IScanProgressRepository` into `GmailTrainingDataService` checkpoint protocol — update `FolderProgressJson` (new `pageToken`, incremented `processedCount`), `ProcessedCount`, `LastProcessedEmailId`, and `UpdatedAt` within the same SQLite transaction as the batch upsert in `src/Providers/Email/TrashMailPanda.Providers.Email/Services/GmailTrainingDataService.cs`
- [X] T042 [US4] Implement Polly `WaitAndRetryAsync` policy in `GmailTrainingDataService` — handle `GoogleApiException` with HTTP codes 429, 503, 500; 5 retries; base 2s exponential backoff with ±20% jitter; honor `Retry-After` header when present; delegate to existing `IGmailRateLimitHandler` in `src/Providers/Email/TrashMailPanda.Providers.Email/Services/GmailTrainingDataService.cs`
- [X] T043 [US4] Implement scan resume protocol on scan start — query `IScanProgressRepository.GetActiveAsync`, parse `FolderProgressJson`, skip `Completed` folders, restart `InProgress` folder from its saved `pageToken`, display resume prompt via Spectre.Console in `src/Providers/Email/TrashMailPanda.Providers.Email/Services/GmailTrainingDataService.cs`
- [X] T044 [US4] Implement pageToken expiry fallback — on Gmail 400/410 response when using a saved `pageToken`, clear the token, set that folder's status to `"Recovering"` in `FolderProgressJson`, and re-scan the folder from its beginning using upsert semantics in `src/Providers/Email/TrashMailPanda.Providers.Email/Services/GmailTrainingDataService.cs`
- [X] T045 [US4] Implement storage pressure detection — before each batch write, call `IEmailArchiveService.ShouldTriggerCleanupAsync()`; if quota ≥ 90%, set folder status to `"PausedStorageFull"`, save `ScanProgressEntity`, display Spectre.Console warning with cleanup instructions (`[yellow]⚠ Training scan paused — storage quota reached…[/]`), and exit cleanly in `src/Providers/Email/TrashMailPanda.Providers.Email/Services/GmailTrainingDataService.cs`
- [X] T046 [US4] Implement `GmailTrainingDataService.RunIncrementalScanAsync` — call `users.history.list` with `startHistoryId` from `ScanProgressEntity.HistoryId`; process label changes, new messages, and deletions; fall back to targeted per-email `messages.get` re-check if `historyId` expired (Gmail returns 404) in `src/Providers/Email/TrashMailPanda.Providers.Email/Services/GmailTrainingDataService.cs`
- [X] T047 [US4] Save the current `historyId` (from `users.getProfile`) to `ScanProgressEntity` upon successful completion of a full initial scan in `src/Providers/Email/TrashMailPanda.Providers.Email/Services/GmailTrainingDataService.cs`
- [X] T048 [US4] Register `IScanProgressRepository → ScanProgressRepository` in the DI container in `src/TrashMailPanda/TrashMailPanda/Services/ServiceCollectionExtensions.cs`

**Checkpoint**: User Story 4 complete — large mailboxes scan safely with crash recovery, rate-limit resilience, and incremental re-scan support.

---

## Phase 7: User Story 6 — Label Usage Frequency Guides Classifier Priorities (Priority: P6)

**Goal**: Track per-label email counts so the ML pipeline can weight high-frequency labels more strongly.

**Independent Test**: After importing 100 emails with a mix of labels, call `GetLabelStatisticsAsync` and verify `UsageCount` on each label matches the actual count of associations in `label_associations`, ordered by count descending.

- [X] T049 [US6] Implement `LabelTaxonomyRepository.UpdateUsageCountsAsync` — execute a single SQL `UPDATE label_taxonomy SET UsageCount = (SELECT COUNT(*) FROM label_associations WHERE label_associations.LabelId = label_taxonomy.LabelId), UpdatedAt = :now` to recalculate all counts from the join (not incremental, to prevent drift) in `src/Providers/Storage/TrashMailPanda.Providers.Storage/LabelTaxonomyRepository.cs`
- [X] T050 [US6] Implement `ILabelTaxonomyRepository.GetLabelStatisticsAsync` returning all labels for an account ordered by `UsageCount DESC` in `src/Providers/Storage/TrashMailPanda.Providers.Storage/LabelTaxonomyRepository.cs`
- [X] T051 [US6] Call `UpdateUsageCountsAsync` at the end of each scan (after the final batch checkpoint) in `GmailTrainingDataService` — outside the per-batch transaction, as a single post-scan reconciliation step in `src/Providers/Email/TrashMailPanda.Providers.Email/Services/GmailTrainingDataService.cs`

**Checkpoint**: User Story 6 complete — label frequency counts are accurate and queryable for ML weighting.

---

## Phase 8: Polish & Cross-Cutting Concerns

**Purpose**: Hardening, graceful edge-case handling, scan completion summary, and final DI validation.

- [X] T052 Add graceful empty-folder handling — when `messages.list` returns 0 results for a folder (or folder does not exist), immediately mark that folder `"Completed"` in `FolderProgressJson` and move to the next folder without error in `src/Providers/Email/TrashMailPanda.Providers.Email/Services/GmailTrainingDataService.cs`
- [X] T053 Add graceful partial-data handling — when a `messages.get` response is missing fields (e.g., no `labelIds`), store available fields and default `IsReplied=false`, `IsForwarded=false`, `IsRead=false` per FR-018; log a warning via `ILogger` but do not fail the batch in `src/Providers/Email/TrashMailPanda.Providers.Email/Services/GmailTrainingDataService.cs`
- [X] T054 Add Spectre.Console scan completion summary — after `RunInitialScanAsync` or `RunIncrementalScanAsync` returns, display `ScanSummary` totals (processed, auto-delete, auto-archive, low-confidence, excluded, labels imported, duration) using `AnsiConsole.MarkupLine("[green]✓[/] Scan complete: …")` in `src/TrashMailPanda/TrashMailPanda/Services/GmailTrainingScanCommand.cs`
- [X] T055 [P] Validate full DI container builds cleanly — build the service provider in a test/startup path and confirm no missing registrations or circular dependencies for the new training-data services

---

## Dependencies & Execution Order

### Phase Dependencies

- **Setup (Phase 1)**: No dependencies — start immediately
- **Foundational (Phase 2)**: Depends on Setup — **BLOCKS all user stories**
- **US1 (Phase 3)** through **US6 (Phase 7)**: All depend on Foundational completion; can proceed in priority order or in parallel by separate developers
- **Polish (Phase 8)**: Depends on all targeted user stories being complete

### User Story Dependencies

| Story | Depends On | Notes |
|-------|------------|-------|
| US1 (P1) | Foundational | Independent of all other stories |
| US2 (P2) | US1 | Back-correction extends US1 checkpoint protocol |
| US3 (P3) | Foundational | Independent of US1/US2; label taxonomy is parallel-safe |
| US4 (P4) | US1, US3 | Scan progress wraps US1 batching; labels need taxonomy repo |
| US5 (P2) | — | Already completed in Foundational phase (T002–T004) |
| US6 (P6) | US3 | UsageCount recalculation depends on label_associations |

### Parallel Opportunities

**Within Phase 2 (Foundational)**:
- T005–T010 (value objects): all independent, run in parallel
- T011–T014 (EF entities): all independent, run in parallel

**Within Phase 3 (US1)**:
- T019, T020, T021 (interfaces): all independent, run in parallel

**Within Phase 5 (US3)**:
- T032, T033 (interfaces): independent, run in parallel

---

## Parallel Execution Examples

### Phase 2 — Foundational

```text
# After T002-T004 (DB path fix):
Parallel: T005 (ClassificationSignal) | T006 (ScanType) | T007 (ScanStatus) |
          T008 (TrainingSignalResult) | T009 (EngagementFlags) | T010 (ScanSummary)

Parallel: T011 (TrainingEmailEntity) | T012 (LabelTaxonomyEntity) |
          T013 (LabelAssociationEntity) | T014 (ScanProgressEntity)

Sequential: T015 (extend EmailFeatureVector) → T016 (TrashMailPandaDbContext) → T017 (migration)
```

### Phase 3 — US1

```text
Parallel: T019 (ITrainingSignalAssigner) | T020 (IGmailTrainingDataService) |
          T021 (ITrainingEmailRepository)

Sequential: T022 (TrainingSignalAssigner) + T023 (TrainingEmailRepository) → T024 (GmailTrainingDataService)
            → T025 (console progress) → T026 (DI registration)
```

---

## Implementation Strategy

### MVP Scope (User Story 1 Only)

1. Complete **Phase 1**: Setup (T001)
2. Complete **Phase 2**: Foundational (T002–T018) — critical gate
3. Complete **Phase 3**: User Story 1 (T019–T026)
4. **STOP and VALIDATE**: Run initial scan; check `training_emails` table in OS-standard DB; verify signal assignments
5. Deliver MVP — usable training data store from day one

### Incremental Delivery

1. MVP via US1 → validate → demonstrates end-to-end value
2. Add US2 (engagement signals) → prevents highest-impact false positives
3. Add US3 (label taxonomy) → adds user-intent training signals
4. Add US4 (rate limiting + resumability) → production hardening for large mailboxes
5. Add US6 (label frequency) → improves classifier weighting

---

## Summary

| Phase | Tasks | Stories | Parallel Tasks |
|-------|-------|---------|----------------|
| Phase 1: Setup | T001 | — | 0 |
| Phase 2: Foundational | T002–T018 (17 tasks) | US5 prereq | 12 (T005–T014) |
| Phase 3: US1 | T019–T026 (8 tasks) | US1 | 3 (T019–T021) |
| Phase 4: US2 | T027–T031 (5 tasks) | US2 | 2 (T027–T028) |
| Phase 5: US3 | T032–T038 (7 tasks) | US3 | 2 (T032–T033) |
| Phase 6: US4 | T039–T048 (10 tasks) | US4 | 1 (T039) |
| Phase 7: US6 | T049–T051 (3 tasks) | US6 | 0 |
| Phase 8: Polish | T052–T055 (4 tasks) | — | 1 (T055) |
| **Total** | **55 tasks** | **6 stories** | **21 parallelizable** |

**Suggested MVP**: Phases 1–3 (26 tasks) — delivers a working initial scan writing correctly to the OS-standard database path.
