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
- Supports multi-label classification for Gmail label prediction
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
| LabelCount | LabelIds | Numeric | Count of Gmail labels |

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
3. **Gmail label assignments**: Existing user-applied labels indicate classification preferences
4. **Rule matches**: Emails matching AlwaysKeep/AutoTrash rules provide strong ground truth

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
