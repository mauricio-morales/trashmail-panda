# Quickstart: Runtime Classification with User Feedback Loop

**Feature**: 062-runtime-classification-feedback  
**Prerequisites**: specs 058 (Gmail training data), 059 (ML.NET pipeline), 060 (console TUI), 061 (backend abstraction) all implemented

## What This Feature Adds

Four new capabilities on top of the existing triage infrastructure:

1. **Auto-apply** — Emails classified with ≥95% confidence are actioned automatically
2. **Bootstrap Starred/Important** — Gmail Starred/Important emails seeded as "Keep" training data
3. **Quality monitoring** — Rolling accuracy tracking with proactive degradation warnings
4. **Review & undo** — Post-hoc review of auto-applied decisions with Gmail reversal

## Architecture Overview

```
                    ┌─────────────────────────┐
                    │  EmailTriageConsoleService│ (extended)
                    │   - auto-apply flow      │
                    │   - quality banners       │
                    │   - review/undo UI        │
                    └────────┬────────┬────────┘
                             │        │
              ┌──────────────┘        └──────────────┐
              ▼                                      ▼
    ┌──────────────────┐               ┌──────────────────────┐
    │ IAutoApplyService │               │ IModelQualityMonitor  │
    │  - threshold eval │               │  - rolling accuracy   │
    │  - redundancy chk │               │  - per-action metrics │
    │  - session log    │               │  - proactive warnings │
    └────────┬─────────┘               └──────────┬───────────┘
             │                                    │
             ▼                                    ▼
    ┌──────────────────┐               ┌──────────────────────┐
    │IAutoApplyUndoSvc  │               │   IEmailArchiveService│ (existing)
    │  - Gmail reversal │               │   - correction queries│
    │  - correction     │               │   - label aggregation │
    └──────────────────┘               └──────────────────────┘
```

## New Files to Create

### Services (4 files)

| File | Type | Purpose |
|------|------|---------|
| `IAutoApplyService.cs` | Interface | Auto-apply evaluation, session log, config persistence |
| `AutoApplyService.cs` | Implementation | Threshold logic, redundancy detection, in-memory log |
| `IModelQualityMonitor.cs` | Interface | Quality tracking, warnings, correction metrics |
| `ModelQualityMonitor.cs` | Implementation | Rolling window, DB aggregation, warning generation |
| `IAutoApplyUndoService.cs` | Interface | Undo auto-applied decisions |
| `AutoApplyUndoService.cs` | Implementation | Gmail reversal + correction storage |

### Models (6 files)

| File | Type | Purpose |
|------|------|---------|
| `AutoApplyConfig.cs` | Record | User settings: enabled, threshold |
| `AutoApplyLogEntry.cs` | Record | Ephemeral session log entry |
| `AutoApplySessionSummary.cs` | Record | Session aggregate stats |
| `ModelQualityMetrics.cs` | Record | Overall + per-action quality snapshot |
| `ActionCategoryMetrics.cs` | Record | Per-action accuracy/confusion data |
| `QualityWarning.cs` | Record | Proactive warning with severity + recommendation |

### Enums (1 file)

| File | Type | Purpose |
|------|------|---------|
| `QualityWarningSeverity.cs` | Enum | Info, Warning, Critical |

## Files to Modify

| File | Change |
|------|--------|
| `EmailTriageConsoleService.cs` | Add auto-apply decision point, quality banners, review/undo keystrokes |
| `EmailTriageSession.cs` | Add `AutoApplyLog`, `AutoAppliedCount`, `RollingDecisions` fields |
| `TriageSessionSummary.cs` | Add `AutoAppliedCount`, `ManuallyReviewedCount` fields |
| `ServiceCollectionExtensions.cs` | Register new services in DI |
| `GmailTrainingDataService.cs` | Add post-scan Starred/Important → Keep label inference |

## Key Integration Points

### Auto-Apply Flow (per email in AI-assisted mode)

```csharp
var classification = await _classificationService.ClassifySingleAsync(feature, ct);
var shouldAutoApply = _autoApplyService.ShouldAutoApply(config, classification.Value);

if (shouldAutoApply)
{
    var isRedundant = _autoApplyService.IsActionRedundant(
        classification.Value.PredictedAction, feature);

    if (!isRedundant)
    {
        // Execute Gmail action + store training label
        await _triageService.ApplyDecisionAsync(
            feature.EmailId, classification.Value.PredictedAction,
            classification.Value.PredictedAction, ct: ct);
    }
    else
    {
        // Skip Gmail, store training label only
        await _archiveService.SetTrainingLabelAsync(
            feature.EmailId, classification.Value.PredictedAction,
            userCorrected: false, ct);
    }

    _autoApplyService.LogAutoApply(new AutoApplyLogEntry { ... });
    _qualityMonitor.RecordDecision(
        classification.Value.PredictedAction,
        classification.Value.PredictedAction,
        isOverride: false);
}
else
{
    // Present to user for manual review (existing flow)
}
```

### Quality Check (per batch boundary)

```csharp
var warning = await _qualityMonitor.CheckForWarningAsync(autoApplyConfig, ct);
if (warning?.Value != null)
{
    RenderQualityWarning(warning.Value);
    if (warning.Value.AutoApplyDisabled)
    {
        autoApplyConfig.Enabled = false;
        await _autoApplyService.SaveConfigAsync(autoApplyConfig, ct);
    }
}
```

### Bootstrap Enhancement (in GmailTrainingDataService)

```csharp
// After RunInitialScanAsync completes, apply Starred/Important → Keep
await _archiveService.SetTrainingLabelForFlaggedAsync(
    "Keep", isStarred: true, isImportant: true);
// OR: simple SQL post-step within the service
```

## Testing Strategy

| Area | Test Type | Key Scenarios |
|------|-----------|---------------|
| AutoApplyService | Unit | Threshold boundary (94.9% → manual, 95.0% → auto), disabled config, redundancy detection |
| ModelQualityMonitor | Unit | Rolling window accuracy calc, warning thresholds (70%, 50%), per-action metrics |
| AutoApplyUndoService | Unit | Gmail reversal mapping, dual-write on undo, failure handling |
| EmailTriageConsoleService | Integration | Full triage loop with auto-apply, quality warnings, review/undo flow |
| Bootstrap Starred/Important | Integration | Verify labels set correctly, idempotency, archive exclusion |
