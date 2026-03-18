using Microsoft.Data.Sqlite;
using TrashMailPanda.Providers.ML.Models;

namespace TrashMailPanda.Providers.ML.Versioning;

/// <summary>
/// Provides raw ADO.NET access to the <c>ml_models</c> and <c>training_events</c> tables.
/// All public methods return <see cref="Result{T}"/> and never throw.
/// </summary>
public sealed class ModelVersionRepository
{
    private readonly SqliteConnection _connection;
    private readonly ILogger<ModelVersionRepository> _logger;
    // Serialize mutations that touch the shared connection
    private readonly SemaphoreSlim _mutex = new(1, 1);

    public ModelVersionRepository(SqliteConnection connection, ILogger<ModelVersionRepository> logger)
    {
        _connection = connection;
        _logger = logger;
    }

    // ──────────────────────────────────────────────────────────────────────────
    // Write operations
    // ──────────────────────────────────────────────────────────────────────────

    /// <summary>
    /// Inserts a new model version row into <c>ml_models</c>.
    /// The caller is responsible for calling <see cref="SetActiveAsync"/> separately.
    /// </summary>
    public async Task<Result<bool>> InsertVersionAsync(ModelVersion version)
    {
        await _mutex.WaitAsync();
        try
        {
            const string sql = """
                INSERT INTO ml_models (
                    model_id, model_type, version, training_date, algorithm,
                    feature_schema_version, training_data_count, accuracy,
                    macro_precision, macro_recall, macro_f1,
                    per_class_metrics_json, is_active, file_path, notes
                ) VALUES (
                    @model_id, @model_type, @version, @training_date, @algorithm,
                    @feature_schema_version, @training_data_count, @accuracy,
                    @macro_precision, @macro_recall, @macro_f1,
                    @per_class_metrics_json, @is_active, @file_path, @notes
                );
                """;

            using var cmd = _connection.CreateCommand();
            cmd.CommandText = sql;
            cmd.Parameters.AddWithValue("@model_id", version.ModelId);
            cmd.Parameters.AddWithValue("@model_type", version.ModelType);
            cmd.Parameters.AddWithValue("@version", version.Version);
            cmd.Parameters.AddWithValue("@training_date", version.TrainingDate);
            cmd.Parameters.AddWithValue("@algorithm", version.Algorithm);
            cmd.Parameters.AddWithValue("@feature_schema_version", version.FeatureSchemaVersion);
            cmd.Parameters.AddWithValue("@training_data_count", version.TrainingDataCount);
            cmd.Parameters.AddWithValue("@accuracy", version.Accuracy);
            cmd.Parameters.AddWithValue("@macro_precision", version.MacroPrecision);
            cmd.Parameters.AddWithValue("@macro_recall", version.MacroRecall);
            cmd.Parameters.AddWithValue("@macro_f1", version.MacroF1);
            cmd.Parameters.AddWithValue("@per_class_metrics_json", version.PerClassMetricsJson);
            cmd.Parameters.AddWithValue("@is_active", version.IsActive ? 1 : 0);
            cmd.Parameters.AddWithValue("@file_path", version.FilePath);
            cmd.Parameters.AddWithValue("@notes", version.Notes ?? (object)DBNull.Value);

            await cmd.ExecuteNonQueryAsync();
            return Result<bool>.Success(true);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to insert model version {ModelId}", version.ModelId);
            return Result<bool>.Failure(new StorageError($"Failed to insert model version: {ex.Message}", InnerException: ex));
        }
        finally
        {
            _mutex.Release();
        }
    }

    /// <summary>
    /// In a single SQLite transaction: deactivates the currently active model of
    /// <paramref name="modelType"/> and activates <paramref name="newModelId"/>.
    /// </summary>
    public async Task<Result<bool>> SetActiveAsync(string newModelId, string modelType)
    {
        await _mutex.WaitAsync();
        try
        {
            using var transaction = _connection.BeginTransaction();
            try
            {
                // Deactivate current active model
                using var deactivateCmd = _connection.CreateCommand();
                deactivateCmd.Transaction = transaction;
                deactivateCmd.CommandText = """
                    UPDATE ml_models SET is_active = 0
                    WHERE model_type = @model_type AND is_active = 1;
                    """;
                deactivateCmd.Parameters.AddWithValue("@model_type", modelType);
                await deactivateCmd.ExecuteNonQueryAsync();

                // Activate the target model
                using var activateCmd = _connection.CreateCommand();
                activateCmd.Transaction = transaction;
                activateCmd.CommandText = """
                    UPDATE ml_models SET is_active = 1
                    WHERE model_id = @model_id AND model_type = @model_type;
                    """;
                activateCmd.Parameters.AddWithValue("@model_id", newModelId);
                activateCmd.Parameters.AddWithValue("@model_type", modelType);
                var affected = await activateCmd.ExecuteNonQueryAsync();

                if (affected == 0)
                {
                    transaction.Rollback();
                    return Result<bool>.Failure(new ValidationError(
                        $"Model '{newModelId}' of type '{modelType}' not found; transaction rolled back."));
                }

                transaction.Commit();
                return Result<bool>.Success(true);
            }
            catch
            {
                transaction.Rollback();
                throw;
            }
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _logger.LogError(ex, "Failed to set active model {ModelId}", newModelId);
            return Result<bool>.Failure(new StorageError($"Failed to set active model: {ex.Message}", InnerException: ex));
        }
        finally
        {
            _mutex.Release();
        }
    }

    // ──────────────────────────────────────────────────────────────────────────
    // Read operations
    // ──────────────────────────────────────────────────────────────────────────

    /// <summary>
    /// Returns all versions of the given <paramref name="modelType"/>, newest first.
    /// </summary>
    public async Task<Result<IReadOnlyList<ModelVersion>>> GetVersionsAsync(string modelType)
    {
        try
        {
            const string sql = """
                SELECT model_id, model_type, version, training_date, algorithm,
                       feature_schema_version, training_data_count, accuracy,
                       macro_precision, macro_recall, macro_f1,
                       per_class_metrics_json, is_active, file_path, notes
                FROM ml_models
                WHERE model_type = @model_type
                ORDER BY version DESC;
                """;

            using var cmd = _connection.CreateCommand();
            cmd.CommandText = sql;
            cmd.Parameters.AddWithValue("@model_type", modelType);

            var versions = new List<ModelVersion>();
            using var reader = await cmd.ExecuteReaderAsync();
            while (await reader.ReadAsync())
            {
                versions.Add(MapRow(reader));
            }

            return Result<IReadOnlyList<ModelVersion>>.Success(versions);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to retrieve model versions for type {ModelType}", modelType);
            return Result<IReadOnlyList<ModelVersion>>.Failure(
                new StorageError($"Failed to retrieve model versions: {ex.Message}", InnerException: ex));
        }
    }

    /// <summary>
    /// Returns the currently active model version or a
    /// <see cref="ConfigurationError"/> when no model has been trained yet.
    /// </summary>
    public async Task<Result<ModelVersion>> GetActiveVersionAsync(string modelType)
    {
        try
        {
            const string sql = """
                SELECT model_id, model_type, version, training_date, algorithm,
                       feature_schema_version, training_data_count, accuracy,
                       macro_precision, macro_recall, macro_f1,
                       per_class_metrics_json, is_active, file_path, notes
                FROM ml_models
                WHERE model_type = @model_type AND is_active = 1
                LIMIT 1;
                """;

            using var cmd = _connection.CreateCommand();
            cmd.CommandText = sql;
            cmd.Parameters.AddWithValue("@model_type", modelType);

            using var reader = await cmd.ExecuteReaderAsync();
            if (await reader.ReadAsync())
                return Result<ModelVersion>.Success(MapRow(reader));

            return Result<ModelVersion>.Failure(new ConfigurationError(
                $"No active model of type '{modelType}' found. Train a model first."));
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to retrieve active model version for type {ModelType}", modelType);
            return Result<ModelVersion>.Failure(
                new StorageError($"Failed to retrieve active model: {ex.Message}", InnerException: ex));
        }
    }

    // ──────────────────────────────────────────────────────────────────────────
    // Events
    // ──────────────────────────────────────────────────────────────────────────

    /// <summary>
    /// Appends an audit event row to <c>training_events</c>.
    /// </summary>
    public async Task<Result<bool>> AppendEventAsync(
        string eventType,
        string modelType,
        string? modelId,
        string detailsJson)
    {
        await _mutex.WaitAsync();
        try
        {
            const string sql = """
                INSERT INTO training_events (event_type, model_type, model_id, details_json)
                VALUES (@event_type, @model_type, @model_id, @details_json);
                """;

            using var cmd = _connection.CreateCommand();
            cmd.CommandText = sql;
            cmd.Parameters.AddWithValue("@event_type", eventType);
            cmd.Parameters.AddWithValue("@model_type", modelType);
            cmd.Parameters.AddWithValue("@model_id", modelId ?? (object)DBNull.Value);
            cmd.Parameters.AddWithValue("@details_json", detailsJson);

            await cmd.ExecuteNonQueryAsync();
            return Result<bool>.Success(true);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to append training event {EventType}", eventType);
            return Result<bool>.Failure(new StorageError($"Failed to append event: {ex.Message}", InnerException: ex));
        }
        finally
        {
            _mutex.Release();
        }
    }

    // ──────────────────────────────────────────────────────────────────────────
    // Private helpers
    // ──────────────────────────────────────────────────────────────────────────

    private static ModelVersion MapRow(SqliteDataReader reader) => new()
    {
        ModelId = reader.GetString(0),
        ModelType = reader.GetString(1),
        Version = reader.GetInt32(2),
        TrainingDate = reader.GetString(3),
        Algorithm = reader.GetString(4),
        FeatureSchemaVersion = reader.GetInt32(5),
        TrainingDataCount = reader.GetInt32(6),
        Accuracy = reader.GetDouble(7),
        MacroPrecision = reader.GetDouble(8),
        MacroRecall = reader.GetDouble(9),
        MacroF1 = reader.GetDouble(10),
        PerClassMetricsJson = reader.GetString(11),
        IsActive = reader.GetInt32(12) != 0,
        FilePath = reader.GetString(13),
        Notes = reader.IsDBNull(14) ? null : reader.GetString(14),
    };
}
