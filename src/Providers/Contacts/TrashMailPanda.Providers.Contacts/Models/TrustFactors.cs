namespace TrashMailPanda.Providers.Contacts.Models;

/// <summary>
/// Container for individual trust calculation factors
/// </summary>
public class TrustFactors
{
    public double EmailInteractionScore { get; set; }
    public double ContactCompletenessScore { get; set; }
    public double OrganizationScore { get; set; }
    public double PhoneVerificationScore { get; set; }
    public double RecencyScore { get; set; }
    public double PlatformPresenceScore { get; set; }
}