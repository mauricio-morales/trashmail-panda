using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Spectre.Console;
using TrashMailPanda.Providers.Email.Services;
using TrashMailPanda.Providers.Email.Models;
using TrashMailPanda.Providers.Storage;
using TrashMailPanda.Shared.Base;

namespace TrashMailPanda.Services;

/// <summary>
/// Console command that orchestrates a Gmail training data scan
/// and displays live progress via Spectre.Console.
/// </summary>
public sealed class GmailTrainingScanCommand
{
    // Maps Gmail API label IDs (used as keys) to friendly display names
    private static readonly (string LabelId, string DisplayName)[] Folders =
    [
        ("SPAM",    "Spam"),
        ("TRASH",   "Trash"),
        ("SENT",    "Sent"),
        ("ARCHIVE", "Archive"),
        ("INBOX",   "Inbox"),
    ];

    // Widest display name length — used to pad descriptions so columns stay aligned
    private static readonly int NameWidth = "Archive".Length; // 7

    private readonly IGmailTrainingDataService _trainingDataService;
    private readonly ITrainingEmailRepository _trainingEmailRepo;

    public GmailTrainingScanCommand(
        IGmailTrainingDataService trainingDataService,
        ITrainingEmailRepository trainingEmailRepo)
    {
        _trainingDataService = trainingDataService ?? throw new ArgumentNullException(nameof(trainingDataService));
        _trainingEmailRepo = trainingEmailRepo ?? throw new ArgumentNullException(nameof(trainingEmailRepo));
    }

    /// <summary>
    /// Runs the initial training scan with a live Spectre.Console progress display.
    /// </summary>
    public async Task RunInitialScanAsync(string accountId, CancellationToken cancellationToken = default)
    {
        var existingCount = await _trainingEmailRepo.CountAsync(accountId, cancellationToken);

        AnsiConsole.MarkupLine("[blue]ℹ Starting Gmail training data scan...[/]");

        if (existingCount > 0)
            AnsiConsole.MarkupLine($"[dim]  Already in database:[/] [white]{existingCount:N0}[/] [dim]training emails[/]");
        else
            AnsiConsole.MarkupLine("[dim]  No emails loaded yet — this is your first scan[/]");

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
                new ElapsedTimeColumn(),
                new SpinnerColumn())
            .StartAsync(async ctx =>
            {
                // One indeterminate task per folder; keyed by label ID, described with friendly name
                var tasks = new Dictionary<string, ProgressTask>(StringComparer.OrdinalIgnoreCase);
                foreach (var (labelId, displayName) in Folders)
                {
                    var desc = PendingDesc(displayName);
                    tasks[labelId] = ctx.AddTask(desc, maxValue: 100, autoStart: false);
                }

                // Start first folder immediately
                tasks[Folders[0].LabelId].StartTask();
                tasks[Folders[0].LabelId].IsIndeterminate = true;
                tasks[Folders[0].LabelId].Description = ActiveDesc(Folders[0].DisplayName, 0);

                var progressReporter = new Progress<ScanProgressUpdate>(update =>
                {
                    // Find matching entry by label ID (update.FolderName is the label ID from the service)
                    var entry = Array.Find(Folders, f =>
                        string.Equals(f.LabelId, update.FolderName, StringComparison.OrdinalIgnoreCase));
                    if (entry == default) return;

                    if (!tasks.TryGetValue(entry.LabelId, out var task)) return;

                    if (!task.IsStarted)
                    {
                        task.StartTask();
                        task.IsIndeterminate = true;
                    }

                    if (update.FolderCompleted)
                    {
                        task.Description = DoneDesc(entry.DisplayName, update.EmailsProcessedInFolder);
                        task.IsIndeterminate = false;
                        task.Value = 100;
                        task.StopTask();
                        task.StopTask();

                        // Pre-start next folder
                        var idx = Array.FindIndex(Folders, f => f.LabelId == entry.LabelId);
                        if (idx >= 0 && idx + 1 < Folders.Length)
                        {
                            var next = tasks[Folders[idx + 1].LabelId];
                            if (!next.IsStarted)
                            {
                                next.StartTask();
                                next.IsIndeterminate = true;
                                next.Description = ActiveDesc(Folders[idx + 1].DisplayName, 0);
                            }
                        }
                    }
                    else
                    {
                        task.Description = ActiveDesc(entry.DisplayName, update.EmailsProcessedInFolder);
                    }
                });

                result = await _trainingDataService.RunInitialScanAsync(accountId, cancellationToken, progressReporter);

                // Finalise any tasks that didn't get a completion event
                foreach (var (_, task) in tasks)
                {
                    if (task.IsStarted && !task.IsFinished)
                    {
                        task.IsIndeterminate = false;
                        task.Value = 100;
                    }
                }
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
    // Description helpers — fixed-width strings so the description column never shifts
    // ──────────────────────────────────────────────────────────────────────────────

    private static string PendingDesc(string name) =>
        $"[dim]{name.PadRight(NameWidth)}[/]";

    private static string ActiveDesc(string name, int count) =>
        count == 0
            ? $"[cyan]{name.PadRight(NameWidth)}[/] [dim]scanning...[/]"
            : $"[cyan]{name.PadRight(NameWidth)}[/] [dim]{count,7:N0} saved[/]";

    private static string DoneDesc(string name, int count) =>
        $"[green]{name.PadRight(NameWidth)}[/] [dim]{count,7:N0} emails[/]";

    // ──────────────────────────────────────────────────────────────────────────────
    // Result display
    // ──────────────────────────────────────────────────────────────────────────────

    private static void DisplayScanResult(Result<ScanSummary> result)
    {
        AnsiConsole.WriteLine();

        if (!result.IsSuccess)
        {
            if (result.Error is OperationCancelledError)
            {
                AnsiConsole.MarkupLine("[yellow]⚠ Scan cancelled.[/]");
                AnsiConsole.MarkupLine("[dim]Any emails processed before cancellation have been saved.[/]");
            }
            else if (result.Error is StorageQuotaError)
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
