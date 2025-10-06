using System.Threading.Tasks;

namespace TrashMailPanda.Services;

/// <summary>
/// Service for managing application dialogs in a testable, MVVM-compliant way
/// Provides abstraction between ViewModels and UI-specific dialog implementations
/// </summary>
public interface IDialogService
{
    /// <summary>
    /// Show the Google OAuth setup dialog for Gmail, Contacts, and other Google services
    /// </summary>
    /// <returns>True if setup was completed successfully, false if cancelled</returns>
    Task<bool> ShowGoogleOAuthSetupAsync();

    /// <summary>
    /// Show the OpenAI API key setup dialog
    /// </summary>
    /// <returns>True if setup was completed successfully, false if cancelled</returns>
    Task<bool> ShowOpenAISetupAsync();

    /// <summary>
    /// Show a generic confirmation dialog
    /// </summary>
    /// <param name="title">Dialog title</param>
    /// <param name="message">Dialog message</param>
    /// <param name="confirmText">Confirm button text (default: "OK")</param>
    /// <param name="cancelText">Cancel button text (default: "Cancel")</param>
    /// <returns>True if confirmed, false if cancelled</returns>
    Task<bool> ShowConfirmationAsync(string title, string message, string confirmText = "OK", string cancelText = "Cancel");

    /// <summary>
    /// Show an information dialog
    /// </summary>
    /// <param name="title">Dialog title</param>
    /// <param name="message">Dialog message</param>
    /// <returns>Task that completes when dialog is dismissed</returns>
    Task ShowInformationAsync(string title, string message);

    /// <summary>
    /// Show an error dialog
    /// </summary>
    /// <param name="title">Dialog title</param>
    /// <param name="message">Error message</param>
    /// <returns>Task that completes when dialog is dismissed</returns>
    Task ShowErrorAsync(string title, string message);
}