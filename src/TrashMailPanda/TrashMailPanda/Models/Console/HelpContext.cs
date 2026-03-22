namespace TrashMailPanda.Models.Console;

/// <summary>
/// Bundles the mode title, optional description, and key binding list for rendering
/// by <c>ConsoleHelpPanel</c>. Contains static factory methods for each mode.
/// </summary>
public sealed class HelpContext
{
    public required string ModeTitle { get; init; }
    public string? Description { get; init; }
    public required IReadOnlyList<KeyBinding> KeyBindings { get; init; }

    public static HelpContext ForEmailTriage(TriageMode mode) => mode == TriageMode.ColdStart
        ? new HelpContext
        {
            ModeTitle = "Email Triage — Cold Start Labeling",
            Description = "Label emails to build training data. No AI suggestions yet.",
            KeyBindings =
              [
                  new("K", "Keep — leave in inbox"),
                  new("A", "Archive — move out of inbox"),
                  new("D", "Delete — move to trash"),
                  new("S", "Spam — report as spam"),
                  new("1", "Archive now, auto-delete after 30 days"),
                  new("2", "Archive now, auto-delete after 1 year"),
                  new("3", "Archive now, auto-delete after 5 years"),
                  new("Q / Esc", "Return to main menu"),
                  new("?", "Show this help panel"),
              ],
        }
        : new HelpContext
        {
            ModeTitle = "Email Triage — AI-Assisted",
            Description = "Review AI recommendations. Accept or override per email.",
            KeyBindings =
              [
                  new("Enter / Y", "Accept AI recommendation"),
                  new("K", "Keep — override with Keep"),
                  new("A", "Archive — override with Archive"),
                  new("D", "Delete — override with Delete"),
                  new("S", "Spam — override with Spam"),
                  new("1", "Archive now, auto-delete after 30 days"),
                  new("2", "Archive now, auto-delete after 1 year"),
                  new("3", "Archive now, auto-delete after 5 years"),
                  new("Q / Esc", "Return to main menu"),
                  new("?", "Show this help panel"),
              ],
        };

    public static HelpContext ForMainMenu() => new()
    {
        ModeTitle = "Main Menu",
        Description = "TrashMail Panda — AI-powered Gmail triage console.",
        KeyBindings =
        [
            new("↑ / ↓", "Navigate menu"),
            new("Enter", "Select mode"),
            new("Q / Esc", "Exit application"),
            new("?", "Show this help panel"),
        ],
    };

    public static HelpContext ForBulkOperations() => new()
    {
        ModeTitle = "Bulk Operations",
        KeyBindings =
        [
            new("↑ / ↓", "Navigate options"),
            new("Enter", "Select / confirm"),
            new("Esc", "Back / cancel"),
            new("?", "Show this help panel"),
        ],
    };

    public static HelpContext ForProviderSettings() => new()
    {
        ModeTitle = "Provider Settings",
        KeyBindings =
        [
            new("↑ / ↓", "Navigate options"),
            new("Enter", "Select"),
            new("Esc", "Back to main menu"),
            new("?", "Show this help panel"),
        ],
    };

    public static HelpContext ForTraining() => new()
    {
        ModeTitle = "Training Mode",
        Description = "Train the ML classification model on your labeled emails.",
        KeyBindings =
        [
            new("Y / Enter", "Confirm save model"),
            new("N / Esc", "Cancel / discard model"),
            new("?", "Show this help panel"),
        ],
    };
}
