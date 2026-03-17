using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using TrashMailPanda.Shared.Base;
using TrashMailPanda.Shared.Models;
using TrashMailPanda.Providers.Storage.Models;

namespace TrashMailPanda.Providers.Storage;

/// <summary>
/// Repository for training email data — upsert, back-correction, and signal re-derivation.
/// </summary>
public interface ITrainingEmailRepository
{
    /// <summary>
    /// Inserts or updates a batch of training email records.
    /// ON CONFLICT(EmailId) DO UPDATE — preserves ImportedAt, updates all other columns.
    /// </summary>
    /// <param name="emails">The emails to upsert.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    Task UpsertBatchAsync(IEnumerable<TrainingEmailEntity> emails, CancellationToken cancellationToken = default);

    /// <summary>
    /// Runs local thread-based back-correction:
    ///   1. Sets IsReplied=1 on all non-SENT emails sharing ThreadId with a SENT message for the given account.
    ///   2. Sets IsForwarded=1 on non-SENT emails whose thread has a SENT message with a Fwd:/FW:/Fw: SubjectPrefix.
    /// Both updates are scoped by accountId.
    /// </summary>
    /// <param name="accountId">Account to scope the back-correction to.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>Number of rows updated.</returns>
    Task<int> RunBackCorrectionAsync(string accountId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Re-derives classification signals for all rows where IsReplied or IsForwarded changed.
    /// Updates ClassificationSignal, SignalConfidence, and IsValid (false when new signal is Excluded).
    /// </summary>
    /// <param name="accountId">Account to scope the re-derivation to.</param>
    /// <param name="signalAssigner">Used to re-apply signal rules.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>Number of rows updated.</returns>
    Task<int> ReDeriveSignalsForThreadsAsync(string accountId, ITrainingSignalAssigner signalAssigner, CancellationToken cancellationToken = default);

    /// <summary>
    /// Returns a single training email by its Gmail message ID, or null if not found.
    /// </summary>
    Task<TrainingEmailEntity?> GetByEmailIdAsync(string emailId, CancellationToken cancellationToken = default);
}
