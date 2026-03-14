# Model Training Pipeline Design

**Feature**: TrashMail Panda ML-Based Email Classification  
**Status**: Training Workflow Specification  
**Last Updated**: 2026-03-14  
**Related Documents**: [ML_ARCHITECTURE.md](ML_ARCHITECTURE.md), [FEATURE_ENGINEERING.md](FEATURE_ENGINEERING.md)

---

## Table of Contents

1. [Overview](#overview)
2. [Three Training Phases](#three-training-phases)
3. [Cold Start Procedures](#cold-start-procedures)
4. [Training Data Sources](#training-data-sources)
5. [Archive Reclamation Bootstrapping](#archive-reclamation-bootstrapping)
6. [Provider-Agnostic Bootstrapping](#provider-agnostic-bootstrapping)
7. [Retraining Triggers](#retraining-triggers)
8. [Incremental Update Strategy](#incremental-update-strategy)
9. [Model Versioning](#model-versioning)
10. [Rollback Procedure](#rollback-procedure)
11. [Model Lifecycle States](#model-lifecycle-states)
12. [IModelTrainer Interface Usage](#imodeltrainer-interface-usage)
13. [Training Performance](#training-performance)
14. [Archive Triage Integration](#archive-triage-integration)
15. [Model Evaluation](#model-evaluation)
16. [Failure Scenarios](#failure-scenarios)
17. [Training Event Audit Log](#training-event-audit-log)
18. [References](#references)

---

## Overview

### Training Pipeline Purpose

The **model training pipeline** manages the complete ML model lifecycle — from initial cold start through iterative improvement via user feedback. It addresses the fundamental challenge of personal email classification: **zero training data at application start**.

### ML.NET Framework Choice

**Why ML.NET**:
- Native .NET library — no Python interop complexity
- Multi-class classification trainers (`SdcaMaximumEntropy`, `LbfgsMaximumEntropy`, `LightGbm`)
- `ITransformer` + `PredictionEngine<TInput, TOutput>` pattern fits cleanly into `IProvider<TConfig>` architecture
- Built-in model serialization (`.zip` files) supports versioning and rollback
- `IDataView` pipeline enables composable feature transformations

### Three-Phase Training Approach

The pipeline evolves through three modes based on available training data:

1. **Cold Start (<100 labeled emails)**: Rule-based classification using `UserRules` + folder-based bootstrapping
2. **Hybrid (100-500 labeled emails)**: ML model + rule fallback for low confidence
3. **ML Primary (500+ labeled emails)**: ML model with rule overrides only for explicit whitelist/blacklist

This phased approach ensures the system provides value **immediately** (Cold Start) while improving continuously as more data accumulates.

---

## Three Training Phases

### Phase Details and Transitions

```
┌─────────────────────────────────────────────────────────────┐
│ COLD START MODE (<100 labeled emails)                      │
│ • Rule-based classification only (UserRules)               │
│ • Bootstrap from existing folder placement                 │
│ • Accumulate training data for future model               │
│ • ClassificationMode.ColdStart                             │
└────────────┬────────────────────────────────────────────────┘
             │ (100+ labels accumulated)
             ▼
┌─────────────────────────────────────────────────────────────┐
│ HYBRID MODE (100-500 labeled emails)                       │
│ • ML model for high-confidence predictions                 │
│ • Rule fallback for low confidence (<0.5 threshold)        │
│ • Sufficient data to train, insufficient to fully trust    │
│ • ClassificationMode.Hybrid                                │
└────────────┬────────────────────────────────────────────────┘
             │ (500+ labels accumulated)
             ▼
┌─────────────────────────────────────────────────────────────┐
│ ML PRIMARY MODE (500+ labeled emails)                      │
│ • ML model is primary classifier                           │
│ • Rules only override for explicit whitelist/blacklist     │
│ • High-quality training corpus                             │
│ • ClassificationMode.MlPrimary                             │
└─────────────────────────────────────────────────────────────┘
```

### Classification Logic by Phase

#### Cold Start (<100 labels)

```csharp
public async Task<Result<ClassifyOutput>> ClassifyAsync(ClassifyInput input)
{
    var totalLabels = await _storage.GetTotalLabeledEmailsAsync();

    if (totalLabels < 100)
    {
        // Rule-based classification only
        return ClassifyWithRules(input.Emails, input.UserRules);
    }
    // ...proceed to hybrid or ML modes
}

private ClassifyOutput ClassifyWithRules(IReadOnlyList<EmailClassificationInput> emails, UserRules rules)
{
    var classifications = emails.Select(email =>
    {
        // Whitelist/blacklist checks
        if (rules.AlwaysKeep.Any(r => MatchesRule(r, email)))
            return new Classification { Action = "keep", Confidence = 1.0, Reason = "User whitelist" };

        if (rules.AutoTrash.Any(r => MatchesRule(r, email)))
            return new Classification { Action = "delete", Confidence = 1.0, Reason = "User blacklist" };

        // Folder-based heuristics
        if (email.SourceFolder == "trash" || email.SourceFolder == "spam")
            return new Classification { Action = "delete", Confidence = 0.9, Reason = "In Trash/Spam folder" };

        if (email.CanonicalFlags.IsFlagged || email.SourceFolder == "inbox")
            return new Classification { Action = "keep", Confidence = 0.9, Reason = "Flagged or in Inbox" };

        // Default: keep (conservative)
        return new Classification { Action = "keep", Confidence = 0.5, Reason = "Default (insufficient data)" };
    }).ToList();

    return new ClassifyOutput { Classifications = classifications };
}
```

#### Hybrid (100-500 labels)

```csharp
public async Task<Result<ClassifyOutput>> ClassifyAsync(ClassifyInput input)
{
    var totalLabels = await _storage.GetTotalLabeledEmailsAsync();

    if (totalLabels >= 100 && totalLabels < 500)
    {
        // ML model + rule fallback
        var mlPredictions = PredictWithModel(input.Emails);

        var classifications = mlPredictions.Select((pred, idx) =>
        {
            var email = input.Emails[idx];

            // Rule overrides for high-confidence rules
            if (input.UserRules.AlwaysKeep.Any(r => MatchesRule(r, email)))
                return new Classification { Action = "keep", Confidence = 1.0, Reason = "User whitelist override" };

            if (input.UserRules.AutoTrash.Any(r => MatchesRule(r, email)))
                return new Classification { Action = "delete", Confidence = 1.0, Reason = "User blacklist override" };

            // Use ML prediction if confidence >= 0.5
            if (pred.Confidence >= 0.5)
                return pred;

            // Fallback to folder heuristics for low confidence
            return ClassifyWithFolderHeuristics(email);
        }).ToList();

        return Result.Success(new ClassifyOutput { Classifications = classifications });
    }
    // ...proceed to ML primary mode
}
```

#### ML Primary (500+ labels)

```csharp
public async Task<Result<ClassifyOutput>> ClassifyAsync(ClassifyInput input)
{
    var totalLabels = await _storage.GetTotalLabeledEmailsAsync();

    if (totalLabels >= 500)
    {
        // ML model is primary, rules only for explicit overrides
        var mlPredictions = PredictWithModel(input.Emails);

        var classifications = mlPredictions.Select((pred, idx) =>
        {
            var email = input.Emails[idx];

            // Explicit rule overrides only
            if (input.UserRules.AlwaysKeep.Any(r => MatchesRule(r, email)))
                return new Classification { Action = "keep", Confidence = 1.0, Reason = "User whitelist" };

            if (input.UserRules.AutoTrash.Any(r => MatchesRule(r, email)))
                return new Classification { Action = "delete", Confidence = 1.0, Reason = "User blacklist" };

            // Trust ML prediction
            return pred;
        }).ToList();

        return Result.Success(new ClassifyOutput { Classifications = classifications });
    }
}
```

---

## Cold Start Procedures

### Bootstrapping from Existing Folder Placement

**Problem**: New users have zero explicit training data (no corrections, no manual labels).

**Solution**: Use the user's **existing email folder placement** as pseudo-labels for initial model training.

### Folder-Based Training Label Mapping

| Source Folder (Canonical) | Training Label | Signal Strength | Rationale |
|---------------------------|---------------|-----------------|-----------|
| **trash** | `"delete"` | ⭐⭐⭐⭐⭐ | User already deleted — strongest signal |
| **spam** | `"delete"` | ⭐⭐⭐⭐⭐ | User already marked spam — strongest signal |
| **inbox** | `"keep"` | ⭐⭐⭐⭐ | Active management — strong keep signal |
| **flagged** (canonical flag) | `"keep"` | ⭐⭐⭐⭐⭐ | Explicit importance marker — strongest keep signal |
| **important** (canonical flag) | `"keep"` | ⭐⭐⭐⭐ | High-priority emails typically kept |
| **archive** | `null` | N/A | **PRIMARY TRIAGE TARGET** — needs classification |
| **sent** | Excluded | N/A | Context only (user-sent emails) |
| **drafts** | Excluded | N/A | In progress, not relevant |

### Bootstrapping Workflow

```
┌─────────────────────────────────────────────────────────────┐
│ 1. SCAN ALL MAILBOX FOLDERS                                │
│    IEmailProvider.GetAllFoldersAsync()                      │
│    Fetch: Inbox, Archive, Trash, Spam, Sent, User Folders  │
└────────────┬────────────────────────────────────────────────┘
             │ EmailFull[] with canonical folders/flags
             ▼
┌─────────────────────────────────────────────────────────────┐
│ 2. EXTRACT TRAINING LABELS FROM FOLDER PLACEMENT           │
│    For each email:                                          │
│    • SourceFolder == "trash" / "spam" → label = "delete"   │
│    • IsFlagged / IsImportant / "inbox" → label = "keep"    │
│    • SourceFolder == "archive" → label = null (triage)     │
│    Exclude: sent, drafts                                    │
└────────────┬────────────────────────────────────────────────┘
             │ Labeled dataset (trash/spam/inbox/flagged emails)
             ▼
┌─────────────────────────────────────────────────────────────┐
│ 3. FEATURE EXTRACTION                                       │
│    IFeatureExtractor.ExtractBatch()                         │
│    Transform labeled emails → EmailFeatureVector[]          │
└────────────┬────────────────────────────────────────────────┘
             │ (EmailFeatureVector, label) pairs
             ▼
┌─────────────────────────────────────────────────────────────┐
│ 4. VALIDATE TRAINING DATA SUFFICIENCY                      │
│    Minimum 100 labeled emails required                      │
│    • If <100 labels → stay in Cold Start mode (rules only) │
│    • If 100-500 labels → train initial model (Hybrid mode) │
│    • If 500+ labels → train full model (ML Primary mode)   │
└────────────┬────────────────────────────────────────────────┘
             │ (if sufficient data)
             ▼
┌─────────────────────────────────────────────────────────────┐
│ 5. TRAIN INITIAL ML MODEL                                  │
│    IModelTrainer.TrainAsync(config)                         │
│    Trainer: SdcaMaximumEntropy (multi-class)                │
│    Classes: "keep", "delete"                                │
└────────────┬────────────────────────────────────────────────┘
             │ TrainingResult (model_v1_*.zip)
             ▼
┌─────────────────────────────────────────────────────────────┐
│ 6. EVALUATE MODEL                                           │
│    Validation split (20% holdout)                           │
│    Metrics: Accuracy, Macro-F1, per-class precision/recall  │
│    • If accuracy < 0.6 → log warning, use Hybrid mode      │
│    • If accuracy >= 0.6 → mark as active, use ML mode      │
└────────────┬────────────────────────────────────────────────┘
             │
┌────────────▼────────────────────────────────────────────────┐
│ 7. READY FOR ARCHIVE CLASSIFICATION                        │
│    Model ready to classify archived emails (triage targets) │
│    Begin archive reclamation workflow                       │
└─────────────────────────────────────────────────────────────┘
```

### Cold Start Example

**Scenario**: User with Gmail account, 10,000 archived emails

```
Inbox: 150 emails → 150 "keep" labels
Starred (flagged): 45 emails → 45 "keep" labels
Trash: 320 emails → 320 "delete" labels
Spam: 180 emails → 180 "delete" labels
Archive: 8,500 emails → 8,500 TRIAGE TARGETS (unlabeled)
Sent: 1,200 emails → excluded
Drafts: 5 emails → excluded

Total labeled: 695 emails (150+45+320+180)
Classification mode: ML Primary (500+)
```

**Result**: Sufficient data to train a high-quality initial model. The 8,500 archived emails can now be classified with deletion confidence scores.

---

## Training Data Sources

### Priority Order (Highest to Lowest Signal)

| Source | Priority | Signal Quality | Acquisition |
|--------|----------|---------------|-------------|
| **Explicit user corrections** | ⭐⭐⭐⭐⭐ | Highest | User marks classification as Correct/Incorrect via UI |
| **Archive triage decisions** | ⭐⭐⭐⭐⭐ | Highest | User accepts/rejects bulk deletion recommendations |
| **Folder placement** | ⭐⭐⭐⭐ | High | Existing emails in Trash/Spam/Inbox/Starred |
| **User rules** | ⭐⭐⭐⭐ | High | AlwaysKeep / AutoTrash rules define explicit policy |
| **Implicit user actions** | ⭐⭐⭐ | Medium | User manually moves email to Trash/Archive (post-classification) |

### Training Data Sources Detail

#### 1. Explicit User Corrections (Highest Priority)

**How acquired**:
```
User sees classification: "Delete (confidence: 0.85)"
User disagrees → clicks "Actually, keep this"
System records:
  user_corrections {
    email_id: "msg123",
    original_classification: "delete",
    corrected_classification: "keep",
    confidence: 0.85,
    used_in_training: false
  }
```

**Why highest priority**: User is explicitly teaching the model their preferences. This is the gold standard.

#### 2. Archive Triage Decisions

**How acquired**:
```
TriageArchiveAsync() output:
  Group: "Newsletters from marketing.shopify.com — 47 emails, 142 MB"
  Recommendation: "delete"
  
User clicks "Delete this group" → 47 emails get UserDecision="accepted" in archive_triage_results
User clicks "Keep this group" → 47 emails get UserDecision="rejected" in archive_triage_results

System records 47 new training samples:
  • Accepted deletions → "delete" labels
  • Rejected deletions → "keep" labels (model was wrong)
```

**Why highest priority**: Bulk decisions provide many training samples at once, and rejections are particularly valuable (they correct model errors).

#### 3. Folder Placement (Bootstrapping)

**How acquired**: Scan all folders during initial setup or periodic sync.

**Why high priority**: Reflects user's historical decisions over months/years. Large volume of training data available immediately.

#### 4. User Rules

**How acquired**: User creates rules in settings:
```
AlwaysKeep: sender == "boss@company.com"
AutoTrash: sender_domain == "marketing.spam.com"
```

**Why high priority**: Explicit policy statements. Model should learn to generalize these rules.

#### 5. Implicit User Actions (Lowest Priority)

**How acquired**: After classification, user manually moves email to different folder.

**Why medium priority**: User intent is unclear — did they disagree with classification, or were they organizing for another reason?

### Training Data Aggregation

```csharp
public async Task<IReadOnlyList<TrainingSample>> GetTrainingSamplesAsync()
{
    var samples = new List<TrainingSample>();

    // 1. Explicit corrections (highest priority)
    var corrections = await _storage.GetUnusedCorrectionsAsync();
    samples.AddRange(corrections.Select(c => new TrainingSample
    {
        EmailId = c.EmailId,
        Label = c.CorrectedClassification,
        Source = "user_correction",
        Weight = 2.0  // Double weight for explicit feedback
    }));

    // 2. Archive triage decisions
    var triageDecisions = await _storage.GetUnusedTriageDecisionsAsync();
    samples.AddRange(triageDecisions.Select(t => new TrainingSample
    {
        EmailId = t.EmailId,
        Label = t.UserDecision == "accepted" ? t.RecommendedAction : InvertAction(t.RecommendedAction),
        Source = "triage_decision",
        Weight = 1.5  // Higher weight for triage rejections (error corrections)
    }));

    // 3. Folder-based labels (bootstrap)
    var folderLabels = await _storage.GetEmailsByFolderAsync(new[] { "trash", "spam", "inbox", "flagged" });
    samples.AddRange(folderLabels.Select(e => new TrainingSample
    {
        EmailId = e.EmailId,
        Label = DeriveLabel(e.SourceFolder, e.CanonicalFlags),
        Source = "folder_placement",
        Weight = 1.0  // Standard weight
    }));

    // 4. Rule-derived labels
    var ruleMatches = await _storage.GetEmailsMatchingRulesAsync();
    samples.AddRange(ruleMatches.Select(e => new TrainingSample
    {
        EmailId = e.EmailId,
        Label = e.RuleAction,  // "keep" or "delete"
        Source = "user_rule",
        Weight = 1.5  // High weight for explicit rules
    }));

    return samples;
}
```

---

## Archive Reclamation Bootstrapping

### Primary Use Case Workflow

The **archive reclamation workflow** is the core value proposition. It leverages folder-based bootstrapping to classify thousands of archived emails and recommend bulk deletions.

### Step-by-Step Archive Reclamation

```
┌─────────────────────────────────────────────────────────────┐
│ STEP 1: FETCH ALL MAILBOX FOLDERS                          │
│ IEmailProvider.GetAllFoldersAsync()                         │
│ • Inbox, Archive, Trash, Spam, Sent, User Folders/Labels   │
└────────────┬────────────────────────────────────────────────┘
             │ FolderSummary[] with canonical folder names
             ▼
┌─────────────────────────────────────────────────────────────┐
│ STEP 2: FETCH EMAILS FROM EACH FOLDER                      │
│ For each folder:                                            │
│   emails = IEmailProvider.GetEmailsAsync(folder)            │
│   Tag with sourceFolder (canonical) + canonical flags       │
└────────────┬────────────────────────────────────────────────┘
             │ EmailFull[] with SourceFolder, CanonicalFlags
             ▼
┌─────────────────────────────────────────────────────────────┐
│ STEP 3: EXTRACT TRAINING LABELS (Bootstrap)                │
│ • Trash/Spam → "delete" labels                             │
│ • Starred/Flagged/Inbox → "keep" labels                    │
│ • Archive → null (triage targets, not labeled)             │
│ Store in email_archive table with sourceFolder             │
└────────────┬────────────────────────────────────────────────┘
             │ Training dataset (labeled emails)
             ▼
┌─────────────────────────────────────────────────────────────┐
│ STEP 4: EXTRACT FEATURES FOR TRAINING SET                  │
│ IFeatureExtractor.ExtractBatch(labeled emails)              │
│ Store in email_features table                               │
└────────────┬────────────────────────────────────────────────┘
             │ (EmailFeatureVector, label) pairs
             ▼
┌─────────────────────────────────────────────────────────────┐
│ STEP 5: TRAIN INITIAL MODEL                                │
│ IModelTrainer.TrainAsync({                                  │
│   ModelType: "action",                                      │
│   TrainerName: "SdcaMaximumEntropy",                        │
│   TriggerReason: "cold_start",                              │
│   ValidationSplit: 0.2                                      │
│ })                                                          │
└────────────┬────────────────────────────────────────────────┘
             │ TrainingResult (model_v1_*.zip)
             ▼
┌─────────────────────────────────────────────────────────────┐
│ STEP 6: EXTRACT FEATURES FOR ARCHIVED EMAILS               │
│ For emails in "archive" folder:                             │
│   IFeatureExtractor.ExtractBatch(archived emails)           │
│   Store in email_features with IsArchived=true             │
└────────────┬────────────────────────────────────────────────┘
             │ EmailFeatureVector[] for archived emails
             ▼
┌─────────────────────────────────────────────────────────────┐
│ STEP 7: CLASSIFY ARCHIVED EMAILS                           │
│ IClassificationProvider.TriageArchiveAsync({                │
│   ArchivedEmails: [...],                                    │
│   MinDeletionConfidence: 0.6                                │
│ })                                                          │
│ Produces ArchiveTriageOutput with bulk groups               │
└────────────┬────────────────────────────────────────────────┘
             │ TriageBulkGroup[] (grouped recommendations)
             ▼
┌─────────────────────────────────────────────────────────────┐
│ STEP 8: PRESENT RECOMMENDATIONS TO USER                    │
│ Console TUI displays:                                       │
│ • Group: "Newsletters from example.com — 47 emails, 142 MB"│
│ • Recommendation: Delete                                    │
│ • Confidence: 0.85                                          │
│ • Actions: [Accept] [Reject] [Review Individual]           │
└────────────┬────────────────────────────────────────────────┘
             │ User decision per group
             ▼
┌─────────────────────────────────────────────────────────────┐
│ STEP 9: EXECUTE USER DECISIONS                             │
│ For accepted groups:                                        │
│   IEmailProvider.BatchDeleteAsync(emailIds)                 │
│   Record UserDecision="accepted" in archive_triage_results  │
│ For rejected groups:                                        │
│   Record UserDecision="rejected" (model was wrong)          │
└────────────┬────────────────────────────────────────────────┘
             │ UserDecision recorded
             ▼
┌─────────────────────────────────────────────────────────────┐
│ STEP 10: RETRAIN MODEL (Continuous Improvement)            │
│ IModelTrainer.ShouldRetrainAsync()                          │
│ • Trigger: 50+ new triage decisions                         │
│ • Retrain with updated training data                        │
│ • Model improves from user feedback                         │
└─────────────────────────────────────────────────────────────┘
```

### Bootstrapping Example: 10,000 Email Corpus

```
Gmail account scan results:
Inbox: 200 emails
Starred: 75 emails
Trash: 450 emails
Spam/Junk: 280 emails
Archive: 8,500 emails (PRIMARY TRIAGE TARGET)
Sent: 1,400 emails (excluded)
Drafts: 10 emails (excluded)

Training label derivation:
"delete" labels: 450 (Trash) + 280 (Spam) = 730
"keep" labels: 200 (Inbox) + 75 (Starred) = 275
Total labeled: 1,005 emails

Classification mode: ML Primary (500+)

Train model on 1,005 emails → classify 8,500 archived emails
Predicted deletions (confidence >= 0.6): ~3,200 emails
Grouped into ~85 bulk groups (by sender domain)
Potential storage reclaim: ~12 GB
```

---

## Provider-Agnostic Bootstrapping

### Why Bootstrapping Works Across All Providers

The folder-based bootstrapping workflow is **provider-agnostic** because:

1. **Canonical folder semantics are universal**: Inbox, Trash, Spam, Archive, Flagged exist conceptually on all email providers
2. **Email provider adapters handle mapping**: Each `IEmailProvider` implementation maps native concepts → canonical folders/flags
3. **Training labels derived from canonical values**: The ML pipeline never sees provider-specific identifiers

### Provider-Specific Mapping Examples

#### Gmail (Multi-Label Model)

```csharp
// GmailEmailProvider maps labels → canonical folders
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

public bool IsFlagged(IList<string> labelIds) => labelIds.Contains("STARRED");
public bool IsImportant(IList<string> labelIds) => labelIds.Contains("IMPORTANT");
```

**Training label extraction**:
```
Email with labels: ["All Mail", "STARRED"] → sourceFolder = "archive", IsFlagged = true
Training label: "keep" (flagged emails are kept)
```

#### IMAP (Single-Folder Model)

```csharp
// ImapEmailProvider maps folder names → canonical folders
public string GetCanonicalFolder(string folderPath)
{
    if (folderPath.EndsWith("INBOX", StringComparison.OrdinalIgnoreCase)) return "inbox";
    if (folderPath.Contains("Trash", StringComparison.OrdinalIgnoreCase)) return "trash";
    if (folderPath.Contains("Junk", StringComparison.OrdinalIgnoreCase)) return "spam";
    if (folderPath.Contains("Spam", StringComparison.OrdinalIgnoreCase)) return "spam";
    if (folderPath.Contains("Archive", StringComparison.OrdinalIgnoreCase)) return "archive";
    if (folderPath.Contains("Sent", StringComparison.OrdinalIgnoreCase)) return "sent";
    if (folderPath.Contains("Drafts", StringComparison.OrdinalIgnoreCase)) return "drafts";
    
    return "user_folder";  // Custom user folder
}

public bool IsFlagged(IEnumerable<string> flags) => flags.Contains("\\Flagged");
```

**Training label extraction**:
```
Email in folder "INBOX/Archive" with \Flagged flag → sourceFolder = "archive", IsFlagged = true
Training label: "keep" (flagged emails are kept)
```

#### Outlook (Graph API)

```csharp
// OutlookEmailProvider maps folders + properties → canonical
public string GetCanonicalFolder(string folderDisplayName, string parentFolderId)
{
    if (folderDisplayName == "Inbox") return "inbox";
    if (folderDisplayName == "SentItems") return "sent";
    if (folderDisplayName == "DeletedItems") return "trash";
    if (folderDisplayName == "JunkEmail") return "spam";
    if (folderDisplayName == "Archive") return "archive";
    if (folderDisplayName == "Drafts") return "drafts";
    
    return "user_folder";
}

public bool IsFlagged(Message msg) => msg.Flag?.FlagStatus == "flagged";
public bool IsImportant(Message msg) => msg.Importance == Importance.High;
```

**Training label extraction**:
```
Email in "DeletedItems" folder → sourceFolder = "trash", IsFlagged = false
Training label: "delete" (emails in trash are deleted)
```

### Unified Training Pipeline

**Key insight**: Because all providers map to the same canonical folders, the training pipeline code is **identical** regardless of email source:

```csharp
public async Task<TrainingResult> BootstrapFromMailbox()
{
    // Works identically for Gmail, IMAP, Outlook, etc.
    var allEmails = await _emailProvider.GetAllEmailsAsync();

    var trainingData = allEmails
        .Where(e => e.SourceFolder != "sent" && e.SourceFolder != "drafts")
        .Select(e => new TrainingSample
        {
            EmailId = e.EmailId,
            Label = DeriveLabel(e.SourceFolder, e.CanonicalFlags),
            Features = _featureExtractor.Extract(e, null, null, _userRules, e.SourceFolder).Value
        })
        .Where(s => s.Label != null)  // Exclude unlabeled (archived emails)
        .ToList();

    return await TrainAsync(new TrainingConfig
    {
        ModelType = "action",
        TrainerName = "SdcaMaximumEntropy",
        TriggerReason = "cold_start"
    });
}

private string? DeriveLabel(string sourceFolder, CanonicalFlags flags)
{
    // Universal logic across all providers
    if (sourceFolder == "trash" || sourceFolder == "spam")
        return "delete";

    if (sourceFolder == "inbox" || flags.IsFlagged || flags.IsImportant)
        return "keep";

    return null;  // Archived emails need classification
}
```

---

## Retraining Triggers

### Automatic Retraining Conditions

| Trigger | Threshold | Priority | Rationale |
|---------|-----------|----------|-----------|
| **User correction count** | 50+ unused corrections | High | Significant new feedback accumulated |
| **Archive triage decisions** | 50+ new decisions | High | Bulk decisions provide many training samples |
| **Scheduled retrain** | 7 days since last training | Medium | Regular model refresh |
| **Feature schema change** | FeatureSchemaVersion increment | Required | Old model incompatible with new features |

### Manual Retraining

User can manually trigger retraining via:
```bash
# Console TUI command
> train --force

# Or programmatically
var result = await _modelTrainer.TrainAsync(new TrainingConfig
{
    TriggerReason = "user_request"
});
```

### ShouldRetrainAsync() Logic

```csharp
public async Task<Result<bool>> ShouldRetrainAsync(CancellationToken cancellationToken)
{
    // 1. Check correction count
    var unusedCorrections = await _storage.GetUnusedCorrectionCountAsync();
    if (unusedCorrections >= 50)
    {
        _logger.LogInformation("Retrain triggered: {Count} unused corrections", unusedCorrections);
        return Result.Success(true);
    }

    // 2. Check archive triage decisions
    var unusedTriageDecisions = await _storage.GetUnusedTriageDecisionCountAsync();
    if (unusedTriageDecisions >= 50)
    {
        _logger.LogInformation("Retrain triggered: {Count} unused triage decisions", unusedTriageDecisions);
        return Result.Success(true);
    }

    // 3. Check last training date
    var activeModel = await _storage.GetActiveModelAsync();
    if (activeModel != null)
    {
        var daysSinceTrain = (DateTime.UtcNow - activeModel.CreatedAt).TotalDays;
        if (daysSinceTrain >= 7)
        {
            _logger.LogInformation("Retrain triggered: {Days} days since last training", daysSinceTrain);
            return Result.Success(true);
        }
    }

    // 4. Check feature schema version
    var currentSchemaVersion = _featureExtractor.SchemaVersion;
    if (activeModel != null && activeModel.FeatureSchemaVersion != currentSchemaVersion)
    {
        _logger.LogWarning("Retrain REQUIRED: schema version mismatch (model: v{ModelVersion}, current: v{CurrentVersion})",
            activeModel.FeatureSchemaVersion, currentSchemaVersion);
        return Result.Success(true);
    }

    return Result.Success(false);
}
```

### Retraining Workflow

```
┌─────────────────────────────────────────────────────────────┐
│ 1. TRIGGER DETECTED                                         │
│    ShouldRetrainAsync() → true                              │
│    Reason: 50+ corrections, 7-day schedule, user request    │
└────────────┬────────────────────────────────────────────────┘
             │
┌────────────▼────────────────────────────────────────────────┐
│ 2. AGGREGATE ALL TRAINING DATA                             │
│    • Unused user corrections                                │
│    • Unused archive triage decisions                        │
│    • Folder-based labels (refresh)                          │
│    • User rule matches                                      │
└────────────┬────────────────────────────────────────────────┘
             │ Updated training dataset
             ▼
┌─────────────────────────────────────────────────────────────┐
│ 3. EXTRACT FEATURES (or reuse from cache)                  │
│    For new emails: IFeatureExtractor.ExtractBatch()         │
│    For existing: load from email_features table             │
└────────────┬────────────────────────────────────────────────┘
             │ (EmailFeatureVector, label) pairs
             ▼
┌─────────────────────────────────────────────────────────────┐
│ 4. TRAIN NEW MODEL VERSION                                 │
│    IModelTrainer.TrainAsync(config)                         │
│    Trainer: SdcaMaximumEntropy                              │
│    Version: Incremented (v2, v3, ...)                       │
└────────────┬────────────────────────────────────────────────┘
             │ TrainingResult (model_v{N+1}_*.zip)
             ▼
┌─────────────────────────────────────────────────────────────┐
│ 5. EVALUATE NEW MODEL                                       │
│    Validation split (20% holdout)                           │
│    Compare metrics to previous model:                       │
│    • Accuracy, Macro-F1, per-class precision/recall         │
└────────────┬────────────────────────────────────────────────┘
             │
             ▼─────── (if accuracy >= previous) ──────┐
┌─────────────────────────────────────────────────────▼───────┐
│ 6. SET AS ACTIVE MODEL                                      │
│    Update ml_models: is_active = true for new version       │
│    Mark training data as used: used_in_training = true      │
│    Record training event: status = "completed"              │
└─────────────────────────────────────────────────────────────┘
             │
             ▼─────── (if accuracy < previous) ──────┐
┌─────────────────────────────────────────────────────▼───────┐
│ 6. SAVE BUT DON'T ACTIVATE                                  │
│    Save model file but keep previous as active              │
│    Log warning: accuracy regression detected                │
│    Record training event with notes: "accuracy regression"  │
└─────────────────────────────────────────────────────────────┘
```

---

## Incremental Update Strategy

### Incremental vs. Full Retraining

| Aspect | Incremental Update | Full Retrain |
|--------|-------------------|--------------|
| **Training data** | Previous training data + new corrections/decisions | All available training data from scratch |
| **Speed** | <30 seconds (warm-start if supported) | 2-5 minutes (full training) |
| **Model initialization** | Load previous model weights (if supported by trainer) | Random initialization |
| **Trainer support** | ⚠️ Limited (ML.NET doesn't natively support warm-start for most trainers) | ✅ All trainers |
| **Use case** | Frequent small updates (50-100 new samples) | Major updates (500+ new samples, schema changes) |

### ML.NET Warm-Start Limitations

**Challenge**: Most ML.NET trainers (`SdcaMaximumEntropy`, `LbfgsMaximumEntropy`) don't support warm-start (continuing training from existing model weights).

**Workaround**: Incremental update = **fast full retrain** with optimized data loading:

```csharp
public async Task<Result<TrainingResult>> TrainAsync(TrainingConfig config, CancellationToken cancellationToken)
{
    // Load ALL training data (not just new samples)
    var allSamples = await GetTrainingSamplesAsync();

    // But optimize by:
    // 1. Reusing cached feature vectors (don't re-extract)
    // 2. Loading only updated samples from database
    // 3. Batching database queries
    
    var featureVectors = allSamples.Select(s => LoadOrExtractFeatures(s)).ToList();

    // Train new model from scratch (but fast due to cached features)
    var mlContext = new MLContext(seed: 42);
    var dataView = mlContext.Data.LoadFromEnumerable(featureVectors);

    var pipeline = BuildPipeline(mlContext);
    var model = pipeline.Fit(dataView);

    // Save as new version
    var newVersion = await GetNextModelVersionAsync();
    var modelPath = $"data/models/model_v{newVersion}_{DateTime.UtcNow:yyyyMMdd_HHmmss}.zip";
    mlContext.Model.Save(model, dataView.Schema, modelPath);

    return Result.Success(new TrainingResult { ModelId = newVersion, ModelPath = modelPath });
}

private EmailFeatureVector LoadOrExtractFeatures(TrainingSample sample)
{
    // Optimization: Reuse cached features if already extracted
    var cached = _storage.GetFeatureVector(sample.EmailId);
    if (cached != null && cached.FeatureSchemaVersion == _featureExtractor.SchemaVersion)
    {
        return cached;  // Reuse existing feature vector
    }

    // Extract fresh if not cached or schema changed
    var email = _storage.GetEmailArchive(sample.EmailId);
    return _featureExtractor.Extract(email, null, null, _userRules, email.SourceFolder).Value;
}
```

**Performance**: With cached features, retraining 10,000 samples takes <30 seconds instead of 2 minutes.

---

## Model Versioning

### File Naming Convention

```
data/models/
├── model_v1_20260314_120000.zip   (initial model)
├── model_v2_20260315_093000.zip   (50 corrections retrain)
├── model_v3_20260316_145000.zip   (scheduled retrain)
├── model_v4_20260317_180000.zip   (100 triage decisions)
└── model_v5_20260318_210000.zip   (active model)
```

**Format**: `model_v{version}_{timestamp}.zip`

| Component | Example | Description |
|-----------|---------|-------------|
| **Prefix** | `model_v` | Constant prefix |
| **Version** | `5` | Incrementing integer (1, 2, 3, ...) |
| **Timestamp** | `20260318_210000` | `YYYYMMdd_HHmmss` format |
| **Extension** | `.zip` | ML.NET model format |

### Model Metadata in Database

**ml_models table** stores metadata for each model version:

```sql
CREATE TABLE ml_models (
    id TEXT PRIMARY KEY,                    -- "v5_20260318_210000"
    model_type TEXT NOT NULL,               -- "action" or "label"
    trainer_name TEXT NOT NULL,             -- "SdcaMaximumEntropy"
    feature_schema_version INTEGER NOT NULL,-- Feature schema version (compatibility)
    training_sample_count INTEGER NOT NULL, -- Number of samples used
    accuracy REAL NOT NULL,                 -- Overall accuracy
    macro_f1 REAL NOT NULL,                 -- Macro-averaged F1
    micro_f1 REAL NOT NULL,                 -- Micro-averaged F1
    per_class_metrics_json TEXT NOT NULL,   -- Per-class precision/recall/F1
    model_file_path TEXT NOT NULL,          -- Relative path to .zip
    is_active INTEGER NOT NULL DEFAULT 0,   -- Only 1 model per type is active
    created_at TEXT NOT NULL,               -- ISO 8601 timestamp
    training_duration_ms INTEGER NOT NULL,  -- Training time
    notes TEXT                              -- Optional notes
);
```

**Example row**:
```json
{
  "id": "v5_20260318_210000",
  "model_type": "action",
  "trainer_name": "SdcaMaximumEntropy",
  "feature_schema_version": 1,
  "training_sample_count": 1250,
  "accuracy": 0.87,
  "macro_f1": 0.85,
  "micro_f1": 0.86,
  "per_class_metrics_json": "{\"keep\": {\"precision\": 0.89, \"recall\": 0.84, \"f1\": 0.86}, \"delete\": {\"precision\": 0.85, \"recall\": 0.88, \"f1\": 0.86}}",
  "model_file_path": "data/models/model_v5_20260318_210000.zip",
  "is_active": 1,
  "created_at": "2026-03-18T21:00:00Z",
  "training_duration_ms": 28500,
  "notes": "Retrained after 50 triage decisions"
}
```

### Active Model Pointer

**Only one model per `model_type` can be active** (enforced by database constraint):

```sql
CREATE UNIQUE INDEX idx_ml_models_type_active
    ON ml_models(model_type) WHERE is_active = 1;
```

**Setting a new model as active**:
```csharp
public async Task SetActiveModelAsync(string modelId)
{
    await _db.ExecuteAsync("UPDATE ml_models SET is_active = 0 WHERE model_type = @ModelType", new { ModelType = "action" });
    await _db.ExecuteAsync("UPDATE ml_models SET is_active = 1 WHERE id = @Id", new { Id = modelId });
}
```

### Version Retention and Pruning

**Retention policy**: Keep last 5 model versions, delete older.

```csharp
public async Task<Result<int>> PruneOldModelsAsync(CancellationToken cancellationToken)
{
    var allModels = await _storage.GetAllModelsAsync("action");
    var sortedModels = allModels.OrderByDescending(m => m.CreatedAt).ToList();

    if (sortedModels.Count <= MaxModelVersions)
    {
        return Result.Success(0);  // Nothing to prune
    }

    var modelsToDelete = sortedModels.Skip(MaxModelVersions).ToList();

    foreach (var model in modelsToDelete)
    {
        // Delete model file
        File.Delete(model.ModelFilePath);

        // Delete database record
        await _storage.DeleteModelAsync(model.Id);
        _logger.LogInformation("Pruned old model: {ModelId}", model.Id);
    }

    return Result.Success(modelsToDelete.Count);
}
```

---

## Rollback Procedure

### When to Rollback

| Scenario | Trigger | Action |
|----------|---------|--------|
| **Accuracy regression** | New model accuracy < previous model | Automatically revert to previous version |
| **User reports poor classifications** | User manually triggers rollback | Revert to previous version |
| **Model corruption** | Model file fails to load | Revert to last working version |
| **Testing/debugging** | Developer wants to compare model versions | Temporarily switch to older version |

### Rollback Workflow

```
┌─────────────────────────────────────────────────────────────┐
│ 1. USER TRIGGERS ROLLBACK                                  │
│    > rollback --to v4                                       │
│    Or: IModelTrainer.RollbackAsync("v4_20260317_180000")    │
└────────────┬────────────────────────────────────────────────┘
             │
┌────────────▼────────────────────────────────────────────────┐
│ 2. VALIDATE TARGET MODEL EXISTS                            │
│    Check ml_models table for target version                 │
│    Verify model file exists on disk                         │
└────────────┬────────────────────────────────────────────────┘
             │ (if valid)
             ▼
┌─────────────────────────────────────────────────────────────┐
│ 3. DEACTIVATE CURRENT MODEL                                │
│    UPDATE ml_models SET is_active = 0 WHERE model_type = 'action' AND is_active = 1 │
└────────────┬────────────────────────────────────────────────┘
             │
┌────────────▼────────────────────────────────────────────────┐
│ 4. ACTIVATE TARGET MODEL                                   │
│    UPDATE ml_models SET is_active = 1 WHERE id = 'v4_...'  │
└────────────┬────────────────────────────────────────────────┘
             │
┌────────────▼────────────────────────────────────────────────┐
│ 5. RELOAD MODEL IN MEMORY                                  │
│    PredictionEngine = mlContext.Model.Load(targetModelPath) │
│    Update internal state to use rolled-back model          │
└────────────┬────────────────────────────────────────────────┘
             │
┌────────────▼────────────────────────────────────────────────┐
│ 6. LOG ROLLBACK EVENT                                       │
│    Record in training_events table:                         │
│    • trigger_reason: "rollback"                             │
│    • notes: "Rolled back from v5 to v4 due to user request"│
└────────────┬────────────────────────────────────────────────┘
             │
┌────────────▼────────────────────────────────────────────────┐
│ 7. NOTIFY USER                                              │
│    "Successfully rolled back to model v4 (2026-03-17)"      │
└─────────────────────────────────────────────────────────────┘
```

### Rollback Implementation

```csharp
public async Task<Result<bool>> RollbackAsync(string targetModelId, CancellationToken cancellationToken)
{
    // 1. Validate target model exists
    var targetModel = await _storage.GetModelAsync(targetModelId);
    if (targetModel == null)
    {
        return Result.Failure<bool>(new ValidationError($"Model {targetModelId} not found"));
    }

    if (!File.Exists(targetModel.ModelFilePath))
    {
        return Result.Failure<bool>(new ValidationError($"Model file not found: {targetModel.ModelFilePath}"));
    }

    // 2. Get current active model (for logging)
    var currentModel = await _storage.GetActiveModelAsync("action");

    // 3. Deactivate current, activate target
    await _storage.SetActiveModelAsync(targetModelId);

    // 4. Reload model in memory
    var mlContext = new MLContext();
    _model = mlContext.Model.Load(targetModel.ModelFilePath, out var schema);
    _predictionEngine = _mlContext.Model.CreatePredictionEngine<EmailFeatureVector, ClassificationPrediction>(_model);

    // 5. Log rollback event
    await _storage.RecordTrainingEventAsync(new TrainingEvent
    {
        ModelId = targetModelId,
        ModelType = "action",
        TriggerReason = "rollback",
        Status = "completed",
        StartedAt = DateTime.UtcNow,
        CompletedAt = DateTime.UtcNow,
        Notes = $"Rolled back from {currentModel?.Id ?? "unknown"} to {targetModelId}"
    });

    _logger.LogInformation("Rolled back model from {Current} to {Target}", currentModel?.Id, targetModelId);

    return Result.Success(true);
}
```

---

## Model Lifecycle States

### State Transition Diagram

```
                      ┌─────────────┐
                      │ NO MODEL    │  (Application start, no training data)
                      └──────┬──────┘
                             │ (bootstrap from folders)
                             ▼
                      ┌─────────────┐
                      │ TRAINING    │  (IModelTrainer.TrainAsync() in progress)
                      └──────┬──────┘
                             │ (training completed)
                             ▼
                  ┌──────────────────────┐
                  │ MODEL ACTIVE (v1)    │ (is_active = 1, ready for inference)
                  └──────┬───────────────┘
                         │
         ┌───────────────┼───────────────┐
         │               │               │
         │ (50+ corrections)  (7 days)  │ (user request)
         │               │               │
         ▼               ▼               ▼
  ┌─────────────┐  ┌─────────────┐  ┌─────────────┐
  │ RETRAINING  │  │ RETRAINING  │  │ RETRAINING  │
  └──────┬──────┘  └──────┬──────┘  └──────┬──────┘
         │               │               │
         └───────────────┼───────────────┘
                         │ (new model ready)
                         ▼
              ┌──────────────────────┐
              │ MODEL ACTIVE (v2)    │ (previous model deactivated)
              └──────┬───────────────┘
                     │
                     │ (accuracy regression detected)
                     ▼
              ┌──────────────────────┐
              │ ROLLBACK             │  (revert to v1)
              └──────┬───────────────┘
                     │
                     ▼
              ┌──────────────────────┐
              │ MODEL ACTIVE (v1)    │ (v1 reactivated, v2 kept but inactive)
              └──────────────────────┘
```

### State Descriptions

| State | Description | is_active | Inference Possible? |
|-------|-------------|-----------|---------------------|
| **No Model** | No trained model exists yet. Using rule-based classification (Cold Start mode). | N/A | ✅ Yes (rules only) |
| **Training** | Model training in progress. Previous model still active (if exists). | Previous: 1 | ✅ Yes (using previous model) |
| **Model Active** | Trained model ready for inference. Used by PredictionEngine. | 1 | ✅ Yes (ML model) |
| **Retraining** | New model training in progress. Old model still active. | Old: 1, New: 0 | ✅ Yes (using old model) |
| **Rollback** | Reverting to previous model version. Target model becomes active. | Target: 1, Others: 0 | ✅ Yes (target model) |
| **Pruned** | Old model deleted (beyond retention limit). Record removed from database. | N/A | ❌ No (deleted) |

### Training Event Status

**training_events table** tracks each training run:

```sql
CREATE TABLE training_events (
    id INTEGER PRIMARY KEY AUTOINCREMENT,
    model_id TEXT REFERENCES ml_models(id),  -- Nullable (null if training failed)
    model_type TEXT NOT NULL,
    trigger_reason TEXT NOT NULL,            -- "cold_start", "correction_threshold", "scheduled", "user_request"
    training_sample_count INTEGER NOT NULL,
    validation_sample_count INTEGER NOT NULL,
    started_at TEXT NOT NULL,
    completed_at TEXT,                       -- Nullable (null if in progress or failed)
    status TEXT NOT NULL DEFAULT 'pending',  -- "pending", "running", "completed", "failed"
    error_message TEXT,
    metrics_json TEXT
);
```

**Status transitions**:
```
pending → running → completed
                  → failed
```

---

## IModelTrainer Interface Usage

### Interface Definition

```csharp
namespace TrashMailPanda.Shared;

public interface IModelTrainer
{
    /// <summary>
    /// Train a new model using all available labeled data.
    /// Bootstraps from mailbox folder placement if no explicit labels.
    /// </summary>
    Task<Result<TrainingResult>> TrainAsync(
        TrainingConfig config,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Evaluate an existing model against a validation dataset.
    /// </summary>
    Task<Result<ModelMetrics>> EvaluateAsync(
        string modelId,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Roll back to a previous model version.
    /// </summary>
    Task<Result<bool>> RollbackAsync(
        string targetModelId,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Check if automatic retraining is needed.
    /// </summary>
    Task<Result<bool>> ShouldRetrainAsync(
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Prune old model versions beyond retention limit.
    /// </summary>
    Task<Result<int>> PruneOldModelsAsync(
        CancellationToken cancellationToken = default);
}
```

### Usage Examples

#### Example 1: Initial Training (Cold Start)

```csharp
var trainer = serviceProvider.GetRequiredService<IModelTrainer>();

var trainResult = await trainer.TrainAsync(new TrainingConfig
{
    ModelType = "action",
    TrainerName = "SdcaMaximumEntropy",
    ValidationSplit = 0.2,
    TriggerReason = "cold_start"
});

if (trainResult.IsSuccess)
{
    Console.WriteLine($"Model trained: {trainResult.Value.ModelId}");
    Console.WriteLine($"Accuracy: {trainResult.Value.Accuracy:P2}");
    Console.WriteLine($"Training time: {trainResult.Value.Duration.TotalSeconds:F1}s");
}
else
{
    Console.WriteLine($"Training failed: {trainResult.Error}");
}
```

#### Example 2: Periodic Retraining Check

```csharp
// Background service checks hourly
var shouldRetrain = await trainer.ShouldRetrainAsync();

if (shouldRetrain.IsSuccess && shouldRetrain.Value)
{
    Console.WriteLine("Retraining triggered automatically");
    
    var trainResult = await trainer.TrainAsync(new TrainingConfig
    {
        ModelType = "action",
        TrainerName = "SdcaMaximumEntropy",
        ValidationSplit = 0.2,
        TriggerReason = "scheduled"
    });

    // Handle result...
}
```

#### Example 3: Model Evaluation

```csharp
var evalResult = await trainer.EvaluateAsync("v5_20260318_210000");

if (evalResult.IsSuccess)
{
    var metrics = evalResult.Value;
    Console.WriteLine($"Accuracy: {metrics.Accuracy:P2}");
    Console.WriteLine($"Macro-F1: {metrics.MacroF1:P2}");
    
    foreach (var (className, classMetrics) in metrics.PerClassMetrics)
    {
        Console.WriteLine($"{className}: Precision={classMetrics.Precision:P2}, Recall={classMetrics.Recall:P2}, F1={classMetrics.F1Score:P2}");
    }
}
```

#### Example 4: Manual Rollback

```csharp
var rollbackResult = await trainer.RollbackAsync("v4_20260317_180000");

if (rollbackResult.IsSuccess)
{
    Console.WriteLine("Rollback successful. Model v4 is now active.");
}
else
{
    Console.WriteLine($"Rollback failed: {rollbackResult.Error}");
}
```

---

## Training Performance

### Performance Targets

| Dataset Size | Target Duration | Trainer | Notes |
|--------------|----------------|---------|-------|
| **100 emails** (Hybrid mode start) | <10 seconds | SdcaMaximumEntropy | Minimum viable training |
| **1,000 emails** | <30 seconds | SdcaMaximumEntropy | Typical personal inbox |
| **10,000 emails** | <2 minutes | SdcaMaximumEntropy | Large personal inbox |
| **100,000 emails** | <5 minutes | SdcaMaximumEntropy | Enterprise user |

### Performance Optimizations

#### 1. Feature Vector Caching

**Optimization**: Reuse extracted features instead of re-extracting on every retrain.

```csharp
private async Task<IReadOnlyList<EmailFeatureVector>> LoadOrExtractFeaturesAsync(
    IReadOnlyList<TrainingSample> samples)
{
    var features = new List<EmailFeatureVector>(samples.Count);

    foreach (var sample in samples)
    {
        // Try loading from cache
        var cached = await _storage.GetFeatureVectorAsync(sample.EmailId);

        if (cached != null && cached.FeatureSchemaVersion == _featureExtractor.SchemaVersion)
        {
            features.Add(cached);  // Reuse
        }
        else
        {
            // Extract fresh
            var email = await _storage.GetEmailArchiveAsync(sample.EmailId);
            var extractResult = _featureExtractor.Extract(email, null, null, _userRules, email.SourceFolder);

            if (extractResult.IsSuccess)
            {
                features.Add(extractResult.Value);
                await _storage.SaveFeatureVectorAsync(extractResult.Value);  // Cache for next time
            }
        }
    }

    return features;
}
```

**Speedup**: ~10x faster for retraining (feature extraction is the bottleneck).

#### 2. Parallel Feature Extraction

```csharp
var features = await Task.WhenAll(samples.Select(async sample =>
{
    var cached = await _storage.GetFeatureVectorAsync(sample.EmailId);
    if (cached != null && cached.FeatureSchemaVersion == _featureExtractor.SchemaVersion)
        return cached;

    var email = await _storage.GetEmailArchiveAsync(sample.EmailId);
    return (await _featureExtractor.Extract(email, null, null, _userRules, email.SourceFolder)).Value;
}));
```

**Speedup**: ~4x on 4-core CPU for fresh feature extraction.

#### 3. Batch Database Queries

**Avoid**:
```csharp
// ❌ Slow: 10,000 individual queries
foreach (var sample in samples)
{
    var email = await _db.GetEmailAsync(sample.EmailId);
}
```

**Optimize**:
```csharp
// ✅ Fast: 1 batch query
var emailIds = samples.Select(s => s.EmailId).ToList();
var emails = await _db.GetEmailsAsync(emailIds);
```

---

## Archive Triage Integration

### How User Decisions Feed Back into Training

```
┌─────────────────────────────────────────────────────────────┐
│ USER REVIEWS TRIAGE RECOMMENDATIONS                         │
│ Group: "Newsletters from marketing.shopify.com — 47 emails" │
│ Recommendation: Delete (confidence: 0.85)                   │
│ User clicks: [Accept] or [Reject]                           │
└────────────┬────────────────────────────────────────────────┘
             │
┌────────────▼────────────────────────────────────────────────┐
│ RECORD USER DECISION                                        │
│ For each email in group:                                    │
│   UPDATE archive_triage_results                             │
│   SET user_decision = 'accepted' | 'rejected'               │
│       user_decision_at = NOW(),                             │
│       used_in_training = false                              │
└────────────┬────────────────────────────────────────────────┘
             │
┌────────────▼────────────────────────────────────────────────┐
│ EXECUTE ACTION (if accepted)                                │
│ IEmailProvider.BatchDeleteAsync(emailIds)                   │
│ Delete emails from provider (Gmail, IMAP, etc.)             │
└────────────┬────────────────────────────────────────────────┘
             │
┌────────────▼────────────────────────────────────────────────┐
│ ACCUMULATE TRAINING SIGNALS                                 │
│ • Accepted deletions → "delete" training labels             │
│ • Rejected deletions → "keep" training labels (corrections) │
│ Unused triage decision count increments                     │
└────────────┬────────────────────────────────────────────────┘
             │ (when 50+ decisions accumulated)
             ▼
┌─────────────────────────────────────────────────────────────┐
│ TRIGGER RETRAINING                                          │
│ IModelTrainer.ShouldRetrainAsync() → true                   │
│ IModelTrainer.TrainAsync(trigger="triage_threshold")        │
└────────────┬────────────────────────────────────────────────┘
             │
┌────────────▼────────────────────────────────────────────────┐
│ MODEL IMPROVES                                              │
│ New model trained on updated dataset including:             │
│ • Original bootstrap labels (folder placement)              │
│ • User corrections                                          │
│ • Archive triage decisions (accepted + rejected)            │
│ Model learns user's preferences more accurately             │
└────────────┬────────────────────────────────────────────────┘
             │
┌────────────▼────────────────────────────────────────────────┐
│ MARK AS USED IN TRAINING                                    │
│ UPDATE archive_triage_results                               │
│ SET used_in_training = true                                 │
│ WHERE user_decision IS NOT NULL AND used_in_training = false│
└─────────────────────────────────────────────────────────────┘
```

### Training Sample Derivation from Triage Decisions

```csharp
public async Task<IReadOnlyList<TrainingSample>> GetTrainingSamplesFromTriageAsync()
{
    var decisions = await _storage.GetUnusedTriageDecisionsAsync();

    return decisions.Select(d =>
    {
        // Accepted deletions → "delete" label
        // Rejected deletions → "keep" label (model was wrong, user corrected)
        var label = d.UserDecision == "accepted" ? d.RecommendedAction : InvertAction(d.RecommendedAction);

        return new TrainingSample
        {
            EmailId = d.EmailId,
            Label = label,
            Source = "triage_decision",
            Weight = d.UserDecision == "rejected" ? 2.0 : 1.5  // Higher weight for corrections
        };
    }).ToList();
}

private string InvertAction(string action)
{
    return action == "delete" ? "keep" : "delete";
}
```

---

## Model Evaluation

### Metrics Tracked

| Metric | Formula | Interpretation |
|--------|---------|----------------|
| **Accuracy** | `(TP + TN) / (TP + TN + FP + FN)` | Overall correctness |
| **Macro-F1** | `Average(F1 per class)` | Balanced across classes (handles imbalanced datasets) |
| **Micro-F1** | `Global precision × recall / (precision + recall)` | Weighted by class support |
| **Per-class Precision** | `TP / (TP + FP)` | Correctness of positive predictions |
| **Per-class Recall** | `TP / (TP + FN)` | Coverage of actual positives |
| **Per-class F1** | `2 × (Precision × Recall) / (Precision + Recall)` | Harmonic mean |

### Confusion Matrix Example

For binary classification (keep vs. delete):

```
                 Predicted
                 keep  delete
Actual  keep     520   30      (Precision: 520/(520+45)=0.92, Recall: 520/(520+30)=0.95)
        delete   45    405     (Precision: 405/(405+30)=0.93, Recall: 405/(405+45)=0.90)
```

**Metrics**:
- Accuracy: `(520+405)/1000 = 0.925` (92.5%)
- Macro-F1: `(0.935 + 0.915) / 2 = 0.925` (F1 keep=0.935, F1 delete=0.915)
- Micro-F1: Similar to accuracy for balanced classes

### Evaluation Workflow

```csharp
public async Task<Result<ModelMetrics>> EvaluateAsync(string modelId, CancellationToken cancellationToken)
{
    // 1. Load model
    var model = await _storage.GetModelAsync(modelId);
    var mlContext = new MLContext();
    var loadedModel = mlContext.Model.Load(model.ModelFilePath, out var schema);

    // 2. Get validation dataset (20% holdout from last training)
    var validationSamples = await _storage.GetValidationSamplesAsync(modelId);
    var validationData = mlContext.Data.LoadFromEnumerable(validationSamples);

    // 3. Predict on validation set
    var predictions = loadedModel.Transform(validationData);

    // 4. Evaluate metrics
    var metrics = mlContext.MulticlassClassification.Evaluate(predictions);

    // 5. Compute per-class metrics
    var confusionMatrix = metrics.ConfusionMatrix;
    var perClassMetrics = new Dictionary<string, ClassMetrics>();

    for (int i = 0; i < confusionMatrix.NumberOfClasses; i++)
    {
        var className = confusionMatrix.GetClassLabelAt(i);
        var precision = confusionMatrix.Counts[i][i] / confusionMatrix.Counts[i].Sum();
        var recall = confusionMatrix.Counts[i][i] / confusionMatrix.Counts.Sum(row => row[i]);
        var f1 = 2 * (precision * recall) / (precision + recall);

        perClassMetrics[className] = new ClassMetrics(
            Precision: (float)precision,
            Recall: (float)recall,
            F1Score: (float)f1,
            SupportCount: (int)confusionMatrix.Counts[i].Sum()
        );
    }

    return Result.Success(new ModelMetrics(
        Accuracy: (float)metrics.MacroAccuracy,
        MacroF1: (float)perClassMetrics.Values.Average(m => m.F1Score),
        MicroF1: (float)metrics.MicroAccuracy,
        PerClassMetrics: perClassMetrics
    ));
}
```

---

## Failure Scenarios

### Training Failures and Mitigation

| Failure Scenario | Detection | Mitigation | Recovery |
|------------------|-----------|------------|----------|
| **Insufficient training data** (<100 samples) | Check sample count before training | Stay in Cold Start mode (rules only) | Accumulate more folder labels |
| **Accuracy regression** (new model worse than old) | Compare new model metrics to active model | Don't activate new model, keep old as active | Investigate data quality, retrain later |
| **Corrupted model file** | Model load fails | Rollback to previous version | Delete corrupted file, retrain |
| **Feature schema mismatch** | FeatureSchemaVersion != active model | Invalidate old features, regenerate | Re-extract features from email_archive |
| **Out of disk space** during training | Model save fails | Clean up old models (prune) | Retry training after cleanup |
| **Training timeout** (>10 minutes) | Training duration exceeds limit | Abort training, log error | Reduce dataset size or use simpler trainer |

### Handling Insufficient Training Data

```csharp
public async Task<Result<TrainingResult>> TrainAsync(TrainingConfig config, CancellationToken cancellationToken)
{
    var samples = await GetTrainingSamplesAsync();

    if (samples.Count < MinTrainingSamples)  // Default: 100
    {
        return Result.Failure<TrainingResult>(
            new ValidationError($"Insufficient training data: {samples.Count} samples (minimum: {MinTrainingSamples}). Stay in Cold Start mode."));
    }

    // Proceed with training...
}
```

### Handling Accuracy Regression

```csharp
public async Task<Result<TrainingResult>> TrainAsync(TrainingConfig config, CancellationToken cancellationToken)
{
    // ... training logic ...

    var newMetrics = await EvaluateModelAsync(newModel);
    var activeModel = await _storage.GetActiveModelAsync(config.ModelType);

    if (activeModel != null && newMetrics.Accuracy < activeModel.Accuracy)
    {
        _logger.LogWarning("Accuracy regression detected: new {NewAccuracy:P2} < current {CurrentAccuracy:P2}",
            newMetrics.Accuracy, activeModel.Accuracy);

        // Save model but DON'T activate
        await _storage.SaveModelAsync(newModel, isActive: false, notes: "Accuracy regression — not activated");

        return Result.Success(new TrainingResult
        {
            ModelId = newModel.Id,
            SampleCount = samples.Count,
            Accuracy = newMetrics.Accuracy,
            MacroF1 = newMetrics.MacroF1,
            Duration = trainingDuration,
            ModelFilePath = newModel.ModelFilePath,
            Notes = "Model saved but not activated due to accuracy regression"
        });
    }

    // Activate if no regression
    await _storage.SetActiveModelAsync(newModel.Id);
    return Result.Success(...);
}
```

---

## Training Event Audit Log

### Purpose

The `training_events` table provides a **complete audit trail** of all training runs for:
- Debugging model degradation
- Tracking model evolution over time
- Compliance and reproducibility
- Performance monitoring

### Training Event Schema

```sql
CREATE TABLE training_events (
    id INTEGER PRIMARY KEY AUTOINCREMENT,
    model_id TEXT REFERENCES ml_models(id),  -- Null if failed
    model_type TEXT NOT NULL,                -- "action" or "label"
    trigger_reason TEXT NOT NULL,            -- "cold_start", "correction_threshold", "scheduled", "user_request", "schema_change"
    training_sample_count INTEGER NOT NULL,
    validation_sample_count INTEGER NOT NULL,
    started_at TEXT NOT NULL,
    completed_at TEXT,                       -- Null if in progress or failed
    status TEXT NOT NULL DEFAULT 'pending',  -- "pending", "running", "completed", "failed"
    error_message TEXT,                      -- Populated on failure
    metrics_json TEXT                        -- Full metrics snapshot
);
```

### Example Training Event Records

```json
// Successful cold start training
{
  "id": 1,
  "model_id": "v1_20260314_120000",
  "model_type": "action",
  "trigger_reason": "cold_start",
  "training_sample_count": 695,
  "validation_sample_count": 174,
  "started_at": "2026-03-14T12:00:00Z",
  "completed_at": "2026-03-14T12:01:45Z",
  "status": "completed",
  "error_message": null,
  "metrics_json": "{\"accuracy\": 0.87, \"macro_f1\": 0.85, \"micro_f1\": 0.86}"
}

// Failed training (insufficient data)
{
  "id": 2,
  "model_id": null,
  "model_type": "action",
  "trigger_reason": "user_request",
  "training_sample_count": 45,
  "validation_sample_count": 11,
  "started_at": "2026-03-15T09:00:00Z",
  "completed_at": "2026-03-15T09:00:02Z",
  "status": "failed",
  "error_message": "Insufficient training data: 45 samples (minimum: 100)",
  "metrics_json": null
}

// Scheduled retrain
{
  "id": 3,
  "model_id": "v2_20260315_093000",
  "model_type": "action",
  "trigger_reason": "scheduled",
  "training_sample_count": 752,
  "validation_sample_count": 188,
  "started_at": "2026-03-15T09:30:00Z",
  "completed_at": "2026-03-15T09:31:30Z",
  "status": "completed",
  "error_message": null,
  "metrics_json": "{\"accuracy\": 0.89, \"macro_f1\": 0.87, \"micro_f1\": 0.88}"
}
```

### Querying Training History

```csharp
// Get all training events for a model type
var events = await _db.QueryAsync<TrainingEvent>(
    "SELECT * FROM training_events WHERE model_type = @Type ORDER BY started_at DESC",
    new { Type = "action" });

// Get failed training attempts
var failures = await _db.QueryAsync<TrainingEvent>(
    "SELECT * FROM training_events WHERE status = 'failed' ORDER BY started_at DESC");

// Track accuracy improvement over time
var accuracyHistory = await _db.QueryAsync<dynamic>(
    @"SELECT te.model_id, te.started_at, te.training_sample_count,
             json_extract(te.metrics_json, '$.accuracy') as accuracy
      FROM training_events te
      WHERE te.status = 'completed' AND te.model_type = 'action'
      ORDER BY te.started_at");
```

---

## References

### Related Documentation

| Document | Path | Purpose |
|----------|------|---------|
| **ML Architecture** | `docs/ML_ARCHITECTURE.md` | System architecture and component interactions |
| **Feature Engineering** | `docs/FEATURE_ENGINEERING.md` | Feature extraction and schema design |
| **Data Model** | `specs/054-ml-architecture-design/data-model.md` | Complete entity and SQL schema |
| **IModelTrainer Contract** | `specs/054-ml-architecture-design/contracts/IModelTrainer.md` | Model training service contract |
| **Research** | `specs/054-ml-architecture-design/research.md` | Architecture decisions (R1-R10) |

### External Resources

- **ML.NET Trainers**: https://docs.microsoft.com/en-us/dotnet/machine-learning/how-to-choose-an-ml-net-algorithm
- **ML.NET Model Evaluation**: https://docs.microsoft.com/en-us/dotnet/machine-learning/how-to-guides/evaluate-a-model
- **ML.NET Model Versioning**: https://docs.microsoft.com/en-us/dotnet/machine-learning/how-to-guides/save-load-machine-learning-models-ml-net

---

**Document Status**: ✅ Complete  
**Last Review**: 2026-03-14  
**Next Review**: After implementation (Issue #57)
