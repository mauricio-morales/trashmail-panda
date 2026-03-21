using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using TrashMailPanda.Shared.Base;
using TrashMailPanda.Shared.Models;
using TrashMailPanda.Providers.Storage.Models;

namespace TrashMailPanda.Providers.Storage;

/// <summary>
/// SQLite-backed repository for training email data.
/// Uses raw SQL for batch upserts and back-correction logic to maximise performance.
/// </summary>
public sealed class TrainingEmailRepository : ITrainingEmailRepository
{
    private readonly TrashMailPandaDbContext _context;
    private readonly SemaphoreSlim _databaseLock;

    public TrainingEmailRepository(TrashMailPandaDbContext context, SemaphoreSlim databaseLock)
    {
        _context = context ?? throw new ArgumentNullException(nameof(context));
        _databaseLock = databaseLock ?? throw new ArgumentNullException(nameof(databaseLock));
    }

    /// <inheritdoc />
    public async Task UpsertBatchAsync(IEnumerable<TrainingEmailEntity> emails, CancellationToken cancellationToken = default)
    {
        var list = emails as IReadOnlyList<TrainingEmailEntity> ?? emails.ToList();
        if (list.Count == 0) return;

        await _databaseLock.WaitAsync(cancellationToken);
        try
        {
            var strategy = _context.Database.CreateExecutionStrategy();
            await strategy.ExecuteAsync(async () =>
            {
                await using var transaction = await _context.Database.BeginTransactionAsync(cancellationToken);

                // Raw SQL is required here: EF Core has no native upsert that can express
                // "ON CONFLICT DO UPDATE SET ... excluding one column". We need ImportedAt
                // preserved on re-runs (original import timestamp), which rules out
                // AddOrUpdate / ExecuteUpdate. A read-before-write per email would create
                // an N+1 problem for batches of hundreds of messages.
                const string sql = """
                    INSERT INTO training_emails
                        (email_id, account_id, thread_id, folder_origin, is_read, is_replied, is_forwarded,
                         subject_prefix, classification_signal, signal_confidence, is_valid,
                         raw_label_ids, last_seen_at, imported_at, updated_at)
                    VALUES
                        (@EmailId, @AccountId, @ThreadId, @FolderOrigin, @IsRead, @IsReplied, @IsForwarded,
                         @SubjectPrefix, @ClassificationSignal, @SignalConfidence, @IsValid,
                         @RawLabelIds, @LastSeenAt, @ImportedAt, @UpdatedAt)
                    ON CONFLICT(email_id) DO UPDATE SET
                        account_id            = excluded.account_id,
                        thread_id             = excluded.thread_id,
                        is_read               = excluded.is_read,
                        is_replied            = excluded.is_replied,
                        is_forwarded          = excluded.is_forwarded,
                        subject_prefix        = excluded.subject_prefix,
                        classification_signal = excluded.classification_signal,
                        signal_confidence     = excluded.signal_confidence,
                        is_valid              = excluded.is_valid,
                        raw_label_ids         = excluded.raw_label_ids,
                        last_seen_at          = excluded.last_seen_at,
                        updated_at            = excluded.updated_at
                    -- folder_origin and imported_at are deliberately excluded:
                    -- folder_origin preserves the first-seen folder (highest-priority scan wins,
                    --   e.g. SENT beats INBOX for self-sent emails).
                    -- imported_at preserves the original import timestamp.
                    """;

                foreach (var email in list)
                {
                    await _context.Database.ExecuteSqlRawAsync(sql,
                        new SqliteParameter("@EmailId", email.EmailId),
                        new SqliteParameter("@AccountId", email.AccountId),
                        new SqliteParameter("@ThreadId", email.ThreadId),
                        new SqliteParameter("@FolderOrigin", email.FolderOrigin),
                        new SqliteParameter("@IsRead", email.IsRead ? 1 : 0),
                        new SqliteParameter("@IsReplied", email.IsReplied ? 1 : 0),
                        new SqliteParameter("@IsForwarded", email.IsForwarded ? 1 : 0),
                        new SqliteParameter("@SubjectPrefix", (object?)email.SubjectPrefix ?? DBNull.Value),
                        new SqliteParameter("@ClassificationSignal", email.ClassificationSignal),
                        new SqliteParameter("@SignalConfidence", email.SignalConfidence),
                        new SqliteParameter("@IsValid", email.IsValid ? 1 : 0),
                        new SqliteParameter("@RawLabelIds", (object?)email.RawLabelIds ?? DBNull.Value),
                        new SqliteParameter("@LastSeenAt", email.LastSeenAt.ToString("O")),
                        new SqliteParameter("@ImportedAt", email.ImportedAt.ToString("O")),
                        new SqliteParameter("@UpdatedAt", email.UpdatedAt.ToString("O")));
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
    public async Task<int> RunBackCorrectionAsync(string accountId, CancellationToken cancellationToken = default)
    {
        await _databaseLock.WaitAsync(cancellationToken);
        try
        {
            // Raw SQL is used for both UPDATE statements below because they are pure set-based
            // operations that must run entirely in the database. Loading affected rows into memory
            // first (the EF approach) would be prohibitively expensive when correcting thousands
            // of emails across threads.

            // Step 1: mark IsReplied on all non-SENT emails sharing a ThreadId with a SENT message
            const string repliedSql = """
                UPDATE training_emails
                SET is_replied = 1,
                    updated_at = @Now
                WHERE account_id = @AccountId
                  AND folder_origin != 'SENT'
                  AND is_replied = 0
                  AND thread_id IN (
                      SELECT thread_id FROM training_emails
                      WHERE account_id = @AccountId
                        AND folder_origin = 'SENT'
                  )
                """;

            // Step 2: mark IsForwarded on non-SENT emails whose thread has a SENT message with Fwd: prefix
            const string forwardedSql = """
                UPDATE training_emails
                SET is_forwarded = 1,
                    updated_at = @Now
                WHERE account_id = @AccountId
                  AND folder_origin != 'SENT'
                  AND is_forwarded = 0
                  AND thread_id IN (
                      SELECT thread_id FROM training_emails
                      WHERE account_id = @AccountId
                        AND folder_origin = 'SENT'
                        AND subject_prefix IN ('Fwd:', 'FW: ', 'Fw: ', 'FW:', 'Fw:')
                  )
                """;

            var now = DateTime.UtcNow.ToString("O");
            int total = 0;
            total += await _context.Database.ExecuteSqlRawAsync(repliedSql,
                new SqliteParameter("@AccountId", accountId),
                new SqliteParameter("@Now", now));

            total += await _context.Database.ExecuteSqlRawAsync(forwardedSql,
                new SqliteParameter("@AccountId", accountId),
                new SqliteParameter("@Now", now));

            return total;
        }
        finally
        {
            _databaseLock.Release();
        }
    }

    /// <inheritdoc />
    public async Task<int> ReDeriveSignalsForThreadsAsync(string accountId, ITrainingSignalAssigner signalAssigner, CancellationToken cancellationToken = default)
    {
        await _databaseLock.WaitAsync(cancellationToken);
        try
        {
            // Load rows where engagement flags changed (IsReplied or IsForwarded is now true)
            var rows = await _context.TrainingEmails
                .Where(e => e.AccountId == accountId && (e.IsReplied || e.IsForwarded))
                .ToListAsync(cancellationToken);

            if (rows.Count == 0) return 0;

            int updated = 0;
            foreach (var row in rows)
            {
                var engagement = new EngagementFlags(row.IsReplied, row.IsForwarded);
                var result = signalAssigner.AssignSignal(row.FolderOrigin, row.IsRead, engagement);

                var newSignal = result.Signal.ToString();
                if (row.ClassificationSignal == newSignal) continue;

                row.ClassificationSignal = newSignal;
                row.SignalConfidence = result.Confidence;
                row.IsValid = result.Signal != ClassificationSignal.Excluded;
                row.UpdatedAt = DateTime.UtcNow;
                updated++;
            }

            if (updated > 0)
                await _context.SaveChangesAsync(cancellationToken);

            return updated;
        }
        finally
        {
            _databaseLock.Release();
        }
    }

    /// <inheritdoc />
    public async Task<TrainingEmailEntity?> GetByEmailIdAsync(string emailId, CancellationToken cancellationToken = default)
    {
        await _databaseLock.WaitAsync(cancellationToken);
        try
        {
            return await _context.TrainingEmails
                .FirstOrDefaultAsync(e => e.EmailId == emailId, cancellationToken);
        }
        finally
        {
            _databaseLock.Release();
        }
    }

    /// <inheritdoc />
    public async Task<bool> HasAnyAsync(string accountId, CancellationToken cancellationToken = default)
    {
        await _databaseLock.WaitAsync(cancellationToken);
        try
        {
            return await _context.TrainingEmails
                .AnyAsync(e => e.AccountId == accountId, cancellationToken);
        }
        finally
        {
            _databaseLock.Release();
        }
    }

    /// <inheritdoc />
    public async Task<int> CountAsync(string accountId, CancellationToken cancellationToken = default)
    {
        await _databaseLock.WaitAsync(cancellationToken);
        try
        {
            return await _context.TrainingEmails
                .CountAsync(e => e.AccountId == accountId, cancellationToken);
        }
        finally
        {
            _databaseLock.Release();
        }
    }
}
