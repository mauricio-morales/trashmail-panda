using System.Collections.Generic;
using TrashMailPanda.Shared.Models;

namespace TrashMailPanda.Shared;

/// <summary>
/// Basic contact information for external API consumers
/// Provides essential contact details without exposing internal provider-specific models
/// </summary>
public record BasicContactInfo
{
    /// <summary>
    /// Unique identifier for the contact
    /// </summary>
    public string Id { get; init; } = string.Empty;

    /// <summary>
    /// Primary email address for the contact (normalized to lowercase)
    /// </summary>
    public string PrimaryEmail { get; init; } = string.Empty;

    /// <summary>
    /// All email addresses associated with the contact
    /// </summary>
    public IReadOnlyList<string> AllEmails { get; init; } = new List<string>();

    /// <summary>
    /// Display name of the contact
    /// </summary>
    public string DisplayName { get; init; } = string.Empty;

    /// <summary>
    /// Given name (first name) of the contact
    /// </summary>
    public string? GivenName { get; init; }

    /// <summary>
    /// Family name (last name) of the contact
    /// </summary>
    public string? FamilyName { get; init; }

    /// <summary>
    /// Organization or company name
    /// </summary>
    public string? OrganizationName { get; init; }

    /// <summary>
    /// Job title or position
    /// </summary>
    public string? OrganizationTitle { get; init; }

    /// <summary>
    /// Computed relationship strength with this contact
    /// </summary>
    public RelationshipStrength Strength { get; init; } = RelationshipStrength.None;

    /// <summary>
    /// Numeric trust score (0.0-1.0)
    /// </summary>
    public double TrustScore { get; init; } = 0.0;
}