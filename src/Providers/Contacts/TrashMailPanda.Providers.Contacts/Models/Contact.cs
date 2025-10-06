using System.ComponentModel.DataAnnotations;
using TrashMailPanda.Shared.Models;

namespace TrashMailPanda.Providers.Contacts.Models;

/// <summary>
/// Unified contact model that normalizes contact information across multiple platforms
/// Designed for fast email classification lookups and trust signal computation
/// </summary>
public record Contact
{
    /// <summary>
    /// TrashMail Panda unique identifier for this contact
    /// Generated as a hash of normalized email and name data
    /// </summary>
    [Required]
    public string Id { get; init; } = string.Empty;

    /// <summary>
    /// Primary email address for this contact (normalized to lowercase)
    /// Used as the primary lookup key for email classification
    /// </summary>
    [Required]
    [EmailAddress]
    public string PrimaryEmail { get; init; } = string.Empty;

    /// <summary>
    /// All known email addresses for this contact (normalized to lowercase)
    /// Enables comprehensive email classification coverage
    /// </summary>
    public List<string> AllEmails { get; init; } = new();

    /// <summary>
    /// Full display name combining given and family names
    /// </summary>
    public string DisplayName { get; init; } = string.Empty;

    /// <summary>
    /// First/given name
    /// </summary>
    public string? GivenName { get; init; }

    /// <summary>
    /// Last/family name
    /// </summary>
    public string? FamilyName { get; init; }

    /// <summary>
    /// Phone numbers in normalized E.164 format
    /// Provides additional trust signals for relationship strength
    /// </summary>
    public List<string> PhoneNumbers { get; init; } = new();

    /// <summary>
    /// Organization/company name
    /// Contributes to trust signal computation
    /// </summary>
    public string? OrganizationName { get; init; }

    /// <summary>
    /// Job title within the organization
    /// </summary>
    public string? OrganizationTitle { get; init; }

    /// <summary>
    /// URL to the contact's profile photo
    /// </summary>
    public string? PhotoUrl { get; init; }

    /// <summary>
    /// Identities from multiple source platforms
    /// Enables cross-platform contact consolidation
    /// </summary>
    public List<SourceIdentity> SourceIdentities { get; init; } = new();

    /// <summary>
    /// Timestamp when this contact was last modified in any source
    /// Used for sync optimization and conflict resolution
    /// </summary>
    public DateTime LastModifiedUtc { get; init; } = DateTime.UtcNow;

    /// <summary>
    /// Timestamp when this contact was last synchronized from any source
    /// </summary>
    public DateTime LastSyncedUtc { get; init; } = DateTime.UtcNow;

    /// <summary>
    /// Computed trust score for this contact (0.0-1.0)
    /// Cached value from TrustSignalCalculator for fast lookups
    /// </summary>
    [Range(0.0, 1.0)]
    public double RelationshipStrength { get; init; } = 0.0;

    /// <summary>
    /// Additional metadata from various sources
    /// Extensible for future platform-specific data
    /// </summary>
    public Dictionary<string, string> Metadata { get; init; } = new();

    /// <summary>
    /// Determines if this contact has sufficient information for trust computation
    /// </summary>
    public bool IsComplete =>
        !string.IsNullOrWhiteSpace(PrimaryEmail) &&
        !string.IsNullOrWhiteSpace(DisplayName) &&
        SourceIdentities.Any(s => s.IsActive);

    /// <summary>
    /// Gets the primary source identity for this contact
    /// Prioritizes Google > Apple > Windows > Manual
    /// </summary>
    public SourceIdentity? PrimarySource => SourceIdentities
        .Where(s => s.IsActive)
        .OrderBy(s => s.SourceType switch
        {
            ContactSourceType.Google => 1,
            ContactSourceType.Apple => 2,
            ContactSourceType.Windows => 3,
            ContactSourceType.Outlook => 4,
            ContactSourceType.Manual => 5,
            _ => 10
        })
        .FirstOrDefault();
}