using System;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace TrashMailPanda.Providers.Storage.Models;

/// <summary>
/// Encrypted OAuth tokens storage.
/// </summary>
[Table("encrypted_tokens")]
public class EncryptedTokenEntity
{
    /// <summary>
    /// Provider name (primary key): "gmail", "openai", etc.
    /// </summary>
    [Key]
    [Required]
    [StringLength(100)]
    [Column("provider")]
    public string Provider { get; set; } = string.Empty;

    /// <summary>
    /// Encrypted token data (OS keychain encrypted).
    /// </summary>
    [Required]
    [Column("encrypted_token")]
    public string EncryptedToken { get; set; } = string.Empty;

    /// <summary>
    /// Token creation timestamp.
    /// </summary>
    [Required]
    [Column("created_at")]
    public DateTime CreatedAt { get; set; }
}
