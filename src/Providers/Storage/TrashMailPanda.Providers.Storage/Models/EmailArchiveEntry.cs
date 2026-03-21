using System;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace TrashMailPanda.Providers.Storage.Models;

/// <summary>
/// Complete email archive entry for feature regeneration and retraining.
/// Storage is optional based on capacity limits.
/// </summary>
public class EmailArchiveEntry
{
    // ============================================================
    // PRIMARY KEY
    // ============================================================

    /// <summary>
    /// Provider-assigned email message ID (opaque identifier).
    /// Links to email_features table.
    /// </summary>
    [Required]
    [StringLength(500)]
    [Column("email_id")]
    public string EmailId { get; init; } = string.Empty;

    // ============================================================
    // EMAIL METADATA
    // ============================================================

    /// <summary>
    /// Conversation/thread ID.
    /// Null if provider lacks threading support.
    /// </summary>
    [StringLength(500)]
    [Column("thread_id")]
    public string? ThreadId { get; init; }

    /// <summary>
    /// Email provider identifier.
    /// Values: "gmail", "imap", "outlook", etc.
    /// </summary>
    [Required]
    [StringLength(50)]
    [Column("provider_type")]
    public string ProviderType { get; init; } = string.Empty;

    /// <summary>
    /// JSON-serialized headers dictionary.
    /// Must be valid JSON object.
    /// </summary>
    [Required]
    [Column("headers_json")]
    public string HeadersJson { get; init; } = "{}";

    // ============================================================
    // EMAIL CONTENT (TEXT BLOB STORAGE)
    // ============================================================

    /// <summary>
    /// Plain text email body.
    /// At least one of BodyText or BodyHtml must be non-null.
    /// </summary>
    [Column("body_text")]
    public string? BodyText { get; init; }

    /// <summary>
    /// Sanitized HTML email body.
    /// At least one of BodyText or BodyHtml must be non-null.
    /// </summary>
    [Column("body_html")]
    public string? BodyHtml { get; init; }

    // ============================================================
    // FOLDER/TAG METADATA
    // ============================================================

    /// <summary>
    /// JSON array of canonical folder/tag names.
    /// Must be valid JSON array.
    /// </summary>
    [Required]
    [Column("folder_tags_json")]
    public string FolderTagsJson { get; init; } = "[]";

    /// <summary>
    /// Canonical source folder.
    /// Values: "inbox", "archive", "trash", "spam", "sent", "drafts".
    /// </summary>
    [Required]
    [StringLength(50)]
    [Column("source_folder")]
    public string SourceFolder { get; init; } = string.Empty;

    // ============================================================
    // SIZE AND TIMESTAMPS
    // ============================================================

    /// <summary>
    /// Email size in bytes.
    /// </summary>
    [Required]
    [Range(1, long.MaxValue)]
    [Column("size_estimate")]
    public long SizeEstimate { get; init; }

    /// <summary>
    /// ISO8601 original received timestamp.
    /// </summary>
    [Required]
    [Column("received_date")]
    public DateTime ReceivedDate { get; init; }

    /// <summary>
    /// ISO8601 local archive timestamp.
    /// </summary>
    [Required]
    [Column("archived_at")]
    public DateTime ArchivedAt { get; init; }

    // ============================================================
    // OPTIONAL DISPLAY DATA
    // ============================================================

    /// <summary>
    /// Email preview text (max 200 chars).
    /// </summary>
    [StringLength(200)]
    [Column("snippet")]
    public string? Snippet { get; init; }

    // ============================================================
    // RETENTION PRIORITY
    // ============================================================

    /// <summary>
    /// Boolean: user corrected classification - retention priority (0=false, 1=true).
    /// </summary>
    [Required]
    [Range(0, 1)]
    [Column("user_corrected")]
    public int UserCorrected { get; init; }
}
