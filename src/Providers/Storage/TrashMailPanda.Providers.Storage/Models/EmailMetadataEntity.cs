using System;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace TrashMailPanda.Providers.Storage.Models;

/// <summary>
/// Email classification metadata and processing state.
/// </summary>
[Table("email_metadata")]
public class EmailMetadataEntity
{
    /// <summary>
    /// Email ID (primary key).
    /// </summary>
    [Key]
    [Required]
    [StringLength(500)]
    [Column("email_id")]
    public string EmailId { get; set; } = string.Empty;

    /// <summary>
    /// Folder/label ID.
    /// </summary>
    [StringLength(500)]
    [Column("folder_id")]
    public string? FolderId { get; set; }

    /// <summary>
    /// Human-readable folder name.
    /// </summary>
    [StringLength(255)]
    [Column("folder_name")]
    public string? FolderName { get; set; }

    /// <summary>
    /// Email subject line.
    /// </summary>
    [StringLength(2000)]
    [Column("subject")]
    public string? Subject { get; set; }

    /// <summary>
    /// Sender email address.
    /// </summary>
    [StringLength(500)]
    [Column("sender_email")]
    public string? SenderEmail { get; set; }

    /// <summary>
    /// Sender display name.
    /// </summary>
    [StringLength(500)]
    [Column("sender_name")]
    public string? SenderName { get; set; }

    /// <summary>
    /// Email received timestamp.
    /// </summary>
    [Column("received_date")]
    public DateTime? ReceivedDate { get; set; }

    /// <summary>
    /// AI classification result: "keep", "trash", etc.
    /// </summary>
    [StringLength(50)]
    [Column("classification")]
    public string? Classification { get; set; }

    /// <summary>
    /// Classification confidence score (0.0-1.0).
    /// </summary>
    [Column("confidence")]
    public double? Confidence { get; set; }

    /// <summary>
    /// JSON array of classification reasons.
    /// </summary>
    [Column("reasons")]
    public string? ReasonsJson { get; set; }

    /// <summary>
    /// Bulk operation grouping key.
    /// </summary>
    [StringLength(500)]
    [Column("bulk_key")]
    public string? BulkKey { get; set; }

    /// <summary>
    /// Last classification timestamp.
    /// </summary>
    [Column("last_classified")]
    public DateTime? LastClassified { get; set; }

    /// <summary>
    /// User action taken: "kept", "deleted", etc.
    /// </summary>
    [StringLength(50)]
    [Column("user_action")]
    public string? UserAction { get; set; }

    /// <summary>
    /// Timestamp of user action.
    /// </summary>
    [Column("user_action_timestamp")]
    public DateTime? UserActionTimestamp { get; set; }

    /// <summary>
    /// Processing batch identifier.
    /// </summary>
    [StringLength(100)]
    [Column("processing_batch_id")]
    public string? ProcessingBatchId { get; set; }
}
