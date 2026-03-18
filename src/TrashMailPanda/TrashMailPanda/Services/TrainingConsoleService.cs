using Microsoft.Extensions.Logging;
using Spectre.Console;
using TrashMailPanda.Providers.ML;
using TrashMailPanda.Providers.ML.Models;

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

    public TrainingConsoleService(
        IModelTrainingPipeline pipeline,
        ILogger<TrainingConsoleService> logger,
        IAnsiConsole? console = null)
    {
        _pipeline = pipeline;
        _logger = logger;
        _console = console ?? AnsiConsole.Console;
    }

    // ──────────────────────────────────────────────────────────────────────────
    // T020 — RunTrainingAsync
    // ──────────────────────────────────────────────────────────────────────────

    /// <summary>
    /// Runs a full training cycle with live Spectre.Console progress display.
    /// </summary>
    public async Task RunTrainingAsync(
        string triggerReason = "manual",
        CancellationToken cancellationToken = default)
    {
        _console.MarkupLine("[cyan]→[/] Starting model training…");

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
                var loadingTask = ctx.AddTask(
                    $"[cyan]{TrainingProgress.PhaseLoading}[/]", maxValue: 20);
                var pipelineTask = ctx.AddTask(
                    $"[cyan]{TrainingProgress.PhaseBuildingPipeline}[/]", maxValue: 10);
                var trainingTask = ctx.AddTask(
                    $"[cyan]{TrainingProgress.PhaseTraining}[/]", maxValue: 50);
                var evaluatingTask = ctx.AddTask(
                    $"[cyan]{TrainingProgress.PhaseEvaluating}[/]", maxValue: 20);

                var progress = new Progress<TrainingProgress>(p =>
                {
                    switch (p.Phase)
                    {
                        case TrainingProgress.PhaseLoading:
                            loadingTask.Value = p.PercentComplete;
                            break;
                        case TrainingProgress.PhaseBuildingPipeline:
                            loadingTask.Value = 20;
                            pipelineTask.Value = p.PercentComplete - 20;
                            break;
                        case TrainingProgress.PhaseTraining:
                            loadingTask.Value = 20;
                            pipelineTask.Value = 10;
                            trainingTask.Value = p.PercentComplete - 30;
                            break;
                        case TrainingProgress.PhaseEvaluating:
                            loadingTask.Value = 20;
                            pipelineTask.Value = 10;
                            trainingTask.Value = 50;
                            evaluatingTask.Value = p.PercentComplete - 80;
                            break;
                        default:
                            // "Completed" phase — mark everything done
                            loadingTask.Value = 20;
                            pipelineTask.Value = 10;
                            trainingTask.Value = 50;
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
                        $"[bold red]✗ Training failed:[/] [red]{Markup.Escape(result.Error.Message)}[/]");
                    _logger.LogError("Training failed: {Error}", result.Error.Message);
                }
            });

        if (report is not null)
        {
            _console.MarkupLine("[green]✓ Training complete![/]");
            RenderMetricsReport(report);
        }
    }

    // ──────────────────────────────────────────────────────────────────────────
    // T021 — RenderMetricsReport (quality advisory + table)
    // ──────────────────────────────────────────────────────────────────────────

    /// <summary>
    /// Renders a formatted Spectre.Console table for the training metrics report.
    /// Emits a quality advisory when overall MacroF1 is below threshold.
    /// </summary>
    public void RenderMetricsReport(TrainingMetricsReport report)
    {
        if (report.IsQualityAdvisory)
        {
            _console.MarkupLine(
                $"[yellow]⚠ Quality advisory: overall F1 = {report.MacroF1:F2} " +
                $"(below 0.70 threshold). Consider collecting more labeled data.[/]");
        }
        else
        {
            _console.MarkupLine(
                $"[green]✓ Model quality: overall F1 = {report.MacroF1:F2}[/]");
        }

        var table = new Table();
        table.AddColumn("Class");
        table.AddColumn("Precision");
        table.AddColumn("Recall");
        table.AddColumn("F1");

        // Per-class rows
        foreach (var (className, metrics) in report.PerClassMetrics)
        {
            var f1 = metrics.Precision + metrics.Recall > 0
                ? 2 * metrics.Precision * metrics.Recall / (metrics.Precision + metrics.Recall)
                : 0f;
            table.AddRow(
                className,
                $"{metrics.Precision:F3}",
                $"{metrics.Recall:F3}",
                $"{f1:F3}");
        }

        // Macro averages footer
        table.AddRow(
            "[bold]Macro avg[/]",
            $"[bold]{report.MacroPrecision:F3}[/]",
            $"[bold]{report.MacroRecall:F3}[/]",
            $"[bold]{report.MacroF1:F3}[/]");

        table.Title = new TableTitle(
            $"Training Metrics — {report.Algorithm} — {report.TrainingDataCount} samples");

        _console.Write(table);

        _console.MarkupLine(
            $"[blue]ℹ[/] Model ID: [bold]{Markup.Escape(report.ModelId)}[/]  " +
            $"Duration: {report.TrainingDuration.TotalSeconds:F1}s");
    }
}
