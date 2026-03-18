# Quickstart: ML.NET Model Training Infrastructure

**Feature**: #59 — ML.NET Model Training Infrastructure  
**Developer Guide** | **Date**: 2026-03-17

---

## Overview

This guide covers how to use the ML.NET training pipeline to train, evaluate, version, and roll back the action classification model in TrashMail Panda.

The **action model** assigns one of Keep / Archive / Delete / Spam per email, with a confidence score.

> **Label suggestion** is handled by a separate future feature using an LLM mini model (e.g. `gpt-4o-mini`) — see GitHub issue #77.

## Prerequisites

- Feature #55 (ML Data Storage) implemented and `IEmailArchiveService` available via DI  
- At least 100 labeled `EmailFeatureVector` records stored (run `dotnet run` and let the app accumulate training data, or use the import command)
- `TrashMailPanda.Providers.ML` project added to the solution and registered in DI

---

## 1. Dependency Injection Setup

```csharp
// Program.cs
using TrashMailPanda.Providers.ML;

// Register ML model provider
services.AddSingleton<IMLModelProvider, MLModelProvider>();
services.AddTransient<IModelTrainingPipeline, ModelTrainingPipeline>();

// Configure the provider
services.AddOptions<MLModelProviderConfig>()
    .Configure(config =>
    {
        config.ModelDirectory = "data/models";
        config.MaxModelVersions = 5;
        config.MinTrainingSamples = 100;
        config.ActionConfidenceThreshold = 0.5f;
        config.ActionTrainerName = "SdcaMaximumEntropy"; // or "LightGbm"
        config.QualityFloor = 0.70f;
    })
    .ValidateDataAnnotations();

// Initialize on startup
var provider = services.GetRequiredService<IMLModelProvider>();
var initResult = await provider.InitializeAsync(config, CancellationToken.None);
if (!initResult.IsSuccess)
{
    AnsiConsole.MarkupLine($"[red]✗ ML provider init failed: {initResult.Error.Message}[/]");
}
```

---

## 2. Check Training Readiness

Before training, check whether enough labeled data is available:

```csharp
var pipeline = services.GetRequiredService<IModelTrainingPipeline>();

// Check action model readiness
var actionSummaryResult = await pipeline.GetActionTrainingDataSummaryAsync();
if (!actionSummaryResult.IsSuccess)
{
    AnsiConsole.MarkupLine($"[red]✗ {actionSummaryResult.Error.Message}[/]");
    return;
}

var summary = actionSummaryResult.Value;
if (!summary.HasSufficientData)
{
    AnsiConsole.MarkupLine(
        $"[yellow]⚠ Insufficient training data: {summary.AvailableCount} of {summary.RequiredCount} labeled emails available.[/]");
    AnsiConsole.MarkupLine(
        $"[cyan]→ {summary.RequiredCount - summary.AvailableCount} more labeled emails needed.[/]");
    return;
}

AnsiConsole.MarkupLine($"[green]✓ Ready to train with {summary.AvailableCount} labeled emails.[/]");
```

---

## 3. Train the Action Model

```csharp
var pipeline = services.GetRequiredService<IModelTrainingPipeline>();
var provider = services.GetRequiredService<IMLModelProvider>();

// Set up progress reporting
var progressHandler = new Progress<TrainingProgress>(p =>
{
    AnsiConsole.MarkupLine($"[cyan]→[/] {p.Phase} ({p.PercentComplete}%) — {p.Message}");
});

// Train with Spectre.Console status spinner
TrainingMetricsReport? report = null;
await AnsiConsole.Status()
    .Spinner(Spinner.Known.Dots)
    .StartAsync("[cyan]Training action model...[/]", async ctx =>
    {
        var trainResult = await pipeline.TrainActionModelAsync(
            new TrainingRequest(
                ValidationSplit: 0.2f,
                TriggerReason: "user_request"),
            progressHandler,
            CancellationToken.None);

        if (!trainResult.IsSuccess)
        {
            AnsiConsole.MarkupLine($"[red]✗ Training failed: {trainResult.Error.Message}[/]");
            return;
        }

        report = trainResult.Value;
    });

if (report is null) return;

// Display results
AnsiConsole.MarkupLine($"[green]✓ Action model trained — v{report.ModelId}[/]");
AnsiConsole.MarkupLine($"  Accuracy:   {report.Accuracy:P1}");
AnsiConsole.MarkupLine($"  Macro F1:   {report.MacroF1:P1}");
AnsiConsole.MarkupLine($"  Duration:   {report.TrainingDuration.TotalSeconds:F1}s");

// Display per-class metrics
foreach (var (className, metrics) in report.PerClassMetrics)
{
    AnsiConsole.MarkupLine(
        $"  {className,-10} P={metrics.Precision:P1}  R={metrics.Recall:P1}  F1={metrics.F1Score:P1}");
}

// Display quality advisory if triggered
if (report.IsQualityAdvisory)
{
    AnsiConsole.MarkupLine($"[yellow]⚠ {report.QualityAdvisoryMessage}[/]");
}

// Reload the provider to use the newly trained model
await provider.InitializeAsync(config, CancellationToken.None);
```

---

## 4. Classify Emails

### Single email classification

```csharp
var provider = services.GetRequiredService<IMLModelProvider>();

// Get action prediction for a single email
var actionResult = await provider.ClassifyActionAsync(featureVector);
if (!actionResult.IsSuccess)
{
    AnsiConsole.MarkupLine($"[red]✗ {actionResult.Error.Message}[/]");
    return;
}

var action = actionResult.Value;
AnsiConsole.MarkupLine(
    $"[cyan]→[/] Action: [bold]{action.PredictedLabel}[/] (confidence: {action.Confidence:P0})");
```

### Batch classification

```csharp
// Batch 100 emails: target <100ms
var batchResult = await provider.ClassifyActionBatchAsync(featureVectors);
if (!batchResult.IsSuccess)
{
    AnsiConsole.MarkupLine($"[red]✗ Batch classification failed: {batchResult.Error.Message}[/]");
    return;
}

var predictions = batchResult.Value;
var deleteCount = predictions.Count(p => p.PredictedLabel == "Delete");
var spamCount = predictions.Count(p => p.PredictedLabel == "Spam");
AnsiConsole.MarkupLine(
    $"[cyan]→[/] Batch complete: {deleteCount} delete, {spamCount} spam, " +
    $"{predictions.Count - deleteCount - spamCount} keep/archive");
```

---

## 5. View Model Versions

```csharp
var versionsResult = await provider.GetModelVersionsAsync("action");
if (!versionsResult.IsSuccess)
{
    AnsiConsole.MarkupLine($"[red]✗ {versionsResult.Error.Message}[/]");
    return;
}

var table = new Table()
    .AddColumn("Version")
    .AddColumn("Date")
    .AddColumn("Algorithm")
    .AddColumn("F1")
    .AddColumn("Samples")
    .AddColumn("Active");

foreach (var version in versionsResult.Value)
{
    table.AddRow(
        version.ModelId,
        version.TrainingDate.ToString("yyyy-MM-dd HH:mm"),
        version.Algorithm,
        $"{version.MacroF1:P1}",
        version.TrainingDataCount.ToString(),
        version.IsActive ? "[green]✓[/]" : "");
}

AnsiConsole.Write(table);
```

---

## 6. Roll Back to a Prior Version

```csharp
var rollbackResult = await provider.RollbackAsync(targetModelId: "act_v2_20260316120000");

if (!rollbackResult.IsSuccess)
{
    AnsiConsole.MarkupLine($"[red]✗ Rollback failed: {rollbackResult.Error.Message}[/]");
    return;
}

var restored = rollbackResult.Value;
AnsiConsole.MarkupLine(
    $"[green]✓ Rolled back to model {restored.ModelId} " +
    $"(trained {restored.TrainingDate:yyyy-MM-dd}, F1={restored.MacroF1:P1})[/]");
```

---

## 7. Incremental Update

After 50+ new user corrections have accumulated:

```csharp
var updateResult = await pipeline.IncrementalUpdateActionModelAsync(
    new IncrementalUpdateRequest(
        MinNewCorrections: 50,
        TriggerReason: "correction_threshold"),
    progressHandler,
    CancellationToken.None);

if (!updateResult.IsSuccess)
{
    // Includes the case where fewer than 50 new corrections exist
    AnsiConsole.MarkupLine($"[yellow]⚠ {updateResult.Error.Message}[/]");
    return;
}

AnsiConsole.MarkupLine($"[green]✓ Incremental update complete — {updateResult.Value.ModelId}[/]");
```

---

## 8. Key Behaviours Reference

| Scenario | Behaviour |
|----------|-----------|
| Fewer than 100 labeled emails | Training returns `Result.Failure(ValidationError)` with remaining count |
| All emails have the same action class | Training returns `Result.Failure(ValidationError)` — degenerate dataset |
| Feature schema version mismatch | Training blocked with diagnostic message; re-extraction required |
| MacroF1 < 0.70 | `IsQualityAdvisory = true` in report; model is activated but user is warned with `[yellow]⚠[/]` |
| Training cancelled mid-run | Temp file deleted; prior active model unchanged |
| No prior model for rollback | Returns `ValidationError`: "No prior version to restore" |
| More than 5 model versions exist | Oldest version(s) beyond retention limit auto-pruned after new version saved |

---

## 9. Performance Expectations

| Operation | Target | Conditions |
|-----------|--------|------------|
| Action model training | < 2 min | 500–10,000 labeled emails |
| Action model training | < 5 min | Up to 100,000 labeled emails |
| Single action classification | < 10 ms | After model loaded |
| Batch 100 emails | < 100 ms | After model loaded |
| Incremental update (50–200 new corrections) | < 30 s | Full retrain |
| Rollback to prior version | < 5 s | File is already on disk |

---

## 10. Troubleshooting

**"Insufficient training data" error**  
Use `GetActionTrainingDataSummaryAsync()` to check how many labeled emails are available. Run the app with Gmail connected and classify emails to accumulate training data.

**MacroF1 is below 0.70**  
- Check per-class sample counts in the metrics report — often a class with <50 examples drives poor recall
- Consider waiting for more user corrections before retraining
- Try `LightGbm` trainer via `TrainingRequest(TrainerNameOverride: "LightGbm")` for better handling of imbalanced classes

**Model not classifying new emails**  
After training completes, call `provider.InitializeAsync(config, ct)` to reload the newly saved model file. The provider caches the loaded model and does not watch for file system changes.
