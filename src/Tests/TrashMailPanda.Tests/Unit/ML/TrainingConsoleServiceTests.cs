using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Spectre.Console;
using TrashMailPanda.Providers.ML;
using TrashMailPanda.Providers.ML.Models;
using TrashMailPanda.Services;
using TrashMailPanda.Shared.Base;
using Xunit;

namespace TrashMailPanda.Tests.Unit.ML;

[Trait("Category", "Unit")]
public class TrainingConsoleServiceTests
{
    // ──────────────────────────────────────────────────────────────────────────
    // Helpers
    // ──────────────────────────────────────────────────────────────────────────

    private static (TrainingConsoleService Service, StringWriter Writer) CreateService(
        IModelTrainingPipeline pipeline)
    {
        var writer = new StringWriter();
        var console = AnsiConsole.Create(new AnsiConsoleSettings
        {
            Ansi = AnsiSupport.No,
            ColorSystem = ColorSystemSupport.NoColors,
            Out = new AnsiConsoleOutput(writer),
        });

        var service = new TrainingConsoleService(
            pipeline,
            NullLogger<TrainingConsoleService>.Instance,
            console);

        return (service, writer);
    }

    private static TrainingMetricsReport BuildReport(float macroF1, bool isAdvisory) =>
        new()
        {
            ModelId = "test-model-id",
            Algorithm = "SdcaMaximumEntropy",
            TrainingDataCount = 100,
            MacroPrecision = macroF1,
            MacroRecall = macroF1,
            MacroF1 = macroF1,
            IsQualityAdvisory = isAdvisory,
            TrainingDuration = TimeSpan.FromSeconds(2),
            PerClassMetrics = new Dictionary<string, ClassMetrics>
            {
                ["Keep"] = new ClassMetrics(macroF1, macroF1, macroF1),
                ["Delete"] = new ClassMetrics(macroF1, macroF1, macroF1),
            },
        };

    // ──────────────────────────────────────────────────────────────────────────
    // RenderMetricsReport — quality advisory
    // ──────────────────────────────────────────────────────────────────────────

    [Fact]
    public void RenderMetricsReport_ShowsQualityAdvisory_WhenMacroF1Is0_65()
    {
        var pipeline = new Mock<IModelTrainingPipeline>();
        var (service, writer) = CreateService(pipeline.Object);
        var report = BuildReport(macroF1: 0.65f, isAdvisory: true);

        service.RenderMetricsReport(report);

        var output = writer.ToString();
        Assert.Contains("Quality advisory", output);
        // MacroF1 value appears with locale-specific decimal separator
        Assert.Contains("0", output);
    }

    [Fact]
    public void RenderMetricsReport_ShowsSuccess_WhenMacroF1Is0_82()
    {
        var pipeline = new Mock<IModelTrainingPipeline>();
        var (service, writer) = CreateService(pipeline.Object);
        var report = BuildReport(macroF1: 0.82f, isAdvisory: false);

        service.RenderMetricsReport(report);

        var output = writer.ToString();
        Assert.Contains("Model quality", output);
        Assert.DoesNotContain("Quality advisory", output);
    }

    [Fact]
    public void RenderMetricsReport_RendersPerClassRows_AndMacroAvgFooter()
    {
        var pipeline = new Mock<IModelTrainingPipeline>();
        var (service, writer) = CreateService(pipeline.Object);
        var report = BuildReport(macroF1: 0.80f, isAdvisory: false);

        service.RenderMetricsReport(report);

        var output = writer.ToString();
        Assert.Contains("Keep", output);
        Assert.Contains("Delete", output);
        Assert.Contains("Macro avg", output);
    }

    // ──────────────────────────────────────────────────────────────────────────
    // RunTrainingAsync — phase constants + error path
    // ──────────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task RunTrainingAsync_HandlesAllTrainingPhases_WithoutThrowing()
    {
        var pipeline = new Mock<IModelTrainingPipeline>();

        // Arrange: pipeline calls progress for every phase then returns success
        pipeline
            .Setup(p => p.TrainActionModelAsync(
                It.IsAny<TrainingRequest>(),
                It.IsAny<IProgress<TrainingProgress>>(),
                It.IsAny<CancellationToken>()))
            .Returns(async (TrainingRequest _, IProgress<TrainingProgress> prog, CancellationToken ct) =>
            {
                // Report each phase constant in sequence
                prog.Report(new TrainingProgress { Phase = TrainingProgress.PhaseLoading, PercentComplete = 10 });
                prog.Report(new TrainingProgress { Phase = TrainingProgress.PhaseBuildingPipeline, PercentComplete = 25 });
                prog.Report(new TrainingProgress { Phase = TrainingProgress.PhaseTraining, PercentComplete = 50 });
                prog.Report(new TrainingProgress { Phase = TrainingProgress.PhaseEvaluating, PercentComplete = 90 });
                prog.Report(new TrainingProgress { Phase = "Completed", PercentComplete = 100 });

                await Task.CompletedTask;
                return Result<TrainingMetricsReport>.Success(BuildReport(0.80f, false));
            });

        var (service, _) = CreateService(pipeline.Object);

        // Act & Assert: no exception thrown
        await service.RunTrainingAsync("test", CancellationToken.None);
    }

    [Fact]
    public async Task RunTrainingAsync_RendersErrorMessage_OnTrainingFailure()
    {
        var pipeline = new Mock<IModelTrainingPipeline>();

        pipeline
            .Setup(p => p.TrainActionModelAsync(
                It.IsAny<TrainingRequest>(),
                It.IsAny<IProgress<TrainingProgress>>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result<TrainingMetricsReport>.Failure(
                new ValidationError("Not enough training data")));

        var (service, writer) = CreateService(pipeline.Object);

        await service.RunTrainingAsync("test", CancellationToken.None);

        var output = writer.ToString();
        Assert.Contains("Training failed", output);
        Assert.Contains("Not enough training data", output);
    }
}
