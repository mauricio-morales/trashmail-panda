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
                Console.WriteLine("Press Ctrl+C to exit...");

                // Keep running until Ctrl+C
                await Task.Delay(Timeout.Infinite, _cancellationTokenSource.Token);
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
                Console.WriteLine();
                Console.WriteLine("✗ Application startup failed.");
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
}
