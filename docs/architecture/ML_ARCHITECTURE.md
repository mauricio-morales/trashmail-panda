# ML Architecture Design

**Feature**: TrashMail Panda ML-Based Email Classification  
**Status**: Architecture Specification  
**Last Updated**: 2026-03-14  
**Related Documents**: [FEATURE_ENGINEERING.md](FEATURE_ENGINEERING.md), [MODEL_TRAINING_PIPELINE.md](MODEL_TRAINING_PIPELINE.md)

---

## Table of Contents

1. [Overview](#overview)
2. [System Architecture](#system-architecture)
3. [IClassificationProvider Integration](#iclassificationprovider-integration)
4. [Archive Reclamation Workflow](#archive-reclamation-workflow)
5. [Canonical Folder Abstraction](#canonical-folder-abstraction)
6. [Data Storage Architecture](#data-storage-architecture)
7. [Component Interaction Diagrams](#component-interaction-diagrams)
8. [Provider Integration](#provider-integration)
9. [Provider Adapter Contract](#provider-adapter-contract)
10. [Provider Compatibility Matrix](#provider-compatibility-matrix)
11. [Performance Targets](#performance-targets)
12. [Security & Privacy](#security--privacy)
13. [Constitution Compliance](#constitution-compliance)
14. [Multi-Provider Support](#multi-provider-support)
15. [Future Extension Points](#future-extension-points)
16. [References](#references)

---

## Overview

### Project Context

TrashMail Panda is transitioning from external LLM-based email classification (OpenAI GPT-4o-mini) to a locally-trained machine learning model using **ML.NET**. This architectural shift prioritizes:

- **Privacy**: 100% local processing with no external API calls for classification
- **Cost**: Eliminate recurring LLM API costs
- **Performance**: Sub-10ms classification latency with local inference
- **Offline capability**: Full functionality without internet connectivity

### Architectural Shift from LLM to Local ML.NET

| Aspect | Previous (LLM) | New (ML.NET) |
|--------|---------------|--------------|
| **Classification Engine** | OpenAI GPT-4o-mini API | ML.NET local models |
| **Feature Engineering** | Natural language prompts | Structured + text features (40+ fields) |
| **Training Data** | Few-shot examples in prompt | Supervised learning on user's email corpus |
| **Inference Location** | External API call | Local PredictionEngine |
| **Privacy** | Email content sent to OpenAI | All data stays local |
| **Cost** | $0.15 per 1M tokens | Zero marginal cost |
| **Latency** | ~500ms (network + API) | <10ms (local inference) |
| **Offline** | Requires internet | Fully offline-capable |

### Primary Use Case: Archive Reclamation

The **core value proposition** is helping users reclaim storage from thousands of accumulated archived emails. The ML pipeline:

1. **Scans all mailbox folders** via any email provider (Gmail, IMAP, Outlook, etc.)
2. **Bootstraps training** from existing folder signals:
   - Emails in Trash/Spam → "delete" training labels
   - Emails that are Starred/Flagged/in Inbox → "keep" labels
   - Archived emails (not in Inbox) → primary triage targets
3. **Classifies archived emails** and generates bulk deletion recommendations with:
   - Deletion confidence scores (0.0 = keep, 1.0 = delete)
   - Storage reclaim estimates per group
   - Human-readable explanations
4. **Learns from user decisions** — accepts/rejects feed back into model retraining

**Secondary use case**: Incoming email classification for real-time triage (steady-state workflow after archive is cleaned).

---

## System Architecture

### Layer Separation

The ML classification system is organized into five distinct layers:

```
┌─────────────────────────────────────────────────────────────┐
│                    1. UI Layer (Console TUI)                 │
│                     Spectre.Console Interface                │
│          TriageArchive | ClassifyIncoming | TrainModel       │
└────────────────┬────────────────────────────────────────────┘
                 │
┌────────────────▼────────────────────────────────────────────┐
│             2. Classification Engine Layer                   │
│                  IClassificationProvider                     │
│  ┌─────────────────────────────────────────────────────┐   │
│  │ ML.NET PredictionEngine<TInput, TOutput>            │   │
│  │ - ClassifyAsync()                                    │   │
│  │ - TriageArchiveAsync() [PRIMARY WORKFLOW]           │   │
│  │ - GetModelInfoAsync()                                │   │
│  └─────────────────────────────────────────────────────┘   │
└────────────────┬────────────────────────────────────────────┘
                 │
┌────────────────▼────────────────────────────────────────────┐
│             3. Feature Extraction Layer                      │
│                  IFeatureExtractor Service                   │
│  ┌─────────────────────────────────────────────────────┐   │
│  │ Email (canonical) → EmailFeatureVector              │   │
│  │ - Extract() / ExtractBatch()                         │   │
│  │ - 40+ structured + text features                     │   │
│  │ - Provider-agnostic extraction                       │   │
│  └─────────────────────────────────────────────────────┘   │
└────────────────┬────────────────────────────────────────────┘
                 │
┌────────────────▼────────────────────────────────────────────┐
│             4. Model Training Layer                          │
│                  IModelTrainer Service                       │
│  ┌─────────────────────────────────────────────────────┐   │
│  │ ML.NET IDataView → ITransformer                     │   │
│  │ - TrainAsync() [3 modes: Cold Start, Hybrid, ML]    │   │
│  │ - EvaluateAsync() / RollbackAsync()                 │   │
│  │ - ShouldRetrainAsync() / PruneOldModelsAsync()      │   │
│  └─────────────────────────────────────────────────────┘   │
└────────────────┬────────────────────────────────────────────┘
                 │
┌────────────────▼────────────────────────────────────────────┐
│                  5. Data Layer                               │
│         SQLite + SQLCipher Encrypted Storage                 │
│  ┌──────────────────┬──────────────────┬─────────────────┐ │
│  │ email_features   │ email_archive    │ ml_models       │ │
│  │ user_corrections │ training_events  │ triage_results  │ │
│  └──────────────────┴──────────────────┴─────────────────┘ │
│            + File System: data/models/*.zip                  │
└──────────────────────────────────────────────────────────────┘
                 │
┌────────────────▼────────────────────────────────────────────┐
│                  Email Provider Layer                        │
│          IEmailProvider (Provider-Agnostic)                  │
│  ┌─────────┬─────────┬─────────┬─────────┬─────────────┐   │
│  │ Gmail   │ IMAP    │ Outlook │ Yahoo   │ ProtonMail  │   │
│  │ (REST)  │ (IMAP)  │ (Graph) │ (IMAP)  │ (Bridge)    │   │
│  └─────────┴─────────┴─────────┴─────────┴─────────────┘   │
│       All map to canonical folder/flag semantics            │
└──────────────────────────────────────────────────────────────┘
```

### Layer Responsibilities

| Layer | Responsibility | Key Types |
|-------|---------------|-----------|
| **UI Layer** | User interaction, command dispatch | Console commands, display formatters |
| **Classification Engine** | Email classification, triage orchestration | `IClassificationProvider`, `ClassifyInput`, `ArchiveTriageOutput` |
| **Feature Extraction** | Email → feature vector transformation | `IFeatureExtractor`, `EmailFeatureVector` |
| **Model Training** | ML model lifecycle management | `IModelTrainer`, `TrainingConfig`, `ModelMetrics` |
| **Data Layer** | Persistent storage, model files | SQLite tables, .zip model files |
| **Email Provider** | Email fetching, folder mapping | `IEmailProvider`, `EmailFull`, canonical folders |

---

## IClassificationProvider Integration

### IProvider Pattern Compliance

`IClassificationProvider` follows the existing `IProvider<TConfig>` pattern, ensuring consistency with other TrashMail Panda providers (Email, Storage).

```csharp
namespace TrashMailPanda.Shared;

/// <summary>
/// Provider interface for ML-based email classification.
/// PRIMARY USE CASE: Archive reclamation — classify archived emails
/// and recommend bulk deletions to reclaim storage.
/// </summary>
public interface IClassificationProvider : IProvider<ClassificationProviderConfig>
{
    /// <summary>
    /// Classify a batch of emails using the active ML model.
    /// Falls back to rule-based classification when no trained model exists.
    /// </summary>
    Task<Result<ClassifyOutput>> ClassifyAsync(
        ClassifyInput input,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Triage archived emails for deletion recommendations.
    /// This is the PRIMARY workflow for archive reclamation.
    /// Returns grouped recommendations with confidence scores and storage estimates.
    /// </summary>
    Task<Result<ArchiveTriageOutput>> TriageArchiveAsync(
        ArchiveTriageInput input,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Get information about the currently active model.
    /// </summary>
    Task<Result<ModelInfo>> GetModelInfoAsync(
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Get current classification mode (ColdStart, Hybrid, MlPrimary).
    /// </summary>
    Task<Result<ClassificationMode>> GetClassificationModeAsync(
        CancellationToken cancellationToken = default);
}
```

### Result Pattern Usage

All `IClassificationProvider` methods return `Result<T>` instead of throwing exceptions, consistent with TrashMail Panda's error handling philosophy:

```csharp
// ✅ Correct usage
var result = await classificationProvider.TriageArchiveAsync(input);
if (result.IsSuccess)
{
    foreach (var group in result.Value.Groups)
    {
        Console.WriteLine($"Group: {group.Label} — {group.EmailCount} emails, {group.StorageReclaimDisplay}");
    }
}
else
{
    _logger.LogError("Triage failed: {Error}", result.Error);
}

// ❌ Never do this — providers don't throw
try
{
    var output = await classificationProvider.TriageArchiveAsync(input);
}
catch (Exception ex) // This will never be hit
{
    // ...
}
```

### Dependency Injection Lifecycle

```csharp
// Startup.cs or Program.cs
services.AddSingleton<IClassificationProvider, MlNetClassificationProvider>();
services.AddOptions<ClassificationProviderConfig>()
    .Configure(config =>
    {
        config.ModelDirectory = "data/models/";
        config.MaxModelVersions = 5;
        config.MinTrainingSamples = 100;
        config.ConfidenceThreshold = 0.5;
    })
    .ValidateDataAnnotations()
    .ValidateOnStart();

services.AddSingleton<IFeatureExtractor, FeatureExtractorService>();
services.AddSingleton<IModelTrainer, MlNetModelTrainer>();
```

---

## Archive Reclamation Workflow

### Primary Workflow: TriageArchiveAsync

The **TriageArchiveAsync** method implements the core archive reclamation use case:

```
┌──────────────────────────────────────────────────────────────┐
│ 1. SCAN MAILBOX                                              │
│    IEmailProvider.GetAllFoldersAsync()                       │
│    Fetch: Inbox, Archive, Trash, Spam, Sent, User Folders   │
└────────────┬─────────────────────────────────────────────────┘
             │
┌────────────▼─────────────────────────────────────────────────┐
│ 2. BOOTSTRAP TRAINING DATA                                   │
│    Map existing folder placement → training labels:          │
│    • Trash/Spam folders → "delete" labels (strong signal)    │
│    • Starred/Flagged emails → "keep" labels (strong signal)  │
│    • Inbox emails → "keep" labels (keep signal)              │
│    • Archived emails → TRIAGE TARGETS (no label yet)         │
└────────────┬─────────────────────────────────────────────────┘
             │
┌────────────▼─────────────────────────────────────────────────┐
│ 3. TRAIN INITIAL MODEL                                       │
│    IModelTrainer.TrainAsync(TrainingConfig)                  │
│    Build classifier from bootstrapped folder signals         │
│    ClassificationMode: ColdStart → Hybrid → MlPrimary        │
└────────────┬─────────────────────────────────────────────────┘
             │
┌────────────▼─────────────────────────────────────────────────┐
│ 4. CLASSIFY ARCHIVED EMAILS                                  │
│    For each archived email (not in Inbox/Trash/Spam):        │
│    • IFeatureExtractor.Extract() → EmailFeatureVector        │
│    • ML.NET PredictionEngine.Predict() → deletion confidence │
│    • Filter by MinDeletionConfidence (default 0.6)           │
└────────────┬─────────────────────────────────────────────────┘
             │
┌────────────▼─────────────────────────────────────────────────┐
│ 5. GENERATE BULK RECOMMENDATIONS                             │
│    Group similar emails by:                                  │
│    • Sender domain (e.g., all from marketing.example.com)    │
│    • Topic cluster (Phase 2+)                                │
│    Calculate per-group:                                      │
│    • Average deletion confidence                             │
│    • Total storage reclaim (bytes → MB/GB display)           │
│    • Human-readable reason                                   │
└────────────┬─────────────────────────────────────────────────┘
             │
┌────────────▼─────────────────────────────────────────────────┐
│ 6. PRESENT TO USER                                           │
│    ArchiveTriageOutput with TriageBulkGroup[] for review     │
│    User reviews each group → accept or reject                │
│    Store decisions in archive_triage_results table           │
└────────────┬─────────────────────────────────────────────────┘
             │
┌────────────▼─────────────────────────────────────────────────┐
│ 7. EXECUTE DECISIONS                                         │
│    For accepted groups:                                      │
│    • IEmailProvider.BatchDeleteAsync(emailIds)               │
│    Record UserDecision="accepted" in archive_triage_results  │
│    For rejected groups:                                      │
│    • Record UserDecision="rejected" (used as training data)  │
└────────────┬─────────────────────────────────────────────────┘
             │
┌────────────▼─────────────────────────────────────────────────┐
│ 8. RETRAIN MODEL                                             │
│    User decisions feed back as high-quality training data    │
│    IModelTrainer.ShouldRetrainAsync()                        │
│    Trigger: 50+ new corrections or 7-day schedule            │
│    Model improves → repeat for remaining archive             │
└──────────────────────────────────────────────────────────────┘
```

### TriageArchiveAsync Method Specification

```csharp
/// <summary>
/// Entry point for archive reclamation workflow.
/// Classifies archived emails and returns grouped deletion recommendations.
/// </summary>
/// <param name="input">Archived emails to triage + user rules</param>
/// <param name="cancellationToken">Cancellation token</param>
/// <returns>
/// Success: ArchiveTriageOutput with grouped recommendations
/// Failure: ValidationError (no model available, insufficient training data)
///          NetworkError (email provider unavailable)
/// </returns>
public async Task<Result<ArchiveTriageOutput>> TriageArchiveAsync(
    ArchiveTriageInput input,
    CancellationToken cancellationToken = default)
{
    // 1. Validate model availability (fall back to rules if no model)
    // 2. Extract features for all archived emails via IFeatureExtractor
    // 3. Predict deletion confidence via ML.NET PredictionEngine
    // 4. Filter emails above MinDeletionConfidence threshold
    // 5. Group by sender domain or topic cluster
    // 6. Calculate group metrics (avg confidence, storage reclaim)
    // 7. Generate human-readable reasons per group
    // 8. Return ArchiveTriageOutput with TriageBulkGroup[]
}
```

---

## Canonical Folder Abstraction

### Universal Folder Semantics

To support **multiple email providers** (Gmail, IMAP, Outlook, Yahoo, ProtonMail, etc.), the ML architecture operates on **canonical folder semantics** — a provider-agnostic representation.

Each email provider adapter (implementing `IEmailProvider`) is responsible for mapping its native folder/label concepts to these canonical values **before** emails are processed by the ML pipeline.

### Canonical Folder Mapping Table

| Canonical Folder | Gmail | IMAP | Outlook (Graph) | Yahoo Mail (IMAP) | Semantic Meaning |
|------------------|-------|------|-----------------|-------------------|------------------|
| `Inbox` | INBOX label | INBOX folder | Inbox folder | INBOX folder | **Keep signal**: User actively manages this folder |
| `Sent` | SENT label | Sent folder | SentItems folder | Sent folder | Context only (not classified) |
| `Trash` | TRASH label | Trash / Deleted Items | DeletedItems folder | Trash folder | **Strong delete signal**: User already trashed |
| `Spam` | SPAM label | Junk / Spam folder | JunkEmail folder | Bulk folder | **Strong delete signal**: Identified as junk |
| `Archive` | All Mail (no INBOX label) | Archive folder | Archive folder | Archive folder | **Primary triage target**: Uncertain fate |
| `Drafts` | DRAFT label | Drafts folder | Drafts folder | Drafts folder | Excluded from triage (in progress) |
| `Flagged` | STARRED label | \Flagged IMAP flag | flag.flagStatus property | N/A (IMAP flag) | **Strong keep signal**: User marked important |
| `Important` | IMPORTANT label | N/A | importance == "high" | N/A | **Keep signal**: High-priority emails |
| `UserFolder` | User-created label | User-created folder | User-created folder | User-created folder | **Preserves user intent**: Custom categorization |

### Canonical Flag Mapping

Boolean attributes that providers map to:

| Canonical Flag | Gmail | IMAP | Outlook | Meaning |
|----------------|-------|------|---------|---------|
| `IsRead` | No UNREAD label | \Seen flag | isRead property | Email has been read |
| `IsFlagged` | STARRED label | \Flagged flag | flag.flagStatus | User marked as important/starred |
| `IsImportant` | IMPORTANT label | N/A | importance property | High-priority marker |
| `HasAttachment` | Has attachment parts | BODYSTRUCTURE | hasAttachments | Email contains attachments |

### Why Canonical Abstraction Matters

1. **Feature engineering is provider-agnostic**: All 40+ features in `EmailFeatureVector` are derived from canonical fields (sender domain, subject tokens, canonical folder, canonical flags). No provider-specific concepts leak into the ML model.

2. **Training data is unified**: A user with both Gmail and Outlook accounts can train a single model using emails from both sources. The model learns "this sender domain in Trash → delete", not "this Gmail label → delete".

3. **Bootstrapping works identically**: The cold start procedure (Trash → delete labels, Starred → keep labels) applies uniformly across all providers because the canonical semantics are universal.

4. **Future providers are straightforward**: Adding Yahoo Mail, iCloud, ProtonMail requires only implementing the `IEmailProvider` interface and mapping to canonical folders. The ML pipeline requires zero changes.

---

## Data Storage Architecture

### SQLCipher Encrypted Storage

All email data, feature vectors, and training artifacts are stored in a **SQLite database encrypted with SQLCipher**. This ensures:

- **Data at rest encryption**: Email content and features are encrypted on disk
- **Transparent decryption**: Application accesses data normally after database unlock
- **Cross-platform**: Works on Windows, macOS, Linux without OS-specific APIs

### Database Schema Overview

| Table | Purpose | Approximate Size |
|-------|---------|------------------|
| **email_features** | Denormalized feature vectors (40+ fields) | ~200 bytes/email |
| **email_archive** | Full email storage (headers + body) | ~50KB avg/email |
| **archive_triage_results** | Triage outputs + user decisions | ~500 bytes/email |
| **ml_models** | Model metadata (accuracy, version, path) | <1KB per model |
| **training_events** | Training run audit log | <1KB per event |
| **user_corrections** | Explicit user feedback (highest signal) | <200 bytes/correction |

### Storage Capacity Planning

**Scenario**: User with 100,000 archived emails

| Component | Size Calculation | Total |
|-----------|------------------|-------|
| **email_features** | 200 bytes × 100K | ~20 MB |
| **email_archive** | 50KB × 100K | ~5 GB |
| **triage_results** | 500 bytes × 100K | ~50 MB |
| **ml_models** (5 versions) | 5MB × 5 | ~25 MB |
| **Overhead** (indexes, metadata) | ~10% | ~500 MB |
| **Total** | — | **~5.6 GB** |

### Configurable Storage Cap with Pruning

**Default**: 50 GB storage cap

**Pruning Strategy**:
1. **Email Archive First**: Oldest `email_archive` entries pruned when cap reached
2. **Feature Vectors Retained**: `email_features` kept longer (small, high-value for retraining)
3. **Model Versions**: Automatic pruning keeps last 5 versions, deletes older
4. **Triage Results**: Preserved indefinitely (training data source)

**Configuration**:
```csharp
public class StorageConfig
{
    [Range(1, 500)]
    public int MaxStorageGB { get; set; } = 50;

    [Range(1, 20)]
    public int MaxModelVersions { get; set; } = 5;

    public bool EnableAutoPruning { get; set; } = true;
}
```

### Model File Storage

ML.NET trained models are saved as `.zip` files in `data/models/` directory:

```
data/models/
├── model_v1_20260314_120000.zip   (5.2 MB)
├── model_v2_20260315_093000.zip   (5.3 MB)
├── model_v3_20260316_145000.zip   (5.4 MB)
├── model_v4_20260317_180000.zip   (5.3 MB)
└── model_v5_20260318_210000.zip   (5.5 MB) ← Active model
```

**File Naming Convention**: `model_v{version}_{timestamp}.zip`

**Metadata in Database**: `ml_models` table stores:
- Model version, type, trainer name
- Feature schema version (compatibility check)
- Training sample count, accuracy metrics (Accuracy, MacroF1, MicroF1)
- Model file path, active status, creation timestamp

---

## Component Interaction Diagrams

### Data Flow: Email → Classification

```
┌─────────────────┐
│ IEmailProvider  │  Fetch emails from provider (Gmail, IMAP, etc.)
│ GetEmailsAsync()│
└────────┬────────┘
         │ EmailFull[] (with canonical folders/flags)
         ▼
┌─────────────────────────────┐
│ IFeatureExtractor           │  Transform email → feature vector
│ ExtractBatch()              │
│ - Structured features (40+) │
│ - Text features (TF-IDF)    │
│ - Archive signals (age, ...)│
└────────┬────────────────────┘
         │ EmailFeatureVector[]
         ▼
┌──────────────────────────────────┐
│ IClassificationProvider          │  Predict classification
│ ClassifyAsync() / TriageAsync()  │
│ - ML.NET PredictionEngine        │
│ - Confidence thresholds          │
└────────┬─────────────────────────┘
         │ ClassifyOutput / ArchiveTriageOutput
         ▼
┌─────────────────┐
│ Console TUI     │  Present results to user
│ Display results │
└─────────────────┘
```

### Training Flow: User Feedback → Model Update

```
┌────────────────────┐
│ User Decision      │  User corrects classification or accepts triage
│ (UI interaction)   │
└─────────┬──────────┘
          │ UserCorrection / ArchiveTriageResult
          ▼
┌───────────────────────┐
│ SQLite Storage        │  Persist decision as training data
│ user_corrections /    │
│ archive_triage_results│
└─────────┬─────────────┘
          │
          ▼  (trigger: 50+ corrections OR 7 days)
┌───────────────────────────┐
│ IModelTrainer             │  Retrain model
│ ShouldRetrainAsync() →    │
│ TrainAsync()              │
│ - Load training data      │
│ - Feature extraction      │
│ - ML.NET training pipeline│
│ - Model evaluation        │
└─────────┬─────────────────┘
          │ TrainingResult (new model version)
          ▼
┌───────────────────────────┐
│ Update Active Model       │  Set new model as active
│ ml_models.is_active = 1   │
│ Save model file .zip      │
└───────────────────────────┘
```

---

## Provider Integration

### How IEmailProvider Exposes Full Email Data

The existing `IEmailProvider` interface provides email data at two levels:

1. **EmailSummary**: Lightweight metadata (sender, subject, date, size, flags, folder/label IDs)
2. **EmailFull**: Complete email (headers dictionary, body text, body HTML, attachments metadata)

For **feature extraction**, the ML pipeline requires `EmailFull` to access:
- **Headers**: Full headers dictionary for SPF/DKIM/DMARC extraction, List-Unsubscribe detection
- **Body Text**: Plain text content for TF-IDF vectorization and link/tracking pixel detection
- **Body HTML**: HTML content for image count, tracking pixel patterns, unsubscribe link detection
- **ProviderSignals**: Authentication results (SPF, DKIM, DMARC), list-unsubscribe header presence

### Canonical Email Abstraction

While `EmailFull` represents the complete email, the **feature extraction layer** operates on an enriched canonical representation:

```csharp
public class CanonicalEmailMetadata
{
    // Core identity (provider-agnostic)
    public string EmailId { get; init; }        // Opaque provider-assigned ID
    public string? ThreadId { get; init; }      // Conversation ID (nullable for non-threading providers)
    
    // Canonical folder and flags (mapped by provider adapter)
    public string SourceFolder { get; init; }   // "inbox", "archive", "trash", "spam", etc.
    public IReadOnlyList<string> Tags { get; init; }  // Additional folder/label tags
    public bool IsRead { get; init; }
    public bool IsFlagged { get; init; }
    public bool IsImportant { get; init; }
    public bool HasAttachment { get; init; }
    
    // Email content (from EmailFull)
    public Dictionary<string, string> Headers { get; init; }
    public string? BodyText { get; init; }
    public string? BodyHtml { get; init; }
    public long SizeEstimate { get; init; }
    public DateTime ReceivedDate { get; init; }
    
    // Provider-specific signals (already processed)
    public ContactSignal ContactSignal { get; init; }
    public ProviderSignals ProviderSignals { get; init; }
}
```

---

## Provider Adapter Contract

### Responsibilities of IEmailProvider Implementations

Each email provider adapter (Gmail, IMAP, Outlook, etc.) **MUST**:

1. **Map native folder/label IDs to canonical folder names**:
   - Gmail: Map label IDs (`INBOX`, `STARRED`, `TRASH`) → canonical names
   - IMAP: Map folder names (`INBOX`, `Trash`, `Archive`) → canonical names
   - Outlook: Map folder types + well-known IDs → canonical names

2. **Map native flags to canonical boolean flags**:
   - Gmail: UNREAD label → `IsRead = false`, STARRED → `IsFlagged = true`
   - IMAP: `\Seen`, `\Flagged` → canonical flags
   - Outlook: `isRead`, `flag.flagStatus`, `importance` → canonical flags

3. **Provide stable ProviderMessageId**:
   - Gmail: Use Gmail message ID (permanent, unique)
   - IMAP: Use UID + folder path (UID is stable within folder)
   - Outlook: Use Graph API message ID (permanent)

4. **Provide ConversationId (thread grouping)**:
   - Gmail: Native `threadId` (fully supported)
   - IMAP: Nullable (no native threading in IMAP4rev1)
   - Outlook: Native `conversationId` (fully supported)
   - **Fallback**: If not supported, set to `null` — feature extraction handles gracefully

### Example: Gmail Provider Adapter

```csharp
public class GmailEmailProvider : IEmailProvider
{
    private Dictionary<string, string> CanonicalFolderMap = new()
    {
        ["INBOX"] = "inbox",
        ["SENT"] = "sent",
        ["TRASH"] = "trash",
        ["SPAM"] = "spam",
        ["DRAFT"] = "drafts",
        ["STARRED"] = "flagged",   // Maps to canonical "flagged"
        ["IMPORTANT"] = "important",
        // All Mail without INBOX = "archive"
    };

    public string GetCanonicalFolder(IList<string> labelIds)
    {
        if (labelIds.Contains("TRASH")) return "trash";
        if (labelIds.Contains("SPAM")) return "spam";
        if (labelIds.Contains("INBOX")) return "inbox";
        if (labelIds.Contains("SENT")) return "sent";
        if (labelIds.Contains("DRAFT")) return "drafts";
        
        // All Mail but not INBOX = archived
        return "archive";
    }

    public CanonicalFlags GetCanonicalFlags(IList<string> labelIds)
    {
        return new CanonicalFlags
        {
            IsRead = !labelIds.Contains("UNREAD"),
            IsFlagged = labelIds.Contains("STARRED"),
            IsImportant = labelIds.Contains("IMPORTANT"),
        };
    }
}
```

---

## Provider Compatibility Matrix

### Multi-Provider Comparison

| Feature | Gmail | Outlook/Hotmail | Yahoo Mail | iCloud | ProtonMail | Generic IMAP |
|---------|-------|-----------------|------------|--------|------------|--------------|
| **API Type** | REST (Google APIs) | REST (MS Graph) | IMAP only | IMAP only | IMAP (Bridge) | IMAP4rev1 |
| **Threading** | ✅ Native `threadId` | ✅ `conversationId` | ❌ No support | ❌ No support | ❌ No support | ❌ No support |
| **Folder Model** | Multi-label (1 email → N labels) | Single-folder + categories | Single-folder | Single-folder | Multi-label (via Bridge) | Single-folder |
| **Canonical Mapping** | ✅ Full support | ✅ Full support | ✅ Full support | ✅ Full support | ✅ Full support | ✅ Full support |
| **Authentication Signals** | ✅ SPF/DKIM/DMARC headers | ✅ All headers | ✅ All headers | ✅ All headers | ⚠️ Headers may be limited | ✅ All headers |
| **Batch Operations** | ✅ Label modify | ✅ Graph batch API | ⚠️ IMAP STORE | ⚠️ IMAP STORE | ⚠️ IMAP STORE | ⚠️ IMAP STORE |
| **Rate Limits** | 250 quota units/sec | 10,000 req/10min | Server-dependent | Server-dependent | Bridge-dependent | Server-dependent |
| **Feature Parity** | 100% (reference impl) | 100% | 95% (no threading) | 95% (no threading) | 95% (no threading) | 95% (no threading) |

### Feature Degradation for Non-Threading Providers

When `ConversationId` is null (IMAP, Yahoo, etc.):

| Feature | With Threading | Without Threading | Impact |
|---------|----------------|-------------------|--------|
| **ThreadMessageCount** | Actual count from thread | Defaults to 1 | Low (minor signal loss) |
| **LastUserInteraction** | Last interaction in thread | Last interaction with sender | Medium (broader scope) |
| **Thread context** | Keep/delete entire thread | Each email independent | Low (still works, less accurate grouping) |

**Mitigation**: Feature extraction gracefully handles null `ConversationId` by treating each email as a single-message thread. Classification accuracy degrades ~2-5% without threading signals.

---

## Performance Targets

### Classification Performance

| Operation | Target Latency | Measurement Point |
|-----------|---------------|-------------------|
| **Single email classification** | <10ms | `PredictionEngine.Predict()` call |
| **Batch classification (100 emails)** | <100ms | Parallel feature extraction + batch predict |
| **Archive triage (10,000 emails)** | <2 minutes | Full pipeline: fetch → extract → classify → group |

### Feature Extraction Performance

| Operation | Target Latency | Notes |
|-----------|---------------|-------|
| **Structured features only** | <5ms per email | Tabular features (sender, auth, time, folder) |
| **Full features (with text)** | <50ms per email | Including TF-IDF vectorization on subject/body |
| **Batch extraction (1,000 emails)** | <30 seconds | Parallel processing with batching |

### Model Training Performance

| Dataset Size | Target Duration | Trainer | Notes |
|--------------|----------------|---------|-------|
| **10,000 emails** | <2 minutes | SdcaMaximumEntropy | Cold start or full retrain |
| **100,000 emails** | <5 minutes | SdcaMaximumEntropy | Large corpus retrain |
| **Incremental retrain** | <30 seconds | Warm-start if supported | 50+ new corrections added |

### Model Loading Performance

| Operation | Target Latency | Notes |
|-----------|---------------|-------|
| **Model load from disk** | <500ms | `MLContext.Model.Load()` at startup |
| **PredictionEngine creation** | <100ms | One-time initialization |

### Memory Constraints

| Component | Peak Memory | Resident Memory |
|-----------|-------------|-----------------|
| **PredictionEngine** | <50 MB | <30 MB |
| **Training (10K emails)** | <500 MB | N/A (transient) |
| **Training (100K emails)** | <2 GB | N/A (transient) |
| **Feature cache** | 0 MB | Stored in SQLite, not in-memory |

---

## Security & Privacy

### Local Processing Guarantee

**ZERO external API calls for classification**:
- All feature extraction happens locally
- All ML model training happens locally (ML.NET on device)
- All classification inference happens locally (PredictionEngine)
- Email content **never leaves the device**

**Network activity limited to**:
- Email fetching from user's own email provider (Gmail, IMAP, etc.)
- Optional: LLM API calls for Phase 4 topic enrichment (user opt-in required)

### Data at Rest Encryption

**SQLCipher encryption**:
- All tables encrypted: `email_features`, `email_archive`, `archive_triage_results`, `ml_models`, `training_events`, `user_corrections`
- Master key derived from system entropy (no user password required)
- Encryption transparent to application code

**OS Keychain for Sensitive Config**:
- OAuth tokens for email providers stored in OS keychain (DPAPI on Windows, Keychain on macOS, libsecret on Linux)
- ML.NET model files are **not** encrypted (models don't contain PII, only learned patterns)
- Configuration secrets (if any) stored in OS keychain via `SecureStorageManager`

### Privacy-First Design Decisions

| Design Choice | Privacy Benefit |
|---------------|-----------------|
| **Local ML.NET** | No email content sent to third parties |
| **SQLCipher encryption** | Email data encrypted at rest |
| **Configurable storage cap** | User controls data retention |
| **Automatic pruning** | Old emails deleted to minimize storage footprint |
| **No telemetry** | No usage tracking or analytics |
| **No cloud sync** | All data stays on local device |

### Threat Model Considerations

| Threat | Mitigation |
|--------|-----------|
| **Unauthorized disk access** | SQLCipher encryption protects at-rest data |
| **Process memory inspection** | Minimize plaintext email in memory; feature vectors are numeric (low PII) |
| **Stolen backup** | Database encrypted; backups inherit encryption |
| **Malicious model substitution** | Model files have SHA256 hash in `ml_models` table for integrity check |
| **SQL injection** | Parameterized queries enforced; SQLite has limited multi-statement risk |

---

## Constitution Compliance

### Mapping to TrashMail Panda Principles

| Constitutional Principle | Implementation in ML Architecture | Verification |
|-------------------------|-----------------------------------|--------------|
| **I. Provider-Agnostic Architecture** | • `IClassificationProvider` extends `IProvider<TConfig>`<br>• Feature engineering uses canonical folders/flags<br>• No provider-specific features in `EmailFeatureVector`<br>• Bootstrapping works identically across Gmail, IMAP, Outlook | ✅ Canonical abstraction enforced |
| **II. Result Pattern** | • All methods return `Result<T>`<br>• No exceptions thrown from `IClassificationProvider`, `IFeatureExtractor`, `IModelTrainer`<br>• Error types: `ValidationError`, `NetworkError`, `StorageError` | ✅ All public APIs return `Result<T>` |
| **III. Security First** | • SQLCipher encryption for all stored data<br>• No external API calls for classification<br>• OS keychain for OAuth tokens<br>• Local ML.NET processing only | ✅ Zero external API dependency |
| **IV. MVVM with CommunityToolkit.Mvvm** | • N/A (console TUI in Phase 1)<br>• Future UI will use Spectre.Console, not Avalonia<br>• Architecture supports future WPF/Avalonia if needed | ⚠️ N/A for docs |
| **V. One Public Type Per File** | • Architecture specifies this for all new components<br>• `EmailFeatureVector.cs`, `IClassificationProvider.cs`, `IFeatureExtractor.cs`, `IModelTrainer.cs` each in separate files | ✅ Enforced in design |
| **VI. Strict Null Safety** | • Explicit nullable annotations in all contracts<br>• `ThreadId?` (nullable for non-threading providers)<br>• `BodyText?`, `BodyHtml?` (nullable for headerless emails)<br>• Topic fields nullable (`TopicClusterId?`, etc.) | ✅ All contracts use `?` notation |
| **VII. Test Coverage & Quality Gates** | • 95% coverage for `IClassificationProvider`, `IFeatureExtractor`, `IModelTrainer` implementations<br>• 100% coverage for data pipeline security (encryption, key management)<br>• Integration tests with mock providers for each (Gmail, IMAP, Outlook) | ✅ Coverage targets defined |

---

## Multi-Provider Support

### Provider-Agnostic Design Philosophy

The ML architecture is designed to support **any email provider** without modification to the core ML pipeline. This is achieved through:

1. **Canonical abstraction layer**: All providers map to universal folder/flag semantics
2. **Feature engineering independence**: `EmailFeatureVector` contains no provider-specific fields
3. **Training data unification**: Model learns from emails across all providers in a single corpus
4. **Bootstrapping universality**: Folder-based training labels work identically on all providers

### Currently Planned Providers

| Provider | Implementation Status | Canonical Mapping | Notes |
|----------|----------------------|-------------------|-------|
| **Gmail** | ✅ Implemented (existing `GmailEmailProvider`) | Full support | Reference implementation |
| **IMAP (generic)** | Planned (Issue #XX) | Full support (no threading) | Universal fallback for Yahoo, iCloud, custom IMAP |
| **Outlook/Hotmail** | Planned (Issue #XX) | Full support | Microsoft Graph API |

### Future Providers (Extensible)

| Provider | Feasibility | Notes |
|----------|------------|-------|
| **Yahoo Mail** | High | IMAP access available; no REST API |
| **iCloud Mail** | High | IMAP access available |
| **ProtonMail** | Medium | Requires ProtonMail Bridge (local IMAP proxy) |
| **FastMail** | High | Full IMAP support |
| **Zoho Mail** | High | IMAP + REST API available |
| **AOL Mail** | High | IMAP support (similar to Yahoo) |

### Adding a New Provider: Implementation Checklist

To add support for a new email provider:

1. ☐ Implement `IEmailProvider` interface
2. ☐ Create canonical folder mapping (native folder names → canonical values)
3. ☐ Create canonical flag mapping (native flags → boolean properties)
4. ☐ Provide stable `ProviderMessageId` (must be persistent across fetches)
5. ☐ Provide `ConversationId` if threading supported (nullable if not)
6. ☐ Implement `BatchModifyAsync()` for efficient bulk operations
7. ☐ Write integration tests verifying canonical mapping correctness
8. ☐ **No changes to ML pipeline required** — feature extraction and classification work immediately

---

## Future Extension Points

### Phase 2: Enhanced Topic Signals (No LLM Required)

**Sender Domain Categorization**:
- Maintain curated dictionary: `github.com` → "Development", `linkedin.com` → "Professional"
- Add `SenderCategory` field to `EmailFeatureVector` (already reserved, nullable in Phase 1)
- Seed from public domain categorization lists + user curation

**LDA Topic Modeling**:
- Run Latent Dirichlet Allocation on user's corpus (500+ emails)
- Discover 10-20 natural topic clusters specific to user's email patterns
- Populate `TopicClusterId` and `TopicDistributionJson` in `EmailFeatureVector`
- Track per-topic keep/delete ratios as user interest profile

### Phase 3: Local Semantic Embeddings (No LLM Required)

**ONNX Runtime Integration**:
- Use lightweight sentence embedding model (e.g., all-MiniLM-L6-v2, ~80MB)
- Run locally via ONNX Runtime (.NET NuGet package)
- Populate `SemanticEmbeddingJson` in `EmailFeatureVector`
- Improve classification accuracy by ~5-10% through semantic similarity

**Benefits**:
- Cluster emails by meaning, not just keywords
- "Python programming" and "JavaScript development" would cluster together
- Fully local, no external API calls

### Phase 4: Optional LLM Enrichment

**Keyword/Topic Extraction via LLM**:
- **User opt-in required** — not enabled by default
- Use existing `ILLMProvider` if configured
- Extract structured topics and keywords enrichment on top of local features
- Highest quality signal but reintroduces external dependency

**Constraint**: Must **never be required** — Phase 1-3 features provide full functionality.

### Advanced Features (Post-MVP)

- **Multi-label classification**: Predict multiple folders/tags per email (Gmail use case)
- **Contextual classification**: Use thread history to classify new emails in conversation
- **Adaptive retraining**: Detect concept drift (user's preferences change over time) and auto-retrain
- **Explainability**: Generate feature importance reports ("You keep emails from this sender", "You delete newsletters older than 6 months")
- **Transfer learning**: Share anonymized model structure (not data) across users for cold start improvement

---

## References

### Related Documentation

| Document | Path | Purpose |
|----------|------|---------|
| **Parent Architecture** | `docs/ARCHITECTURE_SHIFT_TO_LOCAL_ML.md` | High-level rationale for LLM → ML.NET shift |
| **Feature Engineering Spec** | `docs/FEATURE_ENGINEERING.md` | Detailed feature extraction specification |
| **Model Training Pipeline** | `docs/MODEL_TRAINING_PIPELINE.md` | Training workflow and model lifecycle |
| **Specification** | `specs/054-ml-architecture-design/spec.md` | Original feature requirements |
| **Data Model** | `specs/054-ml-architecture-design/data-model.md` | Complete entity and SQL schema definitions |
| **Research** | `specs/054-ml-architecture-design/research.md` | Architecture decisions (R1-R10) with rationale |
| **IClassificationProvider Contract** | `specs/054-ml-architecture-design/contracts/IClassificationProvider.md` | Provider interface specification |
| **IFeatureExtractor Contract** | `specs/054-ml-architecture-design/contracts/IFeatureExtractor.md` | Feature extraction service contract |
| **IModelTrainer Contract** | `specs/054-ml-architecture-design/contracts/IModelTrainer.md` | Model training service contract |

### External Resources

- **ML.NET Documentation**: https://docs.microsoft.com/en-us/dotnet/machine-learning/
- **SQLCipher**: https://www.zetetic.net/sqlcipher/
- **Gmail API**: https://developers.google.com/gmail/api
- **Microsoft Graph API**: https://docs.microsoft.com/en-us/graph/api/resources/mail-api-overview
- **IMAP4rev1 RFC**: https://tools.ietf.org/html/rfc3501

---

**Document Status**: ✅ Complete  
**Last Review**: 2026-03-14  
**Next Review**: After implementation (Issue #55-59)
