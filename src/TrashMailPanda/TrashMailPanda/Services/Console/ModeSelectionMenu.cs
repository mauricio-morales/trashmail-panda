using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Spectre.Console;
using TrashMailPanda.Models.Console;
using TrashMailPanda.Providers.Email;
using TrashMailPanda.Providers.Storage;
namespace TrashMailPanda.Services.Console;

/// <summary>
/// Provides mode selection menu after successful provider initialization.
/// </summary>
public class ModeSelectionMenu
{
    private readonly IStorageProvider _storageProvider;
    private readonly IEmailProvider _emailProvider;
    private readonly IScanProgressRepository _scanProgressRepo;
    private readonly ConsoleStatusDisplay _statusDisplay;
    private readonly ConsoleDisplayOptions _displayOptions;
    private readonly ILogger<ModeSelectionMenu> _logger;

    public ModeSelectionMenu(
        IStorageProvider storageProvider,
        IEmailProvider emailProvider,
        IScanProgressRepository scanProgressRepo,
        ConsoleStatusDisplay statusDisplay,
        IOptions<ConsoleDisplayOptions> displayOptions,
        ILogger<ModeSelectionMenu> logger)
    {
        _storageProvider = storageProvider ?? throw new ArgumentNullException(nameof(storageProvider));
        _emailProvider = emailProvider ?? throw new ArgumentNullException(nameof(emailProvider));
        _scanProgressRepo = scanProgressRepo ?? throw new ArgumentNullException(nameof(scanProgressRepo));
        _statusDisplay = statusDisplay ?? throw new ArgumentNullException(nameof(statusDisplay));
        _displayOptions = displayOptions?.Value ?? throw new ArgumentNullException(nameof(displayOptions));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    /// <summary>
    /// Displays mode selection menu and returns the selected mode.
    /// </summary>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The selected operational mode.</returns>
    public async Task<OperationalMode> ShowAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            _logger.LogInformation("Displaying mode selection menu");

            // Display current provider status summary
            await DisplayProviderStatusAsync();

            // Get available modes based on provider health
            var availableModes = await GetAvailableModesAsync();

            // Prompt user for mode selection
            var selectedMode = await PromptForModeAsync(availableModes, cancellationToken);

            _logger.LogInformation("User selected mode: {Mode}", selectedMode);

            return selectedMode;
        }
        catch (OperationCanceledException)
        {
            _logger.LogInformation("Mode selection cancelled by user");
            return OperationalMode.Exit;
        }
    }

    /// <summary>
    /// Displays current health status summary for all providers.
    /// </summary>
    private async Task DisplayProviderStatusAsync()
    {
        AnsiConsole.WriteLine();
        AnsiConsole.Write(new Rule("[blue]Provider Status[/]"));
        AnsiConsole.WriteLine();

        // Storage provider status
        await CheckProviderHealthAsync(_storageProvider, "Storage");

        // Gmail provider status
        await CheckProviderHealthAsync(_emailProvider, "Gmail");

        AnsiConsole.WriteLine();
    }

    /// <summary>
    /// Checks provider health and displays status.
    /// </summary>
    private async Task<bool> CheckProviderHealthAsync(object provider, string providerName)
    {
        try
        {
            if (provider is IEmailProvider emailProvider)
            {
                var healthResult = await emailProvider.HealthCheckAsync();
                if (healthResult.IsSuccess && healthResult.Value)
                {
                    AnsiConsole.MarkupLine($"[green]✓[/] {providerName}: [green]Healthy[/]");
                    return true;
                }
                else
                {
                    AnsiConsole.MarkupLine($"[yellow]⚠[/] {providerName}: [yellow]Degraded[/]");
                    return false;
                }
            }
            else
            {
                // Storage provider doesn't have HealthCheck method yet
                AnsiConsole.MarkupLine($"[green]✓[/] {providerName}: [green]Ready[/]");
                return true;
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Health check failed for {Provider}", providerName);
            AnsiConsole.MarkupLine($"[red]✗[/] {providerName}: [red]Unavailable[/]");
            return false;
        }
    }

    /// <summary>
    /// Gets available operational modes based on provider health.
    /// </summary>
    /// <returns>List of available modes with display text.</returns>
    private async Task<List<(OperationalMode Mode, string DisplayText, bool Enabled)>> GetAvailableModesAsync()
    {
        // Check Gmail health
        var gmailHealthy = false;
        try
        {
            var gmailHealth = await _emailProvider.HealthCheckAsync();
            gmailHealthy = gmailHealth.IsSuccess && gmailHealth.Value;
        }
        catch
        {
            gmailHealthy = false;
        }

        // Gate email ops on a fully completed initial scan
        var hasCompletedScan = false;
        try
        {
            var latestResult = await _scanProgressRepo.GetLatestAsync("me");
            hasCompletedScan = latestResult.IsSuccess && latestResult.Value?.Status == "Completed";
        }
        catch
        {
            hasCompletedScan = false;
        }

        var emailOpsEnabled = gmailHealthy && hasCompletedScan;
        var emailOpsLabel = !gmailHealthy ? " [dim](Requires Gmail)[/]"
                          : !hasCompletedScan ? " [dim](Requires training data)[/]"
                          : string.Empty;

        var modes = new List<(OperationalMode, string, bool)>
        {
            // Email Triage - requires Storage + Gmail + training data
            (OperationalMode.EmailTriage,
             $"📧 Email Triage{emailOpsLabel}",
             emailOpsEnabled),

            // Bulk Operations - requires Storage + Gmail + training data
            (OperationalMode.BulkOperations,
             $"⚡ Bulk Operations{emailOpsLabel}",
             emailOpsEnabled),

            // Training Data Scan - requires Gmail
            (OperationalMode.TrainData,
             gmailHealthy ? "🤖 Build Training Data" : "🤖 Build Training Data [dim](Requires Gmail)[/]",
             gmailHealthy),

            // Provider Settings - always available
            (OperationalMode.ProviderSettings,
             "⚙️  Provider Settings",
             true),

            // UI Mode - requires Storage + Gmail
            (OperationalMode.UIMode,
             gmailHealthy ? "🖥️  Launch UI Mode" : "🖥️  Launch UI Mode [dim](Requires Gmail)[/]",
             gmailHealthy),

            // Exit always available
            (OperationalMode.Exit,
             "🚪 Exit Application",
             true)
        };

        return modes;
    }

    /// <summary>
    /// Displays interactive mode selection prompt using Spectre.Console.
    /// </summary>
    /// <param name="availableModes">List of modes with availability status.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>Selected operational mode.</returns>
    private async Task<OperationalMode> PromptForModeAsync(
        List<(OperationalMode Mode, string DisplayText, bool Enabled)> availableModes,
        CancellationToken cancellationToken)
    {
        await Task.CompletedTask; // Method is sync but signature allows for future async

        AnsiConsole.Write(new Rule("[blue]Main Menu[/]"));
        AnsiConsole.WriteLine();

        // Create selection prompt with only enabled modes
        var enabledModes = availableModes.Where(m => m.Enabled).ToList();

        var selection = AnsiConsole.Prompt(
            new SelectionPrompt<(OperationalMode Mode, string DisplayText, bool Enabled)>()
                .Title("[cyan]Select an operational mode:[/]")
                .PageSize(10)
                .MoreChoicesText("[grey](Move up and down to reveal more options)[/grey]")
                .AddChoices(enabledModes)
                .UseConverter(m => m.DisplayText)
        );

        AnsiConsole.WriteLine();

        return selection.Mode;
    }
}
