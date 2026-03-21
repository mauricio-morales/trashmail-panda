using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace TrashMailPanda.Providers.Storage.Models;

/// <summary>
/// Tracks scan state for training data collection.
/// Supports crash recovery, multi-session resumability, and incremental change detection.
/// At most one active scan per account at any time.
/// </summary>
public class ScanProgressEntity
{
    [Column("id")]
    public int Id { get; set; }                             // PK (auto-increment)

    [Required]
    [StringLength(320)]
    [Column("account_id")]
    public string AccountId { get; set; } = string.Empty;

    [Required]
    [StringLength(20)]
    [Column("scan_type")]
    public string ScanType { get; set; } = "Initial";      // "Initial" or "Incremental"

    [Required]
    [StringLength(20)]
    [Column("status")]
    public string Status { get; set; } = "InProgress";
    // Overall status: "InProgress", "Completed", "Interrupted", "PausedStorageFull"

    [StringLength(4000)]
    [Column("folder_progress_json")]
    public string? FolderProgressJson { get; set; }
    // Per-folder cursor state. JSON object keyed by folder name.
    // Example:
    // {
    //   "Spam":    { "status": "Completed",  "processedCount": 450,  "pageToken": null },
    //   "Trash":   { "status": "Completed",  "processedCount": 1200, "pageToken": null },
    //   "Sent":    { "status": "InProgress", "processedCount": 800,  "pageToken": "NEXT_TOKEN" },
    //   "Archive": { "status": "NotStarted", "processedCount": 0,    "pageToken": null },
    //   "Inbox":   { "status": "NotStarted", "processedCount": 0,    "pageToken": null }
    // }
    // Updated atomically with each batch commit (same SQLite transaction as the batch write).

    [Column("history_id")]
    public ulong? HistoryId { get; set; }
    // Gmail historyId saved at completion of a full scan.
    // Used by subsequent incremental scans via users.history.list.
    // Null until first complete scan finishes.

    [Column("processed_count")]
    public int ProcessedCount { get; set; }                // Total emails processed across all folders
    [Column("total_estimate")]
    public int? TotalEstimate { get; set; }                // Estimated total (nullable; from Gmail count API)

    [StringLength(500)]
    [Column("last_processed_email_id")]
    public string? LastProcessedEmailId { get; set; }      // EmailId of last successfully committed email

    [Column("started_at")]
    public DateTime StartedAt { get; set; }                // Timestamp of scan initiation
    [Column("updated_at")]
    public DateTime UpdatedAt { get; set; }                // Timestamp of last checkpoint commit
    [Column("completed_at")]
    public DateTime? CompletedAt { get; set; }             // Null while scan is active
}
