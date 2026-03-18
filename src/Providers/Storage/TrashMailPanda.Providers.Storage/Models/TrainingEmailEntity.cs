using System.ComponentModel.DataAnnotations;

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
    public string EmailId { get; set; } = string.Empty;     // PK — Gmail message ID

    [Required]
    [StringLength(320)]
    public string AccountId { get; set; } = string.Empty;   // Authenticated user email

    [Required]
    [StringLength(500)]
    public string ThreadId { get; set; } = string.Empty;    // Gmail threadId — used for local back-correction

    [Required]
    [StringLength(20)]
    public string FolderOrigin { get; set; } = string.Empty; // "Spam", "Trash", "Archive", "Inbox", "Sent"

    public bool IsRead { get; set; }

    public bool IsReplied { get; set; }
    // Initially false. Back-corrected to true when any SENT message in the same ThreadId is encountered.

    public bool IsForwarded { get; set; }
    // Initially false. Back-corrected to true when a SENT message in the same ThreadId
    // has SubjectPrefix matching "Fwd:", "FW:", or "Fw:".

    [StringLength(10)]
    public string? SubjectPrefix { get; set; }
    // First 10 chars of subject — stored only for SENT messages to enable IsForwarded back-correction.
    // Null for non-SENT messages.

    [Required]
    [StringLength(20)]
    public string ClassificationSignal { get; set; } = string.Empty;
    // Stored as string: "AutoDelete", "AutoArchive", "LowConfidence", "Excluded"

    public float SignalConfidence { get; set; } // 0.0–1.0; 0 for Excluded

    public bool IsValid { get; set; } = true;
    // Set to false when a state change moves email to Excluded category

    [StringLength(2000)]
    public string? RawLabelIds { get; set; }
    // JSON array of Gmail label IDs at time of last scan (e.g. ["Label_123", "UNREAD"])

    public DateTime LastSeenAt { get; set; }   // Timestamp of last scan that touched this email
    public DateTime ImportedAt { get; set; }   // Timestamp of initial import (immutable)
    public DateTime UpdatedAt { get; set; }    // Timestamp of last state update

    // Navigation
    public ICollection<LabelAssociationEntity> LabelAssociations { get; set; } = [];
}
