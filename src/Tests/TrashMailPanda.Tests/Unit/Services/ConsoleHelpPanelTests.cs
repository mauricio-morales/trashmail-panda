using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging.Abstractions;
using Spectre.Console;
using TrashMailPanda.Models.Console;
using TrashMailPanda.Services.Console;
using Xunit;

namespace TrashMailPanda.Tests.Unit.Services;

[Trait("Category", "Unit")]
public class ConsoleHelpPanelTests
{
    private (ConsoleHelpPanel Panel, StringWriter Writer) CreatePanel(
        Func<ConsoleKeyInfo>? readKey = null)
    {
        var writer = new StringWriter();
        var console = AnsiConsole.Create(new AnsiConsoleSettings
        {
            Ansi = AnsiSupport.No,
            ColorSystem = ColorSystemSupport.NoColors,
            Out = new AnsiConsoleOutput(writer),
        });

        // Default to pressing Esc immediately
        readKey ??= () => new ConsoleKeyInfo((char)0, ConsoleKey.Escape, false, false, false);

        var panel = new ConsoleHelpPanel(
            NullLogger<ConsoleHelpPanel>.Instance,
            console,
            readKey);

        return (panel, writer);
    }

    // ── Mode title is rendered ────────────────────────────────────────────────

    [Fact]
    public async Task ShowAsync_RendersModeTitleInPanel()
    {
        var context = HelpContext.ForMainMenu();
        var (panel, writer) = CreatePanel();

        await panel.ShowAsync(context);

        var output = writer.ToString();
        Assert.Contains("Main Menu", output);
    }

    [Fact]
    public async Task ShowAsync_RendersEmailTriageColdStartTitle()
    {
        var context = HelpContext.ForEmailTriage(TriageMode.ColdStart);
        var (panel, writer) = CreatePanel();

        await panel.ShowAsync(context);

        var output = writer.ToString();
        Assert.Contains("Cold Start", output);
    }

    [Fact]
    public async Task ShowAsync_RendersAiAssistedTitle()
    {
        var context = HelpContext.ForEmailTriage(TriageMode.AiAssisted);
        var (panel, writer) = CreatePanel();

        await panel.ShowAsync(context);

        var output = writer.ToString();
        Assert.Contains("AI-Assisted", output);
    }

    // ── Key bindings table rendered ───────────────────────────────────────────

    [Fact]
    public async Task ShowAsync_RendersKeyBindings()
    {
        var context = new HelpContext
        {
            ModeTitle = "Test Mode",
            KeyBindings =
            [
                new KeyBinding("K", "Keep email"),
                new KeyBinding("Q", "Quit"),
            ],
        };
        var (panel, writer) = CreatePanel();

        await panel.ShowAsync(context);

        var output = writer.ToString();
        Assert.Contains("Keep email", output);
        Assert.Contains("Quit", output);
    }

    // ── Optional description rendered ────────────────────────────────────────

    [Fact]
    public async Task ShowAsync_RendersDescription_WhenProvided()
    {
        var context = new HelpContext
        {
            ModeTitle = "Test Mode",
            Description = "This is a test description",
            KeyBindings = [new KeyBinding("Q", "Quit")],
        };
        var (panel, writer) = CreatePanel();

        await panel.ShowAsync(context);

        var output = writer.ToString();
        Assert.Contains("This is a test description", output);
    }

    // ── Dismissal keys ────────────────────────────────────────────────────────

    [Fact]
    public async Task ShowAsync_DismissesOnEscape()
    {
        var context = HelpContext.ForMainMenu();
        int keyPresses = 0;

        var (panel, writer) = CreatePanel(readKey: () =>
        {
            keyPresses++;
            return new ConsoleKeyInfo((char)0, ConsoleKey.Escape, false, false, false);
        });

        await panel.ShowAsync(context);

        Assert.Equal(1, keyPresses);
    }

    [Fact]
    public async Task ShowAsync_DismissesOnF1()
    {
        var context = HelpContext.ForMainMenu();
        int keyPresses = 0;

        var (panel, writer) = CreatePanel(readKey: () =>
        {
            keyPresses++;
            return new ConsoleKeyInfo((char)0, ConsoleKey.F1, false, false, false);
        });

        await panel.ShowAsync(context);

        Assert.Equal(1, keyPresses);
    }

    [Fact]
    public async Task ShowAsync_DismissesOnQuestionMark()
    {
        var context = HelpContext.ForMainMenu();
        int keyPresses = 0;

        var (panel, writer) = CreatePanel(readKey: () =>
        {
            keyPresses++;
            return new ConsoleKeyInfo('?', ConsoleKey.Oem2, false, false, false);
        });

        await panel.ShowAsync(context);

        Assert.Equal(1, keyPresses);
    }

    [Fact]
    public async Task ShowAsync_DismissesViaCancel()
    {
        var context = HelpContext.ForMainMenu();
        using var cts = new CancellationTokenSource();

        var (panel, writer) = CreatePanel(readKey: () =>
        {
            // Trigger cancel on first call, return a non-dismiss key to force the loop to check CT
            cts.Cancel();
            return new ConsoleKeyInfo('Z', ConsoleKey.Z, false, false, false);
        });

        // Should complete without hanging
        await panel.ShowAsync(context, cts.Token);

        // If we reach here, cancellation worked
        Assert.True(true);
    }
}
