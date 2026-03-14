# Foundational Reference for ML Architecture Implementation

**Feature**: #54  
**Created**: 2026-03-14  
**Purpose**: Consolidated extraction from research.md, data-model.md, contracts/ for cross-document reuse

---

## Architecture Decisions (from research.md R1-R10)

### R1: ML.NET Framework Choice
- **Decision**: ML.NET for local email classification
- **Rationale**: Native .NET, multi-class/multi-label support, ITransformer pattern fits IProvider architecture
- **Trainers**: SdcaMaximumEntropy, LbfgsMaximumEntropy, LightGbm

### R2: Two-Tier Feature Extraction
- **Tier 1**: Structured features (sender, auth, time, folder placement) - low cost, high signal
- **Tier 2**: Text features (TF-IDF subject/body, links, tracking pixels) - higher cost, medium signal
- **40+ total features** enumerated

### R3: Three-Phase Training Workflow
- **Cold Start (0-100 emails)**: Rule-based classification only
- **Full Training (100+ emails)**: Initial ML.NET model
- **Incremental Updates**: Retrain every 50 corrections or weekly

### R4: Data Storage Schema
- **email_features**: Denormalized feature vectors (~200 bytes/email)
- **email_archive**: Full email storage (~50KB avg/email)
- **ml_models, training_events, user_corrections**: Metadata tables
- **Configurable cap**: Default 50GB with automatic pruning

### R5: Model Versioning Strategy
- **File naming**: `model_v{version}_{timestamp}.zip`
- **Metadata**: SQLite `ml_models` table
- **Rollback**: Change active model pointer
- **Retention**: Last 5 versions, auto-prune older

### R6: Performance Requirements

| Operation | Target | Notes |
|-----------|--------|-------|
| Single email classification | <10ms | PredictionEngine.Predict() |
| Batch classification (100 emails) | <100ms | Parallel feature extraction |
| Feature extraction (structured only) | <5ms | Tabular features |
| Feature extraction (with text) | <50ms | Including TF-IDF |
| Model load from disk | <500ms | MLContext.Model.Load() |
| Full training (10K emails) | <2min | SdcaMaximumEntropy |
| Full training (100K emails) | <5min | SdcaMaximumEntropy |
| Incremental retrain | <30s | Warm-start if supported |

### R7: Provider-Agnostic Architecture
- **New interface**: `IClassificationProvider` (replaces `ILLMProvider` for classification)
- **Inherits**: `IProvider<TConfig>`
- **Key methods**: `ClassifyAsync()`, `TrainAsync()`, `GetModelInfoAsync()`, `RollbackModelAsync()`
- **Result pattern**: All methods return `Result<T>`

### R8: Archive Reclamation as Primary Use Case
**Workflow**:
1. Scan all mailbox folders (Inbox, Archive, Trash, Spam, Sent, user folders)
2. Bootstrap from existing folder signals (Trash/Spam → delete, Starred/Inbox → keep)
3. Classify archived emails (not in Inbox/Trash/Spam)
4. Recommend deletions with confidence + storage reclaim
5. Learn from user decisions → retrain

**Signal Priority for Archive**:
- Folder placement (Trash/Spam/Starred) - **Critical**
- Email age - **High**
- Sender frequency - **High**
- Thread length/last interaction - **High**
- User whitelist/blacklist - **High**
- Authentication (SPF/DKIM) - **Medium**
- Email size - **Medium**
- Subject/body content - **Medium**
- Time-of-day - **Low**

### R9: Canonical Folder Mapping (Multi-Provider Support)

| Canonical | Gmail | IMAP | Outlook | Notes |
|-----------|-------|------|---------|-------|
| `Inbox` | INBOX label | INBOX folder | Inbox folder | Universal |
| `Sent` | SENT label | Sent folder | SentItems | Universal |
| `Trash` | TRASH label | Trash folder | DeletedItems | Strong delete signal |
| `Spam` | SPAM label | Junk folder | JunkEmail | Strong delete signal |
| `Archive` | All Mail (no INBOX) | Archive folder | Archive | Primary triage target |
| `Drafts` | DRAFT label | Drafts folder | Drafts | Excluded from triage |
| `Flagged` | STARRED label | \Flagged flag | Flag.flagStatus | Strong keep signal |
| `Important` | IMPORTANT label | N/A | Importance: high | Keep signal |
| `UserFolder` | User labels | User folders | User folders | Preserves user intent |

**Canonical Flags**:

| Canonical | Gmail | IMAP | Outlook |
|-----------|-------|------|---------|
| `IsRead` | No UNREAD label | \Seen flag | isRead property |
| `IsFlagged` | STARRED label | \Flagged flag | flag.flagStatus |
| `IsImportant` | IMPORTANT label | N/A | importance == "high" |
| `HasAttachment` | Has parts | BODYSTRUCTURE | hasAttachments |

**Provider Capabilities**:

| Provider | API | Thread Support | Label/Folder | Rate Limits |
|----------|-----|----------------|--------------|-------------|
| Gmail | REST API | Native threads | Multi-label | 250 quota/sec |
| Outlook/Hotmail | Graph API | ConversationId | Single-folder + categories | 10k req/10min |
| Yahoo Mail | IMAP only | No threading | Single-folder | Standard IMAP |
| iCloud Mail | IMAP only | No threading | Single-folder | Standard IMAP |
| ProtonMail | Bridge (IMAP) | No threading | Multi-label (Bridge) | Bridge-dependent |
| Generic IMAP | IMAP4rev1 | No threading | Single-folder | Server-dependent |

### R10: Topic Signals — Phased Approach

**Phase 1 (Current)**: TF-IDF on subject + body only  
**Phase 2**: Sender-domain categories + LDA topic modeling (no LLM)  
**Phase 3**: Local ONNX embeddings (no LLM)  
**Phase 4**: Optional LLM keyword extraction (strictly optional)

**EmailFeatureVector reserved fields** (null in Phase 1):
- `TopicClusterId` (int?)
- `TopicDistributionJson` (string?)
- `SenderCategory` (string?)
- `SemanticEmbeddingJson` (string?)

---

## Entities (from data-model.md)

### EmailFeatureVector (40+ fields)

**Structured Features**:
- SenderDomain, SenderKnown, ContactStrength
- SpfResult, DkimResult, DmarcResult
- HasListUnsubscribe, HasAttachments
- HourReceived, DayOfWeek, EmailSizeLog
- SubjectLength, RecipientCount, IsReply
- InUserWhitelist, InUserBlacklist, LabelCount
- LinkCount, ImageCount, HasTrackingPixel, UnsubscribeLinkInBody

**Archive-Specific Features**:
- EmailAgeDays, IsInInbox, IsStarred, IsImportant
- WasInTrash, WasInSpam, IsArchived
- ThreadMessageCount, SenderFrequency

**Text Features**:
- SubjectText (nullable), BodyTextShort (nullable)

**Phase 2+ Topic Features** (nullable):
- TopicClusterId, TopicDistributionJson
- SenderCategory, SemanticEmbeddingJson

**Metadata**:
- FeatureSchemaVersion, ExtractedAt

### EmailArchiveEntry

- EmailId (PK), ThreadId (nullable)
- ProviderType, HeadersJson, BodyText, BodyHtml
- FolderTagsJson, SizeEstimate, ReceivedDate
- ArchivedAt, Snippet, SourceFolder (canonical)

### ArchiveTriageResult

- EmailId (PK), DeletionConfidence (0.0-1.0)
- RecommendedAction ("keep", "delete", "review")
- BulkGroupKey, Reason, StorageReclaimBytes
- TriagedAt, UserDecision (nullable), UserDecisionAt (nullable)
- UsedInTraining

### MlModel

- Id, ModelType, TrainerName, FeatureSchemaVersion
- TrainingSampleCount, Accuracy, MacroF1, MicroF1
- PerClassMetricsJson, ModelFilePath, IsActive
- CreatedAt, TrainingDurationMs, Notes

### TrainingEvent

- Id, ModelId, ModelType, TriggerReason
- TrainingSampleCount, ValidationSampleCount
- StartedAt, CompletedAt, Status, ErrorMessage, MetricsJson

### UserCorrection

- Id, EmailId, OriginalClassification, CorrectedClassification
- Confidence, CorrectedAt, UsedInTraining

---

## Interfaces (from contracts/)

### IClassificationProvider

**Inherits**: `IProvider<ClassificationProviderConfig>`

**Methods**:
- `ClassifyAsync(ClassifyInput, CancellationToken)` → `Result<ClassifyOutput>`
- `TriageArchiveAsync(ArchiveTriageInput, CancellationToken)` → `Result<ArchiveTriageOutput>`
- `GetModelInfoAsync(CancellationToken)` → `Result<ModelInfo>`
- `GetClassificationModeAsync(CancellationToken)` → `Result<ClassificationMode>`

**Config**:
- ModelDirectory, MaxModelVersions, MinTrainingSamples, ConfidenceThreshold

**Types**:
- ModelInfo, ClassificationMode (ColdStart, Hybrid, MlPrimary)
- ArchiveTriageInput, ArchiveTriageOutput, TriageBulkGroup

### IFeatureExtractor

**Methods**:
- `Extract(EmailFull, ContactSignal?, ProviderSignals?, UserRules, sourceFolder)` → `Result<EmailFeatureVector>`
- `ExtractBatch(IReadOnlyList<EmailClassificationInput>, UserRules)` → `Result<IReadOnlyList<EmailFeatureVector>>`
- `SchemaVersion` (property)

**Behavior**:
- Null body → null SubjectText/BodyTextShort, numeric features still computed
- Missing headers → defaults (SenderDomain="unknown", auth="none")
- Batch with failures → partial success (valid vectors returned, invalid logged)

### IModelTrainer

**Methods**:
- `TrainAsync(TrainingConfig, CancellationToken)` → `Result<TrainingResult>`
- `EvaluateAsync(modelId, CancellationToken)` → `Result<ModelMetrics>`
- `RollbackAsync(targetModelId, CancellationToken)` → `Result<bool>`
- `ShouldRetrainAsync(CancellationToken)` → `Result<bool>`
- `PruneOldModelsAsync(CancellationToken)` → `Result<int>`

**Config**:
- TrainingConfig: ModelType, TrainerName, ValidationSplit, TriggerReason

**Types**:
- TrainingResult, ModelMetrics, ClassMetrics

**Retraining Triggers**:
- 50+ unused corrections - High priority
- 7 days since last training - Medium priority
- User request - Immediate
- Feature schema change - Required

---

## State Transitions

### Model Lifecycle
```
[No Model] → [Rule-Based Mode]
    ↓ (100+ labels)
[Training] → [Model Active v1]
    ↓ (50+ corrections or 7-day schedule)
[Retraining] → [Model Active v2]
    ↓ (accuracy regression)
[Rollback] → [Model Active v1 restored]
```

### Classification Mode
```
[Cold Start] → (<100 labels) → Rule-based only
[Hybrid] → (100-500 labels) → ML + rule fallback
[ML Primary] → (500+ labels) → ML with rule override
```

### Archive Triage Workflow
```
[Scan Mailbox] → All folders (Inbox, Archive, Trash, Spam, Sent, user)
    ↓
[Bootstrap Training] → Trash/Spam → "delete", Starred/Inbox → "keep"
    ↓
[Train Model] → Build classifier
    ↓
[Classify Archive] → Archived emails only (not Inbox/Trash/Spam)
    ↓
[Generate Recommendations] → Group by sender/topic, confidence scores
    ↓
[Present to User] → Bulk groups with accept/reject
    ↓
[Execute Decisions] → Delete accepted via provider API
    ↓
[Retrain] → Model improves from decisions
```

---

## Constitution Compliance Mapping

| Principle | Implementation |
|-----------|----------------|
| **I. Provider-Agnostic** | `IClassificationProvider` extends `IProvider<TConfig>`; canonical folder abstraction; no provider-specific features in ML model |
| **II. Result Pattern** | All methods return `Result<T>` — no exceptions thrown |
| **III. Security First** | SQLCipher encryption for features/archive; no external API calls (local ML.NET); OS keychain for sensitive config |
| **IV. MVVM** | N/A (documentation only; future console TUI uses different patterns) |
| **V. One Public Type/File** | Architecture specifies this for all new components |
| **VI. Strict Null Safety** | Explicit nullable annotations (`ThreadId?`, `BodyText?`, topic fields nullable in schema) |
| **VII. Test Coverage** | 95% for ML providers, 100% for data pipeline security |

---

## Cross-Document Terminology

**Canonical Term** → **Consistent Usage**:
- **Archive Reclamation** → Primary use case, not "archive triage cleanup"
- **Canonical Folder** → Universal folder semantics, not "normalized folder"
- **Feature Vector** → `EmailFeatureVector`, not "feature set" or "email features"
- **Classification Mode** → ColdStart/Hybrid/MlPrimary, not "training phase"
- **Provider-Agnostic** → Works across all providers, not "multi-provider"
- **Bootstrapping** → Training from existing folder signals, not "cold start initialization"
- **Deletion Confidence** → 0.0 (keep) to 1.0 (delete), not "triage score"

---

## Performance Targets Summary

| Target | Value | Context |
|--------|-------|---------|
| Email classification | <10ms | Single email with PredictionEngine |
| Batch classification | <100ms | 100 emails parallel |
| Feature extraction (structured) | <5ms | Tabular features only |
| Feature extraction (full) | <50ms | Including TF-IDF |
| Model training (10K emails) | <2min | SdcaMaximumEntropy |
| Model training (100K emails) | <5min | SdcaMaximumEntropy |
| Incremental retrain | <30s | Warm-start |

---

## Ready for User Story Implementation

✅ All foundational materials extracted and consolidated  
✅ Architecture decisions (R1-R10) documented  
✅ Entities mapped from data-model.md  
✅ Interfaces extracted from contracts/  
✅ Canonical folder mapping table ready (R9)  
✅ Performance requirements table ready (R6)  
✅ Archive reclamation workflow defined (R8)  

**Next**: User Stories 1, 2, 3 can now proceed in parallel or sequentially.
