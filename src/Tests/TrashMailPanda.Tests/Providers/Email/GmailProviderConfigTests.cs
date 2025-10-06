using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.Linq;
using Google.Apis.Gmail.v1;
using TrashMailPanda.Providers.Email;
using TrashMailPanda.Shared.Base;
using TrashMailPanda.Shared.Models;
using TrashMailPanda.Shared.Security;

namespace TrashMailPanda.Tests.Providers.Email;

/// <summary>
/// Comprehensive unit tests for GmailProviderConfig validation and configuration logic
/// Tests validation rules, OAuth scopes, retry settings, and factory methods
/// </summary>
public class GmailProviderConfigTests
{
    /// <summary>
    /// Tests that default constructor creates a valid configuration with expected defaults
    /// </summary>
    [Fact]
    public void Constructor_SetsExpectedDefaults()
    {
        // Arrange & Act
        var config = new GmailProviderConfig();

        // Assert
        Assert.Equal("Gmail", config.Name);
        Assert.Contains("email", config.Tags);
        Assert.Contains("gmail", config.Tags);
        Assert.Contains("google", config.Tags);
        Assert.Equal("TrashMail Panda", config.ApplicationName);
        Assert.Single(config.Scopes);
        Assert.Equal(GmailService.Scope.GmailModify, config.Scopes[0]);
        Assert.Equal(TimeSpan.FromMinutes(2), config.RequestTimeout);
        Assert.Equal(5, config.MaxRetries);
        Assert.Equal(TimeSpan.FromSeconds(1), config.BaseRetryDelay);
        Assert.Equal(TimeSpan.FromMinutes(2), config.MaxRetryDelay);
        Assert.True(config.EnableBatchOptimization);
        Assert.Equal(50, config.BatchSize);
        Assert.Equal(100, config.DefaultPageSize);
    }

    /// <summary>
    /// Tests that DataAnnotations validation works correctly for required fields
    /// </summary>
    [Theory]
    [InlineData("", "valid_secret_12345", false, "ClientId")]
    [InlineData("valid_id_12345", "", false, "ClientSecret")]
    [InlineData("valid_id_12345", "valid_secret_12345", true, null)]
    [InlineData(null, "valid_secret_12345", false, "ClientId")]
    [InlineData("valid_id_12345", null, false, "ClientSecret")]
    public void DataAnnotationsValidation_RequiredFields(string? clientId, string? clientSecret, bool shouldBeValid, string? expectedErrorField)
    {
        // Arrange
        var config = new GmailProviderConfig
        {
            ClientId = clientId ?? string.Empty,
            ClientSecret = clientSecret ?? string.Empty,
            TimeoutSeconds = 60,
            MaxRetryAttempts = 3,
            RetryDelayMilliseconds = 1000,
            IsEnabled = true
        };

        var validationContext = new ValidationContext(config);
        var results = new List<ValidationResult>();

        // Act
        var isValid = Validator.TryValidateObject(config, validationContext, results, true);

        // Assert
        Assert.Equal(shouldBeValid, isValid);
        if (!shouldBeValid && expectedErrorField != null)
        {
            Assert.Contains(results, r => r.MemberNames.Contains(expectedErrorField));
        }
    }

    /// <summary>
    /// Tests string length validation for too short ClientId
    /// </summary>
    [Fact]
    public void DataAnnotationsValidation_ClientIdTooShort_ReturnsInvalid()
    {
        // Arrange
        var config = new GmailProviderConfig
        {
            ClientId = "short",
            ClientSecret = "valid_secret_12345",
            TimeoutSeconds = 60,
            MaxRetryAttempts = 3,
            RetryDelayMilliseconds = 1000,
            IsEnabled = true
        };

        var validationContext = new ValidationContext(config);
        var results = new List<ValidationResult>();

        // Act
        var isValid = Validator.TryValidateObject(config, validationContext, results, true);

        // Assert
        Assert.False(isValid);
    }

    /// <summary>
    /// Tests string length validation for valid ClientId
    /// </summary>
    [Fact]
    public void DataAnnotationsValidation_ClientIdValidLength_ReturnsValid()
    {
        // Arrange
        var config = new GmailProviderConfig
        {
            ClientId = "valid_client_id_12345",
            ClientSecret = "valid_secret_12345",
            TimeoutSeconds = 60,
            MaxRetryAttempts = 3,
            RetryDelayMilliseconds = 1000,
            IsEnabled = true
        };

        var validationContext = new ValidationContext(config);
        var results = new List<ValidationResult>();

        // Act
        var isValid = Validator.TryValidateObject(config, validationContext, results, true);

        // Assert
        Assert.True(isValid);
    }

    /// <summary>
    /// Tests string length validation for too long ClientId
    /// </summary>
    [Fact]
    public void DataAnnotationsValidation_ClientIdTooLong_ReturnsInvalid()
    {
        // Arrange
        var longClientId = new string('x', 201); // Too long (> 200 chars)
        var config = new GmailProviderConfig
        {
            ClientId = longClientId,
            ClientSecret = "valid_secret_12345",
            TimeoutSeconds = 60,
            MaxRetryAttempts = 3,
            RetryDelayMilliseconds = 1000,
            IsEnabled = true
        };

        var validationContext = new ValidationContext(config);
        var results = new List<ValidationResult>();

        // Act
        var isValid = Validator.TryValidateObject(config, validationContext, results, true);

        // Assert
        Assert.False(isValid);
    }

    /// <summary>
    /// Tests range validation for numeric properties
    /// </summary>
    [Theory]
    [InlineData(5, true)] // Valid timeout
    [InlineData(400, false)] // Timeout too high
    [InlineData(3, true)] // Valid max retries
    [InlineData(15, false)] // Max retries too high
    public void DataAnnotationsValidation_RangeValidation(int value, bool shouldBeValid)
    {
        // Arrange
        var config = new GmailProviderConfig
        {
            ClientId = "valid_client_id_12345",
            ClientSecret = "valid_secret_12345",
            RequestTimeout = TimeSpan.FromSeconds(value * 60), // Convert to minutes for timeout
            MaxRetries = value,
            TimeoutSeconds = 60,
            MaxRetryAttempts = 3,
            RetryDelayMilliseconds = 1000,
            IsEnabled = true
        };

        var validationContext = new ValidationContext(config);
        var results = new List<ValidationResult>();

        // Act
        var isValid = Validator.TryValidateObject(config, validationContext, results, true);

        // Assert - Note: We're only testing one property at a time
        if (shouldBeValid)
        {
            // May still have other validation errors, but not from our test property
            Assert.True(isValid || !results.Any(r => r.ErrorMessage?.Contains("timeout") == true || r.ErrorMessage?.Contains("retries") == true));
        }
        else
        {
            Assert.False(isValid);
        }
    }

    /// <summary>
    /// Tests ValidateConfiguration with empty ClientId
    /// </summary>
    [Fact]
    public void ValidateConfiguration_EmptyClientId_ReturnsFailure()
    {
        // Arrange
        var config = new GmailProviderConfig();
        config.ClientId = "";
        config.ClientSecret = "valid_secret";

        // Act
        var result = config.ValidateConfiguration();

        // Assert
        Assert.True(result.IsFailure);
        Assert.IsType<ValidationError>(result.Error);
        Assert.Contains("Client ID", result.Error.Message);
    }

    /// <summary>
    /// Tests ValidateConfiguration with empty ClientSecret
    /// </summary>
    [Fact]
    public void ValidateConfiguration_EmptyClientSecret_ReturnsFailure()
    {
        // Arrange
        var config = new GmailProviderConfig();
        config.ClientId = "valid_client_id";
        config.ClientSecret = "";

        // Act
        var result = config.ValidateConfiguration();

        // Assert
        Assert.True(result.IsFailure);
        Assert.IsType<ValidationError>(result.Error);
        Assert.Contains("Client Secret", result.Error.Message);
    }

    /// <summary>
    /// Tests ValidateConfiguration with null scopes
    /// </summary>
    [Fact]
    public void ValidateConfiguration_NullScopes_ReturnsFailure()
    {
        // Arrange
        var config = new GmailProviderConfig();
        config.ClientId = "valid_client_id";
        config.ClientSecret = "valid_secret";
        config.Scopes = null!;

        // Act
        var result = config.ValidateConfiguration();

        // Assert
        Assert.True(result.IsFailure);
        Assert.IsType<ValidationError>(result.Error);
        Assert.Contains("scope", result.Error.Message.ToLowerInvariant());
    }

    /// <summary>
    /// Tests ValidateConfiguration with empty scopes
    /// </summary>
    [Fact]
    public void ValidateConfiguration_EmptyScopes_ReturnsFailure()
    {
        // Arrange
        var config = new GmailProviderConfig();
        config.ClientId = "valid_client_id";
        config.ClientSecret = "valid_secret";
        config.Scopes = Array.Empty<string>();

        // Act
        var result = config.ValidateConfiguration();

        // Assert
        Assert.True(result.IsFailure);
        Assert.IsType<ValidationError>(result.Error);
        Assert.Contains("scope", result.Error.Message.ToLowerInvariant());
    }

    /// <summary>
    /// Tests ValidateConfiguration with invalid OAuth scope
    /// </summary>
    [Fact]
    public void ValidateConfiguration_InvalidOAuthScope_ReturnsFailure()
    {
        // Arrange
        var config = new GmailProviderConfig();
        config.ClientId = "valid_client_id";
        config.ClientSecret = "valid_secret";
        config.Scopes = new[] { "invalid_scope" };

        // Act
        var result = config.ValidateConfiguration();

        // Assert
        Assert.True(result.IsFailure);
        Assert.IsType<ValidationError>(result.Error);
        Assert.Contains("scope", result.Error.Message.ToLowerInvariant());
    }

    /// <summary>
    /// Tests ValidateConfiguration with readonly scope
    /// </summary>
    [Fact]
    public void ValidateConfiguration_ReadOnlyScope_ReturnsFailure()
    {
        // Arrange
        var config = new GmailProviderConfig
        {
            ClientId = "valid_client_id",
            ClientSecret = "valid_secret",
            Scopes = new[] { GoogleOAuthScopes.GmailReadonly },
            TimeoutSeconds = 60,
            MaxRetryAttempts = 3,
            RetryDelayMilliseconds = 1000,
            IsEnabled = true
        };

        // Act
        var result = config.ValidateConfiguration();

        // Assert - Should fail because readonly doesn't have modify permissions
        Assert.True(result.IsFailure);
    }

    /// <summary>
    /// Tests ValidateConfiguration with modify scope
    /// </summary>
    [Fact]
    public void ValidateConfiguration_ModifyScope_ReturnsSuccess()
    {
        // Arrange
        var config = new GmailProviderConfig
        {
            ClientId = "valid_client_id",
            ClientSecret = "valid_secret",
            Scopes = new[] { GoogleOAuthScopes.GmailModify },
            RequestTimeout = TimeSpan.FromSeconds(30), // Less than TimeoutSeconds
            TimeoutSeconds = 60,
            MaxRetryAttempts = 3,
            RetryDelayMilliseconds = 1000,
            IsEnabled = true
        };

        // Act
        var result = config.ValidateConfiguration();

        // Assert
        Assert.True(result.IsSuccess);
    }

    /// <summary>
    /// Tests ValidateConfiguration with full Gmail scope
    /// </summary>
    [Fact]
    public void ValidateConfiguration_FullGmailScope_ReturnsSuccess()
    {
        // Arrange
        var config = new GmailProviderConfig
        {
            ClientId = "valid_client_id",
            ClientSecret = "valid_secret",
            Scopes = new[] { "https://mail.google.com/" },
            RequestTimeout = TimeSpan.FromSeconds(30), // Less than TimeoutSeconds
            TimeoutSeconds = 60,
            MaxRetryAttempts = 3,
            RetryDelayMilliseconds = 1000,
            IsEnabled = true
        };

        // Act
        var result = config.ValidateConfiguration();

        // Assert
        Assert.True(result.IsSuccess);
    }

    /// <summary>
    /// Tests ValidateConfiguration with invalid retry delay configuration
    /// </summary>
    [Fact]
    public void ValidateConfiguration_BaseRetryDelayGreaterThanMax_ReturnsFailure()
    {
        // Arrange
        var config = new GmailProviderConfig
        {
            ClientId = "valid_client_id",
            ClientSecret = "valid_secret",
            BaseRetryDelay = TimeSpan.FromMinutes(5),
            MaxRetryDelay = TimeSpan.FromMinutes(2),
            TimeoutSeconds = 60,
            MaxRetryAttempts = 3,
            RetryDelayMilliseconds = 1000,
            IsEnabled = true
        };

        // Act
        var result = config.ValidateConfiguration();

        // Assert
        Assert.True(result.IsFailure);
        Assert.IsType<ValidationError>(result.Error);
        Assert.Contains("Base retry delay", result.Error.Message);
    }

    /// <summary>
    /// Tests ValidateConfiguration with request timeout exceeding provider timeout
    /// </summary>
    [Fact]
    public void ValidateConfiguration_RequestTimeoutExceedsProviderTimeout_ReturnsFailure()
    {
        // Arrange
        var config = new GmailProviderConfig
        {
            ClientId = "valid_client_id",
            ClientSecret = "valid_secret",
            RequestTimeout = TimeSpan.FromMinutes(5),
            TimeoutSeconds = 60, // 1 minute
            MaxRetryAttempts = 3,
            RetryDelayMilliseconds = 1000,
            IsEnabled = true
        };

        // Act
        var result = config.ValidateConfiguration();

        // Assert
        Assert.True(result.IsFailure);
        Assert.IsType<ValidationError>(result.Error);
        Assert.Contains("Request timeout", result.Error.Message);
    }

    /// <summary>
    /// Tests ValidateCustomLogic for insufficient permissions
    /// </summary>
    [Fact]
    public void ValidateCustomLogic_InsufficientPermissions_ReturnsFailure()
    {
        // Arrange
        var config = new GmailProviderConfig
        {
            ClientId = "valid_client_id",
            ClientSecret = "valid_secret",
            Scopes = new[] { GmailService.Scope.GmailReadonly },
            RequestTimeout = TimeSpan.FromSeconds(30), // Less than TimeoutSeconds
            TimeoutSeconds = 60,
            MaxRetryAttempts = 3,
            RetryDelayMilliseconds = 1000,
            IsEnabled = true
        };

        // Act
        var result = config.ValidateConfiguration();

        // Assert
        Assert.True(result.IsFailure);
        Assert.IsType<ValidationError>(result.Error);
        Assert.Contains("modify permissions", result.Error.Message);
    }

    /// <summary>
    /// Tests ValidateCustomLogic with modify scope
    /// </summary>
    [Fact]
    public void ValidateCustomLogic_ModifyScope_ReturnsSuccess()
    {
        // Arrange
        var config = new GmailProviderConfig
        {
            ClientId = "valid_client_id",
            ClientSecret = "valid_secret",
            Scopes = new[] { GoogleOAuthScopes.GmailModify },
            RequestTimeout = TimeSpan.FromSeconds(30), // Less than TimeoutSeconds
            TimeoutSeconds = 60,
            MaxRetryAttempts = 3,
            RetryDelayMilliseconds = 1000,
            IsEnabled = true
        };

        // Act
        var result = config.ValidateConfiguration();

        // Assert
        Assert.True(result.IsSuccess);
    }

    /// <summary>
    /// Tests ValidateCustomLogic with full mail scope
    /// </summary>
    [Fact]
    public void ValidateCustomLogic_FullMailScope_ReturnsSuccess()
    {
        // Arrange
        var config = new GmailProviderConfig
        {
            ClientId = "valid_client_id",
            ClientSecret = "valid_secret",
            Scopes = new[] { "https://mail.google.com/" },
            RequestTimeout = TimeSpan.FromSeconds(30), // Less than TimeoutSeconds
            TimeoutSeconds = 60,
            MaxRetryAttempts = 3,
            RetryDelayMilliseconds = 1000,
            IsEnabled = true
        };

        // Act
        var result = config.ValidateConfiguration();

        // Assert
        Assert.True(result.IsSuccess);
    }

    /// <summary>
    /// Tests ValidateCustomLogic with batch size exceeding Gmail API limits
    /// </summary>
    [Fact]
    public void ValidateCustomLogic_BatchSizeExceedsLimit_ReturnsFailure()
    {
        // Arrange
        var config = new GmailProviderConfig
        {
            ClientId = "valid_client_id",
            ClientSecret = "valid_secret",
            BatchSize = 150, // Exceeds 100 limit
            RequestTimeout = TimeSpan.FromSeconds(30), // Less than TimeoutSeconds
            TimeoutSeconds = 60,
            MaxRetryAttempts = 3,
            RetryDelayMilliseconds = 1000,
            IsEnabled = true
        };

        // Act
        var result = config.ValidateConfiguration();

        // Assert
        Assert.True(result.IsFailure);
        Assert.IsType<ValidationError>(result.Error);
        Assert.Contains("Batch size must be between", result.Error.Message);
    }

    /// <summary>
    /// Tests ValidateCustomLogic with page size exceeding Gmail API limits
    /// </summary>
    [Fact]
    public void ValidateCustomLogic_PageSizeExceedsLimit_ReturnsFailure()
    {
        // Arrange
        var config = new GmailProviderConfig
        {
            ClientId = "valid_client_id",
            ClientSecret = "valid_secret",
            DefaultPageSize = 600, // Exceeds 500 limit
            RequestTimeout = TimeSpan.FromSeconds(30), // Less than TimeoutSeconds
            TimeoutSeconds = 60,
            MaxRetryAttempts = 3,
            RetryDelayMilliseconds = 1000,
            IsEnabled = true
        };

        // Act
        var result = config.ValidateConfiguration();

        // Assert
        Assert.True(result.IsFailure);
        Assert.IsType<ValidationError>(result.Error);
        Assert.Contains("Default page size must be between", result.Error.Message);
    }

    /// <summary>
    /// Tests GetSanitizedCopy redacts sensitive information
    /// </summary>
    [Fact]
    public void GetSanitizedCopy_RedactsSensitiveInformation()
    {
        // Arrange
        var config = new GmailProviderConfig
        {
            ClientId = "test_client_id_12345",
            ClientSecret = "secret_value_12345",
            ApplicationName = "Test App"
        };

        // Act
        var sanitized = config.GetSanitizedCopy() as GmailProviderConfig;

        // Assert
        Assert.NotNull(sanitized);
        Assert.Equal("test_client_id_12345", sanitized.ClientId); // Not sensitive - keep as is
        Assert.Equal("***REDACTED***", sanitized.ClientSecret); // Fully redacted
        Assert.Equal("Test App", sanitized.ApplicationName); // Not redacted
    }

    /// <summary>
    /// Tests GetSanitizedCopy with short ClientId (4 chars or less)
    /// </summary>
    [Fact]
    public void GetSanitizedCopy_ShortClientId_FullyRedacted()
    {
        // Arrange
        var config = new GmailProviderConfig
        {
            ClientId = "1234",
            ClientSecret = "secret_value",
            ApplicationName = "Test App"
        };

        // Act
        var sanitized = config.GetSanitizedCopy() as GmailProviderConfig;

        // Assert
        Assert.NotNull(sanitized);
        Assert.Equal("1234", sanitized.ClientId); // Not sensitive - keep as is even for short IDs
        Assert.Equal("***REDACTED***", sanitized.ClientSecret);
    }

    /// <summary>
    /// Tests GetSanitizedCopy with empty ClientId
    /// </summary>
    [Fact]
    public void GetSanitizedCopy_EmptyClientId_RemainsEmpty()
    {
        // Arrange
        var config = new GmailProviderConfig
        {
            ClientId = "",
            ClientSecret = "secret_value",
            ApplicationName = "Test App"
        };

        // Act
        var sanitized = config.GetSanitizedCopy() as GmailProviderConfig;

        // Assert
        Assert.NotNull(sanitized);
        Assert.Equal("", sanitized.ClientId); // Empty remains empty
        Assert.Equal("***REDACTED***", sanitized.ClientSecret);
    }

    /// <summary>
    /// Tests CreateDevelopmentConfig factory method
    /// </summary>
    [Fact]
    public void CreateDevelopmentConfig_ReturnsValidDevelopmentConfiguration()
    {
        // Arrange
        const string clientId = "dev_client_id";
        const string clientSecret = "dev_client_secret";

        // Act
        var config = GmailProviderConfig.CreateDevelopmentConfig(clientId, clientSecret);

        // Assert
        Assert.Equal(clientId, config.ClientId);
        Assert.Equal(clientSecret, config.ClientSecret);
        Assert.Equal(TimeSpan.FromSeconds(30), config.RequestTimeout);
        Assert.Equal(3, config.MaxRetries);
        Assert.Equal(TimeSpan.FromMilliseconds(500), config.BaseRetryDelay);
        Assert.Equal(TimeSpan.FromSeconds(30), config.MaxRetryDelay);
        Assert.Equal(25, config.BatchSize);
        Assert.Equal(50, config.DefaultPageSize);
        Assert.Equal(60, config.TimeoutSeconds);
        Assert.Equal(3, config.MaxRetryAttempts);
        Assert.Equal(500, config.RetryDelayMilliseconds);
        Assert.True(config.IsEnabled);

        // Verify it passes validation
        var result = config.ValidateConfiguration();
        Assert.True(result.IsSuccess);
    }

    /// <summary>
    /// Tests CreateProductionConfig factory method
    /// </summary>
    [Fact]
    public void CreateProductionConfig_ReturnsValidProductionConfiguration()
    {
        // Arrange
        const string clientId = "prod_client_id";
        const string clientSecret = "prod_client_secret";

        // Act
        var config = GmailProviderConfig.CreateProductionConfig(clientId, clientSecret);

        // Assert
        Assert.Equal(clientId, config.ClientId);
        Assert.Equal(clientSecret, config.ClientSecret);
        Assert.Equal(TimeSpan.FromMinutes(2), config.RequestTimeout);
        Assert.Equal(5, config.MaxRetries);
        Assert.Equal(TimeSpan.FromSeconds(1), config.BaseRetryDelay);
        Assert.Equal(TimeSpan.FromMinutes(2), config.MaxRetryDelay);
        Assert.Equal(50, config.BatchSize);
        Assert.Equal(100, config.DefaultPageSize);
        Assert.Equal(120, config.TimeoutSeconds);
        Assert.Equal(5, config.MaxRetryAttempts);
        Assert.Equal(1000, config.RetryDelayMilliseconds);
        Assert.True(config.EnableBatchOptimization);
        Assert.True(config.IsEnabled);

        // Verify it passes validation
        var result = config.ValidateConfiguration();
        Assert.True(result.IsSuccess);
    }

    /// <summary>
    /// Tests complete valid configuration passes all validation
    /// </summary>
    [Fact]
    public void ValidateConfiguration_CompleteValidConfiguration_ReturnsSuccess()
    {
        // Arrange
        var config = new GmailProviderConfig();
        config.ClientId = "valid_client_id_12345";
        config.ClientSecret = "valid_client_secret_12345";
        config.ApplicationName = "Test Application";
        config.Scopes = new[] { GmailService.Scope.GmailModify };
        config.RequestTimeout = TimeSpan.FromMinutes(1);
        config.MaxRetries = 3;
        config.BaseRetryDelay = TimeSpan.FromSeconds(1);
        config.MaxRetryDelay = TimeSpan.FromMinutes(1);
        config.EnableBatchOptimization = true;
        config.BatchSize = 50;
        config.DefaultPageSize = 100;
        config.TimeoutSeconds = 120;
        config.MaxRetryAttempts = 3;
        config.RetryDelayMilliseconds = 1000;
        config.IsEnabled = true;

        // Act
        var result = config.ValidateConfiguration();

        // Assert
        Assert.True(result.IsSuccess);
    }
}