namespace TrashMailPanda.Services.Console;

/// <summary>
/// Semantic Spectre.Console markup string constants for consistent coloring across
/// all console output. Use these constants instead of raw markup strings.
/// Zero hardcoded markup strings should exist outside this file.
/// </summary>
public static class ConsoleColors
{
    // ── Semantic tokens ─────────────────────────────────────────────────────

    /// <summary>Bold red prefix for error indicators (✗).</summary>
    public const string Error = "[bold red]";

    /// <summary>Red for error body text.</summary>
    public const string ErrorText = "[red]";

    /// <summary>Green for success confirmations.</summary>
    public const string Success = "[green]";

    /// <summary>Yellow for warnings and advisories.</summary>
    public const string Warning = "[yellow]";

    /// <summary>Blue for informational messages.</summary>
    public const string Info = "[blue]";

    /// <summary>Magenta for training metric values (precision, recall, F1).</summary>
    public const string Metric = "[magenta]";

    /// <summary>Cyan for highlights, prompts, and action hints.</summary>
    public const string Highlight = "[cyan]";

    /// <summary>Cyan for key binding hints (semantic alias for Highlight).</summary>
    public const string ActionHint = "[cyan]";

    /// <summary>Dim for secondary/supporting text.</summary>
    public const string Dim = "[dim]";

    /// <summary>Closes any open markup tag.</summary>
    public const string Close = "[/]";

    // ── Composite helpers ────────────────────────────────────────────────────

    /// <summary>Bold cyan for AI recommendation display.</summary>
    public const string AiRecommendation = "[bold cyan]";

    /// <summary>Bold yellow for threshold prompt options.</summary>
    public const string PromptOption = "[bold yellow]";
}
