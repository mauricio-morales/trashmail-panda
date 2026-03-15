using System;
using System.ComponentModel.DataAnnotations;

namespace TrashMailPanda.Providers.Storage.Models;

/// <summary>
/// Tracks schema version history for email feature vectors.
/// Enables database upgrades and compatibility checks.
/// </summary>
public class FeatureSchema
{
    /// <summary>
    /// Current active schema version for new feature extractions.
    /// </summary>
    public const int CurrentVersion = 1;

    /// <summary>
    /// Schema version number.
    /// </summary>
    [Required]
    [Range(1, int.MaxValue)]
    public int Version { get; init; }

    /// <summary>
    /// ISO8601 timestamp when this schema version was applied.
    /// </summary>
    [Required]
    public DateTime AppliedAt { get; init; }

    /// <summary>
    /// Human-readable description of schema changes.
    /// </summary>
    [Required]
    [StringLength(500)]
    public string Description { get; init; } = string.Empty;

    /// <summary>
    /// Total feature count in this schema version.
    /// </summary>
    [Required]
    [Range(1, int.MaxValue)]
    public int FeatureCount { get; init; }
}
