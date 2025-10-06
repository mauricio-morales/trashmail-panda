namespace TrashMailPanda.Shared.Models;

/// <summary>
/// Enumeration representing the strength of relationship with a contact
/// Used for trust signal computation in email classification
/// </summary>
public enum RelationshipStrength
{
    /// <summary>
    /// Unknown contact - no relationship established
    /// </summary>
    None = 0,

    /// <summary>
    /// Weak relationship - contact exists but limited interaction
    /// </summary>
    Weak = 1,

    /// <summary>
    /// Moderate relationship - regular interaction patterns
    /// </summary>
    Moderate = 2,

    /// <summary>
    /// Strong relationship - frequent interaction and complete contact info
    /// </summary>
    Strong = 3,

    /// <summary>
    /// Trusted relationship - high-trust contact (family, close work contacts, etc.)
    /// </summary>
    Trusted = 4
}