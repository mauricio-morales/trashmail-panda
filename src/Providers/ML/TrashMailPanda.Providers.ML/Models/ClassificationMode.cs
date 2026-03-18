namespace TrashMailPanda.Providers.ML.Models;

/// <summary>
/// Indicates how email actions are being classified.
/// </summary>
public enum ClassificationMode
{
    /// <summary>No model trained yet — rule-based fallback.</summary>
    ColdStart,
    /// <summary>Model loaded but supplemented with rules.</summary>
    Hybrid,
    /// <summary>Fully trained ML model is authoritative.</summary>
    MlPrimary,
}
