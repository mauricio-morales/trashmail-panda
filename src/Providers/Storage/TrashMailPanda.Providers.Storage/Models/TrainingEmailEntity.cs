using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace TrashMailPanda.Providers.Storage.Models;

/// <summary>
/// Training email record imported from Gmail training scans.
/// Each row represents one email retrieved from Spam, Trash, Archive, Inbox, or Sent.
/// Records are upserted on incremental scans to reflect current state.
/// </summary>
public class TrainingEmailEntity
{
    [Required]
    [StringLength(500)]
    [Column("email_id")]
    public string EmailId { get; set; } = string.Empty;     // PK — Gmail message ID

    [Required]
    [StringLength(320)]
    [Column("account_id")]
    public string AccountId { get; set; } = string.Empty;   // Authenticated user email

    [Required]
    [StringLength(500)]
    [Column("thread_id")]
    public string ThreadId { get; set; } = string.Empty;    // Gmail threadId — used for local back-correction

    [Required]
    [StringLength(20)]
    [Column("folder_origin")]
    public string FolderOrigin { get; set; } = string.Empty; // "Spam", "Trash", "Archive", "Inbox", "Sent"

    [Column("is_read")]
    public bool IsRead { get; set; }

    [Column("is_replied")]
    public bool IsReplied { get; set; }
    // Initially false. Back-corrected to true when any SENT message in the same ThreadId is encountered.

    [Column("is_forwarded")]
    public bool IsForwarded { get; set; }
    // Initially false. Back-corrected to true when a SENT message in the same ThreadId
    // has SubjectPrefix matching "Fwd:", "FW:", or "Fw:".

    [StringLength(10)]
    [Column("subject_prefix")]
    public string? SubjectPrefix { get; set; }
    // First 10 chars of subject — stored only for SENT messages to enable IsForwarded back-correction.
    // Null for non-SENT messages.

    [Required]
    [StringLength(20)]
    [Column("classification_signal")]
    public string ClassificationSignal { get; set; } = string.Empty;
    // Stored as string: "AutoDelete", "AutoArchive", "LowConfidence", "Excluded"

    [Column("signal_confidence")]
    public float SignalConfidence { get; set; } // 0.0–1.0; 0 for Excluded

    [Column("is_valid")]
    public bool IsValid { get; set; } = true;
    // Set to false when a state change moves email to Excluded category

    [StringLength(2000)]
    [Column("raw_label_ids")]
    public string? RawLabelIds { get; set; }
    // JSON array of Gmail label IDs at time of last scan (e.g. ["Label_123", "UNREAD"])

    [Column("last_seen_at")]
    public DateTime LastSeenAt { get; set; }   // Timestamp of last scan that touched this email
    [Column("imported_at")]
    public DateTime ImportedAt { get; set; }   // Timestamp of initial import (immutable)
    [Column("updated_at")]
    public DateTime UpdatedAt { get; set; }    // Timestamp of last state update

    // Navigation
    public ICollection<LabelAssociationEntity> LabelAssociations { get; set; } = [];
}
