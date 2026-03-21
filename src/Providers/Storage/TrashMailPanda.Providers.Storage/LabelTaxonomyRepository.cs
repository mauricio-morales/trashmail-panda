using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using TrashMailPanda.Providers.Storage.Models;
using TrashMailPanda.Shared.Base;

namespace TrashMailPanda.Providers.Storage;

/// <summary>
/// SQLite-backed repository for Gmail label taxonomy.
/// Defines known system label IDs to classify labels on import.
/// </summary>
public sealed class LabelTaxonomyRepository : ILabelTaxonomyRepository
{
    private static readonly HashSet<string> KnownSystemLabelIds =
    [
        "INBOX", "SENT", "TRASH", "SPAM", "STARRED", "IMPORTANT",
        "UNREAD", "DRAFT", "CATEGORY_PERSONAL", "CATEGORY_SOCIAL",
        "CATEGORY_PROMOTIONS", "CATEGORY_UPDATES", "CATEGORY_FORUMS"
    ];

    private readonly TrashMailPandaDbContext _context;
    private readonly SemaphoreSlim _databaseLock;

    public LabelTaxonomyRepository(TrashMailPandaDbContext context, SemaphoreSlim databaseLock)
    {
        _context = context ?? throw new ArgumentNullException(nameof(context));
        _databaseLock = databaseLock ?? throw new ArgumentNullException(nameof(databaseLock));
    }

    /// <inheritdoc />
    public async Task UpsertBatchAsync(IEnumerable<LabelTaxonomyEntity> labels, CancellationToken cancellationToken = default)
    {
        var list = labels as IReadOnlyList<LabelTaxonomyEntity> ?? labels.ToList();
        if (list.Count == 0) return;

        await _databaseLock.WaitAsync(cancellationToken);
        try
        {
            var strategy = _context.Database.CreateExecutionStrategy();
            await strategy.ExecuteAsync(async () =>
            {
                await using var transaction = await _context.Database.BeginTransactionAsync(cancellationToken);

                // Raw SQL is required here: EF Core has no upsert primitive that can express
                // "ON CONFLICT DO UPDATE SET ... excluding some columns". We need UsageCount
                // and CreatedAt preserved across re-imports, which rules out AddOrUpdate /
                // ExecuteUpdate. A read-before-write per label would create an N+1 problem.
                const string sql = """
                    INSERT INTO label_taxonomy
                        (label_id, account_id, name, color, label_type, usage_count, created_at, updated_at)
                    VALUES
                        (@LabelId, @AccountId, @Name, @Color, @LabelType, @UsageCount, @CreatedAt, @UpdatedAt)
                    ON CONFLICT(label_id) DO UPDATE SET
                        account_id = excluded.account_id,
                        name       = excluded.name,
                        color      = excluded.color,
                        label_type = excluded.label_type,
                        updated_at = excluded.updated_at
                    -- usage_count and created_at are preserved
                    """;

                foreach (var label in list)
                {
                    // Determine label type: use the Gmail API `type` field if available,
                    // fall back to system-ID-set check.
                    var labelType = IsSystemLabel(label.LabelId)
                        ? "System"
                        : (label.LabelType == "System" ? "System" : "User");

                    await _context.Database.ExecuteSqlRawAsync(sql,
                        new SqliteParameter("@LabelId", label.LabelId),
                        new SqliteParameter("@AccountId", label.AccountId),
                        new SqliteParameter("@Name", label.Name),
                        new SqliteParameter("@Color", (object?)label.Color ?? DBNull.Value),
                        new SqliteParameter("@LabelType", labelType),
                        new SqliteParameter("@UsageCount", label.UsageCount),
                        new SqliteParameter("@CreatedAt", label.CreatedAt.ToString("O")),
                        new SqliteParameter("@UpdatedAt", label.UpdatedAt.ToString("O")));
                }

                await transaction.CommitAsync(cancellationToken);
            });
        }
        finally
        {
            _databaseLock.Release();
        }
    }

    /// <inheritdoc />
    public async Task<Result<IReadOnlyList<LabelTaxonomyEntity>>> GetAllLabelsAsync(
        string accountId,
        CancellationToken cancellationToken = default)
    {
        await _databaseLock.WaitAsync(cancellationToken);
        try
        {
            var labels = await _context.LabelTaxonomy
                .Where(l => l.AccountId == accountId)
                .ToListAsync(cancellationToken);

            return Result<IReadOnlyList<LabelTaxonomyEntity>>.Success(labels);
        }
        catch (Exception ex)
        {
            return Result<IReadOnlyList<LabelTaxonomyEntity>>.Failure(new StorageError("Failed to retrieve labels", ex.Message, ex));
        }
        finally
        {
            _databaseLock.Release();
        }
    }

    /// <inheritdoc />
    public async Task UpdateUsageCountsAsync(string accountId, CancellationToken cancellationToken = default)
    {
        await _databaseLock.WaitAsync(cancellationToken);
        try
        {
            // Raw SQL is used here for a correlated UPDATE subquery that recalculates all
            // UsageCount values in a single database round-trip. The EF equivalent would
            // require loading every label entity into memory and issuing separate UPDATE
            // statements, which is expensive for large taxonomies.
            const string sql = """
                UPDATE label_taxonomy
                SET usage_count = (
                    SELECT COUNT(*) FROM label_associations
                    WHERE label_associations.label_id = label_taxonomy.label_id
                ),
                updated_at = @Now
                WHERE account_id = @AccountId
                """;

            await _context.Database.ExecuteSqlRawAsync(sql,
                new SqliteParameter("@AccountId", accountId),
                new SqliteParameter("@Now", DateTime.UtcNow.ToString("O")));
        }
        finally
        {
            _databaseLock.Release();
        }
    }

    /// <inheritdoc />
    public async Task<Result<IReadOnlyList<LabelTaxonomyEntity>>> GetLabelStatisticsAsync(
        string accountId,
        CancellationToken cancellationToken = default)
    {
        await _databaseLock.WaitAsync(cancellationToken);
        try
        {
            var labels = await _context.LabelTaxonomy
                .Where(l => l.AccountId == accountId)
                .OrderByDescending(l => l.UsageCount)
                .ToListAsync(cancellationToken);

            return Result<IReadOnlyList<LabelTaxonomyEntity>>.Success(labels);
        }
        catch (Exception ex)
        {
            return Result<IReadOnlyList<LabelTaxonomyEntity>>.Failure(new StorageError("Failed to retrieve label statistics", ex.Message, ex));
        }
        finally
        {
            _databaseLock.Release();
        }
    }

    private static bool IsSystemLabel(string labelId) =>
        KnownSystemLabelIds.Contains(labelId) ||
        labelId.StartsWith("CATEGORY_", StringComparison.OrdinalIgnoreCase);
}
