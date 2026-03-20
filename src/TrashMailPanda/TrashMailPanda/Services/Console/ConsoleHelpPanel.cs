using Microsoft.Extensions.Logging;
using Spectre.Console;
using System;
using System.Threading;
using System.Threading.Tasks;
using TrashMailPanda.Models.Console;

namespace TrashMailPanda.Services.Console;

/// <summary>
/// Context-aware help panel accessible via <c>?</c>/<c>F1</c> from any mode.
/// Renders mode title, optional description, and key bindings as a Spectre.Console Panel.
/// Blocks until <c>?</c>, <c>F1</c>, or <c>Esc</c> pressed.
/// </summary>
public sealed class ConsoleHelpPanel : IConsoleHelpPanel
{
    private readonly IAnsiConsole _console;
    private readonly Func<ConsoleKeyInfo> _readKey;
    private readonly ILogger<ConsoleHelpPanel> _logger;

    public ConsoleHelpPanel(
        ILogger<ConsoleHelpPanel> logger,
        IAnsiConsole? console = null,
        Func<ConsoleKeyInfo>? readKey = null)
    {
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _console = console ?? AnsiConsole.Console;
        _readKey = readKey ?? (() => System.Console.ReadKey(intercept: true));
    }

    /// <inheritdoc />
    public Task ShowAsync(HelpContext context, CancellationToken cancellationToken = default)
    {
        _logger.LogDebug("Showing help panel for mode: {ModeTitle}", context.ModeTitle);

        // Build key-binding table
        var table = new Table()
            .Border(TableBorder.Rounded)
            .AddColumn(new TableColumn($"{ConsoleColors.Highlight}Key{ConsoleColors.Close}").LeftAligned())
            .AddColumn(new TableColumn($"{ConsoleColors.Info}Action{ConsoleColors.Close}").LeftAligned());

        foreach (var binding in context.KeyBindings)
        {
            table.AddRow(
                $"{ConsoleColors.ActionHint}{Markup.Escape(binding.Key)}{ConsoleColors.Close}",
                Markup.Escape(binding.Description));
        }

        // Wrap in a panel
        var panelContent = new Padder(table, new Padding(0, 0));
        var titleMarkup = $"{ConsoleColors.Highlight}{Markup.Escape(context.ModeTitle)}{ConsoleColors.Close}";

        var panel = new Panel(panelContent)
            .Header(titleMarkup, Justify.Center)
            .Border(BoxBorder.Double)
            .Expand();

        _console.WriteLine();
        _console.Write(panel);

        if (context.Description is not null)
        {
            _console.MarkupLine($"{ConsoleColors.Dim}{Markup.Escape(context.Description)}{ConsoleColors.Close}");
        }

        _console.MarkupLine($"{ConsoleColors.Dim}Press [[?]], [[F1]], or [[Esc]] to close help...{ConsoleColors.Close}");
        _console.WriteLine();

        // Block until dismiss key
        while (!cancellationToken.IsCancellationRequested)
        {
            var key = _readKey();

            if (key.Key is ConsoleKey.Escape or ConsoleKey.F1 || key.KeyChar == '?')
                break;
        }

        return Task.CompletedTask;
    }
}
