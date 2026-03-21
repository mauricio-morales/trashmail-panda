using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace TrashMailPanda.Providers.Storage.Models;

/// <summary>
/// The user's complete Gmail label catalog, including user-created and system labels.
/// Updated at the beginning of each training scan.
/// </summary>
public class LabelTaxonomyEntity
{
    [Required]
    [StringLength(500)]
    [Column("label_id")]
    public string LabelId { get; set; } = string.Empty;    // PK — Gmail label ID (e.g., "Label_123")

    [Required]
    [StringLength(320)]
    [Column("account_id")]
    public string AccountId { get; set; } = string.Empty;  // Authenticated user email

    [Required]
    [StringLength(200)]
    [Column("name")]
    public string Name { get; set; } = string.Empty;       // Display name (e.g., "Receipts")

    [StringLength(50)]
    [Column("color")]
    public string? Color { get; set; }                     // Gmail label color hex or name (nullable)

    [Required]
    [StringLength(10)]
    [Column("label_type")]
    public string LabelType { get; set; } = "User";        // "User" or "System"

    [Column("usage_count")]
    public int UsageCount { get; set; }                    // Count of training emails bearing this label

    [Column("created_at")]
    public DateTime CreatedAt { get; set; }
    [Column("updated_at")]
    public DateTime UpdatedAt { get; set; }

    // Navigation
    public ICollection<LabelAssociationEntity> LabelAssociations { get; set; } = [];
}
