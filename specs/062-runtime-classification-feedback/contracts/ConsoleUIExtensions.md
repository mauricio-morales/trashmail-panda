# Contract: Console UI Extensions

**Feature**: 062-runtime-classification-feedback  
**Layer**: `src/TrashMailPanda/TrashMailPanda/Services/Console/`  
**Existing Service**: `EmailTriageConsoleService`

## Triage Loop Changes

The existing triage loop in `EmailTriageConsoleService.RunAsync` needs the following integration points:

### 1. Auto-Apply Decision Point (per-email)

```
Current flow:
  for each email in batch:
    → GetAiRecommendationAsync(feature)
    → RenderEmailCard(feature, prediction)
    → WaitForKeystroke()
    → ApplyDecisionAsync(emailId, chosenAction, aiRec)

New flow:
  for each email in batch:
    → GetAiRecommendationAsync(feature)
    → IF autoApplyService.ShouldAutoApply(config, classification):
        → IF autoApplyService.IsActionRedundant(action, feature):
            → Store training label only (skip Gmail API)
        → ELSE:
            → ApplyDecisionAsync(emailId, action, aiRec)
        → autoApplyService.LogAutoApply(entry)
        → qualityMonitor.RecordDecision(predicted, actual, isOverride: false)
    → ELSE:
        → RenderEmailCard(feature, prediction)
        → WaitForKeystroke()
        → ApplyDecisionAsync(emailId, chosenAction, aiRec)
        → qualityMonitor.RecordDecision(predicted, chosenAction, isOverride)
```

### 2. Quality Warning Banner (per-batch)

At the start of each batch (before rendering the first email):
```
→ qualityMonitor.CheckForWarningAsync(autoApplyConfig)
→ IF warning != null:
    → RenderQualityWarning(warning)
    → IF warning.Severity == Critical:
        → autoApplyConfig.Enabled = false
        → autoApplyService.SaveConfigAsync(config)
```

### 3. Retrain Suggestion (between batches)

After the batch completes, if corrections ≥ threshold:
```
→ qualityMonitor.GetCorrectionsSinceLastTrainingAsync()
→ IF corrections >= 50:
    → RenderRetrainSuggestion(corrections, accuracy)
    → IF user presses 'T' (retrain):
        → modelTrainingPipeline.IncrementalUpdateActionModelAsync()
        → RenderTrainingResults(metrics)
```

### 4. Session Summary Extension

At session end, extend the existing summary with auto-apply stats:
```
→ autoApplyService.GetSessionSummary(manualCount)
→ RenderAutoApplySummary(summary)
```

### 5. Review/Undo Menu (new keystroke 'R')

New menu option accessible from the triage loop and session end:
```
→ User presses 'R' (review auto-applied)
→ autoApplyService.GetSessionLog()
→ RenderAutoApplyReviewTable(entries)
→ User selects entry and presses 'U' (undo)
→ autoApplyUndoService.UndoAsync(emailId, originalAction, correctedAction)
→ qualityMonitor.RecordDecision(predicted, corrected, isOverride: true)
```

### 6. Model Stats Display (new keystroke 'M')

New menu option to view model performance:
```
→ User presses 'M' (model stats)
→ qualityMonitor.GetMetricsAsync()
→ RenderModelQualityDashboard(metrics)
  → Overall accuracy, rolling accuracy
  → Per-action table: action, total, accepted, correction rate
  → Confusion summary: "Archive→Delete: 15 times"
```

## Spectre.Console Rendering Specs

### Quality Warning Banner
```csharp
// Critical (red panel)
AnsiConsole.MarkupLine("[bold red]⚠ MODEL QUALITY CRITICAL[/]");
AnsiConsole.MarkupLine($"[red]Rolling accuracy: {accuracy:P0} — auto-apply disabled[/]");
AnsiConsole.MarkupLine("[yellow]→ Recommend retraining before continuing[/]");

// Warning (yellow)
AnsiConsole.MarkupLine("[yellow]⚠ Model accuracy declining: {accuracy:P0}[/]");

// Info / retrain suggestion
AnsiConsole.MarkupLine($"[cyan]ℹ {corrections} corrections since last training[/]");
AnsiConsole.MarkupLine("[cyan]→ Press [bold]T[/] to retrain now[/]");
```

### Auto-Apply Review Table
```csharp
var table = new Table()
    .AddColumn("#")
    .AddColumn("Sender")
    .AddColumn("Subject")
    .AddColumn("Action")
    .AddColumn("Confidence")
    .AddColumn("Status");

// Each row:
// [green]✓ Auto-applied[/] or [yellow]↩ Undone → {correctedAction}[/]
```
