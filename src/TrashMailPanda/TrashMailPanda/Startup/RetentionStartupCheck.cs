using System;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Spectre.Console;
using TrashMailPanda.Services;
using TrashMailPanda.Shared.Base;

namespace TrashMailPanda.Startup;

/// <summary>
/// Startup step that checks whether a retention scan should be prompted and,
/// if the user confirms, runs the scan and displays a summary.
/// </summary>
public sealed class RetentionStartupCheck
{
    private readonly IRetentionEnforcementService _retentionService;
    private readonly Func<bool> _confirmAction;
    private readonly ILogger<RetentionStartupCheck>? _logger;

    /// <param name="retentionService">The retention enforcement service.</param>
    /// <param name="confirmAction">
    /// Optional override for the confirmation prompt (used for unit testing).
    /// When null, the default Spectre.Console interactive prompt is used.
    /// </param>
    /// <param name="logger">Optional logger.</param>
    public RetentionStartupCheck(
        IRetentionEnforcementService retentionService,
        Func<bool>? confirmAction = null,
        ILogger<RetentionStartupCheck>? logger = null)
    {
        _retentionService = retentionService ?? throw new ArgumentNullException(nameof(retentionService));
        _confirmAction = confirmAction ?? PromptUserConfirmation;
        _logger = logger;
    }

    /// <summary>
    /// Checks whether a retention scan should be prompted.
    /// If yes, displays the interactive prompt; if the user confirms, runs the scan.
    /// </summary>
    public async Task<Result<bool>> RunAsync(CancellationToken cancellationToken = default)
    {
        var shouldPromptResult = await _retentionService.ShouldPromptAsync(cancellationToken);
        if (!shouldPromptResult.IsSuccess)
        {
            _logger?.LogWarning("Could not determine retention scan status: {Error}",
                shouldPromptResult.Error.Message);
            return Result<bool>.Success(false);
        }

        if (!shouldPromptResult.Value)
            return Result<bool>.Success(false);

        // Show prompt
        var lastScanResult = await _retentionService.GetLastScanTimeAsync(cancellationToken);
        var daysSinceLast = lastScanResult.IsSuccess && lastScanResult.Value.HasValue
            ? (int)(DateTime.UtcNow - lastScanResult.Value.Value).TotalDays
            : (int?)null;

        AnsiConsole.WriteLine();
        if (daysSinceLast.HasValue)
        {
            AnsiConsole.MarkupLine(
                $"[yellow]⚠  Retention scan last ran {daysSinceLast} day(s) ago.[/]");
        }
        else
        {
            AnsiConsole.MarkupLine("[yellow]⚠  No retention scan has ever run.[/]");
        }

        AnsiConsole.MarkupLine(
            "[cyan]→[/] Scan archived emails and delete any past their retention window? [[Y/n]]");

        var userConfirmed = _confirmAction();
        if (!userConfirmed)
        {
            AnsiConsole.MarkupLine("[dim]Retention scan skipped.[/]");
            return Result<bool>.Success(false);
        }

        // Run the scan
        AnsiConsole.MarkupLine("[cyan]→[/] Running retention scan...");

        var scanResult = await _retentionService.RunScanAsync(cancellationToken);
        if (!scanResult.IsSuccess)
        {
            AnsiConsole.MarkupLine(
                $"[bold red]✗[/] [red]Retention scan failed: {Markup.Escape(scanResult.Error.Message)}[/]");
            return Result<bool>.Success(false);
        }

        var scan = scanResult.Value;
        AnsiConsole.MarkupLine(
            $"[green]✓[/] Retention scan complete: " +
            $"[green]{scan.DeletedCount} deleted[/], " +
            $"[dim]{scan.SkippedCount} not yet expired[/]" +
            (scan.HasFailures ? $", [red]{scan.FailedIds.Count} failed[/]" : string.Empty));

        AnsiConsole.WriteLine();
        return Result<bool>.Success(true);
    }

    private static bool PromptUserConfirmation()
    {
        var key = Console.ReadKey(intercept: true);
        return key.Key is ConsoleKey.Enter ||
               key.KeyChar is 'Y' or 'y';
    }
}
