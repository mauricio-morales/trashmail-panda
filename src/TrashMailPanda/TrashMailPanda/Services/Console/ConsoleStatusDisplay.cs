using Spectre.Console;
using TrashMailPanda.Models.Console;
using TrashMailPanda.Shared.Base;

namespace TrashMailPanda.Services.Console;

/// <summary>
/// Service for displaying provider status and messages using Spectre.Console.
/// Implements color-coded status indicators and formatted output.
/// </summary>
public class ConsoleStatusDisplay
{
    private readonly ConsoleDisplayOptions _options;

    /// <summary>
    /// Initializes a new instance of the <see cref="ConsoleStatusDisplay"/> class.
    /// </summary>
    /// <param name="options">Console display options for rendering control.</param>
    public ConsoleStatusDisplay(ConsoleDisplayOptions options)
    {
        _options = options ?? throw new ArgumentNullException(nameof(options));
    }

    /// <summary>
    /// Displays the welcome banner with application name and version.
    /// </summary>
    public void DisplayWelcomeBanner()
    {
        var rule = new Rule("[bold blue]🦝 TrashMail Panda v1.0.0[/]")
        {
            Justification = Justify.Center
        };
        AnsiConsole.Write(rule);

        AnsiConsole.MarkupLine("[dim]AI-Powered Email Triage Assistant[/]");
        AnsiConsole.WriteLine();
    }

    /// <summary>
    /// Displays a message indicating a provider is starting initialization.
    /// </summary>
    /// <param name="state">The provider initialization state.</param>
    public void DisplayProviderInitializing(ProviderInitializationState state)
    {
        var timestamp = _options.ShowTimestamps ? $"[dim]{DateTime.Now:HH:mm:ss}[/] " : "";
        AnsiConsole.MarkupLine($"{timestamp}[blue]●[/] Initializing [bold]{state.ProviderName}[/] provider...");

        if (!string.IsNullOrWhiteSpace(state.StatusMessage))
        {
            AnsiConsole.MarkupLine($"    [dim]{state.StatusMessage}[/]");
        }
    }

    /// <summary>
    /// Displays a success message when a provider initialization completes.
    /// </summary>
    /// <param name="state">The provider initialization state.</param>
    public void DisplayProviderSuccess(ProviderInitializationState state)
    {
        var timestamp = _options.ShowTimestamps ? $"[dim]{DateTime.Now:HH:mm:ss}[/] " : "";
        var duration = _options.ShowDuration && state.Duration.HasValue
            ? $" ({state.Duration.Value.TotalSeconds:F1}s)"
            : "";

        AnsiConsole.MarkupLine($"{timestamp}[green]✓[/] [bold]{state.ProviderName}[/] provider initialized successfully{duration}");
    }

    /// <summary>
    /// Displays an error message when a provider initialization fails.
    /// </summary>
    /// <param name="state">The provider initialization state.</param>
    public void DisplayProviderFailed(ProviderInitializationState state)
    {
        var timestamp = _options.ShowTimestamps ? $"[dim]{DateTime.Now:HH:mm:ss}[/] " : "";
        AnsiConsole.MarkupLine($"{timestamp}[bold red]✗[/] [bold]{state.ProviderName}[/] provider initialization failed");

        if (state.Error != null)
        {
            DisplayProviderError(state);
        }
    }

    /// <summary>
    /// Displays detailed error information based on configured detail level.
    /// </summary>
    /// <param name="state">The provider initialization state with error details.</param>
    public void DisplayProviderError(ProviderInitializationState state)
    {
        if (state.Error == null)
        {
            return;
        }

        var error = state.Error;

        // Minimal: Just the message
        if (_options.ErrorDetailLevel == ErrorDetailLevel.Minimal)
        {
            AnsiConsole.MarkupLine($"    [red]{error.Message}[/]");
            return;
        }

        // Standard: Message + category + code
        AnsiConsole.WriteLine();
        AnsiConsole.MarkupLine($"  [bold]Error Category:[/] {error.Category}");
        AnsiConsole.MarkupLine($"  [bold]Error Code:[/] {error.ErrorCode}");
        AnsiConsole.MarkupLine($"  [bold]Message:[/] [red]{error.Message}[/]");

        // Handle scope mismatch errors specifically (check context)
        if (error.ErrorCode == "INSUFFICIENT_SCOPES" && error.Context.ContainsKey("MissingScopes"))
        {
            var missingScopes = error.Context["MissingScopes"] as string[];
            if (missingScopes?.Length > 0)
            {
                DisplayScopeMismatch(missingScopes);
            }
        }

        // Verbose: Full exception with stack trace
        if (_options.ErrorDetailLevel == ErrorDetailLevel.Verbose && error.InnerException != null)
        {
            AnsiConsole.WriteLine();
            AnsiConsole.MarkupLine("  [dim]Exception Details:[/]");
            AnsiConsole.WriteException(error.InnerException);
        }

        AnsiConsole.WriteLine();
    }

    /// <summary>
    /// Displays OAuth scope mismatch warning with missing scopes.
    /// </summary>
    /// <param name="missingScopes">Array of missing OAuth scopes.</param>
    public void DisplayScopeMismatch(string[] missingScopes)
    {
        if (missingScopes == null || missingScopes.Length == 0)
        {
            return;
        }

        AnsiConsole.WriteLine();
        AnsiConsole.MarkupLine("  [yellow]⚠ OAuth Scope Mismatch Detected[/]");
        AnsiConsole.MarkupLine("  Your token is valid but missing required permissions:");
        AnsiConsole.WriteLine();

        foreach (var scope in missingScopes)
        {
            AnsiConsole.MarkupLine($"    [red]✗[/] {scope}");
        }

        AnsiConsole.WriteLine();
        AnsiConsole.MarkupLine("  [blue]ℹ[/] [dim]You must re-authorize to grant additional permissions.[/]");
    }

    /// <summary>
    /// Displays health check status after provider initialization.
    /// </summary>
    /// <param name="state">The provider initialization state with health status.</param>
    public void DisplayHealthCheckStatus(ProviderInitializationState state)
    {
        var timestamp = _options.ShowTimestamps ? $"[dim]{DateTime.Now:HH:mm:ss}[/] " : "";

        var status = state.HealthStatus switch
        {
            Models.Console.HealthStatus.Healthy => "[green]✓ Healthy[/]",
            Models.Console.HealthStatus.Degraded => "[yellow]⚠ Degraded[/]",
            Models.Console.HealthStatus.Critical => "[bold red]✗ Critical[/]",
            _ => "[dim]? Unknown[/]"
        };

        AnsiConsole.MarkupLine($"{timestamp}{status} - [bold]{state.ProviderName}[/] provider");
    }

    /// <summary>
    /// Displays the final startup summary.
    /// </summary>
    /// <param name="sequenceState">The overall startup sequence state.</param>
    public void DisplayStartupSummary(StartupSequenceState sequenceState)
    {
        AnsiConsole.WriteLine();
        var rule = new Rule();
        AnsiConsole.Write(rule);

        if (sequenceState.OverallStatus == SequenceStatus.Completed)
        {
            var duration = sequenceState.TotalDuration?.TotalSeconds.ToString("F1") ?? "N/A";
            AnsiConsole.MarkupLine($"[green]✓[/] All providers initialized successfully ([bold]{duration}s[/] total)");
        }
        else if (sequenceState.OverallStatus == SequenceStatus.Failed)
        {
            AnsiConsole.MarkupLine($"[bold red]✗[/] Startup sequence failed - required provider(s) unavailable");
        }
        else if (sequenceState.OverallStatus == SequenceStatus.Cancelled)
        {
            AnsiConsole.MarkupLine($"[yellow]⚠[/] Startup cancelled by user");
        }

        AnsiConsole.Write(new Rule());
        AnsiConsole.WriteLine();
    }

    /// <summary>
    /// Displays error recovery menu with options.
    /// </summary>
    /// <param name="providerName">Name of the failed provider.</param>
    /// <returns>Selected recovery option.</returns>
    public string DisplayErrorRecoveryMenu(string providerName)
    {
        AnsiConsole.WriteLine();
        AnsiConsole.MarkupLine($"[bold]What would you like to do?[/]");

        var selection = AnsiConsole.Prompt(
            new SelectionPrompt<string>()
                .PageSize(10)
                .AddChoices(new[]
                {
                    $"Reconfigure {providerName} Provider",
                    "Retry Initialization",
                    "Exit Application"
                }));

        return selection;
    }

    /// <summary>
    /// Displays a countdown timer during timeout scenarios.
    /// </summary>
    /// <param name="message">The status message.</param>
    /// <param name="remainingSeconds">Seconds remaining before timeout.</param>
    public void DisplayTimeoutWarning(string message, int remainingSeconds)
    {
        var timestamp = _options.ShowTimestamps ? $"[dim]{DateTime.Now:HH:mm:ss}[/] " : "";
        AnsiConsole.MarkupLine($"{timestamp}[yellow]⚠[/] {message} ([bold]{remainingSeconds}s[/] remaining)");
    }

    /// <summary>
    /// Gets provider-specific error message and recovery hint for common error codes.
    /// </summary>
    /// <param name="errorCode">The error code from the provider error.</param>
    /// <param name="providerName">Name of the failed provider.</param>
    /// <returns>Tuple of (error message, recovery hint).</returns>
    public (string Message, string RecoveryHint) GetProviderSpecificErrorMessage(string errorCode, string providerName)
    {
        return errorCode switch
        {
            "AUTH_ERROR" or "AUTH_TOKEN_EXPIRED" =>
                ($"Authentication failed for {providerName}",
                 "Your credentials may have expired. Try reconfiguring the provider."),

            "INSUFFICIENT_SCOPES" =>
                ($"OAuth permissions missing for {providerName}",
                 "Your token is missing required permissions. You must re-authorize to grant additional access."),

            "DB_LOCKED" or "STORAGE_ERROR" =>
                ($"Database access error for {providerName}",
                 "The database may be locked by another process. Close other instances and retry."),

            "NET_ERROR" or "NETWORK_ERROR" =>
                ($"Network connectivity issue with {providerName}",
                 "Check your internet connection and firewall settings, then retry."),

            "TIMEOUT" =>
                ($"{providerName} operation timed out",
                 "The operation took too long to complete. This may be a temporary issue - try again."),

            "CONFIG_ERROR" or "VALIDATION_ERROR" =>
                ($"Configuration error for {providerName}",
                 "Your provider settings may be invalid. Try reconfiguring with correct values."),

            "QUOTA_EXCEEDED" =>
                ($"API quota exceeded for {providerName}",
                 "You've exceeded your API usage limits. Wait for quota reset or upgrade your plan."),

            _ =>
                ($"Unknown error with {providerName}",
                 "An unexpected error occurred. Check logs for details or try reconfiguring.")
        };
    }
}
