namespace TrashMailPanda.Models.Console;

/// <summary>
/// Represents the current step in the configuration wizard.
/// </summary>
public enum WizardStep
{
    /// <summary>
    /// Display welcome message and setup overview.
    /// </summary>
    Welcome,

    /// <summary>
    /// Configure Storage provider (database path, encryption).
    /// </summary>
    StorageSetup,

    /// <summary>
    /// Configure Gmail provider (OAuth credentials and flow).
    /// </summary>
    GmailSetup,

    /// <summary>
    /// Display summary of configured providers.
    /// </summary>
    Confirmation,

    /// <summary>
    /// Save configurations and exit wizard.
    /// </summary>
    Complete
}
