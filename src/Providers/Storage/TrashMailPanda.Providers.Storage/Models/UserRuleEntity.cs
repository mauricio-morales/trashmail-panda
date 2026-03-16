using System;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace TrashMailPanda.Providers.Storage.Models;

/// <summary>
/// Represents a user-defined email filtering rule.
/// Used for whitelist/blacklist patterns.
/// </summary>
[Table("user_rules")]
public class UserRuleEntity
{
    /// <summary>
    /// Auto-incrementing primary key.
    /// </summary>
    [Key]
    [DatabaseGenerated(DatabaseGeneratedOption.Identity)]
    [Column("id")]
    public int Id { get; set; }

    /// <summary>
    /// Rule type: "always_keep" or "auto_trash".
    /// </summary>
    [Required]
    [StringLength(50)]
    [Column("rule_type")]
    public string RuleType { get; set; } = string.Empty;

    /// <summary>
    /// Rule key: "sender", "domain", or "listid".
    /// </summary>
    [Required]
    [StringLength(50)]
    [Column("rule_key")]
    public string RuleKey { get; set; } = string.Empty;

    /// <summary>
    /// Rule value: email address, domain, or list-id.
    /// </summary>
    [Required]
    [StringLength(500)]
    [Column("rule_value")]
    public string RuleValue { get; set; } = string.Empty;

    /// <summary>
    /// UTC timestamp when rule was created.
    /// </summary>
    [Required]
    [Column("created_at")]
    public DateTime CreatedAt { get; set; }

    /// <summary>
    /// UTC timestamp when rule was last updated.
    /// </summary>
    [Required]
    [Column("updated_at")]
    public DateTime UpdatedAt { get; set; }
}
