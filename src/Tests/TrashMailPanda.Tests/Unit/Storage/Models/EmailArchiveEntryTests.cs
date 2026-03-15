using System.ComponentModel.DataAnnotations;
using TrashMailPanda.Providers.Storage.Models;
using Xunit;

namespace TrashMailPanda.Tests.Unit.Storage.Models;

[Trait("Category", "Unit")]
public class EmailArchiveEntryTests
{
    [Fact]
    public void EmailArchiveEntry_ValidModel_PassesValidation()
    {
        // Arrange
        var entry = CreateValidArchiveEntry();

        // Act
        var validationResults = new List<ValidationResult>();
        var isValid = Validator.TryValidateObject(
            entry,
            new ValidationContext(entry),
            validationResults,
            validateAllProperties: true);

        // Assert
        Assert.True(isValid);
        Assert.Empty(validationResults);
    }

    [Fact]
    public void EmailArchiveEntry_MissingEmailId_FailsValidation()
    {
        // Arrange
        var entry = new EmailArchiveEntry
        {
            EmailId = "", // Invalid - required
            ThreadId = "thread-123",
            ProviderType = "Gmail",
            HeadersJson = "{}",
            BodyText = "Test email content",
            BodyHtml = null,
            FolderTagsJson = "[\"INBOX\"]",
            SourceFolder = "inbox",
            SizeEstimate = 1024,
            ReceivedDate = DateTime.UtcNow,
            ArchivedAt = DateTime.UtcNow,
            Snippet = "Test snippet",
            UserCorrected = 0
        };

        // Act
        var validationResults = new List<ValidationResult>();
        var isValid = Validator.TryValidateObject(
            entry,
            new ValidationContext(entry),
            validationResults,
            validateAllProperties: true);

        // Assert
        Assert.False(isValid);
        Assert.Contains(validationResults, vr => vr.MemberNames.Contains("EmailId"));
    }

    [Fact]
    public void EmailArchiveEntry_MissingProviderType_FailsValidation()
    {
        // Arrange
        var entry = new EmailArchiveEntry
        {
            EmailId = "test-email-123",
            ThreadId = "thread-123",
            ProviderType = "", // Invalid - required
            HeadersJson = "{}",
            BodyText = "Test email content",
            BodyHtml = null,
            FolderTagsJson = "[\"INBOX\"]",
            SourceFolder = "inbox",
            SizeEstimate = 1024,
            ReceivedDate = DateTime.UtcNow,
            ArchivedAt = DateTime.UtcNow,
            Snippet = "Test snippet",
            UserCorrected = 0
        };

        // Act
        var validationResults = new List<ValidationResult>();
        var isValid = Validator.TryValidateObject(
            entry,
            new ValidationContext(entry),
            validationResults,
            validateAllProperties: true);

        // Assert
        Assert.False(isValid);
        Assert.Contains(validationResults, vr => vr.MemberNames.Contains("ProviderType"));
    }

    [Fact]
    public void EmailArchiveEntry_InvalidSizeEstimate_FailsValidation()
    {
        // Arrange
        var entry = new EmailArchiveEntry
        {
            EmailId = "test-email-123",
            ThreadId = "thread-123",
            ProviderType = "Gmail",
            HeadersJson = "{}",
            BodyText = "Test email content",
            BodyHtml = null,
            FolderTagsJson = "[\"INBOX\"]",
            SourceFolder = "inbox",
            SizeEstimate = 0, // Invalid - must be >= 1
            ReceivedDate = DateTime.UtcNow,
            ArchivedAt = DateTime.UtcNow,
            Snippet = "Test snippet",
            UserCorrected = 0
        };

        // Act
        var validationResults = new List<ValidationResult>();
        var isValid = Validator.TryValidateObject(
            entry,
            new ValidationContext(entry),
            validationResults,
            validateAllProperties: true);

        // Assert
        Assert.False(isValid);
        Assert.Contains(validationResults, vr => vr.MemberNames.Contains("SizeEstimate"));
    }

    [Fact]
    public void EmailArchiveEntry_WithBodyText_PassesValidation()
    {
        // Arrange - BodyText provided, BodyHtml null
        var entry = new EmailArchiveEntry
        {
            EmailId = "test-with-text",
            ThreadId = "thread-123",
            ProviderType = "Gmail",
            HeadersJson = "{}",
            BodyText = "Plain text email body",
            BodyHtml = null,
            FolderTagsJson = "[\"INBOX\"]",
            SourceFolder = "inbox",
            SizeEstimate = 1024,
            ReceivedDate = DateTime.UtcNow,
            ArchivedAt = DateTime.UtcNow,
            Snippet = "Test snippet",
            UserCorrected = 0
        };

        // Act
        var validationResults = new List<ValidationResult>();
        var isValid = Validator.TryValidateObject(
            entry,
            new ValidationContext(entry),
            validationResults,
            validateAllProperties: true);

        // Assert
        Assert.True(isValid);
    }

    [Fact]
    public void EmailArchiveEntry_WithBodyHtml_PassesValidation()
    {
        // Arrange - BodyHtml provided, BodyText null
        var entry = new EmailArchiveEntry
        {
            EmailId = "test-with-html",
            ThreadId = "thread-123",
            ProviderType = "Gmail",
            HeadersJson = "{}",
            BodyText = null,
            BodyHtml = "<html><body>HTML email body</body></html>",
            FolderTagsJson = "[\"INBOX\"]",
            SourceFolder = "inbox",
            SizeEstimate = 2048,
            ReceivedDate = DateTime.UtcNow,
            ArchivedAt = DateTime.UtcNow,
            Snippet = "HTML email snippet",
            UserCorrected = 0
        };

        // Act
        var validationResults = new List<ValidationResult>();
        var isValid = Validator.TryValidateObject(
            entry,
            new ValidationContext(entry),
            validationResults,
            validateAllProperties: true);

        // Assert
        Assert.True(isValid);
    }

    [Fact]
    public void EmailArchiveEntry_WithBothBodyTextAndHtml_PassesValidation()
    {
        // Arrange - Both BodyText and BodyHtml provided
        var entry = new EmailArchiveEntry
        {
            EmailId = "test-with-both",
            ThreadId = "thread-123",
            ProviderType = "Gmail",
            HeadersJson = "{}",
            BodyText = "Plain text version",
            BodyHtml = "<html><body>HTML version</body></html>",
            FolderTagsJson = "[\"INBOX\"]",
            SourceFolder = "inbox",
            SizeEstimate = 3072,
            ReceivedDate = DateTime.UtcNow,
            ArchivedAt = DateTime.UtcNow,
            Snippet = "Test snippet",
            UserCorrected = 0
        };

        // Act
        var validationResults = new List<ValidationResult>();
        var isValid = Validator.TryValidateObject(
            entry,
            new ValidationContext(entry),
            validationResults,
            validateAllProperties: true);

        // Assert
        Assert.True(isValid);
    }

    [Fact]
    public void EmailArchiveEntry_NullableFieldsCanBeNull()
    {
        // Arrange - ThreadId, Snippet, and one body field can be null
        var entry = new EmailArchiveEntry
        {
            EmailId = "test-nullable",
            ThreadId = null, // Nullable
            ProviderType = "Gmail",
            HeadersJson = "{}",
            BodyText = "Plain text content",
            BodyHtml = null, // Nullable (as long as BodyText is provided)
            FolderTagsJson = "[\"INBOX\"]",
            SourceFolder = "inbox",
            SizeEstimate = 1024,
            ReceivedDate = DateTime.UtcNow,
            ArchivedAt = DateTime.UtcNow,
            Snippet = null, // Nullable
            UserCorrected = 0
        };

        // Act
        var validationResults = new List<ValidationResult>();
        var isValid = Validator.TryValidateObject(
            entry,
            new ValidationContext(entry),
            validationResults,
            validateAllProperties: true);

        // Assert
        Assert.True(isValid);
    }

    [Fact]
    public void EmailArchiveEntry_InvalidUserCorrectedValue_FailsValidation()
    {
        // Arrange
        var entry = new EmailArchiveEntry
        {
            EmailId = "test-invalid-corrected",
            ThreadId = "thread-123",
            ProviderType = "Gmail",
            HeadersJson = "{}",
            BodyText = "Test email content",
            BodyHtml = null,
            FolderTagsJson = "[\"INBOX\"]",
            SourceFolder = "inbox",
            SizeEstimate = 1024,
            ReceivedDate = DateTime.UtcNow,
            ArchivedAt = DateTime.UtcNow,
            Snippet = "Test snippet",
            UserCorrected = 5 // Invalid - must be 0 or 1
        };

        // Act
        var validationResults = new List<ValidationResult>();
        var isValid = Validator.TryValidateObject(
            entry,
            new ValidationContext(entry),
            validationResults,
            validateAllProperties: true);

        // Assert
        Assert.False(isValid);
        Assert.Contains(validationResults, vr => vr.MemberNames.Contains("UserCorrected"));
    }

    private EmailArchiveEntry CreateValidArchiveEntry()
    {
        return new EmailArchiveEntry
        {
            EmailId = "test-archive-123",
            ThreadId = "thread-456",
            ProviderType = "Gmail",
            HeadersJson = "{\"From\":\"test@example.com\",\"Subject\":\"Test Email\"}",
            BodyText = "This is the plain text email body.",
            BodyHtml = "<html><body><p>This is the HTML email body.</p></body></html>",
            FolderTagsJson = "[\"INBOX\",\"Important\"]",
            SourceFolder = "inbox",
            SizeEstimate = 2048,
            ReceivedDate = DateTime.UtcNow.AddDays(-1),
            ArchivedAt = DateTime.UtcNow,
            Snippet = "This is the plain text email body...",
            UserCorrected = 0
        };
    }
}
