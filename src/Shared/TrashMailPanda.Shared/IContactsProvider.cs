using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using TrashMailPanda.Shared.Base;
using TrashMailPanda.Shared.Models;

namespace TrashMailPanda.Shared;

/// <summary>
/// Abstract interface for contacts providers
/// Provides contact information to enhance email classification
/// </summary>
public interface IContactsProvider
{
    /// <summary>
    /// Check if an email or domain is in the user's contacts
    /// </summary>
    /// <param name="emailOrDomain">Email address or domain to check</param>
    /// <returns>True if the contact is known</returns>
    Task<bool> IsKnownAsync(string emailOrDomain);

    /// <summary>
    /// Get the relationship strength with a contact
    /// </summary>
    /// <param name="email">Email address to check</param>
    /// <returns>Strength of relationship</returns>
    Task<RelationshipStrength> GetRelationshipStrengthAsync(string email);

    /// <summary>
    /// Get simplified contact signal with known status and relationship strength
    /// </summary>
    /// <param name="emailAddress">Email address to check</param>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>Simple contact signal with known status and relationship strength</returns>
    Task<Result<ContactSignal>> GetContactSignalAsync(string emailAddress, CancellationToken cancellationToken = default);

    /// <summary>
    /// Clear all cached contacts data
    /// </summary>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>True if cache was cleared successfully</returns>
    Task<Result<bool>> ClearCacheAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Get contact information by email address
    /// </summary>
    /// <param name="emailAddress">Email address to look up</param>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>Contact information if found</returns>
    Task<Result<BasicContactInfo?>> GetContactByEmailAsync(string emailAddress, CancellationToken cancellationToken = default);

    /// <summary>
    /// Get all contacts from the provider
    /// </summary>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>List of all contacts</returns>
    Task<Result<IReadOnlyList<BasicContactInfo>>> GetAllContactsAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Get detailed trust signal for a contact
    /// </summary>
    /// <param name="emailAddress">Email address to get trust signal for</param>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>Trust signal details</returns>
    Task<Result<TrustSignalInfo>> GetTrustSignalAsync(string emailAddress, CancellationToken cancellationToken = default);
}