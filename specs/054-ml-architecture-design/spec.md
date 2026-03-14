# Feature Specification: ML Architecture Design

**Feature Branch**: `054-ml-architecture-design`  
**Created**: 2026-03-14  
**Status**: Draft  
**Input**: GitHub Issue #54 - Define ML-based architecture and feature extraction strategy

## User Scenarios & Testing

### User Story 1 - Architecture Review and Validation (Priority: P1)

As a developer implementing the ML.NET transition, I need comprehensive architecture documentation so I can understand the system design, component interactions, and maintain consistency with project patterns.

**Why this priority**: Without clear architecture, implementation will be fragmented and may violate core principles like provider-agnosticity or the Result pattern. This is the foundation for all subsequent implementation work.

**Independent Test**: Architecture documents can be reviewed independently without any implementation. Success is measured by completeness of specifications and alignment with project constitution.

**Acceptance Scenarios**:

1. **Given** the architecture document exists, **When** a developer reads the ML_ARCHITECTURE.md file, **Then** they understand the separation between UI, classification engine, data layer, and how components integrate with existing provider framework
2. **Given** the architecture document, **When** reviewing component interfaces, **Then** all interfaces follow IProvider<TConfig> pattern and return Result<T> types (no exceptions)
3. **Given** the architecture document, **When** checking storage design, **Then** the document clearly specifies SQLCipher encryption for feature vectors and email archive with configurable storage caps

---

### User Story 2 - Feature Engineering Planning (Priority: P1)

As an ML implementer, I need detailed feature extraction specifications so I can transform raw emails into structured feature vectors that enable accurate local classification across all email providers (Gmail, IMAP, Outlook).

**Why this priority**: Feature engineering is the foundation of model quality. Without clear specifications, feature extraction will be inconsistent and provider-specific, breaking multi-provider support.

**Independent Test**: Feature specifications can be validated by mapping sample emails (from different providers) to feature vectors on paper, verifying all required signals are extractable.

**Acceptance Scenarios**:

1. **Given** FEATURE_ENGINEERING.md exists, **When** examining the feature extraction pipeline, **Then** specifications include both structured features (sender domain, authentication, time patterns) and text features (subject/body TF-IDF)
2. **Given** FEATURE_ENGINEERING.md, **When** checking provider compatibility, **Then** all features are extractable from canonical email metadata (not Gmail-specific) and document shows mapping from Gmail labels, IMAP folders, and Outlook folders to canonical semantics
3. **Given** an email from any provider (Gmail, IMAP, Outlook), **When** extracting features, **Then** the specification defines how to map provider-specific concepts (labels/folders/flags) to canonical features (IsInInbox, IsStarred, WasInTrash, etc.)
4. **Given** archived emails from different providers, **When** feature extraction runs, **Then** archive-specific features (EmailAgeDays, SenderFrequency, SourceFolder) are computed identically regardless of provider type

---

### User Story 3 - Training Workflow Definition (Priority: P1)

As a training pipeline developer, I need complete model training workflow documentation so I can implement cold start handling, incremental retraining, and model versioning correctly.

**Why this priority**: Training workflow is critical for user experience—users start with zero labeled data and the system must bootstrap intelligently using folder placement signals (Trash → delete labels, Starred → keep labels) across all email providers.

**Independent Test**: Training workflow can be validated by walking through scenarios (new user with archived emails, user with corrections, scheduled retrain) and verifying all states and transitions are documented.

**Acceptance Scenarios**:

1. **Given** MODEL_TRAINING_PIPELINE.md exists, **When** reviewing cold start procedures, **Then** the document specifies how to bootstrap training data from existing mailbox folder placement (Trash/Spam → "delete", Starred/Flagged/Inbox → "keep", Archive → triage target) using provider-agnostic canonical folders
2. **Given** the training workflow document, **When** checking retraining triggers, **Then** specifications define automatic triggers (50+ corrections, 7-day schedule) and manual user-initiated retraining
3. **Given** the training workflow document, **When** examining model lifecycle, **Then** the document specifies model versioning (file naming, metadata storage), rollback procedures, and retention policy (keep 5 most recent versions)
4. **Given** the training workflow document, **When** reviewing archive reclamation workflow, **Then** the primary use case (scan all folders → bootstrap from folder signals → train model → classify archive → recommend deletions with confidence scores) is clearly defined
5. **Given** emails from Gmail (labels), IMAP (folders), or Outlook (folders+categories), **When** bootstrapping training data, **Then** the workflow document shows how each provider's folder/label structure maps to training labels using canonical folder semantics

---

### User Story 4 - Provider Integration Guidance (Priority: P2)

As a provider developer, I need clear integration points documented so I can extend email providers (IEmailProvider) to support ML feature extraction without breaking existing functionality.

**Why this priority**: ML feature extraction requires additional email metadata (headers, body content, folder placement) beyond current EmailSummary. Provider contracts need extension specifications.

**Independent Test**: Review existing IEmailProvider implementations (Gmail, future IMAP) and verify proposed extensions are feasible without breaking changes.

**Acceptance Scenarios**:

1. **Given** ML_ARCHITECTURE.md exists, **When** reviewing provider integration section, **Then** the document specifies how IEmailProvider implementations should expose full email data (headers, body, folder/label IDs) for feature extraction
2. **Given** the architecture document, **When** checking canonical email abstraction, **Then** specifications define mapping tables showing how Gmail labels, IMAP folders, and Outlook folders/categories map to canonical folder semantics (Inbox, Archive, Trash, Spam, Sent, Flagged, Important)
3. **Given** different email providers (Gmail, IMAP, Outlook), **When** implementing IEmailProvider.GetFullEmailsAsync, **Then** the specification shows how each provider normalizes its native folder/label/flag concepts into CanonicalEmailMetadata
4. **Given** the canonical folder table, **When** reviewing storage design, **Then** the SourceFolder field in email_archive uses canonical values ("inbox", "archive", "trash", "spam", "sent") not provider-specific identifiers

### Edge Cases

- **What happens when a provider doesn't support threading?** ConversationId is nullable; thread-based features (ThreadMessageCount) gracefully degrade to single-message values (count=1).
- **How does the system handle emails with missing headers or body content?** Feature extraction uses sensible defaults (sender_domain="unknown", auth results="none", text features=null) and logs warnings; feature vectors are still generated for numeric features.
- **What if a user has less than 100 labeled emails?** System operates in Cold Start mode using rule-based classification only (UserRules whitelist/blacklist); ML training is deferred until 100+ labels available.
- **How are old model versions cleaned up?** Automatic pruning keeps the 5 most recent model versions; older .zip files are deleted from data/models/ directory; metadata rows remain in ml_models table for audit trail.
- **What happens if feature extraction logic changes after training?** Feature schema version is incremented; existing models are invalidated; features are re-extracted from email_archive data; model retraining is required.
- **How does the system handle emails that are in multiple folders/labels (Gmail)?** Gmail's multi-label system is supported via canonical folder mapping: PrimaryFolder (where email lives) + Tags[] (additional labels); other providers use single-folder with optional categories.
- **What if a provider's folder structure doesn't match canonical folders?** Provider adapters must map their native structure to canonical semantics—this is documented in the provider integration section with explicit mapping tables for Gmail, IMAP, and Outlook.
- **How are emails in user-created folders/labels treated?** User-created folders/labels are preserved in FolderTagsJson but don't affect canonical folder classification (Inbox/Archive/Trash/Spam/Sent); they provide additional keep/delete signals based on user organization patterns.

## Requirements

### Functional Requirements

- **FR-001**: ML_ARCHITECTURE.md MUST define separation between UI layer, classification engine (IClassificationProvider), feature extraction (IFeatureExtractor), model training (IModelTrainer), and data layer (email_features, email_archive tables)
- **FR-002**: ML_ARCHITECTURE.md MUST specify how IClassificationProvider fits into existing IProvider<TConfig> framework with Result<T> return types and dependency injection
- **FR-003**: ML_ARCHITECTURE.md MUST document archive reclamation as the primary use case: scan mailbox → bootstrap from folder signals → train model → classify archive → recommend bulk deletions with confidence scores and storage estimates
- **FR-004**: ML_ARCHITECTURE.md MUST specify provider-agnostic canonical folder abstraction with explicit mapping tables for Gmail (labels), IMAP (folders), and Outlook (folders+categories) to canonical semantics (Inbox, Archive, Trash, Spam, Sent, Flagged, Important)
- **FR-005**: FEATURE_ENGINEERING.md MUST enumerate all features in Tier 1 (structured: sender domain, authentication results, time patterns, folder placement, flags) and Tier 2 (text: subject/body TF-IDF, link counts, tracking pixel detection)
- **FR-006**: FEATURE_ENGINEERING.md MUST specify archive-specific features that work identically across all email providers: EmailAgeDays, SenderFrequency, IsArchived, SourceFolder (using canonical values), WasInTrash, WasInSpam, IsInInbox, IsStarred, IsImportant
- **FR-007**: FEATURE_ENGINEERING.md MUST define EmailFeatureVector schema with 40+ features including schema versioning for compatibility checks
- **FR-008**: FEATURE_ENGINEERING.md MUST document feature extraction from canonical email metadata (CanonicalEmailMetadata) not provider-specific types, enabling multi-provider support
- **FR-009**: MODEL_TRAINING_PIPELINE.md MUST define three training phases: Cold Start (rule-based, <100 labels), Hybrid (100-500 labels, ML + rules), ML Primary (500+ labels)
- **FR-010**: MODEL_TRAINING_PIPELINE.md MUST specify bootstrapping strategy using existing folder placement as training labels across all providers: Trash/Spam folders → "delete" labels, Starred/Flagged/Important → "keep" labels, Inbox → "keep" labels, Archive → triage target
- **FR-011**: MODEL_TRAINING_PIPELINE.md MUST document retraining triggers (50+ corrections, 7-day schedule, user request) and incremental update strategy
- **FR-012**: MODEL_TRAINING_PIPELINE.md MUST specify model versioning scheme (file naming: model_v{version}_{timestamp}.zip, metadata in ml_models table, rollback procedure, retention: 5 versions)
- **FR-013**: All three documents MUST include performance targets: single email classification <10ms, batch 100 emails <100ms, model training 10K emails <2min, 100K emails <5min
- **FR-014**: ML_ARCHITECTURE.md MUST specify SQLCipher encryption for email_features and email_archive tables with configurable storage cap (default 50GB)
- **FR-015**: ML_ARCHITECTURE.md MUST document IClassificationProvider.TriageArchiveAsync as the primary archive reclamation workflow returning TriageBulkGroup recommendations with deletion confidence, reasons, and storage reclaim estimates
- **FR-016**: FEATURE_ENGINEERING.md MUST specify feature schema compatibility checks: FeatureSchemaVersion field in feature vectors must match model's expected version
- **FR-017**: MODEL_TRAINING_PIPELINE.md MUST define archive triage workflow: scan all mailbox folders (Inbox, Archive, Trash, Spam, Sent, user folders) → extract canonical folder signals → train model → classify archived emails → group by sender/topic → present bulk deletion recommendations

### Key Entities

- **IClassificationProvider**: Primary provider interface for email classification; replaces ILLMProvider for classification; supports ClassifyAsync (batch classification) and TriageArchiveAsync (archive reclamation workflow); follows IProvider<TConfig> pattern with Result<T> returns
- **IFeatureExtractor**: Internal service for transforming raw emails into EmailFeatureVector records; extracts structured features (sender, auth, time, folder placement, flags) and text features (TF-IDF); operates on CanonicalEmailMetadata not provider-specific types
- **IModelTrainer**: Service for ML model lifecycle; handles training (bootstraps from folder signals, supports incremental updates), evaluation, rollback, and model pruning
- **EmailFeatureVector**: Structured data record with 40+ features for ML training/inference; includes archive-specific features (EmailAgeDays, SenderFrequency, SourceFolder, IsArchived, WasInTrash, WasInSpam, IsInInbox, IsStarred, IsImportant); includes FeatureSchemaVersion for compatibility
- **EmailArchiveEntry**: Full email storage (headers, body, folder tags, provider type, source folder) enabling feature re-extraction if logic changes; uses canonical SourceFolder values ("inbox", "archive", "trash", "spam", "sent")
- **CanonicalEmailMetadata**: Provider-agnostic email representation with canonical folder semantics; all providers (Gmail, IMAP, Outlook) map to canonical concepts (Inbox, Archive, Trash, Spam, Sent, Flagged, Important, UserFolder)
- **MlModel**: Metadata for trained models (version, type, trainer, metrics, file path, active flag); supports multi-model strategy (action classification and label classification)
- **ArchiveTriageOutput**: Grouped recommendations for archive reclamation; includes TriageBulkGroup records with deletion confidence, storage reclaim estimates, and human-readable reasons
- **TriageBulkGroup**: Batch of similar emails recommended for same action (keep/delete/review); includes GroupKey, Label, Reason, AverageConfidence, EmailCount, StorageReclaimBytes; enables bulk user decisions

## Success Criteria

### Measurable Outcomes

- **SC-001**: All three architecture documents (ML_ARCHITECTURE.md, FEATURE_ENGINEERING.md, MODEL_TRAINING_PIPELINE.md) are created and pass review for completeness against specified requirements
- **SC-002**: Architecture documents include explicit provider compatibility matrices showing how Gmail labels, IMAP folders, and Outlook folders/categories map to canonical folder semantics
- **SC-003**: Feature engineering specification enumerates 40+ features with clear extraction logic that works identically across Gmail, IMAP, and Outlook providers
- **SC-004**: All provider interfaces (IClassificationProvider, IFeatureExtractor, IModelTrainer) are documented with method signatures, input/output types, and behavior contracts
- **SC-005**: Training workflow documentation includes state diagrams for model lifecycle (Cold Start → Hybrid → ML Primary) and archive triage workflow (scan → bootstrap → train → classify → recommend)
- **SC-006**: Architecture documents pass constitution compliance check: provider-agnostic design, Result<T> pattern, security-first (SQLCipher), one public type per file
- **SC-007**: Performance targets are documented for all operations: classification <10ms per email, batch processing <100ms per 100 emails, training <5min for 100K emails
- **SC-008**: Storage design specifies configurable cap (default 50GB), automatic pruning strategy, and separation between compact feature vectors (~200 bytes/email) and full email archive (~50KB/email)
- **SC-009**: Archive reclamation workflow is fully documented as primary use case: folder signal bootstrapping → model training → archive classification → bulk deletion recommendations with confidence scores and storage estimates
- **SC-010**: Canonical folder abstraction is documented with complete mapping tables for at least 3 providers (Gmail, IMAP, Outlook) showing how native concepts map to canonical semantics (Inbox, Archive, Trash, Spam, Sent, Flagged, Important)

## Dependencies & Assumptions

### Dependencies

- Existing provider infrastructure (`IProvider<TConfig>`, `BaseProvider<TConfig>`) must be stable
- SQLite/SQLCipher storage system must be functional
- Result<T> pattern and error types (`ConfigurationError`, `NetworkError`, etc.) must be defined in Shared.Base
- Microsoft.Extensions.DependencyInjection, Logging, and Options infrastructure must be available

### Assumptions

- ML.NET is the selected framework for local classification (decision made in research phase)
- .NET 9.0 and C# 12 with nullable reference types remain the project standard
- Cross-platform support (Windows/macOS/Linux) is maintained
- SQLCipher encryption for all email data is non-negotiable (privacy-first principle)
- Users will have varying corpus sizes (10K-100K emails) requiring configurable storage
- Cold start problem is solved via rule-based classification until sufficient labels exist (100+)
- Archive reclamation (triaging thousands of existing archived emails) is the primary use case; incoming email classification is secondary
- All email providers (Gmail, IMAP, Outlook, Yahoo, iCloud, ProtonMail) can map to canonical folder semantics without loss of critical signals
- Future console TUI interface will replace Avalonia UI but won't affect backend architecture design
- Two model types are planned: action classification (keep/archive/delete/spam) and label/folder prediction (multi-label)

## Out of Scope

- Implementation of IClassificationProvider, IFeatureExtractor, or IModelTrainer (this is documentation-only)
- Actual ML.NET model training code
- Console TUI interface design or implementation
- Migration from Avalonia UI to console UI
- Advanced topic modeling (LDA, semantic embeddings)—deferred to Phase 2+ with explicit "Phase 2" markers in documentation
- GPU acceleration or model compression
- Federated learning or model sharing between users
- Email content analysis beyond text (image analysis, attachment scanning)
- Integration with external ML services or ONNX models (future consideration)
- JMAP protocol support (minimal adoption among free providers)
- Real-time incremental learning (periodic batch retraining is used instead)

## Notes

- This feature produces **documentation artifacts only**—no source code changes
- Subsequent implementation issues (#55+) will reference these architecture documents
- Feature engineering specifications include schema versioning to handle future changes without breaking compatibility
- Archive reclamation workflow is designed to work identically across all email providers by using canonical folder abstraction
- Provider-agnostic design enables future support for additional email sources (Yahoo Mail, iCloud, ProtonMail, generic IMAP) without architecture changes
- Model versioning supports rollback for safety if new model accuracy regresses
- Storage design balances feature retention (compact vectors kept long-term) with full email archival (larger storage, configurable cap with pruning)
