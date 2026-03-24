# Quickstart: Archive-Then-Delete Training Labels (064)

**Phase 1 output** | **Branch**: `064-archive-then-delete-labels`

---

## What this feature does (in one paragraph)

Three existing triage labels — `Archive for 30d`, `Archive for 1y`, `Archive for 5y` — now
execute real retention behaviour. When a user (or bulk operation) applies a time-bounded label,
the system checks the email's current age: if the email has already passed its threshold, it is
deleted from Gmail instead of archived. Either way, the `training_label` stored for ML training
always reflects the content-based classification (e.g., `Archive for 30d`). A startup-prompted
retention scan (default: prompts if last run > 7 days ago, targets monthly cadence) also sweeps
the archive for emails that have since passed their threshold and deletes them.

---

## Key files to touch

| File | Change |
|---|---|
| `src/Shared/TrashMailPanda.Shared/Labels/LabelThresholds.cs` | **NEW** |
| `src/Shared/TrashMailPanda.Shared/RetentionSettings.cs` | **NEW** |
| `src/Shared/TrashMailPanda.Shared/ProcessingSettings.cs` | **MODIFY** — add `RetentionSettings Retention` property |
| `src/TrashMailPanda/TrashMailPanda/Services/IRetentionEnforcementService.cs` | **NEW** |
| `src/TrashMailPanda/TrashMailPanda/Services/RetentionEnforcementService.cs` | **NEW** |
| `src/TrashMailPanda/TrashMailPanda/Models/RetentionScanResult.cs` | **NEW** |
| `src/TrashMailPanda/TrashMailPanda/Models/RetentionEnforcementOptions.cs` | **NEW** |
| `src/TrashMailPanda/TrashMailPanda/Startup/RetentionStartupCheck.cs` | **NEW** |
| `src/TrashMailPanda/TrashMailPanda/Services/EmailTriageService.cs` | **MODIFY** |
| `src/TrashMailPanda/TrashMailPanda/Services/BulkOperationService.cs` | **MODIFY** |
| `src/TrashMailPanda/TrashMailPanda/Program.cs` (or host builder) | **MODIFY** — register service + startup check |

---

## Step 1 — Add `LabelThresholds` to Shared

```csharp
// src/Shared/TrashMailPanda.Shared/Labels/LabelThresholds.cs
namespace TrashMailPanda.Shared.Labels;

public static class LabelThresholds
{
    public const string Archive30d = "Archive for 30d";
    public const string Archive1y  = "Archive for 1y";
    public const string Archive5y  = "Archive for 5y";

    private static readonly IReadOnlyDictionary<string, int> _thresholds =
        new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase)
        {
            [Archive30d] = 30,
            [Archive1y]  = 365,
            [Archive5y]  = 1825,
        };

    public static IReadOnlySet<string> TimeBoundedLabels { get; } =
        new HashSet<string>(_thresholds.Keys, StringComparer.OrdinalIgnoreCase);

    public static bool TryGetThreshold(string label, out int thresholdDays)
        => _thresholds.TryGetValue(label, out thresholdDays);

    public static bool IsTimeBounded(string label)
        => TimeBoundedLabels.Contains(label);
}
```

---

## Step 2 — Fix action routing in `EmailTriageService`

Add `DateTime? receivedDateUtc` to `ApplyDecisionAsync` and thread it into
`ExecuteGmailActionAsync`:

```csharp
// EmailTriageService.cs — ExecuteGmailActionAsync (private helper)
private async Task<Result<bool>> ExecuteGmailActionAsync(
    string emailId,
    string action,
    DateTime? receivedDateUtc,          // <-- new parameter
    CancellationToken cancellationToken)
{
    // Time-bounded labels: check age at execution time
    if (LabelThresholds.TryGetThreshold(action, out var thresholdDays)
        && receivedDateUtc.HasValue)
    {
        var ageDays = (DateTime.UtcNow - receivedDateUtc.Value).TotalDays;
        return ageDays >= thresholdDays
            ? await ApplyDeleteAsync(emailId, cancellationToken)
            : await ApplyArchiveAsync(emailId, cancellationToken);
    }

    // If receivedDateUtc is missing, fall back to Archive (safe default)
    if (LabelThresholds.IsTimeBounded(action))
        return await ApplyArchiveAsync(emailId, cancellationToken);

    return action switch
    {
        "Keep"   => await ApplyKeepAsync(emailId, cancellationToken),
        "Archive"=> await ApplyArchiveAsync(emailId, cancellationToken),
        "Delete" => await ApplyDeleteAsync(emailId, cancellationToken),
        "Spam"   => await _emailProvider.ReportSpamAsync(emailId),
        _ => Result<bool>.Failure(new ValidationError($"Unknown triage action: '{action}'"))
    };
}
```

> **Important**: `training_label` is set by `SetTrainingLabelAsync(emailId, chosenAction, ...)` 
> in `ApplyDecisionAsync` — this call is unchanged. The physical Gmail action (Archive vs Delete)
> is transparent to the label storage step.

---

## Step 3 — Fix `BulkOperationService` (same pattern)

The feature vector is already in scope during the bulk loop. Pass
`vector.ReceivedDateUtc` to `ExecuteGmailActionAsync`:

```csharp
// BulkOperationService.cs — inside the foreach loop
var gmailResult = await ExecuteGmailActionAsync(
    emailId, action, vector.ReceivedDateUtc, cancellationToken);
```

Add the time-bounded routing identical to Step 2.

---

## Step 4 — Add `RetentionEnforcementService`

```csharp
// Key scan logic inside RunScanAsync
var featuresResult = await _archiveService.GetAllFeaturesAsync(cancellationToken: ct);
if (!featuresResult.IsSuccess) return Result<RetentionScanResult>.Failure(featuresResult.Error);

var candidates = featuresResult.Value
    .Where(f => f.IsArchived == 1
             && f.ReceivedDateUtc.HasValue
             && LabelThresholds.IsTimeBounded(f.TrainingLabel ?? string.Empty));

var toDelete = new List<string>();
var skipped  = 0;

foreach (var feature in candidates)
{
    LabelThresholds.TryGetThreshold(feature.TrainingLabel!, out var thresholdDays);
    var elapsed = (DateTime.UtcNow - feature.ReceivedDateUtc!.Value).TotalDays;
    if (elapsed >= thresholdDays)
        toDelete.Add(feature.EmailId);
    else
        skipped++;
}

// Delete in batches; accumulate failures
var failedIds = new List<string>();
foreach (var emailId in toDelete)
{
    var deleteResult = await _emailProvider.BatchModifyAsync(new BatchModifyRequest
    {
        EmailIds    = [emailId],
        AddLabelIds = ["TRASH"],
        RemoveLabelIds = ["INBOX"],
    });
    if (!deleteResult.IsSuccess)
    {
        _logger.LogWarning("Retention delete failed for {EmailId}: {Error}",
            emailId, deleteResult.Error.Message);
        failedIds.Add(emailId);
    }
}
// NEVER call SetTrainingLabelAsync — training_label is not touched

// Persist last run time (even on partial success)
await PersistLastScanTimeAsync(DateTime.UtcNow, ct);

return Result<RetentionScanResult>.Success(new RetentionScanResult(
    ScannedCount: candidates.Count(),
    DeletedCount: toDelete.Count - failedIds.Count,
    SkippedCount: skipped,
    FailedIds:    failedIds,
    RanAtUtc:     DateTime.UtcNow));
```

---

## Step 5 — Add `RetentionStartupCheck`

Hook into the startup sequence (after providers are healthy):

```csharp
// RetentionStartupCheck.cs
public async Task RunAsync(CancellationToken cancellationToken)
{
    var shouldPrompt = await _retentionService.ShouldPromptAsync(cancellationToken);
    if (!shouldPrompt.IsSuccess || !shouldPrompt.Value)
        return;

    var lastRun = await _retentionService.GetLastScanTimeAsync(cancellationToken);
    var daysAgo = lastRun.Value.HasValue
        ? (int)(DateTime.UtcNow - lastRun.Value.Value).TotalDays
        : (int?)null;

    var message = daysAgo.HasValue
        ? $"[yellow]⚠  Retention scan last ran {daysAgo} days ago.[/]"
        : "[yellow]⚠  Retention scan has never been run.[/]";

    AnsiConsole.MarkupLine(message);
    var confirm = AnsiConsole.Confirm(
        "[cyan]→[/] Scan archived emails and delete any past their retention window?",
        defaultValue: true);

    if (!confirm)
    {
        AnsiConsole.MarkupLine("[dim]  Skipped. Will prompt again next session.[/]");
        return;
    }

    await AnsiConsole.Status()
        .Spinner(Spinner.Known.Dots)
        .StartAsync("[cyan]Running retention scan...[/]", async _ =>
        {
            var result = await _retentionService.RunScanAsync(cancellationToken);
            if (result.IsSuccess)
            {
                var r = result.Value;
                AnsiConsole.MarkupLine(
                    $"[green]✓[/] Retention scan complete: {r.DeletedCount} deleted, " +
                    $"{r.SkippedCount} still within window" +
                    (r.HasFailures ? $", [red]{r.FailedIds.Count} failed[/]" : string.Empty));
            }
            else
            {
                AnsiConsole.MarkupLine($"[red]✗ Retention scan failed: {result.Error.Message}[/]");
            }
        });
}
```

---

## Step 6 — Register in DI

```csharp
// Program.cs / host builder
services.Configure<RetentionEnforcementOptions>(
    configuration.GetSection("RetentionEnforcement"));
services.AddSingleton<IRetentionEnforcementService, RetentionEnforcementService>();
// RetentionStartupCheck is newed up inside the startup sequence (not a hosted service)
```

---

## Acceptance test (manual)

1. Find or create an email in Gmail archive with a label of `Archive for 30d`.
2. Ensure its received date is > 30 days ago.
3. Run the app — at the startup prompt, confirm the scan.
4. Verify in Gmail that the email is now in Trash.
5. Verify in SQLite that `email_features.training_label` is still `Archive for 30d`.

---

## EmailAgeDays — when to use which value

| Use case | Value to use |
|---|---|
| ML training dataset | `email_age_days` as stored (= age at decision time) |
| Inference on a new email | `(DateTime.UtcNow - receivedDateUtc).Days` (fresh compute) |
| Action-execution threshold check | `(DateTime.UtcNow - receivedDateUtc).Days` (fresh compute) |
| Retention scan threshold check | `(DateTime.UtcNow - receivedDateUtc).Days` (fresh compute) |
