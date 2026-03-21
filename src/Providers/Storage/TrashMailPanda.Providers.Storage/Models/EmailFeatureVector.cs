using System;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace TrashMailPanda.Providers.Storage.Models;

/// <summary>
/// Complete feature vector for ML-based email classification.
/// Contains 38 structured features extracted from email metadata.
/// All features are provider-agnostic.
/// </summary>
public class EmailFeatureVector
{
    // ============================================================
    // PRIMARY KEY
    // ============================================================

    /// <summary>
    /// Provider-assigned email message ID (opaque identifier).
    /// </summary>
    [Required]
    [StringLength(500)]
    [Column("email_id")]
    public string EmailId { get; init; } = string.Empty;

    // ============================================================
    // SENDER IDENTITY FEATURES
    // ============================================================

    /// <summary>
    /// Extracted domain from sender email address.
    /// </summary>
    [Required]
    [StringLength(255)]
    [Column("sender_domain")]
    public string SenderDomain { get; init; } = string.Empty;

    /// <summary>
    /// Boolean: sender in user's contacts (0=false, 1=true).
    /// </summary>
    [Required]
    [Range(0, 1)]
    [Column("sender_known")]
    public int SenderKnown { get; init; }

    /// <summary>
    /// Contact relationship strength.
    /// 0=None, 1=Weak, 2=Strong.
    /// </summary>
    [Required]
    [Range(0, 2)]
    [Column("contact_strength")]
    public int ContactStrength { get; init; }

    // ============================================================
    // AUTHENTICATION FEATURES
    // ============================================================

    /// <summary>
    /// SPF authentication result.
    /// Values: "pass", "fail", "neutral", "none".
    /// </summary>
    [Required]
    [StringLength(20)]
    [Column("spf_result")]
    public string SpfResult { get; init; } = "none";

    /// <summary>
    /// DKIM authentication result.
    /// Values: "pass", "fail", "neutral", "none".
    /// </summary>
    [Required]
    [StringLength(20)]
    [Column("dkim_result")]
    public string DkimResult { get; init; } = "none";

    /// <summary>
    /// DMARC authentication result.
    /// Values: "pass", "fail", "neutral", "none".
    /// </summary>
    [Required]
    [StringLength(20)]
    [Column("dmarc_result")]
    public string DmarcResult { get; init; } = "none";

    // ============================================================
    // EMAIL METADATA FEATURES
    // ============================================================

    /// <summary>
    /// Boolean: List-Unsubscribe header present (0=false, 1=true).
    /// </summary>
    [Required]
    [Range(0, 1)]
    [Column("has_list_unsubscribe")]
    public int HasListUnsubscribe { get; init; }

    /// <summary>
    /// Boolean: email contains attachments (0=false, 1=true).
    /// </summary>
    [Required]
    [Range(0, 1)]
    [Column("has_attachments")]
    public int HasAttachments { get; init; }

    /// <summary>
    /// Hour of day when received (0-23).
    /// </summary>
    [Required]
    [Range(0, 23)]
    [Column("hour_received")]
    public int HourReceived { get; init; }

    /// <summary>
    /// Day of week when received (0=Sunday, 6=Saturday).
    /// </summary>
    [Required]
    [Range(0, 6)]
    [Column("day_of_week")]
    public int DayOfWeek { get; init; }

    /// <summary>
    /// log10(email size in bytes) to normalize large sizes.
    /// </summary>
    [Required]
    [Column("email_size_log")]
    public float EmailSizeLog { get; init; }

    /// <summary>
    /// Character count of subject line.
    /// </summary>
    [Required]
    [Range(0, int.MaxValue)]
    [Column("subject_length")]
    public int SubjectLength { get; init; }

    /// <summary>
    /// Total To + Cc recipients.
    /// </summary>
    [Required]
    [Range(0, int.MaxValue)]
    [Column("recipient_count")]
    public int RecipientCount { get; init; }

    /// <summary>
    /// Boolean: subject starts with "Re:" (0=false, 1=true).
    /// </summary>
    [Required]
    [Range(0, 1)]
    [Column("is_reply")]
    public int IsReply { get; init; }

    // ============================================================
    // USER RULE FEATURES
    // ============================================================

    /// <summary>
    /// Boolean: matches AlwaysKeep rules (0=false, 1=true).
    /// </summary>
    [Required]
    [Range(0, 1)]
    [Column("in_user_whitelist")]
    public int InUserWhitelist { get; init; }

    /// <summary>
    /// Boolean: matches AutoTrash rules (0=false, 1=true).
    /// </summary>
    [Required]
    [Range(0, 1)]
    [Column("in_user_blacklist")]
    public int InUserBlacklist { get; init; }

    // ============================================================
    // CONTENT FEATURES
    // ============================================================

    /// <summary>
    /// Number of folders/labels on email.
    /// </summary>
    [Required]
    [Range(0, int.MaxValue)]
    [Column("label_count")]
    public int LabelCount { get; init; }

    /// <summary>
    /// Count of links in body HTML.
    /// </summary>
    [Required]
    [Range(0, int.MaxValue)]
    [Column("link_count")]
    public int LinkCount { get; init; }

    /// <summary>
    /// Count of images in body HTML.
    /// </summary>
    [Required]
    [Range(0, int.MaxValue)]
    [Column("image_count")]
    public int ImageCount { get; init; }

    /// <summary>
    /// Boolean: 1x1 pixel image detected (tracking pixel) (0=false, 1=true).
    /// </summary>
    [Required]
    [Range(0, 1)]
    [Column("has_tracking_pixel")]
    public int HasTrackingPixel { get; init; }

    /// <summary>
    /// Boolean: unsubscribe link pattern found in body (0=false, 1=true).
    /// </summary>
    [Required]
    [Range(0, 1)]
    [Column("unsubscribe_link_in_body")]
    public int UnsubscribeLinkInBody { get; init; }

    // ============================================================
    // ARCHIVE-SPECIFIC FEATURES
    // ============================================================

    /// <summary>
    /// Absolute UTC timestamp when the email was received by the mail server.
    /// Used to compute display age dynamically (now - ReceivedDateUtc).
    /// Null for legacy rows migrated before this column was added.
    /// </summary>
    [Column("received_date_utc")]
    public DateTime? ReceivedDateUtc { get; init; }

    /// <summary>
    /// Days since email received (relative to extraction time).
    /// Kept for ML feature compatibility; recomputed at training time.
    /// </summary>
    [Required]
    [Range(0, int.MaxValue)]
    [Column("email_age_days")]
    public int EmailAgeDays { get; init; }

    /// <summary>
    /// Boolean: email in Inbox folder - strong keep signal (0=false, 1=true).
    /// </summary>
    [Required]
    [Range(0, 1)]
    [Column("is_in_inbox")]
    public int IsInInbox { get; init; }

    /// <summary>
    /// Boolean: email starred/flagged - strong keep signal (0=false, 1=true).
    /// </summary>
    [Required]
    [Range(0, 1)]
    [Column("is_starred")]
    public int IsStarred { get; init; }

    /// <summary>
    /// Boolean: marked important - keep signal (0=false, 1=true).
    /// </summary>
    [Required]
    [Range(0, 1)]
    [Column("is_important")]
    public int IsImportant { get; init; }

    /// <summary>
    /// Boolean: source folder is Trash - strong delete signal (0=false, 1=true).
    /// </summary>
    [Required]
    [Range(0, 1)]
    [Column("was_in_trash")]
    public int WasInTrash { get; init; }

    /// <summary>
    /// Boolean: source folder is Spam - strong delete signal (0=false, 1=true).
    /// </summary>
    [Required]
    [Range(0, 1)]
    [Column("was_in_spam")]
    public int WasInSpam { get; init; }

    /// <summary>
    /// Boolean: not in Inbox/Trash/Spam - triage target (0=false, 1=true).
    /// </summary>
    [Required]
    [Range(0, 1)]
    [Column("is_archived")]
    public int IsArchived { get; init; }

    /// <summary>
    /// Number of messages in thread/conversation.
    /// </summary>
    [Required]
    [Range(1, int.MaxValue)]
    [Column("thread_message_count")]
    public int ThreadMessageCount { get; init; }

    /// <summary>
    /// Total emails from this sender domain in corpus.
    /// </summary>
    [Required]
    [Range(1, int.MaxValue)]
    [Column("sender_frequency")]
    public int SenderFrequency { get; init; }

    // ============================================================
    // TEXT FEATURES (TF-IDF)
    // ============================================================

    /// <summary>
    /// Raw subject for TF-IDF processing.
    /// Null for headerless emails.
    /// </summary>
    [StringLength(1000)]
    [Column("subject_text")]
    public string? SubjectText { get; init; }

    /// <summary>
    /// First 500 chars of body text for TF-IDF processing.
    /// Null if no body content available.
    /// </summary>
    [StringLength(500)]
    [Column("body_text_short")]
    public string? BodyTextShort { get; init; }

    // ============================================================
    // PHASE 2+ TOPIC FEATURES (NULLABLE)
    // ============================================================

    /// <summary>
    /// LDA topic cluster ID.
    /// Null until Phase 2 topic modeling implemented.
    /// </summary>
    [Range(0, int.MaxValue)]
    [Column("topic_cluster_id")]
    public int? TopicClusterId { get; init; }

    /// <summary>
    /// JSON array of topic probabilities.
    /// Null until Phase 2 topic modeling implemented.
    /// </summary>
    [Column("topic_distribution_json")]
    public string? TopicDistributionJson { get; init; }

    /// <summary>
    /// Sender domain category (predefined categories).
    /// Null until Phase 2 sender categorization implemented.
    /// </summary>
    [StringLength(100)]
    [Column("sender_category")]
    public string? SenderCategory { get; init; }

    /// <summary>
    /// Dense embedding vector as JSON array.
    /// Null until Phase 3 semantic embeddings implemented.
    /// </summary>
    [Column("semantic_embedding_json")]
    public string? SemanticEmbeddingJson { get; init; }

    // ============================================================
    // METADATA
    // ============================================================

    /// <summary>
    /// Boolean: user replied to this email (or another in the thread).
    /// Resolved via local thread-based back-correction from SENT folder messages.
    /// 0=false, 1=true. Default 0 (backward-safe).
    /// </summary>
    [Required]
    [Range(0, 1)]
    [Column("is_replied")]
    public int IsReplied { get; init; }

    /// <summary>
    /// Boolean: user forwarded this email (or another in the thread).
    /// Resolved via local Fwd:/FW:/Fw: prefix detection in SENT folder messages.
    /// 0=false, 1=true. Default 0 (backward-safe).
    /// </summary>
    [Required]
    [Range(0, 1)]
    [Column("is_forwarded")]
    public int IsForwarded { get; init; }

    /// <summary>
    /// Schema version for compatibility checks and migration.
    /// </summary>
    [Required]
    [Range(1, int.MaxValue)]
    [Column("feature_schema_version")]
    public int FeatureSchemaVersion { get; init; } = FeatureSchema.CurrentVersion;

    /// <summary>
    /// ISO8601 timestamp when features were extracted.
    /// </summary>
    [Required]
    [Column("extracted_at")]
    public DateTime ExtractedAt { get; init; }

    /// <summary>
    /// Boolean: user corrected classification - retention priority (0=false, 1=true).
    /// </summary>
    [Required]
    [Range(0, 1)]
    [Column("user_corrected")]
    public int UserCorrected { get; init; }

    // ============================================================
    // TRIAGE LABEL
    // ============================================================

    /// <summary>
    /// Explicit triage decision stored by the triage service after a successful Gmail action.
    /// One of: "Keep", "Archive", "Delete", "Spam".
    /// <c>NULL</c> means the email has not been manually triaged yet; the training pipeline
    /// infers the label from feature flags (WasInSpam, WasInTrash, IsInInbox, IsArchived)
    /// via <c>ITrainingSignalAssigner</c>.  Non-null values take precedence over inference.
    /// </summary>
    [StringLength(20)]
    [Column("training_label")]
    public string? TrainingLabel { get; set; }
}
