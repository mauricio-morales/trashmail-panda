using System.ComponentModel.DataAnnotations;
using TrashMailPanda.Shared.Models;

namespace TrashMailPanda.Providers.Contacts.Models;

/// <summary>
/// Represents the computed trust signal for a contact
/// Used for enhanced email classification with relationship context
/// </summary>
public record TrustSignal
{
    /// <summary>
    /// The contact ID this trust signal applies to
    /// </summary>
    [Required]
    public string ContactId { get; init; } = string.Empty;

    /// <summary>
    /// Categorical relationship strength classification
    /// </summary>
    public RelationshipStrength Strength { get; init; } = RelationshipStrength.None;

    /// <summary>
    /// Numeric trust score (0.0 = no trust, 1.0 = maximum trust)
    /// Used for fine-grained classification decisions
    /// </summary>
    [Range(0.0, 1.0)]
    public double Score { get; init; } = 0.0;

    /// <summary>
    /// Date of last known interaction with this contact
    /// Derived from email provider interaction history (future implementation)
    /// </summary>
    public DateTime? LastInteractionDate { get; init; }

    /// <summary>
    /// Human-readable explanations for why this trust level was assigned
    /// Used for debugging and user transparency
    /// </summary>
    public List<string> Justification { get; init; } = new();

    /// <summary>
    /// Timestamp when this trust signal was computed
    /// Used for cache invalidation and freshness validation
    /// </summary>
    public DateTime ComputedAt { get; init; } = DateTime.UtcNow;

    /// <summary>
    /// Number of email interactions used in computation
    /// Will be populated from email provider data in future versions
    /// </summary>
    public int InteractionCount { get; init; } = 0;

    /// <summary>
    /// Recency score based on interaction patterns (0.0-1.0)
    /// Higher values indicate more recent interactions
    /// </summary>
    [Range(0.0, 1.0)]
    public double RecencyScore { get; init; } = 0.0;

    /// <summary>
    /// Frequency score based on interaction patterns (0.0-1.0)
    /// Higher values indicate more frequent interactions
    /// </summary>
    [Range(0.0, 1.0)]
    public double FrequencyScore { get; init; } = 0.0;

    /// <summary>
    /// Whether this trust signal should be considered valid for classification
    /// </summary>
    public bool IsValid =>
        !string.IsNullOrWhiteSpace(ContactId) &&
        Score >= 0.0 && Score <= 1.0 &&
        ComputedAt > DateTime.UtcNow.AddDays(-30); // Expire after 30 days

    /// <summary>
    /// Gets a confidence level for this trust signal
    /// Based on data completeness and recency
    /// </summary>
    public double ConfidenceLevel
    {
        get
        {
            var confidence = 0.5; // Base confidence

            // Boost confidence for more justifications (better reasoning)
            confidence += Math.Min(Justification.Count * 0.1, 0.3);

            // Boost confidence for recent computation
            var daysSinceComputed = (DateTime.UtcNow - ComputedAt).TotalDays;
            confidence += Math.Max(0, (30 - daysSinceComputed) / 30 * 0.2);

            // Boost confidence for interaction data
            if (LastInteractionDate.HasValue)
                confidence += 0.1;

            if (InteractionCount > 0)
                confidence += Math.Min(InteractionCount * 0.02, 0.2);

            return Math.Min(confidence, 1.0);
        }
    }
}