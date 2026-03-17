using System.Threading;
using System.Threading.Tasks;
using TrashMailPanda.Providers.Storage.Models;
using TrashMailPanda.Shared.Base;

namespace TrashMailPanda.Providers.Storage;

/// <summary>
/// Repository for tracking scan progress — creation, checkpoint updates, and completion.
/// Enforces the constraint that at most one InProgress/PausedStorageFull scan exists per account.
/// </summary>
public interface IScanProgressRepository
{
    /// <summary>
    /// Returns the active (InProgress or PausedStorageFull) scan for the account, or null if none exists.
    /// </summary>
    Task<Result<ScanProgressEntity?>> GetActiveAsync(string accountId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Creates a new scan progress record (sets Status=InProgress).
    /// </summary>
    Task<Result<ScanProgressEntity>> CreateAsync(ScanProgressEntity entity, CancellationToken cancellationToken = default);

    /// <summary>
    /// Updates the FolderProgressJson and UpdatedAt fields for a scan.
    /// </summary>
    Task UpdateFolderProgressAsync(int id, string folderProgressJson, CancellationToken cancellationToken = default);

    /// <summary>
    /// Saves the Gmail historyId after a successful full scan.
    /// </summary>
    Task SaveHistoryIdAsync(int id, ulong historyId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Marks a scan as Completed and sets CompletedAt.
    /// </summary>
    Task MarkCompletedAsync(int id, CancellationToken cancellationToken = default);

    /// <summary>
    /// Marks a scan as Interrupted (e.g., due to cancellation or an unrecoverable error).
    /// </summary>
    Task MarkInterruptedAsync(int id, CancellationToken cancellationToken = default);
}
