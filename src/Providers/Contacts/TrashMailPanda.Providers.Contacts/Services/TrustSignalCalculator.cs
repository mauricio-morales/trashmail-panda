using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using TrashMailPanda.Shared.Base;
using TrashMailPanda.Shared.Models;
using TrashMailPanda.Providers.Contacts.Models;

namespace TrashMailPanda.Providers.Contacts.Services;

/// <summary>
/// Calculates trust signals and relationship strength for contacts
/// Uses multiple factors to determine email trustworthiness and relationship quality
/// </summary>
public class TrustSignalCalculator
{
    private readonly ILogger<TrustSignalCalculator> _logger;
    private readonly ContactsProviderConfig _config;

    // Trust signal weights - these determine relative importance of each factor
    private const double EMAIL_INTERACTION_WEIGHT = 0.30;    // Email exchange history
    private const double CONTACT_COMPLETENESS_WEIGHT = 0.20; // How complete the contact info is
    private const double ORGANIZATION_WEIGHT = 0.15;         // Shared organization or domain
    private const double PHONE_VERIFICATION_WEIGHT = 0.15;   // Phone number presence/validation
    private const double RECENCY_WEIGHT = 0.10;             // How recent interactions are
    private const double PLATFORM_PRESENCE_WEIGHT = 0.10;    // Multi-platform presence

    // Trust signal thresholds
    private const double TRUSTED_THRESHOLD = 0.80;    // 80%+ = Trusted
    private const double STRONG_THRESHOLD = 0.60;     // 60%+ = Strong
    private const double MODERATE_THRESHOLD = 0.35;   // 35%+ = Moderate  
    private const double WEAK_THRESHOLD = 0.15;       // 15%+ = Weak
    // Below 15% = None

    // Time decay factors
    private static readonly TimeSpan RECENT_INTERACTION_WINDOW = TimeSpan.FromDays(30);
    private static readonly TimeSpan STALE_INTERACTION_THRESHOLD = TimeSpan.FromDays(365);

    public TrustSignalCalculator(
        IOptions<ContactsProviderConfig> config,
        ILogger<TrustSignalCalculator> logger)
    {
        _config = config?.Value ?? throw new ArgumentNullException(nameof(config));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    /// <summary>
    /// Calculates trust signal for a contact based on multiple factors
    /// </summary>
    public async Task<Result<TrustSignal>> CalculateTrustSignalAsync(
        Contact contact,
        ContactInteractionHistory? interactionHistory = null,
        CancellationToken cancellationToken = default)
    {
        if (contact == null)
            return Result<TrustSignal>.Failure(new ValidationError("Contact cannot be null"));

        if (string.IsNullOrWhiteSpace(contact.PrimaryEmail))
            return Result<TrustSignal>.Failure(new ValidationError("Contact must have a primary email"));

        try
        {
            _logger.LogDebug("Calculating trust signal for contact: {ContactId}", contact.Id);

            var factors = await AnalyzeTrustFactorsAsync(contact, interactionHistory, cancellationToken);
            var finalScore = CalculateWeightedScore(factors);
            var strength = DetermineRelationshipStrength(finalScore);
            var confidence = CalculateConfidenceLevel(factors);
            var justification = GenerateJustification(factors, strength);

            var trustSignal = new TrustSignal
            {
                ContactId = contact.Id,
                Strength = strength,
                Score = finalScore,
                LastInteractionDate = interactionHistory?.LastInteractionDate,
                Justification = [justification],
                ComputedAt = DateTime.UtcNow,
                InteractionCount = interactionHistory?.EmailCount ?? 0,
                RecencyScore = factors.RecencyScore,
                FrequencyScore = factors.EmailInteractionScore
            };

            _logger.LogInformation(
                "Trust signal calculated for {ContactId}: {Strength} ({Score:F2}) with {Confidence:F1}% confidence",
                contact.Id, strength, finalScore, trustSignal.ConfidenceLevel * 100);

            return Result<TrustSignal>.Success(trustSignal);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error calculating trust signal for contact: {ContactId}", contact.Id);
            return Result<TrustSignal>.Failure(ex.ToProviderError("Trust signal calculation failed"));
        }
    }

    /// <summary>
    /// Batch calculates trust signals for multiple contacts efficiently using parallel processing
    /// </summary>
    public async Task<Result<Dictionary<string, TrustSignal>>> CalculateBatchTrustSignalsAsync(
        IEnumerable<Contact> contacts,
        Dictionary<string, ContactInteractionHistory>? interactionHistories = null,
        CancellationToken cancellationToken = default)
    {
        if (contacts == null)
            return Result<Dictionary<string, TrustSignal>>.Success(new Dictionary<string, TrustSignal>());

        var contactList = contacts.ToList();
        if (!contactList.Any())
            return Result<Dictionary<string, TrustSignal>>.Success(new Dictionary<string, TrustSignal>());

        // Thread-safe collections for parallel processing - declared outside try block for catch block access
        var results = new System.Collections.Concurrent.ConcurrentDictionary<string, TrustSignal>();
        var successCount = 0;
        var totalCount = contactList.Count;

        try
        {

            _logger.LogInformation("Starting parallel batch trust signal calculation for {TotalCount} contacts with max concurrency of 10", totalCount);

            // Use Parallel.ForEachAsync with concurrency limit
            var parallelOptions = new ParallelOptions
            {
                MaxDegreeOfParallelism = 10, // Limit to 10 parallel operations
                CancellationToken = cancellationToken
            };

            await Parallel.ForEachAsync(contactList, parallelOptions, async (contact, ct) =>
            {
                try
                {
                    // Create a linked cancellation token that combines the main token with the parallel operation token
                    using var linkedCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, ct);
                    var linkedToken = linkedCts.Token;

                    var history = interactionHistories?.GetValueOrDefault(contact.Id);
                    var result = await CalculateTrustSignalAsync(contact, history, linkedToken);

                    if (result.IsSuccess)
                    {
                        results.TryAdd(contact.Id, result.Value);
                        Interlocked.Increment(ref successCount);
                    }
                    else
                    {
                        _logger.LogWarning("Failed to calculate trust signal for contact {ContactId}: {Error}",
                            contact.Id, result.Error.Message);
                    }
                }
                catch (OperationCanceledException) when (ct.IsCancellationRequested || cancellationToken.IsCancellationRequested)
                {
                    // Expected when cancellation is requested - don't log as error
                    _logger.LogDebug("Trust signal calculation cancelled for contact {ContactId}", contact.Id);
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Unexpected error calculating trust signal for contact {ContactId}", contact.Id);
                }
            });

            _logger.LogInformation("Parallel batch calculated trust signals for {SuccessCount} out of {TotalCount} contacts",
                successCount, totalCount);

            // Convert ConcurrentDictionary to regular Dictionary for return
            return Result<Dictionary<string, TrustSignal>>.Success(
                new Dictionary<string, TrustSignal>(results));
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            _logger.LogInformation("Batch trust signal calculation was cancelled after processing {Count} contacts", results.Count);
            // Return partial results as success - graceful degradation for batch operations
            return Result<Dictionary<string, TrustSignal>>.Success(
                new Dictionary<string, TrustSignal>(results));
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error in parallel batch trust signal calculation");
            return Result<Dictionary<string, TrustSignal>>.Failure(
                ex.ToProviderError("Parallel batch trust signal calculation failed"));
        }
    }

    // Private helper methods

    private async Task<TrustFactors> AnalyzeTrustFactorsAsync(
        Contact contact,
        ContactInteractionHistory? interactionHistory,
        CancellationToken cancellationToken)
    {
        await Task.CompletedTask; // For future async operations

        return new TrustFactors
        {
            EmailInteractionScore = CalculateEmailInteractionScore(interactionHistory),
            ContactCompletenessScore = CalculateContactCompletenessScore(contact),
            OrganizationScore = CalculateOrganizationScore(contact),
            PhoneVerificationScore = CalculatePhoneVerificationScore(contact),
            RecencyScore = CalculateRecencyScore(interactionHistory),
            PlatformPresenceScore = CalculatePlatformPresenceScore(contact)
        };
    }

    private double CalculateEmailInteractionScore(ContactInteractionHistory? history)
    {
        if (history == null)
            return 0.0;

        var score = 0.0;

        // Frequency factor (0.4 weight)
        var frequencyScore = Math.Min(history.EmailCount / 50.0, 1.0); // Cap at 50 emails
        score += frequencyScore * 0.4;

        // Bidirectional communication factor (0.4 weight)  
        if (history.EmailCount > 0)
        {
            var bidirectionalScore = Math.Min(
                (history.SentEmailCount + history.ReceivedEmailCount) / (double)history.EmailCount, 1.0);
            score += bidirectionalScore * 0.4;
        }

        // Reply rate factor (0.2 weight)
        if (history.SentEmailCount > 0)
        {
            var replyRate = Math.Min(history.ReceivedEmailCount / (double)history.SentEmailCount, 1.0);
            score += replyRate * 0.2;
        }

        return Math.Min(score, 1.0);
    }

    private double CalculateContactCompletenessScore(Contact contact)
    {
        var score = 0.0;
        var maxFields = 6;

        // Email presence (required, always 1)
        score += 1.0 / maxFields;

        // Name completeness
        if (!string.IsNullOrWhiteSpace(contact.DisplayName))
            score += 1.0 / maxFields;

        // Phone number presence
        if (contact.PhoneNumbers?.Any() == true)
            score += 1.0 / maxFields;

        // Organization info
        if (!string.IsNullOrWhiteSpace(contact.OrganizationName))
            score += 1.0 / maxFields;

        // Job title
        if (!string.IsNullOrWhiteSpace(contact.OrganizationTitle))
            score += 1.0 / maxFields;

        // Photo presence
        if (!string.IsNullOrWhiteSpace(contact.PhotoUrl))
            score += 1.0 / maxFields;

        return score;
    }

    private double CalculateOrganizationScore(Contact contact)
    {
        var score = 0.0;

        // Known organization
        if (!string.IsNullOrWhiteSpace(contact.OrganizationName))
        {
            score += 0.5;

            // Business domain indicators
            var email = contact.PrimaryEmail.ToLowerInvariant();
            if (!email.Contains("gmail.") && !email.Contains("yahoo.") &&
                !email.Contains("hotmail.") && !email.Contains("outlook."))
            {
                score += 0.3; // Likely business domain
            }

            // Job title indicates professional contact
            if (!string.IsNullOrWhiteSpace(contact.OrganizationTitle))
            {
                score += 0.2;
            }
        }

        return Math.Min(score, 1.0);
    }

    private double CalculatePhoneVerificationScore(Contact contact)
    {
        if (contact.PhoneNumbers?.Any() != true)
            return 0.0;

        var score = 0.5; // Base score for having any phone number

        // Multiple phone numbers increase trust
        if (contact.PhoneNumbers.Count > 1)
            score += 0.3;

        // E.164 formatted numbers (properly normalized) increase trust
        var e164Count = contact.PhoneNumbers.Count(p => p.StartsWith("+"));
        if (e164Count > 0)
        {
            score += (e164Count / (double)contact.PhoneNumbers.Count) * 0.2;
        }

        return Math.Min(score, 1.0);
    }

    private double CalculateRecencyScore(ContactInteractionHistory? history)
    {
        if (history?.LastInteractionDate == null)
            return 0.0;

        var daysSinceLastInteraction = (DateTime.UtcNow - history.LastInteractionDate.Value).TotalDays;

        if (daysSinceLastInteraction <= RECENT_INTERACTION_WINDOW.TotalDays)
            return 1.0; // Very recent

        if (daysSinceLastInteraction <= STALE_INTERACTION_THRESHOLD.TotalDays)
        {
            // Linear decay over one year
            var decayFactor = 1.0 - (daysSinceLastInteraction - RECENT_INTERACTION_WINDOW.TotalDays) /
                             (STALE_INTERACTION_THRESHOLD.TotalDays - RECENT_INTERACTION_WINDOW.TotalDays);
            return Math.Max(decayFactor, 0.0);
        }

        return 0.1; // Old but not zero (still some historical value)
    }

    private double CalculatePlatformPresenceScore(Contact contact)
    {
        if (contact.SourceIdentities?.Any() != true)
            return 0.0;

        var score = 0.5; // Base score for being in any contact system

        // Multiple platform presence increases trust
        var uniqueSources = contact.SourceIdentities
            .Select(si => si.SourceType)
            .Distinct()
            .Count();

        if (uniqueSources > 1)
        {
            score += Math.Min(uniqueSources / 3.0, 0.5); // Cap bonus at 0.5
        }

        return Math.Min(score, 1.0);
    }

    private double CalculateWeightedScore(TrustFactors factors)
    {
        return factors.EmailInteractionScore * EMAIL_INTERACTION_WEIGHT +
               factors.ContactCompletenessScore * CONTACT_COMPLETENESS_WEIGHT +
               factors.OrganizationScore * ORGANIZATION_WEIGHT +
               factors.PhoneVerificationScore * PHONE_VERIFICATION_WEIGHT +
               factors.RecencyScore * RECENCY_WEIGHT +
               factors.PlatformPresenceScore * PLATFORM_PRESENCE_WEIGHT;
    }

    private RelationshipStrength DetermineRelationshipStrength(double score)
    {
        return score switch
        {
            >= TRUSTED_THRESHOLD => RelationshipStrength.Trusted,
            >= STRONG_THRESHOLD => RelationshipStrength.Strong,
            >= MODERATE_THRESHOLD => RelationshipStrength.Moderate,
            >= WEAK_THRESHOLD => RelationshipStrength.Weak,
            _ => RelationshipStrength.None
        };
    }

    private double CalculateConfidenceLevel(TrustFactors factors)
    {
        // Confidence based on how many factors we have data for
        var factorCount = 0;
        var factorSum = 0.0;

        if (factors.EmailInteractionScore > 0) { factorCount++; factorSum += factors.EmailInteractionScore; }
        if (factors.ContactCompletenessScore > 0) { factorCount++; factorSum += factors.ContactCompletenessScore; }
        if (factors.OrganizationScore > 0) { factorCount++; factorSum += factors.OrganizationScore; }
        if (factors.PhoneVerificationScore > 0) { factorCount++; factorSum += factors.PhoneVerificationScore; }
        if (factors.RecencyScore > 0) { factorCount++; factorSum += factors.RecencyScore; }
        if (factors.PlatformPresenceScore > 0) { factorCount++; factorSum += factors.PlatformPresenceScore; }

        if (factorCount == 0)
            return 0.1; // Minimum confidence

        // Confidence increases with more factors and higher average scores
        var averageScore = factorSum / factorCount;
        var dataCompletenessScore = factorCount / 6.0; // 6 total factors

        return Math.Min((averageScore * 0.7) + (dataCompletenessScore * 0.3), 0.95); // Cap at 95%
    }

    private string GenerateJustification(TrustFactors factors, RelationshipStrength strength)
    {
        var reasons = new List<string>();

        if (factors.EmailInteractionScore > 0.5)
            reasons.Add("Regular email communication");
        else if (factors.EmailInteractionScore > 0.2)
            reasons.Add("Some email history");

        if (factors.ContactCompletenessScore > 0.7)
            reasons.Add("Complete contact information");
        else if (factors.ContactCompletenessScore > 0.4)
            reasons.Add("Basic contact details");

        if (factors.OrganizationScore > 0.5)
            reasons.Add("Professional/business contact");

        if (factors.PhoneVerificationScore > 0.5)
            reasons.Add("Verified phone number");

        if (factors.RecencyScore > 0.7)
            reasons.Add("Recent interactions");
        else if (factors.RecencyScore > 0.3)
            reasons.Add("Historical contact");

        if (factors.PlatformPresenceScore > 0.5)
            reasons.Add("Multi-platform presence");

        if (!reasons.Any())
            reasons.Add("Limited contact information available");

        var justification = string.Join(", ", reasons);
        return $"{strength} relationship based on: {justification}";
    }
}


