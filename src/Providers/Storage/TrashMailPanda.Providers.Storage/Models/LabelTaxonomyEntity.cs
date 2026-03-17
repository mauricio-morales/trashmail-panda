using System.ComponentModel.DataAnnotations;

namespace TrashMailPanda.Providers.Storage.Models;

/// <summary>
/// The user's complete Gmail label catalog, including user-created and system labels.
/// Updated at the beginning of each training scan.
/// </summary>
public class LabelTaxonomyEntity
{
    [Required]
    [StringLength(500)]
    public string LabelId { get; set; } = string.Empty;    // PK — Gmail label ID (e.g., "Label_123")

    [Required]
    [StringLength(320)]
    public string AccountId { get; set; } = string.Empty;  // Authenticated user email

    [Required]
    [StringLength(200)]
    public string Name { get; set; } = string.Empty;       // Display name (e.g., "Receipts")

    [StringLength(50)]
    public string? Color { get; set; }                     // Gmail label color hex or name (nullable)

    [Required]
    [StringLength(10)]
    public string LabelType { get; set; } = "User";        // "User" or "System"

    public int UsageCount { get; set; }                    // Count of training emails bearing this label

    public DateTime CreatedAt { get; set; }
    public DateTime UpdatedAt { get; set; }

    // Navigation
    public ICollection<LabelAssociationEntity> LabelAssociations { get; set; } = [];
}
