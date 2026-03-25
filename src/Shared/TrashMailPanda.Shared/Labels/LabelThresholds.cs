using System.Collections.Generic;

namespace TrashMailPanda.Shared.Labels;

/// <summary>
/// Authoritative mapping of time-bounded triage labels to their retention thresholds.
/// Shared by <c>EmailTriageService</c> and <c>RetentionEnforcementService</c> — no local copies elsewhere.
/// </summary>
public static class LabelThresholds
{
    public const string Archive30d = "Archive for 30d";
    public const string Archive1y = "Archive for 1y";
    public const string Archive5y = "Archive for 5y";

    private static readonly IReadOnlyDictionary<string, int> _thresholds =
        new Dictionary<string, int>(System.StringComparer.OrdinalIgnoreCase)
        {
            [Archive30d] = 30,
            [Archive1y] = 365,
            [Archive5y] = 1825,
        };

    /// <summary>All three time-bounded label strings (case-insensitive).</summary>
    public static IReadOnlySet<string> TimeBoundedLabels { get; } =
        new HashSet<string>(_thresholds.Keys, System.StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// Looks up the retention threshold in days for a time-bounded label.
    /// </summary>
    /// <param name="label">The triage label string to look up.</param>
    /// <param name="thresholdDays">Set to the threshold (30, 365, or 1825) when this returns true.</param>
    /// <returns>True if <paramref name="label"/> is a time-bounded label; false otherwise.</returns>
    public static bool TryGetThreshold(string label, out int thresholdDays)
        => _thresholds.TryGetValue(label, out thresholdDays);

    /// <summary>Returns true iff <paramref name="label"/> is one of the three time-bounded labels.</summary>
    public static bool IsTimeBounded(string label)
        => TimeBoundedLabels.Contains(label);
}
