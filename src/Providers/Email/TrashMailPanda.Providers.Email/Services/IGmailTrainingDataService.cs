using System;
using System.Threading;
using System.Threading.Tasks;
using TrashMailPanda.Providers.Email.Models;
using TrashMailPanda.Shared.Base;

namespace TrashMailPanda.Providers.Email.Services;

/// <summary>
/// Service for collecting Gmail training data by scanning email folders
/// and producing classification signals for ML model training.
/// </summary>
public interface IGmailTrainingDataService
{
    /// <summary>
    /// Runs a full initial scan across all relevant Gmail folders (Spam, Trash, Sent, Archive, Inbox).
    /// Fetches emails, assigns classification signals, and stores training records in batches.
    /// Resumes from the last saved checkpoint if an interrupted scan is found.
    /// </summary>
    /// <param name="accountId">The user's Gmail account identifier.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <param name="progress">Optional progress reporter; receives updates after each batch and folder completion.</param>
    /// <returns>A summary of the completed scan.</returns>
    Task<Result<ScanSummary>> RunInitialScanAsync(
        string accountId,
        CancellationToken cancellationToken,
        IProgress<ScanProgressUpdate>? progress = null);

    /// <summary>
    /// Runs an incremental scan using the Gmail History API to process only changes
    /// since the last completed full scan.
    /// </summary>
    /// <param name="accountId">The user's Gmail account identifier.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>A summary of the incremental scan.</returns>
    Task<Result<ScanSummary>> RunIncrementalScanAsync(string accountId, CancellationToken cancellationToken);
}
