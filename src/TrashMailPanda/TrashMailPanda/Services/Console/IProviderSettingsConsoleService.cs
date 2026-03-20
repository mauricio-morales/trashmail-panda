using System.Threading;
using System.Threading.Tasks;
using TrashMailPanda.Shared.Base;

namespace TrashMailPanda.Services.Console;

/// <summary>
/// Thin TUI presenter for the Provider Settings workflow.
/// Renders settings menu, delegates Gmail re-auth to ConfigurationWizard,
/// and manages storage limit adjustments.
/// </summary>
public interface IProviderSettingsConsoleService
{
    /// <summary>
    /// Runs the provider settings UI flow until the user exits.
    /// </summary>
    Task<Result<bool>> RunAsync(CancellationToken cancellationToken = default);
}
