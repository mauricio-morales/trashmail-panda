using TrashMailPanda.Shared.Models;

namespace TrashMailPanda.Providers.Contacts.Models;

/// <summary>
/// Represents a contact's identity from a specific source platform
/// Enables tracking contacts across multiple platforms and sources
/// </summary>
public record SourceIdentity
{
    /// <summary>
    /// The type of contact source (Google, Apple, Windows, etc.)
    /// </summary>
    public ContactSourceType SourceType { get; init; } = ContactSourceType.Unknown;

    /// <summary>
    /// The unique identifier for this contact within the source platform
    /// </summary>
    public string SourceContactId { get; init; } = string.Empty;

    /// <summary>
    /// Optional additional metadata from the source platform
    /// </summary>
    public Dictionary<string, string> SourceMetadata { get; init; } = new();

    /// <summary>
    /// Timestamp when this source identity was last updated
    /// </summary>
    public DateTime LastUpdatedUtc { get; init; } = DateTime.UtcNow;

    /// <summary>
    /// Whether this source identity is currently active/valid
    /// </summary>
    public bool IsActive { get; init; } = true;
}