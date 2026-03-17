using System.ComponentModel.DataAnnotations;

namespace TrashMailPanda.Providers.Storage.Models;

/// <summary>
/// Links training emails to Gmail labels.
/// User-created labels are recorded as positive training signals;
/// system labels are recorded as context features.
/// A single email may have multiple associations.
/// </summary>
public class LabelAssociationEntity
{
    public int Id { get; set; }                             // PK (auto-increment)

    [Required]
    [StringLength(500)]
    public string EmailId { get; set; } = string.Empty;    // FK → training_emails.EmailId

    [Required]
    [StringLength(500)]
    public string LabelId { get; set; } = string.Empty;    // FK → label_taxonomy.LabelId

    public bool IsTrainingSignal { get; set; }             // true: user label → positive training signal
    public bool IsContextFeature { get; set; }             // true: system label → context feature only

    public DateTime CreatedAt { get; set; }

    // Navigation
    public TrainingEmailEntity? TrainingEmail { get; set; }
    public LabelTaxonomyEntity? LabelTaxonomy { get; set; }
}
