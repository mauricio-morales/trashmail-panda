using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.ML;
using Moq;
using TrashMailPanda.Providers.ML.Classification;
using TrashMailPanda.Providers.ML.Config;
using TrashMailPanda.Providers.ML.Models;
using TrashMailPanda.Providers.ML.Training;
using TrashMailPanda.Providers.ML.Versioning;
using TrashMailPanda.Providers.Storage;
using TrashMailPanda.Providers.Storage.Models;
using TrashMailPanda.Shared.Base;
using Xunit;

namespace TrashMailPanda.Tests.Unit.ML;

[Trait("Category", "Unit")]
public class IncrementalUpdateServiceTests : IDisposable
{
    // ──────────────────────────────────────────────────────────────────────────
    // Infrastructure: in-memory SQLite + repository
    // ──────────────────────────────────────────────────────────────────────────

    private readonly SqliteConnection _connection;
    private readonly ModelVersionRepository _repository;

    public IncrementalUpdateServiceTests()
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

    // ──────────────────────────────────────────────────────────────────────────
    // Helpers
    // ──────────────────────────────────────────────────────────────────────────

    private static EmailFeatureVector MakeVector(string emailId, int userCorrected, DateTime extractedAt) =>
        new()
        {
            EmailId = emailId,
            SenderDomain = "example.com",
            ExtractedAt = extractedAt,
            UserCorrected = userCorrected,
            SpfResult = "pass",
            DkimResult = "pass",
            DmarcResult = "pass",
            FeatureSchemaVersion = FeatureSchema.CurrentVersion,
        };

    private IncrementalUpdateService CreateService(
        IEmailArchiveService archiveService,
        string tempModelDir)
    {
        var config = new MLModelProviderConfig
        {
            ModelDirectory = tempModelDir,
            MinTrainingSamples = 10,
            MaxModelVersions = 5,
            DominantClassImbalanceThreshold = 0.80,
        };
        var pruner = new ModelVersionPruner(_repository, NullLogger<ModelVersionPruner>.Instance);
        var trainer = new ActionModelTrainer(NullLogger<ActionModelTrainer>.Instance);

        return new IncrementalUpdateService(
            archiveService,
            _repository,
            pruner,
            trainer,
            config,
            NullLogger<IncrementalUpdateService>.Instance);
    }

    // ──────────────────────────────────────────────────────────────────────────
    // T026 — Tests
    // ──────────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task UpdateAsync_ReturnsValidationError_WhenFewerThan50NewCorrections()
    {
        // Arrange: 30 corrected vectors since "yesterday", fewer than default 50
        var yesterday = DateTime.UtcNow.AddDays(-1);
        var since = DateTime.UtcNow.AddHours(-12);

        var vectors = Enumerable.Range(0, 30)
            .Select(i => MakeVector($"email-{i}", userCorrected: 1, extractedAt: since.AddMinutes(i)))
            .Concat(Enumerable.Range(30, 20)
                .Select(i => MakeVector($"email-{i}", userCorrected: 0, extractedAt: since.AddMinutes(i))))
            .ToList();

        var archiveMock = new Mock<IEmailArchiveService>();
        archiveMock
            .Setup(a => a.GetAllFeaturesAsync(It.IsAny<int?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result<IEnumerable<EmailFeatureVector>>.Success(vectors));

        // Pretend last training happened at "yesterday"
        await _repository.InsertVersionAsync(new ModelVersion
        {
            ModelId = "old-model",
            ModelType = "action",
            Version = 1,
            TrainingDate = yesterday.ToString("o"),
            Algorithm = "SdcaMaximumEntropy",
            FeatureSchemaVersion = FeatureSchema.CurrentVersion,
            TrainingDataCount = 50,
        });
        await _repository.SetActiveAsync("old-model", "action");

        var dir = Path.Combine(Path.GetTempPath(), Path.GetRandomFileName());
        var service = CreateService(archiveMock.Object, dir);

        // Act
        var result = await service.UpdateAsync(new IncrementalUpdateRequest
        {
            MinNewCorrections = 50,
            TriggerReason = "test",
        });

        // Assert
        Assert.False(result.IsSuccess);
        Assert.IsType<ValidationError>(result.Error);
        Assert.Contains("Insufficient new corrections: 30", result.Error.Message);
        Assert.Contains("50 required", result.Error.Message);
    }

    [Fact]
    public async Task UpdateAsync_ReturnsValidationError_WhenFeatureFetchFails()
    {
        var archiveMock = new Mock<IEmailArchiveService>();
        archiveMock
            .Setup(a => a.GetAllFeaturesAsync(It.IsAny<int?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result<IEnumerable<EmailFeatureVector>>.Failure(
                new StorageError("DB unavailable")));

        var dir = Path.Combine(Path.GetTempPath(), Path.GetRandomFileName());
        var service = CreateService(archiveMock.Object, dir);

        var result = await service.UpdateAsync(new IncrementalUpdateRequest { MinNewCorrections = 50 });

        Assert.False(result.IsSuccess);
        Assert.IsType<StorageError>(result.Error);
    }

    [Fact]
    public async Task ShouldRetrainAsync_ReturnsTrue_When50OrMoreCorrectionsAccumulated()
    {
        // Arrange: insert an active model trained yesterday
        var yesterday = DateTime.UtcNow.AddDays(-1);
        await _repository.InsertVersionAsync(new ModelVersion
        {
            ModelId = "active-model",
            ModelType = "action",
            Version = 1,
            TrainingDate = yesterday.ToString("o"),
            Algorithm = "SdcaMaximumEntropy",
            FeatureSchemaVersion = FeatureSchema.CurrentVersion,
            TrainingDataCount = 100,
        });
        await _repository.SetActiveAsync("active-model", "action");

        // 55 user-corrected vectors since yesterday
        var since = yesterday.AddHours(1);
        var vectors = Enumerable.Range(0, 55)
            .Select(i => MakeVector($"c-{i}", userCorrected: 1, extractedAt: since.AddMinutes(i)))
            .ToList<EmailFeatureVector>();

        var archiveMock = new Mock<IEmailArchiveService>();
        archiveMock
            .Setup(a => a.GetAllFeaturesAsync(It.IsAny<int?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result<IEnumerable<EmailFeatureVector>>.Success(vectors));

        var config = new MLModelProviderConfig
        {
            ModelDirectory = Path.GetTempPath(),
            MinTrainingSamples = 10,
            MaxModelVersions = 5,
        };
        var pipeline = new ModelTrainingPipeline(
            archiveMock.Object,
            _repository,
            new ModelVersionPruner(_repository, NullLogger<ModelVersionPruner>.Instance),
            new ActionModelTrainer(NullLogger<ActionModelTrainer>.Instance),
            new FeaturePipelineBuilder(),
            config,
            new IncrementalUpdateService(
                archiveMock.Object,
                _repository,
                new ModelVersionPruner(_repository, NullLogger<ModelVersionPruner>.Instance),
                new ActionModelTrainer(NullLogger<ActionModelTrainer>.Instance),
                config,
                NullLogger<IncrementalUpdateService>.Instance),
            NullLogger<ModelTrainingPipeline>.Instance);

        // Act
        var result = await pipeline.ShouldRetrainAsync();

        // Assert
        Assert.True(result.IsSuccess);
        Assert.True(result.Value);
    }
}
