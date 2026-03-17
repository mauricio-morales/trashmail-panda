namespace TrashMailPanda.Models.Console;

/// <summary>
/// Represents the verbosity level for error messages displayed in the console.
/// </summary>
public enum ErrorDetailLevel
{
    /// <summary>
    /// Display only the error message.
    /// </summary>
    Minimal,

    /// <summary>
    /// Display error message, category, and error code (default).
    /// </summary>
    Standard,

    /// <summary>
    /// Display full exception details including stack trace.
    /// </summary>
    Verbose
}
