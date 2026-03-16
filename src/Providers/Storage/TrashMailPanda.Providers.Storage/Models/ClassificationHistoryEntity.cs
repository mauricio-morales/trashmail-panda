using System;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace TrashMailPanda.Providers.Storage.Models;

/// <summary>
/// Historical record of email classification decisions.
/// </summary>
[Table("classification_history")]
public class ClassificationHistoryEntity
{
    /// <summary>
    /// Auto-incrementing primary key.
    /// </summary>
    [Key]
    [DatabaseGenerated(DatabaseGeneratedOption.Identity)]
    [Column("id")]
    public int Id { get; set; }

    /// <summary>
    /// Classification timestamp.
    /// </summary>
    [Required]
    [Column("timestamp")]
    public DateTime Timestamp { get; set; }

    /// <summary>
    /// Email ID that was classified.
    /// </summary>
    [Required]
    [StringLength(500)]
    [Column("email_id")]
    public string EmailId { get; set; } = string.Empty;

    /// <summary>
    /// Classification result: "keep", "trash", etc.
    /// </summary>
    [Required]
    [StringLength(50)]
    [Column("classification")]
    public string Classification { get; set; } = string.Empty;

    /// <summary>
    /// Classification confidence score (0.0-1.0).
    /// </summary>
    [Required]
    [Column("confidence")]
    public double Confidence { get; set; }

    /// <summary>
    /// JSON array of classification reasons.
    /// </summary>
    [Required]
    [Column("reasons")]
    public string ReasonsJson { get; set; } = "[]";

    /// <summary>
    /// User action taken (if any).
    /// </summary>
    [StringLength(50)]
    [Column("user_action")]
    public string? UserAction { get; set; }

    /// <summary>
    /// User feedback on classification accuracy.
    /// </summary>
    [StringLength(50)]
    [Column("user_feedback")]
    public string? UserFeedback { get; set; }

    /// <summary>
    /// Processing batch identifier.
    /// </summary>
    [StringLength(100)]
    [Column("batch_id")]
    public string? BatchId { get; set; }
}
