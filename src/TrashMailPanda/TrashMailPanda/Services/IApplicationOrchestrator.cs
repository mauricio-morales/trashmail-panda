using TrashMailPanda.Models;
using TrashMailPanda.Shared.Base;

namespace TrashMailPanda.Services;

/// <summary>
/// Drives the full application workflow: startup → mode selection → triage/bulk ops/
/// settings/training → exit. Emits <see cref="ApplicationEvent"/> records through
/// the <see cref="ApplicationEventRaised"/> event for any UI layer to subscribe to.
///
/// This service is UI-agnostic with respect to its own code — it MUST NOT reference
/// Spectre.Console directly. However, it delegates to console rendering
/// services (e.g., <c>IEmailTriageConsoleService</c>) for the current TUI
/// implementation.
///
/// Key responsibility: extract the workflow loop currently in <c>Program.cs</c> into
/// a testable, injectable service with typed lifecycle events.
/// </summary>
public interface IApplicationOrchestrator
{
    /// <summary>
    /// Raised when any application lifecycle event occurs.
    /// UI layers subscribe to this event and pattern-match on the concrete
    /// <see cref="ApplicationEvent"/> subtype to render appropriately.
    ///
    /// Subscribers that throw are caught and logged — they do not block other
    /// subscribers or the orchestrator.
    /// </summary>
    event EventHandler<ApplicationEventArgs>? ApplicationEventRaised;

    /// <summary>
    /// Runs the full application workflow:
    /// <list type="number">
    ///   <item>Check if first-time setup is needed; emit <see cref="ProviderStatusChangedEvent"/> accordingly</item>
    ///   <item>Initialize providers via <c>ConsoleStartupOrchestrator</c></item>
    ///   <item>Run startup training data sync</item>
    ///   <item>Enter mode selection loop (emit <see cref="ModeSelectionRequestedEvent"/>)</item>
    ///   <item>Dispatch to the selected mode's console service</item>
    ///   <item>On exit: emit <see cref="ApplicationWorkflowCompletedEvent"/> with exit code</item>
    /// </list>
    ///
    /// Returns the process exit code (0 = success).
    ///
    /// Calling this method while already running returns
    /// <c>Result.Failure</c> with an <c>InvalidOperationError</c>.
    /// </summary>
    /// <param name="cancellationToken">
    /// Cancellation token wired to Ctrl+C. When cancelled, the orchestrator
    /// performs graceful shutdown and emits <see cref="ApplicationWorkflowCompletedEvent"/>.
    /// </param>
    /// <returns>Result containing the exit code.</returns>
    Task<Result<int>> RunAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Returns whether the orchestrator is currently running a workflow.
    /// </summary>
    bool IsRunning { get; }
}
