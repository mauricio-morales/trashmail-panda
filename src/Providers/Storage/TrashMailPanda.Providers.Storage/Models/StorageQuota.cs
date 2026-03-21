using System;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace TrashMailPanda.Providers.Storage.Models;

/// <summary>
/// Tracks storage usage and cleanup history.
/// Single-row configuration table (Id always = 1).
/// </summary>
public class StorageQuota
{
    /// <summary>
    /// Default storage limit: 50GB in bytes.
    /// </summary>
    public const long DefaultLimitBytes = 53_687_091_200; // 50GB = 50 * 1024^3

    /// <summary>
    /// Primary key (always 1 for single-row table).
    /// </summary>
    [Required]
    [Range(1, 1)]
    [Column("id")]
    public int Id { get; set; } = 1;

    /// <summary>
    /// Configured storage limit in bytes.
    /// Default: 50GB = 53,687,091,200 bytes.
    /// </summary>
    [Required]
    [Range(1, long.MaxValue)]
    [Column("limit_bytes")]
    public long LimitBytes { get; set; }

    /// <summary>
    /// Current database size in bytes.
    /// </summary>
    [Required]
    [Range(0, long.MaxValue)]
    [Column("current_bytes")]
    public long CurrentBytes { get; set; }

    /// <summary>
    /// Space used by email_features table in bytes.
    /// </summary>
    [Required]
    [Range(0, long.MaxValue)]
    [Column("feature_bytes")]
    public long FeatureBytes { get; set; }

    /// <summary>
    /// Space used by email_archive table in bytes.
    /// </summary>
    [Required]
    [Range(0, long.MaxValue)]
    [Column("archive_bytes")]
    public long ArchiveBytes { get; set; }

    /// <summary>
    /// Total stored feature vectors count.
    /// </summary>
    [Required]
    [Range(0, long.MaxValue)]
    [Column("feature_count")]
    public long FeatureCount { get; set; }

    /// <summary>
    /// Total stored full email archives count.
    /// </summary>
    [Required]
    [Range(0, long.MaxValue)]
    [Column("archive_count")]
    public long ArchiveCount { get; set; }

    /// <summary>
    /// Count of user-corrected emails (high priority retention).
    /// </summary>
    [Required]
    [Range(0, long.MaxValue)]
    [Column("user_corrected_count")]
    public long UserCorrectedCount { get; set; }

    /// <summary>
    /// ISO8601 timestamp of last cleanup execution.
    /// Null if never executed.
    /// </summary>
    [Column("last_cleanup_at")]
    public DateTime? LastCleanupAt { get; set; }

    /// <summary>
    /// ISO8601 timestamp of last monitoring check.
    /// </summary>
    [Required]
    [Column("last_monitored_at")]
    public DateTime LastMonitoredAt { get; set; }
}
