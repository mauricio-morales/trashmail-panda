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

    /// <summary>
    /// Re-fetches emails from the Gmail API for any message IDs that exist in
    /// <c>training_emails</c> but have no row in <c>email_features</c>, then builds
    /// and stores the missing feature vectors.  This repairs data created by the
    /// previous incremental-sync implementation that wrote only to <c>training_emails</c>.
    /// </summary>
    /// <param name="accountId">The user's Gmail account identifier.</param>
    /// <param name="orphanedIds">Message IDs that need feature vectors.</param>
    /// <param name="progress">Optional: reports (processed, total) tuples.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>Number of feature vectors successfully stored.</returns>
    Task<Result<int>> BackfillMissingFeatureVectorsAsync(
        string accountId,
        IReadOnlyList<string> orphanedIds,
        IProgress<(int Processed, int Total)>? progress = null,
        CancellationToken cancellationToken = default);
}
