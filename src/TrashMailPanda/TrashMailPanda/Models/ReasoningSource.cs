namespace TrashMailPanda.Models;

/// <summary>
/// Indicates whether a classification was produced by the trained ML model
/// or by the rule-based cold-start fallback.
/// </summary>
public enum ReasoningSource
{
    /// <summary>Classification produced by a trained ML.NET model.</summary>
    ML,

    /// <summary>Classification produced by rule-based fallback (cold-start or model unavailable).</summary>
    RuleBased,
}
