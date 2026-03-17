using System;
using System.Threading;
using System.Threading.Tasks;
using Spectre.Console;
using TrashMailPanda.Providers.Email.Services;
using TrashMailPanda.Providers.Email.Models;
using TrashMailPanda.Shared.Base;

namespace TrashMailPanda.Services;

/// <summary>
/// Console command that orchestrates a Gmail training data scan
/// and displays live progress via Spectre.Console.
/// </summary>
public sealed class GmailTrainingScanCommand
{
    private readonly IGmailTrainingDataService _trainingDataService;

    public GmailTrainingScanCommand(IGmailTrainingDataService trainingDataService)
    {
        _trainingDataService = trainingDataService ?? throw new ArgumentNullException(nameof(trainingDataService));
    }

    /// <summary>
    /// Runs the initial training scan with a live Spectre.Console progress display.
    /// </summary>
    public async Task RunInitialScanAsync(string accountId, CancellationToken cancellationToken = default)
    {
        AnsiConsole.MarkupLine("[blue]ℹ Starting Gmail training data scan...[/]");
        AnsiConsole.MarkupLine("[dim]Folders: Spam → Trash → Sent → Archive → Inbox[/]");
        AnsiConsole.WriteLine();

        Result<ScanSummary> result = default;

        await AnsiConsole.Progress()
            .AutoRefresh(true)
            .AutoClear(false)
            .HideCompleted(false)
            .Columns(
                new TaskDescriptionColumn(),
                new ProgressBarColumn(),
                new PercentageColumn(),
                new SpinnerColumn())
            .StartAsync(async ctx =>
            {
                var task = ctx.AddTask("[cyan]Scanning Gmail folders[/]", maxValue: 100);
                task.IsIndeterminate = true;

                result = await _trainingDataService.RunInitialScanAsync(accountId, cancellationToken);

                task.IsIndeterminate = false;
                task.Value = 100;
            });

        DisplayScanResult(result);
    }

    /// <summary>
    /// Runs an incremental scan (History API) with live progress.
    /// </summary>
    public async Task RunIncrementalScanAsync(string accountId, CancellationToken cancellationToken = default)
    {
        AnsiConsole.MarkupLine("[blue]ℹ Running incremental Gmail scan (History API)...[/]");

        Result<ScanSummary> result = default;

        await AnsiConsole.Status()
            .Spinner(Spinner.Known.Dots)
            .StartAsync("[cyan]→ Processing history...[/]", async ctx =>
            {
                result = await _trainingDataService.RunIncrementalScanAsync(accountId, cancellationToken);
            });

        DisplayScanResult(result);
    }

    // ──────────────────────────────────────────────────────────────────────────────
    // Private helpers
    // ──────────────────────────────────────────────────────────────────────────────

    private static void DisplayScanResult(Result<ScanSummary> result)
    {
        AnsiConsole.WriteLine();

        if (!result.IsSuccess)
        {
            if (result.Error is StorageQuotaError)
            {
                AnsiConsole.MarkupLine("[yellow]⚠ Training scan paused — storage quota reached.[/]");
                AnsiConsole.MarkupLine("[dim]Free up space using the cleanup command, then resume the scan.[/]");
            }
            else
            {
                AnsiConsole.MarkupLine($"[bold red]✗ Scan failed:[/] [red]{Markup.Escape(result.Error.Message)}[/]");
            }
            return;
        }

        var s = result.Value;
        AnsiConsole.MarkupLine("[green]✓ Scan complete[/]");
        AnsiConsole.WriteLine();

        var table = new Table()
            .Border(TableBorder.Rounded)
            .AddColumn("Metric")
            .AddColumn(new TableColumn("[bold]Value[/]").RightAligned());

        table.AddRow("Total processed", $"[white]{s.TotalProcessed:N0}[/]");
        table.AddRow("Auto-delete signals", $"[red]{s.AutoDeleteCount:N0}[/]");
        table.AddRow("Auto-archive signals", $"[yellow]{s.AutoArchiveCount:N0}[/]");
        table.AddRow("Low-confidence signals", $"[dim]{s.LowConfidenceCount:N0}[/]");
        table.AddRow("Excluded", $"[dim]{s.ExcludedCount:N0}[/]");
        table.AddRow("Labels imported", $"[cyan]{s.LabelsImported:N0}[/]");
        table.AddRow("Duration", $"[blue]{s.Duration:mm\\:ss}[/]");

        AnsiConsole.Write(table);
    }
}
