namespace TrashMailPanda.Models.Console;

/// <summary>
/// Triage operating mode: whether an AI-trained model is available.
/// </summary>
public enum TriageMode
{
    /// <summary>No trained model available; user manually labels emails to build training data.</summary>
    ColdStart,

    /// <summary>A trained model is available; the UI presents AI recommendations with confidence scores.</summary>
    AiAssisted,
}
