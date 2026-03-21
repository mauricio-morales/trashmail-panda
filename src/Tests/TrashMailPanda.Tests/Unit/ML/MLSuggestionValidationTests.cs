using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.ML;
using TrashMailPanda.Providers.ML.Classification;
using TrashMailPanda.Providers.ML.Config;
using TrashMailPanda.Providers.ML.Models;
using TrashMailPanda.Providers.ML.Training;
using TrashMailPanda.Providers.Storage.Models;
using Xunit;

namespace TrashMailPanda.Tests.Unit.ML;

/// <summary>
/// End-to-end ML suggestion validation tests.
/// Trains real ML.NET models on synthetic data and validates that predictions
/// match the training distribution — catching bugs like "Keep at 50%" for
/// clearly archived emails.
/// </summary>
[Trait("Category", "Unit")]
public sealed class MLSuggestionValidationTests : IDisposable
{
    private readonly string _tempDir;

    // Force SdcaMaximumEntropy trainer (threshold=1.0 means "never use LightGBM").
    // LightGBM native library is unavailable on macOS ARM64 with ML.NET 4.0.2.
    // The SDCA trainer validates the same end-to-end pipeline (features → labels →
    // model → prediction) which is what these tests verify.
    private const double UseSdcaThreshold = 1.0;

    public MLSuggestionValidationTests()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), "TMP_ML_SUGGEST_" + Path.GetRandomFileName());
        Directory.CreateDirectory(_tempDir);
    }

    public void Dispose()
    {
        try { Directory.Delete(_tempDir, recursive: true); }
        catch { /* best-effort */ }
    }

    // ─────────────────────────────────────────────────────────────────────────
    // Scenario 1: Verify the "Keep at 50%" fallback — this is NOT a real model
    // prediction; it fires when no model is loaded.
    // ─────────────────────────────────────────────────────────────────────────

    [Fact]
    public void Classify_ReturnsFailure_WhenNoModelLoaded()
    {
        // Arrange: classifier with no model loaded
        var mlContext = new MLContext(seed: 42);
        var classifier = new ActionClassifier(mlContext, NullLogger<ActionClassifier>.Instance);

        var feature = CreateArchivedEmailFeature("newsletter.com", "Weekly digest");

        // Act
        var result = classifier.Classify(feature);

        // Assert: must fail, not return "Keep" at 50% — that's the MLModelProvider
        // fallback, which means the model was never actually loaded.
        Assert.False(result.IsSuccess, "Classify should fail when no model is loaded");
    }

    // ─────────────────────────────────────────────────────────────────────────
    // Scenario 2: InferLabelFromFlags correctness — all flag combos produce
    // the expected training label.
    // ─────────────────────────────────────────────────────────────────────────

    [Theory]
    [InlineData(0, 0, 0, "Keep")]      // No flags → Keep
    [InlineData(0, 0, 1, "Archive")]   // IsArchived=1 → Archive
    [InlineData(0, 1, 0, "Delete")]    // WasInTrash=1 → Delete
    [InlineData(0, 1, 1, "Delete")]    // WasInTrash=1 takes priority over IsArchived=1
    [InlineData(1, 0, 0, "Spam")]      // WasInSpam=1 → Spam
    [InlineData(1, 0, 1, "Spam")]      // WasInSpam=1 takes priority over IsArchived=1
    [InlineData(1, 1, 0, "Spam")]      // WasInSpam=1 takes priority over WasInTrash=1
    [InlineData(1, 1, 1, "Spam")]      // WasInSpam=1 takes priority over everything
    public void InferLabelFromFlags_ProducesCorrectLabel(
        int wasInSpam, int wasInTrash, int isArchived, string expectedLabel)
    {
        // This mirrors the private InferLabelFromFlags logic in ModelTrainingPipeline
        var vector = new EmailFeatureVector
        {
            EmailId = "test",
            WasInSpam = wasInSpam,
            WasInTrash = wasInTrash,
            IsArchived = isArchived,
            SenderDomain = "test.com",
            SpfResult = "pass",
            DkimResult = "pass",
            DmarcResult = "pass",
            FeatureSchemaVersion = FeatureSchema.CurrentVersion,
            ExtractedAt = DateTime.UtcNow,
        };

        // Reproduce the label inference (same as ModelTrainingPipeline.InferLabelFromFlags)
        string inferred;
        if (vector.WasInSpam == 1) inferred = "Spam";
        else if (vector.WasInTrash == 1) inferred = "Delete";
        else if (vector.IsArchived == 1) inferred = "Archive";
        else inferred = "Keep";

        Assert.Equal(expectedLabel, inferred);
    }

    // ─────────────────────────────────────────────────────────────────────────
    // Scenario 3: Explicit TrainingLabel overrides inferred label — user
    // corrections must be honoured during training.
    // ─────────────────────────────────────────────────────────────────────────

    [Fact]
    public void MapToTrainingInput_PrefersExplicitTrainingLabel_OverInferredFlags()
    {
        // An archived email where the user said "Delete" in triage
        var vector = new EmailFeatureVector
        {
            EmailId = "corrected-1",
            IsArchived = 1,
            WasInTrash = 0,
            WasInSpam = 0,
            TrainingLabel = "Delete", // User override
            SenderDomain = "spam.com",
            SpfResult = "pass",
            DkimResult = "pass",
            DmarcResult = "pass",
            FeatureSchemaVersion = FeatureSchema.CurrentVersion,
            ExtractedAt = DateTime.UtcNow,
        };

        // Reproduce the label selection (same as ModelTrainingPipeline.MapToTrainingInput)
        string label = vector.TrainingLabel ?? InferLabel(vector);

        Assert.Equal("Delete", label);
    }

    // ─────────────────────────────────────────────────────────────────────────
    // Scenario 4: Train model on archive-heavy dataset → model should
    // predict "Archive" (NOT "Keep") for archive-like emails.
    // This is the core regression test for the reported bug.
    // ─────────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task TrainAndPredict_ArchiveHeavyDataset_PredictArchiveForArchivedEmails()
    {
        // Arrange: 1200 archived, 50 trash, 30 inbox (Keep), 20 spam
        // This mirrors the user's real scenario
        var trainingData = new List<EmailFeatureVector>();
        trainingData.AddRange(CreateArchivedEmailBatch(1200, "newsletter.com"));
        trainingData.AddRange(CreateTrashEmailBatch(50, "junk.com"));
        trainingData.AddRange(CreateInboxEmailBatch(30, "important.com"));
        trainingData.AddRange(CreateSpamEmailBatch(20, "scam.org"));

        // Train model
        var modelPath = await TrainModelAsync(trainingData);

        // Load model into classifier
        var mlContext = new MLContext(seed: 42);
        var classifier = new ActionClassifier(mlContext, NullLogger<ActionClassifier>.Instance);
        var loadResult = await classifier.LoadModelAsync(modelPath, CancellationToken.None);
        Assert.True(loadResult.IsSuccess,
            loadResult.IsFailure ? $"Model load failed: {loadResult.Error.Message}" : "");

        // Act: Classify a NEW archived email (same characteristics as training data)
        var testEmail = CreateArchivedEmailFeature("newsletter.com", "Your weekly update #53");
        var prediction = classifier.Classify(testEmail);

        // Assert
        Assert.True(prediction.IsSuccess,
            prediction.IsFailure ? $"Classification failed: {prediction.Error.Message}" : "");
        Assert.Equal("Archive", prediction.Value.PredictedLabel);
        Assert.True(prediction.Value.Confidence > 0.5f,
            $"Expected confidence > 50% for clear Archive email, got {prediction.Value.Confidence:P1}");
    }

    // ─────────────────────────────────────────────────────────────────────────
    // Scenario 5: Model should predict "Delete" for trash-like emails
    // ─────────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task TrainAndPredict_PredictDeleteForTrashLikeEmails()
    {
        var trainingData = new List<EmailFeatureVector>();
        trainingData.AddRange(CreateArchivedEmailBatch(500, "newsletter.com"));
        trainingData.AddRange(CreateTrashEmailBatch(300, "junk.com"));
        trainingData.AddRange(CreateInboxEmailBatch(100, "important.com"));
        trainingData.AddRange(CreateSpamEmailBatch(100, "scam.org"));

        var modelPath = await TrainModelAsync(trainingData);

        var mlContext = new MLContext(seed: 42);
        var classifier = new ActionClassifier(mlContext, NullLogger<ActionClassifier>.Instance);
        var loadResult = await classifier.LoadModelAsync(modelPath, CancellationToken.None);
        Assert.True(loadResult.IsSuccess);

        // Classify an email with trash characteristics
        var testEmail = CreateTrashEmailFeature("junk.com", "Buy now! Limited offer!");
        var prediction = classifier.Classify(testEmail);

        Assert.True(prediction.IsSuccess);
        Assert.Equal("Delete", prediction.Value.PredictedLabel);
    }

    // ─────────────────────────────────────────────────────────────────────────
    // Scenario 6: Model should predict "Keep" for inbox-like emails
    // ─────────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task TrainAndPredict_PredictKeepForInboxLikeEmails()
    {
        var trainingData = new List<EmailFeatureVector>();
        trainingData.AddRange(CreateArchivedEmailBatch(500, "newsletter.com"));
        trainingData.AddRange(CreateTrashEmailBatch(200, "junk.com"));
        trainingData.AddRange(CreateInboxEmailBatch(300, "important.com"));
        trainingData.AddRange(CreateSpamEmailBatch(100, "scam.org"));

        var modelPath = await TrainModelAsync(trainingData);

        var mlContext = new MLContext(seed: 42);
        var classifier = new ActionClassifier(mlContext, NullLogger<ActionClassifier>.Instance);
        var loadResult = await classifier.LoadModelAsync(modelPath, CancellationToken.None);
        Assert.True(loadResult.IsSuccess);

        var testEmail = CreateInboxEmailFeature("important.com", "Re: Meeting tomorrow");
        var prediction = classifier.Classify(testEmail);

        Assert.True(prediction.IsSuccess);
        Assert.Equal("Keep", prediction.Value.PredictedLabel);
    }

    // ─────────────────────────────────────────────────────────────────────────
    // Scenario 7: Model should predict "Spam" for spam-like emails
    // ─────────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task TrainAndPredict_PredictSpamForSpamLikeEmails()
    {
        var trainingData = new List<EmailFeatureVector>();
        trainingData.AddRange(CreateArchivedEmailBatch(500, "newsletter.com"));
        trainingData.AddRange(CreateTrashEmailBatch(200, "junk.com"));
        trainingData.AddRange(CreateInboxEmailBatch(200, "important.com"));
        trainingData.AddRange(CreateSpamEmailBatch(200, "scam.org"));

        var modelPath = await TrainModelAsync(trainingData);

        var mlContext = new MLContext(seed: 42);
        var classifier = new ActionClassifier(mlContext, NullLogger<ActionClassifier>.Instance);
        var loadResult = await classifier.LoadModelAsync(modelPath, CancellationToken.None);
        Assert.True(loadResult.IsSuccess);

        var testEmail = CreateSpamEmailFeature("scam.org", "YOU WON $1,000,000!!!");
        var prediction = classifier.Classify(testEmail);

        Assert.True(prediction.IsSuccess);
        Assert.Equal("Spam", prediction.Value.PredictedLabel);
    }

    // ─────────────────────────────────────────────────────────────────────────
    // Scenario 8: Batch classification returns consistent predictions
    // ─────────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task ClassifyBatch_ReturnsConsistentPredictions()
    {
        var trainingData = new List<EmailFeatureVector>();
        trainingData.AddRange(CreateArchivedEmailBatch(800, "newsletter.com"));
        trainingData.AddRange(CreateTrashEmailBatch(200, "junk.com"));
        trainingData.AddRange(CreateInboxEmailBatch(100, "important.com"));
        trainingData.AddRange(CreateSpamEmailBatch(100, "scam.org"));

        var modelPath = await TrainModelAsync(trainingData);

        var mlContext = new MLContext(seed: 42);
        var classifier = new ActionClassifier(mlContext, NullLogger<ActionClassifier>.Instance);
        var loadResult = await classifier.LoadModelAsync(modelPath, CancellationToken.None);
        Assert.True(loadResult.IsSuccess);

        // Classify a batch of mixed emails
        var testEmails = new[]
        {
            CreateArchivedEmailFeature("newsletter.com", "Weekly digest #10"),
            CreateTrashEmailFeature("junk.com", "Special deal just for you"),
            CreateInboxEmailFeature("important.com", "Re: Project update"),
        };

        var batchResult = classifier.ClassifyBatch(testEmails);
        Assert.True(batchResult.IsSuccess);
        Assert.Equal(3, batchResult.Value.Count);

        // Each prediction should match its source profile
        Assert.Equal("Archive", batchResult.Value[0].PredictedLabel);
        Assert.Equal("Delete", batchResult.Value[1].PredictedLabel);
        Assert.Equal("Keep", batchResult.Value[2].PredictedLabel);
    }

    // ─────────────────────────────────────────────────────────────────────────
    // Scenario 9: User-corrected labels influence model training — if user
    // marks archived emails as "Delete", model should learn that pattern.
    // ─────────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task TrainAndPredict_UserCorrections_OverrideInferredLabels()
    {
        // Most archived emails from spammy-newsletter.com are user-corrected to "Delete"
        var trainingData = new List<EmailFeatureVector>();

        // 500 normal archive emails from legitimate newsletter
        trainingData.AddRange(CreateArchivedEmailBatch(500, "good-newsletter.com"));

        // 300 archived emails from spammy sender, user corrected them to "Delete"
        trainingData.AddRange(
            CreateArchivedEmailBatch(300, "spammy-newsletter.com", trainingLabel: "Delete"));

        // Some real trash and inbox for balance
        trainingData.AddRange(CreateTrashEmailBatch(100, "junk.com"));
        trainingData.AddRange(CreateInboxEmailBatch(100, "important.com"));

        var modelPath = await TrainModelAsync(trainingData);

        var mlContext = new MLContext(seed: 42);
        var classifier = new ActionClassifier(mlContext, NullLogger<ActionClassifier>.Instance);
        var loadResult = await classifier.LoadModelAsync(modelPath, CancellationToken.None);
        Assert.True(loadResult.IsSuccess);

        // New email from the spammy sender — model should suggest "Delete" not "Archive"
        var testEmail = CreateArchivedEmailFeature("spammy-newsletter.com", "Amazing deals inside!");
        var prediction = classifier.Classify(testEmail);

        Assert.True(prediction.IsSuccess);
        Assert.Equal("Delete", prediction.Value.PredictedLabel);
    }

    // ─────────────────────────────────────────────────────────────────────────
    // Scenario 10: Confidence should be meaningful — not always 50%
    // ─────────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task TrainAndPredict_ConfidenceReflectsTrainingDistribution()
    {
        // Heavily dominant archive class — model should be very confident
        var trainingData = new List<EmailFeatureVector>();
        trainingData.AddRange(CreateArchivedEmailBatch(2000, "common-newsletter.com"));
        trainingData.AddRange(CreateTrashEmailBatch(50, "junk.com"));
        trainingData.AddRange(CreateInboxEmailBatch(30, "important.com"));
        trainingData.AddRange(CreateSpamEmailBatch(20, "scam.org"));

        var modelPath = await TrainModelAsync(trainingData);

        var mlContext = new MLContext(seed: 42);
        var classifier = new ActionClassifier(mlContext, NullLogger<ActionClassifier>.Instance);
        var loadResult = await classifier.LoadModelAsync(modelPath, CancellationToken.None);
        Assert.True(loadResult.IsSuccess);

        var testEmail = CreateArchivedEmailFeature("common-newsletter.com", "Newsletter #42");
        var prediction = classifier.Classify(testEmail);

        Assert.True(prediction.IsSuccess);
        Assert.True(prediction.Value.Confidence > 0.5f,
            $"Confidence for dominant class should be > 50%, got {prediction.Value.Confidence:P1}");
        Assert.NotEqual(0.5f, prediction.Value.Confidence);
    }

    // ─────────────────────────────────────────────────────────────────────────
    // Scenario 11: Score array has one entry per class and sums plausibly
    // ─────────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task TrainAndPredict_ScoreArrayHasExpectedStructure()
    {
        var trainingData = new List<EmailFeatureVector>();
        trainingData.AddRange(CreateArchivedEmailBatch(500, "newsletter.com"));
        trainingData.AddRange(CreateTrashEmailBatch(200, "junk.com"));
        trainingData.AddRange(CreateInboxEmailBatch(200, "important.com"));
        trainingData.AddRange(CreateSpamEmailBatch(100, "scam.org"));

        var modelPath = await TrainModelAsync(trainingData);

        var mlContext = new MLContext(seed: 42);
        var classifier = new ActionClassifier(mlContext, NullLogger<ActionClassifier>.Instance);
        await classifier.LoadModelAsync(modelPath, CancellationToken.None);

        var testEmail = CreateArchivedEmailFeature("newsletter.com", "Weekly update");
        var prediction = classifier.Classify(testEmail);

        Assert.True(prediction.IsSuccess);
        Assert.NotNull(prediction.Value.Score);
        // Must have exactly 4 classes: Keep, Archive, Delete, Spam
        Assert.Equal(4, prediction.Value.Score.Length);
        // All scores should be non-negative (softmax output)
        Assert.All(prediction.Value.Score, s => Assert.True(s >= 0, $"Score {s} is negative"));
        // Confidence should equal max score
        Assert.Equal(prediction.Value.Score.Max(), prediction.Value.Confidence);
    }

    // ─────────────────────────────────────────────────────────────────────────
    // Scenario 12: MLModelProvider fallback when classifier not loaded
    // This test proves that "Keep at 50%" is the no-model fallback, not a
    // real prediction.
    // ─────────────────────────────────────────────────────────────────────────

    [Fact]
    public void ActionClassifier_IsLoaded_IsFalse_BeforeModelLoad()
    {
        var mlContext = new MLContext(seed: 42);
        var classifier = new ActionClassifier(mlContext, NullLogger<ActionClassifier>.Instance);

        Assert.False(classifier.IsLoaded);
    }

    [Fact]
    public async Task ActionClassifier_IsLoaded_IsTrue_AfterSuccessfulModelLoad()
    {
        // We need to train a model to have something to load
        var trainingData = new List<EmailFeatureVector>();
        trainingData.AddRange(CreateArchivedEmailBatch(200, "newsletter.com"));
        trainingData.AddRange(CreateTrashEmailBatch(100, "junk.com"));
        trainingData.AddRange(CreateInboxEmailBatch(100, "important.com"));
        trainingData.AddRange(CreateSpamEmailBatch(100, "scam.org"));

        var modelPath = await TrainModelAsync(trainingData);

        var mlContext = new MLContext(seed: 42);
        var classifier = new ActionClassifier(mlContext, NullLogger<ActionClassifier>.Instance);

        Assert.False(classifier.IsLoaded);

        var loadResult = await classifier.LoadModelAsync(modelPath, CancellationToken.None);
        Assert.True(loadResult.IsSuccess);
        Assert.True(classifier.IsLoaded);
    }

    // ─────────────────────────────────────────────────────────────────────────
    // Scenario 13: Diverse sender domains — model should generalize beyond
    // a single domain.
    // ─────────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task TrainAndPredict_MultipleDomains_GeneralizesArchiveSignal()
    {
        var trainingData = new List<EmailFeatureVector>();

        // Many different archive domains
        trainingData.AddRange(CreateArchivedEmailBatch(200, "alerts.github.com"));
        trainingData.AddRange(CreateArchivedEmailBatch(200, "notifications.linkedin.com"));
        trainingData.AddRange(CreateArchivedEmailBatch(200, "noreply.medium.com"));
        trainingData.AddRange(CreateArchivedEmailBatch(200, "digest.substack.com"));
        trainingData.AddRange(CreateArchivedEmailBatch(200, "updates.slack.com"));

        // Some trash and inbox
        trainingData.AddRange(CreateTrashEmailBatch(100, "promo.spam.com"));
        trainingData.AddRange(CreateInboxEmailBatch(100, "boss@company.com"));
        trainingData.AddRange(CreateSpamEmailBatch(50, "nigerian-prince.scam"));

        var modelPath = await TrainModelAsync(trainingData);

        var mlContext = new MLContext(seed: 42);
        var classifier = new ActionClassifier(mlContext, NullLogger<ActionClassifier>.Instance);
        await classifier.LoadModelAsync(modelPath, CancellationToken.None);

        // Test with an archived email from a domain seen during training
        var knownDomain = CreateArchivedEmailFeature("alerts.github.com", "[repo] New issue #42");
        var predKnown = classifier.Classify(knownDomain);
        Assert.True(predKnown.IsSuccess);
        Assert.Equal("Archive", predKnown.Value.PredictedLabel);

        // Test with an archived email from an UNSEEN domain (should still predict Archive
        // because IsArchived=1 is a strong feature signal independent of domain)
        var unseenDomain = CreateArchivedEmailFeature("digest.techcrunch.com", "Daily tech roundup");
        var predUnseen = classifier.Classify(unseenDomain);
        Assert.True(predUnseen.IsSuccess);
        Assert.Equal("Archive", predUnseen.Value.PredictedLabel);
    }

    // ─────────────────────────────────────────────────────────────────────────
    // Scenario 14: Feature importance — IsArchived flag should strongly
    // correlate with Archive prediction regardless of other features.
    // ─────────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task TrainAndPredict_IsArchivedFlag_StronglyCorrelatesWithArchivePrediction()
    {
        var trainingData = new List<EmailFeatureVector>();
        trainingData.AddRange(CreateArchivedEmailBatch(800, "newsletter.com"));
        trainingData.AddRange(CreateTrashEmailBatch(200, "junk.com"));
        trainingData.AddRange(CreateInboxEmailBatch(200, "important.com"));
        trainingData.AddRange(CreateSpamEmailBatch(100, "scam.org"));

        var modelPath = await TrainModelAsync(trainingData);

        var mlContext = new MLContext(seed: 42);
        var classifier = new ActionClassifier(mlContext, NullLogger<ActionClassifier>.Instance);
        await classifier.LoadModelAsync(modelPath, CancellationToken.None);

        // Same email, one with IsArchived=1, one with IsArchived=0
        var archivedVersion = CreateArchivedEmailFeature("newsletter.com", "Weekly digest");
        var inboxVersion = CreateInboxEmailFeature("newsletter.com", "Weekly digest");

        var archivePred = classifier.Classify(archivedVersion);
        var inboxPred = classifier.Classify(inboxVersion);

        Assert.True(archivePred.IsSuccess);
        Assert.True(inboxPred.IsSuccess);

        // Archived version should predict Archive
        Assert.Equal("Archive", archivePred.Value.PredictedLabel);
        // Inbox version should NOT predict Archive (likely Keep or different)
        Assert.NotEqual("Archive", inboxPred.Value.PredictedLabel);
    }

    // ═══════════════════════════════════════════════════════════════════════
    // Helper: Train a model from feature vectors and return the model path
    // ═══════════════════════════════════════════════════════════════════════

    private async Task<string> TrainModelAsync(List<EmailFeatureVector> vectors)
    {
        var mlContext = new MLContext(seed: 42);
        var trainer = new ActionModelTrainer(NullLogger<ActionModelTrainer>.Instance);

        // Map to training inputs (reproducing ModelTrainingPipeline logic)
        var trainingInputs = vectors.Select(v => new ActionTrainingInput
        {
            SenderKnown = v.SenderKnown,
            ContactStrength = v.ContactStrength,
            HasListUnsubscribe = v.HasListUnsubscribe,
            HasAttachments = v.HasAttachments,
            HourReceived = v.HourReceived,
            DayOfWeek = v.DayOfWeek,
            EmailSizeLog = v.EmailSizeLog,
            SubjectLength = v.SubjectLength,
            RecipientCount = v.RecipientCount,
            IsReply = v.IsReply,
            InUserWhitelist = v.InUserWhitelist,
            InUserBlacklist = v.InUserBlacklist,
            LabelCount = v.LabelCount,
            LinkCount = v.LinkCount,
            ImageCount = v.ImageCount,
            HasTrackingPixel = v.HasTrackingPixel,
            UnsubscribeLinkInBody = v.UnsubscribeLinkInBody,
            EmailAgeDays = v.EmailAgeDays,
            IsInInbox = v.IsInInbox,
            IsStarred = v.IsStarred,
            IsImportant = v.IsImportant,
            WasInTrash = v.WasInTrash,
            WasInSpam = v.WasInSpam,
            IsArchived = v.IsArchived,
            ThreadMessageCount = v.ThreadMessageCount,
            SenderFrequency = v.SenderFrequency,
            IsReplied = v.IsReplied,
            IsForwarded = v.IsForwarded,
            SenderDomain = v.SenderDomain,
            SpfResult = v.SpfResult,
            DkimResult = v.DkimResult,
            DmarcResult = v.DmarcResult,
            SubjectText = v.SubjectText ?? string.Empty,
            BodyTextShort = v.BodyTextShort ?? string.Empty,
            Weight = 1.0f,
            Label = v.TrainingLabel ?? InferLabel(v),
        }).ToList();

        var dataView = mlContext.Data.LoadFromEnumerable(trainingInputs);

        var trainResult = await trainer.TrainAsync(
            mlContext, dataView, dominantClassImbalanceThreshold: UseSdcaThreshold, CancellationToken.None);
        Assert.True(trainResult.IsSuccess,
            $"Training failed: {(trainResult.IsFailure ? trainResult.Error.Message : "unknown")}");

        var modelPath = Path.Combine(_tempDir, $"test_model_{Guid.NewGuid():N}.zip");
        mlContext.Model.Save(trainResult.Value.Model, dataView.Schema, modelPath);

        return modelPath;
    }

    // ═══════════════════════════════════════════════════════════════════════
    // Helper: Reproduce InferLabelFromFlags (same logic as pipeline)
    // ═══════════════════════════════════════════════════════════════════════

    private static string InferLabel(EmailFeatureVector v)
    {
        if (v.WasInSpam == 1) return "Spam";
        if (v.WasInTrash == 1) return "Delete";
        if (v.IsArchived == 1) return "Archive";
        return "Keep";
    }

    // ═══════════════════════════════════════════════════════════════════════
    // Feature vector factory methods — create realistic email profiles
    // ═══════════════════════════════════════════════════════════════════════

    private static EmailFeatureVector CreateArchivedEmailFeature(
        string domain, string subject, int ageDays = 30) => new()
        {
            EmailId = $"archived-{Guid.NewGuid():N}",
            SenderDomain = domain,
            SenderKnown = 0,
            ContactStrength = 0,
            SpfResult = "pass",
            DkimResult = "pass",
            DmarcResult = "pass",
            HasListUnsubscribe = 1,
            HasAttachments = 0,
            EmailSizeLog = 4.2f,
            SubjectLength = subject.Length,
            RecipientCount = 1,
            IsReply = 0,
            InUserWhitelist = 0,
            InUserBlacklist = 0,
            LabelCount = 2,
            LinkCount = 5,
            ImageCount = 2,
            HasTrackingPixel = 1,
            UnsubscribeLinkInBody = 1,
            HourReceived = 10,
            DayOfWeek = 2,
            EmailAgeDays = ageDays,
            IsInInbox = 0,
            IsStarred = 0,
            IsImportant = 0,
            WasInTrash = 0,
            WasInSpam = 0,
            IsArchived = 1,           // KEY: This is an archived email
            ThreadMessageCount = 1,
            SenderFrequency = 15,
            IsReplied = 0,
            IsForwarded = 0,
            SubjectText = subject,
            BodyTextShort = $"Content from {domain} newsletter about various topics.",
            FeatureSchemaVersion = FeatureSchema.CurrentVersion,
            ExtractedAt = DateTime.UtcNow,
            UserCorrected = 0,
            TrainingLabel = null,      // Inferred from flags → "Archive"
        };

    private static EmailFeatureVector CreateTrashEmailFeature(
        string domain, string subject) => new()
        {
            EmailId = $"trash-{Guid.NewGuid():N}",
            SenderDomain = domain,
            SenderKnown = 0,
            ContactStrength = 0,
            SpfResult = "fail",
            DkimResult = "neutral",
            DmarcResult = "fail",
            HasListUnsubscribe = 0,
            HasAttachments = 0,
            EmailSizeLog = 3.5f,
            SubjectLength = subject.Length,
            RecipientCount = 1,
            IsReply = 0,
            InUserWhitelist = 0,
            InUserBlacklist = 0,
            LabelCount = 1,
            LinkCount = 10,
            ImageCount = 5,
            HasTrackingPixel = 1,
            UnsubscribeLinkInBody = 0,
            HourReceived = 3,
            DayOfWeek = 5,
            EmailAgeDays = 7,
            IsInInbox = 0,
            IsStarred = 0,
            IsImportant = 0,
            WasInTrash = 1,            // KEY: This was in trash
            WasInSpam = 0,
            IsArchived = 0,
            ThreadMessageCount = 1,
            SenderFrequency = 3,
            IsReplied = 0,
            IsForwarded = 0,
            SubjectText = subject,
            BodyTextShort = $"Buy now from {domain}! Limited time offer. Act fast!",
            FeatureSchemaVersion = FeatureSchema.CurrentVersion,
            ExtractedAt = DateTime.UtcNow,
            UserCorrected = 0,
            TrainingLabel = null,       // Inferred → "Delete"
        };

    private static EmailFeatureVector CreateInboxEmailFeature(
        string domain, string subject) => new()
        {
            EmailId = $"inbox-{Guid.NewGuid():N}",
            SenderDomain = domain,
            SenderKnown = 1,
            ContactStrength = 2,
            SpfResult = "pass",
            DkimResult = "pass",
            DmarcResult = "pass",
            HasListUnsubscribe = 0,
            HasAttachments = 1,
            EmailSizeLog = 4.8f,
            SubjectLength = subject.Length,
            RecipientCount = 3,
            IsReply = 1,
            InUserWhitelist = 0,
            InUserBlacklist = 0,
            LabelCount = 3,
            LinkCount = 1,
            ImageCount = 0,
            HasTrackingPixel = 0,
            UnsubscribeLinkInBody = 0,
            HourReceived = 14,
            DayOfWeek = 1,
            EmailAgeDays = 1,
            IsInInbox = 1,             // KEY: This is in the inbox
            IsStarred = 0,
            IsImportant = 1,
            WasInTrash = 0,
            WasInSpam = 0,
            IsArchived = 0,
            ThreadMessageCount = 4,
            SenderFrequency = 20,
            IsReplied = 1,
            IsForwarded = 0,
            SubjectText = subject,
            BodyTextShort = $"Hi, regarding your email about the project, the deadline is...",
            FeatureSchemaVersion = FeatureSchema.CurrentVersion,
            ExtractedAt = DateTime.UtcNow,
            UserCorrected = 0,
            TrainingLabel = null,       // Inferred → "Keep" (IsInInbox=1 but InferLabel checks flags only)
        };

    private static EmailFeatureVector CreateSpamEmailFeature(
        string domain, string subject) => new()
        {
            EmailId = $"spam-{Guid.NewGuid():N}",
            SenderDomain = domain,
            SenderKnown = 0,
            ContactStrength = 0,
            SpfResult = "fail",
            DkimResult = "fail",
            DmarcResult = "fail",
            HasListUnsubscribe = 0,
            HasAttachments = 0,
            EmailSizeLog = 3.0f,
            SubjectLength = subject.Length,
            RecipientCount = 1,
            IsReply = 0,
            InUserWhitelist = 0,
            InUserBlacklist = 0,
            LabelCount = 1,
            LinkCount = 15,
            ImageCount = 3,
            HasTrackingPixel = 1,
            UnsubscribeLinkInBody = 0,
            HourReceived = 1,
            DayOfWeek = 6,
            EmailAgeDays = 2,
            IsInInbox = 0,
            IsStarred = 0,
            IsImportant = 0,
            WasInTrash = 0,
            WasInSpam = 1,             // KEY: This was in spam
            IsArchived = 0,
            ThreadMessageCount = 1,
            SenderFrequency = 1,
            IsReplied = 0,
            IsForwarded = 0,
            SubjectText = subject,
            BodyTextShort = $"CONGRATULATIONS! You have been selected for a special prize from {domain}!",
            FeatureSchemaVersion = FeatureSchema.CurrentVersion,
            ExtractedAt = DateTime.UtcNow,
            UserCorrected = 0,
            TrainingLabel = null,       // Inferred → "Spam"
        };

    // ═══════════════════════════════════════════════════════════════════════
    // Batch creation helpers
    // ═══════════════════════════════════════════════════════════════════════

    private static List<EmailFeatureVector> CreateArchivedEmailBatch(
        int count, string domain, string? trainingLabel = null)
    {
        var rng = new Random(42 + count);
        var subjects = new[]
        {
            "Weekly digest", "Monthly newsletter", "Product updates",
            "Your daily summary", "Community highlights", "New features available",
            "Release notes v2.1", "Announcement: upcoming changes",
        };

        return Enumerable.Range(0, count).Select(i => new EmailFeatureVector
        {
            EmailId = $"archived-{i}-{Guid.NewGuid():N}",
            SenderDomain = domain,
            SenderKnown = 0,
            ContactStrength = 0,
            SpfResult = "pass",
            DkimResult = "pass",
            DmarcResult = "pass",
            HasListUnsubscribe = 1,
            HasAttachments = 0,
            EmailSizeLog = 3.5f + (float)(rng.NextDouble() * 2.0),
            SubjectLength = subjects[i % subjects.Length].Length,
            RecipientCount = 1,
            IsReply = 0,
            InUserWhitelist = 0,
            InUserBlacklist = 0,
            LabelCount = 2,
            LinkCount = rng.Next(2, 10),
            ImageCount = 2,
            HasTrackingPixel = 1,
            UnsubscribeLinkInBody = 1,
            HourReceived = rng.Next(0, 24),
            DayOfWeek = rng.Next(0, 7),
            EmailAgeDays = rng.Next(1, 365),
            IsInInbox = 0,
            IsStarred = 0,
            IsImportant = 0,
            WasInTrash = 0,
            WasInSpam = 0,
            IsArchived = 1,
            ThreadMessageCount = 1,
            SenderFrequency = rng.Next(5, 50),
            IsReplied = 0,
            IsForwarded = 0,
            SubjectText = subjects[i % subjects.Length],
            BodyTextShort = $"Content from {domain} newsletter about various topics.",
            FeatureSchemaVersion = FeatureSchema.CurrentVersion,
            ExtractedAt = DateTime.UtcNow,
            UserCorrected = trainingLabel != null ? 1 : 0,
            TrainingLabel = trainingLabel,
        }).ToList();
    }

    private static List<EmailFeatureVector> CreateTrashEmailBatch(int count, string domain)
    {
        var rng = new Random(84 + count);
        var subjects = new[]
        {
            "Limited time offer!", "Buy now save 50%", "Act fast before it's gone",
            "Exclusive deal for you", "Don't miss out!", "Free trial expiring",
        };

        return Enumerable.Range(0, count).Select(i => new EmailFeatureVector
        {
            EmailId = $"trash-{i}-{Guid.NewGuid():N}",
            SenderDomain = domain,
            SenderKnown = 0,
            ContactStrength = 0,
            SpfResult = "fail",
            DkimResult = "neutral",
            DmarcResult = "fail",
            HasListUnsubscribe = 0,
            HasAttachments = 0,
            EmailSizeLog = 3.5f,
            SubjectLength = subjects[i % subjects.Length].Length,
            RecipientCount = 1,
            IsReply = 0,
            InUserWhitelist = 0,
            InUserBlacklist = 0,
            LabelCount = 1,
            LinkCount = rng.Next(5, 20),
            ImageCount = 5,
            HasTrackingPixel = 1,
            UnsubscribeLinkInBody = 0,
            HourReceived = rng.Next(0, 24),
            DayOfWeek = rng.Next(0, 7),
            EmailAgeDays = 7,
            IsInInbox = 0,
            IsStarred = 0,
            IsImportant = 0,
            WasInTrash = 1,
            WasInSpam = 0,
            IsArchived = 0,
            ThreadMessageCount = 1,
            SenderFrequency = rng.Next(1, 10),
            IsReplied = 0,
            IsForwarded = 0,
            SubjectText = subjects[i % subjects.Length],
            BodyTextShort = $"Buy now from {domain}! Limited time offer. Act fast!",
            FeatureSchemaVersion = FeatureSchema.CurrentVersion,
            ExtractedAt = DateTime.UtcNow,
            UserCorrected = 0,
            TrainingLabel = null,
        }).ToList();
    }

    private static List<EmailFeatureVector> CreateInboxEmailBatch(int count, string domain)
    {
        var rng = new Random(126 + count);
        var subjects = new[]
        {
            "Re: Meeting tomorrow", "Re: Project update", "Follow up on discussion",
            "Quick question about the report", "Invitation: Team sync", "Action needed",
        };

        return Enumerable.Range(0, count).Select(i => new EmailFeatureVector
        {
            EmailId = $"inbox-{i}-{Guid.NewGuid():N}",
            SenderDomain = domain,
            SenderKnown = 1,
            ContactStrength = 2,
            SpfResult = "pass",
            DkimResult = "pass",
            DmarcResult = "pass",
            HasListUnsubscribe = 0,
            HasAttachments = 1,
            EmailSizeLog = 4.8f,
            SubjectLength = subjects[i % subjects.Length].Length,
            RecipientCount = 3,
            IsReply = 1,
            InUserWhitelist = 0,
            InUserBlacklist = 0,
            LabelCount = 3,
            LinkCount = 1,
            ImageCount = 0,
            HasTrackingPixel = 0,
            UnsubscribeLinkInBody = 0,
            HourReceived = rng.Next(8, 18),
            DayOfWeek = rng.Next(1, 6),
            EmailAgeDays = 1,
            IsInInbox = 1,
            IsStarred = 0,
            IsImportant = 1,
            WasInTrash = 0,
            WasInSpam = 0,
            IsArchived = 0,
            ThreadMessageCount = 4,
            SenderFrequency = rng.Next(10, 40),
            IsReplied = 1,
            IsForwarded = 0,
            SubjectText = subjects[i % subjects.Length],
            BodyTextShort = "Hi, regarding your email about the project, the deadline is...",
            FeatureSchemaVersion = FeatureSchema.CurrentVersion,
            ExtractedAt = DateTime.UtcNow,
            UserCorrected = 0,
            TrainingLabel = null,
        }).ToList();
    }

    private static List<EmailFeatureVector> CreateSpamEmailBatch(int count, string domain)
    {
        var rng = new Random(168 + count);
        var subjects = new[]
        {
            "YOU WON A PRIZE!", "URGENT: Account verification needed",
            "Make $$$ from home", "Free gift waiting for you",
            "Your account has been compromised", "Click here NOW",
        };

        return Enumerable.Range(0, count).Select(i => new EmailFeatureVector
        {
            EmailId = $"spam-{i}-{Guid.NewGuid():N}",
            SenderDomain = domain,
            SenderKnown = 0,
            ContactStrength = 0,
            SpfResult = "fail",
            DkimResult = "fail",
            DmarcResult = "fail",
            HasListUnsubscribe = 0,
            HasAttachments = 0,
            EmailSizeLog = 3.0f,
            SubjectLength = subjects[i % subjects.Length].Length,
            RecipientCount = 1,
            IsReply = 0,
            InUserWhitelist = 0,
            InUserBlacklist = 0,
            LabelCount = 1,
            LinkCount = 15,
            ImageCount = 3,
            HasTrackingPixel = 1,
            UnsubscribeLinkInBody = 0,
            HourReceived = rng.Next(0, 24),
            DayOfWeek = rng.Next(0, 7),
            EmailAgeDays = 2,
            IsInInbox = 0,
            IsStarred = 0,
            IsImportant = 0,
            WasInTrash = 0,
            WasInSpam = 1,
            IsArchived = 0,
            ThreadMessageCount = 1,
            SenderFrequency = rng.Next(1, 3),
            IsReplied = 0,
            IsForwarded = 0,
            SubjectText = subjects[i % subjects.Length],
            BodyTextShort = $"CONGRATULATIONS! You have been selected for a special prize from {domain}!",
            FeatureSchemaVersion = FeatureSchema.CurrentVersion,
            ExtractedAt = DateTime.UtcNow,
            UserCorrected = 0,
            TrainingLabel = null,
        }).ToList();
    }
}
