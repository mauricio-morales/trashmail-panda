using System.Threading;
using System.Threading.Tasks;
using TrashMailPanda.Models.Console;
using TrashMailPanda.Shared.Base;

namespace TrashMailPanda.Services.Console;

/// <summary>
/// Context-aware help panel accessible via <c>?</c>/<c>F1</c> from any mode.
/// Renders mode title, optional description, and key bindings as a Spectre.Console Panel.
/// </summary>
public interface IConsoleHelpPanel
{
    /// <summary>
    /// Renders the help panel for the given context and blocks until dismissed
    /// (<c>?</c>, <c>F1</c>, or <c>Esc</c> pressed).
    /// </summary>
    Task ShowAsync(HelpContext context, CancellationToken cancellationToken = default);
}
