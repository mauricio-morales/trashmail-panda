# Research: ML Architecture Design

**Feature**: #54 — ML Architecture Design  
**Date**: 2026-03-14  
**Status**: Complete

## Research Tasks & Findings

### R1: ML.NET Suitability for Email Classification

**Decision**: ML.NET is the right framework for local email classification.

**Rationale**:
- Native .NET library — no Python/ONNX interop complexity
- Supports multi-class classification (keep/archive/delete/spam) natively via `SdcaMaximumEntropy`, `LbfgsMaximumEntropy`, or `LightGbm` trainers
- Supports multi-label classification for folder/tag prediction (applicable to Gmail labels, Outlook categories, IMAP folders)
- `ITransformer` + `PredictionEngine<TInput, TOutput>` pattern fits cleanly into the `IProvider<TConfig>` architecture
- Model serialization is built-in (`mlContext.Model.Save()` to .zip files) — supports versioning and rollback
- `IDataView` pipeline pattern enables composable feature extraction

**Alternatives considered**:
- **TorchSharp**: More powerful but unnecessarily complex for tabular/text classification on small corpora. Heavy dependency.
- **ONNX Runtime**: Useful for deployment but requires training elsewhere. Breaks the "all .NET" constraint.
- **Accord.NET**: Legacy, no active maintenance. ML.NET supersedes it.

### R2: Feature Extraction Strategy for Email Classification

**Decision**: Two-tier feature extraction — structured features (tabular) + text features (NLP).

**Rationale**:
Email classification benefits from both structured metadata signals (sender domain, authentication results, time patterns) and text-based signals (subject keywords, body content). ML.NET supports both through its `IEstimator<ITransformer>` pipeline.

**Tier 1 — Structured Features (high-signal, low-cost)**:
| Feature | Source | Type | Extraction |
|---------|--------|------|------------|
| SenderDomain | Headers["From"] | Categorical | Parse email → extract domain |
| SenderKnown | ContactSignal.Known | Boolean | Direct map |
| ContactStrength | ContactSignal.Strength | Ordinal (0-2) | Enum → int |
| SpfResult | ProviderSignals.Spf | Categorical | Map to pass/fail/neutral/none |
| DkimResult | ProviderSignals.Dkim | Categorical | Same |
| DmarcResult | ProviderSignals.Dmarc | Categorical | Same |
| HasListUnsubscribe | ProviderSignals.HasListUnsubscribe | Boolean | Direct map |
| HasAttachments | EmailSummary.HasAttachments | Boolean | Direct map |
| Hour | ReceivedDate | Numeric | Extract hour (0-23) |
| DayOfWeek | ReceivedDate | Categorical | Extract day |
| EmailSize | SizeEstimate | Numeric | Direct (log-scale) |
| SubjectLength | Subject | Numeric | Character count |
| RecipientCount | Headers["To"] + Headers["Cc"] | Numeric | Parse + count |
| IsReply | Subject | Boolean | Starts with "Re:" |
| InUserWhitelist | UserRules.AlwaysKeep | Boolean | Match sender/domain |
| InUserBlacklist | UserRules.AutoTrash | Boolean | Match sender/domain |
| LabelCount | FolderTags | Numeric | Count of folders/tags assigned |
| EmailAgeDays | ReceivedDate | Numeric | Days since received (older = more deletable) |
| IsInInbox | LabelIds / Folder | Boolean | Email is in Inbox (strong keep signal) |
| IsStarred | LabelIds / Flags | Boolean | Has STARRED/FLAGGED status (strong keep signal) |
| IsImportant | LabelIds / Flags | Boolean | Has IMPORTANT/HIGH-PRIORITY flag (keep signal) |
| WasInTrash | SourceFolder | Boolean | Fetched from Trash (strong delete signal) |
| WasInSpam | SourceFolder | Boolean | Fetched from Spam/Junk (strong delete signal) |
| IsArchived | Folder / Labels | Boolean | Not in Inbox/Trash/Spam (triage target) |
| ThreadMessageCount | ThreadId | Numeric | Messages in thread (context for relevance) |
| SenderFrequency | SenderDomain | Numeric | How many emails from this sender in corpus |
| LastUserInteraction | email_metadata | DateTime? | Last time user acted on email from this sender |

**Tier 2 — Text Features (medium-signal, higher-cost)**:
| Feature | Source | Type | Extraction |
|---------|--------|------|------------|
| SubjectTokens | Subject | Text → Vector | ML.NET `FeaturizeText` (TF-IDF, n-grams) |
| BodyTokensShort | BodyText[0..500] | Text → Vector | Truncated body TF-IDF |
| LinkCount | BodyHtml | Numeric | Count `<a>` tags |
| ImageCount | BodyHtml | Numeric | Count `<img>` tags |
| HasTrackingPixel | BodyHtml | Boolean | Detect 1x1 images |
| UnsubscribeLinkPresent | BodyHtml | Boolean | Pattern match for unsubscribe links |

**Alternatives considered**:
- **Deep learning embeddings (BERT/GPT)**: Far too heavy for local execution on email corpus. Structured features with TF-IDF achieve 85-95% accuracy for email classification.
- **Full body text processing**: Diminishing returns past ~500 characters. Truncation is standard practice.
- **Image analysis**: Out of scope — attachment content analysis deferred to future iteration.

### R3: Model Training Workflow Design

**Decision**: Three-phase training approach: Cold Start → Full Training → Incremental Updates.

**Rationale**:
A personal email classifier faces the "cold start" problem — no training data exists initially. The workflow must handle:

1. **Cold Start (0-100 emails)**: Rule-based classification using `UserRules` (always-keep/auto-trash) as pseudo-labels. No ML model needed yet.
2. **Full Training (100+ labeled emails)**: Train initial ML.NET model on user-labeled corpus (explicit feedback + implicit signals from user actions).
3. **Incremental Updates**: Retrain periodically (e.g., every 50 new user-corrected classifications or weekly) without discarding prior knowledge.

**Training data sources** (in priority order):
1. **Explicit user feedback**: User marks classification as Correct/Incorrect/Partial (highest signal)
2. **Implicit user actions**: User keeps/deletes/archives email (medium signal)
3. **Mailbox folder placement**: Emails already in Trash or Spam are strong "delete" labels; emails in Inbox or starred/flagged are "keep" labels. Archived emails (not in Inbox) are the primary triage target. Works across all providers — Gmail uses labels, IMAP uses folders, Outlook uses folders+categories.
4. **User-applied tags/labels**: Existing user-applied labels or categories indicate classification preferences
5. **Rule matches**: Emails matching AlwaysKeep/AutoTrash rules provide strong ground truth

**Archive reclamation priority**: The primary use case is triaging the user's existing archive — potentially thousands of emails that were archived rather than deleted. The training pipeline bootstraps from the user's existing folder signals (Trash=delete, Starred/Flagged=keep, Inbox=keep) to build an initial model that can then classify archived emails and recommend which to delete. This workflow applies identically across Gmail, Outlook/Hotmail, IMAP, and other providers — the canonical folder semantics (Inbox, Trash, Spam, Sent, Flagged, Archive) are universal.

**Model lifecycle**:
- Models stored as `.zip` files in versioned directory
- Each model has metadata: version, training date, sample count, accuracy metrics
- Rollback supported by keeping last N model versions (default: 5)
- Automatic retraining triggered by: (a) 50+ new corrections, (b) 7-day schedule, (c) user request

**Alternatives considered**:
- **Transfer learning from pre-trained model**: ML.NET doesn't support this natively for text classification. Cold start rules provide equivalent bootstrapping.
- **Online learning (single-sample updates)**: ML.NET trainers don't support true online learning. Periodic batch retrain is more stable and achieves similar freshness.
- **Federated learning**: Out of scope for a single-user tool.

### R4: Data Storage Schema for Email Features and Archive

**Decision**: Extend existing SQLite schema with two new tables: `email_features` and `email_archive`.

**Rationale**:
Separation of concerns — feature vectors are compact numeric data for fast ML inference, while the email archive stores full email data for feature regeneration.

- **email_features**: Denormalized feature vector per email. ~200 bytes/email. At 100K emails = ~20MB.
- **email_archive**: Full email BLOB storage (headers + body). ~50KB avg/email. At 100K emails = ~5GB.
- **ml_models**: Model metadata and versioning. Tiny table.
- **training_events**: Training run audit log. Tiny table.

**Storage limits**: Configurable cap (default 50GB) with automatic pruning — oldest archive entries pruned first, feature vectors retained longer.

**Alternatives considered**:
- **Separate ML database file**: Adds complexity to connection management. Single SQLCipher DB with new tables is simpler and maintains encryption consistency.
- **File-based feature storage (Parquet/CSV)**: Adds external dependency. SQLite handles tabular data well at this scale.
- **No email archive** (features only): Prevents feature regeneration if extraction logic changes. Archive is worth the storage cost.

### R5: Model Versioning and Rollback Strategy

**Decision**: File-based versioning with metadata in SQLite.

**Rationale**:
- Models saved as `model_v{version}_{timestamp}.zip` in `data/models/` directory
- Metadata (version, accuracy, training sample count, feature schema hash) stored in `ml_models` table
- Active model pointer in `app_config` table  
- Rollback: change active model pointer to previous version
- Keep last 5 versions; prune older automatically
- Feature schema hash ensures model-data compatibility

**Alternatives considered**:
- **Git-based model versioning**: Heavy dependency for model files. Simple file naming + DB metadata is sufficient.
- **Model stored in database as BLOB**: Unnecessarily complicates model loading. File system is natural for ML.NET model loading.

### R6: Performance Requirements and Constraints

**Decision**: Define three performance tiers based on operation type.

**Rationale**:

| Operation | Target | Measurement |
|-----------|--------|-------------|
| Single email classification | <10ms | PredictionEngine.Predict() |
| Batch classification (100 emails) | <100ms | Parallel feature extraction + batch predict |
| Feature extraction (1 email) | <5ms | Structured features only |
| Feature extraction w/ text (1 email) | <50ms | Including TF-IDF vectorization |
| Model load from disk | <500ms | MLContext.Model.Load() |
| Full training (10K emails) | <2min | SdcaMaximumEntropy trainer |
| Full training (100K emails) | <5min | SdcaMaximumEntropy trainer |
| Incremental retrain (from checkpoint) | <30s | Warm-start if supported by trainer |

**Memory constraints**:
- PredictionEngine: <50MB resident memory
- Training: <500MB peak during model training
- Feature cache: In-SQLite, not in-memory

**Alternatives considered**:
- **GPU acceleration**: Not needed at this scale. CPU inference is <10ms per email.
- **Model compression**: Not needed — ML.NET multiclass models are typically <5MB for this feature count.

### R7: Integration with Provider-Agnostic Architecture

**Decision**: New `IClassificationProvider` interface replacing `ILLMProvider` for classification, inheriting from `IProvider<TConfig>`.

**Rationale**:
The current `ILLMProvider` interface is tightly coupled to LLM semantics (`InitAsync(LLMAuth auth)`). The ML approach needs different lifecycle management (model loading, training triggers). A new dedicated interface preserves provider-agnosticity while supporting both ML.NET and potential future providers (ONNX, remote ML service).

The existing `ILLMProvider` can be retained for LLM-specific operations (search query suggestions, grouping) if needed, or deprecated entirely.

**Key interface methods**:
- `ClassifyAsync(ClassifyInput)` → `Result<ClassifyOutput>` (uses Result pattern)
- `TrainAsync(TrainingConfig)` → `Result<TrainingResult>`
- `GetModelInfoAsync()` → `Result<ModelInfo>`
- `RollbackModelAsync(string version)` → `Result<bool>`

**Alternatives considered**:
- **Extend ILLMProvider**: Unclear semantics — ML model isn't an "LLM". Separate interface is cleaner.
- **Replace ILLMProvider entirely**: Premature — LLM may still be useful for natural language features (search suggestions). Keep both, deprecate classification from ILLMProvider.

### R8: Archive Reclamation as Primary Use Case

**Decision**: The tool's primary workflow is **archive triage** — classifying existing archived/deleted/spam emails and recommending bulk deletion to reclaim storage. Incoming email classification is a secondary, steady-state workflow.

**Rationale**:
Most email users have thousands of archived emails that accumulate over years. These emails were archived (removed from Inbox) rather than deleted because the user was unsure whether to keep them. The tool's core value proposition is:

1. **Scan all mailbox folders** — Inbox, Archive, Sent, Trash, Spam/Junk, all user folders/labels — to build comprehensive training data
2. **Bootstrap model** from existing folder signals — emails the user already deleted/trashed are "delete" training labels; starred/flagged/inbox emails are "keep" labels
3. **Classify the archive** — run the trained model against all archived emails (not in Inbox/Trash/Spam)
4. **Recommend deletions** — present grouped bulk deletion recommendations with confidence scores and storage reclaim estimates
5. **Learn from decisions** — user accepts/rejects recommendations, feeding corrections back into the model

**Signal priority for archive reclamation**:
| Signal | Priority | Rationale |
|--------|----------|----------|
| Mailbox folder placement (Trash/Spam → delete) | **Critical** | Pre-existing user intent — these emails were already triaged |
| Mailbox folder placement (Starred/Flagged/Important → keep) | **Critical** | Explicit user value signal |
| Email age (days since received) | **High** | Older archived emails are more likely safe to delete |
| Sender frequency in corpus | **High** | Bulk senders (newsletters, promos) cluster well for deletion |
| Thread length / last interaction | **High** | Long-dormant threads are deletion candidates |
| User whitelist/blacklist rules | **High** | Explicit user policy |
| Authentication signals (SPF/DKIM) | **Medium** | Helps identify legitimate vs. junk senders |
| Email size | **Medium** | Larger emails = more storage recovered |
| Subject/body content | **Medium** | Text similarity to known junk patterns |
| Time-of-day patterns | **Low** | Weak signal for archive triage vs. incoming mail |

**Archive triage output**: Instead of a simple classification enum, the archive triage produces:
- **Deletion confidence**: 0.0 (definitely keep) to 1.0 (definitely delete)
- **Reclaim estimate**: Storage that would be freed
- **Bulk grouping**: Cluster similar deletable emails by sender/topic for batch decisions
- **Reason**: Human-readable explanation ("Newsletter from sender@example.com — 47 similar emails, last opened 2 years ago")

**Alternatives considered**:
- **Treat archive and inbox identically**: Misses the core value — the archive is the storage problem. Incoming emails are a trickle; the archive is the flood.
- **Delete without user confirmation**: Violates safety principles. Always present recommendations and require explicit approval.
- **Classify only unread archive**: Many archived emails were read before archiving. Read-state is not a reliable filter.

### R9: Multi-Provider Email Source Abstraction

**Decision**: The ML architecture operates on **canonical email metadata** — a provider-agnostic representation that all email sources map to. Provider-specific adapters (Gmail, IMAP, Microsoft Graph) normalize their native concepts into canonical folder semantics before features are extracted.

**Rationale**:
Free email users are distributed across multiple providers. Locking the ML pipeline to Gmail-specific concepts (labels, Gmail API IDs) would prevent supporting Outlook/Hotmail, Yahoo Mail, iCloud Mail, ProtonMail, and generic IMAP accounts. Since the feature engineering is based on structural email metadata (sender, date, size, folder placement, flags) rather than provider-specific APIs, abstraction is straightforward.

**Canonical folder semantics** (every provider maps to these):

| Canonical Folder | Gmail | IMAP | Outlook (Graph API) | Notes |
|------------------|-------|------|---------------------|-------|
| `Inbox` | INBOX label | INBOX folder | Inbox folder | Universal |
| `Sent` | SENT label | Sent folder | SentItems folder | Universal |
| `Trash` | TRASH label | Trash/Deleted Items | DeletedItems folder | Strong delete signal |
| `Spam` | SPAM label | Junk folder | JunkEmail folder | Strong delete signal |
| `Archive` | All Mail (no INBOX label) | Archive folder | Archive folder | Primary triage target |
| `Drafts` | DRAFT label | Drafts folder | Drafts folder | Excluded from triage |
| `Flagged` | STARRED label | \Flagged IMAP flag | Flag.flagStatus | Strong keep signal |
| `Important` | IMPORTANT label | N/A (provider-specific) | Importance: high | Keep signal where available |
| `UserFolder` | User-created labels | User-created folders | User-created folders | Preserves user intent |

**Canonical flags** (boolean attributes):

| Canonical Flag | Gmail | IMAP | Outlook |
|----------------|-------|------|---------|
| `IsRead` | No UNREAD label | \Seen flag | isRead property |
| `IsFlagged` | STARRED label | \Flagged flag | flag.flagStatus |
| `IsImportant` | IMPORTANT label | N/A | importance == "high" |
| `HasAttachment` | Has attachment parts | BODYSTRUCTURE | hasAttachments |

**Provider adapter contract**:
Each email provider adapter (the existing `IEmailProvider` interface) is responsible for:
1. Mapping native folder/label IDs → canonical folder names
2. Mapping native flags → canonical boolean flags
3. Providing a stable `ProviderMessageId` (opaque string unique per provider)
4. Providing `ConversationId` (thread grouping, where supported)

The ML pipeline (`IFeatureExtractor`, `IClassificationProvider`, `IModelTrainer`) never sees provider-specific types — only canonical `EmailSummary`, `EmailFull`, and the new `CanonicalEmailMetadata` that includes folder placement and flags.

**Provider-specific considerations**:

| Provider | API | Thread Support | Label/Folder Model | Rate Limits |
|----------|-----|----------------|---------------------|-------------|
| Gmail | REST (Google APIs) | Native threads | Multi-label (1 email → N labels) | 250 quota units/sec |
| Outlook/Hotmail | Microsoft Graph API | ConversationId | Single-folder + categories | 10,000 req/10min |
| Yahoo Mail | IMAP only (no REST API) | No native threading | Single-folder | Standard IMAP limits |
| iCloud Mail | IMAP only | No native threading | Single-folder | Standard IMAP limits |
| ProtonMail | ProtonMail Bridge (IMAP) | No native threading | Multi-label (via Bridge) | Bridge-dependent |
| Generic IMAP | IMAP4rev1 | No native threading | Single-folder | Server-dependent |

**Key design implications**:
1. **Gmail's multi-label model** is the richest — an email can be in INBOX + STARRED + user-label simultaneously. Other providers use single-folder placement. The canonical model supports *both*: `PrimaryFolder` (where the email lives) + `Tags[]` (additional labels/categories).
2. **Threading**: Gmail has native threads; others may not. The ML pipeline treats `ConversationId` as optional — if absent, each email is an independent unit. Thread-based features (ThreadMessageCount, LastUserInteraction) gracefully degrade to single-message values.
3. **Batch operations**: Gmail supports batch modify via label changes; IMAP uses STORE/COPY/EXPUNGE; Outlook Graph uses batch JSON. The `IEmailProvider.BatchModifyAsync` already abstracts this.
4. **Feature extraction is provider-agnostic**: All features derive from canonical fields (sender domain, subject tokens, date, size, canonical folder, canonical flags). No provider-specific features leak into the ML model.

**Alternatives considered**:
- **Gmail-only with future abstraction**: Faster to ship but creates technical debt and forces a rewrite of the ML pipeline later. Since the abstraction cost is low (minimal extra mapping code), doing it now is preferable.
- **JMAP standard**: Modern email protocol but adoption is minimal among major free providers. Not practical as a primary integration path.
- **Per-provider ML models**: Training separate models per provider would fragment the user's data. A single model with provider-agnostic features is simpler and benefits from all the user's email data regardless of source.

### R10: Topic/Subject-Matter Signals — Deferred Design

**Decision**: Topic-based signals are **deferred to Phase 2+** of model training. The Phase 1 model uses structural features + basic TF-IDF only. However, the architecture should be **extensible** for topic signals without requiring a full redesign. No LLM dependency is introduced.

**Rationale**:
Users have strong topical preferences that correlate with keep/delete behavior — a computer engineer keeps tech content and discards e-commerce marketing; a musician keeps music industry emails and discards tech newsletters. Topic signals could significantly improve classification accuracy, especially for the archive triage use case where structural signals (age, sender frequency) may be ambiguous.

However, deep topic understanding is complex, and the first model should prove value with simpler signals before adding this layer. The risk is over-engineering the first iteration.

**The topic signal spectrum** (ordered by LLM dependency):

| Approach | LLM Required | Signal Quality | Complexity | Phase |
|----------|-------------|----------------|------------|-------|
| **TF-IDF on subject + body** | No | Low-medium | Already designed (R2) | **Phase 1 (current)** |
| **Sender-domain topic proxy** | No | Medium | Low — cluster senders by domain category | **Phase 2** |
| **LDA/NMF topic modeling** | No | Medium | Moderate — unsupervised topic extraction via ML.NET | **Phase 2** |
| **Local embedding model (ONNX)** | No (local) | Medium-high | Moderate — sentence embeddings for semantic similarity | **Phase 3** |
| **User interest profile** | No | High | Moderate — build implicit topic preferences from keep/delete patterns | **Phase 2-3** |
| **LLM keyword extraction** | **Yes** | High | Low code, high dependency | **Phase 4 (optional)** |

**What Phase 1 already provides**:
The current TF-IDF features (`SubjectText`, `BodyTextShort`) capture basic topical signal. Words like "invoice", "newsletter", "unsubscribe", "shipping", "meeting" will naturally cluster. Combined with sender domain (which is a strong topic proxy — `github.com` = tech, `marketing.shopify.com` = e-commerce), Phase 1 has reasonable topic coverage without explicit topic modeling.

**Phase 2 topic enrichment (no LLM)**:

1. **Sender-domain category mapping**: Maintain a lightweight lookup of known sender domains → topic categories (e.g., `github.com` → "Development", `linkedin.com` → "Professional", `newsletter.medium.com` → "Tech News"). This is essentially a curated dictionary — zero ML needed. Can be seeded from public domain categorization lists and extended by the user.

2. **LDA topic modeling on the user's corpus**: After accumulating 500+ archived emails, run Latent Dirichlet Allocation (LDA) to discover the user's ~10-20 natural topic clusters. ML.NET supports this via custom transforms. Each email gets a topic distribution vector (e.g., 60% tech, 20% finance, 20% personal). This runs entirely locally.

3. **User interest profile (implicit)**: Track which topic clusters the user tends to keep vs. delete. Over time, the model learns that this specific user keeps "Development" and "Personal" topics but deletes "E-commerce" and "Travel deals". This is just a derived feature — no new infrastructure needed, just a per-topic keep/delete ratio computed from training data.

**Phase 3 enhancement (still no LLM)**:

4. **Local ONNX embedding model**: Use a lightweight sentence embedding model (e.g., MiniLM-L6, ~80MB) running locally via ONNX Runtime. This produces dense semantic vectors that capture meaning beyond keyword overlap. Emails about "Python programming" and "JavaScript development" would cluster together even without shared words. ONNX Runtime is a NuGet package — no external API calls.

**Phase 4 option (LLM-dependent — explicitly optional)**:

5. **LLM-based topic/keyword extraction**: If the user has configured an LLM provider (OpenAI, local Ollama, etc.), use it to extract structured topics and keywords. This produces the highest-quality signals but reintroduces external dependency. Must **never be required** — always optional enrichment on top of local features.

**Key design principle**: Each phase adds signal quality but **none are prerequisites for the previous phase to work**. The model degrades gracefully:
- Phase 1 alone: Structural features + TF-IDF → functional but topic-blind
- + Phase 2: Sender categories + LDA → topic-aware without any ML infrastructure beyond ML.NET
- + Phase 3: Semantic embeddings → nuanced topic understanding, still fully local
- + Phase 4: LLM extraction → maximum quality, but optional

**Architecture preparation for Phase 2+**:

The `IFeatureExtractor` interface does **not** need to change — it already accepts `EmailFull` (which includes body text) and returns `EmailFeatureVector`. The preparation needed is:

1. **Reserve feature vector slots**: The `EmailFeatureVector` entity should be designed to tolerate null/zero-valued topic features. The `FeatureSchemaVersion` field already handles this — when topic features are added, bump the schema version and retrain.

2. **Add optional topic fields to EmailFeatureVector** (not populated in Phase 1):
   - `TopicClusterId` (int?, null until LDA is implemented)
   - `TopicDistributionJson` (string?, null until LDA is implemented)
   - `SenderCategory` (string?, null until domain categorization is implemented)
   - `SemanticEmbeddingJson` (string?, null until ONNX embeddings are implemented)

3. **Feature extraction pipeline is already extensible**: The `IFeatureExtractor.Extract()` method can add topic features internally when the corpus is large enough, without changing the interface. The ML.NET pipeline handles variable-length feature vectors via schema versioning.

**LLM dependency analysis**:
- Phases 1-3: **Zero LLM dependency**. All processing is local (ML.NET, ONNX Runtime, dictionary lookups).
- Phase 4: **Optional LLM enrichment**. The existing `ILLMProvider` interface could be used if configured, but the system works without it.
- The user's concern about LLM coupling is valid — topic extraction is the primary scenario where LLM adds real value. The phased approach ensures the system never *requires* it.

**Alternatives considered**:
- **Include topic signals in Phase 1**: Premature — the structural features haven't been validated yet. Adding topic complexity before proving basic value increases development time and debugging surface area.
- **Skip topic signals entirely**: Misses a significant classification signal. Users have clear topical preferences that structural features alone can't capture (two newsletters from different domains may look identical structurally but differ completely in topic relevance).
- **LLM-first topic extraction**: Contradicts the architectural shift from LLM to local ML. Would make the system dependent on an external API for core functionality.
- **Static topic taxonomy**: A fixed topic hierarchy (Finance, Tech, Social, etc.) would be rigid and miss user-specific topics. LDA discovers the user's natural clusters instead.
