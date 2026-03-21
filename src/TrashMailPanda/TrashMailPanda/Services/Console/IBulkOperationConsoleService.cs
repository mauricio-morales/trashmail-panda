using System.Threading;
using System.Threading.Tasks;
using TrashMailPanda.Shared.Base;

namespace TrashMailPanda.Services.Console;

/// <summary>
/// Thin TUI presenter for the Bulk Operations workflow.
/// Renders criteria prompts, preview lists, and confirmation before execution.
/// Delegates all business logic to <see cref="IBulkOperationService"/>.
/// </summary>
public interface IBulkOperationConsoleService
{
    /// <summary>
    /// Runs the bulk operations UI flow: criteria wizard → preview → confirm → execute.
    /// </summary>
    Task<Result<bool>> RunAsync(CancellationToken cancellationToken = default);
}
