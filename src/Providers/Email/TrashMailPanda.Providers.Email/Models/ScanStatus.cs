namespace TrashMailPanda.Providers.Email.Models;

/// <summary>
/// String constants representing the possible states of a training scan.
/// </summary>
public static class ScanStatus
{
    public const string InProgress = "InProgress";
    public const string Completed = "Completed";
    public const string Interrupted = "Interrupted";
    public const string PausedStorageFull = "PausedStorageFull";
    public const string Recovering = "Recovering";
    public const string NotStarted = "NotStarted";
}
