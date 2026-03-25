namespace TrashMailPanda.Models.Console;

/// <summary>
/// Represents available operational modes after successful startup.
/// </summary>
public enum OperationalMode
{
    /// <summary>
    /// Main email triage operations (requires Storage and Gmail).
    /// </summary>
    EmailTriage,

    /// <summary>
    /// Reconfigure provider settings (requires Storage).
    /// </summary>
    ProviderSettings,

    /// <summary>
    /// Train the ML action classifier using the collected email feature data.
    /// </summary>
    TrainModel,

    /// <summary>
    /// Exit the application.
    /// </summary>
    Exit
}
