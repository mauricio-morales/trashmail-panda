# Quickstart: Backend Refactoring for UI Abstraction

**Feature**: 061-backend-ui-abstraction
**Date**: 2026-03-21

## What Changed

This feature introduces two new service contracts (`IClassificationService` and
`IApplicationOrchestrator`), a typed event system for application lifecycle events,
extracts the workflow loop from `Program.cs` into a testable orchestrator, and
conditionally excludes Avalonia from the default build.

## Using IClassificationService

### Single Classification

```csharp
// Inject via DI
public class MyService(IClassificationService classifier)
{
    public async Task ClassifyEmailAsync(EmailFeatureVector feature, CancellationToken ct)
    {
        var result = await classifier.ClassifySingleAsync(feature, ct);
        if (result.IsSuccess)
        {
            var cr = result.Value;
            // cr.PredictedAction  → "Keep", "Archive", "Delete", or "Spam"
            // cr.Confidence       → 0.0–1.0
            // cr.ReasoningSource  → ML or RuleBased
        }
    }
}
```

### Batch Classification

```csharp
var features = new List<EmailFeatureVector> { f1, f2, f3 };
var result = await classifier.ClassifyBatchAsync(features, ct);
if (result.IsSuccess)
{
    foreach (var cr in result.Value)
    {
        Console.WriteLine($"{cr.EmailId}: {cr.PredictedAction} ({cr.Confidence:P0}, {cr.ReasoningSource})");
    }
}
```

### Empty Input

```csharp
// Returns Success with empty list — no ML call, no error
var result = await classifier.ClassifyBatchAsync(Array.Empty<EmailFeatureVector>(), ct);
// result.IsSuccess == true
// result.Value.Count == 0
```

## Using IApplicationOrchestrator

### In Program.cs

```csharp
var host = CreateHostBuilder(args).Build();
var orchestrator = host.Services.GetRequiredService<IApplicationOrchestrator>();
var result = await orchestrator.RunAsync(cancellationToken);
return result.IsSuccess ? result.Value : 1;
```

### Subscribing to Events

```csharp
orchestrator.ApplicationEventRaised += (sender, args) =>
{
    switch (args.Event)
    {
        case ModeSelectionRequestedEvent modeEvent:
            // Render mode selection UI
            break;
        case BatchProgressEvent progress:
            // Update progress bar: progress.ProcessedCount / progress.TotalCount
            break;
        case ProviderStatusChangedEvent status:
            // Show provider health: status.ProviderName, status.IsHealthy
            break;
        case ApplicationWorkflowCompletedEvent completed:
            // Workflow done: completed.ExitCode, completed.Reason
            break;
    }
};
```

## DI Registration

All new services are registered in `ServiceCollectionExtensions.AddApplicationServices()`:

```csharp
// In AddApplicationServices()
services.AddSingleton<IClassificationService, ClassificationService>();
services.AddSingleton<IApplicationOrchestrator, ApplicationOrchestrator>();
services.AddSingleton<ConsoleEventRenderer>();
```

## Building Without Avalonia (Default)

```bash
# Default: console-only, no Avalonia
dotnet build
dotnet run --project src/TrashMailPanda

# With Avalonia (when you need the desktop UI)
dotnet build /p:EnableAvaloniaUI=true
```

## Testing

```csharp
// ClassificationService test — mock IMLModelProvider
var mockMl = new Mock<IMLModelProvider>();
mockMl.Setup(m => m.ClassifyActionAsync(It.IsAny<EmailFeatureVector>(), It.IsAny<CancellationToken>()))
      .ReturnsAsync(Result<ActionPrediction>.Success(new ActionPrediction
      {
          PredictedLabel = "Archive",
          Confidence = 0.92f,
          Score = new[] { 0.05f, 0.92f, 0.02f, 0.01f }
      }));

var service = new ClassificationService(mockMl.Object, logger);
var result = await service.ClassifySingleAsync(testFeature);
Assert.True(result.IsSuccess);
Assert.Equal("Archive", result.Value.PredictedAction);
Assert.Equal(ReasoningSource.ML, result.Value.ReasoningSource);
```

```csharp
// ApplicationOrchestrator test — verify event emission
var events = new List<ApplicationEvent>();
orchestrator.ApplicationEventRaised += (_, args) => events.Add(args.Event);
await orchestrator.RunAsync(ct);
Assert.Contains(events, e => e is ApplicationWorkflowCompletedEvent);
```

## Key Design Decisions

1. **IClassificationService wraps IMLModelProvider** — it does NOT replace IEmailTriageService (which handles the full triage workflow).
2. **ApplicationOrchestrator delegates to existing console services** — it coordinates, doesn't re-implement.
3. **Event pattern uses standard .NET EventHandler<T>** — no System.Reactive dependency.
4. **Avalonia build exclusion via MSBuild** — source files preserved, only compilation is conditional.
