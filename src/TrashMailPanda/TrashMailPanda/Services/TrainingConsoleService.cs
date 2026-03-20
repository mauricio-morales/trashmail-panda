using Microsoft.Extensions.Logging;
using Spectre.Console;
using TrashMailPanda.Models.Console;
using TrashMailPanda.Providers.ML;
using TrashMailPanda.Providers.ML.Models;
using TrashMailPanda.Services.Console;

namespace TrashMailPanda.Services;

/// <summary>
/// Console-facing service that drives the ML model training workflow.
/// Renders live Spectre.Console progress, a formatted metrics table, and
/// quality advisories for human-readable feedback during training.
/// </summary>
public sealed class TrainingConsoleService
{
    private readonly IModelTrainingPipeline _pipeline;
    private readonly ILogger<TrainingConsoleService> _logger;
    private readonly IAnsiConsole _console;
    private readonly Func<ConsoleKeyInfo> _readKey;
    private readonly IConsoleHelpPanel? _helpPanel;

    public TrainingConsoleService(
        IModelTrainingPipeline pipeline,
        ILogger<TrainingConsoleService> logger,
        IAnsiConsole? console = null,
        Func<ConsoleKeyInfo>? readKey = null,
        IConsoleHelpPanel? helpPanel = null)
    {
        _pipeline = pipeline;
        _logger = logger;
        _console = console ?? AnsiConsole.Console;
        _readKey = readKey ?? (() => System.Console.ReadKey(intercept: true));
        _helpPanel = helpPanel;
    }

    // ──────────────────────────────────────────────────────────────────────────
    // T045/T048 — RunTrainingAsync (with save confirmation)
    // ──────────────────────────────────────────────────────────────────────────

    /// <summary>
    /// Runs a full training cycle with live Spectre.Console progress display.
    /// Prompts the user for confirmation before starting (T048).
    /// </summary>
    public async Task RunTrainingAsync(
        string triggerReason = "manual",
        CancellationToken cancellationToken = default)
    {
        // T048 — Pre-training save confirmation
        _console.WriteLine();
        _console.MarkupLine(
            $"  {ConsoleColors.Warning}⚠ This will train and activate a new AI model version.{ConsoleColors.Close}");
        _console.MarkupLine(
            $"  {ConsoleColors.PromptOption}Proceed?{ConsoleColors.Close}  " +
            $"{ConsoleColors.ActionHint}Y{ConsoleColors.Close}/Enter=train  " +
            $"{ConsoleColors.ActionHint}N{ConsoleColors.Close}/Esc=cancel");

        while (true)
        {
            var keyInfo = _readKey();
            var k = char.ToUpperInvariant(keyInfo.KeyChar);
            if (k == 'Y' || keyInfo.Key == ConsoleKey.Enter) break;
            if (k == 'N' || keyInfo.Key == ConsoleKey.Escape)
            {
                _console.MarkupLine(
                    $"  {ConsoleColors.Dim}Training cancelled.{ConsoleColors.Close}");
                return;
            }
            if (k == '?')
            {
                if (_helpPanel != null)
                    await _helpPanel.ShowAsync(HelpContext.ForTraining(), cancellationToken);
            }
        }

        _console.MarkupLine(
            $"  {ConsoleColors.Highlight}→{ConsoleColors.Close} Starting model training…");

        TrainingMetricsReport? report = null;

        await _console.Progress()
            .AutoRefresh(true)
            .HideCompleted(false)
            .Columns(
                new TaskDescriptionColumn(),
                new ProgressBarColumn(),
                new PercentageColumn(),
                new SpinnerColumn())
            .StartAsync(async ctx =>
            {
                // T045 — Phase tasks with semantic color labels
                var loadingTask = ctx.AddTask(
                    PhaseLabel(TrainingProgress.PhaseLoading, isActive: true), maxValue: 20);
                var pipelineTask = ctx.AddTask(
                    PhaseLabel(TrainingProgress.PhaseBuildingPipeline, isActive: false), maxValue: 10);
                var trainingTask = ctx.AddTask(
                    PhaseLabel(TrainingProgress.PhaseTraining, isActive: false), maxValue: 50);
                var evaluatingTask = ctx.AddTask(
                    PhaseLabel(TrainingProgress.PhaseEvaluating, isActive: false), maxValue: 20);

                var progress = new Progress<TrainingProgress>(p =>
                {
                    switch (p.Phase)
                    {
                        case TrainingProgress.PhaseLoading:
                            loadingTask.Description = PhaseLabel(TrainingProgress.PhaseLoading, isActive: true);
                            loadingTask.Value = p.PercentComplete;
                            break;
                        case TrainingProgress.PhaseBuildingPipeline:
                            loadingTask.Description = PhaseLabel(TrainingProgress.PhaseLoading, isComplete: true);
                            loadingTask.Value = 20;
                            pipelineTask.Description = PhaseLabel(TrainingProgress.PhaseBuildingPipeline, isActive: true);
                            pipelineTask.Value = p.PercentComplete - 20;
                            break;
                        case TrainingProgress.PhaseTraining:
                            loadingTask.Description = PhaseLabel(TrainingProgress.PhaseLoading, isComplete: true);
                            loadingTask.Value = 20;
                            pipelineTask.Description = PhaseLabel(TrainingProgress.PhaseBuildingPipeline, isComplete: true);
                            pipelineTask.Value = 10;
                            trainingTask.Description = PhaseLabel(TrainingProgress.PhaseTraining, isActive: true);
                            trainingTask.Value = p.PercentComplete - 30;
                            break;
                        case TrainingProgress.PhaseEvaluating:
                            loadingTask.Description = PhaseLabel(TrainingProgress.PhaseLoading, isComplete: true);
                            loadingTask.Value = 20;
                            pipelineTask.Description = PhaseLabel(TrainingProgress.PhaseBuildingPipeline, isComplete: true);
                            pipelineTask.Value = 10;
                            trainingTask.Description = PhaseLabel(TrainingProgress.PhaseTraining, isComplete: true);
                            trainingTask.Value = 50;
                            evaluatingTask.Description = PhaseLabel(TrainingProgress.PhaseEvaluating, isActive: true);
                            evaluatingTask.Value = p.PercentComplete - 80;
                            break;
                        default:
                            // "Completed" phase — mark everything done
                            loadingTask.Description = PhaseLabel(TrainingProgress.PhaseLoading, isComplete: true);
                            loadingTask.Value = 20;
                            pipelineTask.Description = PhaseLabel(TrainingProgress.PhaseBuildingPipeline, isComplete: true);
                            pipelineTask.Value = 10;
                            trainingTask.Description = PhaseLabel(TrainingProgress.PhaseTraining, isComplete: true);
                            trainingTask.Value = 50;
                            evaluatingTask.Description = PhaseLabel(TrainingProgress.PhaseEvaluating, isComplete: true);
                            evaluatingTask.Value = 20;
                            break;
                    }
                });

                var request = new TrainingRequest
                {
                    TriggerReason = triggerReason,
                    ForceRetrain = false,
                };

                var result = await _pipeline.TrainActionModelAsync(request, progress, cancellationToken);

                if (result.IsSuccess)
                {
                    report = result.Value;
                }
                else
                {
                    _console.MarkupLine(
                        $"{ConsoleColors.Error}✗ Training failed:{ConsoleColors.Close} " +
                        $"{ConsoleColors.ErrorText}{Markup.Escape(result.Error.Message)}{ConsoleColors.Close}");
                    _logger.LogError("Training failed: {Error}", result.Error.Message);
                }
            });

        if (report is not null)
        {
            _console.MarkupLine(
                $"{ConsoleColors.Success}✓ Training complete!{ConsoleColors.Close}");
            RenderMetricsReport(report);
        }
    }

    // ──────────────────────────────────────────────────────────────────────────
    // T046 / T047 — RenderMetricsReport (quality advisory + table)
    // ──────────────────────────────────────────────────────────────────────────

    /// <summary>
    /// Renders a formatted Spectre.Console table for the training metrics report.
    /// Emits a quality advisory when overall MacroF1 is below threshold (T047).
    /// Per-class value cells use ConsoleColors.Metric (T046).
    /// </summary>
    public void RenderMetricsReport(TrainingMetricsReport report)
    {
        // T047 — Quality advisory
        if (report.IsQualityAdvisory)
        {
            _console.MarkupLine(
                $"{ConsoleColors.Warning}⚠ Quality advisory: overall F1 = {report.MacroF1:F2} " +
                $"(below 0.70 threshold). Consider collecting more labeled data.{ConsoleColors.Close}");
        }
        else
        {
            _console.MarkupLine(
                $"{ConsoleColors.Success}✓ Model quality: overall F1 = {report.MacroF1:F2}{ConsoleColors.Close}");
        }

        // T046 — Per-class metrics table with ConsoleColors.Metric values
        var table = new Table()
            .Border(TableBorder.Rounded)
            .AddColumn("Class")
            .AddColumn(new TableColumn("Precision").RightAligned())
            .AddColumn(new TableColumn("Recall").RightAligned())
            .AddColumn(new TableColumn("F1").RightAligned());

        // Per-class rows
        foreach (var (className, metrics) in report.PerClassMetrics)
        {
            var f1 = metrics.Precision + metrics.Recall > 0
                ? 2 * metrics.Precision * metrics.Recall / (metrics.Precision + metrics.Recall)
                : 0f;
            table.AddRow(
                className,
                $"{ConsoleColors.Metric}{metrics.Precision:F3}{ConsoleColors.Close}",
                $"{ConsoleColors.Metric}{metrics.Recall:F3}{ConsoleColors.Close}",
                $"{ConsoleColors.Metric}{f1:F3}{ConsoleColors.Close}");
        }

        // Macro averages footer
        table.AddRow(
            "[bold]Macro avg[/]",
            $"[bold]{ConsoleColors.Metric}{report.MacroPrecision:F3}{ConsoleColors.Close}[/]",
            $"[bold]{ConsoleColors.Metric}{report.MacroRecall:F3}{ConsoleColors.Close}[/]",
            $"[bold]{ConsoleColors.Metric}{report.MacroF1:F3}{ConsoleColors.Close}[/]");

        table.Title = new TableTitle(
            $"Training Metrics — {Markup.Escape(report.Algorithm)} — {report.TrainingDataCount} samples");

        _console.Write(table);

        _console.MarkupLine(
            $"{ConsoleColors.Info}ℹ{ConsoleColors.Close} " +
            $"Model ID: [bold]{Markup.Escape(report.ModelId)}[/]  " +
            $"Duration: {report.TrainingDuration.TotalSeconds:F1}s");
    }

    // ──────────────────────────────────────────────────────────────────────────
    // T045 — Phase label helper (active=cyan, complete=green)
    // ──────────────────────────────────────────────────────────────────────────

    private static string PhaseLabel(
        string phase,
        bool isActive = false,
        bool isComplete = false)
    {
        if (isComplete)
            return $"{ConsoleColors.Success}✓ {Markup.Escape(phase)}{ConsoleColors.Close}";
        if (isActive)
            return $"{ConsoleColors.Highlight}{Markup.Escape(phase)}{ConsoleColors.Close}";
        return $"{ConsoleColors.Dim}{Markup.Escape(phase)}{ConsoleColors.Close}";
    }
}
