using System;

namespace TrashMailPanda.Shared;

/// <summary>
/// Retention enforcement settings stored inside <see cref="ProcessingSettings"/>.
/// Persisted via <c>IConfigurationService</c> in the app_config KV table.
/// </summary>
public sealed class RetentionSettings
{
    /// <summary>
    /// Target interval in days between retention scans.
    /// Default: 30 (one calendar month).
    /// </summary>
    public int ScanIntervalDays { get; set; } = 30;

    /// <summary>
    /// Number of days since the last scan before the startup prompt appears.
    /// Default: 7 (prompt appears if last scan was > 7 days ago).
    /// </summary>
    public int PromptThresholdDays { get; set; } = 7;

    /// <summary>
    /// UTC timestamp of the last completed retention scan.
    /// Null if no scan has ever run.
    /// </summary>
    public DateTime? LastScanUtc { get; set; }
}
