namespace TrashMailPanda.Models.Console;

/// <summary>
/// Represents progress through the first-time setup wizard.
/// </summary>
public class ConfigurationWizardState
{
    /// <summary>
    /// Gets or sets the current step in the wizard.
    /// </summary>
    public WizardStep CurrentStep { get; set; } = WizardStep.Welcome;

    /// <summary>
    /// Gets or sets whether Storage provider configuration has been saved.
    /// </summary>
    public bool StorageConfigured { get; set; }

    /// <summary>
    /// Gets or sets whether Gmail OAuth has been completed.
    /// </summary>
    public bool GmailConfigured { get; set; }

    /// <summary>
    /// Gets or sets validation errors from the current step.
    /// </summary>
    public List<string> Errors { get; set; } = new();

    /// <summary>
    /// Gets whether the wizard is complete (all required providers configured).
    /// </summary>
    public bool IsComplete => StorageConfigured && GmailConfigured;

    /// <summary>
    /// Gets whether the current step can proceed (no validation errors).
    /// </summary>
    public bool CanProceed => Errors.Count == 0;

    /// <summary>
    /// Adds a validation error to the current step.
    /// </summary>
    /// <param name="error">The error message to add.</param>
    public void AddError(string error)
    {
        Errors.Add(error);
    }

    /// <summary>
    /// Clears all validation errors for the current step.
    /// </summary>
    public void ClearErrors()
    {
        Errors.Clear();
    }
}
