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
    /// Bulk email actions - delete, archive, label (requires Storage and Gmail).
    /// </summary>
    BulkOperations,

    /// <summary>
    /// Reconfigure provider settings (requires Storage).
    /// </summary>
    ProviderSettings,

    /// <summary>
    /// Run Gmail training data scan to build the ML dataset (requires Gmail).
    /// </summary>
    TrainData,

    /// <summary>
    /// Train the ML action classifier using the collected email feature data.
    /// </summary>
    TrainModel,

    /// <summary>
    /// Launch Avalonia UI application mode (requires Storage and Gmail).
    /// </summary>
    UIMode,

    /// <summary>
    /// Exit the application.
    /// </summary>
    Exit
}
