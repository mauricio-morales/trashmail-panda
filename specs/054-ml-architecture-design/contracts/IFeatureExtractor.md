# Contract: IFeatureExtractor

**Feature**: #54 — ML Architecture Design  
**Date**: 2026-03-14  
**Type**: Service Interface

## Overview

`IFeatureExtractor` is responsible for transforming raw email data (`EmailFull`, `EmailSummary`, signals) into `EmailFeatureVector` records suitable for ML training and inference. This is an internal service, not a provider.

## Interface Definition

```csharp
namespace TrashMailPanda.Shared;

/// <summary>
/// Extracts feature vectors from raw email data for ML classification.
/// Transforms emails into structured + text features for training and inference.
/// </summary>
public interface IFeatureExtractor
{
    /// <summary>
    /// Extract feature vector from a full email with optional signals.
    /// </summary>
    Result<EmailFeatureVector> Extract(
        EmailFull email,
        ContactSignal? contactSignal,
        ProviderSignals? providerSignals,
        UserRules userRules);

    /// <summary>
    /// Extract feature vectors from a batch of emails.
    /// Invalid emails produce Result.Failure entries (not thrown).
    /// </summary>
    Result<IReadOnlyList<EmailFeatureVector>> ExtractBatch(
        IReadOnlyList<EmailClassificationInput> emails,
        UserRules userRules);

    /// <summary>
    /// Get the current feature schema version.
    /// Used to verify model-data compatibility.
    /// </summary>
    int SchemaVersion { get; }
}
```

## Behavior Contract

| Scenario | Expected Behavior |
|----------|-------------------|
| Email with all fields populated | Returns complete feature vector |
| Email with null BodyText and BodyHtml | SubjectText/BodyTextShort set to null; numeric features still computed |
| Email with missing headers | SenderDomain defaults to "unknown"; auth results default to "none" |
| Email with empty subject | SubjectLength = 0, IsReply = false, SubjectText = null |
| Batch with mixed valid/invalid emails | Returns partial success — valid vectors returned, invalid logged |

## Feature Schema Versioning

When the feature extraction logic changes (new features added, feature computation changed):
1. Increment `SchemaVersion`
2. Existing `email_features` rows with old version are invalidated
3. New features must be re-extracted from `email_archive` data
4. ML models trained on old schema cannot be used — retraining required
