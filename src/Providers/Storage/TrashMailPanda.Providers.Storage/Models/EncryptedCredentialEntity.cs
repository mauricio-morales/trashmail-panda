using System;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace TrashMailPanda.Providers.Storage.Models;

/// <summary>
/// Generic encrypted credentials storage.
/// </summary>
[Table("encrypted_credentials")]
public class EncryptedCredentialEntity
{
    /// <summary>
    /// Credential key (primary key).
    /// </summary>
    [Key]
    [Required]
    [StringLength(255)]
    [Column("key")]
    public string Key { get; set; } = string.Empty;

    /// <summary>
    /// Encrypted credential value (OS keychain encrypted).
    /// </summary>
    [Required]
    [Column("encrypted_value")]
    public string EncryptedValue { get; set; } = string.Empty;

    /// <summary>
    /// Credential creation timestamp.
    /// </summary>
    [Required]
    [Column("created_at")]
    public DateTime CreatedAt { get; set; }

    /// <summary>
    /// Optional expiration timestamp.
    /// </summary>
    [Column("expires_at")]
    public DateTime? ExpiresAt { get; set; }
}
