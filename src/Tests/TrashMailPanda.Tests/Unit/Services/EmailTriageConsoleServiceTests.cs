using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Spectre.Console;
using TrashMailPanda.Models.Console;
using TrashMailPanda.Providers.ML.Models;
using TrashMailPanda.Providers.Storage.Models;
using TrashMailPanda.Services;
using TrashMailPanda.Services.Console;
using TrashMailPanda.Shared.Base;
using Xunit;

namespace TrashMailPanda.Tests.Unit.Services;

[Trait("Category", "Unit")]
public sealed class EmailTriageConsoleServiceTests
{
    // ── Helpers ───────────────────────────────────────────────────────────────

    private static EmailFeatureVector MakeVector(string id = "email-1", string subject = "Test Subject") =>
        new()
        {
            EmailId = id,
            SenderDomain = "example.com",
            SubjectText = subject,
            EmailAgeDays = 5,
        };

    private static TriageSessionInfo ColdStartInfo(int labeled = 5, int threshold = 100) =>
        new(TriageMode.ColdStart, labeled, threshold, labeled >= threshold);

    private static TriageSessionInfo AiAssistedInfo(int labeled = 150, int threshold = 100) =>
        new(TriageMode.AiAssisted, labeled, threshold, labeled >= threshold);

    private static TriageDecision MakeDecision(string action, bool isOverride) =>
        new(EmailId: "email-1", ChosenAction: action, AiRecommendation: null,
            ConfidenceScore: null, IsOverride: isOverride, DecidedAtUtc: DateTime.UtcNow);

    /// <summary>
    /// Creates the console service with injectable key sequences and captured output.
    /// <paramref name="keys"/> feed the ReadKey calls (action dispatch + threshold prompt).
    /// </summary>
    private static (EmailTriageConsoleService Svc, StringWriter Writer) CreateService(
        Mock<IEmailTriageService> triageMock,
        Queue<ConsoleKeyInfo> keys,
        Mock<IConsoleHelpPanel>? helpPanelMock = null)
    {
        var writer = new StringWriter();
        var console = AnsiConsole.Create(new AnsiConsoleSettings
        {
            Ansi = AnsiSupport.No,
            ColorSystem = ColorSystemSupport.NoColors,
            Out = new AnsiConsoleOutput(writer),
        });

        helpPanelMock ??= new Mock<IConsoleHelpPanel>();
        helpPanelMock
            .Setup(h => h.ShowAsync(It.IsAny<HelpContext>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);

        var svc = new EmailTriageConsoleService(
            triageService: triageMock.Object,
            helpPanel: helpPanelMock.Object,
            logger: NullLogger<EmailTriageConsoleService>.Instance,
            console: console,
            readKey: () => keys.Count > 0
                ? keys.Dequeue()
                : new ConsoleKeyInfo('Q', ConsoleKey.Q, false, false, false));

        return (svc, writer);
    }

    // ── T038 — Cold-Start UI tests ────────────────────────────────────────────

    /// <summary>T038: In cold-start mode no AI recommendation is rendered.</summary>
    [Fact]
    public async Task RunAsync_ColdStart_NoAiRecommendationRendered()
    {
        var triage = new Mock<IEmailTriageService>();
        triage.Setup(t => t.GetSessionInfoAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result<TriageSessionInfo>.Success(ColdStartInfo(labeled: 0)));

        triage.SetupSequence(t => t.GetNextBatchAsync(It.IsAny<int>(), It.IsAny<int>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result<IReadOnlyList<EmailFeatureVector>>.Success(
                new List<EmailFeatureVector> { MakeVector() }))
            .ReturnsAsync(Result<IReadOnlyList<EmailFeatureVector>>.Success(
                new List<EmailFeatureVector>()));

        triage.Setup(t => t.ApplyDecisionAsync(It.IsAny<string>(), "Keep", It.IsAny<string?>(), It.IsAny<bool>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result<TriageDecision>.Success(MakeDecision("Keep", false)));

        // K = Keep, then loop terminates on empty batch
        var keys = new Queue<ConsoleKeyInfo>([
            new ConsoleKeyInfo('K', ConsoleKey.K, false, false, false),
        ]);

        var (svc, writer) = CreateService(triage, keys);
        await svc.RunAsync("me");

        var output = writer.ToString();
        Assert.DoesNotContain("AI suggests", output);
        Assert.Contains("No AI suggestions", output);
    }

    /// <summary>T038: Progress counter (labeled/threshold) is shown in card output.</summary>
    [Fact]
    public async Task RunAsync_ColdStart_ProgressCounterShownInCard()
    {
        var triage = new Mock<IEmailTriageService>();
        triage.Setup(t => t.GetSessionInfoAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result<TriageSessionInfo>.Success(ColdStartInfo(labeled: 42, threshold: 100)));

        triage.SetupSequence(t => t.GetNextBatchAsync(It.IsAny<int>(), It.IsAny<int>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result<IReadOnlyList<EmailFeatureVector>>.Success(
                new List<EmailFeatureVector> { MakeVector() }))
            .ReturnsAsync(Result<IReadOnlyList<EmailFeatureVector>>.Success(
                new List<EmailFeatureVector>()));

        triage.Setup(t => t.ApplyDecisionAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string?>(), It.IsAny<bool>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result<TriageDecision>.Success(MakeDecision("Keep", false)));

        var keys = new Queue<ConsoleKeyInfo>([
            new ConsoleKeyInfo('K', ConsoleKey.K, false, false, false),
        ]);

        var (svc, writer) = CreateService(triage, keys);
        await svc.RunAsync("me");

        var output = writer.ToString();
        // Card shows "42 / 100 labels collected"
        Assert.Contains("42", output);
        Assert.Contains("100", output);
    }

    /// <summary>T038: Threshold prompt is shown exactly once per session when labeled >= threshold.</summary>
    [Fact]
    public async Task RunAsync_ColdStart_ThresholdPromptShownExactlyOnce()
    {
        var triage = new Mock<IEmailTriageService>();
        // Start at 99/100 — one decision pushes it over
        triage.Setup(t => t.GetSessionInfoAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result<TriageSessionInfo>.Success(ColdStartInfo(labeled: 99, threshold: 100)));

        triage.SetupSequence(t => t.GetNextBatchAsync(It.IsAny<int>(), It.IsAny<int>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result<IReadOnlyList<EmailFeatureVector>>.Success(
                new List<EmailFeatureVector> { MakeVector("id-1"), MakeVector("id-2") }))
            .ReturnsAsync(Result<IReadOnlyList<EmailFeatureVector>>.Success(
                new List<EmailFeatureVector>()));

        triage.Setup(t => t.ApplyDecisionAsync(It.IsAny<string>(), "Keep", It.IsAny<string?>(), It.IsAny<bool>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result<TriageDecision>.Success(MakeDecision("Keep", false)));

        // Two K presses + one key to dismiss threshold prompt + Q to stop the second batch loop
        var keys = new Queue<ConsoleKeyInfo>([
            new ConsoleKeyInfo('K', ConsoleKey.K, false, false, false),  // decide id-1 → threshold reached
            new ConsoleKeyInfo('\r', ConsoleKey.Enter, false, false, false), // dismiss threshold prompt
            new ConsoleKeyInfo('K', ConsoleKey.K, false, false, false),  // decide id-2
        ]);

        var (svc, writer) = CreateService(triage, keys);
        await svc.RunAsync("me");

        var output = writer.ToString();
        // Count occurrences of threshold message
        var occurrences = CountOccurrences(output, "Training threshold reached");
        Assert.Equal(1, occurrences);
    }

    // ── T044 — AI-Assisted UI tests ───────────────────────────────────────────

    /// <summary>T044: In AI-assisted mode the recommendation and confidence score are rendered.</summary>
    [Fact]
    public async Task RunAsync_AiAssisted_RendersRecommendationAndConfidence()
    {
        var prediction = new ActionPrediction { PredictedLabel = "Archive", Confidence = 0.85f };

        var triage = new Mock<IEmailTriageService>();
        triage.Setup(t => t.GetSessionInfoAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result<TriageSessionInfo>.Success(AiAssistedInfo()));

        triage.SetupSequence(t => t.GetNextBatchAsync(It.IsAny<int>(), 0, It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result<IReadOnlyList<EmailFeatureVector>>.Success(
                new List<EmailFeatureVector> { MakeVector() }))
            .ReturnsAsync(Result<IReadOnlyList<EmailFeatureVector>>.Success(
                new List<EmailFeatureVector>()));

        triage.Setup(t => t.GetAiRecommendationAsync(It.IsAny<EmailFeatureVector>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result<ActionPrediction?>.Success(prediction));

        triage.Setup(t => t.ApplyDecisionAsync(It.IsAny<string>(), "Archive", It.IsAny<string?>(), It.IsAny<bool>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result<TriageDecision>.Success(MakeDecision("Archive", false)));

        // Enter = accept AI recommendation
        var keys = new Queue<ConsoleKeyInfo>([
            new ConsoleKeyInfo('\r', ConsoleKey.Enter, false, false, false),
        ]);

        var (svc, writer) = CreateService(triage, keys);
        await svc.RunAsync("me");

        var output = writer.ToString();
        Assert.Contains("AI suggests", output);
        Assert.Contains("Archive", output);
        Assert.Contains("85%", output);
    }

    /// <summary>T044: Accept with Enter calls ApplyDecisionAsync with chosenAction == aiRecommendation.</summary>
    [Fact]
    public async Task RunAsync_AiAssisted_AcceptEnter_CallsApplyWithSameAction()
    {
        var prediction = new ActionPrediction { PredictedLabel = "Archive", Confidence = 0.90f };

        var triage = new Mock<IEmailTriageService>();
        triage.Setup(t => t.GetSessionInfoAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result<TriageSessionInfo>.Success(AiAssistedInfo()));

        triage.SetupSequence(t => t.GetNextBatchAsync(It.IsAny<int>(), 0, It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result<IReadOnlyList<EmailFeatureVector>>.Success(
                new List<EmailFeatureVector> { MakeVector() }))
            .ReturnsAsync(Result<IReadOnlyList<EmailFeatureVector>>.Success(
                new List<EmailFeatureVector>()));

        triage.Setup(t => t.GetAiRecommendationAsync(It.IsAny<EmailFeatureVector>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result<ActionPrediction?>.Success(prediction));

        triage.Setup(t => t.ApplyDecisionAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string?>(), It.IsAny<bool>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result<TriageDecision>.Success(MakeDecision("Archive", false)));

        var keys = new Queue<ConsoleKeyInfo>([
            new ConsoleKeyInfo('\r', ConsoleKey.Enter, false, false, false),
        ]);

        var (svc, writer) = CreateService(triage, keys);
        await svc.RunAsync("me");

        // Verify: chosenAction == AI recommendation ("Archive"), aiRec == "Archive"
        triage.Verify(t => t.ApplyDecisionAsync(
            It.IsAny<string>(),
            "Archive",         // chosenAction matches AI recommendation
            "Archive",         // aiRecommendation passed through
            It.IsAny<bool>(),
            It.IsAny<CancellationToken>()), Times.Once);
    }

    /// <summary>T044: Override key calls ApplyDecisionAsync with different chosenAction and aiRecommendation.</summary>
    [Fact]
    public async Task RunAsync_AiAssisted_OverrideKey_CallsApplyWithDifferentAction()
    {
        var prediction = new ActionPrediction { PredictedLabel = "Archive", Confidence = 0.75f };

        var triage = new Mock<IEmailTriageService>();
        triage.Setup(t => t.GetSessionInfoAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result<TriageSessionInfo>.Success(AiAssistedInfo()));

        triage.SetupSequence(t => t.GetNextBatchAsync(It.IsAny<int>(), 0, It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result<IReadOnlyList<EmailFeatureVector>>.Success(
                new List<EmailFeatureVector> { MakeVector() }))
            .ReturnsAsync(Result<IReadOnlyList<EmailFeatureVector>>.Success(
                new List<EmailFeatureVector>()));

        triage.Setup(t => t.GetAiRecommendationAsync(It.IsAny<EmailFeatureVector>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result<ActionPrediction?>.Success(prediction));

        triage.Setup(t => t.ApplyDecisionAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string?>(), It.IsAny<bool>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result<TriageDecision>.Success(MakeDecision("Keep", isOverride: true)));

        // K = Keep (override — AI suggested Archive)
        var keys = new Queue<ConsoleKeyInfo>([
            new ConsoleKeyInfo('K', ConsoleKey.K, false, false, false),
        ]);

        var (svc, writer) = CreateService(triage, keys);
        await svc.RunAsync("me");

        // Verify: chosenAction="Keep" (override), aiRec="Archive"
        triage.Verify(t => t.ApplyDecisionAsync(
            It.IsAny<string>(),
            "Keep",            // user's choice (different from AI)
            "Archive",         // AI recommendation passed through for training signal
            It.IsAny<bool>(),
            It.IsAny<CancellationToken>()), Times.Once);
    }

    /// <summary>T044: Session summary shows override count when user overrides AI.</summary>
    [Fact]
    public async Task RunAsync_AiAssisted_Override_ShowsOverrideInSummary()
    {
        var prediction = new ActionPrediction { PredictedLabel = "Archive", Confidence = 0.70f };

        var triage = new Mock<IEmailTriageService>();
        triage.Setup(t => t.GetSessionInfoAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result<TriageSessionInfo>.Success(AiAssistedInfo()));

        triage.SetupSequence(t => t.GetNextBatchAsync(It.IsAny<int>(), 0, It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result<IReadOnlyList<EmailFeatureVector>>.Success(
                new List<EmailFeatureVector> { MakeVector() }))
            .ReturnsAsync(Result<IReadOnlyList<EmailFeatureVector>>.Success(
                new List<EmailFeatureVector>()));

        triage.Setup(t => t.GetAiRecommendationAsync(It.IsAny<EmailFeatureVector>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result<ActionPrediction?>.Success(prediction));

        triage.Setup(t => t.ApplyDecisionAsync(It.IsAny<string>(), "Keep", "Archive", It.IsAny<bool>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result<TriageDecision>.Success(MakeDecision("Keep", isOverride: true)));

        var keys = new Queue<ConsoleKeyInfo>([
            new ConsoleKeyInfo('K', ConsoleKey.K, false, false, false), // override
        ]);

        var (svc, writer) = CreateService(triage, keys);
        await svc.RunAsync("me");

        var output = writer.ToString();
        // Session summary should mention "AI overrides" when override count > 0
        Assert.Contains("override", output, StringComparison.OrdinalIgnoreCase);
    }

    // ── Helpers ───────────────────────────────────────────────────────────────

    private static int CountOccurrences(string source, string substring)
    {
        int count = 0;
        int index = 0;
        while ((index = source.IndexOf(substring, index, StringComparison.Ordinal)) >= 0)
        {
            count++;
            index += substring.Length;
        }

        return count;
    }

    // ── Re-triage phase tests ─────────────────────────────────────────────────

    /// <summary>When the oldest untriaged email exceeds 5 years, GetRetriageQueueAsync is called.</summary>
    [Fact]
    public async Task RunAsync_EntersRetriagePhase_WhenBatchExceedsOldEmailThreshold()
    {
        // A feature with EmailAgeDays > 1825 triggers the re-triage transition.
        var oldFeature = new EmailFeatureVector
        {
            EmailId = "old-email",
            SenderDomain = "example.com",
            SubjectText = "Old Email",
            EmailAgeDays = 1826, // just over the 5-year threshold
        };
        var retriageFeature = new EmailFeatureVector
        {
            EmailId = "retriage-email",
            SenderDomain = "example.com",
            SubjectText = "Re-triage Email",
            EmailAgeDays = 100,
            TrainingLabel = "Keep",
        };

        var triage = new Mock<IEmailTriageService>();
        triage.Setup(t => t.GetSessionInfoAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result<TriageSessionInfo>.Success(ColdStartInfo()));

        // Normal batch returns old email → triggers threshold detection
        triage.Setup(t => t.GetNextBatchAsync(It.IsAny<int>(), 0, It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result<IReadOnlyList<EmailFeatureVector>>.Success(
                new List<EmailFeatureVector> { oldFeature }));

        // Re-triage batch: first call returns retriage item, second call returns empty (done)
        triage.SetupSequence(t => t.GetRetriageQueueAsync(It.IsAny<int>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result<IReadOnlyList<EmailFeatureVector>>.Success(
                new List<EmailFeatureVector> { retriageFeature }))
            .ReturnsAsync(Result<IReadOnlyList<EmailFeatureVector>>.Success(
                new List<EmailFeatureVector>()));

        triage.Setup(t => t.ApplyDecisionAsync(
                It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string?>(),
                It.IsAny<bool>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result<TriageDecision>.Success(MakeDecision("Keep", false)));

        // Keys: one for the transition notice "press any key", one K to process the retriage item
        var keys = new Queue<ConsoleKeyInfo>([
            new ConsoleKeyInfo(' ', ConsoleKey.Spacebar, false, false, false), // dismiss notice
            new ConsoleKeyInfo('K', ConsoleKey.K, false, false, false),        // action on retriage item
        ]);

        var (svc, writer) = CreateService(triage, keys);
        await svc.RunAsync("me");

        // GetRetriageQueueAsync must have been called (at least twice: once for the
        // threshold re-fetch, once for the next empty loop iteration)
        triage.Verify(t => t.GetRetriageQueueAsync(It.IsAny<int>(), It.IsAny<CancellationToken>()),
            Times.AtLeastOnce);

        var output = writer.ToString();
        Assert.Contains("Re-Triage", output, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>When IsRetriagedPhase and queue is empty, the completion message is shown.</summary>
    [Fact]
    public async Task RunAsync_RetriageComplete_ShowsCompletionMessage()
    {
        var oldFeature = new EmailFeatureVector
        {
            EmailId = "old-complete",
            SenderDomain = "example.com",
            SubjectText = "Old",
            EmailAgeDays = 1826,
        };

        var triage = new Mock<IEmailTriageService>();
        triage.Setup(t => t.GetSessionInfoAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result<TriageSessionInfo>.Success(ColdStartInfo(labeled: 50)));

        triage.Setup(t => t.GetNextBatchAsync(It.IsAny<int>(), 0, It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result<IReadOnlyList<EmailFeatureVector>>.Success(
                new List<EmailFeatureVector> { oldFeature }));

        // Both re-triage fetches return empty immediately
        triage.Setup(t => t.GetRetriageQueueAsync(It.IsAny<int>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result<IReadOnlyList<EmailFeatureVector>>.Success(
                new List<EmailFeatureVector>()));

        // Key for dismissing transition notice
        var keys = new Queue<ConsoleKeyInfo>([
            new ConsoleKeyInfo(' ', ConsoleKey.Spacebar, false, false, false),
        ]);

        var (svc, writer) = CreateService(triage, keys);
        await svc.RunAsync("me");

        var output = writer.ToString();
        Assert.Contains("re-triage complete", output, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>Pressing Enter on a re-triage item (has TrainingLabel) confirms the previous label.</summary>
    [Fact]
    public async Task RunAsync_RetriagedItem_EnterKeyConfirmsPreviousLabel()
    {
        const string previousLabel = "Archive";
        var oldFeature = new EmailFeatureVector
        {
            EmailId = "old-for-enter-test",
            SenderDomain = "example.com",
            SubjectText = "Old",
            EmailAgeDays = 1826,
        };
        var retriageFeature = new EmailFeatureVector
        {
            EmailId = "retriage-enter",
            SenderDomain = "example.com",
            SubjectText = "Re-evaluated",
            EmailAgeDays = 90,
            TrainingLabel = previousLabel, // previously labeled "Archive"
        };

        var triage = new Mock<IEmailTriageService>();
        triage.Setup(t => t.GetSessionInfoAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result<TriageSessionInfo>.Success(ColdStartInfo(labeled: 50)));

        triage.Setup(t => t.GetNextBatchAsync(It.IsAny<int>(), 0, It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result<IReadOnlyList<EmailFeatureVector>>.Success(
                new List<EmailFeatureVector> { oldFeature }));

        triage.SetupSequence(t => t.GetRetriageQueueAsync(It.IsAny<int>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result<IReadOnlyList<EmailFeatureVector>>.Success(
                new List<EmailFeatureVector> { retriageFeature }))
            .ReturnsAsync(Result<IReadOnlyList<EmailFeatureVector>>.Success(
                new List<EmailFeatureVector>()));

        triage.Setup(t => t.ApplyDecisionAsync(
                "retriage-enter", previousLabel, previousLabel, true, It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result<TriageDecision>.Success(MakeDecision(previousLabel, false)));

        // Space = dismiss notice, Enter = confirm previous label
        var keys = new Queue<ConsoleKeyInfo>([
            new ConsoleKeyInfo(' ', ConsoleKey.Spacebar, false, false, false),
            new ConsoleKeyInfo('\r', ConsoleKey.Enter, false, false, false),
        ]);

        var (svc, writer) = CreateService(triage, keys);
        await svc.RunAsync("me");

        // Verify ApplyDecisionAsync was called with the previous label and forceUserCorrected=true
        triage.Verify(t => t.ApplyDecisionAsync(
            "retriage-enter", previousLabel, previousLabel, true, It.IsAny<CancellationToken>()),
            Times.Once);
    }
}
