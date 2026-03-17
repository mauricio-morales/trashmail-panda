using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using TrashMailPanda.Providers.Storage.Models;
using TrashMailPanda.Shared.Base;

namespace TrashMailPanda.Providers.Storage;

/// <summary>
/// Repository for label-email association data.
/// Reconciles the full set of labels for an email, inserting new associations
/// and removing stale ones.
/// </summary>
public interface ILabelAssociationRepository
{
    /// <summary>
    /// Reconciles label associations for an email:
    ///   - Inserts associations for labels not yet recorded.
    ///   - Deletes associations for labels no longer present.
    ///   Sets IsTrainingSignal=true for user labels and IsContextFeature=true for system labels.
    /// </summary>
    /// <param name="emailId">The Gmail message ID.</param>
    /// <param name="currentLabelIds">The current set of label IDs from the latest Gmail fetch.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    Task ReconcileAssociationsAsync(string emailId, IEnumerable<string> currentLabelIds, CancellationToken cancellationToken = default);

    /// <summary>
    /// Returns all label associations for the given email ID.
    /// </summary>
    Task<Result<IReadOnlyList<LabelAssociationEntity>>> GetByEmailIdAsync(string emailId, CancellationToken cancellationToken = default);
}
