using Microsoft.Data.Sqlite;
using System;
using System.Threading.Tasks;

namespace TrashMailPanda.Providers.Storage.Migrations;

/// <summary>
/// Initial migration for ML storage tables.
/// Creates email_features, email_archive, storage_quota, and schema_version tables.
/// </summary>
public static class Migration_001_MLStorage
{
    public const int Version = 5;
    public const string Description = "Add ML storage tables (email_features, email_archive, storage_quota)";

    /// <summary>
    /// Applies the migration to the database.
    /// Idempotent - safe to run multiple times.
    /// </summary>
    public static async Task ApplyAsync(SqliteConnection connection)
    {
        if (connection == null)
            throw new ArgumentNullException(nameof(connection));

        // Create schema version tracking table first
        await CreateSchemaVersionTableAsync(connection);

        // Check if migration already applied
        if (await IsMigrationAppliedAsync(connection, Version))
        {
            return; // Already applied, skip
        }

        // Apply migration
        await CreateEmailFeaturesTableAsync(connection);
        await CreateEmailArchiveTableAsync(connection);
        await CreateStorageQuotaTableAsync(connection);

        // Record migration version
        await RecordMigrationAsync(connection, Version, Description);
    }

    private static async Task CreateSchemaVersionTableAsync(SqliteConnection connection)
    {
        const string sql = @"
            CREATE TABLE IF NOT EXISTS schema_version (
                version INTEGER PRIMARY KEY,
                applied_at TEXT NOT NULL,
                description TEXT NOT NULL
            )";

        using var command = connection.CreateCommand();
        command.CommandText = sql;
        await command.ExecuteNonQueryAsync();
    }

    private static async Task<bool> IsMigrationAppliedAsync(SqliteConnection connection, int version)
    {
        const string sql = "SELECT COUNT(*) FROM schema_version WHERE version = @version";

        using var command = connection.CreateCommand();
        command.CommandText = sql;
        command.Parameters.AddWithValue("@version", version);

        var result = await command.ExecuteScalarAsync();
        return Convert.ToInt64(result) > 0;
    }

    private static async Task CreateEmailFeaturesTableAsync(SqliteConnection connection)
    {
        const string createTableSql = @"
            CREATE TABLE IF NOT EXISTS email_features (
                EmailId TEXT PRIMARY KEY,
                SenderDomain TEXT NOT NULL,
                SenderKnown INTEGER NOT NULL,
                ContactStrength INTEGER NOT NULL,
                SpfResult TEXT NOT NULL,
                DkimResult TEXT NOT NULL,
                DmarcResult TEXT NOT NULL,
                HasListUnsubscribe INTEGER NOT NULL,
                HasAttachments INTEGER NOT NULL,
                HourReceived INTEGER NOT NULL,
                DayOfWeek INTEGER NOT NULL,
                EmailSizeLog REAL NOT NULL,
                SubjectLength INTEGER NOT NULL,
                RecipientCount INTEGER NOT NULL,
                IsReply INTEGER NOT NULL,
                InUserWhitelist INTEGER NOT NULL,
                InUserBlacklist INTEGER NOT NULL,
                LabelCount INTEGER NOT NULL,
                LinkCount INTEGER NOT NULL,
                ImageCount INTEGER NOT NULL,
                HasTrackingPixel INTEGER NOT NULL,
                UnsubscribeLinkInBody INTEGER NOT NULL,
                EmailAgeDays INTEGER NOT NULL,
                IsInInbox INTEGER NOT NULL,
                IsStarred INTEGER NOT NULL,
                IsImportant INTEGER NOT NULL,
                WasInTrash INTEGER NOT NULL,
                WasInSpam INTEGER NOT NULL,
                IsArchived INTEGER NOT NULL,
                ThreadMessageCount INTEGER NOT NULL,
                SenderFrequency INTEGER NOT NULL,
                SubjectText TEXT,
                BodyTextShort TEXT,
                TopicClusterId INTEGER,
                TopicDistributionJson TEXT,
                SenderCategory TEXT,
                SemanticEmbeddingJson TEXT,
                FeatureSchemaVersion INTEGER NOT NULL,
                ExtractedAt TEXT NOT NULL,
                UserCorrected INTEGER NOT NULL DEFAULT 0
            )";

        const string createIndexExtractedAtSql = @"
            CREATE INDEX IF NOT EXISTS idx_features_extracted_at 
            ON email_features(ExtractedAt)";

        const string createIndexSchemaVersionSql = @"
            CREATE INDEX IF NOT EXISTS idx_features_schema_version 
            ON email_features(FeatureSchemaVersion)";

        const string createIndexUserCorrectedSql = @"
            CREATE INDEX IF NOT EXISTS idx_features_user_corrected 
            ON email_features(UserCorrected)";

        using (var command = connection.CreateCommand())
        {
            command.CommandText = createTableSql;
            await command.ExecuteNonQueryAsync();
        }

        using (var command = connection.CreateCommand())
        {
            command.CommandText = createIndexExtractedAtSql;
            await command.ExecuteNonQueryAsync();
        }

        using (var command = connection.CreateCommand())
        {
            command.CommandText = createIndexSchemaVersionSql;
            await command.ExecuteNonQueryAsync();
        }

        using (var command = connection.CreateCommand())
        {
            command.CommandText = createIndexUserCorrectedSql;
            await command.ExecuteNonQueryAsync();
        }
    }

    private static async Task CreateEmailArchiveTableAsync(SqliteConnection connection)
    {
        const string createTableSql = @"
            CREATE TABLE IF NOT EXISTS email_archive (
                EmailId TEXT PRIMARY KEY,
                ThreadId TEXT,
                ProviderType TEXT NOT NULL,
                HeadersJson TEXT NOT NULL,
                BodyText TEXT,
                BodyHtml TEXT,
                FolderTagsJson TEXT NOT NULL,
                SizeEstimate INTEGER NOT NULL,
                ReceivedDate TEXT NOT NULL,
                ArchivedAt TEXT NOT NULL,
                Snippet TEXT,
                SourceFolder TEXT NOT NULL,
                UserCorrected INTEGER NOT NULL DEFAULT 0,
                FOREIGN KEY (EmailId) REFERENCES email_features(EmailId) ON DELETE CASCADE
            )";

        const string createIndexReceivedDateSql = @"
            CREATE INDEX IF NOT EXISTS idx_archive_received_date 
            ON email_archive(ReceivedDate)";

        const string createIndexUserCorrectedDateSql = @"
            CREATE INDEX IF NOT EXISTS idx_archive_user_corrected_date 
            ON email_archive(UserCorrected, ReceivedDate)";

        const string createIndexSourceFolderSql = @"
            CREATE INDEX IF NOT EXISTS idx_archive_source_folder 
            ON email_archive(SourceFolder)";

        using (var command = connection.CreateCommand())
        {
            command.CommandText = createTableSql;
            await command.ExecuteNonQueryAsync();
        }

        using (var command = connection.CreateCommand())
        {
            command.CommandText = createIndexReceivedDateSql;
            await command.ExecuteNonQueryAsync();
        }

        using (var command = connection.CreateCommand())
        {
            command.CommandText = createIndexUserCorrectedDateSql;
            await command.ExecuteNonQueryAsync();
        }

        using (var command = connection.CreateCommand())
        {
            command.CommandText = createIndexSourceFolderSql;
            await command.ExecuteNonQueryAsync();
        }
    }

    private static async Task CreateStorageQuotaTableAsync(SqliteConnection connection)
    {
        const string createTableSql = @"
            CREATE TABLE IF NOT EXISTS storage_quota (
                Id INTEGER PRIMARY KEY CHECK (Id = 1),
                LimitBytes INTEGER NOT NULL,
                CurrentBytes INTEGER NOT NULL,
                FeatureBytes INTEGER NOT NULL,
                ArchiveBytes INTEGER NOT NULL,
                FeatureCount INTEGER NOT NULL,
                ArchiveCount INTEGER NOT NULL,
                UserCorrectedCount INTEGER NOT NULL,
                LastCleanupAt TEXT,
                LastMonitoredAt TEXT NOT NULL
            )";

        const string initializeDefaultRowSql = @"
            INSERT OR IGNORE INTO storage_quota 
            (Id, LimitBytes, CurrentBytes, FeatureBytes, ArchiveBytes, FeatureCount, ArchiveCount, UserCorrectedCount, LastCleanupAt, LastMonitoredAt)
            VALUES (1, 53687091200, 0, 0, 0, 0, 0, 0, NULL, datetime('now'))";

        using (var command = connection.CreateCommand())
        {
            command.CommandText = createTableSql;
            await command.ExecuteNonQueryAsync();
        }

        using (var command = connection.CreateCommand())
        {
            command.CommandText = initializeDefaultRowSql;
            await command.ExecuteNonQueryAsync();
        }
    }

    private static async Task RecordMigrationAsync(SqliteConnection connection, int version, string description)
    {
        const string sql = @"
            INSERT INTO schema_version (version, applied_at, description)
            VALUES (@version, @appliedAt, @description)";

        using var command = connection.CreateCommand();
        command.CommandText = sql;
        command.Parameters.AddWithValue("@version", version);
        command.Parameters.AddWithValue("@appliedAt", DateTime.UtcNow.ToString("O"));
        command.Parameters.AddWithValue("@description", description);
        await command.ExecuteNonQueryAsync();
    }
}
