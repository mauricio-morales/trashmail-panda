using System.ComponentModel.DataAnnotations;

namespace TrashMailPanda.Providers.Storage.Models;

/// <summary>
/// Tracks scan state for training data collection.
/// Supports crash recovery, multi-session resumability, and incremental change detection.
/// At most one active scan per account at any time.
/// </summary>
public class ScanProgressEntity
{
    public int Id { get; set; }                             // PK (auto-increment)

    [Required]
    [StringLength(320)]
    public string AccountId { get; set; } = string.Empty;

    [Required]
    [StringLength(20)]
    public string ScanType { get; set; } = "Initial";      // "Initial" or "Incremental"

    [Required]
    [StringLength(20)]
    public string Status { get; set; } = "InProgress";
    // Overall status: "InProgress", "Completed", "Interrupted", "PausedStorageFull"

    [StringLength(4000)]
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

    public ulong? HistoryId { get; set; }
    // Gmail historyId saved at completion of a full scan.
    // Used by subsequent incremental scans via users.history.list.
    // Null until first complete scan finishes.

    public int ProcessedCount { get; set; }                // Total emails processed across all folders
    public int? TotalEstimate { get; set; }                // Estimated total (nullable; from Gmail count API)

    [StringLength(500)]
    public string? LastProcessedEmailId { get; set; }      // EmailId of last successfully committed email

    public DateTime StartedAt { get; set; }                // Timestamp of scan initiation
    public DateTime UpdatedAt { get; set; }                // Timestamp of last checkpoint commit
    public DateTime? CompletedAt { get; set; }             // Null while scan is active
}
