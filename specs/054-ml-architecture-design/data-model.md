# Data Model: ML Architecture Design

**Feature**: #54 — ML Architecture Design  
**Date**: 2026-03-14  
**Status**: Complete

## Entity Overview

This data model defines the storage schema and domain types for the ML-based email classification system. It extends the existing SQLCipher-encrypted SQLite database with new tables for feature storage, email archival, and model management.

## Entities

### 1. EmailFeatureVector

Stores the extracted feature vector for each processed email. Used as both training input and inference input.

| Field | Type | Nullable | Description |
|-------|------|----------|-------------|
| EmailId | string (PK) | No | Provider-assigned message ID (opaque, unique per provider) |
| SenderDomain | string | No | Extracted domain from sender address |
| SenderKnown | bool | No | Whether sender is in contacts |
| ContactStrength | int | No | 0=None, 1=Weak, 2=Strong |
| SpfResult | string | No | "pass", "fail", "neutral", "none" |
| DkimResult | string | No | "pass", "fail", "neutral", "none" |
| DmarcResult | string | No | "pass", "fail", "neutral", "none" |
| HasListUnsubscribe | bool | No | List-Unsubscribe header present |
| HasAttachments | bool | No | Email contains attachments |
| HourReceived | int | No | Hour of day (0-23) |
| DayOfWeek | int | No | Day of week (0=Sun, 6=Sat) |
| EmailSizeLog | float | No | log10(SizeEstimate) |
| SubjectLength | int | No | Character count of subject |
| RecipientCount | int | No | To + Cc recipient count |
| IsReply | bool | No | Subject starts with "Re:" |
| InUserWhitelist | bool | No | Matches AlwaysKeep rules |
| InUserBlacklist | bool | No | Matches AutoTrash rules |
| LabelCount | int | No | Number of folders/tags assigned to this email |
| LinkCount | int | No | Count of links in body HTML |
| ImageCount | int | No | Count of images in body HTML |
| HasTrackingPixel | bool | No | 1x1 pixel image detected |
| UnsubscribeLinkInBody | bool | No | Unsubscribe link pattern found |
| EmailAgeDays | int | No | Days since email was received (relative to extraction time) |
| IsInInbox | bool | No | In Inbox folder — strong keep signal |
| IsStarred | bool | No | Flagged/starred — strong keep signal |
| IsImportant | bool | No | Marked important/high-priority — keep signal |
| WasInTrash | bool | No | Source folder is Trash/Deleted Items — strong delete signal |
| WasInSpam | bool | No | Source folder is Spam/Junk — strong delete signal |
| IsArchived | bool | No | Not in Inbox/Trash/Spam — primary triage target |
| ThreadMessageCount | int | No | Number of messages in the email thread |
| SenderFrequency | int | No | Total emails from this sender domain in corpus |
| SubjectText | string | Yes | Raw subject for TF-IDF (nullable for headerless emails) |
| BodyTextShort | string | Yes | First 500 chars of body text for TF-IDF |
| TopicClusterId | int | Yes | LDA topic cluster ID (null until Phase 2 — see R10) |
| TopicDistributionJson | string | Yes | JSON topic probability distribution (null until Phase 2) |
| SenderCategory | string | Yes | Sender domain category, e.g. "Development", "E-commerce" (null until Phase 2) |
| SemanticEmbeddingJson | string | Yes | Dense embedding vector from local ONNX model (null until Phase 3) |
| FeatureSchemaVersion | int | No | Schema version for compatibility checks |
| ExtractedAt | DateTime | No | When features were extracted |

**Relationships**: 
- 1:1 with `email_metadata` via `EmailId`
- 1:1 with `EmailArchiveEntry` via `EmailId` (optional)

**Validation**: 
- `FeatureSchemaVersion` must match the active model's expected schema version
- `EmailSizeLog` must be >= 0

### 2. EmailArchiveEntry

Stores complete email data for feature regeneration when extraction logic changes.

| Field | Type | Nullable | Description |
|-------|------|----------|-------------|
| EmailId | string (PK) | No | Provider-assigned message ID (opaque, unique per provider) |
| ThreadId | string | Yes | Conversation/thread ID (null if provider lacks threading) |
| ProviderType | string | No | Email provider type: "gmail", "imap", "outlook", etc. |
| HeadersJson | string | No | JSON-serialized headers dictionary |
| BodyText | string | Yes | Plain text email body |
| BodyHtml | string | Yes | Sanitized HTML email body |
| FolderTagsJson | string | No | JSON array of canonical folder/tag names |
| SizeEstimate | long | No | Email size in bytes |
| ReceivedDate | DateTime | No | Original received date |
| ArchivedAt | DateTime | No | When archived locally |
| Snippet | string | Yes | Email preview/snippet text |
| SourceFolder | string | No | Canonical source folder: "inbox", "archive", "trash", "spam", "sent" |

**Relationships**:
- 1:1 with `EmailFeatureVector` via `EmailId`
- 1:0..1 with `ArchiveTriageResult` via `EmailId`
- Referenced by training pipeline for feature regeneration

**Validation**:
- At least one of `BodyText` or `BodyHtml` should be non-null (warning, not error)
- `SourceFolder` must be one of the canonical values: "inbox", "archive", "trash", "spam", "sent", "drafts"
- `ProviderType` must be a recognized provider identifier

### 2b. ArchiveTriageResult

Stores the outcome of archive reclamation classification. One entry per archived email that has been triaged.

| Field | Type | Nullable | Description |
|-------|------|----------|-------------|
| EmailId | string (PK) | No | Provider-assigned message ID |
| DeletionConfidence | float | No | 0.0 (definitely keep) to 1.0 (definitely delete) |
| RecommendedAction | string | No | "keep", "delete", "review" |
| BulkGroupKey | string | Yes | Grouping key for batch decisions (e.g., sender domain or topic cluster) |
| Reason | string | No | Human-readable explanation for the recommendation |
| StorageReclaimBytes | long | No | Bytes that would be freed if deleted |
| TriagedAt | DateTime | No | When classification was performed |
| UserDecision | string | Yes | "accepted", "rejected", null (pending) |
| UserDecisionAt | DateTime | Yes | When user made decision |
| UsedInTraining | bool | No | Whether this decision has fed back into training |

**Relationships**:
- 1:1 with `EmailArchiveEntry` via `EmailId`
- 1:1 with `EmailFeatureVector` via `EmailId`
- Consumed by training pipeline when `UserDecision` is set

**Validation**:
- `DeletionConfidence` must be in range [0.0, 1.0]
- `RecommendedAction` must be one of "keep", "delete", "review"
- `UserDecision` must be one of "accepted", "rejected", or null

### 3. MlModel

Metadata for each trained model version. The actual model file is stored on the filesystem.

| Field | Type | Nullable | Description |
|-------|------|----------|-------------|
| Id | string (PK) | No | Model version identifier (e.g., "v1_20260314_143022") |
| ModelType | string | No | "action" (keep/archive/delete/spam) or "folder" (folder/tag prediction) |
| TrainerName | string | No | ML.NET trainer used (e.g., "SdcaMaximumEntropy") |
| FeatureSchemaVersion | int | No | Feature schema this model was trained on |
| TrainingSampleCount | int | No | Number of training samples |
| Accuracy | float | No | Overall accuracy on validation set |
| MacroF1 | float | No | Macro-averaged F1 score |
| MicroF1 | float | No | Micro-averaged F1 score |
| PerClassMetricsJson | string | No | JSON: per-class precision/recall/F1 |
| ModelFilePath | string | No | Relative path to .zip model file |
| IsActive | bool | No | Whether this is the currently active model |
| CreatedAt | DateTime | No | Training completion timestamp |
| TrainingDurationMs | long | No | Training time in milliseconds |
| Notes | string | Yes | Optional human-readable notes |

**Relationships**:
- Referenced by `app_config` ("active_action_model", "active_label_model")
- 1:N with `TrainingEvent` via model version

**Validation**:
- Only one model per `ModelType` can have `IsActive = true`
- `Accuracy`, `MacroF1`, `MicroF1` must be in range [0.0, 1.0]

### 4. TrainingEvent

Audit log of each training run for diagnostics and reproducibility.

| Field | Type | Nullable | Description |
|-------|------|----------|-------------|
| Id | int (PK, auto) | No | Event ID |
| ModelId | string (FK) | Yes | Resulting model version (null if training failed) |
| ModelType | string | No | "action" or "label" |
| TriggerReason | string | No | "scheduled", "correction_threshold", "user_request", "cold_start" |
| TrainingSampleCount | int | No | Samples used in this run |
| ValidationSampleCount | int | No | Samples used for validation |
| StartedAt | DateTime | No | Training start time |
| CompletedAt | DateTime | Yes | Training completion time (null if in progress/failed) |
| Status | string | No | "pending", "running", "completed", "failed" |
| ErrorMessage | string | Yes | Error details if failed |
| MetricsJson | string | Yes | Full metrics snapshot |

**Relationships**:
- N:1 with `MlModel` via `ModelId`

**Validation**:
- `CompletedAt` must be >= `StartedAt` when not null
- `Status` must be one of the allowed values

### 5. UserCorrection

Explicit user corrections to classification results — highest-signal training data.

| Field | Type | Nullable | Description |
|-------|------|----------|-------------|
| Id | int (PK, auto) | No | Correction ID |
| EmailId | string | No | Provider-assigned message ID |
| OriginalClassification | string | No | What the model predicted |
| CorrectedClassification | string | No | What the user said it should be |
| Confidence | float | No | Model confidence when prediction was made |
| CorrectedAt | DateTime | No | When user corrected |
| UsedInTraining | bool | No | Whether this correction has been consumed by a training run |

**Relationships**:
- References `email_features` via `EmailId`
- Consumed by training pipeline

**Validation**:
- `OriginalClassification` != `CorrectedClassification` (would be pointless otherwise)

## SQL Schema (SQLite/SQLCipher)

```sql
-- Email feature vectors for ML training and inference
CREATE TABLE IF NOT EXISTS email_features (
    email_id TEXT PRIMARY KEY,
    sender_domain TEXT NOT NULL,
    sender_known INTEGER NOT NULL DEFAULT 0,
    contact_strength INTEGER NOT NULL DEFAULT 0,
    spf_result TEXT NOT NULL DEFAULT 'none',
    dkim_result TEXT NOT NULL DEFAULT 'none',
    dmarc_result TEXT NOT NULL DEFAULT 'none',
    has_list_unsubscribe INTEGER NOT NULL DEFAULT 0,
    has_attachments INTEGER NOT NULL DEFAULT 0,
    hour_received INTEGER NOT NULL,
    day_of_week INTEGER NOT NULL,
    email_size_log REAL NOT NULL,
    subject_length INTEGER NOT NULL DEFAULT 0,
    recipient_count INTEGER NOT NULL DEFAULT 1,
    is_reply INTEGER NOT NULL DEFAULT 0,
    in_user_whitelist INTEGER NOT NULL DEFAULT 0,
    in_user_blacklist INTEGER NOT NULL DEFAULT 0,
    label_count INTEGER NOT NULL DEFAULT 0,
    link_count INTEGER NOT NULL DEFAULT 0,
    image_count INTEGER NOT NULL DEFAULT 0,
    has_tracking_pixel INTEGER NOT NULL DEFAULT 0,
    unsubscribe_link_in_body INTEGER NOT NULL DEFAULT 0,
    email_age_days INTEGER NOT NULL DEFAULT 0,
    is_in_inbox INTEGER NOT NULL DEFAULT 0,
    is_starred INTEGER NOT NULL DEFAULT 0,
    is_important INTEGER NOT NULL DEFAULT 0,
    was_in_trash INTEGER NOT NULL DEFAULT 0,
    was_in_spam INTEGER NOT NULL DEFAULT 0,
    is_archived INTEGER NOT NULL DEFAULT 0,
    thread_message_count INTEGER NOT NULL DEFAULT 1,
    sender_frequency INTEGER NOT NULL DEFAULT 1,
    subject_text TEXT,
    body_text_short TEXT,
    topic_cluster_id INTEGER,           -- Phase 2: LDA topic cluster (see R10)
    topic_distribution_json TEXT,        -- Phase 2: topic probability vector
    sender_category TEXT,                -- Phase 2: sender domain category
    semantic_embedding_json TEXT,         -- Phase 3: local ONNX embedding vector
    feature_schema_version INTEGER NOT NULL DEFAULT 1,
    extracted_at TEXT NOT NULL
);

CREATE INDEX IF NOT EXISTS idx_email_features_schema_version
    ON email_features(feature_schema_version);
CREATE INDEX IF NOT EXISTS idx_email_features_extracted_at
    ON email_features(extracted_at);

-- Full email archive for feature regeneration
CREATE TABLE IF NOT EXISTS email_archive (
    email_id TEXT PRIMARY KEY,
    thread_id TEXT,  -- Nullable: not all providers support threading
    headers_json TEXT NOT NULL,
    body_text TEXT,
    body_html TEXT,
    provider_type TEXT NOT NULL DEFAULT 'gmail',
    folder_tags_json TEXT NOT NULL DEFAULT '[]',
    size_estimate INTEGER NOT NULL DEFAULT 0,
    received_date TEXT NOT NULL,
    archived_at TEXT NOT NULL,
    snippet TEXT,
    source_folder TEXT NOT NULL DEFAULT 'archive'
);

CREATE INDEX IF NOT EXISTS idx_email_archive_received_date
    ON email_archive(received_date);
CREATE INDEX IF NOT EXISTS idx_email_archive_source_folder
    ON email_archive(source_folder);
CREATE INDEX IF NOT EXISTS idx_email_archive_provider_type
    ON email_archive(provider_type);
CREATE INDEX IF NOT EXISTS idx_email_archive_archived_at
    ON email_archive(archived_at);

-- ML model metadata and versioning
CREATE TABLE IF NOT EXISTS ml_models (
    id TEXT PRIMARY KEY,
    model_type TEXT NOT NULL,
    trainer_name TEXT NOT NULL,
    feature_schema_version INTEGER NOT NULL,
    training_sample_count INTEGER NOT NULL,
    accuracy REAL NOT NULL,
    macro_f1 REAL NOT NULL,
    micro_f1 REAL NOT NULL,
    per_class_metrics_json TEXT NOT NULL,
    model_file_path TEXT NOT NULL,
    is_active INTEGER NOT NULL DEFAULT 0,
    created_at TEXT NOT NULL,
    training_duration_ms INTEGER NOT NULL,
    notes TEXT
);

CREATE INDEX IF NOT EXISTS idx_ml_models_type_active
    ON ml_models(model_type, is_active);

-- Training run audit log
CREATE TABLE IF NOT EXISTS training_events (
    id INTEGER PRIMARY KEY AUTOINCREMENT,
    model_id TEXT REFERENCES ml_models(id),
    model_type TEXT NOT NULL,
    trigger_reason TEXT NOT NULL,
    training_sample_count INTEGER NOT NULL,
    validation_sample_count INTEGER NOT NULL,
    started_at TEXT NOT NULL,
    completed_at TEXT,
    status TEXT NOT NULL DEFAULT 'pending',
    error_message TEXT,
    metrics_json TEXT
);

CREATE INDEX IF NOT EXISTS idx_training_events_status
    ON training_events(status);
CREATE INDEX IF NOT EXISTS idx_training_events_started_at
    ON training_events(started_at);

-- User corrections (highest-signal training data)
CREATE TABLE IF NOT EXISTS user_corrections (
    id INTEGER PRIMARY KEY AUTOINCREMENT,
    email_id TEXT NOT NULL,
    original_classification TEXT NOT NULL,
    corrected_classification TEXT NOT NULL,
    confidence REAL NOT NULL,
    corrected_at TEXT NOT NULL,
    used_in_training INTEGER NOT NULL DEFAULT 0
);

CREATE INDEX IF NOT EXISTS idx_user_corrections_unused
    ON user_corrections(used_in_training) WHERE used_in_training = 0;
CREATE INDEX IF NOT EXISTS idx_user_corrections_email_id
    ON user_corrections(email_id);

-- Archive triage results (primary use case: reclaim storage from archived emails)
CREATE TABLE IF NOT EXISTS archive_triage_results (
    email_id TEXT PRIMARY KEY,
    deletion_confidence REAL NOT NULL,
    recommended_action TEXT NOT NULL DEFAULT 'review',
    bulk_group_key TEXT,
    reason TEXT NOT NULL,
    storage_reclaim_bytes INTEGER NOT NULL DEFAULT 0,
    triaged_at TEXT NOT NULL,
    user_decision TEXT,
    user_decision_at TEXT,
    used_in_training INTEGER NOT NULL DEFAULT 0
);

CREATE INDEX IF NOT EXISTS idx_archive_triage_action
    ON archive_triage_results(recommended_action);
CREATE INDEX IF NOT EXISTS idx_archive_triage_pending
    ON archive_triage_results(user_decision) WHERE user_decision IS NULL;
CREATE INDEX IF NOT EXISTS idx_archive_triage_bulk_group
    ON archive_triage_results(bulk_group_key);
CREATE INDEX IF NOT EXISTS idx_archive_triage_unused_training
    ON archive_triage_results(used_in_training) WHERE used_in_training = 0;
```

## State Transitions

### Model Lifecycle

```
[No Model] → (cold start rules) → [Rule-Based Mode]
    ↓ (100+ labeled emails)
[Training] → (success) → [Model Active v1]
    ↓ (50+ corrections or 7-day schedule)
[Retraining] → (success) → [Model Active v2]
    ↓ (accuracy regression detected)
[Rollback] → [Model Active v1 restored]
```

### Training Event Status

```
pending → running → completed
                  → failed
```

### Classification Mode

```
[Cold Start] → (< 100 labels) → Rule-based only
[Hybrid]     → (100-500 labels) → ML + rule fallback
[ML Primary] → (500+ labels) → ML model with rule override
```

### Archive Triage Workflow (Primary Use Case)

```
[Scan Mailbox] → Fetch all folders (Inbox, Archive, Trash, Spam, Sent, user folders/labels)
       ↓
[Bootstrap Training Data] → Trash/Spam emails → "delete" labels
                          → Starred/Important → "keep" labels
                          → Inbox emails → "keep" labels
                          → Sent emails → context only (not classified)
       ↓
[Train Model] → Build classifier from bootstrapped labels
       ↓
[Classify Archive] → Run model on all archived emails (not in Inbox/Trash/Spam)
       ↓
[Generate Recommendations] → Group by sender/topic → deletion confidence scores
                           → Calculate storage reclaim per group
       ↓
[Present to User] → Bulk groups with accept/reject per group
                  → User reviews and decides
       ↓
[Execute Decisions] → Delete accepted emails via email provider API
                    → Feed decisions back as training data
       ↓
[Retrain] → Model improves from user decisions → repeat
```

### Archive Triage Result Lifecycle

```
[Classified]  → user_decision = null   (pending review)
[Accepted]    → user_decision = "accepted"  (email will be deleted)
[Rejected]    → user_decision = "rejected"  (email preserved, feeds training)
```
