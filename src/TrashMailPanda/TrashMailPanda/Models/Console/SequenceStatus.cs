namespace TrashMailPanda.Models.Console;

/// <summary>
/// Represents the overall status of the startup sequence.
/// </summary>
public enum SequenceStatus
{
    /// <summary>
    /// Startup sequence is in progress.
    /// </summary>
    Initializing,

    /// <summary>
    /// All required providers initialized successfully.
    /// </summary>
    Completed,

    /// <summary>
    /// One or more required providers failed initialization.
    /// </summary>
    Failed,

    /// <summary>
    /// User cancelled startup sequence (Ctrl+C).
    /// </summary>
    Cancelled
}
