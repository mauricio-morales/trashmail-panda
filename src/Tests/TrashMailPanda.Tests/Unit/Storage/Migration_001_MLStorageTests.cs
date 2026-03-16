using Microsoft.Data.Sqlite;
using System;
using System.IO;
using System.Threading.Tasks;
using TrashMailPanda.Providers.Storage.Migrations;
using Xunit;

namespace TrashMailPanda.Tests.Unit.Storage;

/// <summary>
/// Unit tests for Migration_001_MLStorage.
/// Validates schema creation and version tracking.
/// </summary>
public class Migration_001_MLStorageTests : IDisposable
{
    private readonly string _testDbPath;
    private readonly SqliteConnection _connection;

    public Migration_001_MLStorageTests()
    {
        // Create temporary test database
        _testDbPath = Path.Combine(Path.GetTempPath(), $"test_ml_migration_{Guid.NewGuid()}.db");
        var connectionString = new SqliteConnectionStringBuilder
        {
            DataSource = _testDbPath
        }.ToString();

        _connection = new SqliteConnection(connectionString);
        _connection.Open();
    }

    public void Dispose()
    {
        _connection?.Dispose();
        if (File.Exists(_testDbPath))
        {
            File.Delete(_testDbPath);
        }
    }

    [Fact]
    [Trait("Category", "Unit")]
    public async Task ApplyAsync_CreatesSchemaVersionTable()
    {
        // Act
        await Migration_001_MLStorage.ApplyAsync(_connection);

        // Assert
        var tableExists = await TableExistsAsync("schema_version");
        Assert.True(tableExists, "schema_version table should be created");
    }

    [Fact]
    [Trait("Category", "Unit")]
    public async Task ApplyAsync_CreatesEmailFeaturesTable()
    {
        // Act
        await Migration_001_MLStorage.ApplyAsync(_connection);

        // Assert
        var tableExists = await TableExistsAsync("email_features");
        Assert.True(tableExists, "email_features table should be created");

        // Verify all required columns exist
        var hasEmailId = await ColumnExistsAsync("email_features", "EmailId");
        var hasSenderDomain = await ColumnExistsAsync("email_features", "SenderDomain");
        var hasFeatureSchemaVersion = await ColumnExistsAsync("email_features", "FeatureSchemaVersion");
        var hasExtractedAt = await ColumnExistsAsync("email_features", "ExtractedAt");

        Assert.True(hasEmailId, "EmailId column should exist");
        Assert.True(hasSenderDomain, "SenderDomain column should exist");
        Assert.True(hasFeatureSchemaVersion, "FeatureSchemaVersion column should exist");
        Assert.True(hasExtractedAt, "ExtractedAt column should exist");
    }

    [Fact]
    [Trait("Category", "Unit")]
    public async Task ApplyAsync_CreatesEmailArchiveTable()
    {
        // Act
        await Migration_001_MLStorage.ApplyAsync(_connection);

        // Assert
        var tableExists = await TableExistsAsync("email_archive");
        Assert.True(tableExists, "email_archive table should be created");

        // Verify key columns exist
        var hasEmailId = await ColumnExistsAsync("email_archive", "EmailId");
        var hasProviderType = await ColumnExistsAsync("email_archive", "ProviderType");
        var hasBodyText = await ColumnExistsAsync("email_archive", "BodyText");
        var hasBodyHtml = await ColumnExistsAsync("email_archive", "BodyHtml");

        Assert.True(hasEmailId, "EmailId column should exist");
        Assert.True(hasProviderType, "ProviderType column should exist");
        Assert.True(hasBodyText, "BodyText column should exist");
        Assert.True(hasBodyHtml, "BodyHtml column should exist");
    }

    [Fact]
    [Trait("Category", "Unit")]
    public async Task ApplyAsync_CreatesStorageQuotaTable()
    {
        // Act
        await Migration_001_MLStorage.ApplyAsync(_connection);

        // Assert
        var tableExists = await TableExistsAsync("storage_quota");
        Assert.True(tableExists, "storage_quota table should be created");

        // Verify default row is initialized
        var hasDefaultRow = await HasDefaultQuotaRowAsync();
        Assert.True(hasDefaultRow, "Default storage quota row should be initialized");
    }

    [Fact]
    [Trait("Category", "Unit")]
    public async Task ApplyAsync_RecordsMigrationVersion()
    {
        // Act
        await Migration_001_MLStorage.ApplyAsync(_connection);

        // Assert
        var versionRecorded = await MigrationVersionRecordedAsync(Migration_001_MLStorage.Version);
        Assert.True(versionRecorded, $"Migration version {Migration_001_MLStorage.Version} should be recorded");
    }

    [Fact]
    [Trait("Category", "Unit")]
    public async Task ApplyAsync_IsIdempotent()
    {
        // Act - Apply migration twice
        await Migration_001_MLStorage.ApplyAsync(_connection);
        await Migration_001_MLStorage.ApplyAsync(_connection);

        // Assert - Should not throw, and version should only be recorded once
        var versionCount = await GetMigrationVersionCountAsync(Migration_001_MLStorage.Version);
        Assert.Equal(1, versionCount);
    }

    [Fact]
    [Trait("Category", "Unit")]
    public async Task ApplyAsync_CreatesRequiredIndexes()
    {
        // Act
        await Migration_001_MLStorage.ApplyAsync(_connection);

        // Assert
        var hasFeaturesExtractedAtIndex = await IndexExistsAsync("idx_features_extracted_at");
        var hasFeaturesSchemaVersionIndex = await IndexExistsAsync("idx_features_schema_version");
        var hasFeaturesUserCorrectedIndex = await IndexExistsAsync("idx_features_user_corrected");
        var hasArchiveReceivedDateIndex = await IndexExistsAsync("idx_archive_received_date");

        Assert.True(hasFeaturesExtractedAtIndex, "idx_features_extracted_at index should exist");
        Assert.True(hasFeaturesSchemaVersionIndex, "idx_features_schema_version index should exist");
        Assert.True(hasFeaturesUserCorrectedIndex, "idx_features_user_corrected index should exist");
        Assert.True(hasArchiveReceivedDateIndex, "idx_archive_received_date index should exist");
    }

    [Fact]
    [Trait("Category", "Unit")]
    public async Task ApplyAsync_ThrowsOnNullConnection()
    {
        // Act & Assert
        await Assert.ThrowsAsync<ArgumentNullException>(
            async () => await Migration_001_MLStorage.ApplyAsync(null!));
    }

    // Helper methods

    private async Task<bool> TableExistsAsync(string tableName)
    {
        const string sql = "SELECT COUNT(*) FROM sqlite_master WHERE type='table' AND name=@tableName";
        using var command = _connection.CreateCommand();
        command.CommandText = sql;
        command.Parameters.AddWithValue("@tableName", tableName);

        var result = await command.ExecuteScalarAsync();
        return Convert.ToInt64(result) > 0;
    }

    private async Task<bool> ColumnExistsAsync(string tableName, string columnName)
    {
        var sql = $"PRAGMA table_info({tableName})";
        using var command = _connection.CreateCommand();
        command.CommandText = sql;

        using var reader = await command.ExecuteReaderAsync();
        while (await reader.ReadAsync())
        {
            var name = reader.GetString(1); // Column name is at index 1
            if (name == columnName)
                return true;
        }
        return false;
    }

    private async Task<bool> IndexExistsAsync(string indexName)
    {
        const string sql = "SELECT COUNT(*) FROM sqlite_master WHERE type='index' AND name=@indexName";
        using var command = _connection.CreateCommand();
        command.CommandText = sql;
        command.Parameters.AddWithValue("@indexName", indexName);

        var result = await command.ExecuteScalarAsync();
        return Convert.ToInt64(result) > 0;
    }

    private async Task<bool> MigrationVersionRecordedAsync(int version)
    {
        const string sql = "SELECT COUNT(*) FROM schema_version WHERE version=@version";
        using var command = _connection.CreateCommand();
        command.CommandText = sql;
        command.Parameters.AddWithValue("@version", version);

        var result = await command.ExecuteScalarAsync();
        return Convert.ToInt64(result) > 0;
    }

    private async Task<int> GetMigrationVersionCountAsync(int version)
    {
        const string sql = "SELECT COUNT(*) FROM schema_version WHERE version=@version";
        using var command = _connection.CreateCommand();
        command.CommandText = sql;
        command.Parameters.AddWithValue("@version", version);

        var result = await command.ExecuteScalarAsync();
        return Convert.ToInt32(result);
    }

    private async Task<bool> HasDefaultQuotaRowAsync()
    {
        const string sql = "SELECT COUNT(*) FROM storage_quota WHERE Id=1";
        using var command = _connection.CreateCommand();
        command.CommandText = sql;

        var result = await command.ExecuteScalarAsync();
        return Convert.ToInt64(result) > 0;
    }
}
