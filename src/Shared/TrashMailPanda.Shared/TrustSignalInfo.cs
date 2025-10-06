using System;
using System.Collections.Generic;
using TrashMailPanda.Shared.Models;

namespace TrashMailPanda.Shared;

/// <summary>
/// Detailed trust signal information for a contact
/// Provides context and scoring details for relationship strength computation
/// </summary>
public record TrustSignalInfo
{
    /// <summary>
    /// Contact identifier this trust signal applies to
    /// </summary>
    public string ContactId { get; init; } = string.Empty;

    /// <summary>
    /// Email address this trust signal is for
    /// </summary>
    public string EmailAddress { get; init; } = string.Empty;

    /// <summary>
    /// Categorical relationship strength classification
    /// </summary>
    public RelationshipStrength Strength { get; init; } = RelationshipStrength.None;

    /// <summary>
    /// Numeric trust score (0.0-1.0)
    /// Higher scores indicate stronger trust relationship
    /// </summary>
    public double Score { get; init; } = 0.0;

    /// <summary>
    /// Date of the most recent interaction with this contact
    /// </summary>
    public DateTime? LastInteractionDate { get; init; }

    /// <summary>
    /// Number of interactions with this contact
    /// </summary>
    public int InteractionCount { get; init; } = 0;

    /// <summary>
    /// List of reasons contributing to this trust level
    /// Provides transparency in trust computation
    /// </summary>
    public IReadOnlyList<string> Justification { get; init; } = new List<string>();

    /// <summary>
    /// Timestamp when this trust signal was computed
    /// </summary>
    public DateTime ComputedAt { get; init; } = DateTime.UtcNow;

    /// <summary>
    /// Indicates if the contact is known (exists in contacts)
    /// </summary>
    public bool Known { get; init; } = false;

    /// <summary>
    /// Source type that provided this contact information
    /// </summary>
    public string? SourceType { get; init; }
}