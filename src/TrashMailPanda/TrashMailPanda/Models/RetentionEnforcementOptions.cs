namespace TrashMailPanda.Models;

/// <summary>
/// Configuration options for the retention enforcement service.
/// Bound from the <c>RetentionEnforcement</c> section of <c>appsettings.json</c>.
/// </summary>
public sealed class RetentionEnforcementOptions
{
    /// <summary>
    /// Target interval in days between retention scans.
    /// Must be ≥ 1. Default: 30 (one calendar month).
    /// </summary>
    public int ScanIntervalDays { get; set; } = 30;

    /// <summary>
    /// Number of days since the last scan before the startup prompt appears.
    /// Must be ≥ 1 and ≤ <see cref="ScanIntervalDays"/>. Default: 7.
    /// </summary>
    public int PromptThresholdDays { get; set; } = 7;
}
