using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace TrashMailPanda.Providers.Storage.Models;

/// <summary>
/// Application configuration key-value storage.
/// </summary>
[Table("app_config")]
public class AppConfigEntity
{
    /// <summary>
    /// Configuration key (primary key).
    /// </summary>
    [Key]
    [Required]
    [StringLength(255)]
    [Column("key")]
    public string Key { get; set; } = string.Empty;

    /// <summary>
    /// Configuration value (JSON or plain text).
    /// </summary>
    [Required]
    [Column("value")]
    public string Value { get; set; } = string.Empty;
}
