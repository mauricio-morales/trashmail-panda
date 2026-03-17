namespace TrashMailPanda.Models.Console;

/// <summary>
/// Configuration options for Spectre.Console rendering.
/// </summary>
public class ConsoleDisplayOptions
{
    /// <summary>
    /// Gets or sets whether to display timestamps for each status message.
    /// Default: true
    /// </summary>
    public bool ShowTimestamps { get; set; } = true;

    /// <summary>
    /// Gets or sets whether to display duration after each provider completion.
    /// Default: true
    /// </summary>
    public bool ShowDuration { get; set; } = true;

    /// <summary>
    /// Gets or sets whether to enable ANSI color codes.
    /// Auto-detects terminal support if true.
    /// Default: true
    /// </summary>
    public bool UseColors { get; set; } = true;

    /// <summary>
    /// Gets or sets how often to update progress spinners.
    /// Must be between 100ms and 1000ms.
    /// Default: 200ms
    /// </summary>
    public TimeSpan StatusRefreshInterval { get; set; } = TimeSpan.FromMilliseconds(200);

    /// <summary>
    /// Gets or sets the verbosity level for error messages.
    /// Default: Standard
    /// </summary>
    public ErrorDetailLevel ErrorDetailLevel { get; set; } = ErrorDetailLevel.Standard;
}
