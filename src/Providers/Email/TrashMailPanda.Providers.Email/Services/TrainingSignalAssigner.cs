using TrashMailPanda.Shared.Base;
using TrashMailPanda.Shared.Models;

namespace TrashMailPanda.Providers.Email.Services;

/// <summary>
/// Assigns a training classification signal using the 8-rule priority table
/// defined in the Gmail training data research specification.
/// Rules are evaluated in descending priority order and the first match wins.
/// </summary>
public sealed class TrainingSignalAssigner : ITrainingSignalAssigner
{
    // Canonical folder names (case-insensitive comparison used in AssignSignal)
    private const string FolderSpam = "SPAM";
    private const string FolderTrash = "TRASH";
    private const string FolderSent = "SENT";
    private const string FolderArchive = "ARCHIVE";
    private const string FolderInbox = "INBOX";

    /// <inheritdoc />
    public TrainingSignalResult AssignSignal(string folder, bool isRead, EngagementFlags engagement)
    {
        var f = folder?.ToUpperInvariant() ?? string.Empty;
        bool engaged = engagement.IsReplied || engagement.IsForwarded;

        // Rule 1: Spam → AutoDelete, engagement irrelevant
        if (f == FolderSpam)
            return new TrainingSignalResult(ClassificationSignal.AutoDelete, 0.95f);

        // Rule 2: Archive + engaged → Excluded
        if (f == FolderArchive && engaged)
            return new TrainingSignalResult(ClassificationSignal.Excluded, 1.0f);

        // Rule 3: Trash + engaged → LowConfidence (user cared about this email)
        if (f == FolderTrash && engaged)
            return new TrainingSignalResult(ClassificationSignal.LowConfidence, 0.30f);

        // Rule 4: Trash (no engagement) → AutoDelete
        if (f == FolderTrash)
            return new TrainingSignalResult(ClassificationSignal.AutoDelete, 0.90f);

        // Rule 5: Archive + Unread → AutoArchive (passively received / not acted on)
        if (f == FolderArchive && !isRead)
            return new TrainingSignalResult(ClassificationSignal.AutoArchive, 0.85f);

        // Rule 6: Archive + Read (no engagement) → Excluded (user read it and archived — neutral)
        if (f == FolderArchive)
            return new TrainingSignalResult(ClassificationSignal.Excluded, 1.0f);

        // Rule 7: Inbox + Unread → LowConfidence (never opened)
        if (f == FolderInbox && !isRead)
            return new TrainingSignalResult(ClassificationSignal.LowConfidence, 0.20f);

        // Rule 8: Inbox + Read / Sent / anything else → Excluded
        return new TrainingSignalResult(ClassificationSignal.Excluded, 1.0f);
    }
}
