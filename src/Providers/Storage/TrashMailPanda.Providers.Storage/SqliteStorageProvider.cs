using Microsoft.Data.Sqlite;
using SQLitePCL;
using System;
using System.Collections.Generic;
using System.Data;
using System.IO;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using TrashMailPanda.Shared;
using TrashMailPanda.Shared.Models;

namespace TrashMailPanda.Providers.Storage;

/// <summary>
/// SQLite implementation of IStorageProvider with encryption support
/// Provides encrypted local storage for all application data
/// </summary>
public class SqliteStorageProvider : IStorageProvider, IDisposable
{
    private SqliteConnection? _connection;
    private readonly string _databasePath;
    private readonly string _password;
    private bool _initialized = false;
    private bool _disposed = false;
    private readonly SemaphoreSlim _connectionLock = new(1, 1);

    public SqliteStorageProvider(string databasePath, string password)
    {
        _databasePath = databasePath;
        _password = password;
    }

    public async Task InitAsync()
    {
        if (_initialized) return;

        try
        {
            // Initialize SQLitePCLRaw with SQLCipher bundle
            Batteries_V2.Init();

            // Ensure directory exists
            var directory = Path.GetDirectoryName(_databasePath);
            if (!string.IsNullOrEmpty(directory) && !Directory.Exists(directory))
            {
                Directory.CreateDirectory(directory);
            }

            // Create connection with encryption
            var connectionString = new SqliteConnectionStringBuilder
            {
                DataSource = _databasePath,
                Password = _password
            }.ToString();

            _connection = new SqliteConnection(connectionString);
            await _connection.OpenAsync();

            // Create database schema
            await CreateSchemaAsync();

            _initialized = true;
        }
        catch (Exception ex)
        {
            throw new InvalidOperationException($"Failed to initialize SQLite storage: {ex.Message}", ex);
        }
    }

    public async Task<UserRules> GetUserRulesAsync()
    {
        EnsureInitialized();

        const string sql = "SELECT rule_type, rule_key, rule_value FROM user_rules";

        var alwaysKeepSenders = new List<string>();
        var alwaysKeepDomains = new List<string>();
        var alwaysKeepListIds = new List<string>();
        var autoTrashSenders = new List<string>();
        var autoTrashDomains = new List<string>();
        var autoTrashListIds = new List<string>();

        using var command = _connection!.CreateCommand();
        command.CommandText = sql;

        using var reader = await command.ExecuteReaderAsync();
        while (await reader.ReadAsync())
        {
            var ruleType = reader.GetString(0);
            var ruleKey = reader.GetString(1);
            var ruleValue = reader.GetString(2);

            switch (ruleType)
            {
                case "always_keep" when ruleKey == "sender":
                    alwaysKeepSenders.Add(ruleValue);
                    break;
                case "always_keep" when ruleKey == "domain":
                    alwaysKeepDomains.Add(ruleValue);
                    break;
                case "always_keep" when ruleKey == "listid":
                    alwaysKeepListIds.Add(ruleValue);
                    break;
                case "auto_trash" when ruleKey == "sender":
                    autoTrashSenders.Add(ruleValue);
                    break;
                case "auto_trash" when ruleKey == "domain":
                    autoTrashDomains.Add(ruleValue);
                    break;
                case "auto_trash" when ruleKey == "listid":
                    autoTrashListIds.Add(ruleValue);
                    break;
            }
        }

        return new UserRules
        {
            AlwaysKeep = new AlwaysKeepRules
            {
                Senders = alwaysKeepSenders,
                Domains = alwaysKeepDomains,
                ListIds = alwaysKeepListIds
            },
            AutoTrash = new AutoTrashRules
            {
                Senders = autoTrashSenders,
                Domains = autoTrashDomains,
                ListIds = autoTrashListIds
            }
        };
    }

    public async Task UpdateUserRulesAsync(UserRules rules)
    {
        EnsureInitialized();

        using var transaction = _connection!.BeginTransaction();
        try
        {
            // Clear existing rules
            using (var deleteCommand = _connection.CreateCommand())
            {
                deleteCommand.CommandText = "DELETE FROM user_rules";
                await deleteCommand.ExecuteNonQueryAsync();
            }

            // Insert new rules
            const string insertSql = "INSERT INTO user_rules (rule_type, rule_key, rule_value, created_at, updated_at) VALUES (@type, @key, @value, @now, @now)";
            var now = DateTime.UtcNow.ToString("O");

            async Task InsertRules(string ruleType, string ruleKey, IEnumerable<string> values)
            {
                foreach (var value in values)
                {
                    using var command = _connection.CreateCommand();
                    command.CommandText = insertSql;
                    command.Parameters.AddWithValue("@type", ruleType);
                    command.Parameters.AddWithValue("@key", ruleKey);
                    command.Parameters.AddWithValue("@value", value);
                    command.Parameters.AddWithValue("@now", now);
                    await command.ExecuteNonQueryAsync();
                }
            }

            // Insert always keep rules
            await InsertRules("always_keep", "sender", rules.AlwaysKeep.Senders);
            await InsertRules("always_keep", "domain", rules.AlwaysKeep.Domains);
            await InsertRules("always_keep", "listid", rules.AlwaysKeep.ListIds);

            // Insert auto trash rules
            await InsertRules("auto_trash", "sender", rules.AutoTrash.Senders);
            await InsertRules("auto_trash", "domain", rules.AutoTrash.Domains);
            await InsertRules("auto_trash", "listid", rules.AutoTrash.ListIds);

            await transaction.CommitAsync();
        }
        catch
        {
            await transaction.RollbackAsync();
            throw;
        }
    }

    public async Task<EmailMetadata?> GetEmailMetadataAsync(string emailId)
    {
        EnsureInitialized();

        const string sql = @"
            SELECT email_id, folder_id, subject, sender_email, sender_name, received_date, 
                   classification, confidence, reasons, bulk_key, last_classified, 
                   user_action, user_action_timestamp
            FROM email_metadata 
            WHERE email_id = @emailId";

        using var command = _connection!.CreateCommand();
        command.CommandText = sql;
        command.Parameters.AddWithValue("@emailId", emailId);

        using var reader = await command.ExecuteReaderAsync();
        if (!await reader.ReadAsync())
            return null;

        var reasonsJson = reader.IsDBNull(8) ? null : reader.GetString(8);
        var reasons = string.IsNullOrEmpty(reasonsJson) ? null :
            JsonSerializer.Deserialize<string[]>(reasonsJson);

        var userActionStr = reader.IsDBNull(11) ? null : reader.GetString(11);
        UserAction? userAction = string.IsNullOrEmpty(userActionStr) ? null :
            Enum.Parse<UserAction>(userActionStr);

        return new EmailMetadata
        {
            Id = reader.GetString(0),
            Classification = reader.IsDBNull(6) ? null : reader.GetString(6),
            Confidence = reader.IsDBNull(7) ? null : reader.GetDouble(7),
            Reasons = reasons,
            BulkKey = reader.IsDBNull(9) ? null : reader.GetString(9),
            LastClassified = reader.IsDBNull(10) ? DateTime.MinValue : DateTime.Parse(reader.GetString(10)),
            UserAction = userAction,
            UserActionTimestamp = reader.IsDBNull(12) ? null : DateTime.Parse(reader.GetString(12))
        };
    }

    public async Task SetEmailMetadataAsync(string emailId, EmailMetadata metadata)
    {
        EnsureInitialized();

        const string sql = @"
            INSERT OR REPLACE INTO email_metadata 
            (email_id, classification, confidence, reasons, bulk_key, last_classified, user_action, user_action_timestamp)
            VALUES (@emailId, @classification, @confidence, @reasons, @bulkKey, @lastClassified, @userAction, @userActionTimestamp)";

        using var command = _connection!.CreateCommand();
        command.CommandText = sql;
        command.Parameters.AddWithValue("@emailId", emailId);
        command.Parameters.AddWithValue("@classification", metadata.Classification ?? (object)DBNull.Value);
        command.Parameters.AddWithValue("@confidence", metadata.Confidence ?? (object)DBNull.Value);
        command.Parameters.AddWithValue("@reasons", metadata.Reasons != null ? JsonSerializer.Serialize(metadata.Reasons) : DBNull.Value);
        command.Parameters.AddWithValue("@bulkKey", metadata.BulkKey ?? (object)DBNull.Value);
        command.Parameters.AddWithValue("@lastClassified", metadata.LastClassified.ToString("O"));
        command.Parameters.AddWithValue("@userAction", metadata.UserAction?.ToString() ?? (object)DBNull.Value);
        command.Parameters.AddWithValue("@userActionTimestamp", metadata.UserActionTimestamp?.ToString("O") ?? (object)DBNull.Value);

        await command.ExecuteNonQueryAsync();
    }

    public async Task BulkSetEmailMetadataAsync(IReadOnlyList<EmailMetadataEntry> entries)
    {
        EnsureInitialized();

        using var transaction = _connection!.BeginTransaction();
        try
        {
            foreach (var entry in entries)
            {
                await SetEmailMetadataAsync(entry.Id, entry.Metadata);
            }
            await transaction.CommitAsync();
        }
        catch
        {
            await transaction.RollbackAsync();
            throw;
        }
    }

    public async Task<IReadOnlyList<ClassificationHistoryItem>> GetClassificationHistoryAsync(HistoryFilters? filters = null)
    {
        EnsureInitialized();

        var sql = "SELECT timestamp, email_id, classification, confidence, reasons, user_action, user_feedback FROM classification_history WHERE 1=1";
        var parameters = new List<SqliteParameter>();

        if (filters != null)
        {
            if (filters.After.HasValue)
            {
                sql += " AND timestamp >= @after";
                parameters.Add(new SqliteParameter("@after", filters.After.Value.ToString("O")));
            }

            if (filters.Before.HasValue)
            {
                sql += " AND timestamp <= @before";
                parameters.Add(new SqliteParameter("@before", filters.Before.Value.ToString("O")));
            }

            if (filters.Limit.HasValue)
            {
                sql += " LIMIT @limit";
                parameters.Add(new SqliteParameter("@limit", filters.Limit.Value));
            }
        }

        sql += " ORDER BY timestamp DESC";

        using var command = _connection!.CreateCommand();
        command.CommandText = sql;
        command.Parameters.AddRange(parameters.ToArray());

        var results = new List<ClassificationHistoryItem>();
        using var reader = await command.ExecuteReaderAsync();

        while (await reader.ReadAsync())
        {
            var reasonsJson = reader.IsDBNull(4) ? "[]" : reader.GetString(4);
            var reasons = JsonSerializer.Deserialize<string[]>(reasonsJson) ?? Array.Empty<string>();

            var userFeedbackStr = reader.IsDBNull(6) ? null : reader.GetString(6);
            UserFeedback? userFeedback = string.IsNullOrEmpty(userFeedbackStr) ? null :
                Enum.Parse<UserFeedback>(userFeedbackStr);

            results.Add(new ClassificationHistoryItem
            {
                Timestamp = DateTime.Parse(reader.GetString(0)),
                EmailId = reader.GetString(1),
                Classification = reader.GetString(2),
                Confidence = reader.GetDouble(3),
                Reasons = reasons,
                UserAction = reader.IsDBNull(5) ? null : reader.GetString(5),
                UserFeedback = userFeedback
            });
        }

        return results;
    }

    public async Task AddClassificationResultAsync(ClassificationHistoryItem result)
    {
        EnsureInitialized();

        const string sql = @"
            INSERT INTO classification_history 
            (timestamp, email_id, classification, confidence, reasons, user_action, user_feedback)
            VALUES (@timestamp, @emailId, @classification, @confidence, @reasons, @userAction, @userFeedback)";

        using var command = _connection!.CreateCommand();
        command.CommandText = sql;
        command.Parameters.AddWithValue("@timestamp", result.Timestamp.ToString("O"));
        command.Parameters.AddWithValue("@emailId", result.EmailId);
        command.Parameters.AddWithValue("@classification", result.Classification);
        command.Parameters.AddWithValue("@confidence", result.Confidence);
        command.Parameters.AddWithValue("@reasons", JsonSerializer.Serialize(result.Reasons));
        command.Parameters.AddWithValue("@userAction", result.UserAction ?? (object)DBNull.Value);
        command.Parameters.AddWithValue("@userFeedback", result.UserFeedback?.ToString() ?? (object)DBNull.Value);

        await command.ExecuteNonQueryAsync();
    }

    public async Task<IReadOnlyDictionary<string, string>> GetEncryptedTokensAsync()
    {
        EnsureInitialized();

        const string sql = "SELECT provider, encrypted_token FROM encrypted_tokens";

        var tokens = new Dictionary<string, string>();
        using var command = _connection!.CreateCommand();
        command.CommandText = sql;

        using var reader = await command.ExecuteReaderAsync();
        while (await reader.ReadAsync())
        {
            tokens[reader.GetString(0)] = reader.GetString(1);
        }

        return tokens;
    }

    public async Task SetEncryptedTokenAsync(string provider, string encryptedToken)
    {
        EnsureInitialized();

        const string sql = @"
            INSERT OR REPLACE INTO encrypted_tokens (provider, encrypted_token, created_at)
            VALUES (@provider, @encryptedToken, @createdAt)";

        using var command = _connection!.CreateCommand();
        command.CommandText = sql;
        command.Parameters.AddWithValue("@provider", provider);
        command.Parameters.AddWithValue("@encryptedToken", encryptedToken);
        command.Parameters.AddWithValue("@createdAt", DateTime.UtcNow.ToString("O"));

        await command.ExecuteNonQueryAsync();
    }

    public async Task<string?> GetEncryptedCredentialAsync(string key)
    {
        const string sql = "SELECT encrypted_value FROM encrypted_credentials WHERE key = @key";

        var command = await CreateCommandAsync();
        try
        {
            command.CommandText = sql;
            command.Parameters.AddWithValue("@key", key);

            using var reader = await command.ExecuteReaderAsync();
            if (await reader.ReadAsync())
            {
                return reader.GetString(0);
            }

            return null;
        }
        finally
        {
            command.Dispose();
            ReleaseConnection();
        }
    }

    public async Task SetEncryptedCredentialAsync(string key, string encryptedValue, DateTime? expiresAt = null)
    {
        const string sql = @"
            INSERT OR REPLACE INTO encrypted_credentials (key, encrypted_value, created_at, expires_at)
            VALUES (@key, @encryptedValue, @createdAt, @expiresAt)";

        var command = await CreateCommandAsync();
        try
        {
            command.CommandText = sql;
            command.Parameters.AddWithValue("@key", key);
            command.Parameters.AddWithValue("@encryptedValue", encryptedValue);
            command.Parameters.AddWithValue("@createdAt", DateTime.UtcNow.ToString("O"));
            command.Parameters.AddWithValue("@expiresAt", expiresAt?.ToString("O") ?? (object)DBNull.Value);

            await command.ExecuteNonQueryAsync();
        }
        finally
        {
            command.Dispose();
            ReleaseConnection();
        }
    }

    public async Task RemoveEncryptedCredentialAsync(string key)
    {
        const string sql = "DELETE FROM encrypted_credentials WHERE key = @key";

        var command = await CreateCommandAsync();
        try
        {
            command.CommandText = sql;
            command.Parameters.AddWithValue("@key", key);

            await command.ExecuteNonQueryAsync();
        }
        finally
        {
            command.Dispose();
            ReleaseConnection();
        }
    }

    public async Task<IReadOnlyList<string>> GetExpiredCredentialKeysAsync()
    {
        EnsureInitialized();

        const string sql = @"
            SELECT key FROM encrypted_credentials 
            WHERE expires_at IS NOT NULL AND expires_at <= @currentTime";

        var keys = new List<string>();
        using var command = _connection!.CreateCommand();
        command.CommandText = sql;
        command.Parameters.AddWithValue("@currentTime", DateTime.UtcNow.ToString("O"));

        using var reader = await command.ExecuteReaderAsync();
        while (await reader.ReadAsync())
        {
            keys.Add(reader.GetString(0));
        }

        return keys;
    }

    public async Task<IReadOnlyList<string>> GetAllEncryptedCredentialKeysAsync()
    {
        EnsureInitialized();

        const string sql = "SELECT key FROM encrypted_credentials";

        var keys = new List<string>();
        using var command = _connection!.CreateCommand();
        command.CommandText = sql;

        using var reader = await command.ExecuteReaderAsync();
        while (await reader.ReadAsync())
        {
            keys.Add(reader.GetString(0));
        }

        return keys;
    }

    public async Task<AppConfig> GetConfigAsync()
    {
        EnsureInitialized();

        const string sql = "SELECT key, value FROM app_config";

        var config = new AppConfig();
        using var command = _connection!.CreateCommand();
        command.CommandText = sql;

        using var reader = await command.ExecuteReaderAsync();
        while (await reader.ReadAsync())
        {
            var key = reader.GetString(0);
            var value = reader.GetString(1);

            switch (key)
            {
                case "ConnectionState":
                    config.ConnectionState = JsonSerializer.Deserialize<TrashMailPanda.Shared.ConnectionState>(value);
                    break;
                case "ProcessingSettings":
                    config.ProcessingSettings = JsonSerializer.Deserialize<ProcessingSettings>(value);
                    break;
                case "UISettings":
                    config.UISettings = JsonSerializer.Deserialize<UISettings>(value);
                    break;
            }
        }

        return config;
    }

    public async Task UpdateConfigAsync(AppConfig config)
    {
        EnsureInitialized();

        using var transaction = _connection!.BeginTransaction();
        try
        {
            await SetConfigValueAsync("ConnectionState", config.ConnectionState);
            await SetConfigValueAsync("ProcessingSettings", config.ProcessingSettings);
            await SetConfigValueAsync("UISettings", config.UISettings);

            await transaction.CommitAsync();
        }
        catch
        {
            await transaction.RollbackAsync();
            throw;
        }
    }

    private async Task SetConfigValueAsync(string key, object? value)
    {
        const string sql = "INSERT OR REPLACE INTO app_config (key, value) VALUES (@key, @value)";

        using var command = _connection!.CreateCommand();
        command.CommandText = sql;
        command.Parameters.AddWithValue("@key", key);
        command.Parameters.AddWithValue("@value", value != null ? JsonSerializer.Serialize(value) : DBNull.Value);

        await command.ExecuteNonQueryAsync();
    }

    private async Task CreateSchemaAsync()
    {
        var schemaCommands = new[]
        {
            @"CREATE TABLE IF NOT EXISTS user_rules (
                id INTEGER PRIMARY KEY AUTOINCREMENT,
                rule_type TEXT NOT NULL,
                rule_key TEXT NOT NULL,
                rule_value TEXT NOT NULL,
                created_at TEXT NOT NULL,
                updated_at TEXT NOT NULL
            )",

            @"CREATE TABLE IF NOT EXISTS email_metadata (
                email_id TEXT PRIMARY KEY,
                folder_id TEXT,
                folder_name TEXT,
                subject TEXT,
                sender_email TEXT,
                sender_name TEXT,
                received_date TEXT,
                classification TEXT,
                confidence REAL,
                reasons TEXT,
                bulk_key TEXT,
                last_classified TEXT,
                user_action TEXT,
                user_action_timestamp TEXT,
                processing_batch_id TEXT
            )",

            @"CREATE INDEX IF NOT EXISTS idx_email_classification ON email_metadata(classification)",
            @"CREATE INDEX IF NOT EXISTS idx_email_user_action ON email_metadata(user_action)",

            @"CREATE TABLE IF NOT EXISTS classification_history (
                id INTEGER PRIMARY KEY AUTOINCREMENT,
                timestamp TEXT NOT NULL,
                email_id TEXT NOT NULL,
                classification TEXT NOT NULL,
                confidence REAL NOT NULL,
                reasons TEXT NOT NULL,
                user_action TEXT,
                user_feedback TEXT,
                batch_id TEXT
            )",

            @"CREATE INDEX IF NOT EXISTS idx_classification_timestamp ON classification_history(timestamp)",
            @"CREATE INDEX IF NOT EXISTS idx_classification_email ON classification_history(email_id)",

            @"CREATE TABLE IF NOT EXISTS app_config (
                key TEXT PRIMARY KEY,
                value TEXT NOT NULL
            )",

            @"CREATE TABLE IF NOT EXISTS encrypted_tokens (
                provider TEXT PRIMARY KEY,
                encrypted_token TEXT NOT NULL,
                created_at TEXT NOT NULL
            )",

            @"CREATE TABLE IF NOT EXISTS encrypted_credentials (
                key TEXT PRIMARY KEY,
                encrypted_value TEXT NOT NULL,
                created_at TEXT NOT NULL,
                expires_at TEXT
            )",

            // Contacts cache tables
            @"CREATE TABLE IF NOT EXISTS contacts (
                contact_id TEXT PRIMARY KEY,
                primary_email TEXT NOT NULL,
                display_name TEXT,
                given_name TEXT,
                family_name TEXT,
                organization_name TEXT,
                organization_title TEXT,
                relationship_strength REAL NOT NULL DEFAULT 0.0,
                created_at TEXT NOT NULL,
                updated_at TEXT NOT NULL
            )",

            @"CREATE INDEX IF NOT EXISTS idx_contacts_primary_email ON contacts(primary_email)",
            @"CREATE INDEX IF NOT EXISTS idx_contacts_display_name ON contacts(display_name)",

            @"CREATE TABLE IF NOT EXISTS contact_emails (
                id INTEGER PRIMARY KEY AUTOINCREMENT,
                contact_id TEXT NOT NULL,
                email TEXT NOT NULL,
                is_primary INTEGER NOT NULL DEFAULT 0,
                created_at TEXT NOT NULL,
                FOREIGN KEY (contact_id) REFERENCES contacts (contact_id) ON DELETE CASCADE
            )",

            @"CREATE INDEX IF NOT EXISTS idx_contact_emails_email ON contact_emails(email)",
            @"CREATE INDEX IF NOT EXISTS idx_contact_emails_contact_id ON contact_emails(contact_id)",
            @"CREATE UNIQUE INDEX IF NOT EXISTS idx_contact_emails_unique ON contact_emails(contact_id, email)",

            @"CREATE TABLE IF NOT EXISTS trust_signals (
                contact_id TEXT PRIMARY KEY,
                strength TEXT NOT NULL,
                score REAL NOT NULL,
                last_interaction_date TEXT,
                justification TEXT NOT NULL DEFAULT '[]',
                computed_at TEXT NOT NULL,
                interaction_count INTEGER NOT NULL DEFAULT 0,
                email_address TEXT,
                source_type TEXT,
                created_at TEXT NOT NULL,
                updated_at TEXT NOT NULL,
                FOREIGN KEY (contact_id) REFERENCES contacts (contact_id) ON DELETE CASCADE
            )",

            @"CREATE INDEX IF NOT EXISTS idx_trust_signals_score ON trust_signals(score)",
            @"CREATE INDEX IF NOT EXISTS idx_trust_signals_strength ON trust_signals(strength)",
            @"CREATE INDEX IF NOT EXISTS idx_trust_signals_computed_at ON trust_signals(computed_at)"
        };

        foreach (var sql in schemaCommands)
        {
            using var command = _connection!.CreateCommand();
            command.CommandText = sql;
            await command.ExecuteNonQueryAsync();
        }
    }

    private void EnsureInitialized()
    {
        if (!_initialized || _connection == null)
            throw new InvalidOperationException("Storage provider not initialized. Call InitAsync first.");

        if (_disposed)
            throw new ObjectDisposedException(nameof(SqliteStorageProvider));
    }

    private async Task<SqliteCommand> CreateCommandAsync()
    {
        EnsureInitialized();
        await _connectionLock.WaitAsync();

        try
        {
            if (_connection == null)
                throw new InvalidOperationException("Database connection is null");

            return _connection.CreateCommand();
        }
        catch
        {
            _connectionLock.Release();
            throw;
        }
    }

    private void ReleaseConnection()
    {
        _connectionLock.Release();
    }

    // Contacts cache implementation
    public async Task<BasicContactInfo?> GetContactAsync(string contactId)
    {
        if (string.IsNullOrWhiteSpace(contactId))
            return null;

        const string sql = @"
            SELECT contact_id, primary_email, display_name, given_name, family_name, 
                   organization_name, organization_title, relationship_strength
            FROM contacts 
            WHERE contact_id = @contactId";

        var command = await CreateCommandAsync();
        try
        {
            command.CommandText = sql;
            command.Parameters.AddWithValue("@contactId", contactId);

            using var reader = await command.ExecuteReaderAsync();
            if (!await reader.ReadAsync())
                return null;

            // Get all emails for this contact
            var emails = await GetContactEmailsAsync(contactId);

            // Get trust score for this contact
            var trustSignal = await GetTrustSignalAsync(contactId);

            return new BasicContactInfo
            {
                Id = reader.GetString(0),
                PrimaryEmail = reader.GetString(1),
                AllEmails = emails,
                DisplayName = reader.IsDBNull(2) ? string.Empty : reader.GetString(2),
                GivenName = reader.IsDBNull(3) ? null : reader.GetString(3),
                FamilyName = reader.IsDBNull(4) ? null : reader.GetString(4),
                OrganizationName = reader.IsDBNull(5) ? null : reader.GetString(5),
                OrganizationTitle = reader.IsDBNull(6) ? null : reader.GetString(6),
                Strength = trustSignal?.Strength ?? RelationshipStrength.None,
                TrustScore = trustSignal?.Score ?? 0.0
            };
        }
        finally
        {
            command.Dispose();
            ReleaseConnection();
        }
    }

    public async Task SetContactAsync(BasicContactInfo contact)
    {
        if (contact == null) return;

        using var transaction = _connection!.BeginTransaction();
        try
        {
            // Insert or update contact
            const string contactSql = @"
                INSERT OR REPLACE INTO contacts 
                (contact_id, primary_email, display_name, given_name, family_name, 
                 organization_name, organization_title, relationship_strength, 
                 created_at, updated_at)
                VALUES (@contactId, @primaryEmail, @displayName, @givenName, @familyName,
                        @organizationName, @organizationTitle, @relationshipStrength, @now, @now)";

            using (var command = _connection.CreateCommand())
            {
                var now = DateTime.UtcNow.ToString("O");
                command.CommandText = contactSql;
                command.Parameters.AddWithValue("@contactId", contact.Id);
                command.Parameters.AddWithValue("@primaryEmail", contact.PrimaryEmail);
                command.Parameters.AddWithValue("@displayName", contact.DisplayName ?? (object)DBNull.Value);
                command.Parameters.AddWithValue("@givenName", contact.GivenName ?? (object)DBNull.Value);
                command.Parameters.AddWithValue("@familyName", contact.FamilyName ?? (object)DBNull.Value);
                command.Parameters.AddWithValue("@organizationName", contact.OrganizationName ?? (object)DBNull.Value);
                command.Parameters.AddWithValue("@organizationTitle", contact.OrganizationTitle ?? (object)DBNull.Value);
                command.Parameters.AddWithValue("@relationshipStrength", (double)contact.Strength);
                command.Parameters.AddWithValue("@now", now);

                await command.ExecuteNonQueryAsync();
            }

            // Delete existing emails for this contact
            using (var deleteCommand = _connection.CreateCommand())
            {
                deleteCommand.CommandText = "DELETE FROM contact_emails WHERE contact_id = @contactId";
                deleteCommand.Parameters.AddWithValue("@contactId", contact.Id);
                await deleteCommand.ExecuteNonQueryAsync();
            }

            // Insert contact emails
            const string emailSql = @"
                INSERT INTO contact_emails (contact_id, email, is_primary, created_at)
                VALUES (@contactId, @email, @isPrimary, @now)";

            foreach (var email in contact.AllEmails)
            {
                using var command = _connection.CreateCommand();
                var now = DateTime.UtcNow.ToString("O");
                command.CommandText = emailSql;
                command.Parameters.AddWithValue("@contactId", contact.Id);
                command.Parameters.AddWithValue("@email", email.ToLowerInvariant());
                command.Parameters.AddWithValue("@isPrimary", email.Equals(contact.PrimaryEmail, StringComparison.OrdinalIgnoreCase) ? 1 : 0);
                command.Parameters.AddWithValue("@now", now);
                await command.ExecuteNonQueryAsync();
            }

            await transaction.CommitAsync();
        }
        catch
        {
            await transaction.RollbackAsync();
            throw;
        }
    }

    public async Task<string?> GetContactIdByEmailAsync(string normalizedEmail)
    {
        if (string.IsNullOrWhiteSpace(normalizedEmail))
            return null;

        const string sql = "SELECT contact_id FROM contact_emails WHERE email = @email LIMIT 1";

        var command = await CreateCommandAsync();
        try
        {
            command.CommandText = sql;
            command.Parameters.AddWithValue("@email", normalizedEmail.ToLowerInvariant());

            using var reader = await command.ExecuteReaderAsync();
            if (await reader.ReadAsync())
            {
                return reader.GetString(0);
            }

            return null;
        }
        finally
        {
            command.Dispose();
            ReleaseConnection();
        }
    }

    public async Task<TrustSignalInfo?> GetTrustSignalAsync(string contactId)
    {
        if (string.IsNullOrWhiteSpace(contactId))
            return null;

        const string sql = @"
            SELECT contact_id, strength, score, last_interaction_date, justification,
                   computed_at, interaction_count, email_address, source_type
            FROM trust_signals 
            WHERE contact_id = @contactId";

        var command = await CreateCommandAsync();
        try
        {
            command.CommandText = sql;
            command.Parameters.AddWithValue("@contactId", contactId);

            using var reader = await command.ExecuteReaderAsync();
            if (!await reader.ReadAsync())
                return null;

            var justificationJson = reader.IsDBNull(4) ? "[]" : reader.GetString(4);
            var justification = JsonSerializer.Deserialize<List<string>>(justificationJson) ?? new List<string>();

            var lastInteractionDate = reader.IsDBNull(3) ? (DateTime?)null : DateTime.Parse(reader.GetString(3));

            return new TrustSignalInfo
            {
                ContactId = reader.GetString(0),
                Strength = Enum.Parse<RelationshipStrength>(reader.GetString(1)),
                Score = reader.GetDouble(2),
                LastInteractionDate = lastInteractionDate,
                Justification = justification,
                ComputedAt = DateTime.Parse(reader.GetString(5)),
                InteractionCount = reader.GetInt32(6),
                EmailAddress = reader.IsDBNull(7) ? string.Empty : reader.GetString(7),
                Known = true, // If it's in the database, it's known
                SourceType = reader.IsDBNull(8) ? null : reader.GetString(8)
            };
        }
        finally
        {
            command.Dispose();
            ReleaseConnection();
        }
    }

    public async Task SetTrustSignalAsync(TrustSignalInfo trustSignal)
    {
        if (trustSignal == null) return;

        const string sql = @"
            INSERT OR REPLACE INTO trust_signals 
            (contact_id, strength, score, last_interaction_date, justification,
             computed_at, interaction_count, email_address, source_type, created_at, updated_at)
            VALUES (@contactId, @strength, @score, @lastInteractionDate, @justification,
                    @computedAt, @interactionCount, @emailAddress, @sourceType, @now, @now)";

        var command = await CreateCommandAsync();
        try
        {
            var now = DateTime.UtcNow.ToString("O");
            command.CommandText = sql;
            command.Parameters.AddWithValue("@contactId", trustSignal.ContactId);
            command.Parameters.AddWithValue("@strength", trustSignal.Strength.ToString());
            command.Parameters.AddWithValue("@score", trustSignal.Score);
            command.Parameters.AddWithValue("@lastInteractionDate", trustSignal.LastInteractionDate?.ToString("O") ?? (object)DBNull.Value);
            command.Parameters.AddWithValue("@justification", JsonSerializer.Serialize(trustSignal.Justification));
            command.Parameters.AddWithValue("@computedAt", trustSignal.ComputedAt.ToString("O"));
            command.Parameters.AddWithValue("@interactionCount", trustSignal.InteractionCount);
            command.Parameters.AddWithValue("@emailAddress", trustSignal.EmailAddress ?? (object)DBNull.Value);
            command.Parameters.AddWithValue("@sourceType", trustSignal.SourceType ?? (object)DBNull.Value);
            command.Parameters.AddWithValue("@now", now);

            await command.ExecuteNonQueryAsync();
        }
        finally
        {
            command.Dispose();
            ReleaseConnection();
        }
    }

    public async Task RemoveContactAsync(string contactId)
    {
        if (string.IsNullOrWhiteSpace(contactId)) return;

        using var transaction = _connection!.BeginTransaction();
        try
        {
            // Remove trust signals
            using (var command = _connection.CreateCommand())
            {
                command.CommandText = "DELETE FROM trust_signals WHERE contact_id = @contactId";
                command.Parameters.AddWithValue("@contactId", contactId);
                await command.ExecuteNonQueryAsync();
            }

            // Remove emails
            using (var command = _connection.CreateCommand())
            {
                command.CommandText = "DELETE FROM contact_emails WHERE contact_id = @contactId";
                command.Parameters.AddWithValue("@contactId", contactId);
                await command.ExecuteNonQueryAsync();
            }

            // Remove contact
            using (var command = _connection.CreateCommand())
            {
                command.CommandText = "DELETE FROM contacts WHERE contact_id = @contactId";
                command.Parameters.AddWithValue("@contactId", contactId);
                await command.ExecuteNonQueryAsync();
            }

            await transaction.CommitAsync();
        }
        catch
        {
            await transaction.RollbackAsync();
            throw;
        }
    }

    public async Task ClearContactsCacheAsync()
    {
        using var transaction = _connection!.BeginTransaction();
        try
        {
            var tables = new[] { "trust_signals", "contact_emails", "contacts" };

            foreach (var table in tables)
            {
                using var command = _connection.CreateCommand();
                command.CommandText = $"DELETE FROM {table}";
                await command.ExecuteNonQueryAsync();
            }

            await transaction.CommitAsync();
        }
        catch
        {
            await transaction.RollbackAsync();
            throw;
        }
    }

    private async Task<List<string>> GetContactEmailsAsync(string contactId)
    {
        const string sql = "SELECT email FROM contact_emails WHERE contact_id = @contactId";

        using var command = _connection!.CreateCommand();
        command.CommandText = sql;
        command.Parameters.AddWithValue("@contactId", contactId);

        var emails = new List<string>();
        using var reader = await command.ExecuteReaderAsync();
        while (await reader.ReadAsync())
        {
            emails.Add(reader.GetString(0));
        }

        return emails;
    }

    public void Dispose()
    {
        if (_disposed) return;

        _connectionLock.Wait();
        try
        {
            if (_connection != null)
            {
                // CRITICAL FIX: Force WAL checkpoint before closing connection to release file locks
                // This is essential on Windows where SQLite WAL mode can keep auxiliary files locked
                try
                {
                    if (_connection.State == System.Data.ConnectionState.Open)
                    {
                        // Execute WAL checkpoint to merge WAL into main database and release locks
                        using var checkpointCmd = _connection.CreateCommand();
                        checkpointCmd.CommandText = "PRAGMA wal_checkpoint(TRUNCATE);";
                        checkpointCmd.ExecuteNonQuery();

                        // Also ensure journal mode is properly closed
                        using var journalCmd = _connection.CreateCommand();
                        journalCmd.CommandText = "PRAGMA journal_mode=DELETE;";
                        journalCmd.ExecuteNonQuery();
                    }
                }
                catch (Exception ex)
                {
                    // Log but don't throw - disposal should be robust
                    System.Diagnostics.Debug.WriteLine($"WAL checkpoint failed during disposal: {ex.Message}");
                }

                // Now close and dispose the connection
                try
                {
                    if (_connection.State != System.Data.ConnectionState.Closed)
                    {
                        _connection.Close();
                    }
                }
                catch
                {
                    // Ignore exceptions during close
                }

                _connection.Dispose();
                _connection = null;
            }
            _initialized = false;
            _disposed = true;
        }
        finally
        {
            _connectionLock.Release();
            _connectionLock.Dispose();
        }
    }
}