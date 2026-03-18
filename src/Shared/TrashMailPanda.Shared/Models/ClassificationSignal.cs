namespace TrashMailPanda.Shared.Models;

/// <summary>
/// Classification signal assigned to a training email during import.
/// Determines what the ML model should learn about this email's disposition.
/// </summary>
public enum ClassificationSignal
{
    AutoDelete = 1,       // High confidence delete signal (Spam/Trash)
    AutoArchive = 2,      // High confidence archive signal (Archive+Unread)
    LowConfidence = 3,    // Conflicting signals — low weight in training
    Excluded = 4          // Neutral — excluded from supervised training set
}
