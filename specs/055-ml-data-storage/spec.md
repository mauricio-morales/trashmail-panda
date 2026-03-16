# Feature Specification: ML Data Storage System

**Feature Branch**: `055-ml-data-storage`  
**Created**: March 14, 2026  
**Status**: Draft  
**Input**: User description: "Design and implement local storage system for feature vectors, full email storage, metadata, and storage limits with configurable retention policy"

## User Scenarios & Testing *(mandatory)*

### User Story 1 - Email Feature Vector Storage (Priority: P1)

The system automatically extracts and stores feature vectors from each processed email to enable ML model training and classification. These lightweight feature vectors (sender patterns, subject characteristics, timestamps, behavioral signals) persist independently and are used for model improvement without requiring full email retention.

**Why this priority**: Without feature storage, the ML system cannot learn from user interactions or improve classification accuracy over time. This is the foundational requirement for any ML-based email classification.

**Independent Test**: Can be fully tested by processing test emails and verifying feature vectors are persisted to storage, retrievable for model training, and remain available even after the full email is deleted. Delivers value by enabling model training and continuous improvement.

**Acceptance Scenarios**:

1. **Given** an email is processed by the classification system, **When** features are extracted, **Then** the feature vector is stored with email metadata and timestamp
2. **Given** feature vectors are stored in the database, **When** the ML model requests training data, **Then** all stored feature vectors are retrievable with their associated classifications
3. **Given** a full email is deleted from storage, **When** querying for that email's features, **Then** the feature vector remains available for future model training

---

### User Story 2 - Complete Email Archive (Priority: P2)

The system stores complete email data when storage capacity allows, enabling future feature regeneration, model retraining with new signals, and audit trails for classification decisions. Full email storage is optional and subject to storage limits.

**Why this priority**: While feature vectors are sufficient for basic ML operations, storing complete emails enables advanced capabilities like reprocessing with improved feature extraction algorithms and investigating classification edge cases.

**Independent Test**: Can be tested independently by storing emails, verifying stored data integrity, and successfully retrieving complete email data for reprocessing. Delivers value by enabling model evolution without requiring re-fetch from source systems.

**Acceptance Scenarios**:

1. **Given** an email is processed and storage capacity is available, **When** the email is archived, **Then** the complete email data is stored with associated metadata
2. **Given** a stored email archive entry, **When** the system needs to regenerate features with improved extraction logic, **Then** the complete email data can be retrieved and reprocessed
3. **Given** storage limits are approaching capacity, **When** new emails need archiving, **Then** oldest full email archives are removed while preserving their feature data

---

### User Story 3 - Storage Limit Management (Priority: P3)

The system monitors storage usage and automatically manages capacity to prevent disk exhaustion. A configurable storage limit (default 50GB) triggers automatic cleanup of oldest data while preserving critical information based on retention policies.

**Why this priority**: Prevents system failures due to disk exhaustion and ensures predictable storage costs, but can be implemented after basic storage functionality is working.

**Independent Test**: Can be tested by filling storage to the configured limit and verifying automatic cleanup behavior. Delivers value by preventing disk space emergencies and maintaining system stability.

**Acceptance Scenarios**:

1. **Given** storage usage is monitored continuously, **When** usage exceeds 90% of configured limit, **Then** system triggers automatic cleanup of oldest archived emails
2. **Given** cleanup is triggered, **When** removing old data, **Then** feature data is preserved while full email archives are removed first
3. **Given** storage limit is configurable, **When** administrator changes the limit, **Then** system adjusts cleanup thresholds accordingly

---

### User Story 4 - User Correction Preservation (Priority: P4)

The system prioritizes preservation of emails where users provided classification corrections, as these represent the highest-value training data. User-corrected emails are retained longer and protected from automatic cleanup when possible.

**Why this priority**: User corrections are the most valuable training signal, but this priority-based retention can be layered on top of basic storage management.

**Independent Test**: Can be tested by marking emails as user-corrected and verifying they survive cleanup cycles that remove other emails. Delivers value by ensuring the most important training data is always available.

**Acceptance Scenarios**:

1. **Given** a user corrects a classification decision, **When** metadata is stored, **Then** the email and features are marked as user-corrected with higher retention priority
2. **Given** storage cleanup is triggered, **When** selecting emails for deletion, **Then** user-corrected emails are excluded from cleanup until all non-corrected emails are removed
3. **Given** user-corrected email data is retrieved, **When** training the model, **Then** these corrections are weighted more heavily than automated classifications

---

### Edge Cases

- What happens when storage limit is reached but all remaining emails are user-corrected? (System should warn administrator and temporarily exceed limit rather than delete valuable training data)
- How does system handle corrupted stored email data during retrieval? (Should log error, mark entry as corrupted, attempt graceful fallback to feature data only)
- What if feature extraction fails but email archiving succeeds? (Should retry feature extraction on next processing cycle, preserve archived email)
- How are features and archives handled when emails are deleted from Gmail source? (Features remain for training, full archives can be removed if storage is needed)
- What happens during database migration if schema changes affect stored features? (Migration should preserve feature data or transform to new schema without loss)
- How does system behave if configured storage limit is smaller than current usage? (Should immediately trigger aggressive cleanup while displaying warning)

## Requirements *(mandatory)*

### Functional Requirements

- **FR-001**: System MUST extract and store feature vectors from each processed email, including sender patterns, subject characteristics, timestamps, and behavioral signals
- **FR-002**: System MUST store complete email data when storage capacity allows, enabling future reprocessing and feature regeneration
- **FR-003**: System MUST store metadata for each email including classification decisions, user corrections, processing timestamps, and retention priorities
- **FR-004**: System MUST enforce a configurable storage limit with default value of 50GB
- **FR-005**: System MUST automatically monitor storage usage and trigger cleanup when usage exceeds 90% of configured limit
- **FR-006**: System MUST implement automatic cleanup that removes oldest full email archives first while preserving feature data
- **FR-007**: System MUST mark user-corrected emails with higher retention priority and exclude them from cleanup until all non-corrected emails are removed
- **FR-008**: System MUST preserve feature vectors even after full email data is deleted during cleanup or retention policy enforcement
- **FR-009**: System MUST provide storage usage metrics including total size, feature vector size, full email archive size, and percentage of limit used
- **FR-010**: System MUST support schema versioning for email archive data to enable backward compatibility during system updates
- **FR-011**: System MUST validate stored email data integrity during storage and retrieval, handling corrupted data gracefully with error logging
- **FR-012**: System MUST support configuration override for storage limits per deployment environment (development, staging, production)

### Key Entities

- **EmailFeatureVector**: Lightweight structured data representing extracted signals from an email (sender domain, subject patterns, timestamp, user engagement signals, classification confidence). Persists independently of full email storage.
- **EmailArchive**: Complete email data with associated metadata (original message ID, archive timestamp, retrieval count, integrity status). Optional storage based on capacity.
- **ClassificationMetadata**: Decision history for each email including initial classification, user corrections, confidence scores, model version used, and correction timestamp. Links to both feature vectors and archives.
- **StorageQuota**: Configuration and monitoring entity tracking current usage, configured limits, cleanup thresholds, last cleanup timestamp, and priority statistics (user-corrected vs. auto-classified counts).
- **FeatureSchema**: Version tracking for feature vector structure to support schema evolution and backward compatibility during model updates.

## Success Criteria *(mandatory)*

### Measurable Outcomes

- **SC-001**: System successfully stores feature vectors for 100,000 processed emails without exceeding configured storage limit
- **SC-002**: Feature extraction and storage completes within 100ms per email on average, measured across 1,000 email batch processing
- **SC-003**: Storage cleanup automatically triggers when usage reaches 90% of limit and reduces usage to below 80% within 5 minutes
- **SC-004**: User-corrected emails have 95% retention rate after cleanup cycles compared to 60% retention for auto-classified emails
- **SC-005**: Feature data remains retrievable for 100% of emails even after full email archive deleted during cleanup
- **SC-006**: Storage monitoring reports accurate usage metrics with less than 2% variance from actual disk usage
- **SC-007**: System upgrades complete successfully with up to 1 million existing email records without data loss or service interruption
- **SC-008**: Retrieved feature data maintains 100% integrity (no data corruption) across storage/retrieval cycles

## Dependencies & Assumptions

### Dependencies

- **GitHub Issue #54** (ML Architecture Design): This feature depends on the ML architecture specification that defines the feature schema and extraction process. The storage system must align with the feature structure defined in #54.
- **Existing Storage Infrastructure**: Assumes current email metadata storage system is operational and can be extended to support ML-specific data storage.
- **Email Processing Pipeline**: Storage occurs after email processing and feature extraction, requiring integration with the email classification workflow.

### Assumptions

- **Disk Space Availability**: Default 50GB storage limit assumes typical deployment environments have at least 100GB available disk space for the application.
- **Feature Vector Size**: Assumes average feature vector size of approximately 5-10KB per email, allowing storage of ~5-10 million email features within the 50GB limit.
- **Full Email Size**: Assumes average full email size of 50-100KB, significantly larger than feature vectors, making selective archiving necessary.
- **Cleanup Frequency**: Assumes storage monitoring occurs at regular intervals (hourly or daily) rather than real-time, allowing batch cleanup operations.
- **User Correction Rate**: Assumes <10% of emails receive user corrections, making priority-based retention feasible without excessive storage pressure.
- **Email Volume**: Designed for typical users processing 100-1,000 emails per day, with storage capacity supporting months to years of history.
- **Schema Stability**: Assumes feature schema changes are infrequent (quarterly or less), making schema versioning overhead acceptable.
