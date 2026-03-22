using Microsoft.Extensions.Logging;
using Spectre.Console;
using TrashMailPanda.Models;

namespace TrashMailPanda.Services.Console;

/// <summary>
/// Subscribes to <see cref="IApplicationOrchestrator.ApplicationEventRaised"/>
/// and pattern-matches on event types to render them via Spectre.Console.
///
/// This is the only Spectre.Console consumer in the event pipeline — the
/// orchestrator itself has zero rendering references.
/// </summary>
public sealed class ConsoleEventRenderer
{
    private readonly ILogger<ConsoleEventRenderer> _logger;

    public ConsoleEventRenderer(ILogger<ConsoleEventRenderer> logger)
    {
        _logger = logger;
    }

    /// <summary>
    /// Subscribes this renderer to the orchestrator's event stream.
    /// Call before <see cref="IApplicationOrchestrator.RunAsync"/>.
    /// </summary>
    public void Subscribe(IApplicationOrchestrator orchestrator)
    {
        orchestrator.ApplicationEventRaised += OnApplicationEventRaised;
    }

    // ─────────────────────────────────────────────────────────────────────────
    // Event handler
    // ─────────────────────────────────────────────────────────────────────────

    private void OnApplicationEventRaised(object? sender, ApplicationEventArgs args)
    {
        try
        {
            switch (args.Event)
            {
                case ModeSelectionRequestedEvent modeEvent:
                    RenderModeSelectionRequested(modeEvent);
                    break;

                case BatchProgressEvent progressEvent:
                    RenderBatchProgress(progressEvent);
                    break;

                case ProviderStatusChangedEvent statusEvent:
                    RenderProviderStatusChanged(statusEvent);
                    break;

                case ApplicationWorkflowCompletedEvent completedEvent:
                    RenderWorkflowCompleted(completedEvent);
                    break;

                case StatusMessageEvent msgEvent:
                    RenderStatusMessage(msgEvent);
                    break;

                case StartupScanRequiredEvent scanEvent:
                    RenderStartupScanRequired(scanEvent);
                    break;

                default:
                    _logger.LogDebug(
                        "ConsoleEventRenderer: unhandled event type {EventType}",
                        args.Event.EventType);
                    break;
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex,
                "ConsoleEventRenderer threw rendering {EventType}", args.Event.EventType);
        }
    }

    // ─────────────────────────────────────────────────────────────────────────
    // Renderers per event type
    // ─────────────────────────────────────────────────────────────────────────

    private void RenderModeSelectionRequested(ModeSelectionRequestedEvent @event)
    {
        _logger.LogDebug(
            "Mode selection: {Count} mode(s) available",
            @event.AvailableModes.Count);
    }

    private static void RenderBatchProgress(BatchProgressEvent @event)
    {
        var pct = @event.TotalCount > 0
            ? (int)(100.0 * @event.ProcessedCount / @event.TotalCount)
            : 0;

        var eta = @event.EstimatedSecondsRemaining.HasValue
            ? $" (~{@event.EstimatedSecondsRemaining.Value:F0}s remaining)"
            : string.Empty;

        AnsiConsole.MarkupLine(
            $"[cyan]→[/] Processed [cyan]{@event.ProcessedCount}[/]/[cyan]{@event.TotalCount}[/] ({pct}%){eta}");
    }

    private static void RenderProviderStatusChanged(ProviderStatusChangedEvent @event)
    {
        if (@event.IsHealthy)
        {
            AnsiConsole.MarkupLine(
                $"[green]✓[/] {Markup.Escape(@event.ProviderName)}: [green]{Markup.Escape(@event.StatusMessage ?? "healthy")}[/]");
        }
        else
        {
            AnsiConsole.MarkupLine(
                $"[yellow]⚠[/] {Markup.Escape(@event.ProviderName)}: [yellow]{Markup.Escape(@event.StatusMessage ?? "unhealthy")}[/]");
        }
    }

    private static void RenderWorkflowCompleted(ApplicationWorkflowCompletedEvent @event)
    {
        if (@event.ExitCode == 0)
        {
            AnsiConsole.MarkupLine($"[dim]Workflow complete: {Markup.Escape(@event.Reason)}[/]");
        }
        else
        {
            AnsiConsole.MarkupLine(
                $"[yellow]⚠[/] Workflow ended with exit code {@event.ExitCode}: {Markup.Escape(@event.Reason)}");
        }
    }

    private static void RenderStatusMessage(StatusMessageEvent @event)
    {
        AnsiConsole.MarkupLine(@event.Message);
    }

    private static void RenderStartupScanRequired(StartupScanRequiredEvent @event)
    {
        if (@event.IsResume)
        {
            AnsiConsole.Write(
                new Panel(
                    new Markup(
                        "[yellow]A previous scan was interrupted[/] — resuming from last checkpoint.\n" +
                        "[dim]The initial scan must complete before Email Triage is available.[/]"))
                    .Header("[bold yellow]⚠  Resuming Incomplete Scan[/]")
                    .BorderColor(Color.Yellow)
                    .Padding(1, 0));
        }
        else
        {
            AnsiConsole.Write(
                new Panel(
                    new Markup(
                        "[yellow]No training data found[/] — running initial Gmail scan.\n" +
                        "[dim]This scans Spam → Trash → Sent → Archive → Inbox to build your dataset.\n" +
                        "It runs once and takes 1–5 minutes depending on mailbox size.[/]"))
                    .Header("[bold yellow]⚠  Initial Scan Required[/]")
                    .BorderColor(Color.Yellow)
                    .Padding(1, 0));
        }
    }
}
