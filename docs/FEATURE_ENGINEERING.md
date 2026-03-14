# Feature Engineering Design

**Feature**: TrashMail Panda ML-Based Email Classification  
**Status**: Feature Extraction Specification  
**Last Updated**: 2026-03-14  
**Related Documents**: [ML_ARCHITECTURE.md](ML_ARCHITECTURE.md), [MODEL_TRAINING_PIPELINE.md](MODEL_TRAINING_PIPELINE.md)

---

## Table of Contents

1. [Overview](#overview)
2. [Tier 1 Structured Features](#tier-1-structured-features)
3. [Archive-Specific Features](#archive-specific-features)
4. [Tier 2 Text Features](#tier-2-text-features)
5. [EmailFeatureVector Schema](#emailfeaturevector-schema)
6. [Extraction from Canonical Email Metadata](#extraction-from-canonical-email-metadata)
7. [Provider Compatibility](#provider-compatibility)
8. [Feature Extraction Pipeline](#feature-extraction-pipeline)
9. [Schema Versioning](#schema-versioning)
10. [Performance Characteristics](#performance-characteristics)
11. [Phase 2+ Topic Signals](#phase-2-topic-signals)
12. [Edge Cases](#edge-cases)
13. [Feature Importance](#feature-importance)
14. [References](#references)

---

## Overview

### Purpose of Feature Extraction

**Feature extraction** transforms raw email data (`EmailFull` with canonical metadata) into structured, numeric `EmailFeatureVector` records suitable for machine learning. This is the critical bridge between email provider APIs and ML.NET classification models.

### Input and Output

**Input**: `CanonicalEmailMetadata` containing:
- Email headers, body text, body HTML
- Canonical folder placement (`inbox`, `archive`, `trash`, `spam`, etc.)
- Canonical flags (`IsRead`, `IsFlagged`, `IsImportant`, `HasAttachment`)
- Contact signals, provider signals (SPF/DKIM/DMARC)
- User rules (whitelist/blacklist)

**Output**: `EmailFeatureVector` with 40+ numeric/categorical features:
- **Structured features** (tabular): sender domain, authentication, time patterns, folder placement
- **Text features**: TF-IDF vectors for subject and body snippets
- **Archive-specific features**: email age, sender frequency, folder signals
- **Phase 2+ topic features** (nullable): topic clusters, sender categories, semantic embeddings

### Feature Engineering Philosophy

1. **Provider-agnostic**: All features derived from canonical fields, not provider-specific APIs
2. **Privacy-preserving**: Features are statistical/structural, not raw email content
3. **Fast extraction**: <5ms for structured features, <50ms including text features
4. **Versioned schema**: `FeatureSchemaVersion` tracks breaking changes, requiring regeneration

---

## Tier 1 Structured Features

### Overview

Structured features are **low-cost, high-signal** tabular attributes derived from email metadata. These features alone achieve ~70-85% classification accuracy without any text processing.

### Complete Feature Enumeration (40+ Fields)

#### 1. Sender Identity Features

| Feature | Type | Source | Extraction Logic | Example Values |
|---------|------|--------|------------------|----------------|
| **SenderDomain** | string | Headers["From"] | Parse email → extract domain | `"github.com"`, `"marketing.shopify.com"`, `"unknown"` |
| **SenderKnown** | bool | ContactSignal.Known | Direct map | `true` if sender in contacts |
| **ContactStrength** | int | ContactSignal.Strength | Enum → int (0=None, 1=Weak, 2=Strong) | `0`, `1`, `2` |
| **SenderFrequency** | int | Corpus-wide count | Count emails from this sender domain in entire corpus | `1`, `47`, `532` |

#### 2. Authentication Features

| Feature | Type | Source | Extraction Logic | Example Values |
|---------|------|--------|------------------|----------------|
| **SpfResult** | string | ProviderSignals.Spf | Map to canonical value | `"pass"`, `"fail"`, `"neutral"`, `"none"` |
| **DkimResult** | string | ProviderSignals.Dkim | Map to canonical value | `"pass"`, `"fail"`, `"neutral"`, `"none"` |
| **DmarcResult** | string | ProviderSignals.Dmarc | Map to canonical value | `"pass"`, `"fail"`, `"neutral"`, `"none"` |

**Signal**: Legitimate emails typically have `"pass"` for all three; spam often has `"fail"` or `"none"`.

#### 3. Email Metadata Features

| Feature | Type | Source | Extraction Logic | Example Values |
|---------|------|--------|------------------|----------------|
| **HasListUnsubscribe** | bool | ProviderSignals.HasListUnsubscribe | Direct map | `true` if List-Unsubscribe header present |
| **HasAttachments** | bool | EmailSummary.HasAttachments | Direct map | `true` if email has attachments |
| **EmailSizeLog** | float | SizeEstimate | `log10(SizeEstimate)` to normalize large sizes | `3.5` (for ~3KB), `5.2` (for ~150KB) |
| **SubjectLength** | int | Subject | Character count | `0` (empty), `25`, `142` |
| **RecipientCount** | int | Headers["To"] + Headers["Cc"] | Parse + count addresses | `1`, `5`, `47` |
| **IsReply** | bool | Subject | Starts with "Re:" or "RE:" | `true` if reply |
| **LabelCount** | int | FolderTags | Count of folders/tags assigned | `1` (Gmail: 1 label), `3` (Gmail: multiple labels) |

**Signal**: Bulk emails (newsletters) often have ListUnsubscribe, large RecipientCount, and no Re: prefix.

#### 4. Time Pattern Features

| Feature | Type | Source | Extraction Logic | Example Values |
|---------|------|--------|------------------|----------------|
| **HourReceived** | int | ReceivedDate | Extract hour (0-23) | `8` (8 AM), `14` (2 PM), `22` (10 PM) |
| **DayOfWeek** | int | ReceivedDate | Day of week (0=Sunday, 6=Saturday) | `0` (Sunday), `3` (Wednesday) |

**Signal**: Marketing emails cluster at specific send times (often early morning or lunch hour); personal emails spread throughout day.

#### 5. User Rule Features

| Feature | Type | Source | Extraction Logic | Example Values |
|---------|------|--------|------------------|----------------|
| **InUserWhitelist** | bool | UserRules.AlwaysKeep | Match sender or domain against AlwaysKeep rules | `true` if whitelisted |
| **InUserBlacklist** | bool | UserRules.AutoTrash | Match sender or domain against AutoTrash rules | `true` if blacklisted |

**Signal**: Strongest possible signals — user explicitly stated keep/delete intent.

---

## Archive-Specific Features

### Purpose

These features are **critical for archive reclamation** — the primary use case. They capture signals about email age, folder placement, and user interaction history.

### Archive Feature Enumeration

| Feature | Type | Source | Extraction Logic | Signal Strength |
|---------|------|--------|------------------|-----------------|
| **EmailAgeDays** | int | `DateTime.Now - ReceivedDate` | Days since email was received | **High** — older archived emails more deletable |
| **IsInInbox** | bool | `SourceFolder == "inbox"` | Email is in Inbox folder | **Critical** — strong keep signal |
| **IsStarred** | bool | Canonical `IsFlagged` | Starred/flagged by user | **Critical** — strong keep signal |
| **IsImportant** | bool | Canonical `IsImportant` | Marked important/high-priority | **High** — keep signal |
| **WasInTrash** | bool | `SourceFolder == "trash"` | Source folder is Trash | **Critical** — strong delete signal |
| **WasInSpam** | bool | `SourceFolder == "spam"` | Source folder is Spam/Junk | **Critical** — strong delete signal |
| **IsArchived** | bool | `SourceFolder == "archive"` | Not in Inbox/Trash/Spam | **High** — primary triage target |
| **ThreadMessageCount** | int | Count messages in `ThreadId` | Number of messages in thread | **Medium** — long threads may be valuable |
| **SenderFrequency** | int | Corpus-wide sender count | Total emails from sender domain | **High** — bulk senders cluster well |

### Archive Triage Logic

**Training Data Bootstrapping**:
```csharp
// Cold start: Use existing folder placement as training labels
if (WasInTrash || WasInSpam)
{
    trainingLabel = "delete";  // User already trashed/marked spam
}
else if (IsStarred || IsImportant || IsInInbox)
{
    trainingLabel = "keep";    // User explicitly kept or flagged
}
else if (IsArchived)
{
    trainingLabel = null;      // TRIAGE TARGET — needs classification
}
```

**Deletion Confidence Scoring**:
```csharp
// Higher age + higher sender frequency + no keep signals = higher deletion confidence
if (EmailAgeDays > 365 && SenderFrequency > 50 && !IsStarred && !IsImportant)
{
    deletionConfidence = 0.85;  // Likely safe to delete
}
else if (IsStarred || IsImportant || IsInInbox)
{
    deletionConfidence = 0.05;  // Definitely keep
}
```

---

## Tier 2 Text Features

### Overview

Text features extract signals from subject and body content using **TF-IDF** (Term Frequency-Inverse Document Frequency) vectorization. These features are **higher-cost** (~45ms additional processing) but provide **medium-signal** improvement (~10-15% accuracy gain over structured features alone).

### Text Feature Enumeration

| Feature | Type | Source | Extraction Method | Cost |
|---------|------|--------|-------------------|------|
| **SubjectText** | string (nullable) | Subject | Raw subject for TF-IDF | Low (already in memory) |
| **BodyTextShort** | string (nullable) | BodyText or BodyHtml | First 500 chars for TF-IDF | Medium (may need HTML stripping) |
| **LinkCount** | int | BodyHtml | Count `<a>` tags in HTML | Low (regex) |
| **ImageCount** | int | BodyHtml | Count `<img>` tags in HTML | Low (regex) |
| **HasTrackingPixel** | bool | BodyHtml | Detect 1x1 images (tracking pixels) | Low (regex + size check) |
| **UnsubscribeLinkInBody** | bool | BodyHtml or BodyText | Pattern match for unsubscribe links | Low (regex) |

### TF-IDF Vectorization in ML.NET

ML.NET's `FeaturizeText` transform handles TF-IDF automatically:

```csharp
var pipeline = mlContext.Transforms.Text.FeaturizeText(
    outputColumnName: "SubjectFeatures",
    inputColumnName: nameof(EmailFeatureVector.SubjectText))
.Append(mlContext.Transforms.Text.FeaturizeText(
    outputColumnName: "BodyFeatures",
    inputColumnName: nameof(EmailFeatureVector.BodyTextShort)))
.Append(mlContext.Transforms.Concatenate(
    "Features",
    "SubjectFeatures", "BodyFeatures", "SenderDomain" /* ... other features */));
```

**Parameters**:
- **N-grams**: Unigrams + bigrams (e.g., "meeting", "meeting tomorrow")
- **Max features**: Top 1000 TF-IDF features per field
- **Stop words**: English stop words removed ("the", "a", "is")

### Text Feature Extraction Logic

```csharp
public string? ExtractBodyTextShort(string? bodyText, string? bodyHtml)
{
    // Prefer plain text if available
    if (!string.IsNullOrWhiteSpace(bodyText))
    {
        return bodyText.Length > 500 ? bodyText[..500] : bodyText;
    }

    // Fall back to HTML → plain text conversion
    if (!string.IsNullOrWhiteSpace(bodyHtml))
    {
        var plainText = HtmlToPlainText(bodyHtml);
        return plainText.Length > 500 ? plainText[..500] : plainText;
    }

    return null;  // No body content
}

public bool HasTrackingPixel(string? bodyHtml)
{
    if (string.IsNullOrWhiteSpace(bodyHtml)) return false;

    // Regex: <img ... width="1" height="1" /> or similar
    var matches = Regex.Matches(bodyHtml, @"<img[^>]*(?:width|height)=[""']?1[""']?[^>]*>");
    return matches.Any();
}
```

### Text Feature Signals

| Feature | High Signal Patterns | Low Signal / Noise |
|---------|---------------------|-------------------|
| **SubjectText** | Keywords: "invoice", "meeting", "urgent", "newsletter", "unsubscribe" | Generic words: "hi", "update", "info" |
| **BodyTextShort** | Repeated patterns across sender domain (e.g., all Shopify emails mention "View order") | Unique conversational text |
| **LinkCount** | High link count (>5) → newsletters, low (0-2) → personal emails | Moderate count (3-4) is ambiguous |
| **HasTrackingPixel** | Strong newsletter/marketing signal | Rare in personal emails |
| **UnsubscribeLinkInBody** | Strong bulk email signal | Absent in personal emails |

---

## EmailFeatureVector Schema

### Complete Field List with Types and Nullability

```csharp
namespace TrashMailPanda.Shared;

/// <summary>
/// Complete feature vector for email classification.
/// All features are provider-agnostic.
/// </summary>
public class EmailFeatureVector
{
    // ============================================================
    // IDENTITY
    // ============================================================
    public string EmailId { get; init; } = string.Empty;

    // ============================================================
    // TIER 1: STRUCTURED FEATURES
    // ============================================================

    // Sender Identity
    public string SenderDomain { get; init; } = "unknown";
    public bool SenderKnown { get; init; }
    public int ContactStrength { get; init; }  // 0=None, 1=Weak, 2=Strong
    public int SenderFrequency { get; init; } = 1;  // Emails from this sender in corpus

    // Authentication
    public string SpfResult { get; init; } = "none";  // pass, fail, neutral, none
    public string DkimResult { get; init; } = "none";
    public string DmarcResult { get; init; } = "none";

    // Email Metadata
    public bool HasListUnsubscribe { get; init; }
    public bool HasAttachments { get; init; }
    public float EmailSizeLog { get; init; }  // log10(SizeEstimate)
    public int SubjectLength { get; init; }
    public int RecipientCount { get; init; } = 1;
    public bool IsReply { get; init; }
    public int LabelCount { get; init; }

    // Time Patterns
    public int HourReceived { get; init; }  // 0-23
    public int DayOfWeek { get; init; }     // 0=Sunday, 6=Saturday

    // User Rules
    public bool InUserWhitelist { get; init; }
    public bool InUserBlacklist { get; init; }

    // ============================================================
    // ARCHIVE-SPECIFIC FEATURES
    // ============================================================
    public int EmailAgeDays { get; init; }
    public bool IsInInbox { get; init; }
    public bool IsStarred { get; init; }
    public bool IsImportant { get; init; }
    public bool WasInTrash { get; init; }
    public bool WasInSpam { get; init; }
    public bool IsArchived { get; init; }
    public int ThreadMessageCount { get; init; } = 1;

    // ============================================================
    // TIER 2: TEXT FEATURES
    // ============================================================
    public string? SubjectText { get; init; }      // Raw subject for TF-IDF
    public string? BodyTextShort { get; init; }    // First 500 chars for TF-IDF
    public int LinkCount { get; init; }
    public int ImageCount { get; init; }
    public bool HasTrackingPixel { get; init; }
    public bool UnsubscribeLinkInBody { get; init; }

    // ============================================================
    // PHASE 2+ TOPIC FEATURES (nullable, not populated in Phase 1)
    // ============================================================
    public int? TopicClusterId { get; init; }            // LDA topic cluster ID
    public string? TopicDistributionJson { get; init; }  // Topic probability vector
    public string? SenderCategory { get; init; }         // e.g., "Development", "E-commerce"
    public string? SemanticEmbeddingJson { get; init; }  // ONNX embedding vector

    // ============================================================
    // METADATA
    // ============================================================
    public int FeatureSchemaVersion { get; init; } = 1;
    public DateTime ExtractedAt { get; init; }
}
```

### Feature Count by Category

| Category | Count | Nullable? |
|----------|-------|-----------|
| **Structured features** | 23 | No |
| **Archive-specific features** | 9 | No |
| **Text features** | 6 | SubjectText, BodyTextShort nullable |
| **Phase 2+ topic features** | 4 | All nullable |
| **Metadata** | 2 | No |
| **Total** | **44 fields** | — |

### Default Values

| Field | Default | Rationale |
|-------|---------|-----------|
| `SenderDomain` | `"unknown"` | Missing From header |
| `SenderFrequency` | `1` | First email from sender |
| `SpfResult`, `DkimResult`, `DmarcResult` | `"none"` | No authentication headers |
| `RecipientCount` | `1` | At least one recipient (the user) |
| `ThreadMessageCount` | `1` | Non-threading providers treat each email as single-message thread |
| `EmailSizeLog` | `log10(SizeEstimate)` | Must be >= 0 |
| `FeatureSchemaVersion` | `1` | Current schema version |

---

## Extraction from Canonical Email Metadata

### CanonicalEmailMetadata → EmailFeatureVector Mapping

The `IFeatureExtractor` service transforms canonical email metadata into feature vectors:

```csharp
public interface IFeatureExtractor
{
    /// <summary>
    /// Extract feature vector from canonical email metadata.
    /// sourceFolder is a canonical folder name: "inbox", "archive", "trash", "spam", "sent".
    /// </summary>
    Result<EmailFeatureVector> Extract(
        EmailFull email,
        ContactSignal? contactSignal,
        ProviderSignals? providerSignals,
        UserRules userRules,
        string sourceFolder = "archive");
}
```

### Provider-Agnostic Extraction Logic

**Key Principle**: All extraction logic operates on **canonical fields** provided by the email provider adapter. No provider-specific APIs or identifiers appear in feature extraction code.

```csharp
public Result<EmailFeatureVector> Extract(
    EmailFull email,
    ContactSignal? contactSignal,
    ProviderSignals? providerSignals,
    UserRules userRules,
    string sourceFolder)
{
    // 1. Extract sender domain (provider-agnostic)
    var senderDomain = ParseSenderDomain(email.Headers.GetValueOrDefault("From", ""));

    // 2. Map canonical folder to boolean flags
    var isInInbox = sourceFolder == "inbox";
    var wasInTrash = sourceFolder == "trash";
    var wasInSpam = sourceFolder == "spam";
    var isArchived = !isInInbox && !wasInTrash && !wasInSpam && sourceFolder != "sent";

    // 3. Extract canonical flags (already mapped by provider adapter)
    var isStarred = email.CanonicalFlags.IsFlagged;
    var isImportant = email.CanonicalFlags.IsImportant;

    // 4. Compute time features
    var hourReceived = email.ReceivedDate.Hour;
    var dayOfWeek = (int)email.ReceivedDate.DayOfWeek;
    var emailAgeDays = (DateTime.UtcNow - email.ReceivedDate).Days;

    // 5. Compute user rule matches (provider-agnostic)
    var inWhitelist = userRules.AlwaysKeep.Any(r => MatchesRule(r, senderDomain, email));
    var inBlacklist = userRules.AutoTrash.Any(r => MatchesRule(r, senderDomain, email));

    // 6. Extract text features
    var subjectText = email.Subject;
    var bodyTextShort = ExtractBodyTextShort(email.BodyText, email.BodyHtml);
    var linkCount = CountLinks(email.BodyHtml);
    var hasTrackingPixel = DetectTrackingPixel(email.BodyHtml);

    // 7. Build feature vector
    return Result.Success(new EmailFeatureVector
    {
        EmailId = email.MessageId,
        SenderDomain = senderDomain,
        SenderKnown = contactSignal?.Known ?? false,
        ContactStrength = contactSignal?.Strength ?? 0,
        SpfResult = providerSignals?.Spf ?? "none",
        DkimResult = providerSignals?.Dkim ?? "none",
        DmarcResult = providerSignals?.Dmarc ?? "none",
        HasListUnsubscribe = providerSignals?.HasListUnsubscribe ?? false,
        HasAttachments = email.HasAttachments,
        EmailSizeLog = (float)Math.Log10(Math.Max(1, email.SizeEstimate)),
        SubjectLength = email.Subject?.Length ?? 0,
        RecipientCount = CountRecipients(email.Headers),
        IsReply = email.Subject?.StartsWith("Re:", StringComparison.OrdinalIgnoreCase) ?? false,
        InUserWhitelist = inWhitelist,
        InUserBlacklist = inBlacklist,
        LabelCount = email.FolderTags.Count,
        HourReceived = hourReceived,
        DayOfWeek = dayOfWeek,
        EmailAgeDays = emailAgeDays,
        IsInInbox = isInInbox,
        IsStarred = isStarred,
        IsImportant = isImportant,
        WasInTrash = wasInTrash,
        WasInSpam = wasInSpam,
        IsArchived = isArchived,
        ThreadMessageCount = email.ThreadId != null ? GetThreadCount(email.ThreadId) : 1,
        SenderFrequency = GetSenderFrequency(senderDomain),
        SubjectText = subjectText,
        BodyTextShort = bodyTextShort,
        LinkCount = linkCount,
        ImageCount = CountImages(email.BodyHtml),
        HasTrackingPixel = hasTrackingPixel,
        UnsubscribeLinkInBody = DetectUnsubscribeLink(email.BodyHtml, email.BodyText),
        // Phase 2+ topic features remain null
        TopicClusterId = null,
        TopicDistributionJson = null,
        SenderCategory = null,
        SemanticEmbeddingJson = null,
        FeatureSchemaVersion = 1,
        ExtractedAt = DateTime.UtcNow
    });
}
```

---

## Provider Compatibility

### How Feature Extraction Handles Provider Differences

#### Gmail (Multi-Label Model)

- **Canonical mapping**: Gmail labels → canonical folders/flags (done by `GmailEmailProvider`)
- **FolderTags**: Multiple labels map to `Tags[]` array
- **LabelCount**: Direct count of assigned labels
- **IsArchived**: `!labelIds.Contains("INBOX") && labelIds.Contains("All Mail")`

#### IMAP (Single-Folder Model)

- **Canonical mapping**: IMAP folders → canonical folders, IMAP flags → canonical flags
- **FolderTags**: Single folder name in canonical form
- **LabelCount**: Always 1 (email lives in one folder)
- **IsArchived**: `sourceFolder == "archive"` or custom folder detection

#### Outlook (Single-Folder + Categories)

- **Canonical mapping**: Outlook folders + categories → canonical folders/tags
- **FolderTags**: Folder name + category tags if present
- **LabelCount**: 1 (folder) + category count
- **IsArchived**: `sourceFolder == "archive"` or user-defined rule

### Threading Support Degradation

| Provider | ThreadId Support | ThreadMessageCount Fallback |
|----------|------------------|----------------------------|
| **Gmail** | ✅ Native `threadId` | Actual thread message count |
| **Outlook** | ✅ Native `conversationId` | Actual conversation count |
| **IMAP** | ❌ No threading | Defaults to `1` |
| **Yahoo** | ❌ No threading | Defaults to `1` |
| **iCloud** | ❌ No threading | Defaults to `1` |

**Impact**: ~2-5% accuracy loss without threading signals. Features gracefully degrade.

---

## Feature Extraction Pipeline

### IFeatureExtractor Interface

```csharp
namespace TrashMailPanda.Shared;

public interface IFeatureExtractor
{
    /// <summary>
    /// Extract feature vector from a full email with optional signals.
    /// sourceFolder uses canonical folder names (inbox, archive, trash, spam, sent).
    /// </summary>
    Result<EmailFeatureVector> Extract(
        EmailFull email,
        ContactSignal? contactSignal,
        ProviderSignals? providerSignals,
        UserRules userRules,
        string sourceFolder = "archive");

    /// <summary>
    /// Extract feature vectors from a batch of emails.
    /// Invalid emails produce Result.Failure entries (not thrown).
    /// </summary>
    Result<IReadOnlyList<EmailFeatureVector>> ExtractBatch(
        IReadOnlyList<EmailClassificationInput> emails,
        UserRules userRules);

    /// <summary>
    /// Current feature schema version for compatibility checks.
    /// </summary>
    int SchemaVersion { get; }
}
```

### Error Handling with Result Pattern

```csharp
// ✅ Correct usage
var extractResult = featureExtractor.Extract(email, contactSignal, providerSignals, userRules);

if (extractResult.IsSuccess)
{
    var features = extractResult.Value;
    _logger.LogDebug("Extracted {FeatureCount} features for email {EmailId}",
        features.GetType().GetProperties().Length, features.EmailId);
}
else
{
    _logger.LogWarning("Feature extraction failed for {EmailId}: {Error}",
        email.MessageId, extractResult.Error);
}

// ❌ Never do this — IFeatureExtractor doesn't throw
try
{
    var features = featureExtractor.Extract(email, contactSignal, providerSignals, userRules);
}
catch (Exception ex) // This will never be hit
{
    // ...
}
```

### Batch Extraction Optimization

```csharp
public Result<IReadOnlyList<EmailFeatureVector>> ExtractBatch(
    IReadOnlyList<EmailClassificationInput> emails,
    UserRules userRules)
{
    var features = new List<EmailFeatureVector>(emails.Count);
    var failures = new List<string>();

    // Pre-compute sender frequency map (avoid repeated DB queries)
    var senderFrequencyMap = BuildSenderFrequencyMap(emails);

    foreach (var email in emails)
    {
        var result = Extract(
            email.EmailFull,
            email.ContactSignal,
            email.ProviderSignals,
            userRules,
            email.SourceFolder);

        if (result.IsSuccess)
        {
            features.Add(result.Value);
        }
        else
        {
            failures.Add($"{email.EmailFull.MessageId}: {result.Error}");
        }
    }

    if (failures.Any())
    {
        _logger.LogWarning("Batch extraction: {SuccessCount}/{TotalCount} succeeded, {FailureCount} failed",
            features.Count, emails.Count, failures.Count);
    }

    return Result.Success<IReadOnlyList<EmailFeatureVector>>(features);
}
```

---

## Schema Versioning

### Feature Schema Version Management

**FeatureSchemaVersion** tracks breaking changes to feature extraction logic or the `EmailFeatureVector` schema. When the schema changes, all existing `email_features` rows with old versions must be regenerated from `email_archive` data.

### When to Increment SchemaVersion

| Change Type | Increment? | Example |
|-------------|-----------|---------|
| **Add new optional feature** | ❌ No | Adding Phase 2 topic features (nullable fields) |
| **Change feature computation** | ✅ Yes | Changing `EmailSizeLog` from linear to log10 scale |
| **Remove feature** | ✅ Yes | Deprecating a feature entirely |
| **Rename feature** | ✅ Yes | `SenderDomain` → `SenderDomainName` |
| **Change feature type** | ✅ Yes | `SenderFrequency` from `int` to `float` |
| **Add new required feature** | ✅ Yes | Adding a non-nullable field like `SenderReputation` |

### Compatibility Workflow

```
┌──────────────────────────────────────────────────────────┐
│ 1. DETECT SCHEMA MISMATCH                                │
│    Model trained on SchemaVersion=2                      │
│    Feature vector has SchemaVersion=1                    │
└────────────┬─────────────────────────────────────────────┘
             │
┌────────────▼─────────────────────────────────────────────┐
│ 2. INVALIDATE OLD FEATURES                               │
│    Mark all email_features rows with SchemaVersion=1      │
│    as invalid (set extracted_at to NULL or delete)       │
└────────────┬─────────────────────────────────────────────┘
             │
┌────────────▼─────────────────────────────────────────────┐
│ 3. REGENERATE FROM ARCHIVE                               │
│    For each email in email_archive:                       │
│    • IFeatureExtractor.Extract() with new logic          │
│    • Save with SchemaVersion=2                           │
└────────────┬─────────────────────────────────────────────┘
             │
┌────────────▼─────────────────────────────────────────────┐
│ 4. RETRAIN MODEL                                         │
│    Model now compatible with new feature schema         │
│    IModelTrainer.TrainAsync(trigger="schema_change")     │
└──────────────────────────────────────────────────────────┘
```

### Schema Version Checks in Code

```csharp
public async Task<Result<ClassifyOutput>> ClassifyAsync(ClassifyInput input)
{
    var activeModel = await GetActiveModelAsync();
    var featureSchemaVersion = _featureExtractor.SchemaVersion;

    if (activeModel.FeatureSchemaVersion != featureSchemaVersion)
    {
        return Result.Failure<ClassifyOutput>(
            new ValidationError($"Model schema version mismatch: model expects v{activeModel.FeatureSchemaVersion}, current extractor is v{featureSchemaVersion}. Regeneration required."));
    }

    // Proceed with classification...
}
```

---

## Performance Characteristics

### Extraction Performance Targets

| Operation | Target Latency | Notes |
|-----------|---------------|-------|
| **Structured features only** | <5ms per email | Tabular features: sender, auth, time, folder |
| **Full features (with text)** | <50ms per email | Including TF-IDF vectorization |
| **Batch extraction (1,000 emails)** | <30 seconds | Parallel processing with pre-computed frequency map |

### Performance Optimization Strategies

#### 1. Sender Frequency Caching

**Problem**: Querying `email_archive` for sender frequency on every extraction is slow.

**Solution**: Pre-compute sender frequency map for batch operations:

```csharp
private Dictionary<string, int> BuildSenderFrequencyMap(IReadOnlyList<EmailClassificationInput> emails)
{
    var senderDomains = emails.Select(e => ParseSenderDomain(e.EmailFull.Headers["From"])).Distinct();
    var frequencyMap = new Dictionary<string, int>();

    foreach (var domain in senderDomains)
    {
        var count = _dbContext.EmailArchive
            .Where(e => e.HeadersJson.Contains($"\"From\":\"{domain}\""))
            .Count();
        frequencyMap[domain] = count;
    }

    return frequencyMap;
}
```

**Speedup**: Reduces 1,000 individual queries to ~50 batch queries (one per unique sender domain).

#### 2. Parallel Batch Extraction

```csharp
public Result<IReadOnlyList<EmailFeatureVector>> ExtractBatch(
    IReadOnlyList<EmailClassificationInput> emails,
    UserRules userRules)
{
    var frequencyMap = BuildSenderFrequencyMap(emails);

    var features = emails.AsParallel()
        .WithDegreeOfParallelism(Environment.ProcessorCount)
        .Select(email =>
        {
            var result = Extract(email.EmailFull, email.ContactSignal, email.ProviderSignals, userRules, email.SourceFolder);
            return result.IsSuccess ? result.Value : null;
        })
        .Where(f => f != null)
        .ToList();

    return Result.Success<IReadOnlyList<EmailFeatureVector>>(features!);
}
```

**Speedup**: ~4x on 4-core CPU for text feature extraction.

#### 3. TF-IDF Caching (ML.NET Native)

ML.NET's `FeaturizeText` transform caches vocabulary internally. No manual caching needed.

---

## Phase 2+ Topic Signals

### Reserved Schema Fields

The `EmailFeatureVector` schema reserves four nullable fields for **future topic-based signals** (Phase 2+). These fields are **not populated in Phase 1** but are included in the schema now to avoid breaking changes later.

| Field | Type | Phase | Description |
|-------|------|-------|-------------|
| **TopicClusterId** | `int?` | Phase 2 | LDA topic cluster ID (0-19 for 20-cluster model) |
| **TopicDistributionJson** | `string?` | Phase 2 | JSON array of topic probabilities: `[0.6, 0.2, 0.1, ...]` |
| **SenderCategory** | `string?` | Phase 2 | Sender domain category: `"Development"`, `"E-commerce"`, `"Social"` |
| **SemanticEmbeddingJson** | `string?` | Phase 3 | Dense embedding vector from local ONNX model: `[0.42, -0.13, ...]` |

### Phase 2: Sender Categories + LDA (No LLM)

**Sender Domain Categorization**:
```csharp
private static Dictionary<string, string> SenderCategoryMap = new()
{
    ["github.com"] = "Development",
    ["linkedin.com"] = "Professional",
    ["shopify.com"] = "E-commerce",
    ["medium.com"] = "Tech News",
    ["netflix.com"] = "Entertainment",
    // Seed from public lists + user curation
};

public string? GetSenderCategory(string senderDomain)
{
    return SenderCategoryMap.GetValueOrDefault(senderDomain);
}
```

**LDA Topic Modeling** (after 500+ emails in corpus):
```csharp
// Use ML.NET's LDA transform (or external library)
var ldaModel = TrainLdaModel(emailCorpus, numTopics: 20);

public string GetTopicDistributionJson(string subjectAndBody)
{
    var topicProbabilities = ldaModel.Predict(subjectAndBody);  // [0.6, 0.2, 0.1, ...]
    return JsonSerializer.Serialize(topicProbabilities);
}
```

### Phase 3: Local ONNX Embeddings (No LLM)

**Sentence Embedding Model**:
- Model: `all-MiniLM-L6-v2` (~80MB)
- ONNX Runtime for .NET (NuGet package)
- Input: Subject + Body snippet → Output: 384-dimensional dense vector

```csharp
public string GetSemanticEmbeddingJson(string text)
{
    var embedding = _onnxModel.Predict(text);  // float[384]
    return JsonSerializer.Serialize(embedding);
}
```

### Phase 4: Optional LLM Enrichment

**LLM-based Topic Extraction** (user opt-in required):
```csharp
if (_llmProvider != null && userConfig.EnableLlmEnrichment)
{
    var topics = await _llmProvider.ExtractTopicsAsync(email.Subject, email.BodyText);
    // Use topics to enhance SenderCategory or create custom fields
}
```

**Constraint**: Must **never be required** — Phases 1-3 provide full functionality without LLM.

---

## Edge Cases

### Email-Specific Edge Cases

| Scenario | Feature Extraction Behavior | Impact |
|----------|----------------------------|--------|
| **Email with null/empty Subject** | `SubjectLength = 0`, `IsReply = false`, `SubjectText = null` | Low (TF-IDF handles nulls) |
| **Email with null BodyText and BodyHtml** | `BodyTextShort = null`, text features default to 0 | Medium (15% signal loss) |
| **Missing From header** | `SenderDomain = "unknown"`, `SenderKnown = false` | Medium (sender features invalid) |
| **Empty From header** | `SenderDomain = "unknown"` | Medium |
| **Invalid ReceivedDate** | Use current timestamp as fallback, `EmailAgeDays = 0` | Low (rare) |
| **No authentication headers** | `SpfResult = "none"`, `DkimResult = "none"`, `DmarcResult = "none"` | Low (common for personal domains) |
| **Email in multiple folders** (Gmail) | `LabelCount > 1`, `SourceFolder` = primary canonical folder | Low (handled correctly) |
| **Email with ThreadId but no thread metadata** | `ThreadMessageCount = 1` (fallback) | Low |
| **Extremely large email** (>10MB) | `EmailSizeLog` handles via log10 normalization | Low |

### Provider-Specific Edge Cases

| Scenario | Provider(s) | Handling |
|----------|------------|----------|
| **No threading support** | IMAP, Yahoo, iCloud | `ThreadId = null`, `ThreadMessageCount = 1` |
| **Multi-folder placement** | Gmail (labels) | `FolderTags[]` array, `LabelCount` reflects all labels |
| **Single-folder model** | IMAP, Outlook | `FolderTags[]` has one item, `LabelCount = 1` |
| **Missing SPF/DKIM headers** | All providers (personal domains) | Defaults to `"none"`, not treated as spam signal |
| **Provider-specific flags** | Outlook (Importance), Gmail (IMPORTANT label) | Mapped to canonical `IsImportant` flag |

### User Rule Edge Cases

| Scenario | Behavior |
|----------|----------|
| **Email matches both whitelist and blacklist** | Whitelist takes precedence (`InUserWhitelist = true`, `InUserBlacklist = true`) |
| **No rules defined** | `InUserWhitelist = false`, `InUserBlacklist = false` |
| **Rule uses wildcard** (e.g., `*@example.com`) | Standard wildcard matching applied |

---

## Feature Importance

### Signal Strength Analysis

Based on expected correlation with archive deletion decisions:

#### Critical Features (Strongest Signals)

| Feature | Importance | Rationale |
|---------|-----------|-----------|
| **WasInTrash** | ⭐⭐⭐⭐⭐ | User already deleted — strongest possible signal |
| **WasInSpam** | ⭐⭐⭐⭐⭐ | User already marked spam — strongest delete signal |
| **InUserWhitelist** | ⭐⭐⭐⭐⭐ | Explicit user intent to keep |
| **InUserBlacklist** | ⭐⭐⭐⭐⭐ | Explicit user intent to delete |
| **IsStarred** | ⭐⭐⭐⭐⭐ | User flagged as important — strong keep signal |
| **IsInInbox** | ⭐⭐⭐⭐ | User actively managing — keep signal |
| **IsImportant** | ⭐⭐⭐⭐ | High-priority marker — keep signal |

#### High-Importance Features

| Feature | Importance | Rationale |
|---------|-----------|-----------|
| **EmailAgeDays** | ⭐⭐⭐⭐ | Older archived emails more deletable |
| **SenderFrequency** | ⭐⭐⭐⭐ | Bulk senders (newsletters) cluster well for deletion |
| **HasListUnsubscribe** | ⭐⭐⭐⭐ | Strong newsletter/marketing indicator |
| **HasTrackingPixel** | ⭐⭐⭐⭐ | Marketing email indicator |
| **UnsubscribeLinkInBody** | ⭐⭐⭐⭐ | Bulk email indicator |
| **IsArchived** | ⭐⭐⭐ | Triage target (not Inbox) |
| **ThreadMessageCount** | ⭐⭐⭐ | Long threads may be valuable conversations |

#### Medium-Importance Features

| Feature | Importance | Rationale |
|---------|-----------|-----------|
| **SenderKnown** | ⭐⭐⭐ | Contacts more likely to be kept |
| **ContactStrength** | ⭐⭐⭐ | Strong contacts → keep, weak → deletable |
| **SpfResult**, **DkimResult**, **DmarcResult** | ⭐⭐⭐ | Authentication helps identify legitimate vs. spam |
| **EmailSizeLog** | ⭐⭐⭐ | Larger emails → more storage reclaim |
| **SubjectText** (TF-IDF) | ⭐⭐⭐ | Keywords like "newsletter", "unsubscribe" indicate bulk |
| **BodyTextShort** (TF-IDF) | ⭐⭐⭐ | Repeated patterns across sender domain |
| **LinkCount** | ⭐⭐ | High link count → newsletters |
| **RecipientCount** | ⭐⭐ | High count → bulk emails |

#### Low-Importance Features (Weak Signals)

| Feature | Importance | Rationale |
|---------|-----------|-----------|
| **HourReceived** | ⭐⭐ | Marketing emails cluster at send times, but weak signal for archive |
| **DayOfWeek** | ⭐⭐ | Minimal correlation with keep/delete |
| **IsReply** | ⭐⭐ | Replies may be valuable, but context-dependent |
| **SubjectLength** | ⭐ | Weak correlation |
| **LabelCount** | ⭐ | Gmail-specific feature, weak signal |
| **ImageCount** | ⭐ | Decorative images in newsletters, but noisy |
| **HasAttachments** | ⭐ | Ambiguous signal (attachments can be spam or valuable) |

### Feature Importance for Training vs. Inference

**Archive Bootstrapping** (cold start training):
- **Critical**: `WasInTrash`, `WasInSpam`, `IsStarred`, `IsInInbox`
- **High**: `EmailAgeDays`, `SenderFrequency`

**Incoming Email Classification** (steady-state):
- **Critical**: `InUserWhitelist`, `InUserBlacklist`
- **High**: `SenderKnown`, authentication results, `HasListUnsubscribe`

---

## References

### Related Documentation

| Document | Path | Purpose |
|----------|------|---------|
| **ML Architecture** | `docs/ML_ARCHITECTURE.md` | System architecture and component interactions |
| **Model Training Pipeline** | `docs/MODEL_TRAINING_PIPELINE.md` | Training workflow and model lifecycle |
| **Data Model** | `specs/054-ml-architecture-design/data-model.md` | Complete entity and SQL schema |
| **IFeatureExtractor Contract** | `specs/054-ml-architecture-design/contracts/IFeatureExtractor.md` | Feature extraction service contract |
| **Research** | `specs/054-ml-architecture-design/research.md` | Architecture decisions (R1-R10) |

### External Resources

- **ML.NET Text Featurization**: https://docs.microsoft.com/en-us/dotnet/api/microsoft.ml.transforms.text
- **TF-IDF Explanation**: https://en.wikipedia.org/wiki/Tf%E2%80%93idf
- **Email Authentication (SPF/DKIM/DMARC)**: https://dmarc.org/wiki/FAQ

---

**Document Status**: ✅ Complete  
**Last Review**: 2026-03-14  
**Next Review**: After implementation (Issue #56)
