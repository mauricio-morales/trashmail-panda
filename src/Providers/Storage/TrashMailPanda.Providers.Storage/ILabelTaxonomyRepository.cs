using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using TrashMailPanda.Providers.Storage.Models;
using TrashMailPanda.Shared.Base;

namespace TrashMailPanda.Providers.Storage;

/// <summary>
/// Repository for label taxonomy data — import, query, and usage-count recalculation.
/// </summary>
public interface ILabelTaxonomyRepository
{
    /// <summary>
    /// Inserts or updates a batch of label taxonomy entries.
    /// ON CONFLICT(LabelId) DO UPDATE preserves CreatedAt.
    /// </summary>
    Task UpsertBatchAsync(IEnumerable<LabelTaxonomyEntity> labels, CancellationToken cancellationToken = default);

    /// <summary>
    /// Returns all known labels for the given account.
    /// </summary>
    Task<Result<IReadOnlyList<LabelTaxonomyEntity>>> GetAllLabelsAsync(string accountId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Recalculates UsageCount for all labels by joining against label_associations.
    /// Executes a single UPDATE statement — not incremental, to prevent drift.
    /// </summary>
    Task UpdateUsageCountsAsync(string accountId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Returns all labels for the account ordered by UsageCount descending.
    /// </summary>
    Task<Result<IReadOnlyList<LabelTaxonomyEntity>>> GetLabelStatisticsAsync(string accountId, CancellationToken cancellationToken = default);
}
