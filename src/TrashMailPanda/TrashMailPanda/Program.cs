using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Serilog;
using Serilog.Events;
using System.IO;
using System;
using System.Threading;
using System.Threading.Tasks;
using TrashMailPanda.Models.Console;
using TrashMailPanda.Services;
using TrashMailPanda.Services.Console;

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

            var appOrchestrator = host.Services.GetRequiredService<IApplicationOrchestrator>();
            var renderer = host.Services.GetRequiredService<ConsoleEventRenderer>();

            // Wire console renderer to event stream before starting
            renderer.Subscribe(appOrchestrator);

            var result = await appOrchestrator.RunAsync(_cancellationTokenSource.Token);
            return result.IsSuccess ? result.Value : 1;
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
            .UseSerilog((_, _, loggerConfig) =>
            {
                var logDir = Path.Combine("data", "logs");
                Directory.CreateDirectory(logDir);
                var timestamp = DateTime.Now.ToString("yyyyMMdd-HHmmss");
                var logPath = Path.Combine(logDir, $"trashmail-panda-{timestamp}.log");

                loggerConfig
                    .MinimumLevel.Debug()
                    .MinimumLevel.Override("Microsoft", LogEventLevel.Warning)
                    .MinimumLevel.Override("Microsoft.Hosting.Lifetime", LogEventLevel.Information)
                    .MinimumLevel.Override("Microsoft.EntityFrameworkCore", LogEventLevel.Warning)
                    .MinimumLevel.Override("System", LogEventLevel.Warning)
                    .MinimumLevel.Override("Google", LogEventLevel.Warning)
                    .WriteTo.File(
                        logPath,
                        outputTemplate: "{Timestamp:yyyy-MM-dd HH:mm:ss.fff zzz} [{Level:u3}] {SourceContext}: {Message:lj}{NewLine}{Exception}");
            });

    private static void OnCancelKeyPress(object? sender, ConsoleCancelEventArgs e)
    {
        e.Cancel = true; // Prevent immediate termination
        Console.WriteLine();
        Console.WriteLine("Shutting down gracefully...");
        _cancellationTokenSource.Cancel();
    }
}
