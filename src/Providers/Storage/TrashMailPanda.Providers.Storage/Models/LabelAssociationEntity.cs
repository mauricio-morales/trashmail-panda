using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace TrashMailPanda.Providers.Storage.Models;

/// <summary>
/// Links training emails to Gmail labels.
/// User-created labels are recorded as positive training signals;
/// system labels are recorded as context features.
/// A single email may have multiple associations.
/// </summary>
public class LabelAssociationEntity
{
    [Column("id")]
    public int Id { get; set; }                             // PK (auto-increment)

    [Required]
    [StringLength(500)]
    [Column("email_id")]
    public string EmailId { get; set; } = string.Empty;    // FK → training_emails.email_id

    [Required]
    [StringLength(500)]
    [Column("label_id")]
    public string LabelId { get; set; } = string.Empty;    // FK → label_taxonomy.label_id

    [Column("is_training_signal")]
    public bool IsTrainingSignal { get; set; }             // true: user label → positive training signal
    [Column("is_context_feature")]
    public bool IsContextFeature { get; set; }             // true: system label → context feature only

    [Column("created_at")]
    public DateTime CreatedAt { get; set; }

    // Navigation
    public TrainingEmailEntity? TrainingEmail { get; set; }
    public LabelTaxonomyEntity? LabelTaxonomy { get; set; }
}
