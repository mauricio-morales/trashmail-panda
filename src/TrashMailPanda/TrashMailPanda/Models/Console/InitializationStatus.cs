namespace TrashMailPanda.Models.Console;

/// <summary>
/// Represents the current state of a provider during initialization.
/// </summary>
public enum InitializationStatus
{
    /// <summary>
    /// Provider has not started initialization.
    /// </summary>
    NotStarted,

    /// <summary>
    /// Provider's InitializeAsync() method is currently executing.
    /// </summary>
    Initializing,

    /// <summary>
    /// Provider's HealthCheckAsync() method is currently executing.
    /// </summary>
    HealthChecking,

    /// <summary>
    /// Provider initialized successfully and passed health check.
    /// </summary>
    Ready,

    /// <summary>
    /// Provider initialization or health check failed.
    /// </summary>
    Failed,

    /// <summary>
    /// Provider initialization or health check exceeded timeout limit.
    /// </summary>
    Timeout
}
