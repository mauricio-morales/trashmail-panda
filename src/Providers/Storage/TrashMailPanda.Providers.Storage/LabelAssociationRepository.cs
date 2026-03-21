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
/// SQLite-backed repository for label-email associations.
/// Reconciles the full set of labels for an email, inserting new and removing stale associations.
/// </summary>
public sealed class LabelAssociationRepository : ILabelAssociationRepository
{
    private static readonly HashSet<string> SystemLabelIds =
    [
        "INBOX", "SENT", "TRASH", "SPAM", "STARRED", "IMPORTANT",
        "UNREAD", "DRAFT", "CATEGORY_PERSONAL", "CATEGORY_SOCIAL",
        "CATEGORY_PROMOTIONS", "CATEGORY_UPDATES", "CATEGORY_FORUMS"
    ];

    private readonly TrashMailPandaDbContext _context;
    private readonly SemaphoreSlim _databaseLock;

    public LabelAssociationRepository(TrashMailPandaDbContext context, SemaphoreSlim databaseLock)
    {
        _context = context ?? throw new ArgumentNullException(nameof(context));
        _databaseLock = databaseLock ?? throw new ArgumentNullException(nameof(databaseLock));
    }

    /// <inheritdoc />
    public async Task ReconcileAssociationsAsync(
        string emailId,
        IEnumerable<string> currentLabelIds,
        CancellationToken cancellationToken = default)
    {
        var currentSet = currentLabelIds as IReadOnlyCollection<string>
            ?? currentLabelIds.ToHashSet();

        await _databaseLock.WaitAsync(cancellationToken);
        try
        {
            var strategy = _context.Database.CreateExecutionStrategy();
            await strategy.ExecuteAsync(async () =>
            {
                await using var transaction = await _context.Database.BeginTransactionAsync(cancellationToken);

                // Load existing association label IDs for this email
                var existing = await _context.LabelAssociations
                    .Where(a => a.EmailId == emailId)
                    .Select(a => a.LabelId)
                    .ToListAsync(cancellationToken);

                var existingSet = new HashSet<string>(existing);

                // Insert missing associations
                var toInsert = currentSet.Except(existingSet).ToList();
                foreach (var labelId in toInsert)
                {
                    bool isSystem = IsSystemLabel(labelId);
                    const string insertSql = """
                        INSERT INTO label_associations
                            (email_id, label_id, is_training_signal, is_context_feature, created_at)
                        VALUES
                            (@EmailId, @LabelId, @IsTrainingSignal, @IsContextFeature, @CreatedAt)
                        ON CONFLICT(email_id, label_id) DO NOTHING
                        """;

                    await _context.Database.ExecuteSqlRawAsync(insertSql,
                        new SqliteParameter("@EmailId", emailId),
                        new SqliteParameter("@LabelId", labelId),
                        new SqliteParameter("@IsTrainingSignal", isSystem ? 0 : 1),
                        new SqliteParameter("@IsContextFeature", isSystem ? 1 : 0),
                        new SqliteParameter("@CreatedAt", DateTime.UtcNow.ToString("O")));
                }

                // Delete stale associations
                var toDelete = existingSet.Except(currentSet).ToList();
                foreach (var labelId in toDelete)
                {
                    const string deleteSql = """
                        DELETE FROM label_associations
                        WHERE email_id = @EmailId AND label_id = @LabelId
                        """;

                    await _context.Database.ExecuteSqlRawAsync(deleteSql,
                        new SqliteParameter("@EmailId", emailId),
                        new SqliteParameter("@LabelId", labelId));
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
    public async Task<Result<IReadOnlyList<LabelAssociationEntity>>> GetByEmailIdAsync(
        string emailId,
        CancellationToken cancellationToken = default)
    {
        await _databaseLock.WaitAsync(cancellationToken);
        try
        {
            var associations = await _context.LabelAssociations
                .Where(a => a.EmailId == emailId)
                .ToListAsync(cancellationToken);

            return Result<IReadOnlyList<LabelAssociationEntity>>.Success(associations);
        }
        catch (Exception ex)
        {
            return Result<IReadOnlyList<LabelAssociationEntity>>.Failure(
                new StorageError("Failed to load label associations", ex.Message, ex));
        }
        finally
        {
            _databaseLock.Release();
        }
    }

    private static bool IsSystemLabel(string labelId) =>
        SystemLabelIds.Contains(labelId) ||
        labelId.StartsWith("CATEGORY_", StringComparison.OrdinalIgnoreCase);
}
