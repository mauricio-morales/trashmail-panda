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
                                totalStored++;
                            else
                                throw new InvalidOperationException($"Failed to store feature: {result.Error.Message}");
                        }

                        await transaction.CommitAsync(cancellationToken);
                    }
                    catch
                    {
                        await transaction.RollbackAsync(cancellationToken);
                        throw;
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
        // TODO: Implement in Phase 5 (User Story 3)
        await Task.CompletedTask;
        return Result<StorageQuota>.Failure(new UnsupportedOperationError("Storage monitoring not yet implemented"));
    }

    public async Task<Result<bool>> UpdateStorageLimitAsync(
        long limitBytes,
        CancellationToken cancellationToken = default)
    {
        // TODO: Implement in Phase 5 (User Story 3)
        await Task.CompletedTask;
        return Result<bool>.Failure(new UnsupportedOperationError("Storage limit update not yet implemented"));
    }

    public async Task<Result<bool>> ShouldTriggerCleanupAsync(
        CancellationToken cancellationToken = default)
    {
        // TODO: Implement in Phase 5 (User Story 3)
        await Task.CompletedTask;
        return Result<bool>.Failure(new UnsupportedOperationError("Cleanup trigger check not yet implemented"));
    }

    // ============================================================
    // Automatic Cleanup
    // ============================================================

    public async Task<Result<int>> ExecuteCleanupAsync(
        int targetPercent = 80,
        CancellationToken cancellationToken = default)
    {
        // TODO: Implement in Phase 5 (User Story 3)
        await Task.CompletedTask;
        return Result<int>.Failure(new UnsupportedOperationError("Automatic cleanup not yet implemented"));
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
