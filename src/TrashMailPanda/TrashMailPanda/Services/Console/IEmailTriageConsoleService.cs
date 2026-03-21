using System.Threading;
using System.Threading.Tasks;
using TrashMailPanda.Models.Console;
using TrashMailPanda.Shared.Base;

namespace TrashMailPanda.Services.Console;

/// <summary>
/// Thin TUI presenter for the Email Triage workflow.
/// Renders email cards, captures keypresses, and delegates all business logic
/// to <see cref="IEmailTriageService"/>.
/// </summary>
public interface IEmailTriageConsoleService
{
    /// <summary>
    /// Runs a triage session until the user exits or the batch is exhausted.
    /// Returns a summary of the session.
    /// </summary>
    Task<Result<TriageSessionSummary>> RunAsync(
        string accountId,
        CancellationToken cancellationToken = default);
}
