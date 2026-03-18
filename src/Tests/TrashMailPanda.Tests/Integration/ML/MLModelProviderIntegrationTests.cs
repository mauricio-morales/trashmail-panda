using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.ML;
using Moq;
using TrashMailPanda.Providers.ML;
using TrashMailPanda.Providers.ML.Classification;
using TrashMailPanda.Providers.ML.Config;
using TrashMailPanda.Providers.ML.Models;
using TrashMailPanda.Providers.ML.Training;
using TrashMailPanda.Providers.ML.Versioning;
using TrashMailPanda.Providers.Storage;
using TrashMailPanda.Providers.Storage.Models;
using TrashMailPanda.Shared.Base;
using Xunit;

namespace TrashMailPanda.Tests.Integration.ML;

/// <summary>
/// Full round-trip integration test for <see cref="MLModelProvider"/>.
/// Requires ML.NET with enough synthetic data to complete training.
/// Run manually: <c>dotnet test --filter FullyQualifiedName~MLModelProviderIntegrationTests</c>
/// </summary>
[Trait("Category", "Integration")]
public class MLModelProviderIntegrationTests : IDisposable
{
    private readonly SqliteConnection _connection;
    private readonly string _tempDir;

    public MLModelProviderIntegrationTests()
    {
        _connection = new SqliteConnection("Data Source=:memory:");
        _connection.Open();
        CreateSchema();
        _tempDir = Path.Combine(Path.GetTempPath(), "TMP_ML_" + Path.GetRandomFileName());
        Directory.CreateDirectory(_tempDir);
    }

    public void Dispose()
    {
        _connection.Dispose();
        try { Directory.Delete(_tempDir, recursive: true); }
        catch { /* best-effort */ }
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

    [Fact(Skip = "Requires ML.NET full training — run manually: dotnet test --filter FullyQualifiedName~MLModelProviderIntegrationTests")]
    public async Task FullRoundTrip_TrainVersionRollbackPrune()
    {
        // Arrange
        var config = new MLModelProviderConfig
        {
            ModelDirectory = _tempDir,
            MinTrainingSamples = 10,
            MaxModelVersions = 5,
            DominantClassImbalanceThreshold = 0.80,
        };

        var mlContext = new MLContext(seed: 42);
        var repo = new ModelVersionRepository(_connection, NullLogger<ModelVersionRepository>.Instance);
        var pruner = new ModelVersionPruner(repo, NullLogger<ModelVersionPruner>.Instance);
        var trainer = new ActionModelTrainer(NullLogger<ActionModelTrainer>.Instance);
        var pipelineBuilder = new FeaturePipelineBuilder();
        var classifier = new ActionClassifier(mlContext, NullLogger<ActionClassifier>.Instance);

        var archiveMock = BuildSyntheticArchive(150);

        var incrementalService = new IncrementalUpdateService(
            archiveMock,
            repo,
            pruner,
            trainer,
            config,
            NullLogger<IncrementalUpdateService>.Instance);

        var pipeline = new ModelTrainingPipeline(
            archiveMock,
            repo,
            pruner,
            trainer,
            pipelineBuilder,
            config,
            incrementalService,
            NullLogger<ModelTrainingPipeline>.Instance);

        // Act: Train first time
        var result1 = await pipeline.TrainActionModelAsync(
            new TrainingRequest { TriggerReason = "integration-test-1", ForceRetrain = true });

        Assert.True(result1.IsSuccess, $"First training failed: {result1.Error?.Message}");

        var versions1 = await repo.GetVersionsAsync("action");
        Assert.True(versions1.IsSuccess);
        Assert.Single(versions1.Value);

        // Act: Train second time
        var result2 = await pipeline.TrainActionModelAsync(
            new TrainingRequest { TriggerReason = "integration-test-2", ForceRetrain = true });

        Assert.True(result2.IsSuccess, $"Second training failed: {result2.Error?.Message}");

        var versions2 = await repo.GetVersionsAsync("action");
        Assert.True(versions2.IsSuccess);
        Assert.Equal(2, versions2.Value.Count);

        // Rollback to first model
        var firstModelId = versions2.Value.OrderBy(v => v.Version).First().ModelId;
        var provider = new MLModelProvider(
            classifier,
            repo,
            config,
            NullLogger<MLModelProvider>.Instance);

        var rollbackResult = await provider.RollbackAsync(firstModelId, CancellationToken.None);
        Assert.True(rollbackResult.IsSuccess, $"Rollback failed: {rollbackResult.Error?.Message}");

        var active = await repo.GetActiveVersionAsync("action");
        Assert.True(active.IsSuccess);
        Assert.Equal(firstModelId, active.Value.ModelId);

        // Train 4 more times — total 6 versions, should prune to 5
        for (var i = 3; i <= 6; i++)
        {
            var r = await pipeline.TrainActionModelAsync(
                new TrainingRequest { TriggerReason = $"integration-test-{i}", ForceRetrain = true });
            Assert.True(r.IsSuccess, $"Training {i} failed: {r.Error?.Message}");
        }

        var finalVersions = await repo.GetVersionsAsync("action");
        Assert.True(finalVersions.IsSuccess);
        Assert.Equal(5, finalVersions.Value.Count); // MaxModelVersions = 5

        // Exactly one active
        Assert.Equal(1, finalVersions.Value.Count(v => v.IsActive));
    }

    private static IEmailArchiveService BuildSyntheticArchive(int count)
    {
        var labels = new[] { "Keep", "Delete", "Archive", "Label" };
        var rng = new Random(42);
        var vectors = Enumerable.Range(0, count)
            .Select(i => new EmailFeatureVector
            {
                EmailId = $"email-{i}",
                SenderDomain = "example.com",
                SenderKnown = rng.Next(0, 2),
                HasListUnsubscribe = rng.Next(0, 2),
                SpfResult = "pass",
                DkimResult = "pass",
                DmarcResult = "pass",
                FeatureSchemaVersion = FeatureSchema.CurrentVersion,
                ExtractedAt = DateTime.UtcNow.AddDays(-rng.Next(1, 60)),
                UserCorrected = 0,
            })
            .ToList<EmailFeatureVector>();

        var mock = new Mock<IEmailArchiveService>();
        mock.Setup(a => a.GetAllFeaturesAsync(It.IsAny<int?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result<IEnumerable<EmailFeatureVector>>.Success(vectors));
        return mock.Object;
    }
}
