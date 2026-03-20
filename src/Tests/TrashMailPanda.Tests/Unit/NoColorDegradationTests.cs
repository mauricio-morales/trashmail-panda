using System.IO;
using Spectre.Console;
using Xunit;

namespace TrashMailPanda.Tests.Unit;

/// <summary>
/// Verifies that Spectre.Console degrades gracefully when color output is disabled,
/// producing no raw ANSI escape codes in the output (NO_COLOR behaviour).
/// </summary>
[Trait("Category", "Unit")]
public sealed class NoColorDegradationTests
{
    /// <summary>
    /// Creates a console with ANSI support disabled (equivalent to the NO_COLOR env var).
    /// Verifies that MarkupLine output contains no ANSI escape sequences.
    /// </summary>
    [Fact]
    public void MarkupLine_WithAnsiDisabled_ContainsNoColorEscapeCodes()
    {
        // Arrange
        var writer = new StringWriter();
        var console = AnsiConsole.Create(new AnsiConsoleSettings
        {
            Ansi = AnsiSupport.No,
            ColorSystem = ColorSystemSupport.NoColors,
            Out = new AnsiConsoleOutput(writer),
        });

        // Act — write a colorful message
        console.MarkupLine("[green]✓ Success[/]");
        console.MarkupLine("[bold red]✗ Error[/]");
        console.MarkupLine("[yellow]⚠ Warning[/]");
        console.MarkupLine("[cyan]→ Action[/]");
        console.MarkupLine("[dim]secondary text[/]");

        // Assert — no ANSI color code sequences (ESC + "[" + number + "m")
        var output = writer.ToString();
        Assert.DoesNotContain("\x1B[32m", output); // green
        Assert.DoesNotContain("\x1B[31m", output); // red
        Assert.DoesNotContain("\x1B[33m", output); // yellow
        Assert.DoesNotContain("\x1B[36m", output); // cyan
        Assert.DoesNotContain("\x1B[0m", output);  // reset
    }

    [Fact]
    public void MarkupLine_WithAnsiDisabled_PlainTextPreserved()
    {
        // Arrange
        var writer = new StringWriter();
        var console = AnsiConsole.Create(new AnsiConsoleSettings
        {
            Ansi = AnsiSupport.No,
            ColorSystem = ColorSystemSupport.NoColors,
            Out = new AnsiConsoleOutput(writer),
        });

        // Act
        console.MarkupLine("[green]hello world[/]");

        // Assert — content still present even without ANSI codes
        var output = writer.ToString();
        Assert.Contains("hello world", output);
    }

    [Fact]
    public void MarkupLine_WithAnsiEnabled_RendersText()
    {
        // Arrange — verify the enabled path doesn't throw (ANSI codes exist in real output)
        var writer = new StringWriter();
        var console = AnsiConsole.Create(new AnsiConsoleSettings
        {
            Ansi = AnsiSupport.Yes,
            ColorSystem = ColorSystemSupport.Detect,
            Out = new AnsiConsoleOutput(writer),
        });

        // Act & Assert — should not throw
        console.MarkupLine("[green]✓[/] output works");
        var output = writer.ToString();
        Assert.Contains("output works", output);
    }
}
