using Microsoft.Extensions.Logging;
using Spectre.Console;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using TrashMailPanda.Models.Console;
using TrashMailPanda.Providers.Storage.Models;
using TrashMailPanda.Shared.Base;

namespace TrashMailPanda.Services.Console;

/// <summary>
/// TUI presenter for the Bulk Operations workflow.
/// Criteria wizard → preview → confirm → execute.
/// Delegates all business logic to <see cref="IBulkOperationService"/>.
/// </summary>
public sealed class BulkOperationConsoleService : IBulkOperationConsoleService
{
    private const int MaxPreviewRows = 10;

    private readonly IBulkOperationService _bulkService;
    private readonly IConsoleHelpPanel? _helpPanel;
    private readonly ILogger<BulkOperationConsoleService> _logger;
    private readonly IAnsiConsole _console;
    private readonly Func<ConsoleKeyInfo> _readKey;
    private readonly Func<string?> _readLine;

    public BulkOperationConsoleService(
        IBulkOperationService bulkService,
        ILogger<BulkOperationConsoleService> logger,
        IAnsiConsole? console = null,
        Func<ConsoleKeyInfo>? readKey = null,
        Func<string?>? readLine = null,
        IConsoleHelpPanel? helpPanel = null)
    {
        _bulkService = bulkService ?? throw new ArgumentNullException(nameof(bulkService));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _console = console ?? AnsiConsole.Console;
        _readKey = readKey ?? (() => System.Console.ReadKey(intercept: true));
        _readLine = readLine ?? (() => System.Console.ReadLine());
        _helpPanel = helpPanel;
    }

    /// <inheritdoc />
    public async Task<Result<bool>> RunAsync(CancellationToken cancellationToken = default)
    {
        _logger.LogInformation("Starting Bulk Operations mode");

        while (!cancellationToken.IsCancellationRequested)
        {
            // Step 1: Build criteria
            var criteria = BuildCriteria();

            if (criteria == null)
            {
                // User chose to exit
                return Result<bool>.Success(true);
            }

            // Step 2: Preview
            _console.WriteLine();
            _console.MarkupLine($"{ConsoleColors.Info}ℹ Searching for matching emails...{ConsoleColors.Close}");

            var previewResult = await _bulkService.PreviewAsync(criteria, cancellationToken);

            if (!previewResult.IsSuccess)
            {
                _console.MarkupLine($"{ConsoleColors.Error}✗{ConsoleColors.Close} {ConsoleColors.ErrorText}Preview failed: {Markup.Escape(previewResult.Error?.Message ?? "unknown error")}{ConsoleColors.Close}");
                WaitForKey();
                continue;
            }

            var matches = previewResult.Value;

            if (matches.Count == 0)
            {
                _console.MarkupLine($"{ConsoleColors.Warning}⚠ No emails match the specified criteria.{ConsoleColors.Close}");
                WaitForKey();
                continue;
            }

            // Step 3: Show preview
            RenderPreview(matches);

            // Step 4: Prompt for action
            var action = PromptAction();

            if (action == null)
            {
                // User cancelled
                continue;
            }

            // Step 5: Require confirmation before executing
            _console.WriteLine();
            _console.MarkupLine($"{ConsoleColors.PromptOption}Execute '{Markup.Escape(action)}' on {matches.Count} email(s)? {ConsoleColors.Close}");
            _console.MarkupLine($"{ConsoleColors.ActionHint}[[Y / Enter]]{ConsoleColors.Close} Confirm  {ConsoleColors.Dim}[[N / Esc]]{ConsoleColors.Close} Cancel");

            var confirm = _readKey();

            if (confirm.Key is ConsoleKey.Escape or ConsoleKey.N ||
                confirm.KeyChar is 'N' or 'n')
            {
                _console.MarkupLine($"{ConsoleColors.Warning}⚠ Operation cancelled.{ConsoleColors.Close}");
                continue;
            }

            if (confirm.Key is not ConsoleKey.Enter &&
                confirm.KeyChar is not 'Y' and not 'y')
            {
                continue;
            }

            // Step 6: Execute
            var emailIds = matches.Select(m => m.EmailId).ToList();
            var execResult = await _bulkService.ExecuteAsync(emailIds, action, cancellationToken);

            if (!execResult.IsSuccess)
            {
                _console.MarkupLine($"{ConsoleColors.Error}✗{ConsoleColors.Close} {ConsoleColors.ErrorText}Execution failed: {Markup.Escape(execResult.Error?.Message ?? "unknown error")}{ConsoleColors.Close}");
            }
            else
            {
                var summary = execResult.Value;
                _console.MarkupLine($"{ConsoleColors.Success}✓ {summary.SuccessCount} email(s) processed successfully.{ConsoleColors.Close}");

                if (summary.FailedIds.Count > 0)
                {
                    _console.MarkupLine($"{ConsoleColors.Error}✗{ConsoleColors.Close} {ConsoleColors.ErrorText}{summary.FailedIds.Count} email(s) failed:{ConsoleColors.Close}");
                    foreach (var failedId in summary.FailedIds.Take(10))
                    {
                        _console.MarkupLine($"  {ConsoleColors.ErrorText}{Markup.Escape(failedId)}{ConsoleColors.Close}");
                    }
                }
            }

            WaitForKey();
        }

        return Result<bool>.Success(true);
    }

    // ── Criteria builder ──────────────────────────────────────────────────────

    private BulkOperationCriteria? BuildCriteria()
    {
        _console.WriteLine();
        _console.Write(new Rule($"{ConsoleColors.Highlight}⚡ Bulk Operations — Filter Criteria{ConsoleColors.Close}"));
        _console.WriteLine();
        _console.MarkupLine($"{ConsoleColors.Dim}Leave blank to skip a filter. Press Esc on any step to exit.{ConsoleColors.Close}");
        _console.WriteLine();

        // Sender filter
        _console.MarkupLine($"{ConsoleColors.Info}Sender domain / email (e.g. newsletter.com):{ConsoleColors.Close}");
        var senderInput = _readLine()?.Trim();
        if (senderInput == null) return null; // EOF / cancelled

        // Date from
        _console.MarkupLine($"{ConsoleColors.Info}Emails received after (YYYY-MM-DD, blank to skip):{ConsoleColors.Close}");
        var dateFromInput = _readLine()?.Trim();
        DateTime? dateFrom = null;
        if (!string.IsNullOrEmpty(dateFromInput) &&
            DateTime.TryParse(dateFromInput, out var parsedFrom))
            dateFrom = parsedFrom;

        // Date to
        _console.MarkupLine($"{ConsoleColors.Info}Emails received before (YYYY-MM-DD, blank to skip):{ConsoleColors.Close}");
        var dateToInput = _readLine()?.Trim();
        DateTime? dateTo = null;
        if (!string.IsNullOrEmpty(dateToInput) &&
            DateTime.TryParse(dateToInput, out var parsedTo))
            dateTo = parsedTo;

        return new BulkOperationCriteria
        {
            Sender = string.IsNullOrWhiteSpace(senderInput) ? null : senderInput,
            DateFrom = dateFrom,
            DateTo = dateTo,
        };
    }

    // ── Preview rendering ─────────────────────────────────────────────────────

    private void RenderPreview(IReadOnlyList<EmailFeatureVector> matches)
    {
        _console.WriteLine();
        _console.MarkupLine($"{ConsoleColors.Info}ℹ Found {ConsoleColors.Close}{ConsoleColors.Metric}{matches.Count}{ConsoleColors.Close}{ConsoleColors.Info} matching email(s).{ConsoleColors.Close}");
        _console.WriteLine();

        var table = new Table()
            .Border(TableBorder.Rounded)
            .AddColumn(new TableColumn($"{ConsoleColors.Info}#  {ConsoleColors.Close}").RightAligned())
            .AddColumn(new TableColumn($"{ConsoleColors.Info}Email ID{ConsoleColors.Close}").LeftAligned())
            .AddColumn(new TableColumn($"{ConsoleColors.Info}Sender Domain{ConsoleColors.Close}").LeftAligned())
            .AddColumn(new TableColumn($"{ConsoleColors.Info}Age (days){ConsoleColors.Close}").RightAligned());

        var rowsToShow = matches.Take(MaxPreviewRows).ToList();
        for (int i = 0; i < rowsToShow.Count; i++)
        {
            var v = rowsToShow[i];
            table.AddRow(
                $"{ConsoleColors.Dim}{i + 1}{ConsoleColors.Close}",
                Markup.Escape(TruncateId(v.EmailId)),
                Markup.Escape(v.SenderDomain),
                $"{ConsoleColors.Metric}{v.EmailAgeDays}{ConsoleColors.Close}");
        }

        _console.Write(table);

        if (matches.Count > MaxPreviewRows)
        {
            _console.MarkupLine($"{ConsoleColors.Dim}… and {matches.Count - MaxPreviewRows} more.{ConsoleColors.Close}");
        }
    }

    // ── Action prompt ─────────────────────────────────────────────────────────

    private string? PromptAction()
    {
        _console.WriteLine();
        _console.Write(new Rule($"{ConsoleColors.Highlight}Select Action{ConsoleColors.Close}"));
        _console.MarkupLine($"{ConsoleColors.ActionHint}[[K]]{ConsoleColors.Close} Keep   {ConsoleColors.ActionHint}[[A]]{ConsoleColors.Close} Archive   {ConsoleColors.ActionHint}[[D]]{ConsoleColors.Close} Delete   {ConsoleColors.ActionHint}[[S]]{ConsoleColors.Close} Spam   {ConsoleColors.Dim}[[Esc / Q]]{ConsoleColors.Close} Cancel   {ConsoleColors.ActionHint}[[?]]{ConsoleColors.Close} Help");

        while (true)
        {
            var key = _readKey();

            if (key.KeyChar == '?')
            {
                if (_helpPanel != null)
                    _helpPanel.ShowAsync(HelpContext.ForBulkOperations()).GetAwaiter().GetResult();
                _console.MarkupLine($"{ConsoleColors.ActionHint}[[K]]{ConsoleColors.Close} Keep   {ConsoleColors.ActionHint}[[A]]{ConsoleColors.Close} Archive   {ConsoleColors.ActionHint}[[D]]{ConsoleColors.Close} Delete   {ConsoleColors.ActionHint}[[S]]{ConsoleColors.Close} Spam   {ConsoleColors.Dim}[[Esc / Q]]{ConsoleColors.Close} Cancel   {ConsoleColors.ActionHint}[[?]]{ConsoleColors.Close} Help");
                continue;
            }

            return key.Key switch
            {
                ConsoleKey.K => "Keep",
                ConsoleKey.A => "Archive",
                ConsoleKey.D => "Delete",
                ConsoleKey.S => "Spam",
                ConsoleKey.Escape or ConsoleKey.Q => null,
                _ => null,
            };
        }
    }

    // ── Helpers ───────────────────────────────────────────────────────────────

    private void WaitForKey()
    {
        _console.MarkupLine($"{ConsoleColors.Dim}Press any key to continue...{ConsoleColors.Close}");
        _readKey();
    }

    private static string TruncateId(string id) =>
        id.Length <= 20 ? id : id[..17] + "...";
}
