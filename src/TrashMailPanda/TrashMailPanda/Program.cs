using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using System;
using System.Threading;
using System.Threading.Tasks;
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
                Console.WriteLine("✓ Application ready!");
                Console.WriteLine();

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
                    running = await HandleModeSelectionAsync(selectedMode, _cancellationTokenSource.Token);
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
                logging.AddDebug();
            });

    private static void OnCancelKeyPress(object? sender, ConsoleCancelEventArgs e)
    {
        e.Cancel = true; // Prevent immediate termination
        Console.WriteLine();
        Console.WriteLine("Shutting down gracefully...");
        _cancellationTokenSource.Cancel();
    }

    /// <summary>
    /// Handles mode selection with stub implementations.
    /// </summary>
    /// <param name="mode">Selected operational mode.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>True to continue showing menu, false to exit.</returns>
    private static async Task<bool> HandleModeSelectionAsync(OperationalMode mode, CancellationToken cancellationToken)
    {
        await Task.CompletedTask; // Stub implementations are sync for now

        Console.WriteLine();
        Console.WriteLine($"Selected mode: {mode}");
        Console.WriteLine();

        switch (mode)
        {
            case OperationalMode.EmailTriage:
                Spectre.Console.AnsiConsole.MarkupLine("[yellow]📧 Email Triage mode - Coming soon![/]");
                Spectre.Console.AnsiConsole.MarkupLine("[dim]This mode will launch the email triage workflow.[/]");
                Spectre.Console.AnsiConsole.WriteLine();
                Spectre.Console.AnsiConsole.MarkupLine("Press [green]Enter[/] to return to menu...");
                Console.ReadLine();
                return true;

            case OperationalMode.BulkOperations:
                Spectre.Console.AnsiConsole.MarkupLine("[yellow]⚡ Bulk Operations mode - Coming soon![/]");
                Spectre.Console.AnsiConsole.MarkupLine("[dim]This mode will allow bulk email actions (delete, archive, label).[/]");
                Spectre.Console.AnsiConsole.WriteLine();
                Spectre.Console.AnsiConsole.MarkupLine("Press [green]Enter[/] to return to menu...");
                Console.ReadLine();
                return true;

            case OperationalMode.ProviderSettings:
                Spectre.Console.AnsiConsole.MarkupLine("[yellow]⚙️  Provider Settings mode - Coming soon![/]");
                Spectre.Console.AnsiConsole.MarkupLine("[dim]This mode will allow reconfiguration of provider settings.[/]");
                Spectre.Console.AnsiConsole.WriteLine();
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
