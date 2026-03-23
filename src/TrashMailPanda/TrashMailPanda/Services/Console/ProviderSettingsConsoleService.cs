using Microsoft.Extensions.Logging;
using Spectre.Console;
using System;
using System.Threading;
using System.Threading.Tasks;
using TrashMailPanda.Models.Console;
using TrashMailPanda.Providers.Storage;
using TrashMailPanda.Providers.Storage.Models;
using TrashMailPanda.Services;
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
    private readonly IAutoApplyService? _autoApplyService;
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
        IConsoleHelpPanel? helpPanel = null,
        IAutoApplyService? autoApplyService = null)
    {
        if (runWizard == null && wizard == null)
            throw new ArgumentNullException(nameof(wizard), "Either wizard or runWizard must be provided.");
        _archiveService = archiveService ?? throw new ArgumentNullException(nameof(archiveService));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _console = console ?? AnsiConsole.Console;
        _readKey = readKey ?? (() => System.Console.ReadKey(intercept: true));
        _runWizard = runWizard ?? (ct => wizard!.RunAsync(ct));
        _helpPanel = helpPanel;
        _autoApplyService = autoApplyService;
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
            _console.MarkupLine($"{ConsoleColors.ActionHint}[[4]]{ConsoleColors.Close} Auto-apply settings");
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
            else if (key.KeyChar == '4')
            {
                await ConfigureAutoApplyAsync(cancellationToken);
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

    // ── Auto-apply settings ──────────────────────────────────────────────────

    private async Task ConfigureAutoApplyAsync(CancellationToken cancellationToken)
    {
        _console.WriteLine();
        _console.Write(new Rule($"{ConsoleColors.Highlight}⚡ Auto-Apply Settings{ConsoleColors.Close}"));
        _console.WriteLine();

        if (_autoApplyService is null)
        {
            _console.MarkupLine($"{ConsoleColors.Warning}⚠ Auto-apply service is not available.{ConsoleColors.Close}");
            return;
        }

        var configResult = await _autoApplyService.GetConfigAsync(cancellationToken);
        if (!configResult.IsSuccess)
        {
            _console.MarkupLine($"{ConsoleColors.Error}✗{ConsoleColors.Close} {ConsoleColors.ErrorText}Could not load auto-apply config: {Markup.Escape(configResult.Error.Message)}{ConsoleColors.Close}");
            return;
        }

        var config = configResult.Value;
        var enabledLabel = config.Enabled
            ? $"{ConsoleColors.Success}enabled{ConsoleColors.Close}"
            : $"{ConsoleColors.Warning}disabled{ConsoleColors.Close}";
        _console.MarkupLine($"  Status:     {enabledLabel}");
        _console.MarkupLine($"  Threshold:  {ConsoleColors.Info}{config.ConfidenceThreshold:P0}{ConsoleColors.Close}  (emails above this confidence are applied automatically)");
        _console.WriteLine();
        _console.MarkupLine($"  {ConsoleColors.ActionHint}T{ConsoleColors.Close}=Toggle on/off  {ConsoleColors.ActionHint}C{ConsoleColors.Close}=Change threshold  {ConsoleColors.ActionHint}Esc/Enter{ConsoleColors.Close}=Back");
        _console.WriteLine();

        var key = _readKey();
        var k = char.ToUpperInvariant(key.KeyChar);

        if (k == 'T')
        {
            config.Enabled = !config.Enabled;
            var saveResult = await _autoApplyService.SaveConfigAsync(config, cancellationToken);
            if (saveResult.IsSuccess)
            {
                var newLabel = config.Enabled ? "enabled" : "disabled";
                _console.MarkupLine(config.Enabled
                    ? $"{ConsoleColors.Success}✓ Auto-apply {newLabel} (threshold {config.ConfidenceThreshold:P0}).{ConsoleColors.Close}"
                    : $"{ConsoleColors.Warning}✓ Auto-apply {newLabel}.{ConsoleColors.Close}");
                _logger.LogInformation("Auto-apply toggled: Enabled={Enabled}", config.Enabled);
            }
            else
            {
                _console.MarkupLine($"{ConsoleColors.Error}✗{ConsoleColors.Close} {ConsoleColors.ErrorText}Failed to save: {Markup.Escape(saveResult.Error.Message)}{ConsoleColors.Close}");
            }
        }
        else if (k == 'C')
        {
            _console.MarkupLine($"{ConsoleColors.Info}Enter confidence threshold as a percentage (e.g. 90 for 90%): {ConsoleColors.Close}");
            var input = System.Console.ReadLine()?.Trim();

            if (string.IsNullOrWhiteSpace(input) ||
                !float.TryParse(input, System.Globalization.NumberStyles.Any,
                    System.Globalization.CultureInfo.InvariantCulture, out var pct) ||
                pct < 50f || pct > 100f)
            {
                _console.MarkupLine($"{ConsoleColors.Error}✗{ConsoleColors.Close} {ConsoleColors.ErrorText}Invalid value — enter a number between 50 and 100.{ConsoleColors.Close}");
                return;
            }

            config.ConfidenceThreshold = pct / 100f;
            var saveResult = await _autoApplyService.SaveConfigAsync(config, cancellationToken);
            if (saveResult.IsSuccess)
            {
                _console.MarkupLine($"{ConsoleColors.Success}✓ Threshold updated to {config.ConfidenceThreshold:P0}.{ConsoleColors.Close}");
                _logger.LogInformation("Auto-apply threshold updated: {Threshold:P0}", config.ConfidenceThreshold);
            }
            else
            {
                _console.MarkupLine($"{ConsoleColors.Error}✗{ConsoleColors.Close} {ConsoleColors.ErrorText}Failed to save: {Markup.Escape(saveResult.Error.Message)}{ConsoleColors.Close}");
            }
        }
    }
}
