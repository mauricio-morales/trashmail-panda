using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Logging.Abstractions;
using TrashMailPanda.Providers.ML.Models;
using TrashMailPanda.Providers.ML.Versioning;
using TrashMailPanda.Providers.Storage.Models;
using TrashMailPanda.Shared.Base;
using Xunit;

namespace TrashMailPanda.Tests.Unit.ML;

[Trait("Category", "Unit")]
public class ModelVersionRepositoryTests : IDisposable
{
    private readonly SqliteConnection _connection;
    private readonly ModelVersionRepository _repository;

    public ModelVersionRepositoryTests()
    {
        _connection = new SqliteConnection("Data Source=:memory:");
        _connection.Open();
        CreateSchema();
        _repository = new ModelVersionRepository(_connection, NullLogger<ModelVersionRepository>.Instance);
    }

    public void Dispose()
    {
        _connection.Dispose();
    }

    private void CreateSchema()
    {
        using var cmd = _connection.CreateCommand();
        cmd.CommandText = """
            CREATE TABLE IF NOT EXISTS ml_models (
                model_id TEXT PRIMARY KEY,
                model_type TEXT NOT NULL,
                version INTEGER NOT NULL,
                training_date TEXT NOT NULL,
                algorithm TEXT NOT NULL,
                feature_schema_version INTEGER NOT NULL DEFAULT 1,
                training_data_count INTEGER NOT NULL DEFAULT 0,
                accuracy REAL NOT NULL DEFAULT 0,
                macro_precision REAL NOT NULL DEFAULT 0,
                macro_recall REAL NOT NULL DEFAULT 0,
                macro_f1 REAL NOT NULL DEFAULT 0,
                per_class_metrics_json TEXT NOT NULL DEFAULT '{}',
                is_active INTEGER NOT NULL DEFAULT 0,
                file_path TEXT NOT NULL DEFAULT '',
                notes TEXT
            );
            CREATE TABLE IF NOT EXISTS training_events (
                id INTEGER PRIMARY KEY AUTOINCREMENT,
                event_type TEXT NOT NULL,
                model_type TEXT NOT NULL,
                model_id TEXT,
                details_json TEXT,
                occurred_at TEXT NOT NULL DEFAULT (datetime('now'))
            );
            """;
        cmd.ExecuteNonQuery();
    }

    private static ModelVersion MakeVersion(string modelId, int version, string algorithm = "SdcaMaximumEntropy") =>
        new()
        {
            ModelId = modelId,
            ModelType = "action",
            Version = version,
            TrainingDate = DateTime.UtcNow.ToString("o"),
            Algorithm = algorithm,
            FeatureSchemaVersion = FeatureSchema.CurrentVersion,
            TrainingDataCount = 100,
            Accuracy = 0.90,
            MacroPrecision = 0.88,
            MacroRecall = 0.87,
            MacroF1 = 0.875,
            PerClassMetricsJson = @"{""Keep"":0.9}",
            IsActive = false,
            FilePath = $"/models/{modelId}.zip",
        };

    // ── InsertVersionAsync ─────────────────────────────────────────────────────

    [Fact]
    public async Task InsertVersionAsync_RoundTripsAllFields()
    {
        var v = MakeVersion("model-abc", version: 1, algorithm: "LightGbm");

        var insertResult = await _repository.InsertVersionAsync(v);
        Assert.True(insertResult.IsSuccess);

        var listResult = await _repository.GetVersionsAsync("action");
        Assert.True(listResult.IsSuccess);
        Assert.Single(listResult.Value);

        var round = listResult.Value[0];
        Assert.Equal(v.ModelId, round.ModelId);
        Assert.Equal(v.Version, round.Version);
        Assert.Equal(v.Algorithm, round.Algorithm);
        Assert.Equal(v.TrainingDataCount, round.TrainingDataCount);
        Assert.Equal(v.FilePath, round.FilePath);
        Assert.Equal(v.PerClassMetricsJson, round.PerClassMetricsJson);
        Assert.Equal(v.MacroF1, round.MacroF1, precision: 4);
    }

    // ── SetActiveAsync ────────────────────────────────────────────────────────

    [Fact]
    public async Task SetActiveAsync_SetsExactlyOneActiveRowPerModelType()
    {
        // Insert two versions
        await _repository.InsertVersionAsync(MakeVersion("m-1", 1));
        await _repository.InsertVersionAsync(MakeVersion("m-2", 2));

        // Activate first
        await _repository.SetActiveAsync("m-1", "action");

        // Activate second — first should become inactive
        var activateResult = await _repository.SetActiveAsync("m-2", "action");
        Assert.True(activateResult.IsSuccess);

        var listResult = await _repository.GetVersionsAsync("action");
        Assert.True(listResult.IsSuccess);

        var activeRows = listResult.Value.Count(v => v.IsActive);
        Assert.Equal(1, activeRows);
        Assert.True(listResult.Value.Single(v => v.ModelId == "m-2").IsActive);
        Assert.False(listResult.Value.Single(v => v.ModelId == "m-1").IsActive);
    }

    // ── GetVersionsAsync ──────────────────────────────────────────────────────

    [Fact]
    public async Task GetVersionsAsync_ReturnsNewestFirst()
    {
        await _repository.InsertVersionAsync(MakeVersion("m-1", 1));
        await _repository.InsertVersionAsync(MakeVersion("m-2", 3));
        await _repository.InsertVersionAsync(MakeVersion("m-3", 2));

        var result = await _repository.GetVersionsAsync("action");

        Assert.True(result.IsSuccess);
        Assert.Equal(3, result.Value.Count);
        Assert.Equal(3, result.Value[0].Version); // newest first
        Assert.Equal(2, result.Value[1].Version);
        Assert.Equal(1, result.Value[2].Version);
    }

    // ── AppendEventAsync ──────────────────────────────────────────────────────

    [Fact]
    public async Task AppendEventAsync_WritesCorrectEventTypeAndDetails()
    {
        var details = @"{""reason"":""manual""}";
        var result = await _repository.AppendEventAsync(
            "training_started", "action", "model-1", details);

        Assert.True(result.IsSuccess);

        // Read back via raw SQL
        using var cmd = _connection.CreateCommand();
        cmd.CommandText = "SELECT event_type, model_type, model_id, details_json FROM training_events";
        using var reader = await cmd.ExecuteReaderAsync();
        Assert.True(await reader.ReadAsync());
        Assert.Equal("training_started", reader.GetString(0));
        Assert.Equal("action", reader.GetString(1));
        Assert.Equal("model-1", reader.GetString(2));
        Assert.Equal(details, reader.GetString(3));
    }

    // ── GetActiveVersionAsync ─────────────────────────────────────────────────

    [Fact]
    public async Task GetActiveVersionAsync_ReturnsConfigurationError_WhenTableEmpty()
    {
        var result = await _repository.GetActiveVersionAsync("action");

        Assert.False(result.IsSuccess);
        Assert.IsType<ConfigurationError>(result.Error);
        Assert.Contains("No active model", result.Error.Message);
    }

    [Fact]
    public async Task GetActiveVersionAsync_ReturnsActiveModel_WhenOneExists()
    {
        await _repository.InsertVersionAsync(MakeVersion("m-1", 1));
        await _repository.SetActiveAsync("m-1", "action");

        var result = await _repository.GetActiveVersionAsync("action");

        Assert.True(result.IsSuccess);
        Assert.Equal("m-1", result.Value.ModelId);
        Assert.True(result.Value.IsActive);
    }
}
