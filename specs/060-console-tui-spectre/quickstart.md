# Quickstart: Console TUI Feature #060

**Feature**: Console TUI — Email Triage, Color Scheme, Help System, Bulk Ops, Provider Settings  
**Date**: 2026-03-19

This guide shows developers how to implement, test, and integrate the new console TUI components.

---

## Prerequisites

- .NET 9.0 SDK
- Existing providers initialized (run `dotnet run --project src/TrashMailPanda` and complete setup)
- For running unit tests: no external dependencies required (mocks used)

---

## 1. Add ConsoleColors (First, Cross-Cutting)

`ConsoleColors` must be added before implementing any mode service. All new console output uses
these constants.

**Create** `src/TrashMailPanda/TrashMailPanda/Services/Console/ConsoleColors.cs`:

```csharp
// See data-model.md §8 for full class definition
public static class ConsoleColors
{
    public const string Error = "[bold red]";
    public const string ErrorText = "[red]";
    public const string Success = "[green]";
    public const string Warning = "[yellow]";
    public const string Info = "[blue]";
    public const string Metric = "[magenta]";
    public const string Highlight = "[cyan]";
    public const string ActionHint = "[cyan]";
    public const string Dim = "[dim]";
    public const string Close = "[/]";
    public const string AiRecommendation = "[bold cyan]";
    public const string PromptOption = "[bold yellow]";
}
```

**Verify** no raw markup strings exist outside this file:

```bash
grep -rn '\[green\]\|\[red\]\|\[cyan\]\|\[yellow\]\|\[magenta\]' \
  src/TrashMailPanda/TrashMailPanda/ \
  --include="*.cs" \
  | grep -v ConsoleColors.cs
# Expected: zero matches after migration
```

---

## 2. Add the Storage Migration

Add an EF Core migration to add `training_label` column to the existing `email_features` table:

```bash
cd src/Providers/Storage/TrashMailPanda.Providers.Storage
dotnet ef migrations add AddTrainingLabelToEmailFeatures \
  --context TrashMailPandaDbContext \
  --project ./TrashMailPanda.Providers.Storage.csproj
```

The migration `Up()` method should contain:

```csharp
migrationBuilder.AddColumn<string>(
    name: "training_label",
    table: "email_features",
    type: "TEXT",
    nullable: true,
    defaultValue: null);
```

**Verify**: After migration, all existing `email_features` rows have `training_label = NULL` —
correctly marking them as "untriaged" and eligible for the triage queue.

---

## 3. Implement SetTrainingLabelAsync on EmailArchiveService

**File**: `src/Providers/Storage/TrashMailPanda.Providers.Storage/EmailArchiveService.cs` (addition)

Add three new methods to the existing `EmailArchiveService` implementation:

```csharp
public async Task<Result<bool>> SetTrainingLabelAsync(
    string emailId,
    string label,
    bool userCorrected,
    CancellationToken ct = default)
{
    try
    {
        await _semaphore.WaitAsync(ct);
        try
        {
            using var cmd = _connection.CreateCommand();
            cmd.CommandText = userCorrected
                ? """
                   UPDATE email_features
                   SET training_label = @label, user_corrected = 1
                   WHERE email_id = @emailId
                   """
                : """
                   UPDATE email_features
                   SET training_label = @label
                   WHERE email_id = @emailId
                   """;
            cmd.Parameters.AddWithValue("@label", label);
            cmd.Parameters.AddWithValue("@emailId", emailId);
            var rows = await cmd.ExecuteNonQueryAsync(ct);
            return Result<bool>.Success(rows > 0);
        }
        finally { _semaphore.Release(); }
    }
    catch (Exception ex)
    {
        _logger.LogError(ex, "Failed to set training label for email {EmailId}", emailId);
        return Result<bool>.Failure(new StorageError($"Failed to set label: {ex.Message}"));
    }
}

public async Task<Result<int>> CountLabeledAsync(CancellationToken ct = default)
{
    try
    {
        await _semaphore.WaitAsync(ct);
        try
        {
            using var cmd = _connection.CreateCommand();
            cmd.CommandText = """
                SELECT COUNT(*) FROM email_features WHERE training_label IS NOT NULL
                """;
            var count = await cmd.ExecuteScalarAsync(ct);
            return Result<int>.Success(Convert.ToInt32(count));
        }
        finally { _semaphore.Release(); }
    }
    catch (Exception ex)
    {
        _logger.LogError(ex, "Failed to count labeled features");
        return Result<int>.Failure(new StorageError($"Failed to count: {ex.Message}"));
    }
}

public async Task<Result<IReadOnlyList<EmailFeatureVector>>> GetUntriagedAsync(
    int pageSize,
    int offset,
    CancellationToken ct = default)
{
    try
    {
        await _semaphore.WaitAsync(ct);
        try
        {
            using var cmd = _connection.CreateCommand();
            cmd.CommandText = """
                SELECT * FROM email_features
                WHERE training_label IS NULL
                ORDER BY extracted_at DESC
                LIMIT @pageSize OFFSET @offset
                """;
            cmd.Parameters.AddWithValue("@pageSize", pageSize);
            cmd.Parameters.AddWithValue("@offset", offset);
            // ... map reader to EmailFeatureVector list
            var results = await MapToFeatureVectorsAsync(cmd, ct);
            return Result<IReadOnlyList<EmailFeatureVector>>.Success(results);
        }
        finally { _semaphore.Release(); }
    }
    catch (Exception ex)
    {
        _logger.LogError(ex, "Failed to get untriaged features");
        return Result<IReadOnlyList<EmailFeatureVector>>.Failure(
            new StorageError($"Failed to get queue: {ex.Message}"));
    }
}
```

---

## 3.5. Implement EmailTriageService (Business Logic)

**File**: `src/TrashMailPanda/TrashMailPanda/Services/EmailTriageService.cs`

This is the UI-agnostic service. It depends on the provider layer directly. No `IAnsiConsole` reference.

### Constructor

```csharp
public sealed class EmailTriageService : IEmailTriageService
{
    private readonly IEmailProvider _emailProvider;
    private readonly IMLModelProvider _mlProvider;
    private readonly IEmailArchiveService _archiveService;
    private readonly MLModelProviderConfig _mlConfig;
    private readonly ILogger<EmailTriageService> _logger;

    public EmailTriageService(
        IEmailProvider emailProvider,
        IMLModelProvider mlProvider,
        IEmailArchiveService archiveService,
        IOptions<MLModelProviderConfig> mlConfig,
        ILogger<EmailTriageService> logger)
    {
        _emailProvider   = emailProvider;
        _mlProvider      = mlProvider;
        _archiveService  = archiveService;
        _mlConfig        = mlConfig.Value;
        _logger          = logger;
    }
```

### GetSessionInfoAsync

```csharp
public async Task<Result<TriageSessionInfo>> GetSessionInfoAsync(CancellationToken ct = default)
{
    var modelResult = await _mlProvider.GetActiveModelVersionAsync("action", ct);
    var mode = modelResult.IsSuccess ? TriageMode.AiAssisted : TriageMode.ColdStart;

    var countResult = await _archiveService.CountLabeledAsync(ct);
    var count = countResult.IsSuccess ? countResult.Value : 0;

    return Result<TriageSessionInfo>.Success(new TriageSessionInfo(
        mode,
        count,
        _mlConfig.MinTrainingSamples,
        ThresholdAlreadyReached: count >= _mlConfig.MinTrainingSamples
    ));
}
```

### ApplyDecisionAsync (dual-write)

```csharp
public async Task<Result<TriageDecision>> ApplyDecisionAsync(
    string emailId,
    string chosenAction,
    string? aiRecommendation,
    CancellationToken ct = default)
{
    // Step 1: Execute Gmail action FIRST.
    // On failure: return error — training_label is NOT stored (no false training signal).
    var actionResult = await ExecuteGmailActionAsync(emailId, chosenAction, ct);
    if (!actionResult.IsSuccess)
        return Result<TriageDecision>.Failure(actionResult.Error);

    // Step 2: Store training label only on Gmail success.
    var isOverride = aiRecommendation is not null && chosenAction != aiRecommendation;
    await _archiveService.SetTrainingLabelAsync(emailId, chosenAction, isOverride, ct);

    return Result<TriageDecision>.Success(
        new TriageDecision(emailId, chosenAction, isOverride, DateTimeOffset.UtcNow));
}
```

---

## 4. Implement EmailTriageConsoleService (TUI Presenter)

**File**: `src/TrashMailPanda/TrashMailPanda/Services/Console/EmailTriageConsoleService.cs`

This is the thin TUI presenter. It has **no** dependency on `IEmailProvider`, `IMLModelProvider`,
or `IEmailArchiveService`. All orchestration is delegated to `IEmailTriageService`.

### Constructor (dependencies)

```csharp
public sealed class EmailTriageConsoleService : IEmailTriageConsoleService
{
    private readonly IEmailTriageService _triageService;
    private readonly IConsoleHelpPanel _helpPanel;
    private readonly ILogger<EmailTriageConsoleService> _logger;
    private readonly IAnsiConsole _console;

    public EmailTriageConsoleService(
        IEmailTriageService triageService,
        IConsoleHelpPanel helpPanel,
        ILogger<EmailTriageConsoleService> logger,
        IAnsiConsole? console = null)
    {
        _triageService = triageService;
        _helpPanel     = helpPanel;
        _logger        = logger;
        _console       = console ?? AnsiConsole.Console;
    }
```
    private readonly IConsoleHelpPanel _helpPanel;
    private readonly ILogger<EmailTriageConsoleService> _logger;
    private readonly IAnsiConsole _console;

    // Constructor shown above in §3.5 — no direct provider injection
```

### RunAsync core flow (presentation only)

```csharp
public async Task<Result<TriageSessionSummary>> RunAsync(string accountId, CancellationToken ct = default)
{
    // 1. Retrieve session state from business logic layer (no provider calls here)
    var infoResult = await _triageService.GetSessionInfoAsync(ct);
    if (!infoResult.IsSuccess)
        return Result<TriageSessionSummary>.Failure(infoResult.Error);

    var info = infoResult.Value;
    var session = new EmailTriageSession
    {
        AccountId        = accountId,
        Mode             = info.Mode,
        LabeledCount     = info.LabeledCount,
        LabelingThreshold = info.LabelingThreshold
    };

    RenderSessionHeader(session);   // TUI rendering only

    // 2. Main rendering loop
    const int PageSize = 50;
    while (!ct.IsCancellationRequested)
    {
        // Fetch next page via business logic layer
        var pageResult = await _triageService.GetNextBatchAsync(PageSize, session.CurrentOffset, ct);
        if (!pageResult.IsSuccess)
        {
            _console.MarkupLine($"{ConsoleColors.Error}✗{ConsoleColors.Close} {ConsoleColors.ErrorText}{Markup.Escape(pageResult.Error.Message)}{ConsoleColors.Close}");
            break;
        }

        if (pageResult.Value.Count == 0)
        {
            ShowBatchComplete(session);
            break;
        }

        foreach (var feature in pageResult.Value)
        {
            // Rendering
            RenderEmailCard(feature, session);

            var predResult = await _triageService.GetAiRecommendationAsync(feature, ct);
            var prediction = predResult.IsSuccess ? predResult.Value : null;
            RenderAiRecommendation(prediction, session.Mode);  // null = "(No AI suggestions yet)"

            // Capture user keypress (TUI responsibility)
            var chosenAction = await CaptureKeypressAsync(feature, prediction, ct);
            if (chosenAction == null) goto ExitLoop;  // Q or Esc

            // Delegate execution entirely to business logic layer
            var decisionResult = await _triageService.ApplyDecisionAsync(
                feature.EmailId!, chosenAction, prediction?.PredictedAction, ct);

            if (!decisionResult.IsSuccess)
            {
                _console.MarkupLine($"{ConsoleColors.Error}✗{ConsoleColors.Close} {ConsoleColors.ErrorText}Action failed: {Markup.Escape(decisionResult.Error.Message)}{ConsoleColors.Close}");
                continue;  // Email stays in queue (training_label remains NULL)
            }

            // Update local rendering counters only (no business logic here)
            session.SessionProcessedCount++;
            session.CurrentOffset++;
            session.ActionCounts[chosenAction]++;
            if (decisionResult.Value.IsOverride) session.SessionOverrideCount++;

            // Cold-start threshold prompt
            if (session.Mode == TriageMode.ColdStart)
            {
                session.LabeledCount++;
                if (session.LabeledCount >= session.LabelingThreshold && !session.ThresholdPromptShownThisSession)
                {
                    session.ThresholdPromptShownThisSession = true;
                    if (await ShowThresholdPromptAsync(ct) == ThresholdChoice.GoToTraining)
                        goto ExitLoop;
                }
            }
        }
    }

    ExitLoop:
    return Result<TriageSessionSummary>.Success(BuildSummary(session));
}
```

---

## 5. Register Services in DI

**File**: `src/TrashMailPanda/TrashMailPanda/` (ServiceCollectionExtensions or equivalent)

```csharp
// In AddTrashMailPandaServices or equivalent registration method

// Business logic layer (no IAnsiConsole dependency)
services.AddScoped<IEmailTriageService, EmailTriageService>();
services.AddScoped<IBulkOperationService, BulkOperationService>();

// TUI presentation layer
services.AddScoped<IEmailTriageConsoleService, EmailTriageConsoleService>();
services.AddScoped<IBulkOperationConsoleService, BulkOperationConsoleService>();
services.AddScoped<IProviderSettingsConsoleService, ProviderSettingsConsoleService>();
services.AddSingleton<IConsoleHelpPanel, ConsoleHelpPanel>();
services.AddSingleton<IAnsiConsole>(_ => AnsiConsole.Console);
// Note: IEmailArchiveService already registered by the storage provider setup
```

---

## 6. Wire Email Triage into Program.cs

**File**: `src/TrashMailPanda/TrashMailPanda/Program.cs`

Replace the stub in `HandleModeSelectionAsync`:

```csharp
case OperationalMode.EmailTriage:
    var triageService = host.Services.GetRequiredService<IEmailTriageConsoleService>();
    var triageResult = await triageService.RunAsync("me", cancellationToken);
    if (!triageResult.IsSuccess)
        AnsiConsole.MarkupLine($"{ConsoleColors.Error}✗{ConsoleColors.Close} {ConsoleColors.ErrorText}{Markup.Escape(triageResult.Error.Message)}{ConsoleColors.Close}");
    return true; // keep app running; return to main menu
```

---

## 7. Writing Unit Tests

**File**: `src/Tests/TrashMailPanda.Tests/Unit/Services/EmailTriageConsoleServiceTests.cs`

### Setup pattern

The TUI presenter test only mocks `IEmailTriageService` — not individual providers.
This keeps presenter tests fast and focused purely on rendering/interaction logic.

```csharp
[Trait("Category", "Unit")]
public class EmailTriageConsoleServiceTests
{
    private static (EmailTriageConsoleService Service, StringWriter Output) CreateService(
        IEmailTriageService? triageService = null)
    {
        var writer = new StringWriter();
        var console = AnsiConsole.Create(new AnsiConsoleSettings
        {
            Out = new AnsiConsoleOutput(writer),
            Interactive = InteractivityMode.NonInteractive
        });

        // Default: empty queue, ColdStart mode
        triageService ??= CreateDefaultMockTriageService();

        var service = new EmailTriageConsoleService(
            triageService,
            new ConsoleHelpPanel(console),
            NullLogger<EmailTriageConsoleService>.Instance,
            console);

        return (service, writer);
    }

    // For business logic tests, use EmailTriageServiceTests.cs which mocks
    // IEmailProvider, IMLModelProvider, IEmailArchiveService individually.

    [Fact]
    public async Task ColdStart_NoModel_DoesNotShowAiRecommendation()
    {
        // Arrange: IEmailTriageService.GetSessionInfoAsync returns ColdStart mode
        // Mock IEmailTriageService.GetAiRecommendationAsync returns Success(null)
        // Mock IEmailTriageService.GetNextBatchAsync returns 1 feature
        var mockTriage = new Mock<IEmailTriageService>();
        mockTriage.Setup(s => s.GetSessionInfoAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result<TriageSessionInfo>.Success(
                new TriageSessionInfo(TriageMode.ColdStart, 0, 50, false)));
        mockTriage.Setup(s => s.GetAiRecommendationAsync(It.IsAny<EmailFeatureVector>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result<ActionPrediction?>.Success(null));
        // ... GetNextBatchAsync setup, ApplyDecisionAsync setup ...

        var (service, output) = CreateService(mockTriage.Object);
        // Assert: output does not contain "Recommendation" or "Confidence"
        // Assert: output contains "(No AI suggestions yet)"
    }

    [Fact]
    public async Task ColdStart_ThresholdCrossed_ShowsThresholdPrompt()
    {
        // Arrange: LabelingThreshold = 3; 3 mock emails in GetNextBatchAsync
        // ApplyDecisionAsync succeeds for all 3
        // Assert: after 3rd email, output contains threshold prompt text
    }

    [Fact]
    public async Task AiAssisted_UserOverride_IsRecordedInDecision()
    {
        // Arrange: GetAiRecommendationAsync returns "Keep"
        // Simulate user pressing "A" (Archive) — different from AI recommendation
        // ApplyDecisionAsync is called with chosenAction="Archive", aiRecommendation="Keep"
        // Assert: ApplyDecisionAsync was called with the correct override arguments
        // Note: IsOverride calculation (chosenAction != aiRecommendation) lives in EmailTriageService,
        //       tested in EmailTriageServiceTests.cs
    }

    [Fact]
    public async Task EmailAction_ServiceFailure_DisplaysRedError_DoesNotAdvanceSession()
    {
        // Arrange: IEmailTriageService.ApplyDecisionAsync returns NetworkError
        // Assert: output contains bold red error markup
        // Assert: session.SessionProcessedCount still 0 (presenter respects failure)
        // Note: the guarantee that training_label is NOT stored is tested in EmailTriageServiceTests.cs
    }
}
```

---

## 8. Verify Color Scheme Compliance

After implementing all modes, run the compliance check from §1 to confirm zero raw markup strings:

```bash
grep -rn '\[green\]\|\[bold red\]\|\[cyan\]\|\[yellow\]\|\[magenta\]\|\[blue\]\|\[dim\]' \
  src/TrashMailPanda/TrashMailPanda/ \
  src/Tests/ \
  --include="*.cs" \
  | grep -v ConsoleColors.cs
# Expected: zero matches (all markup goes through ConsoleColors constants)
```

---

## 9. Run All Tests

```bash
# Unit tests
dotnet test --filter "Category=Unit" --logger console --verbosity normal

# Full suite
dotnet test --configuration Release

# With coverage
dotnet test --collect:"XPlat Code Coverage" --configuration Release
```

---

## 10. Verify End-to-End (Manual Testing Checklist)

- [ ] App starts with no trained model → Mode Selection shows EmailTriage as available
- [ ] Enter Email Triage → Cold Start notice appears, no AI recommendation shown
- [ ] Press `K` → Next email shown, `labeled count` increments
- [ ] Reach threshold (100 labels by default) → Threshold prompt appears with two choices
- [ ] Choose "Go to Training" → Back at main menu
- [ ] Train a model (Training mode works) → Re-enter Email Triage → AI recommendations and confidence shown
- [ ] Press `?` in any mode → Help panel appears with correct key bindings
- [ ] Press `Esc` in help → Returns to previous state
- [ ] All error messages are bold red + red body text (no yellow/white errors)
- [ ] All success messages are green
- [ ] Training metrics values appear in magenta
