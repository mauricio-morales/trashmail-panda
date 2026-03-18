namespace TrashMailPanda.Providers.Email.Models;

/// <summary>
/// Carries incremental progress information reported by IGmailTrainingDataService
/// during an initial or incremental scan.
/// </summary>
/// <param name="FolderName">Human-readable folder name (e.g. "SPAM", "INBOX").</param>
/// <param name="EmailsProcessedInFolder">Emails saved so far in the current folder.</param>
/// <param name="FolderCompleted">True when the folder has finished scanning.</param>
/// <param name="TotalEmailsProcessed">Running total across all folders so far.</param>
public record ScanProgressUpdate(
    string FolderName,
    int EmailsProcessedInFolder,
    bool FolderCompleted,
    int TotalEmailsProcessed);
