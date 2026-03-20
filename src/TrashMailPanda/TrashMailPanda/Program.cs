using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using System;
using System.Threading;
using System.Threading.Tasks;
using Spectre.Console;
using TrashMailPanda.Providers.Storage;
using TrashMailPanda.Providers.Storage.Models;
using TrashMailPanda.Services;
using TrashMailPanda.Services.Console;
using TrashMailPanda.Models.Console;

namespace TrashMailPanda;

sealed class Program
{
    private static readonly CancellationTokenSource _cancellationTokenSource = new();

    public static async Task<int> Main(string[] args)
    {
        // Set up Ctrl+C handling
        Console.CancelKeyPress += OnCancelKeyPress;

        try
        {
            var host = CreateHostBuilder(args).Build();

            // Get required services
            var orchestrator = host.Services.GetRequiredService<ConsoleStartupOrchestrator>();
            var statusDisplay = host.Services.GetRequiredService<ConsoleStatusDisplay>();
            var wizard = host.Services.GetRequiredService<ConfigurationWizard>();
            var modeMenu = host.Services.GetRequiredService<ModeSelectionMenu>();
            var trainingScanCommand = host.Services.GetRequiredService<GmailTrainingScanCommand>();
            var scanProgressRepo = host.Services.GetRequiredService<IScanProgressRepository>();
            var trainingConsoleService = host.Services.GetRequiredService<TrainingConsoleService>();
            var triageConsoleService = host.Services.GetRequiredService<IEmailTriageConsoleService>();
            var bulkConsoleService = host.Services.GetRequiredService<IBulkOperationConsoleService>();
            var settingsConsoleService = host.Services.GetRequiredService<IProviderSettingsConsoleService>();

            // Display welcome banner
            statusDisplay.DisplayWelcomeBanner();

            // Check if first-time setup is needed
            var setupNeeded = await orchestrator.CheckProviderSetupNeeded();

            if (setupNeeded)
            {
                Console.WriteLine();
                Console.WriteLine("First-time setup required...");
                Console.WriteLine();

                // Run configuration wizard
                var wizardCompleted = await wizard.RunAsync(_cancellationTokenSource.Token);

                if (!wizardCompleted)
                {
                    Console.WriteLine();
                    Console.WriteLine("Setup cancelled by user.");
                    return 1;
                }

                // Configuration wizard displays its own confirmation and starts initialization automatically
                // The wizard shows "Starting provider initialization..." before returning
            }

            // Initialize providers sequentially
            var state = await orchestrator.InitializeProvidersAsync(_cancellationTokenSource.Token);

            if (state.OverallStatus == SequenceStatus.Completed)
            {
                Console.WriteLine();
                AnsiConsole.MarkupLine("[green]✓ Application ready![/]");
                Console.WriteLine();

                var archiveService = host.Services.GetRequiredService<IEmailArchiveService>();

                // Sync training data: resume/run initial scan or auto-sync history changes
                await HandleStartupTrainingSyncAsync(
                    scanProgressRepo, trainingScanCommand, archiveService, _cancellationTokenSource.Token);

                // Display mode selection menu
                var running = true;
                while (running && !_cancellationTokenSource.Token.IsCancellationRequested)
                {
                    var selectedMode = await modeMenu.ShowAsync(_cancellationTokenSource.Token);

                    if (selectedMode == OperationalMode.Exit)
                    {
                        running = false;
                        break;
                    }

                    // Handle mode selection
                    running = await HandleModeSelectionAsync(selectedMode, trainingConsoleService, triageConsoleService, bulkConsoleService, settingsConsoleService, _cancellationTokenSource.Token);
                }

                Console.WriteLine();
                Console.WriteLine("Application exiting...");
                return 0;
            }
            else if (state.OverallStatus == SequenceStatus.Cancelled)
            {
                Console.WriteLine();
                Console.WriteLine("Application startup cancelled by user.");
                return 1;
            }
            else
            {
                // Failed status means user chose to exit or all recovery attempts failed
                Console.WriteLine();
                Console.WriteLine("Application startup incomplete.");
                Console.WriteLine("Please resolve the provider issues before trying again.");
                return 1;
            }
        }
        catch (OperationCanceledException)
        {
            Console.WriteLine();
            Console.WriteLine("Application cancelled.");
            return 0;
        }
        catch (Exception ex)
        {
            Console.WriteLine();
            Console.WriteLine($"Fatal error: {ex.Message}");
            Console.WriteLine(ex.StackTrace);
            return 1;
        }
        finally
        {
            _cancellationTokenSource.Dispose();
        }
    }

    private static IHostBuilder CreateHostBuilder(string[] args) =>
        Host.CreateDefaultBuilder(args)
            .ConfigureAppConfiguration((context, config) =>
            {
                // Set base path to the application directory where appsettings.json is located
                config.SetBasePath(AppContext.BaseDirectory);
                config.AddJsonFile("appsettings.json", optional: false, reloadOnChange: true);
                config.AddEnvironmentVariables();
                config.AddCommandLine(args);
            })
            .ConfigureServices((context, services) =>
            {
                // Add all TrashMailPanda services (from ServiceCollectionExtensions)
                services.AddTrashMailPandaServices(context.Configuration);

                // Add console-specific services (already registered in AddTrashMailPandaServices)
                services.Configure<ConsoleDisplayOptions>(context.Configuration.GetSection("ConsoleDisplayOptions"));
            })
            .ConfigureLogging(logging =>
            {
                logging.ClearProviders();
                logging.AddConsole();
            });

    private static void OnCancelKeyPress(object? sender, ConsoleCancelEventArgs e)
    {
        e.Cancel = true; // Prevent immediate termination
        Console.WriteLine();
        Console.WriteLine("Shutting down gracefully...");
        _cancellationTokenSource.Cancel();
    }

    /// <summary>
    /// Checks whether training data exists and, if not, prompts the user to run an
    /// initial Gmail scan before entering the main menu.
    /// </summary>
    /// <summary>
    /// Called after providers are ready. Determines the correct training data action:
    /// - No scan ever / interrupted → prompt to run initial scan
    /// - In-progress / paused       → prompt to resume initial scan
    /// - Completed                  → auto-run incremental History sync (silent, every startup)
    /// </summary>
    private static async Task HandleStartupTrainingSyncAsync(
        IScanProgressRepository scanProgressRepo,
        GmailTrainingScanCommand trainingScanCommand,
        IEmailArchiveService archiveService,
        CancellationToken cancellationToken)
    {
        ScanProgressEntity? latest = null;
        try
        {
            var result = await scanProgressRepo.GetLatestAsync("me", cancellationToken);
            latest = result.IsSuccess ? result.Value : (ScanProgressEntity?)null;
        }
        catch { return; }

        var status = latest?.Status;

        // ———————————————————————————————————————————————————————
        // Case 1: completed initial scan → auto-sync history changes, no prompt needed
        // ———————————————————————————————————————————————————————
        if (status == "Completed")
        {
            // If the scan completed but email_features is empty, the scan ran before
            // feature extraction was wired up.  Reset progress to trigger a full re-scan
            // so the triage queue gets populated.
            var featureCountResult = await archiveService.CountLabeledAsync(cancellationToken);
            var totalFeaturesResult = await archiveService.GetStorageUsageAsync(cancellationToken);
            bool featuresEmpty = totalFeaturesResult.IsSuccess && totalFeaturesResult.Value.FeatureCount == 0;

            if (featuresEmpty && latest is not null)
            {
                AnsiConsole.MarkupLine("[yellow]⚠ Triage queue is empty — re-scanning to populate email features...[/]");
                Console.WriteLine();
                await trainingScanCommand.RunInitialScanAsync("me", cancellationToken);
                Console.WriteLine();
                return;
            }

            AnsiConsole.MarkupLine("[dim]→ Syncing new email changes...[/]");
            await trainingScanCommand.RunIncrementalScanAsync("me", cancellationToken);
            Console.WriteLine();
            return;
        }

        // ———————————————————————————————————————————————————————
        // Case 2: in-progress, paused, or interrupted → auto-resume
        // Case 3: null (never started)                → auto-run initial scan
        // Scan runs automatically on every startup until completed — no skip option.
        // ———————————————————————————————————————————————————————
        bool isResume = status is "InProgress" or "PausedStorageFull" or "Interrupted";

        if (isResume)
        {
            AnsiConsole.Write(
                new Panel(
                    new Markup(
                        "[yellow]A previous scan was interrupted[/] — resuming from last checkpoint.\n" +
                        "[dim]The initial scan must complete before Email Triage is available.[/]"))
                    .Header("[bold yellow]⚠  Resuming Incomplete Scan[/]")
                    .BorderColor(Color.Yellow)
                    .Padding(1, 0));
        }
        else
        {
            AnsiConsole.Write(
                new Panel(
                    new Markup(
                        "[yellow]No training data found[/] — running initial Gmail scan.\n" +
                        "[dim]This scans Spam → Trash → Sent → Archive → Inbox to build your dataset.\n" +
                        "It runs once and takes 1–5 minutes depending on mailbox size.[/]"))
                    .Header("[bold yellow]⚠  Initial Scan Required[/]")
                    .BorderColor(Color.Yellow)
                    .Padding(1, 0));
        }

        Console.WriteLine();
        await trainingScanCommand.RunInitialScanAsync("me", cancellationToken);
        Console.WriteLine();
    }

    /// <summary>
    /// Handles mode selection with stub implementations.
    /// </summary>
    /// <param name="mode">Selected operational mode.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>True to continue showing menu, false to exit.</returns>
    private static async Task<bool> HandleModeSelectionAsync(
        OperationalMode mode,
        TrainingConsoleService trainingConsoleService,
        IEmailTriageConsoleService triageConsoleService,
        IBulkOperationConsoleService bulkConsoleService,
        IProviderSettingsConsoleService settingsConsoleService,
        CancellationToken cancellationToken)
    {
        Console.WriteLine();

        switch (mode)
        {
            case OperationalMode.EmailTriage:
                await triageConsoleService.RunAsync("me", cancellationToken);
                return true;

            case OperationalMode.BulkOperations:
                await bulkConsoleService.RunAsync(cancellationToken);
                return true;

            case OperationalMode.ProviderSettings:
                await settingsConsoleService.RunAsync(cancellationToken);
                return true;

            case OperationalMode.TrainModel:
                await trainingConsoleService.RunTrainingAsync("manual", cancellationToken);
                Spectre.Console.AnsiConsole.MarkupLine("Press [green]Enter[/] to return to menu...");
                Console.ReadLine();
                return true;

            case OperationalMode.UIMode:
                Spectre.Console.AnsiConsole.MarkupLine("[yellow]🖥️  UI Mode - Coming soon![/]");
                Spectre.Console.AnsiConsole.MarkupLine("[dim]This mode will launch the Avalonia desktop UI.[/]");
                Spectre.Console.AnsiConsole.WriteLine();
                Spectre.Console.AnsiConsole.MarkupLine("Press [green]Enter[/] to return to menu...");
                Console.ReadLine();
                return true;

            case OperationalMode.Exit:
                return false;

            default:
                Spectre.Console.AnsiConsole.MarkupLine("[red]Unknown mode selected.[/]");
                return true;
        }
    }
}
