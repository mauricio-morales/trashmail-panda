using Microsoft.Extensions.Logging;
using TrashMailPanda.Models;
using TrashMailPanda.Models.Console;
using TrashMailPanda.Providers.Storage;
using TrashMailPanda.Providers.Storage.Models;
using TrashMailPanda.Services.Console;
using TrashMailPanda.Shared.Base;

namespace TrashMailPanda.Services;

/// <summary>
/// Drives the full application workflow extracted from <c>Program.cs</c>:
/// startup → mode selection → triage/bulk ops/settings/training → exit.
/// Emits typed <see cref="ApplicationEvent"/> records for UI layers to subscribe to.
/// </summary>
public sealed class ApplicationOrchestrator : IApplicationOrchestrator
{
    private volatile bool _isRunning;

    private readonly ConsoleStatusDisplay _statusDisplay;
    private readonly ConsoleStartupOrchestrator _startupOrchestrator;
    private readonly ConfigurationWizard _wizard;
    private readonly ModeSelectionMenu _modeMenu;
    private readonly IScanProgressRepository _scanProgressRepo;
    private readonly GmailTrainingScanCommand _trainingScanCommand;
    private readonly IEmailArchiveService _archiveService;
    private readonly TrainingConsoleService _trainingConsoleService;
    private readonly IEmailTriageConsoleService _triageConsoleService;
    private readonly IBulkOperationConsoleService _bulkConsoleService;
    private readonly IProviderSettingsConsoleService _settingsConsoleService;
    private readonly ILogger<ApplicationOrchestrator> _logger;

    public event EventHandler<ApplicationEventArgs>? ApplicationEventRaised;

    public bool IsRunning => _isRunning;

    public ApplicationOrchestrator(
        ConsoleStatusDisplay statusDisplay,
        ConsoleStartupOrchestrator startupOrchestrator,
        ConfigurationWizard wizard,
        ModeSelectionMenu modeMenu,
        IScanProgressRepository scanProgressRepo,
        GmailTrainingScanCommand trainingScanCommand,
        IEmailArchiveService archiveService,
        TrainingConsoleService trainingConsoleService,
        IEmailTriageConsoleService triageConsoleService,
        IBulkOperationConsoleService bulkConsoleService,
        IProviderSettingsConsoleService settingsConsoleService,
        ILogger<ApplicationOrchestrator> logger)
    {
        _statusDisplay = statusDisplay;
        _startupOrchestrator = startupOrchestrator;
        _wizard = wizard;
        _modeMenu = modeMenu;
        _scanProgressRepo = scanProgressRepo;
        _trainingScanCommand = trainingScanCommand;
        _archiveService = archiveService;
        _trainingConsoleService = trainingConsoleService;
        _triageConsoleService = triageConsoleService;
        _bulkConsoleService = bulkConsoleService;
        _settingsConsoleService = settingsConsoleService;
        _logger = logger;
    }

    /// <inheritdoc/>
    public async Task<Result<int>> RunAsync(CancellationToken cancellationToken = default)
    {
        if (_isRunning)
        {
            return Result<int>.Failure(new InvalidOperationError(
                "Application workflow is already running"));
        }

        _isRunning = true;
        try
        {
            return await RunWorkflowAsync(cancellationToken);
        }
        finally
        {
            _isRunning = false;
        }
    }

    // ─────────────────────────────────────────────────────────────────────────
    // Private workflow
    // ─────────────────────────────────────────────────────────────────────────

    private async Task<Result<int>> RunWorkflowAsync(CancellationToken cancellationToken)
    {
        try
        {
            _statusDisplay.DisplayWelcomeBanner();

            // 1. Check first-time setup
            var setupNeeded = await _startupOrchestrator.CheckProviderSetupNeeded();
            EmitEvent(new ProviderStatusChangedEvent
            {
                ProviderName = "Setup",
                IsHealthy = !setupNeeded,
                StatusMessage = setupNeeded ? "First-time setup required" : "Providers configured",
            });

            if (setupNeeded)
            {
                System.Console.WriteLine();
                System.Console.WriteLine("First-time setup required...");
                System.Console.WriteLine();

                var wizardCompleted = await _wizard.RunAsync(cancellationToken);
                if (!wizardCompleted)
                {
                    System.Console.WriteLine();
                    System.Console.WriteLine("Setup cancelled by user.");
                    EmitEvent(new ApplicationWorkflowCompletedEvent
                    {
                        ExitCode = 1,
                        Reason = "Setup cancelled by user",
                    });
                    return Result<int>.Success(1);
                }
            }

            // 2. Initialize providers
            var state = await _startupOrchestrator.InitializeProvidersAsync(cancellationToken);

            if (state.OverallStatus == SequenceStatus.Completed)
            {
                System.Console.WriteLine();
                EmitEvent(new StatusMessageEvent { Message = "[green]✓ Application ready![/]" });
                System.Console.WriteLine();

                // 3. Startup training sync
                await HandleStartupTrainingSyncAsync(cancellationToken);

                // 4. Mode selection loop
                var availableModes = new List<OperationalMode>
                {
                    OperationalMode.EmailTriage,
                    OperationalMode.BulkOperations,
                    OperationalMode.ProviderSettings,
                    OperationalMode.TrainModel,
                    OperationalMode.UIMode,
                    OperationalMode.Exit,
                };

                var running = true;
                while (running && !cancellationToken.IsCancellationRequested)
                {
                    EmitEvent(new ModeSelectionRequestedEvent
                    {
                        AvailableModes = availableModes,
                    });

                    var selectedMode = await _modeMenu.ShowAsync(cancellationToken);

                    if (selectedMode == OperationalMode.Exit)
                    {
                        running = false;
                        break;
                    }

                    running = await HandleModeSelectionAsync(selectedMode, cancellationToken);
                }

                System.Console.WriteLine();
                System.Console.WriteLine("Application exiting...");
                EmitEvent(new ApplicationWorkflowCompletedEvent
                {
                    ExitCode = 0,
                    Reason = "User exited",
                });
                return Result<int>.Success(0);
            }
            else if (state.OverallStatus == SequenceStatus.Cancelled)
            {
                System.Console.WriteLine();
                System.Console.WriteLine("Application startup cancelled by user.");
                EmitEvent(new ApplicationWorkflowCompletedEvent
                {
                    ExitCode = 1,
                    Reason = "Startup cancelled by user",
                });
                return Result<int>.Success(1);
            }
            else
            {
                System.Console.WriteLine();
                System.Console.WriteLine("Application startup incomplete.");
                System.Console.WriteLine("Please resolve the provider issues before trying again.");
                EmitEvent(new ApplicationWorkflowCompletedEvent
                {
                    ExitCode = 1,
                    Reason = "Provider initialization failed",
                });
                return Result<int>.Success(1);
            }
        }
        catch (OperationCanceledException)
        {
            System.Console.WriteLine();
            System.Console.WriteLine("Application cancelled.");
            EmitEvent(new ApplicationWorkflowCompletedEvent
            {
                ExitCode = 0,
                Reason = "Cancelled via CancellationToken",
            });
            return Result<int>.Success(0);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Unhandled error in application workflow");
            EmitEvent(new ApplicationWorkflowCompletedEvent
            {
                ExitCode = 1,
                Reason = $"Fatal error: {ex.Message}",
            });
            return Result<int>.Failure(new UnknownError($"Fatal error: {ex.Message}", InnerException: ex));
        }
    }

    /// <summary>
    /// Called after providers are ready. Determines the correct training data action:
    /// - No scan ever / interrupted → auto-run initial scan
    /// - In-progress / paused       → auto-resume initial scan
    /// - Completed                  → auto-run incremental History sync (silent, every startup)
    /// </summary>
    private async Task HandleStartupTrainingSyncAsync(CancellationToken cancellationToken)
    {
        ScanProgressEntity? latest = null;
        try
        {
            var result = await _scanProgressRepo.GetLatestAsync("me", cancellationToken);
            latest = result.IsSuccess ? result.Value : (ScanProgressEntity?)null;
        }
        catch { return; }

        var status = latest?.Status;

        // Case 1: completed initial scan → auto-sync history changes, no prompt needed
        if (status == "Completed")
        {
            var featureCountResult = await _archiveService.CountLabeledAsync(cancellationToken);
            var totalFeaturesResult = await _archiveService.GetStorageUsageAsync(cancellationToken);
            bool featuresEmpty = totalFeaturesResult.IsSuccess && totalFeaturesResult.Value.FeatureCount == 0;

            if (featuresEmpty && latest is not null)
            {
                EmitEvent(new StatusMessageEvent { Message = "[yellow]⚠ Triage queue is empty — re-scanning to populate email features...[/]" });
                System.Console.WriteLine();
                await _trainingScanCommand.RunInitialScanAsync("me", cancellationToken);
                System.Console.WriteLine();
                return;
            }

            EmitEvent(new StatusMessageEvent { Message = "[dim]→ Syncing new email changes...[/]" });
            await _trainingScanCommand.RunIncrementalScanAsync("me", cancellationToken);
            System.Console.WriteLine();
            return;
        }

        // Case 2/3: in-progress, paused, interrupted, or never started → run/resume
        bool isResume = status is "InProgress" or "PausedStorageFull" or "Interrupted";

        EmitEvent(new StartupScanRequiredEvent { IsResume = isResume });

        System.Console.WriteLine();
        await _trainingScanCommand.RunInitialScanAsync("me", cancellationToken);
        System.Console.WriteLine();
    }

    /// <summary>
    /// Dispatches the selected operational mode to the appropriate console service.
    /// </summary>
    /// <returns>True to continue showing the menu, false to exit.</returns>
    private async Task<bool> HandleModeSelectionAsync(
        OperationalMode mode,
        CancellationToken cancellationToken)
    {
        System.Console.WriteLine();

        switch (mode)
        {
            case OperationalMode.EmailTriage:
                await _triageConsoleService.RunAsync("me", cancellationToken);
                return true;

            case OperationalMode.BulkOperations:
                await _bulkConsoleService.RunAsync(cancellationToken);
                return true;

            case OperationalMode.ProviderSettings:
                await _settingsConsoleService.RunAsync(cancellationToken);
                return true;

            case OperationalMode.TrainModel:
                await _trainingConsoleService.RunTrainingAsync("manual", cancellationToken);
                EmitEvent(new StatusMessageEvent { Message = "Press [green]Enter[/] to return to menu..." });
                System.Console.ReadLine();
                return true;

            case OperationalMode.UIMode:
                EmitEvent(new StatusMessageEvent { Message = "[yellow]🖥️  UI Mode - Coming soon![/]" });
                EmitEvent(new StatusMessageEvent { Message = "[dim]This mode will launch the Avalonia desktop UI.[/]" });
                System.Console.WriteLine();
                EmitEvent(new StatusMessageEvent { Message = "Press [green]Enter[/] to return to menu..." });
                System.Console.ReadLine();
                return true;

            case OperationalMode.Exit:
                return false;

            default:
                EmitEvent(new StatusMessageEvent { Message = "[red]Unknown mode selected.[/]" });
                return true;
        }
    }

    // ─────────────────────────────────────────────────────────────────────────
    // Event emission helpers
    // ─────────────────────────────────────────────────────────────────────────

    private void EmitEvent(ApplicationEvent @event)
    {
        var handler = ApplicationEventRaised;
        if (handler is null)
            return;

        foreach (var subscriber in handler.GetInvocationList())
        {
            try
            {
                subscriber.DynamicInvoke(this, new ApplicationEventArgs(@event));
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex,
                    "Event subscriber threw an exception for event {EventType}",
                    @event.EventType);
            }
        }
    }
}
