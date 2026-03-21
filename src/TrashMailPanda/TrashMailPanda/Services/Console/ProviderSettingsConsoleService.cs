using Microsoft.Extensions.Logging;
using Spectre.Console;
using System;
using System.Threading;
using System.Threading.Tasks;
using TrashMailPanda.Models.Console;
using TrashMailPanda.Providers.Storage;
using TrashMailPanda.Providers.Storage.Models;
using TrashMailPanda.Shared.Base;

namespace TrashMailPanda.Services.Console;

/// <summary>
/// TUI presenter for the Provider Settings workflow.
/// Renders settings menu, delegates Gmail re-auth to ConfigurationWizard,
/// and manages storage limit adjustments.
/// </summary>
public sealed class ProviderSettingsConsoleService : IProviderSettingsConsoleService
{
    private const long BytesPerGb = 1_073_741_824L;

    private readonly IEmailArchiveService _archiveService;
    private readonly ILogger<ProviderSettingsConsoleService> _logger;
    private readonly IAnsiConsole _console;
    private readonly Func<ConsoleKeyInfo> _readKey;
    private readonly Func<CancellationToken, Task<bool>> _runWizard;
    private readonly IConsoleHelpPanel? _helpPanel;

    public ProviderSettingsConsoleService(
        ConfigurationWizard? wizard,
        IEmailArchiveService archiveService,
        ILogger<ProviderSettingsConsoleService> logger,
        IAnsiConsole? console = null,
        Func<ConsoleKeyInfo>? readKey = null,
        Func<CancellationToken, Task<bool>>? runWizard = null,
        IConsoleHelpPanel? helpPanel = null)
    {
        if (runWizard == null && wizard == null)
            throw new ArgumentNullException(nameof(wizard), "Either wizard or runWizard must be provided.");
        _archiveService = archiveService ?? throw new ArgumentNullException(nameof(archiveService));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _console = console ?? AnsiConsole.Console;
        _readKey = readKey ?? (() => System.Console.ReadKey(intercept: true));
        _runWizard = runWizard ?? (ct => wizard!.RunAsync(ct));
        _helpPanel = helpPanel;
    }

    /// <inheritdoc />
    public async Task<Result<bool>> RunAsync(CancellationToken cancellationToken = default)
    {
        _logger.LogInformation("Starting Provider Settings mode");

        while (!cancellationToken.IsCancellationRequested)
        {
            _console.WriteLine();
            _console.Write(new Rule($"{ConsoleColors.Highlight}⚙  Provider Settings{ConsoleColors.Close}"));
            _console.WriteLine();

            _console.MarkupLine($"{ConsoleColors.ActionHint}[[1]]{ConsoleColors.Close} Gmail re-authorization");
            _console.MarkupLine($"{ConsoleColors.ActionHint}[[2]]{ConsoleColors.Close} View storage statistics");
            _console.MarkupLine($"{ConsoleColors.ActionHint}[[3]]{ConsoleColors.Close} Set storage limit");
            _console.MarkupLine($"{ConsoleColors.Dim}[[0 / Q / Esc]]{ConsoleColors.Close} Back to main menu  {ConsoleColors.ActionHint}[[?]]{ConsoleColors.Close} Help");
            _console.WriteLine();
            _console.MarkupLine($"{ConsoleColors.Info}Select option:{ConsoleColors.Close} ");

            var key = _readKey();

            if (key.Key is ConsoleKey.D0 or ConsoleKey.Q or ConsoleKey.Escape ||
                key.KeyChar == '0')
            {
                _logger.LogInformation("User exited Provider Settings");
                return Result<bool>.Success(true);
            }

            if (key.KeyChar == '?')
            {
                if (_helpPanel != null)
                    await _helpPanel.ShowAsync(HelpContext.ForProviderSettings(), cancellationToken);
                continue;
            }

            if (key.KeyChar == '1')
            {
                await RunGmailReauthAsync(cancellationToken);
            }
            else if (key.KeyChar == '2')
            {
                await ShowStorageStatsAsync(cancellationToken);
            }
            else if (key.KeyChar == '3')
            {
                await AdjustStorageLimitAsync(cancellationToken);
            }
        }

        return Result<bool>.Success(true);
    }

    // ── Gmail re-authorization (T051) ────────────────────────────────────────

    private async Task RunGmailReauthAsync(CancellationToken cancellationToken)
    {
        _console.WriteLine();
        _console.Write(new Rule($"{ConsoleColors.Highlight}Gmail Re-authorization{ConsoleColors.Close}"));
        _console.WriteLine();

        _logger.LogInformation("Starting Gmail re-authorization via ConfigurationWizard");

        try
        {
            var success = await _runWizard(cancellationToken);

            if (success)
            {
                _console.MarkupLine($"{ConsoleColors.Success}✓ Gmail authorization completed successfully.{ConsoleColors.Close}");
            }
            else
            {
                _console.MarkupLine($"{ConsoleColors.Error}✗{ConsoleColors.Close} {ConsoleColors.ErrorText}Gmail authorization was cancelled or failed.{ConsoleColors.Close}");
            }
        }
        catch (OperationCanceledException)
        {
            _console.MarkupLine($"{ConsoleColors.Warning}⚠ Authorization cancelled.{ConsoleColors.Close}");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Unexpected error during Gmail re-authorization");
            _console.MarkupLine($"{ConsoleColors.Error}✗{ConsoleColors.Close} {ConsoleColors.ErrorText}Unexpected error: {Markup.Escape(ex.Message)}{ConsoleColors.Close}");
        }
    }

    // ── Storage stats display (T052) ─────────────────────────────────────────

    private async Task ShowStorageStatsAsync(CancellationToken cancellationToken)
    {
        _console.WriteLine();
        _console.Write(new Rule($"{ConsoleColors.Highlight}Storage Statistics{ConsoleColors.Close}"));
        _console.WriteLine();

        var result = await _archiveService.GetStorageUsageAsync(cancellationToken);

        if (!result.IsSuccess)
        {
            _console.MarkupLine($"{ConsoleColors.Error}✗{ConsoleColors.Close} {ConsoleColors.ErrorText}Failed to retrieve storage stats: {Markup.Escape(result.Error?.Message ?? "unknown error")}{ConsoleColors.Close}");
            return;
        }

        var quota = result.Value;
        var usedGb = (double)quota.CurrentBytes / BytesPerGb;
        var limitGb = (double)quota.LimitBytes / BytesPerGb;
        var usagePercent = quota.LimitBytes > 0
            ? (double)quota.CurrentBytes / quota.LimitBytes * 100.0
            : 0.0;

        var table = new Table()
            .Border(TableBorder.Rounded)
            .AddColumn(new TableColumn($"{ConsoleColors.Info}Metric{ConsoleColors.Close}").LeftAligned())
            .AddColumn(new TableColumn($"{ConsoleColors.Info}Value{ConsoleColors.Close}").RightAligned());

        table.AddRow("Storage used",
            $"{ConsoleColors.Metric}{usedGb:F2} GB{ConsoleColors.Close}");
        table.AddRow("Storage limit",
            $"{ConsoleColors.Metric}{limitGb:F2} GB{ConsoleColors.Close}");
        table.AddRow("Usage",
            $"{ConsoleColors.Metric}{usagePercent:F1}%{ConsoleColors.Close}");
        table.AddRow("Email archives",
            $"{ConsoleColors.Metric}{quota.ArchiveCount:N0}{ConsoleColors.Close}");
        table.AddRow("Feature vectors",
            $"{ConsoleColors.Metric}{quota.FeatureCount:N0}{ConsoleColors.Close}");
        table.AddRow("User-corrected",
            $"{ConsoleColors.Metric}{quota.UserCorrectedCount:N0}{ConsoleColors.Close}");

        _console.Write(table);
    }

    // ── Storage limit adjustment (T053) ──────────────────────────────────────

    private async Task AdjustStorageLimitAsync(CancellationToken cancellationToken)
    {
        _console.WriteLine();
        _console.Write(new Rule($"{ConsoleColors.Highlight}Set Storage Limit{ConsoleColors.Close}"));
        _console.WriteLine();

        _console.MarkupLine($"{ConsoleColors.Info}Enter new storage limit in GB (e.g. 50):{ConsoleColors.Close}");

        var input = System.Console.ReadLine()?.Trim();

        if (string.IsNullOrWhiteSpace(input))
        {
            _console.MarkupLine($"{ConsoleColors.Warning}⚠ No value entered — limit unchanged.{ConsoleColors.Close}");
            return;
        }

        if (!double.TryParse(input, System.Globalization.NumberStyles.Any,
                System.Globalization.CultureInfo.InvariantCulture, out var limitGb) || limitGb <= 0)
        {
            _console.MarkupLine($"{ConsoleColors.Error}✗{ConsoleColors.Close} {ConsoleColors.ErrorText}Invalid value '{Markup.Escape(input)}'. Enter a positive number of GB.{ConsoleColors.Close}");
            return;
        }

        var limitBytes = (long)(limitGb * BytesPerGb);
        var updateResult = await _archiveService.UpdateStorageLimitAsync(limitBytes, cancellationToken);

        if (updateResult.IsSuccess && updateResult.Value)
        {
            _console.MarkupLine($"{ConsoleColors.Success}✓ Storage limit updated to {limitGb:F2} GB.{ConsoleColors.Close}");
            _logger.LogInformation("Storage limit updated to {LimitBytes} bytes ({LimitGb:F2} GB)", limitBytes, limitGb);
        }
        else
        {
            var errorMsg = updateResult.IsSuccess ? "Update returned false" : (updateResult.Error?.Message ?? "unknown error");
            _console.MarkupLine($"{ConsoleColors.Error}✗{ConsoleColors.Close} {ConsoleColors.ErrorText}Failed to update storage limit: {Markup.Escape(errorMsg)}{ConsoleColors.Close}");
            _logger.LogError("Failed to update storage limit: {Error}", errorMsg);
        }
    }
}
