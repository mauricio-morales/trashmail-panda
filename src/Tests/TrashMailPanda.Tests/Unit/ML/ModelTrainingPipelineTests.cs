using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using TrashMailPanda.Providers.ML.Config;
using TrashMailPanda.Providers.ML.Models;
using TrashMailPanda.Providers.ML.Training;
using TrashMailPanda.Providers.ML.Versioning;
using TrashMailPanda.Providers.Storage;
using TrashMailPanda.Providers.Storage.Models;
using TrashMailPanda.Shared.Base;
using Xunit;

namespace TrashMailPanda.Tests.Unit.ML;

/// <summary>
/// Unit tests for <see cref="ModelTrainingPipeline"/> — crash-safety, validation rules.
/// </summary>
[Trait("Category", "Unit")]
public sealed class ModelTrainingPipelineTests : IDisposable
{
    private readonly SqliteConnection _connection;
    private readonly string _tempDir;
    private readonly ModelVersionRepository _repo;
    private readonly MLModelProviderConfig _config;

    public ModelTrainingPipelineTests()
    {
        _connection = new SqliteConnection("Data Source=:memory:");
        _connection.Open();
        CreateSchema();
        _tempDir = Path.Combine(Path.GetTempPath(), "TMP_ML_UNIT_" + Path.GetRandomFileName());
        Directory.CreateDirectory(_tempDir);
        _repo = new ModelVersionRepository(_connection, NullLogger<ModelVersionRepository>.Instance);
        _config = new MLModelProviderConfig
        {
            ModelDirectory = _tempDir,
            MinTrainingSamples = 100,
            MaxModelVersions = 5,
            DominantClassImbalanceThreshold = 0.80,
        };
    }

    public void Dispose()
    {
        _connection.Dispose();
        try { Directory.Delete(_tempDir, recursive: true); }
        catch { /* best-effort */ }
    }

    // ─────────────────────────────────────────────────────────────────────────
    // T033 — Crash-safety: cancellation cleans up temp file and leaves DB clean
    // ─────────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task TrainActionModelAsync_LeavesNoTempFilesAndNoActiveRow_WhenCancelled()
    {
        // Arrange
        var cts = new CancellationTokenSource();
        var archiveMock = new Mock<IEmailArchiveService>();
        archiveMock
            .Setup(a => a.GetAllFeaturesAsync(It.IsAny<int?>(), It.IsAny<CancellationToken>()))
            .Returns<int?, CancellationToken>((_, _) =>
            {
                // Cancel the token while returning valid data so that
                // validation passes but ThrowIfCancellationRequested() fires
                // before any file I/O.
                cts.Cancel();
                return Task.FromResult(
                    Result<IEnumerable<EmailFeatureVector>>.Success(BuildVectors(150)));
            });

        var pipeline = BuildPipeline(archiveMock.Object);

        // Act — training is expected to throw OperationCanceledException
        await Assert.ThrowsAsync<OperationCanceledException>(
            () => pipeline.TrainActionModelAsync(
                new TrainingRequest { TriggerReason = "t033", ForceRetrain = true },
                progress: null,
                cts.Token));

        // Assert: no .tmp artefacts in model directory
        var tempFiles = Directory.GetFiles(_tempDir, "*.tmp");
        Assert.Empty(tempFiles);

        // Assert: no IsActive=1 row in ml_models
        var versionsResult = await _repo.GetVersionsAsync("action");
        if (versionsResult.IsSuccess)
            Assert.DoesNotContain(versionsResult.Value, v => v.IsActive);
    }

    // ─────────────────────────────────────────────────────────────────────────
    // T034 — Minimum-data validation: fewer than MinTrainingSamples → Failure
    // ─────────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task TrainActionModelAsync_ReturnsValidationError_WhenFewerThanMinSamplesAvailable()
    {
        // Arrange: only 40 vectors (< MinTrainingSamples=100)
        const int available = 40;
        var archiveMock = new Mock<IEmailArchiveService>();
        archiveMock
            .Setup(a => a.GetAllFeaturesAsync(It.IsAny<int?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result<IEnumerable<EmailFeatureVector>>.Success(BuildVectors(available)));

        var pipeline = BuildPipeline(archiveMock.Object);

        // Act
        var result = await pipeline.TrainActionModelAsync(
            new TrainingRequest { TriggerReason = "t034", ForceRetrain = false });

        // Assert: failure with ValidationError
        Assert.False(result.IsSuccess);
        Assert.IsType<ValidationError>(result.Error);
        // Error must mention both available count and required count (FR-016)
        Assert.Contains($"{available}", result.Error!.Message);
        Assert.Contains($"{_config.MinTrainingSamples}", result.Error.Message);
    }

    // ─────────────────────────────────────────────────────────────────────────
    // T035 — Schema version mismatch: stale vectors → Failure
    // ─────────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task TrainActionModelAsync_ReturnsValidationError_WhenFeatureSchemaVersionMismatches()
    {
        // Arrange: 150 vectors with outdated schema version
        var staleVersion = FeatureSchema.CurrentVersion - 1;
        var staleVectors = BuildVectors(150, schemaVersion: staleVersion);
        var archiveMock = new Mock<IEmailArchiveService>();
        archiveMock
            .Setup(a => a.GetAllFeaturesAsync(It.IsAny<int?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result<IEnumerable<EmailFeatureVector>>.Success(staleVectors));

        var pipeline = BuildPipeline(archiveMock.Object);

        // Act — ForceRetrain=true bypasses sample-count check so schema check is exercised
        var result = await pipeline.TrainActionModelAsync(
            new TrainingRequest { TriggerReason = "t035", ForceRetrain = true });

        // Assert: failure with ValidationError mentioning schema version (FR-012)
        Assert.False(result.IsSuccess);
        Assert.IsType<ValidationError>(result.Error);
        Assert.Contains("schema version", result.Error!.Message, StringComparison.OrdinalIgnoreCase);
    }

    // ─────────────────────────────────────────────────────────────────────────
    // Helpers
    // ─────────────────────────────────────────────────────────────────────────

    private ModelTrainingPipeline BuildPipeline(IEmailArchiveService archiveService)
    {
        var pruner = new ModelVersionPruner(_repo, NullLogger<ModelVersionPruner>.Instance);
        var trainer = new ActionModelTrainer(NullLogger<ActionModelTrainer>.Instance);
        var pipelineBuilder = new FeaturePipelineBuilder();
        var incrementalService = new IncrementalUpdateService(
            archiveService, _repo, pruner, trainer, _config,
            NullLogger<IncrementalUpdateService>.Instance);

        return new ModelTrainingPipeline(
            archiveService, _repo, pruner, trainer, pipelineBuilder,
            _config, incrementalService, NullLogger<ModelTrainingPipeline>.Instance);
    }

    private static IEnumerable<EmailFeatureVector> BuildVectors(
        int count, int? schemaVersion = null)
    {
        var version = schemaVersion ?? FeatureSchema.CurrentVersion;
        return Enumerable.Range(0, count).Select(i => new EmailFeatureVector
        {
            EmailId = $"e-{i}",
            SenderDomain = "example.com",
            SenderKnown = i % 2,
            SpfResult = "pass",
            DkimResult = "pass",
            FeatureSchemaVersion = version,
            ExtractedAt = DateTime.UtcNow,
            UserCorrected = 0,
        });
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
}
