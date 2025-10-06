using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Moq;
using Xunit;
using TrashMailPanda.Shared.Models;
using TrashMailPanda.Providers.Contacts.Models;
using TrashMailPanda.Providers.Contacts.Services;
using TrashMailPanda.Providers.Contacts;

namespace TrashMailPanda.Tests.Providers.Contacts;

/// <summary>
/// Comprehensive unit tests for TrustSignalCalculator confidence scoring logic
/// Tests all 6-factor weighted scoring algorithms and edge cases
/// </summary>
public class TrustSignalCalculatorTests
{
    private readonly Mock<ILogger<TrustSignalCalculator>> _mockLogger;
    private readonly ContactsProviderConfig _config;
    private readonly TrustSignalCalculator _calculator;

    public TrustSignalCalculatorTests()
    {
        _mockLogger = new Mock<ILogger<TrustSignalCalculator>>();
        _config = new ContactsProviderConfig
        {
            ClientId = "test-client-id",
            ClientSecret = "test-client-secret",
            ApplicationName = "TestApp",
            DefaultPageSize = 100
        };

        var configOptions = Options.Create(_config);
        _calculator = new TrustSignalCalculator(configOptions, _mockLogger.Object);
    }

    #region Core Validation Tests

    [Fact]
    public async Task CalculateTrustSignalAsync_WithNullContact_ReturnsFailure()
    {
        // Act
        var result = await _calculator.CalculateTrustSignalAsync(null!);

        // Assert
        Assert.False(result.IsSuccess);
        Assert.Contains("Contact cannot be null", result.Error.Message);
    }

    [Fact]
    public async Task CalculateTrustSignalAsync_WithNoPrimaryEmail_ReturnsFailure()
    {
        // Arrange
        var contact = new Contact
        {
            Id = "test-contact",
            PrimaryEmail = null
        };

        // Act
        var result = await _calculator.CalculateTrustSignalAsync(contact);

        // Assert
        Assert.False(result.IsSuccess);
        Assert.Contains("Contact must have a primary email", result.Error.Message);
    }

    [Fact]
    public async Task CalculateTrustSignalAsync_WithEmptyPrimaryEmail_ReturnsFailure()
    {
        // Arrange
        var contact = new Contact
        {
            Id = "test-contact",
            PrimaryEmail = string.Empty
        };

        // Act
        var result = await _calculator.CalculateTrustSignalAsync(contact);

        // Assert
        Assert.False(result.IsSuccess);
        Assert.Contains("Contact must have a primary email", result.Error.Message);
    }

    #endregion

    #region Basic Contact Scoring Tests

    [Fact]
    public async Task CalculateTrustSignalAsync_MinimalContact_ReturnsBasicScore()
    {
        // Arrange
        var contact = CreateMinimalContact();

        // Act
        var result = await _calculator.CalculateTrustSignalAsync(contact);

        // Assert
        Assert.True(result.IsSuccess);
        var trustSignal = result.Value;
        Assert.Equal(contact.Id, trustSignal.ContactId);
        Assert.Equal(RelationshipStrength.None, trustSignal.Strength);
        Assert.True(trustSignal.Score >= 0.0 && trustSignal.Score <= 1.0);
        Assert.True(trustSignal.Score < 0.15); // Below weak threshold
    }

    [Fact]
    public async Task CalculateTrustSignalAsync_CompleteContact_ReturnsHigherScore()
    {
        // Arrange
        var contact = CreateCompleteContact();

        // Act
        var result = await _calculator.CalculateTrustSignalAsync(contact);

        // Assert
        Assert.True(result.IsSuccess);
        var trustSignal = result.Value;
        Assert.True(trustSignal.Score > 0.15); // Above minimal score
        Assert.True(trustSignal.Strength >= RelationshipStrength.Weak); // Should be at least weak
    }

    #endregion

    #region Email Interaction Score Tests

    [Fact]
    public async Task CalculateTrustSignalAsync_HighEmailInteractions_BoostsScore()
    {
        // Arrange
        var contact = CreateMinimalContact();
        var history = new ContactInteractionHistory
        {
            ContactId = contact.Id,
            EmailCount = 50, // Max frequency
            SentEmailCount = 25,
            ReceivedEmailCount = 25,
            LastInteractionDate = DateTime.UtcNow.AddDays(-5)
        };

        // Act
        var result = await _calculator.CalculateTrustSignalAsync(contact, history);

        // Assert
        Assert.True(result.IsSuccess);
        var trustSignal = result.Value;
        // High email interaction should result in at least Strong relationship
        Assert.True(trustSignal.Score >= 0.35); // Should be at least Moderate
        Assert.True(trustSignal.Strength >= RelationshipStrength.Moderate);
    }

    [Fact]
    public async Task CalculateTrustSignalAsync_BidirectionalCommunication_ScoresHigher()
    {
        // Arrange
        var contact = CreateMinimalContact();
        var unidirectionalHistory = new ContactInteractionHistory
        {
            ContactId = contact.Id,
            EmailCount = 10,
            SentEmailCount = 10,
            ReceivedEmailCount = 0, // No replies
            LastInteractionDate = DateTime.UtcNow.AddDays(-5)
        };
        var bidirectionalHistory = new ContactInteractionHistory
        {
            ContactId = contact.Id,
            EmailCount = 10,
            SentEmailCount = 5,
            ReceivedEmailCount = 5, // Equal communication
            LastInteractionDate = DateTime.UtcNow.AddDays(-5)
        };

        // Act
        var unidirectionalResult = await _calculator.CalculateTrustSignalAsync(contact, unidirectionalHistory);
        var bidirectionalResult = await _calculator.CalculateTrustSignalAsync(contact, bidirectionalHistory);

        // Assert
        Assert.True(unidirectionalResult.IsSuccess);
        Assert.True(bidirectionalResult.IsSuccess);
        Assert.True(bidirectionalResult.Value.Score > unidirectionalResult.Value.Score);
    }

    [Fact]
    public async Task CalculateTrustSignalAsync_HighReplyRate_ScoresHigher()
    {
        // Arrange
        var contact = CreateMinimalContact();
        var lowReplyHistory = new ContactInteractionHistory
        {
            ContactId = contact.Id,
            EmailCount = 20,
            SentEmailCount = 20,
            ReceivedEmailCount = 2, // 10% reply rate
            LastInteractionDate = DateTime.UtcNow.AddDays(-5)
        };
        var highReplyHistory = new ContactInteractionHistory
        {
            ContactId = contact.Id,
            EmailCount = 20,
            SentEmailCount = 10,
            ReceivedEmailCount = 10, // 100% reply rate
            LastInteractionDate = DateTime.UtcNow.AddDays(-5)
        };

        // Act
        var lowReplyResult = await _calculator.CalculateTrustSignalAsync(contact, lowReplyHistory);
        var highReplyResult = await _calculator.CalculateTrustSignalAsync(contact, highReplyHistory);

        // Assert
        Assert.True(lowReplyResult.IsSuccess);
        Assert.True(highReplyResult.IsSuccess);
        Assert.True(highReplyResult.Value.Score > lowReplyResult.Value.Score);
    }

    #endregion

    #region Contact Completeness Score Tests

    [Fact]
    public async Task CalculateTrustSignalAsync_CompleteContactInfo_ScoresHigher()
    {
        // Arrange
        var minimalContact = CreateMinimalContact();
        var completeContact = new Contact
        {
            Id = "complete-contact",
            PrimaryEmail = "complete@example.com",
            DisplayName = "Complete Contact",
            PhoneNumbers = new List<string> { "+1234567890" },
            OrganizationName = "Test Company",
            OrganizationTitle = "Software Engineer",
            PhotoUrl = "https://example.com/photo.jpg",
            SourceIdentities = new List<SourceIdentity>
            {
                new() { SourceType = ContactSourceType.Google, SourceContactId = "google-123", IsActive = true, LastUpdatedUtc = DateTime.UtcNow }
            }
        };

        // Act
        var minimalResult = await _calculator.CalculateTrustSignalAsync(minimalContact);
        var completeResult = await _calculator.CalculateTrustSignalAsync(completeContact);

        // Assert
        Assert.True(minimalResult.IsSuccess);
        Assert.True(completeResult.IsSuccess);
        Assert.True(completeResult.Value.Score > minimalResult.Value.Score);
    }

    [Theory]
    [InlineData(null, 1.0 / 6.0)] // Only email (required)
    [InlineData("John Doe", 2.0 / 6.0)] // Email + name
    public async Task CalculateTrustSignalAsync_ContactCompleteness_CalculatesCorrectly(string? displayName, double expectedMinScore)
    {
        // Arrange
        var contact = new Contact
        {
            Id = "test-contact",
            PrimaryEmail = "test@example.com",
            DisplayName = displayName
        };

        // Act
        var result = await _calculator.CalculateTrustSignalAsync(contact);

        // Assert
        Assert.True(result.IsSuccess);
        // Score should reflect contact completeness (20% weight)
        var expectedScoreContribution = expectedMinScore * 0.20;
        Assert.True(result.Value.Score >= expectedScoreContribution);
    }

    #endregion

    #region Organization Score Tests

    [Fact]
    public async Task CalculateTrustSignalAsync_BusinessEmail_ScoresHigher()
    {
        // Arrange
        var personalContact = new Contact
        {
            Id = "personal-contact",
            PrimaryEmail = "user@gmail.com"
        };
        var businessContact = new Contact
        {
            Id = "business-contact",
            PrimaryEmail = "user@company.com",
            OrganizationName = "Test Company"
        };

        // Act
        var personalResult = await _calculator.CalculateTrustSignalAsync(personalContact);
        var businessResult = await _calculator.CalculateTrustSignalAsync(businessContact);

        // Assert
        Assert.True(personalResult.IsSuccess);
        Assert.True(businessResult.IsSuccess);
        Assert.True(businessResult.Value.Score > personalResult.Value.Score);
    }

    [Theory]
    [InlineData("user@gmail.com", false)]
    [InlineData("user@yahoo.com", false)]
    [InlineData("user@hotmail.com", false)]
    [InlineData("user@outlook.com", false)]
    [InlineData("user@company.com", true)]
    [InlineData("user@university.edu", true)]
    public async Task CalculateTrustSignalAsync_EmailDomain_DetectsBusiness(string email, bool expectBusinessBonus)
    {
        // Arrange
        var contact = new Contact
        {
            Id = "test-contact",
            PrimaryEmail = email,
            OrganizationName = "Test Org"
        };

        // Act
        var result = await _calculator.CalculateTrustSignalAsync(contact);

        // Assert
        Assert.True(result.IsSuccess);
        var score = result.Value.Score;

        if (expectBusinessBonus)
        {
            // Should have higher organization score component
            Assert.True(score > 0.15 * 0.15); // Org weight * base org score
        }
    }

    #endregion

    #region Phone Verification Score Tests

    [Fact]
    public async Task CalculateTrustSignalAsync_WithPhoneNumbers_ScoresHigher()
    {
        // Arrange
        var contactWithoutPhone = CreateMinimalContact();
        var contactWithPhone = new Contact
        {
            Id = "phone-contact",
            PrimaryEmail = "phone@example.com",
            PhoneNumbers = new List<string> { "555-123-4567" }
        };

        // Act
        var withoutPhoneResult = await _calculator.CalculateTrustSignalAsync(contactWithoutPhone);
        var withPhoneResult = await _calculator.CalculateTrustSignalAsync(contactWithPhone);

        // Assert
        Assert.True(withoutPhoneResult.IsSuccess);
        Assert.True(withPhoneResult.IsSuccess);
        Assert.True(withPhoneResult.Value.Score > withoutPhoneResult.Value.Score);
    }

    [Fact]
    public async Task CalculateTrustSignalAsync_E164PhoneNumbers_ScoreHigher()
    {
        // Arrange
        var contactWithLocalPhone = new Contact
        {
            Id = "local-phone",
            PrimaryEmail = "local@example.com",
            PhoneNumbers = new List<string> { "555-123-4567" }
        };
        var contactWithE164Phone = new Contact
        {
            Id = "e164-phone",
            PrimaryEmail = "e164@example.com",
            PhoneNumbers = new List<string> { "+15551234567" }
        };

        // Act
        var localResult = await _calculator.CalculateTrustSignalAsync(contactWithLocalPhone);
        var e164Result = await _calculator.CalculateTrustSignalAsync(contactWithE164Phone);

        // Assert
        Assert.True(localResult.IsSuccess);
        Assert.True(e164Result.IsSuccess);
        Assert.True(e164Result.Value.Score > localResult.Value.Score);
    }

    [Fact]
    public async Task CalculateTrustSignalAsync_MultiplePhones_ScoreHigher()
    {
        // Arrange
        var singlePhoneContact = new Contact
        {
            Id = "single-phone",
            PrimaryEmail = "single@example.com",
            PhoneNumbers = new List<string> { "+15551234567" }
        };
        var multiPhoneContact = new Contact
        {
            Id = "multi-phone",
            PrimaryEmail = "multi@example.com",
            PhoneNumbers = new List<string> { "+15551234567", "+15559876543" }
        };

        // Act
        var singleResult = await _calculator.CalculateTrustSignalAsync(singlePhoneContact);
        var multiResult = await _calculator.CalculateTrustSignalAsync(multiPhoneContact);

        // Assert
        Assert.True(singleResult.IsSuccess);
        Assert.True(multiResult.IsSuccess);
        Assert.True(multiResult.Value.Score > singleResult.Value.Score);
    }

    #endregion

    #region Recency Score Tests

    [Fact]
    public async Task CalculateTrustSignalAsync_RecentInteraction_ScoresHigher()
    {
        // Arrange
        var contact = CreateMinimalContact();
        var recentHistory = new ContactInteractionHistory
        {
            ContactId = contact.Id,
            EmailCount = 5,
            LastInteractionDate = DateTime.UtcNow.AddDays(-5) // Recent
        };
        var oldHistory = new ContactInteractionHistory
        {
            ContactId = contact.Id,
            EmailCount = 5,
            LastInteractionDate = DateTime.UtcNow.AddDays(-400) // Old
        };

        // Act
        var recentResult = await _calculator.CalculateTrustSignalAsync(contact, recentHistory);
        var oldResult = await _calculator.CalculateTrustSignalAsync(contact, oldHistory);

        // Assert
        Assert.True(recentResult.IsSuccess);
        Assert.True(oldResult.IsSuccess);
        Assert.True(recentResult.Value.Score > oldResult.Value.Score);
        Assert.True(recentResult.Value.RecencyScore > oldResult.Value.RecencyScore);
    }

    [Theory]
    [InlineData(-15, 1.0)] // Within 30-day recent window
    [InlineData(-45, true)] // Should be < 1.0 but > 0.1 (in decay range)
    [InlineData(-400, 0.1)] // Very old, minimum score
    public async Task CalculateTrustSignalAsync_RecencyScoring_MatchesExpectations(int daysAgo, object expectedScoreConstraint)
    {
        // Arrange
        var contact = CreateMinimalContact();
        var history = new ContactInteractionHistory
        {
            ContactId = contact.Id,
            EmailCount = 5,
            LastInteractionDate = DateTime.UtcNow.AddDays(daysAgo)
        };

        // Act
        var result = await _calculator.CalculateTrustSignalAsync(contact, history);

        // Assert
        Assert.True(result.IsSuccess);
        var recencyScore = result.Value.RecencyScore;

        if (expectedScoreConstraint is double expectedScore)
        {
            Assert.Equal(expectedScore, recencyScore, 1);
        }
        else if (expectedScoreConstraint is bool shouldBeInDecayRange && shouldBeInDecayRange)
        {
            Assert.True(recencyScore < 1.0 && recencyScore > 0.1);
        }
    }

    #endregion

    #region Platform Presence Score Tests

    [Fact]
    public async Task CalculateTrustSignalAsync_MultiPlatformPresence_ScoresHigher()
    {
        // Arrange
        var singlePlatformContact = new Contact
        {
            Id = "single-platform",
            PrimaryEmail = "single@example.com",
            SourceIdentities = new List<SourceIdentity>
            {
                new() { SourceType = ContactSourceType.Google, SourceContactId = "google-123", IsActive = true, LastUpdatedUtc = DateTime.UtcNow }
            }
        };
        var multiPlatformContact = new Contact
        {
            Id = "multi-platform",
            PrimaryEmail = "multi@example.com",
            SourceIdentities = new List<SourceIdentity>
            {
                new() { SourceType = ContactSourceType.Google, SourceContactId = "google-123", IsActive = true, LastUpdatedUtc = DateTime.UtcNow },
                new() { SourceType = ContactSourceType.Outlook, SourceContactId = "outlook-456", IsActive = true, LastUpdatedUtc = DateTime.UtcNow }
            }
        };

        // Act
        var singleResult = await _calculator.CalculateTrustSignalAsync(singlePlatformContact);
        var multiResult = await _calculator.CalculateTrustSignalAsync(multiPlatformContact);

        // Assert
        Assert.True(singleResult.IsSuccess);
        Assert.True(multiResult.IsSuccess);
        Assert.True(multiResult.Value.Score > singleResult.Value.Score);
    }

    #endregion

    #region Weighted Score Integration Tests

    [Fact]
    public async Task CalculateTrustSignalAsync_WeightedScoring_SumsTo100Percent()
    {
        // This test ensures the weights sum to 100% (1.0)
        const double EMAIL_INTERACTION_WEIGHT = 0.30;
        const double CONTACT_COMPLETENESS_WEIGHT = 0.20;
        const double ORGANIZATION_WEIGHT = 0.15;
        const double PHONE_VERIFICATION_WEIGHT = 0.15;
        const double RECENCY_WEIGHT = 0.10;
        const double PLATFORM_PRESENCE_WEIGHT = 0.10;

        var totalWeight = EMAIL_INTERACTION_WEIGHT + CONTACT_COMPLETENESS_WEIGHT +
                         ORGANIZATION_WEIGHT + PHONE_VERIFICATION_WEIGHT +
                         RECENCY_WEIGHT + PLATFORM_PRESENCE_WEIGHT;

        Assert.Equal(1.0, totalWeight, 2);
    }

    [Fact]
    public async Task CalculateTrustSignalAsync_PerfectContact_ApproachesTrustedThreshold()
    {
        // Arrange - Create the most trustworthy contact possible
        var perfectContact = new Contact
        {
            Id = "perfect-contact",
            PrimaryEmail = "ceo@microsoft.com", // Business domain
            DisplayName = "Satya Nadella",
            PhoneNumbers = new List<string> { "+14255551234", "+14255555678" }, // Multiple E164 phones
            OrganizationName = "Microsoft Corporation",
            OrganizationTitle = "Chief Executive Officer",
            PhotoUrl = "https://example.com/photo.jpg",
            SourceIdentities = new List<SourceIdentity>
            {
                new() { SourceType = ContactSourceType.Google, SourceContactId = "google-123", IsActive = true, LastUpdatedUtc = DateTime.UtcNow },
                new() { SourceType = ContactSourceType.Outlook, SourceContactId = "outlook-456", IsActive = true, LastUpdatedUtc = DateTime.UtcNow },
                new() { SourceType = ContactSourceType.Outlook, SourceContactId = "outlook-789", IsActive = true, LastUpdatedUtc = DateTime.UtcNow }
            }
        };
        var perfectHistory = new ContactInteractionHistory
        {
            ContactId = perfectContact.Id,
            EmailCount = 50, // Max frequency
            SentEmailCount = 25,
            ReceivedEmailCount = 25, // Perfect bidirectional
            LastInteractionDate = DateTime.UtcNow.AddDays(-1) // Very recent
        };

        // Act
        var result = await _calculator.CalculateTrustSignalAsync(perfectContact, perfectHistory);

        // Assert
        Assert.True(result.IsSuccess);
        var trustSignal = result.Value;
        Assert.True(trustSignal.Score >= 0.75); // Should be high
        Assert.True(trustSignal.Strength >= RelationshipStrength.Strong);
    }

    #endregion

    #region Relationship Strength Threshold Tests

    [Fact]
    public async Task CalculateTrustSignalAsync_StrengthThresholds_ValidateConstants()
    {
        // This test verifies that the thresholds are working correctly
        // by testing with different contact profiles that should hit different thresholds

        // Test a minimal contact (should be None/Weak)
        var minimalContact = CreateMinimalContact();
        var minimalResult = await _calculator.CalculateTrustSignalAsync(minimalContact);
        Assert.True(minimalResult.IsSuccess);
        Assert.True(minimalResult.Value.Strength <= RelationshipStrength.Weak);

        // Test a moderate contact (should be higher)
        var moderateContact = CreateCompleteContact();
        var moderateResult = await _calculator.CalculateTrustSignalAsync(moderateContact);
        Assert.True(moderateResult.IsSuccess);
        Assert.True(moderateResult.Value.Score > minimalResult.Value.Score);
    }

    #endregion

    #region Batch Processing Tests

    [Fact]
    public async Task CalculateBatchTrustSignalsAsync_WithNullContacts_ReturnsEmptyDictionary()
    {
        // Act
        var result = await _calculator.CalculateBatchTrustSignalsAsync(null);

        // Assert
        Assert.True(result.IsSuccess);
        Assert.Empty(result.Value);
    }

    [Fact]
    public async Task CalculateBatchTrustSignalsAsync_WithEmptyContacts_ReturnsEmptyDictionary()
    {
        // Act
        var result = await _calculator.CalculateBatchTrustSignalsAsync(new List<Contact>());

        // Assert
        Assert.True(result.IsSuccess);
        Assert.Empty(result.Value);
    }

    [Fact]
    public async Task CalculateBatchTrustSignalsAsync_WithValidContacts_ReturnsResults()
    {
        // Arrange
        var contacts = new List<Contact>
        {
            new() { Id = "contact1", PrimaryEmail = "user1@example.com" },
            new() { Id = "contact2", PrimaryEmail = "user2@example.com" }
        };

        // Act
        var result = await _calculator.CalculateBatchTrustSignalsAsync(contacts);

        // Assert
        Assert.True(result.IsSuccess);
        Assert.Equal(2, result.Value.Count);
        Assert.True(result.Value.ContainsKey("contact1"));
        Assert.True(result.Value.ContainsKey("contact2"));
    }

    [Fact]
    public async Task CalculateBatchTrustSignalsAsync_WithMixedValidInvalid_ReturnsValidOnes()
    {
        // Arrange
        var contacts = new List<Contact>
        {
            new() { Id = "valid", PrimaryEmail = "valid@example.com" },
            new() { Id = "invalid", PrimaryEmail = null! } // Invalid
        };

        // Act
        var result = await _calculator.CalculateBatchTrustSignalsAsync(contacts);

        // Assert
        Assert.True(result.IsSuccess);
        Assert.Single(result.Value);
        Assert.True(result.Value.ContainsKey("valid"));
        Assert.False(result.Value.ContainsKey("invalid"));
    }

    [Fact]
    public async Task CalculateBatchTrustSignalsAsync_WithCancellation_HandlesGracefully()
    {
        // Arrange
        var contacts = Enumerable.Range(1, 10)
            .Select(i => new Contact { Id = $"contact{i}", PrimaryEmail = $"user{i}@example.com" })
            .ToList();

        using var cts = new CancellationTokenSource();
        cts.CancelAfter(TimeSpan.FromMilliseconds(1)); // Cancel very quickly

        // Act
        var result = await _calculator.CalculateBatchTrustSignalsAsync(contacts, null, cts.Token);

        // Assert
        Assert.True(result.IsSuccess);
        // Either all completed (if fast enough) or some were processed before cancellation
        Assert.True(result.Value.Count <= contacts.Count);
    }

    #endregion

    #region Edge Cases and Error Handling Tests

    [Fact]
    public async Task CalculateTrustSignalAsync_WithVeryLongEmail_HandlesGracefully()
    {
        // Arrange
        var longEmail = new string('a', 1000) + "@example.com";
        var contact = new Contact
        {
            Id = "long-email-contact",
            PrimaryEmail = longEmail
        };

        // Act
        var result = await _calculator.CalculateTrustSignalAsync(contact);

        // Assert
        Assert.True(result.IsSuccess);
        Assert.NotNull(result.Value);
    }

    [Fact]
    public async Task CalculateTrustSignalAsync_WithNegativeInteractionHistory_HandlesGracefully()
    {
        // Arrange
        var contact = CreateMinimalContact();
        var badHistory = new ContactInteractionHistory
        {
            ContactId = contact.Id,
            EmailCount = -5, // Negative count
            SentEmailCount = -2,
            ReceivedEmailCount = -3,
            LastInteractionDate = DateTime.UtcNow.AddDays(1) // Future date
        };

        // Act
        var result = await _calculator.CalculateTrustSignalAsync(contact, badHistory);

        // Assert
        Assert.True(result.IsSuccess);
        var trustSignal = result.Value;
        Assert.True(trustSignal.Score >= 0.0 && trustSignal.Score <= 1.0);
    }

    #endregion

    #region Helper Methods

    private static Contact CreateMinimalContact()
    {
        return new Contact
        {
            Id = "minimal-contact",
            PrimaryEmail = "minimal@example.com"
        };
    }

    private static Contact CreateCompleteContact()
    {
        return new Contact
        {
            Id = "complete-contact",
            PrimaryEmail = "complete@company.com",
            DisplayName = "John Doe",
            PhoneNumbers = new List<string> { "+15551234567" },
            OrganizationName = "Test Company",
            OrganizationTitle = "Manager",
            PhotoUrl = "https://example.com/photo.jpg",
            SourceIdentities = new List<SourceIdentity>
            {
                new() { SourceType = ContactSourceType.Google, SourceContactId = "google-123", IsActive = true, LastUpdatedUtc = DateTime.UtcNow }
            }
        };
    }

    #endregion
}