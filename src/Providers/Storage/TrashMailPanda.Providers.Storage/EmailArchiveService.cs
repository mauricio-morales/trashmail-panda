using Microsoft.Data.Sqlite;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using TrashMailPanda.Shared.Base;
using TrashMailPanda.Providers.Storage.Models;

namespace TrashMailPanda.Providers.Storage;

/// <summary>
/// SQLite implementation of IEmailArchiveService.
/// Provides ML training data storage with encrypted SQLCipher database.
/// </summary>
public class EmailArchiveService : IEmailArchiveService, IDisposable
{
    private readonly SqliteConnection _connection;
    private readonly SemaphoreSlim _connectionLock;
    private readonly bool _ownsLock;

    public EmailArchiveService(SqliteConnection connection, SemaphoreSlim connectionLock)
    {
        _connection = connection ?? throw new ArgumentNullException(nameof(connection));
        _connectionLock = connectionLock ?? throw new ArgumentNullException(nameof(connectionLock));
        _ownsLock = false;
    }

    /// <summary>
    /// Constructor for testing - creates its own connection lock.
    /// </summary>
    public EmailArchiveService(SqliteConnection connection)
    {
        _connection = connection ?? throw new ArgumentNullException(nameof(connection));
        _connectionLock = new SemaphoreSlim(1, 1);
        _ownsLock = true;
    }

    public void Dispose()
    {
        if (_ownsLock)
        {
            _connectionLock?.Dispose();
        }
    }

    // ============================================================
    // Feature Vector Storage
    // ============================================================

    public async Task<Result<bool>> StoreFeatureAsync(
        EmailFeatureVector feature,
        CancellationToken cancellationToken = default)
    {
        if (feature == null)
            return Result<bool>.Failure(new ValidationError("Feature vector cannot be null"));

        if (string.IsNullOrWhiteSpace(feature.EmailId))
            return Result<bool>.Failure(new ValidationError("EmailId is required"));

        try
        {
            await _connectionLock.WaitAsync(cancellationToken);
            try
            {
                const string sql = @"
                    INSERT OR REPLACE INTO email_features (
                        EmailId, SenderDomain, SenderKnown, ContactStrength,
                        SpfResult, DkimResult, DmarcResult, HasListUnsubscribe,
                        HasAttachments, HourReceived, DayOfWeek, EmailSizeLog,
                        SubjectLength, RecipientCount, IsReply, InUserWhitelist,
                        InUserBlacklist, LabelCount, LinkCount, ImageCount,
                        HasTrackingPixel, UnsubscribeLinkInBody, EmailAgeDays,
                        IsInInbox, IsStarred, IsImportant, WasInTrash,
                        WasInSpam, IsArchived, ThreadMessageCount, SenderFrequency,
                        SubjectText, BodyTextShort, TopicClusterId,
                        TopicDistributionJson, SenderCategory, SemanticEmbeddingJson,
                        FeatureSchemaVersion, ExtractedAt, UserCorrected
                    ) VALUES (
                        @EmailId, @SenderDomain, @SenderKnown, @ContactStrength,
                        @SpfResult, @DkimResult, @DmarcResult, @HasListUnsubscribe,
                        @HasAttachments, @HourReceived, @DayOfWeek, @EmailSizeLog,
                        @SubjectLength, @RecipientCount, @IsReply, @InUserWhitelist,
                        @InUserBlacklist, @LabelCount, @LinkCount, @ImageCount,
                        @HasTrackingPixel, @UnsubscribeLinkInBody, @EmailAgeDays,
                        @IsInInbox, @IsStarred, @IsImportant, @WasInTrash,
                        @WasInSpam, @IsArchived, @ThreadMessageCount, @SenderFrequency,
                        @SubjectText, @BodyTextShort, @TopicClusterId,
                        @TopicDistributionJson, @SenderCategory, @SemanticEmbeddingJson,
                        @FeatureSchemaVersion, @ExtractedAt, @UserCorrected
                    )";

                using var command = _connection.CreateCommand();
                command.CommandText = sql;

                // Add all parameters
                command.Parameters.AddWithValue("@EmailId", feature.EmailId);
                command.Parameters.AddWithValue("@SenderDomain", feature.SenderDomain);
                command.Parameters.AddWithValue("@SenderKnown", feature.SenderKnown);
                command.Parameters.AddWithValue("@ContactStrength", feature.ContactStrength);
                command.Parameters.AddWithValue("@SpfResult", feature.SpfResult);
                command.Parameters.AddWithValue("@DkimResult", feature.DkimResult);
                command.Parameters.AddWithValue("@DmarcResult", feature.DmarcResult);
                command.Parameters.AddWithValue("@HasListUnsubscribe", feature.HasListUnsubscribe);
                command.Parameters.AddWithValue("@HasAttachments", feature.HasAttachments);
                command.Parameters.AddWithValue("@HourReceived", feature.HourReceived);
                command.Parameters.AddWithValue("@DayOfWeek", feature.DayOfWeek);
                command.Parameters.AddWithValue("@EmailSizeLog", feature.EmailSizeLog);
                command.Parameters.AddWithValue("@SubjectLength", feature.SubjectLength);
                command.Parameters.AddWithValue("@RecipientCount", feature.RecipientCount);
                command.Parameters.AddWithValue("@IsReply", feature.IsReply);
                command.Parameters.AddWithValue("@InUserWhitelist", feature.InUserWhitelist);
                command.Parameters.AddWithValue("@InUserBlacklist", feature.InUserBlacklist);
                command.Parameters.AddWithValue("@LabelCount", feature.LabelCount);
                command.Parameters.AddWithValue("@LinkCount", feature.LinkCount);
                command.Parameters.AddWithValue("@ImageCount", feature.ImageCount);
                command.Parameters.AddWithValue("@HasTrackingPixel", feature.HasTrackingPixel);
                command.Parameters.AddWithValue("@UnsubscribeLinkInBody", feature.UnsubscribeLinkInBody);
                command.Parameters.AddWithValue("@EmailAgeDays", feature.EmailAgeDays);
                command.Parameters.AddWithValue("@IsInInbox", feature.IsInInbox);
                command.Parameters.AddWithValue("@IsStarred", feature.IsStarred);
                command.Parameters.AddWithValue("@IsImportant", feature.IsImportant);
                command.Parameters.AddWithValue("@WasInTrash", feature.WasInTrash);
                command.Parameters.AddWithValue("@WasInSpam", feature.WasInSpam);
                command.Parameters.AddWithValue("@IsArchived", feature.IsArchived);
                command.Parameters.AddWithValue("@ThreadMessageCount", feature.ThreadMessageCount);
                command.Parameters.AddWithValue("@SenderFrequency", feature.SenderFrequency);
                command.Parameters.AddWithValue("@SubjectText", (object?)feature.SubjectText ?? DBNull.Value);
                command.Parameters.AddWithValue("@BodyTextShort", (object?)feature.BodyTextShort ?? DBNull.Value);
                command.Parameters.AddWithValue("@TopicClusterId", (object?)feature.TopicClusterId ?? DBNull.Value);
                command.Parameters.AddWithValue("@TopicDistributionJson", (object?)feature.TopicDistributionJson ?? DBNull.Value);
                command.Parameters.AddWithValue("@SenderCategory", (object?)feature.SenderCategory ?? DBNull.Value);
                command.Parameters.AddWithValue("@SemanticEmbeddingJson", (object?)feature.SemanticEmbeddingJson ?? DBNull.Value);
                command.Parameters.AddWithValue("@FeatureSchemaVersion", feature.FeatureSchemaVersion);
                command.Parameters.AddWithValue("@ExtractedAt", feature.ExtractedAt.ToString("O"));
                command.Parameters.AddWithValue("@UserCorrected", feature.UserCorrected);

                await command.ExecuteNonQueryAsync(cancellationToken);
                return Result<bool>.Success(true);
            }
            finally
            {
                _connectionLock.Release();
            }
        }
        catch (Exception ex)
        {
            return Result<bool>.Failure(new StorageError($"Failed to store feature for {feature.EmailId}", ex.Message, ex));
        }
    }

    public async Task<Result<int>> StoreFeaturesBatchAsync(
        IEnumerable<EmailFeatureVector> features,
        CancellationToken cancellationToken = default)
    {
        if (features == null)
            return Result<int>.Failure(new ValidationError("Features collection cannot be null"));

        var featureList = features.ToList();
        if (featureList.Count == 0)
            return Result<int>.Success(0);

        // Validate all features before starting transaction
        foreach (var feature in featureList)
        {
            if (string.IsNullOrWhiteSpace(feature.EmailId))
                return Result<int>.Failure(new ValidationError("All features must have valid EmailId"));
        }

        try
        {
            await _connectionLock.WaitAsync(cancellationToken);
            try
            {
                // Process in batches of 500 rows per research.md R5
                const int batchSize = 500;
                int totalStored = 0;

                for (int i = 0; i < featureList.Count; i += batchSize)
                {
                    var batch = featureList.Skip(i).Take(batchSize);

                    using var transaction = _connection.BeginTransaction();
                    try
                    {
                        foreach (var feature in batch)
                        {
                            var result = await StoreFeatureSingleAsync(feature, cancellationToken);
                            if (result.IsSuccess)
                            {
                                totalStored++;
                            }
                            else
                            {
                                // Rollback and return failure immediately on first error
                                await transaction.RollbackAsync(cancellationToken);
                                return Result<int>.Failure(new StorageError(
                                    $"Failed to store feature {feature.EmailId}: {result.Error.Message}"));
                            }
                        }

                        await transaction.CommitAsync(cancellationToken);
                    }
                    catch (Exception ex)
                    {
                        await transaction.RollbackAsync(cancellationToken);
                        return Result<int>.Failure(new StorageError(
                            $"Transaction failed during batch storage: {ex.Message}"));
                    }
                }

                return Result<int>.Success(totalStored);
            }
            finally
            {
                _connectionLock.Release();
            }
        }
        catch (Exception ex)
        {
            return Result<int>.Failure(new StorageError($"Failed to store feature batch", ex.Message, ex));
        }
        finally
        {
            // T046: Add monitoring hook after successful batch operation (outside lock)
            // Update storage usage statistics (best-effort, don't fail batch on monitoring error)
            _ = await GetStorageUsageAsync();
        }
    }

    /// <summary>
    /// Internal helper for storing a single feature without lock (assumes caller holds lock).
    /// </summary>
    private async Task<Result<bool>> StoreFeatureSingleAsync(EmailFeatureVector feature, CancellationToken cancellationToken)
    {
        const string sql = @"
            INSERT OR REPLACE INTO email_features (
                EmailId, SenderDomain, SenderKnown, ContactStrength,
                SpfResult, DkimResult, DmarcResult, HasListUnsubscribe,
                HasAttachments, HourReceived, DayOfWeek, EmailSizeLog,
                SubjectLength, RecipientCount, IsReply, InUserWhitelist,
                InUserBlacklist, LabelCount, LinkCount, ImageCount,
                HasTrackingPixel, UnsubscribeLinkInBody, EmailAgeDays,
                IsInInbox, IsStarred, IsImportant, WasInTrash,
                WasInSpam, IsArchived, ThreadMessageCount, SenderFrequency,
                SubjectText, BodyTextShort, TopicClusterId,
                TopicDistributionJson, SenderCategory, SemanticEmbeddingJson,
                FeatureSchemaVersion, ExtractedAt, UserCorrected
            ) VALUES (
                @EmailId, @SenderDomain, @SenderKnown, @ContactStrength,
                @SpfResult, @DkimResult, @DmarcResult, @HasListUnsubscribe,
                @HasAttachments, @HourReceived, @DayOfWeek, @EmailSizeLog,
                @SubjectLength, @RecipientCount, @IsReply, @InUserWhitelist,
                @InUserBlacklist, @LabelCount, @LinkCount, @ImageCount,
                @HasTrackingPixel, @UnsubscribeLinkInBody, @EmailAgeDays,
                @IsInInbox, @IsStarred, @IsImportant, @WasInTrash,
                @WasInSpam, @IsArchived, @ThreadMessageCount, @SenderFrequency,
                @SubjectText, @BodyTextShort, @TopicClusterId,
                @TopicDistributionJson, @SenderCategory, @SemanticEmbeddingJson,
                @FeatureSchemaVersion, @ExtractedAt, @UserCorrected
            )";

        using var command = _connection.CreateCommand();
        command.CommandText = sql;

        // Add all parameters (same as StoreFeatureAsync)
        command.Parameters.AddWithValue("@EmailId", feature.EmailId);
        command.Parameters.AddWithValue("@SenderDomain", feature.SenderDomain);
        command.Parameters.AddWithValue("@SenderKnown", feature.SenderKnown);
        command.Parameters.AddWithValue("@ContactStrength", feature.ContactStrength);
        command.Parameters.AddWithValue("@SpfResult", feature.SpfResult);
        command.Parameters.AddWithValue("@DkimResult", feature.DkimResult);
        command.Parameters.AddWithValue("@DmarcResult", feature.DmarcResult);
        command.Parameters.AddWithValue("@HasListUnsubscribe", feature.HasListUnsubscribe);
        command.Parameters.AddWithValue("@HasAttachments", feature.HasAttachments);
        command.Parameters.AddWithValue("@HourReceived", feature.HourReceived);
        command.Parameters.AddWithValue("@DayOfWeek", feature.DayOfWeek);
        command.Parameters.AddWithValue("@EmailSizeLog", feature.EmailSizeLog);
        command.Parameters.AddWithValue("@SubjectLength", feature.SubjectLength);
        command.Parameters.AddWithValue("@RecipientCount", feature.RecipientCount);
        command.Parameters.AddWithValue("@IsReply", feature.IsReply);
        command.Parameters.AddWithValue("@InUserWhitelist", feature.InUserWhitelist);
        command.Parameters.AddWithValue("@InUserBlacklist", feature.InUserBlacklist);
        command.Parameters.AddWithValue("@LabelCount", feature.LabelCount);
        command.Parameters.AddWithValue("@LinkCount", feature.LinkCount);
        command.Parameters.AddWithValue("@ImageCount", feature.ImageCount);
        command.Parameters.AddWithValue("@HasTrackingPixel", feature.HasTrackingPixel);
        command.Parameters.AddWithValue("@UnsubscribeLinkInBody", feature.UnsubscribeLinkInBody);
        command.Parameters.AddWithValue("@EmailAgeDays", feature.EmailAgeDays);
        command.Parameters.AddWithValue("@IsInInbox", feature.IsInInbox);
        command.Parameters.AddWithValue("@IsStarred", feature.IsStarred);
        command.Parameters.AddWithValue("@IsImportant", feature.IsImportant);
        command.Parameters.AddWithValue("@WasInTrash", feature.WasInTrash);
        command.Parameters.AddWithValue("@WasInSpam", feature.WasInSpam);
        command.Parameters.AddWithValue("@IsArchived", feature.IsArchived);
        command.Parameters.AddWithValue("@ThreadMessageCount", feature.ThreadMessageCount);
        command.Parameters.AddWithValue("@SenderFrequency", feature.SenderFrequency);
        command.Parameters.AddWithValue("@SubjectText", (object?)feature.SubjectText ?? DBNull.Value);
        command.Parameters.AddWithValue("@BodyTextShort", (object?)feature.BodyTextShort ?? DBNull.Value);
        command.Parameters.AddWithValue("@TopicClusterId", (object?)feature.TopicClusterId ?? DBNull.Value);
        command.Parameters.AddWithValue("@TopicDistributionJson", (object?)feature.TopicDistributionJson ?? DBNull.Value);
        command.Parameters.AddWithValue("@SenderCategory", (object?)feature.SenderCategory ?? DBNull.Value);
        command.Parameters.AddWithValue("@SemanticEmbeddingJson", (object?)feature.SemanticEmbeddingJson ?? DBNull.Value);
        command.Parameters.AddWithValue("@FeatureSchemaVersion", feature.FeatureSchemaVersion);
        command.Parameters.AddWithValue("@ExtractedAt", feature.ExtractedAt.ToString("O"));
        command.Parameters.AddWithValue("@UserCorrected", feature.UserCorrected);

        await command.ExecuteNonQueryAsync(cancellationToken);
        return Result<bool>.Success(true);
    }

    public async Task<Result<EmailFeatureVector?>> GetFeatureAsync(
        string emailId,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(emailId))
            return Result<EmailFeatureVector?>.Failure(new ValidationError("EmailId is required"));

        try
        {
            await _connectionLock.WaitAsync(cancellationToken);
            try
            {
                const string sql = @"
                    SELECT EmailId, SenderDomain, SenderKnown, ContactStrength,
                           SpfResult, DkimResult, DmarcResult, HasListUnsubscribe,
                           HasAttachments, HourReceived, DayOfWeek, EmailSizeLog,
                           SubjectLength, RecipientCount, IsReply, InUserWhitelist,
                           InUserBlacklist, LabelCount, LinkCount, ImageCount,
                           HasTrackingPixel, UnsubscribeLinkInBody, EmailAgeDays,
                           IsInInbox, IsStarred, IsImportant, WasInTrash,
                           WasInSpam, IsArchived, ThreadMessageCount, SenderFrequency,
                           SubjectText, BodyTextShort, TopicClusterId,
                           TopicDistributionJson, SenderCategory, SemanticEmbeddingJson,
                           FeatureSchemaVersion, ExtractedAt, UserCorrected
                    FROM email_features
                    WHERE EmailId = @EmailId";

                using var command = _connection.CreateCommand();
                command.CommandText = sql;
                command.Parameters.AddWithValue("@EmailId", emailId);

                using var reader = await command.ExecuteReaderAsync(cancellationToken);
                if (!await reader.ReadAsync(cancellationToken))
                    return Result<EmailFeatureVector?>.Success(null);

                var feature = ReadFeatureFromReader(reader);
                return Result<EmailFeatureVector?>.Success(feature);
            }
            finally
            {
                _connectionLock.Release();
            }
        }
        catch (Exception ex)
        {
            return Result<EmailFeatureVector?>.Failure(new StorageError($"Failed to retrieve feature for {emailId}", ex.Message, ex));
        }
    }

    public async Task<Result<IEnumerable<EmailFeatureVector>>> GetAllFeaturesAsync(
        int? schemaVersion = null,
        CancellationToken cancellationToken = default)
    {
        try
        {
            await _connectionLock.WaitAsync(cancellationToken);
            try
            {
                var sql = @"
                    SELECT EmailId, SenderDomain, SenderKnown, ContactStrength,
                           SpfResult, DkimResult, DmarcResult, HasListUnsubscribe,
                           HasAttachments, HourReceived, DayOfWeek, EmailSizeLog,
                           SubjectLength, RecipientCount, IsReply, InUserWhitelist,
                           InUserBlacklist, LabelCount, LinkCount, ImageCount,
                           HasTrackingPixel, UnsubscribeLinkInBody, EmailAgeDays,
                           IsInInbox, IsStarred, IsImportant, WasInTrash,
                           WasInSpam, IsArchived, ThreadMessageCount, SenderFrequency,
                           SubjectText, BodyTextShort, TopicClusterId,
                           TopicDistributionJson, SenderCategory, SemanticEmbeddingJson,
                           FeatureSchemaVersion, ExtractedAt, UserCorrected
                    FROM email_features";

                if (schemaVersion.HasValue)
                    sql += " WHERE FeatureSchemaVersion = @schemaVersion";

                using var command = _connection.CreateCommand();
                command.CommandText = sql;

                if (schemaVersion.HasValue)
                    command.Parameters.AddWithValue("@schemaVersion", schemaVersion.Value);

                var features = new List<EmailFeatureVector>();
                using var reader = await command.ExecuteReaderAsync(cancellationToken);

                while (await reader.ReadAsync(cancellationToken))
                {
                    features.Add(ReadFeatureFromReader(reader));
                }

                return Result<IEnumerable<EmailFeatureVector>>.Success(features);
            }
            finally
            {
                _connectionLock.Release();
            }
        }
        catch (Exception ex)
        {
            return Result<IEnumerable<EmailFeatureVector>>.Failure(new StorageError("Failed to retrieve features", ex.Message, ex));
        }
    }

    /// <summary>
    /// Reads an EmailFeatureVector from a data reader.
    /// </summary>
    private EmailFeatureVector ReadFeatureFromReader(SqliteDataReader reader)
    {
        return new EmailFeatureVector
        {
            EmailId = reader.GetString(0),
            SenderDomain = reader.GetString(1),
            SenderKnown = reader.GetInt32(2),
            ContactStrength = reader.GetInt32(3),
            SpfResult = reader.GetString(4),
            DkimResult = reader.GetString(5),
            DmarcResult = reader.GetString(6),
            HasListUnsubscribe = reader.GetInt32(7),
            HasAttachments = reader.GetInt32(8),
            HourReceived = reader.GetInt32(9),
            DayOfWeek = reader.GetInt32(10),
            EmailSizeLog = reader.GetFloat(11),
            SubjectLength = reader.GetInt32(12),
            RecipientCount = reader.GetInt32(13),
            IsReply = reader.GetInt32(14),
            InUserWhitelist = reader.GetInt32(15),
            InUserBlacklist = reader.GetInt32(16),
            LabelCount = reader.GetInt32(17),
            LinkCount = reader.GetInt32(18),
            ImageCount = reader.GetInt32(19),
            HasTrackingPixel = reader.GetInt32(20),
            UnsubscribeLinkInBody = reader.GetInt32(21),
            EmailAgeDays = reader.GetInt32(22),
            IsInInbox = reader.GetInt32(23),
            IsStarred = reader.GetInt32(24),
            IsImportant = reader.GetInt32(25),
            WasInTrash = reader.GetInt32(26),
            WasInSpam = reader.GetInt32(27),
            IsArchived = reader.GetInt32(28),
            ThreadMessageCount = reader.GetInt32(29),
            SenderFrequency = reader.GetInt32(30),
            SubjectText = reader.IsDBNull(31) ? null : reader.GetString(31),
            BodyTextShort = reader.IsDBNull(32) ? null : reader.GetString(32),
            TopicClusterId = reader.IsDBNull(33) ? null : reader.GetInt32(33),
            TopicDistributionJson = reader.IsDBNull(34) ? null : reader.GetString(34),
            SenderCategory = reader.IsDBNull(35) ? null : reader.GetString(35),
            SemanticEmbeddingJson = reader.IsDBNull(36) ? null : reader.GetString(36),
            FeatureSchemaVersion = reader.GetInt32(37),
            ExtractedAt = DateTime.Parse(reader.GetString(38)),
            UserCorrected = reader.GetInt32(39)
        };
    }

    // ============================================================
    // Email Archive Storage
    // ============================================================

    public async Task<Result<bool>> StoreArchiveAsync(
        EmailArchiveEntry archive,
        CancellationToken cancellationToken = default)
    {
        if (archive == null)
            return Result<bool>.Failure(new ValidationError("Archive entry cannot be null"));

        if (string.IsNullOrWhiteSpace(archive.EmailId))
            return Result<bool>.Failure(new ValidationError("EmailId is required"));

        // Validate at least one body field is provided
        if (string.IsNullOrWhiteSpace(archive.BodyText) && string.IsNullOrWhiteSpace(archive.BodyHtml))
            return Result<bool>.Failure(new ValidationError("At least one of BodyText or BodyHtml must be provided"));

        try
        {
            await _connectionLock.WaitAsync(cancellationToken);
            try
            {
                const string sql = @"
                    INSERT OR REPLACE INTO email_archive (
                        EmailId, ThreadId, ProviderType, HeadersJson, BodyText, BodyHtml,
                        FolderTagsJson, SizeEstimate, ReceivedDate, ArchivedAt, Snippet, SourceFolder, UserCorrected
                    ) VALUES (
                        @EmailId, @ThreadId, @ProviderType, @HeadersJson, @BodyText, @BodyHtml,
                        @FolderTagsJson, @SizeEstimate, @ReceivedDate, @ArchivedAt, @Snippet, @SourceFolder, @UserCorrected
                    )";

                using var command = _connection.CreateCommand();
                command.CommandText = sql;

                command.Parameters.AddWithValue("@EmailId", archive.EmailId);
                command.Parameters.AddWithValue("@ThreadId", (object?)archive.ThreadId ?? DBNull.Value);
                command.Parameters.AddWithValue("@ProviderType", archive.ProviderType);
                command.Parameters.AddWithValue("@HeadersJson", archive.HeadersJson);
                command.Parameters.AddWithValue("@BodyText", (object?)archive.BodyText ?? DBNull.Value);
                command.Parameters.AddWithValue("@BodyHtml", (object?)archive.BodyHtml ?? DBNull.Value);
                command.Parameters.AddWithValue("@FolderTagsJson", archive.FolderTagsJson);
                command.Parameters.AddWithValue("@SizeEstimate", archive.SizeEstimate);
                command.Parameters.AddWithValue("@ReceivedDate", archive.ReceivedDate.ToString("O"));
                command.Parameters.AddWithValue("@ArchivedAt", archive.ArchivedAt.ToString("O"));
                command.Parameters.AddWithValue("@Snippet", (object?)archive.Snippet ?? DBNull.Value);
                command.Parameters.AddWithValue("@SourceFolder", archive.SourceFolder);
                command.Parameters.AddWithValue("@UserCorrected", archive.UserCorrected);

                await command.ExecuteNonQueryAsync(cancellationToken);
                return Result<bool>.Success(true);
            }
            finally
            {
                _connectionLock.Release();
            }
        }
        catch (Exception ex)
        {
            return Result<bool>.Failure(new StorageError($"Failed to store archive for {archive.EmailId}", ex.Message, ex));
        }
    }

    public async Task<Result<int>> StoreArchivesBatchAsync(
        IEnumerable<EmailArchiveEntry> archives,
        CancellationToken cancellationToken = default)
    {
        if (archives == null)
            return Result<int>.Failure(new ValidationError("Archives collection cannot be null"));

        var archiveList = archives.ToList();
        if (archiveList.Count == 0)
            return Result<int>.Success(0);

        // Validate all archives before starting transaction
        foreach (var archive in archiveList)
        {
            if (string.IsNullOrWhiteSpace(archive.EmailId))
                return Result<int>.Failure(new ValidationError("All archives must have valid EmailId"));

            if (string.IsNullOrWhiteSpace(archive.BodyText) && string.IsNullOrWhiteSpace(archive.BodyHtml))
                return Result<int>.Failure(new ValidationError($"Archive {archive.EmailId} must have at least one of BodyText or BodyHtml"));
        }

        try
        {
            await _connectionLock.WaitAsync(cancellationToken);
            try
            {
                var totalStored = 0;
                const int batchSize = 500; // Per research.md R5

                // Process in batches
                for (int i = 0; i < archiveList.Count; i += batchSize)
                {
                    var batch = archiveList.Skip(i).Take(batchSize);

                    using var transaction = _connection.BeginTransaction();
                    try
                    {
                        foreach (var archive in batch)
                        {
                            var result = await StoreArchiveSingleAsync(archive, cancellationToken);
                            if (!result.IsSuccess)
                            {
                                transaction.Rollback();
                                return Result<int>.Failure(result.Error);
                            }
                            totalStored++;
                        }

                        transaction.Commit();
                    }
                    catch (Exception ex)
                    {
                        transaction.Rollback();
                        return Result<int>.Failure(new StorageError("Batch archive storage failed", ex.Message, ex));
                    }
                }

                return Result<int>.Success(totalStored);
            }
            finally
            {
                _connectionLock.Release();
            }
        }
        catch (Exception ex)
        {
            return Result<int>.Failure(new StorageError("Failed to acquire lock for batch archive storage", ex.Message, ex));
        }
        finally
        {
            // T046: Add monitoring hook after successful batch operation (outside lock)
            // Update storage usage statistics (best-effort, don't fail batch on monitoring error)
            _ = await GetStorageUsageAsync();
        }
    }

    /// <summary>
    /// Helper method to store a single archive within a transaction.
    /// </summary>
    private async Task<Result<bool>> StoreArchiveSingleAsync(
        EmailArchiveEntry archive,
        CancellationToken cancellationToken)
    {
        const string sql = @"
            INSERT OR REPLACE INTO email_archive (
                EmailId, ThreadId, ProviderType, HeadersJson, BodyText, BodyHtml,
                FolderTagsJson, SizeEstimate, ReceivedDate, ArchivedAt, Snippet, SourceFolder, UserCorrected
            ) VALUES (
                @EmailId, @ThreadId, @ProviderType, @HeadersJson, @BodyText, @BodyHtml,
                @FolderTagsJson, @SizeEstimate, @ReceivedDate, @ArchivedAt, @Snippet, @SourceFolder, @UserCorrected
            )";

        using var command = _connection.CreateCommand();
        command.CommandText = sql;

        command.Parameters.AddWithValue("@EmailId", archive.EmailId);
        command.Parameters.AddWithValue("@ThreadId", (object?)archive.ThreadId ?? DBNull.Value);
        command.Parameters.AddWithValue("@ProviderType", archive.ProviderType);
        command.Parameters.AddWithValue("@HeadersJson", archive.HeadersJson);
        command.Parameters.AddWithValue("@BodyText", (object?)archive.BodyText ?? DBNull.Value);
        command.Parameters.AddWithValue("@BodyHtml", (object?)archive.BodyHtml ?? DBNull.Value);
        command.Parameters.AddWithValue("@FolderTagsJson", archive.FolderTagsJson);
        command.Parameters.AddWithValue("@SizeEstimate", archive.SizeEstimate);
        command.Parameters.AddWithValue("@ReceivedDate", archive.ReceivedDate.ToString("O"));
        command.Parameters.AddWithValue("@ArchivedAt", archive.ArchivedAt.ToString("O"));
        command.Parameters.AddWithValue("@Snippet", (object?)archive.Snippet ?? DBNull.Value);
        command.Parameters.AddWithValue("@SourceFolder", archive.SourceFolder);
        command.Parameters.AddWithValue("@UserCorrected", archive.UserCorrected);

        await command.ExecuteNonQueryAsync(cancellationToken);
        return Result<bool>.Success(true);
    }

    public async Task<Result<EmailArchiveEntry?>> GetArchiveAsync(
        string emailId,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(emailId))
            return Result<EmailArchiveEntry?>.Failure(new ValidationError("EmailId is required"));

        try
        {
            await _connectionLock.WaitAsync(cancellationToken);
            try
            {
                const string sql = @"
                    SELECT EmailId, ThreadId, ProviderType, HeadersJson, BodyText, BodyHtml,
                           FolderTagsJson, SizeEstimate, ReceivedDate, ArchivedAt, Snippet, SourceFolder, UserCorrected
                    FROM email_archive
                    WHERE EmailId = @EmailId";

                using var command = _connection.CreateCommand();
                command.CommandText = sql;
                command.Parameters.AddWithValue("@EmailId", emailId);

                using var reader = await command.ExecuteReaderAsync(cancellationToken);
                if (!await reader.ReadAsync(cancellationToken))
                    return Result<EmailArchiveEntry?>.Success(null);

                var archive = ReadArchiveFromReader(reader);
                return Result<EmailArchiveEntry?>.Success(archive);
            }
            finally
            {
                _connectionLock.Release();
            }
        }
        catch (Exception ex)
        {
            return Result<EmailArchiveEntry?>.Failure(new StorageError($"Failed to retrieve archive for {emailId}", ex.Message, ex));
        }
    }

    public async Task<Result<bool>> DeleteArchiveAsync(
        string emailId,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(emailId))
            return Result<bool>.Failure(new ValidationError("EmailId is required"));

        try
        {
            await _connectionLock.WaitAsync(cancellationToken);
            try
            {
                const string sql = "DELETE FROM email_archive WHERE EmailId = @EmailId";

                using var command = _connection.CreateCommand();
                command.CommandText = sql;
                command.Parameters.AddWithValue("@EmailId", emailId);

                var rowsAffected = await command.ExecuteNonQueryAsync(cancellationToken);
                return Result<bool>.Success(rowsAffected > 0);
            }
            finally
            {
                _connectionLock.Release();
            }
        }
        catch (Exception ex)
        {
            return Result<bool>.Failure(new StorageError($"Failed to delete archive for {emailId}", ex.Message, ex));
        }
    }

    /// <summary>
    /// Reads an EmailArchiveEntry from a data reader.
    /// </summary>
    private EmailArchiveEntry ReadArchiveFromReader(SqliteDataReader reader)
    {
        return new EmailArchiveEntry
        {
            EmailId = reader.GetString(0),
            ThreadId = reader.IsDBNull(1) ? null : reader.GetString(1),
            ProviderType = reader.GetString(2),
            HeadersJson = reader.GetString(3),
            BodyText = reader.IsDBNull(4) ? null : reader.GetString(4),
            BodyHtml = reader.IsDBNull(5) ? null : reader.GetString(5),
            FolderTagsJson = reader.GetString(6),
            SizeEstimate = reader.GetInt64(7),
            ReceivedDate = DateTime.Parse(reader.GetString(8)),
            ArchivedAt = DateTime.Parse(reader.GetString(9)),
            Snippet = reader.IsDBNull(10) ? null : reader.GetString(10),
            SourceFolder = reader.GetString(11),
            UserCorrected = reader.GetInt32(12)
        };
    }

    // ============================================================
    // Storage Monitoring & Quota
    // ============================================================

    public async Task<Result<StorageQuota>> GetStorageUsageAsync(
        CancellationToken cancellationToken = default)
    {
        try
        {
            await _connectionLock.WaitAsync(cancellationToken);
            try
            {
                // Step 1: Get or create StorageQuota record
                const string getQuotaSql = "SELECT Id, LimitBytes, CurrentBytes, FeatureBytes, ArchiveBytes, FeatureCount, ArchiveCount, UserCorrectedCount, LastCleanupAt, LastMonitoredAt FROM storage_quota WHERE Id = 1";

                StorageQuota? quota = null;
                using (var command = _connection.CreateCommand())
                {
                    command.CommandText = getQuotaSql;
                    using var reader = await command.ExecuteReaderAsync(cancellationToken);
                    if (await reader.ReadAsync(cancellationToken))
                    {
                        quota = new StorageQuota
                        {
                            Id = reader.GetInt32(0),
                            LimitBytes = reader.GetInt64(1),
                            CurrentBytes = reader.GetInt64(2),
                            FeatureBytes = reader.GetInt64(3),
                            ArchiveBytes = reader.GetInt64(4),
                            FeatureCount = reader.GetInt64(5),
                            ArchiveCount = reader.GetInt64(6),
                            UserCorrectedCount = reader.GetInt64(7),
                            LastCleanupAt = reader.IsDBNull(8) ? null : DateTime.Parse(reader.GetString(8)),
                            LastMonitoredAt = DateTime.Parse(reader.GetString(9))
                        };
                    }
                }

                // If no quota record exists, create default 50GB limit
                if (quota == null)
                {
                    const string createQuotaSql = @"
                        INSERT INTO storage_quota (Id, LimitBytes, CurrentBytes, FeatureBytes, ArchiveBytes, FeatureCount, ArchiveCount, UserCorrectedCount, LastMonitoredAt)
                        VALUES (1, @LimitBytes, 0, 0, 0, 0, 0, 0, @Now)";

                    using var createCommand = _connection.CreateCommand();
                    createCommand.CommandText = createQuotaSql;
                    createCommand.Parameters.AddWithValue("@LimitBytes", StorageQuota.DefaultLimitBytes);
                    createCommand.Parameters.AddWithValue("@Now", DateTime.UtcNow.ToString("O"));
                    await createCommand.ExecuteNonQueryAsync(cancellationToken);

                    quota = new StorageQuota
                    {
                        Id = 1,
                        LimitBytes = StorageQuota.DefaultLimitBytes,
                        CurrentBytes = 0,
                        FeatureBytes = 0,
                        ArchiveBytes = 0,
                        FeatureCount = 0,
                        ArchiveCount = 0,
                        UserCorrectedCount = 0,
                        LastCleanupAt = null,
                        LastMonitoredAt = DateTime.UtcNow
                    };
                }

                // Step 2: Calculate actual storage usage using PRAGMA page_count
                long pageCount = 0;
                long pageSize = 4096; // SQLite default, will verify

                using (var pragmaCommand = _connection.CreateCommand())
                {
                    pragmaCommand.CommandText = "PRAGMA page_count";
                    var pageCountResult = await pragmaCommand.ExecuteScalarAsync(cancellationToken);
                    if (pageCountResult != null)
                        pageCount = Convert.ToInt64(pageCountResult);

                    pragmaCommand.CommandText = "PRAGMA page_size";
                    var pageSizeResult = await pragmaCommand.ExecuteScalarAsync(cancellationToken);
                    if (pageSizeResult != null)
                        pageSize = Convert.ToInt64(pageSizeResult);
                }

                var totalDatabaseBytes = pageCount * pageSize;

                // Step 3: Get per-table sizes using dbstat (if available)
                long featureBytes = 0;
                long archiveBytes = 0;

                try
                {
                    const string dbstatSql = "SELECT name, SUM(pgsize) FROM dbstat WHERE name IN ('email_features', 'email_archive') GROUP BY name";
                    using var dbstatCommand = _connection.CreateCommand();
                    dbstatCommand.CommandText = dbstatSql;
                    using var dbstatReader = await dbstatCommand.ExecuteReaderAsync(cancellationToken);

                    while (await dbstatReader.ReadAsync(cancellationToken))
                    {
                        var tableName = dbstatReader.GetString(0);
                        var tableSize = dbstatReader.GetInt64(1);

                        if (tableName == "email_features")
                            featureBytes = tableSize;
                        else if (tableName == "email_archive")
                            archiveBytes = tableSize;
                    }
                }
                catch
                {
                    // dbstat might not be available in all SQLite builds, fall back to estimates
                    // Use SUM(length(FeatureJson)) as rough approximation
                    featureBytes = quota.FeatureBytes; // Keep previous value
                    archiveBytes = quota.ArchiveBytes;
                }

                // Step 4: Get record counts
                long featureCount = 0;
                long archiveCount = 0;
                long userCorrectedCount = 0;

                using (var countCommand = _connection.CreateCommand())
                {
                    countCommand.CommandText = "SELECT COUNT(*) FROM email_features";
                    var featureCountResult = await countCommand.ExecuteScalarAsync(cancellationToken);
                    if (featureCountResult != null)
                        featureCount = Convert.ToInt64(featureCountResult);

                    countCommand.CommandText = "SELECT COUNT(*) FROM email_archive";
                    var archiveCountResult = await countCommand.ExecuteScalarAsync(cancellationToken);
                    if (archiveCountResult != null)
                        archiveCount = Convert.ToInt64(archiveCountResult);

                    countCommand.CommandText = "SELECT COUNT(*) FROM email_archive WHERE UserCorrected = 1";
                    var correctedCountResult = await countCommand.ExecuteScalarAsync(cancellationToken);
                    if (correctedCountResult != null)
                        userCorrectedCount = Convert.ToInt64(correctedCountResult);
                }

                // Step 5: Update quota record with fresh data
                var updatedQuota = new StorageQuota
                {
                    Id = 1,
                    LimitBytes = quota.LimitBytes,
                    CurrentBytes = totalDatabaseBytes,
                    FeatureBytes = featureBytes,
                    ArchiveBytes = archiveBytes,
                    FeatureCount = featureCount,
                    ArchiveCount = archiveCount,
                    UserCorrectedCount = userCorrectedCount,
                    LastCleanupAt = quota.LastCleanupAt,
                    LastMonitoredAt = DateTime.UtcNow
                };

                const string updateSql = @"
                    UPDATE storage_quota 
                    SET CurrentBytes = @CurrentBytes,
                        FeatureBytes = @FeatureBytes,
                        ArchiveBytes = @ArchiveBytes,
                        FeatureCount = @FeatureCount,
                        ArchiveCount = @ArchiveCount,
                        UserCorrectedCount = @UserCorrectedCount,
                        LastMonitoredAt = @LastMonitoredAt
                    WHERE Id = 1";

                using (var updateCommand = _connection.CreateCommand())
                {
                    updateCommand.CommandText = updateSql;
                    updateCommand.Parameters.AddWithValue("@CurrentBytes", updatedQuota.CurrentBytes);
                    updateCommand.Parameters.AddWithValue("@FeatureBytes", updatedQuota.FeatureBytes);
                    updateCommand.Parameters.AddWithValue("@ArchiveBytes", updatedQuota.ArchiveBytes);
                    updateCommand.Parameters.AddWithValue("@FeatureCount", updatedQuota.FeatureCount);
                    updateCommand.Parameters.AddWithValue("@ArchiveCount", updatedQuota.ArchiveCount);
                    updateCommand.Parameters.AddWithValue("@UserCorrectedCount", updatedQuota.UserCorrectedCount);
                    updateCommand.Parameters.AddWithValue("@LastMonitoredAt", updatedQuota.LastMonitoredAt.ToString("O"));
                    await updateCommand.ExecuteNonQueryAsync(cancellationToken);
                }

                return Result<StorageQuota>.Success(updatedQuota);
            }
            finally
            {
                _connectionLock.Release();
            }
        }
        catch (Exception ex)
        {
            return Result<StorageQuota>.Failure(new StorageError("Failed to get storage usage", ex.Message, ex));
        }
    }

    public async Task<Result<bool>> UpdateStorageLimitAsync(
        long limitBytes,
        CancellationToken cancellationToken = default)
    {
        if (limitBytes <= 0)
            return Result<bool>.Failure(new ValidationError("Storage limit must be greater than zero"));

        try
        {
            await _connectionLock.WaitAsync(cancellationToken);
            try
            {
                // Ensure quota record exists
                const string checkSql = "SELECT COUNT(*) FROM storage_quota WHERE Id = 1";
                using var checkCommand = _connection.CreateCommand();
                checkCommand.CommandText = checkSql;
                var exists = Convert.ToInt64(await checkCommand.ExecuteScalarAsync(cancellationToken)) > 0;

                if (!exists)
                {
                    // Create default quota record first
                    const string createSql = @"
                        INSERT INTO storage_quota (Id, LimitBytes, CurrentBytes, FeatureBytes, ArchiveBytes, FeatureCount, ArchiveCount, UserCorrectedCount, LastMonitoredAt)
                        VALUES (1, @LimitBytes, 0, 0, 0, 0, 0, 0, @Now)";

                    using var createCommand = _connection.CreateCommand();
                    createCommand.CommandText = createSql;
                    createCommand.Parameters.AddWithValue("@LimitBytes", limitBytes);
                    createCommand.Parameters.AddWithValue("@Now", DateTime.UtcNow.ToString("O"));
                    await createCommand.ExecuteNonQueryAsync(cancellationToken);
                }
                else
                {
                    // Update existing record
                    const string updateSql = "UPDATE storage_quota SET LimitBytes = @LimitBytes WHERE Id = 1";
                    using var updateCommand = _connection.CreateCommand();
                    updateCommand.CommandText = updateSql;
                    updateCommand.Parameters.AddWithValue("@LimitBytes", limitBytes);
                    await updateCommand.ExecuteNonQueryAsync(cancellationToken);
                }

                return Result<bool>.Success(true);
            }
            finally
            {
                _connectionLock.Release();
            }
        }
        catch (Exception ex)
        {
            return Result<bool>.Failure(new StorageError("Failed to update storage limit", ex.Message, ex));
        }
    }

    public async Task<Result<bool>> ShouldTriggerCleanupAsync(
        CancellationToken cancellationToken = default)
    {
        try
        {
            // Get current storage usage
            var usageResult = await GetStorageUsageAsync(cancellationToken);
            if (!usageResult.IsSuccess)
                return Result<bool>.Failure(usageResult.Error);

            var quota = usageResult.Value!;

            // Calculate usage percentage
            if (quota.LimitBytes == 0)
                return Result<bool>.Success(false);

            var usagePercent = (double)quota.CurrentBytes / quota.LimitBytes * 100.0;

            // Trigger cleanup if usage >= 90% (per spec.md FR-005)
            const double cleanupThreshold = 90.0;
            return Result<bool>.Success(usagePercent >= cleanupThreshold);
        }
        catch (Exception ex)
        {
            return Result<bool>.Failure(new StorageError("Failed to check cleanup trigger", ex.Message, ex));
        }
    }

    // ============================================================
    // Automatic Cleanup
    // ============================================================

    public async Task<Result<int>> ExecuteCleanupAsync(
        int targetPercent = 80,
        CancellationToken cancellationToken = default)
    {
        if (targetPercent <= 0 || targetPercent > 100)
            return Result<int>.Failure(new ValidationError("Target percent must be between 1 and 100"));

        try
        {
            await _connectionLock.WaitAsync(cancellationToken);
            try
            {
                // Step 1: Get current storage usage
                var usageResult = await GetStorageUsageAsync(cancellationToken);
                if (!usageResult.IsSuccess)
                    return Result<int>.Failure(usageResult.Error);

                var quota = usageResult.Value!;
                var targetBytes = (long)(quota.LimitBytes * (targetPercent / 100.0));

                // If already below target, no cleanup needed
                if (quota.CurrentBytes <= targetBytes)
                    return Result<int>.Success(0);

                var bytesToFree = quota.CurrentBytes - targetBytes;

                // Step 2: Determine how many archives to delete
                // Strategy: Delete oldest non-corrected archives first, then user-corrected if needed
                // Using batches of 1000 rows per delete operation (per research.md R3)

                int totalDeleted = 0;
                const int batchSize = 1000;

                // Phase 1: Delete non-corrected archives (UserCorrected = 0)
                const string countNonCorrectedSql = "SELECT COUNT(*) FROM email_archive WHERE UserCorrected = 0";
                using var countCommand = _connection.CreateCommand();
                countCommand.CommandText = countNonCorrectedSql;
                var nonCorrectedCount = Convert.ToInt32(await countCommand.ExecuteScalarAsync(cancellationToken));

                // Estimate bytes per archive (rough approximation)
                long avgBytesPerArchive = quota.ArchiveCount > 0
                    ? quota.ArchiveBytes / quota.ArchiveCount
                    : 1024; // Default 1KB if no archives

                var archivesToDelete = (int)Math.Ceiling((double)bytesToFree / avgBytesPerArchive);

                // Delete non-corrected archives first (oldest to newest)
                int nonCorrectedDeleted = 0;
                if (nonCorrectedCount > 0)
                {
                    var toDeleteFromNonCorrected = Math.Min(archivesToDelete, nonCorrectedCount);

                    while (nonCorrectedDeleted < toDeleteFromNonCorrected && !cancellationToken.IsCancellationRequested)
                    {
                        var batchToDelete = Math.Min(batchSize, toDeleteFromNonCorrected - nonCorrectedDeleted);

                        const string deleteNonCorrectedSql = @"
                            DELETE FROM email_archive 
                            WHERE EmailId IN (
                                SELECT EmailId 
                                FROM email_archive 
                                WHERE UserCorrected = 0 
                                ORDER BY ArchivedAt ASC 
                                LIMIT @BatchSize
                            )";

                        using var deleteCommand = _connection.CreateCommand();
                        deleteCommand.CommandText = deleteNonCorrectedSql;
                        deleteCommand.Parameters.AddWithValue("@BatchSize", batchToDelete);
                        var deleted = await deleteCommand.ExecuteNonQueryAsync(cancellationToken);
                        nonCorrectedDeleted += deleted;

                        if (deleted == 0)
                            break; // No more records to delete
                    }

                    totalDeleted += nonCorrectedDeleted;
                }

                // Phase 2: If still over budget, delete user-corrected archives (oldest first)
                // Check current usage after Phase 1
                var currentUsageResult = await GetStorageUsageAsync(cancellationToken);
                if (!currentUsageResult.IsSuccess)
                    return Result<int>.Failure(currentUsageResult.Error);

                var currentQuota = currentUsageResult.Value!;
                if (currentQuota.CurrentBytes > targetBytes)
                {
                    var remainingToDelete = archivesToDelete - totalDeleted;
                    const string countCorrectedSql = "SELECT COUNT(*) FROM email_archive WHERE UserCorrected = 1";
                    countCommand.CommandText = countCorrectedSql;
                    var correctedCount = Convert.ToInt32(await countCommand.ExecuteScalarAsync(cancellationToken));

                    int correctedDeleted = 0;
                    var toDeleteFromCorrected = Math.Min(remainingToDelete, correctedCount);

                    while (correctedDeleted < toDeleteFromCorrected && !cancellationToken.IsCancellationRequested)
                    {
                        var batchToDelete = Math.Min(batchSize, toDeleteFromCorrected - correctedDeleted);

                        const string deleteCorrectedSql = @"
                            DELETE FROM email_archive 
                            WHERE EmailId IN (
                                SELECT EmailId 
                                FROM email_archive 
                                WHERE UserCorrected = 1 
                                ORDER BY ArchivedAt ASC 
                                LIMIT @BatchSize
                            )";

                        using var deleteCommand = _connection.CreateCommand();
                        deleteCommand.CommandText = deleteCorrectedSql;
                        deleteCommand.Parameters.AddWithValue("@BatchSize", batchToDelete);
                        var deleted = await deleteCommand.ExecuteNonQueryAsync(cancellationToken);
                        correctedDeleted += deleted;

                        if (deleted == 0)
                            break; // No more records to delete
                    }

                    totalDeleted += correctedDeleted;
                }

                // T062: Edge case handling - Check if we couldn't meet target due to user-corrected emails
                var finalUsageCheck = await GetStorageUsageAsync(cancellationToken);
                if (finalUsageCheck.IsSuccess && finalUsageCheck.Value!.CurrentBytes > targetBytes)
                {
                    // Still over target after cleanup - likely all/most emails are user-corrected
                    // Per spec.md edge cases: log warning and allow temporary limit exceed
                    var usagePercent = (double)finalUsageCheck.Value.CurrentBytes / finalUsageCheck.Value.LimitBytes * 100;

                    // Log warning (in production, this would use ILogger)
                    Console.WriteLine($"WARNING: Storage cleanup incomplete - {usagePercent:F1}% usage remains. " +
                                    $"UserCorrected emails: {finalUsageCheck.Value.UserCorrectedCount}/{finalUsageCheck.Value.ArchiveCount}. " +
                                    "Consider increasing storage limit or reviewing retention policy.");
                }

                // Step 3: Execute VACUUM to reclaim disk space (per research.md R3)
                if (totalDeleted > 0)
                {
                    using var vacuumCommand = _connection.CreateCommand();
                    vacuumCommand.CommandText = "VACUUM";
                    await vacuumCommand.ExecuteNonQueryAsync(cancellationToken);
                }

                // Step 4: Update LastCleanupAt timestamp
                const string updateCleanupSql = "UPDATE storage_quota SET LastCleanupAt = @Now WHERE Id = 1";
                using var updateCommand = _connection.CreateCommand();
                updateCommand.CommandText = updateCleanupSql;
                updateCommand.Parameters.AddWithValue("@Now", DateTime.UtcNow.ToString("O"));
                await updateCommand.ExecuteNonQueryAsync(cancellationToken);

                // Step 5: Refresh storage usage after cleanup
                await GetStorageUsageAsync(cancellationToken);

                return Result<int>.Success(totalDeleted);
            }
            finally
            {
                _connectionLock.Release();
            }
        }
        catch (Exception ex)
        {
            return Result<int>.Failure(new StorageError("Failed to execute cleanup", ex.Message, ex));
        }
    }

    // ============================================================
    // Database Connection Helper Methods
    // ============================================================

    /// <summary>
    /// Creates a prepared command for batch operations.
    /// Uses connection lock for thread safety.
    /// </summary>
    protected async Task<SqliteCommand> CreateCommandAsync(CancellationToken cancellationToken = default)
    {
        await _connectionLock.WaitAsync(cancellationToken);
        var command = _connection.CreateCommand();
        return command;
    }

    /// <summary>
    /// Executes a scalar query and returns the result.
    /// </summary>
    protected async Task<T?> ExecuteScalarAsync<T>(string sql, CancellationToken cancellationToken = default)
    {
        await _connectionLock.WaitAsync(cancellationToken);
        try
        {
            using var command = _connection.CreateCommand();
            command.CommandText = sql;
            var result = await command.ExecuteScalarAsync(cancellationToken);

            if (result == null || result == DBNull.Value)
                return default;

            return (T)Convert.ChangeType(result, typeof(T));
        }
        finally
        {
            _connectionLock.Release();
        }
    }

    /// <summary>
    /// Executes a non-query command and returns rows affected.
    /// </summary>
    protected async Task<int> ExecuteNonQueryAsync(string sql, CancellationToken cancellationToken = default)
    {
        await _connectionLock.WaitAsync(cancellationToken);
        try
        {
            using var command = _connection.CreateCommand();
            command.CommandText = sql;
            return await command.ExecuteNonQueryAsync(cancellationToken);
        }
        finally
        {
            _connectionLock.Release();
        }
    }
}
