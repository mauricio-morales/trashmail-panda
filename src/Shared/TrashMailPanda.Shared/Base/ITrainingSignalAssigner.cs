using TrashMailPanda.Shared.Models;

namespace TrashMailPanda.Shared.Base;

/// <summary>
/// Assigns a training classification signal to an email based on folder origin and engagement flags.
/// Uses an 8-rule priority table to determine the signal and confidence.
/// </summary>
public interface ITrainingSignalAssigner
{
    /// <summary>
    /// Assigns a training classification signal based on folder origin, read status, and engagement.
    /// </summary>
    /// <param name="folder">The canonical folder origin (e.g. "SPAM", "TRASH", "SENT", "ARCHIVE", "INBOX").</param>
    /// <param name="isRead">Whether the email has been read.</param>
    /// <param name="engagement">Engagement flags indicating if the email was replied to or forwarded.</param>
    /// <returns>The classification signal and confidence score.</returns>
    TrainingSignalResult AssignSignal(string folder, bool isRead, EngagementFlags engagement);
}
