namespace TrashMailPanda.Models.Console;

/// <summary>
/// Severity level of a model quality warning surfaced to the user.
/// </summary>
public enum QualityWarningSeverity
{
    /// <summary>Informational: corrections accumulating — retrain recommended.</summary>
    Info,

    /// <summary>Warning: rolling accuracy &lt;70% — model degradation alert.</summary>
    Warning,

    /// <summary>Critical: rolling accuracy &lt;50% — auto-apply will be disabled.</summary>
    Critical,
}
