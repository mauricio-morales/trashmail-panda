using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using TrashMailPanda.Models.Console;
using TrashMailPanda.Shared.Base;

namespace TrashMailPanda.Services;

/// <summary>
/// UI-agnostic bulk operation business logic.
/// Filters emails from the local DB, previews the scope, and executes
/// batch Gmail actions + training label storage.
/// No rendering dependency — consumable by any UI layer.
/// </summary>
public interface IBulkOperationService
{
    /// <summary>
    /// Returns a preview of emails matching the given criteria, without executing any actions.
    /// </summary>
    Task<Result<IReadOnlyList<TrashMailPanda.Providers.Storage.Models.EmailFeatureVector>>> PreviewAsync(
        BulkOperationCriteria criteria,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Executes the bulk action on the given email IDs.
    /// Each email: Gmail action first, then SetTrainingLabelAsync on success.
    /// Failures are collected and returned but do not stop the batch.
    /// Time-bounded labels fall back to Archive when ReceivedDateUtc is unavailable.
    /// </summary>
    Task<Result<BulkOperationResult>> ExecuteAsync(
        IReadOnlyList<string> emailIds,
        string action,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Executes the bulk action on the given feature vectors.
    /// Passes <see cref="TrashMailPanda.Providers.Storage.Models.EmailFeatureVector.ReceivedDateUtc"/> to
    /// enable correct age-at-execution routing for time-bounded labels.
    /// </summary>
    Task<Result<BulkOperationResult>> ExecuteAsync(
        IReadOnlyList<TrashMailPanda.Providers.Storage.Models.EmailFeatureVector> features,
        string action,
        CancellationToken cancellationToken = default);
}
