using System;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace TrashMailPanda.Providers.Storage.Models;

/// <summary>
/// Tracks applied database migrations/schema versions.
/// </summary>
[Table("schema_version")]
public class SchemaVersionEntity
{
    /// <summary>
    /// Schema version number (primary key).
    /// </summary>
    [Key]
    [Required]
    [Column("version")]
    public int Version { get; set; }

    /// <summary>
    /// Timestamp when migration was applied.
    /// </summary>
    [Required]
    [Column("applied_at")]
    public DateTime AppliedAt { get; set; }

    /// <summary>
    /// Migration description.
    /// </summary>
    [Required]
    [StringLength(500)]
    [Column("description")]
    public string Description { get; set; } = string.Empty;
}
