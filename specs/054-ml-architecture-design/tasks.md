# Tasks: ML Architecture Design

**Feature Branch**: `054-ml-architecture-design`  
**Input**: Design documents from `/specs/054-ml-architecture-design/`  
**Prerequisites**: plan.md ✅, spec.md ✅, research.md ✅, data-model.md ✅, contracts/ ✅  
**Output**: Three architecture documents in `/docs/`

**Note**: This is a **documentation-only deliverable**. No source code changes. Tests are NOT requested for this feature.

## Format: `[ID] [P?] [Story] Description`

- **[P]**: Can run in parallel (different files, no dependencies)
- **[Story]**: Which user story this task belongs to (e.g., US1, US2, US3)
- Include exact file paths in descriptions

---

## Phase 1: Setup (Shared Infrastructure)

**Purpose**: Prepare documentation workspace and validate prerequisites

- [ ] T001 Validate all prerequisite documents exist and are complete (plan.md, spec.md, research.md, data-model.md, contracts/)
- [ ] T002 Create documentation outline structure for three target documents

---

## Phase 2: Foundational (Blocking Prerequisites)

**Purpose**: Research synthesis and cross-document coordination that MUST be complete before ANY user story can be implemented

**⚠️ CRITICAL**: No user story documentation work can begin until this phase is complete

- [ ] T003 Extract and consolidate architecture decisions from research.md (R1-R10)
- [ ] T004 [P] Map entities from data-model.md to their respective document sections
- [ ] T005 [P] Extract interface specifications from contracts/ directory (IClassificationProvider, IFeatureExtractor, IModelTrainer)
- [ ] T006 Create canonical folder mapping table from research.md R9 (Gmail, IMAP, Outlook → canonical semantics)
- [ ] T007 Extract performance requirements table from research.md R6
- [ ] T008 Extract archive reclamation workflow from research.md R8

**Checkpoint**: Foundation ready - user story documentation can now begin in parallel

---

## Phase 3: User Story 1 - Architecture Review and Validation (Priority: P1) 🎯 MVP

**Goal**: Create comprehensive ML_ARCHITECTURE.md document defining system architecture, component interactions, and provider integration

**Independent Test**: Architecture document can be reviewed independently without any implementation. Success measured by completeness against FR-001 through FR-004, FR-014, FR-015 requirements.

### Documentation for User Story 1

- [ ] T009 [US1] Create docs/ML_ARCHITECTURE.md with document header and ToC
- [ ] T010 [US1] Write Overview section: project context, architectural shift from LLM to local ML.NET
- [ ] T011 [US1] Write System Architecture section: layer separation (UI, classification engine, feature extraction, model training, data layer) per FR-001
- [ ] T012 [US1] Document IClassificationProvider integration per FR-002: IProvider<TConfig> pattern, Result<T> returns, DI lifecycle
- [ ] T013 [US1] Write Archive Reclamation Workflow section per FR-003 and FR-015: scan → bootstrap → train → classify → recommend workflow with TriageArchiveAsync method specification
- [ ] T014 [US1] Document canonical folder abstraction per FR-004: mapping tables for Gmail labels, IMAP folders, Outlook folders+categories to canonical semantics (Inbox, Archive, Trash, Spam, Sent, Flagged, Important)
- [ ] T015 [US1] Write Data Storage section per FR-014: SQLCipher encryption for email_features and email_archive tables, configurable 50GB storage cap, pruning strategy
- [ ] T016 [US1] Document component interaction diagrams: data flow from IEmailProvider → IFeatureExtractor → IClassificationProvider → IModelTrainer
- [ ] T017 [US1] Write Provider Integration section (User Story 4 content): how IEmailProvider exposes full email data for feature extraction, canonical email abstraction
- [ ] T018 [US1] Document provider adapter contract: mapping native folder/label IDs to canonical names, mapping native flags to canonical flags, stable ProviderMessageId
- [ ] T019 [US1] Create provider compatibility matrix per FR-004: Gmail, IMAP, Outlook mapping tables with thread support, label/folder model, rate limits
- [ ] T020 [US1] Write Performance Targets section per FR-013: classification <10ms/email, batch 100 emails <100ms, training 10K emails <2min, 100K emails <5min
- [ ] T021 [US1] Write Security & Privacy section: local processing, no external API calls, SQLCipher at rest, OS keychain for sensitive config
- [ ] T022 [US1] Document Constitution compliance: provider-agnostic (IProvider pattern), Result<T> pattern, security-first, one public type per file
- [ ] T023 [US1] Add Multi-Provider Support section: CanonicalEmailMetadata type, provider-agnostic feature engineering, future providers (Yahoo, iCloud, ProtonMail, generic IMAP)
- [ ] T024 [US1] Write Future Extension Points section: Phase 2+ topic signals (LDA, ONNX embeddings), optional LLM integration
- [ ] T025 [US1] Add References section linking to spec.md, data-model.md, contracts/, research.md

**Checkpoint**: At this point, User Story 1 (ML_ARCHITECTURE.md) should be complete and reviewable independently

---

## Phase 4: User Story 2 - Feature Engineering Planning (Priority: P1)

**Goal**: Create detailed FEATURE_ENGINEERING.md specification for transforming raw emails into structured feature vectors for local classification

**Independent Test**: Feature specifications can be validated by mapping sample emails (from different providers) to feature vectors on paper, verifying all required signals are extractable.

### Documentation for User Story 2

- [ ] T026 [US2] Create docs/FEATURE_ENGINEERING.md with document header and ToC
- [ ] T027 [US2] Write Overview section: purpose of feature extraction, input (CanonicalEmailMetadata), output (EmailFeatureVector)
- [ ] T028 [US2] Document Tier 1 Structured Features per FR-005: sender domain, authentication (SPF/DKIM/DMARC), time patterns, folder placement, flags - complete enumeration of 40+ features
- [ ] T029 [US2] Document archive-specific features per FR-006: EmailAgeDays, SenderFrequency, IsArchived, SourceFolder (canonical values), WasInTrash, WasInSpam, IsInInbox, IsStarred, IsImportant
- [ ] T030 [US2] Document Tier 2 Text Features per FR-005: subject/body TF-IDF, link counts, tracking pixel detection, unsubscribe link patterns
- [ ] T031 [US2] Write EmailFeatureVector Schema section per FR-007: all 40+ fields with types, nullability, default values, FeatureSchemaVersion field
- [ ] T032 [US2] Document extraction from canonical email metadata per FR-008: CanonicalEmailMetadata → EmailFeatureVector mapping, provider-agnostic extraction logic
- [ ] T033 [US2] Create provider compatibility section: how each provider (Gmail, IMAP, Outlook) maps to canonical metadata enabling identical feature extraction
- [ ] T034 [US2] Write Feature Extraction Pipeline section: IFeatureExtractor interface usage, Extract() and ExtractBatch() methods, error handling with Result<T>
- [ ] T035 [US2] Document schema versioning per FR-016: FeatureSchemaVersion field, compatibility checks, feature regeneration workflow when schema changes
- [ ] T036 [US2] Write Performance Characteristics section per FR-013: <5ms for structured features, <50ms including text features, batch optimization strategy
- [ ] T037 [US2] Add Phase 2+ Topic Signals section from research.md R10: nullable fields (TopicClusterId, TopicDistributionJson, SenderCategory, SemanticEmbeddingJson), LDA/ONNX future extension
- [ ] T038 [US2] Document edge cases: missing headers, null body content, threading-unsupported providers, multi-label Gmail vs single-folder IMAP
- [ ] T039 [US2] Add Feature Importance section: which features provide strongest signals for archive reclamation vs incoming email classification
- [ ] T040 [US2] Add References section linking to contracts/IFeatureExtractor.md, data-model.md, research.md R2/R6/R9/R10

**Checkpoint**: At this point, User Story 2 (FEATURE_ENGINEERING.md) should be complete and reviewable independently

---

## Phase 5: User Story 3 - Training Workflow Definition (Priority: P1)

**Goal**: Create complete MODEL_TRAINING_PIPELINE.md documentation for model training workflow including cold start handling, incremental retraining, and model versioning

**Independent Test**: Training workflow can be validated by walking through scenarios (new user with archived emails, user with corrections, scheduled retrain) and verifying all states and transitions are documented.

### Documentation for User Story 3

- [ ] T041 [US3] Create docs/MODEL_TRAINING_PIPELINE.md with document header and ToC
- [ ] T042 [US3] Write Overview section: training pipeline purpose, ML.NET framework choice, three-phase training approach
- [ ] T043 [US3] Document three training phases per FR-009: Cold Start (<100 labels, rule-based), Hybrid (100-500 labels, ML+rules), ML Primary (500+ labels, ML with rule overrides)
- [ ] T044 [US3] Write Cold Start Procedures per FR-010: bootstrapping from existing folder placement using canonical folders across all providers (Trash/Spam → delete, Starred/Flagged/Inbox → keep, Archive → triage target)
- [ ] T045 [US3] Document training data sources: explicit user corrections (highest signal), implicit user actions, mailbox folder placement, user rules matches - priority order
- [ ] T046 [US3] Write Archive Reclamation Bootstrapping per FR-010 and FR-017: scan all folders → extract folder signals → train initial model → classify archive → recommend deletions workflow
- [ ] T047 [US3] Document provider-agnostic bootstrapping per FR-010: Gmail labels, IMAP folders, Outlook folders+categories all map to same canonical training labels
- [ ] T048 [US3] Write Retraining Triggers section per FR-011: automatic (50+ corrections, 7-day schedule) and manual user-initiated triggers
- [ ] T049 [US3] Document incremental update strategy per FR-011: warm-start from existing model, batch retrain approach, feature vector reuse
- [ ] T050 [US3] Write Model Versioning section per FR-012: file naming (model_v{version}_{timestamp}.zip), metadata in ml_models table, active model pointer
- [ ] T051 [US3] Document rollback procedure per FR-012: change active model pointer, retain 5 most recent versions, automatic pruning strategy
- [ ] T052 [US3] Write Model Lifecycle States section: transitions between Cold Start → Training → Active → Rollback → Pruned
- [ ] T053 [US3] Document IModelTrainer interface usage: TrainAsync(), EvaluateAsync(), RollbackAsync(), ShouldRetrainAsync(), PruneOldModelsAsync()
- [ ] T054 [US3] Write Training Performance section per FR-013: 10K emails <2min, 100K emails <5min, incremental retrain <30s
- [ ] T055 [US3] Document archive triage integration: how user decisions from TriageArchiveAsync feed back into training data
- [ ] T056 [US3] Write Model Evaluation section: accuracy, macro-F1, micro-F1, per-class metrics, validation split strategy
- [ ] T057 [US3] Document failure scenarios: insufficient training data, accuracy regression, corrupted model file, feature schema mismatch
- [ ] T058 [US3] Add Training Event Audit Log section: training_events table, trigger reasons, status tracking, metrics snapshot
- [ ] T059 [US3] Add References section linking to contracts/IModelTrainer.md, data-model.md, research.md R3/R5/R8

**Checkpoint**: At this point, User Story 3 (MODEL_TRAINING_PIPELINE.md) should be complete and reviewable independently

---

## Phase 6: Polish & Cross-Cutting Concerns

**Purpose**: Review, validate, and finalize all documentation for completeness and consistency

- [ ] T060 [P] Cross-reference validation: Ensure all spec.md requirements (FR-001 through FR-017) are addressed in the three documents
- [ ] T061 [P] Cross-document consistency check: Verify terminology consistency across ML_ARCHITECTURE.md, FEATURE_ENGINEERING.md, MODEL_TRAINING_PIPELINE.md
- [ ] T062 [P] Constitution compliance review: Verify all three documents comply with project constitution (provider-agnostic, Result<T> pattern, security-first, etc.)
- [ ] T063 [P] Performance targets verification: Ensure all performance targets from FR-013 are documented in appropriate sections
- [ ] T064 [P] Provider compatibility verification: Ensure canonical folder mapping tables are complete and consistent across all three documents
- [ ] T065 Update specs/054-ml-architecture-design/quickstart.md with final document locations and key takeaways
- [ ] T066 Run constitution check against all three documents per plan.md GATE requirements
- [ ] T067 Review against Success Criteria SC-001 through SC-010 from spec.md
- [ ] T068 Final proofreading pass: grammar, formatting, code block syntax highlighting
- [ ] T069 Create PR description summarizing the three architecture documents and their purpose
- [ ] T070 Tag completion: Update spec.md status to "Complete" and add completion date

---

## Dependencies & Execution Order

### Phase Dependencies

- **Setup (Phase 1)**: No dependencies - can start immediately
- **Foundational (Phase 2)**: Depends on Setup completion - BLOCKS all user stories
- **User Stories (Phase 3-5)**: All depend on Foundational phase completion
  - User Stories 1, 2, 3 can then proceed in parallel (different target files)
  - Or sequentially in order (US1 → US2 → US3) for logical flow
- **Polish (Phase 6)**: Depends on all three user stories being complete

### User Story Dependencies

- **User Story 1 (ML_ARCHITECTURE.md - P1)**: Can start after Foundational (Phase 2) - No dependencies on other stories
- **User Story 2 (FEATURE_ENGINEERING.md - P1)**: Can start after Foundational (Phase 2) - References architecture concepts but can be written independently
- **User Story 3 (MODEL_TRAINING_PIPELINE.md - P1)**: Can start after Foundational (Phase 2) - References feature vectors but can be written independently
- **User Story 4 (Provider Integration - P2)**: Content integrated into User Story 1 (T017-T019) - No separate phase needed

### Within Each User Story

- Document creation before content sections
- Overview/ToC before detailed sections
- Core architecture concepts before advanced topics
- Main workflow before edge cases
- References added last

### Parallel Opportunities

- All Setup tasks (T001-T002) can run sequentially (only 2 tasks)
- All Foundational tasks marked [P] can run in parallel: T004, T005
- Once Foundational phase completes, all three user stories (Phase 3, 4, 5) can start in parallel:
  - US1: ML_ARCHITECTURE.md (docs/ML_ARCHITECTURE.md)
  - US2: FEATURE_ENGINEERING.md (docs/FEATURE_ENGINEERING.md)
  - US3: MODEL_TRAINING_PIPELINE.md (docs/MODEL_TRAINING_PIPELINE.md)
- All Polish tasks marked [P] can run in parallel: T060, T061, T062, T063, T064
- Different documentation reviewers can validate different documents in parallel

---

## Parallel Example: After Foundational Phase

```bash
# Launch all three user story documentation tasks together:
Task: "Create docs/ML_ARCHITECTURE.md" (US1 - T009)
Task: "Create docs/FEATURE_ENGINEERING.md" (US2 - T026)
Task: "Create docs/MODEL_TRAINING_PIPELINE.md" (US3 - T041)

# Within US1, parallel opportunities:
# (Most US1 tasks are sequential due to logical document flow)

# Within US2, parallel opportunities:
# (Most US2 tasks are sequential due to logical document flow)

# Within US3, parallel opportunities:
# (Most US3 tasks are sequential due to logical document flow)

# Polish phase - all parallel:
Task: "Cross-reference validation" (T060)
Task: "Cross-document consistency check" (T061)
Task: "Constitution compliance review" (T062)
Task: "Performance targets verification" (T063)
Task: "Provider compatibility verification" (T064)
```

---

## Implementation Strategy

### MVP First (All P1 User Stories)

1. Complete Phase 1: Setup (T001-T002)
2. Complete Phase 2: Foundational (T003-T008) - CRITICAL - blocks all stories
3. Complete Phase 3: User Story 1 - ML_ARCHITECTURE.md (T009-T025)
4. Complete Phase 4: User Story 2 - FEATURE_ENGINEERING.md (T026-T040)
5. Complete Phase 5: User Story 3 - MODEL_TRAINING_PIPELINE.md (T041-T059)
6. **STOP and VALIDATE**: Review all three documents against spec.md requirements
7. Complete Phase 6: Polish (T060-T070)

**Note**: User Story 4 (Provider Integration) content is integrated into User Story 1, so all P1 and P2 stories are complete after Phase 5.

### Incremental Delivery (Document by Document)

1. Complete Setup + Foundational → Foundation ready
2. Add User Story 1 (ML_ARCHITECTURE.md) → Review independently → Commit
3. Add User Story 2 (FEATURE_ENGINEERING.md) → Review independently → Commit
4. Add User Story 3 (MODEL_TRAINING_PIPELINE.md) → Review independently → Commit
5. Polish all three together → Final review → PR

### Parallel Team Strategy

With multiple technical writers or developers:

1. Team completes Setup + Foundational together (T001-T008)
2. Once Foundational is done:
   - Writer A: User Story 1 (ML_ARCHITECTURE.md) - T009-T025
   - Writer B: User Story 2 (FEATURE_ENGINEERING.md) - T026-T040
   - Writer C: User Story 3 (MODEL_TRAINING_PIPELINE.md) - T041-T059
3. Team reviews together for consistency (T060-T064)
4. Single person finalizes (T065-T070)

---

## Success Metrics

**From spec.md Success Criteria**:

- ✅ SC-001: All three documents created (ML_ARCHITECTURE.md, FEATURE_ENGINEERING.md, MODEL_TRAINING_PIPELINE.md)
- ✅ SC-002: Provider compatibility matrices with Gmail/IMAP/Outlook canonical mappings
- ✅ SC-003: 40+ features enumerated with provider-agnostic extraction logic
- ✅ SC-004: All provider interfaces documented (IClassificationProvider, IFeatureExtractor, IModelTrainer)
- ✅ SC-005: State diagrams for model lifecycle and archive triage workflow
- ✅ SC-006: Constitution compliance verified
- ✅ SC-007: Performance targets documented
- ✅ SC-008: Storage design with 50GB cap and pruning strategy
- ✅ SC-009: Archive reclamation workflow fully documented
- ✅ SC-010: Canonical folder abstraction with 3+ provider mappings

**Task Completion Metrics**:

- Total tasks: 70
- Setup tasks: 2
- Foundational tasks: 6 (BLOCKS all user stories)
- User Story 1 tasks: 17 (ML_ARCHITECTURE.md)
- User Story 2 tasks: 15 (FEATURE_ENGINEERING.md)
- User Story 3 tasks: 19 (MODEL_TRAINING_PIPELINE.md)
- Polish tasks: 11
- Parallel opportunities: 9 marked [P] tasks + 3 entire user stories after Foundational

**Suggested MVP Scope**: All three P1 user stories (Phases 1-5) - this delivers complete architecture documentation ready for implementation issues #55+.

---

## Notes

- This is a **documentation-only deliverable** - no source code changes
- Tests are NOT included as they were not requested in the spec.md
- [P] tasks = different files or independent review areas
- [Story] label tracks which document each task contributes to (US1=ML_ARCHITECTURE, US2=FEATURE_ENGINEERING, US3=MODEL_TRAINING_PIPELINE)
- Each user story produces one complete document that can be reviewed independently
- Commit after each completed document (after US1, US2, US3)
- Verify against spec.md requirements after each document
- Final validation against all Success Criteria (SC-001 through SC-010) in Polish phase
- User Story 4 (Provider Integration) content integrated into User Story 1 - no separate phase
