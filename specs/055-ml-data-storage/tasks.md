# Tasks: ML Data Storage System

**Feature Branch**: `055-ml-data-storage`  
**Date**: 2026-03-14  
**Input**: Design documents from `/specs/055-ml-data-storage/`

---

## Format: `[ID] [P?] [Story] Description`

- **[P]**: Can run in parallel (different files, no dependencies)
- **[Story]**: Which user story this task belongs to (e.g., US1, US2, US3, US4)
- All file paths are relative to repository root

---

## Phase 1: Setup (Shared Infrastructure)

**Purpose**: Create directory structure and base model classes

- [X] T001 Create Models directory at `src/Providers/Storage/TrashMailPanda.Providers.Storage/Models/`
- [X] T002 Create Migrations directory at `src/Providers/Storage/TrashMailPanda.Providers.Storage/Migrations/`
- [X] T003 Create test directory at `src/Tests/TrashMailPanda.Tests/Unit/Storage/Models/`
- [X] T004 Create integration test directory at `src/Tests/TrashMailPanda.Tests/Integration/Storage/`

---

## Phase 2: Foundational (Blocking Prerequisites)

**Purpose**: Core infrastructure that MUST be complete before ANY user story can be implemented

**⚠️ CRITICAL**: No user story work can begin until this phase is complete

- [X] T005 Create schema version tracking table in `src/Providers/Storage/TrashMailPanda.Providers.Storage/Migrations/Migration_001_MLStorage.cs`
- [X] T006 [P] Create FeatureSchema model with version tracking in `src/Providers/Storage/TrashMailPanda.Providers.Storage/Models/FeatureSchema.cs`
- [X] T007 Implement schema migration logic in Migration_001_MLStorage.cs with email_features, email_archive, storage_quota table creation
- [X] T008 Add IEmailArchiveService interface to IStorageProvider in `src/Providers/Storage/TrashMailPanda.Providers.Storage/IEmailArchiveService.cs`
- [X] T009 Create EmailArchiveService class scaffold in `src/Providers/Storage/TrashMailPanda.Providers.Storage/EmailArchiveService.cs`
- [X] T010 [P] Add database connection management helper methods to EmailArchiveService for batch operations
- [X] T011 Unit test for Migration_001_MLStorage in `src/Tests/TrashMailPanda.Tests/Unit/Storage/Migration_001_MLStorageTests.cs`

**Checkpoint**: Foundation ready - user story implementation can now begin in parallel

---

## Phase 3: User Story 1 - Email Feature Vector Storage (Priority: P1) 🎯 MVP

**Goal**: System stores lightweight feature vectors from each processed email to enable ML model training and classification. Features persist independently of full email storage.

**Independent Test**: Process test emails, verify feature vectors are persisted to storage, retrievable for model training, and remain available even after full email is deleted.

### Domain Models for User Story 1

- [X] T012 [P] [US1] Create EmailFeatureVector model with all 38 feature properties in `src/Providers/Storage/TrashMailPanda.Providers.Storage/Models/EmailFeatureVector.cs`
- [X] T013 [P] [US1] Add DataAnnotations validation to EmailFeatureVector (Required, Range, StringLength attributes)
- [X] T014 [P] [US1] Unit test for EmailFeatureVector validation in `src/Tests/TrashMailPanda.Tests/Unit/Storage/Models/EmailFeatureVectorTests.cs`

### Feature Storage Implementation for User Story 1

- [X] T015 [US1] Implement StoreFeatureAsync method in EmailArchiveService with parameterized INSERT statement
- [X] T016 [US1] Implement StoreFeaturesBatchAsync method in EmailArchiveService with transaction batching (500 rows per batch per research.md)
- [X] T017 [US1] Implement GetFeatureAsync method in EmailArchiveService with SELECT by EmailId
- [X] T018 [US1] Implement GetAllFeaturesAsync method in EmailArchiveService with optional schema version filter
- [X] T019 [US1] Add error handling with Result pattern for all feature storage methods (ValidationError, StorageError)

### Tests for User Story 1

- [X] T020 [P] [US1] Unit test for StoreFeatureAsync in `src/Tests/TrashMailPanda.Tests/Unit/Storage/EmailArchiveServiceTests.cs`
- [X] T021 [P] [US1] Unit test for StoreFeaturesBatchAsync with 1000 feature batch in `src/Tests/TrashMailPanda.Tests/Unit/Storage/EmailArchiveServiceTests.cs`
- [X] T022 [P] [US1] Unit test for GetFeatureAsync in `src/Tests/TrashMailPanda.Tests/Unit/Storage/EmailArchiveServiceTests.cs`
- [X] T023 [P] [US1] Unit test for GetAllFeaturesAsync with schema version filter in `src/Tests/TrashMailPanda.Tests/Unit/Storage/EmailArchiveServiceTests.cs`
- [X] T024 [P] [US1] Integration test for feature storage/retrieval workflow in `src/Tests/TrashMailPanda.Tests/Integration/Storage/FeatureStorageIntegrationTests.cs`
- [X] T025 [P] [US1] Integration test verifying features persist after email deletion in `src/Tests/TrashMailPanda.Tests/Integration/Storage/FeatureStorageIntegrationTests.cs`

**Checkpoint**: At this point, User Story 1 should be fully functional - feature vectors can be stored and retrieved for ML training

---

## Phase 4: User Story 2 - Complete Email Archive (Priority: P2)

**Goal**: System stores complete email data when storage capacity allows, enabling future feature regeneration, model retraining with new signals, and audit trails for classification decisions.

**Independent Test**: Store emails, verify stored data integrity, and successfully retrieve complete email data for reprocessing.

### Domain Models for User Story 2

- [ ] T026 [P] [US2] Create EmailArchiveEntry model with email content fields in `src/Providers/Storage/TrashMailPanda.Providers.Storage/Models/EmailArchiveEntry.cs`
- [ ] T027 [P] [US2] Add DataAnnotations validation to EmailArchiveEntry (Required, at least one of BodyText/BodyHtml required)
- [ ] T028 [P] [US2] Unit test for EmailArchiveEntry validation in `src/Tests/TrashMailPanda.Tests/Unit/Storage/Models/EmailArchiveEntryTests.cs`

### Archive Storage Implementation for User Story 2

- [ ] T029 [US2] Implement StoreArchiveAsync method in EmailArchiveService with BLOB storage for email content
- [ ] T030 [US2] Implement StoreArchivesBatchAsync method in EmailArchiveService with transaction batching and quota checks
- [ ] T031 [US2] Implement GetArchiveAsync method in EmailArchiveService with complete email retrieval
- [ ] T032 [US2] Implement DeleteArchiveAsync method in EmailArchiveService preserving feature data (foreign key ON DELETE CASCADE)
- [ ] T033 [US2] Add error handling for archive operations (ValidationError, QuotaExceededError, StorageError)

### Tests for User Story 2

- [ ] T034 [P] [US2] Unit test for StoreArchiveAsync in `src/Tests/TrashMailPanda.Tests/Unit/Storage/EmailArchiveServiceTests.cs`
- [ ] T035 [P] [US2] Unit test for StoreArchivesBatchAsync in `src/Tests/TrashMailPanda.Tests/Unit/Storage/EmailArchiveServiceTests.cs`
- [ ] T036 [P] [US2] Unit test for GetArchiveAsync in `src/Tests/TrashMailPanda.Tests/Unit/Storage/EmailArchiveServiceTests.cs`
- [ ] T037 [P] [US2] Unit test for DeleteArchiveAsync verifying feature preservation in `src/Tests/TrashMailPanda.Tests/Unit/Storage/EmailArchiveServiceTests.cs`
- [ ] T038 [P] [US2] Integration test for complete email archive workflow in `src/Tests/TrashMailPanda.Tests/Integration/Storage/ArchiveStorageIntegrationTests.cs`
- [ ] T039 [P] [US2] Integration test for feature regeneration from archived email in `src/Tests/TrashMailPanda.Tests/Integration/Storage/ArchiveStorageIntegrationTests.cs`

**Checkpoint**: At this point, User Stories 1 AND 2 should both work independently - features and full emails can be stored/retrieved

---

## Phase 5: User Story 3 - Storage Limit Management (Priority: P3)

**Goal**: System monitors storage usage and automatically manages capacity to prevent disk exhaustion. A configurable storage limit (default 50GB) triggers automatic cleanup of oldest data while preserving critical information.

**Independent Test**: Fill storage to configured limit and verify automatic cleanup behavior reduces usage to target threshold.

### Domain Models for User Story 3

- [ ] T040 [P] [US3] Create StorageQuota model with usage metrics in `src/Providers/Storage/TrashMailPanda.Providers.Storage/Models/StorageQuota.cs`
- [ ] T041 [P] [US3] Add DataAnnotations validation to StorageQuota (LimitBytes > 0, CurrentBytes >= 0)
- [ ] T042 [P] [US3] Unit test for StorageQuota calculation logic in `src/Tests/TrashMailPanda.Tests/Unit/Storage/Models/StorageQuotaTests.cs`

### Storage Monitoring Implementation for User Story 3

- [ ] T043 [US3] Implement GetStorageUsageAsync method using PRAGMA page_count and dbstat per research.md decision R2
- [ ] T044 [US3] Implement UpdateStorageLimitAsync method with configuration update and validation
- [ ] T045 [US3] Implement ShouldTriggerCleanupAsync method checking 90% threshold per spec.md FR-005
- [ ] T046 [US3] Add storage monitoring after batch operations in StoreFeaturesBatchAsync and StoreArchivesBatchAsync
- [ ] T047 [US3] Add error handling for storage monitoring operations (ValidationError, StorageError)

### Automatic Cleanup Implementation for User Story 3

- [ ] T048 [US3] Implement ExecuteCleanupAsync method with two-phase cleanup (DELETE oldest emails, then VACUUM) per research.md decision R3
- [ ] T049 [US3] Add cleanup target calculation to reduce usage to 80% of limit per IEmailArchiveService contract
- [ ] T050 [US3] Add batch DELETE logic removing oldest non-user-corrected archives first (1000 row batches)
- [ ] T051 [US3] Add VACUUM execution after DELETE to reclaim disk space per research.md

### Tests for User Story 3

- [ ] T052 [P] [US3] Unit test for GetStorageUsageAsync in `src/Tests/TrashMailPanda.Tests/Unit/Storage/EmailArchiveServiceTests.cs`
- [ ] T053 [P] [US3] Unit test for UpdateStorageLimitAsync validation in `src/Tests/TrashMailPanda.Tests/Unit/Storage/EmailArchiveServiceTests.cs`
- [ ] T054 [P] [US3] Unit test for ShouldTriggerCleanupAsync threshold logic in `src/Tests/TrashMailPanda.Tests/Unit/Storage/EmailArchiveServiceTests.cs`
- [ ] T055 [P] [US3] Unit test for ExecuteCleanupAsync delete ordering in `src/Tests/TrashMailPanda.Tests/Unit/Storage/EmailArchiveServiceTests.cs`
- [ ] T056 [P] [US3] Integration test for automatic cleanup workflow in `src/Tests/TrashMailPanda.Tests/Integration/Storage/StorageCleanupIntegrationTests.cs`
- [ ] T057 [P] [US3] Integration test for storage limit enforcement in `src/Tests/TrashMailPanda.Tests/Integration/Storage/StorageCleanupIntegrationTests.cs`
- [ ] T058 [P] [US3] Integration test verifying VACUUM reclaims space in `src/Tests/TrashMailPanda.Tests/Integration/Storage/StorageCleanupIntegrationTests.cs`

**Checkpoint**: All storage monitoring and automatic cleanup should be operational - storage limits enforced automatically

---

## Phase 6: User Story 4 - User Correction Preservation (Priority: P4)

**Goal**: System prioritizes preservation of emails where users provided classification corrections, as these represent the highest-value training data. User-corrected emails are retained longer and protected from automatic cleanup when possible.

**Independent Test**: Mark emails as user-corrected and verify they survive cleanup cycles that remove other emails.

### User Correction Implementation for User Story 4

- [ ] T059 [US4] Modify ExecuteCleanupAsync to exclude user-corrected emails from initial DELETE query (UserCorrected = 0 filter)
- [ ] T060 [US4] Add secondary cleanup phase in ExecuteCleanupAsync for user-corrected emails only if target not met
- [ ] T061 [US4] Update storage_quota.UserCorrectedCount tracking in batch storage operations
- [ ] T062 [US4] Add error handling for edge case when all emails are user-corrected and limit reached (log warning, allow temporary limit exceed per spec.md edge cases)

### Tests for User Story 4

- [ ] T063 [P] [US4] Unit test for cleanup prioritization of non-corrected emails in `src/Tests/TrashMailPanda.Tests/Unit/Storage/EmailArchiveServiceTests.cs`
- [ ] T064 [P] [US4] Unit test for user-corrected retention during cleanup in `src/Tests/TrashMailPanda.Tests/Unit/Storage/EmailArchiveServiceTests.cs`
- [ ] T065 [P] [US4] Unit test for edge case when only user-corrected emails remain in `src/Tests/TrashMailPanda.Tests/Unit/Storage/EmailArchiveServiceTests.cs`
- [ ] T066 [P] [US4] Integration test verifying 95% retention rate for user-corrected emails per spec.md SC-004 in `src/Tests/TrashMailPanda.Tests/Integration/Storage/StorageCleanupIntegrationTests.cs`

**Checkpoint**: All user stories should now be independently functional - user corrections are preserved with high priority

---

## Phase 7: Polish & Cross-Cutting Concerns

**Purpose**: Improvements that affect multiple user stories and final validation

- [ ] T067 [P] Add XML documentation comments to all IEmailArchiveService interface methods
- [ ] T068 [P] Add XML documentation comments to all domain models (EmailFeatureVector, EmailArchiveEntry, StorageQuota, FeatureSchema)
- [ ] T069 [P] Verify all methods follow Result pattern with proper error types (no exceptions thrown)
- [ ] T070 [P] Verify all SQL queries use parameterized statements (no string concatenation)
- [ ] T071 [P] Add logging for all storage operations (feature stored, archive stored, cleanup executed)
- [ ] T072 [P] Verify security: no email content in logs, all data encrypted via SQLCipher
- [ ] T073 [P] Performance validation: feature storage <100ms, batch retrieval 1000 vectors <500ms per plan.md
- [ ] T074 [P] Run code coverage analysis targeting 95% for Storage provider extension per plan.md
- [ ] T075 [P] Run `dotnet format --verify-no-changes` for code formatting compliance
- [ ] T076 Validate quickstart.md code examples against actual implementation
- [ ] T077 Run all integration tests end-to-end per quickstart.md scenarios
- [ ] T078 Update CLAUDE.md with EmailArchiveService usage patterns
- [ ] T079 Create migration guide documenting schema version 5 changes

---

## Dependencies & Execution Order

### Phase Dependencies

- **Setup (Phase 1)**: No dependencies - can start immediately
- **Foundational (Phase 2)**: Depends on Setup completion - BLOCKS all user stories
- **User Stories (Phases 3-6)**: All depend on Foundational phase completion
  - User stories can then proceed in parallel (if staffed)
  - Or sequentially in priority order (P1 → P2 → P3 → P4)
- **Polish (Phase 7)**: Depends on all user stories being complete

### User Story Dependencies

- **User Story 1 (P1)**: Can start after Foundational (Phase 2) - No dependencies on other stories
- **User Story 2 (P2)**: Can start after Foundational (Phase 2) - Depends on US1 email_features table for foreign key constraint
- **User Story 3 (P3)**: Can start after Foundational (Phase 2) - Operates independently but cleanup affects US2 archives
- **User Story 4 (P4)**: Depends on US3 ExecuteCleanupAsync implementation - extends cleanup logic with prioritization

### Within Each User Story

- Domain models before service implementations
- Service methods before tests
- Unit tests can run in parallel (all marked [P])
- Integration tests after unit tests
- Story complete before moving to next priority

### Parallel Opportunities

**Phase 1 (Setup)**: All 4 tasks can run in parallel

**Phase 2 (Foundational)**: Tasks T006, T010, T011 can run in parallel after T005-T009 complete

**Phase 3 (User Story 1)**:
- All model tasks (T012, T013, T014) can run in parallel
- All unit tests (T020-T023) can run in parallel after implementation complete
- Integration tests (T024-T025) can run in parallel after unit tests pass

**Phase 4 (User Story 2)**:
- All model tasks (T026, T027, T028) can run in parallel
- All unit tests (T034-T037) can run in parallel after implementation complete
- Integration tests (T038-T039) can run in parallel after unit tests pass

**Phase 5 (User Story 3)**:
- All model tasks (T040, T041, T042) can run in parallel
- All unit tests (T052-T055) can run in parallel after implementation complete
- Integration tests (T056-T058) can run in parallel after unit tests pass

**Phase 6 (User Story 4)**:
- All unit tests (T063-T065) can run in parallel after implementation complete
- Integration test (T066) runs after unit tests pass

**Phase 7 (Polish)**: Tasks T067-T075, T078 can run in parallel

---

## Parallel Example: User Story 1

```bash
# After Foundational phase complete, launch all US1 models in parallel:
Task T012: "Create EmailFeatureVector model"
Task T013: "Add DataAnnotations validation"
Task T014: "Unit test for EmailFeatureVector validation"

# After models complete, implement service methods sequentially:
Task T015: "Implement StoreFeatureAsync"
Task T016: "Implement StoreFeaturesBatchAsync"
Task T017: "Implement GetFeatureAsync"
Task T018: "Implement GetAllFeaturesAsync"
Task T019: "Add error handling"

# After service methods complete, launch all unit tests in parallel:
Task T020: "Unit test StoreFeatureAsync"
Task T021: "Unit test StoreFeaturesBatchAsync"
Task T022: "Unit test GetFeatureAsync"
Task T023: "Unit test GetAllFeaturesAsync"

# After unit tests pass, launch integration tests in parallel:
Task T024: "Integration test feature storage workflow"
Task T025: "Integration test features persist after deletion"
```

---

## Implementation Strategy

### MVP First (User Story 1 Only)

1. Complete Phase 1: Setup (T001-T004)
2. Complete Phase 2: Foundational (T005-T011) - **CRITICAL BLOCKER**
3. Complete Phase 3: User Story 1 (T012-T025)
4. **STOP and VALIDATE**: Test User Story 1 independently with quickstart.md examples
5. Feature vectors can now be stored and retrieved for ML training
6. Deploy/demo if ready - basic ML storage capability available

### Incremental Delivery

1. **Foundation** (Phases 1-2): Setup + schema migration → Database ready
2. **MVP** (Phase 3): User Story 1 → Feature storage working → ML training enabled
3. **Full Email Archive** (Phase 4): User Story 2 → Reprocessing capability added
4. **Quota Management** (Phase 5): User Story 3 → Automatic cleanup prevents disk exhaustion
5. **Priority Retention** (Phase 6): User Story 4 → User corrections preserved with priority
6. **Production Ready** (Phase 7): Polish → Full feature complete

Each phase delivers incremental value without breaking previous functionality.

### Parallel Team Strategy

With multiple developers:

1. **Team**: Complete Setup + Foundational together (Phases 1-2)
2. Once Foundational is done:
   - **Developer A**: User Story 1 (Phase 3) - Feature storage
   - **Developer B**: User Story 2 (Phase 4) - Email archives (after US1 table exists)
   - **Developer C**: User Story 3 (Phase 5) - Storage monitoring
   - **Developer D**: User Story 4 (Phase 6) - Cleanup priority (after US3 cleanup exists)
3. **Team**: Polish together (Phase 7)

Note: US2 must wait for US1's email_features table (T012-T015). US4 must wait for US3's ExecuteCleanupAsync (T048).

---

## Notes

- **[P] tasks**: Different files, no dependencies - safe to parallelize
- **[Story] label**: Maps task to specific user story for traceability (US1, US2, US3, US4)
- **Tests**: 95% coverage target per plan.md constitution compliance
- **Result Pattern**: ALL service methods return `Result<T>`, never throw exceptions
- **Security**: All data encrypted via SQLCipher, no email content in logs
- **Performance**: Feature storage <100ms, batch operations optimized per research.md
- Each user story should be independently testable per spec.md
- Commit after each task or logical group
- Stop at any checkpoint to validate story independently
- Schema version tracking ensures backward compatibility per research.md R1