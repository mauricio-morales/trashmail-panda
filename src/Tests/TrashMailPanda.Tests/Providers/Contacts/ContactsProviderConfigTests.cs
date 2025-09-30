using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.Linq;
using TrashMailPanda.Providers.Contacts;
using TrashMailPanda.Providers.Contacts.Models;
using TrashMailPanda.Shared.Base;
using TrashMailPanda.Shared.Models;
using TrashMailPanda.Shared.Security;

namespace TrashMailPanda.Tests.Providers.Contacts;

/// <summary>
/// Comprehensive unit tests for ContactsProviderConfig
/// Tests configuration validation, custom validation logic, cache configuration interdependencies, and factory methods
/// </summary>
public class ContactsProviderConfigTests
{
    #region Constructor and Default Values Tests

    /// <summary>
    /// Tests that default constructor creates a valid configuration with expected defaults
    /// </summary>
    [Fact]
    public void Constructor_SetsExpectedDefaults()
    {
        // Arrange & Act
        var config = new ContactsProviderConfig();

        // Assert
        Assert.Equal("Contacts", config.Name);
        Assert.Contains("contacts", config.Tags);
        Assert.Contains("google", config.Tags);
        Assert.Contains("people", config.Tags);
        Assert.Contains("trust", config.Tags);
        Assert.Equal("TrashMail Panda", config.ApplicationName);
        Assert.Equal(2, config.Scopes.Length);
        Assert.Contains(GoogleOAuthScopes.ContactsReadonly, config.Scopes);
        Assert.Contains(GoogleOAuthScopes.UserInfoProfile, config.Scopes);
        Assert.Equal(TimeSpan.FromMinutes(1), config.RequestTimeout);
        Assert.Equal(5, config.MaxRetries);
        Assert.Equal(TimeSpan.FromSeconds(1), config.BaseRetryDelay);
        Assert.Equal(TimeSpan.FromMinutes(1), config.MaxRetryDelay);
        Assert.Equal(1000, config.DefaultPageSize);
        Assert.True(config.EnableContactsCaching);
        Assert.True(config.EnableBackgroundSync);
        Assert.Equal(TimeSpan.FromHours(4), config.BackgroundSyncInterval);
        Assert.True(config.EnableTrustSignals);
        Assert.Equal(TimeSpan.FromDays(1), config.TrustSignalCacheExpiry);
        Assert.NotNull(config.Cache);
    }

    #endregion

    #region DataAnnotations Validation Tests

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
        var config = new ContactsProviderConfig
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
    /// Tests string length validation for ClientId
    /// </summary>
    [Theory]
    [InlineData("short", false)] // Too short
    [InlineData("valid_client_id_12345", true)] // Valid length
    [InlineData("", false)] // Empty (already covered in required tests)
    public void DataAnnotationsValidation_ClientIdLength(string clientId, bool shouldBeValid)
    {
        // Arrange
        var config = new ContactsProviderConfig
        {
            ClientId = clientId,
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
        Assert.Equal(shouldBeValid, isValid);
        if (!shouldBeValid)
        {
            Assert.Contains(results, r => r.MemberNames.Contains("ClientId"));
        }
    }

    /// <summary>
    /// Tests string length validation for ClientSecret
    /// </summary>
    [Theory]
    [InlineData("short", false)] // Too short
    [InlineData("valid_client_secret_12345", true)] // Valid length
    public void DataAnnotationsValidation_ClientSecretLength(string clientSecret, bool shouldBeValid)
    {
        // Arrange
        var config = new ContactsProviderConfig
        {
            ClientId = "valid_client_id_12345",
            ClientSecret = clientSecret,
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
        if (!shouldBeValid)
        {
            Assert.Contains(results, r => r.MemberNames.Contains("ClientSecret"));
        }
    }

    /// <summary>
    /// Tests string length validation for ApplicationName
    /// </summary>
    [Theory]
    [InlineData("", false)] // Empty
    [InlineData("Valid App", true)] // Valid length
    public void DataAnnotationsValidation_ApplicationNameLength(string applicationName, bool shouldBeValid)
    {
        // Arrange
        var config = new ContactsProviderConfig
        {
            ClientId = "valid_client_id_12345",
            ClientSecret = "valid_client_secret_12345",
            ApplicationName = applicationName,
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
        if (!shouldBeValid)
        {
            Assert.Contains(results, r => r.MemberNames.Contains("ApplicationName"));
        }
    }

    /// <summary>
    /// Tests range validation for numeric properties
    /// </summary>
    [Theory]
    [InlineData(3, true)] // Valid max retries
    [InlineData(15, false)] // Max retries too high
    [InlineData(0, false)] // Max retries too low
    [InlineData(1000, true)] // Valid page size
    [InlineData(2001, false)] // Page size too high
    public void DataAnnotationsValidation_RangeValidation(int value, bool shouldBeValid)
    {
        // Arrange
        var config = new ContactsProviderConfig
        {
            ClientId = "valid_client_id_12345",
            ClientSecret = "valid_client_secret_12345",
            MaxRetries = value,
            DefaultPageSize = value,
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
        if (shouldBeValid)
        {
            // May still have other validation errors, but not from our test properties
            Assert.True(isValid || !results.Any(r =>
                r.MemberNames.Contains("MaxRetries") ||
                r.MemberNames.Contains("DefaultPageSize")));
        }
        else
        {
            Assert.False(isValid);
        }
    }

    #endregion

    #region ValidateConfiguration Method Tests

    /// <summary>
    /// Tests ValidateConfiguration with empty ClientId returns failure
    /// </summary>
    [Fact]
    public void ValidateConfiguration_EmptyClientId_ReturnsFailure()
    {
        // Arrange
        var config = new ContactsProviderConfig
        {
            ClientId = "",
            ClientSecret = "valid_secret_12345",
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
        Assert.Contains("Client ID", result.Error.Message);
    }

    /// <summary>
    /// Tests ValidateConfiguration with empty ClientSecret returns failure
    /// </summary>
    [Fact]
    public void ValidateConfiguration_EmptyClientSecret_ReturnsFailure()
    {
        // Arrange
        var config = new ContactsProviderConfig
        {
            ClientId = "valid_client_id_12345",
            ClientSecret = "",
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
        Assert.Contains("Client Secret", result.Error.Message);
    }

    /// <summary>
    /// Tests ValidateConfiguration with null scopes returns failure
    /// </summary>
    [Fact]
    public void ValidateConfiguration_NullScopes_ReturnsFailure()
    {
        // Arrange
        var config = new ContactsProviderConfig
        {
            ClientId = "valid_client_id_12345",
            ClientSecret = "valid_client_secret_12345",
            Scopes = null!,
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
        Assert.Contains("scope", result.Error.Message.ToLowerInvariant());
    }

    /// <summary>
    /// Tests ValidateConfiguration with empty scopes returns failure
    /// </summary>
    [Fact]
    public void ValidateConfiguration_EmptyScopes_ReturnsFailure()
    {
        // Arrange
        var config = new ContactsProviderConfig
        {
            ClientId = "valid_client_id_12345",
            ClientSecret = "valid_client_secret_12345",
            Scopes = Array.Empty<string>(),
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
        Assert.Contains("scope", result.Error.Message.ToLowerInvariant());
    }

    /// <summary>
    /// Tests ValidateConfiguration with invalid OAuth scope returns failure
    /// </summary>
    [Fact]
    public void ValidateConfiguration_InvalidOAuthScope_ReturnsFailure()
    {
        // Arrange
        var config = new ContactsProviderConfig
        {
            ClientId = "valid_client_id_12345",
            ClientSecret = "valid_client_secret_12345",
            Scopes = new[] { "invalid_scope" },
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
        Assert.Contains("invalid OAuth scope", result.Error.Message);
    }

    /// <summary>
    /// Tests ValidateConfiguration with valid OAuth scopes returns success
    /// </summary>
    [Fact]
    public void ValidateConfiguration_ContactsReadonlyScope_ReturnsSuccess()
    {
        // Arrange
        var config = new ContactsProviderConfig
        {
            ClientId = "valid_client_id_12345",
            ClientSecret = "valid_client_secret_12345",
            Scopes = new[] { GoogleOAuthScopes.ContactsReadonly },
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

    [Fact]
    public void ValidateConfiguration_ContactsScope_ReturnsSuccess()
    {
        // Arrange
        var config = new ContactsProviderConfig
        {
            ClientId = "valid_client_id_12345",
            ClientSecret = "valid_client_secret_12345",
            Scopes = new[] { GoogleOAuthScopes.Contacts },
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

    [Fact]
    public void ValidateConfiguration_MultipleValidScopes_ReturnsSuccess()
    {
        // Arrange
        var config = new ContactsProviderConfig
        {
            ClientId = "valid_client_id_12345",
            ClientSecret = "valid_client_secret_12345",
            Scopes = new[] { GoogleOAuthScopes.ContactsReadonly, GoogleOAuthScopes.UserInfoProfile },
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
    /// Tests ValidateConfiguration with mixed valid OAuth scopes returns success
    /// </summary>
    [Fact]
    public void ValidateConfiguration_MixedValidOAuthScopes_ReturnsSuccess()
    {
        // Arrange
        var config = new ContactsProviderConfig
        {
            ClientId = "valid_client_id_12345",
            ClientSecret = "valid_client_secret_12345",
            Scopes = new[] { GoogleOAuthScopes.Contacts, GoogleOAuthScopes.UserInfoEmail },
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
    /// Tests ValidateConfiguration with invalid retry delay configuration returns failure
    /// </summary>
    [Fact]
    public void ValidateConfiguration_BaseRetryDelayGreaterThanMax_ReturnsFailure()
    {
        // Arrange
        var config = new ContactsProviderConfig
        {
            ClientId = "valid_client_id_12345",
            ClientSecret = "valid_client_secret_12345",
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
    /// Tests ValidateConfiguration with request timeout exceeding provider timeout returns failure
    /// </summary>
    [Fact]
    public void ValidateConfiguration_RequestTimeoutExceedsProviderTimeout_ReturnsFailure()
    {
        // Arrange
        var config = new ContactsProviderConfig
        {
            ClientId = "valid_client_id_12345",
            ClientSecret = "valid_client_secret_12345",
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

    #endregion

    #region Custom Validation Logic Tests

    /// <summary>
    /// Tests ValidateCustomLogic with insufficient permissions returns failure
    /// </summary>
    [Fact]
    public void ValidateCustomLogic_InsufficientPermissions_ReturnsFailure()
    {
        // Arrange
        var config = new ContactsProviderConfig
        {
            ClientId = "valid_client_id_12345",
            ClientSecret = "valid_client_secret_12345",
            Scopes = new[] { GoogleOAuthScopes.UserInfoProfile }, // No contacts permission
            RequestTimeout = TimeSpan.FromSeconds(30),
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
        Assert.Contains("contacts read permissions", result.Error.Message);
    }

    /// <summary>
    /// Tests ValidateCustomLogic with contacts read permissions returns success
    /// </summary>
    [Fact]
    public void ValidateCustomLogic_ContactsReadPermissions_ReturnsSuccess()
    {
        // Arrange
        var config = new ContactsProviderConfig
        {
            ClientId = "valid_client_id_12345",
            ClientSecret = "valid_client_secret_12345",
            Scopes = new[] { GoogleOAuthScopes.ContactsReadonly },
            RequestTimeout = TimeSpan.FromSeconds(30),
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
    /// Tests ValidateCustomLogic with contacts write permissions returns success
    /// </summary>
    [Fact]
    public void ValidateCustomLogic_ContactsWritePermissions_ReturnsSuccess()
    {
        // Arrange
        var config = new ContactsProviderConfig
        {
            ClientId = "valid_client_id_12345",
            ClientSecret = "valid_client_secret_12345",
            Scopes = new[] { GoogleOAuthScopes.Contacts },
            RequestTimeout = TimeSpan.FromSeconds(30),
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
    /// Tests ValidateCustomLogic with page size exceeding Google People API limits returns failure
    /// </summary>
    [Fact]
    public void ValidateCustomLogic_PageSizeExceedsLimit_ReturnsFailure()
    {
        // Arrange
        var config = new ContactsProviderConfig
        {
            ClientId = "valid_client_id_12345",
            ClientSecret = "valid_client_secret_12345",
            DefaultPageSize = 2500, // Exceeds 2000 limit
            RequestTimeout = TimeSpan.FromSeconds(30),
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
        Assert.Contains("Page size cannot exceed", result.Error.Message);
    }

    /// <summary>
    /// Tests ValidateCustomLogic with background sync interval too short returns failure
    /// </summary>
    [Fact]
    public void ValidateCustomLogic_BackgroundSyncIntervalTooShort_ReturnsFailure()
    {
        // Arrange
        var config = new ContactsProviderConfig
        {
            ClientId = "valid_client_id_12345",
            ClientSecret = "valid_client_secret_12345",
            EnableBackgroundSync = true,
            BackgroundSyncInterval = TimeSpan.FromMinutes(15), // Less than 30 minutes
            RequestTimeout = TimeSpan.FromSeconds(30),
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
        Assert.Contains("Background sync interval must be at least 30 minutes", result.Error.Message);
    }

    /// <summary>
    /// Tests ValidateCustomLogic with disabled background sync ignores interval validation
    /// </summary>
    [Fact]
    public void ValidateCustomLogic_DisabledBackgroundSync_IgnoresIntervalValidation()
    {
        // Arrange
        var config = new ContactsProviderConfig
        {
            ClientId = "valid_client_id_12345",
            ClientSecret = "valid_client_secret_12345",
            EnableBackgroundSync = false,
            BackgroundSyncInterval = TimeSpan.FromMinutes(15), // Would be invalid if enabled
            RequestTimeout = TimeSpan.FromSeconds(30),
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

    #endregion

    #region Cache Configuration Validation Tests

    /// <summary>
    /// Tests ValidateConfiguration with caching enabled validates cache configuration
    /// </summary>
    [Fact]
    public void ValidateConfiguration_CachingEnabled_ValidatesCacheConfiguration()
    {
        // Arrange
        var config = new ContactsProviderConfig
        {
            ClientId = "valid_client_id_12345",
            ClientSecret = "valid_client_secret_12345",
            EnableContactsCaching = true,
            Cache = new CacheConfiguration
            {
                MemoryTtl = TimeSpan.Zero // Invalid - should cause validation failure
            },
            RequestTimeout = TimeSpan.FromSeconds(30),
            TimeoutSeconds = 60,
            MaxRetryAttempts = 3,
            RetryDelayMilliseconds = 1000,
            IsEnabled = true
        };

        // Act
        var result = config.ValidateConfiguration();

        // Assert
        Assert.True(result.IsFailure);
        // Error should be from cache configuration validation
    }

    /// <summary>
    /// Tests ValidateConfiguration with caching disabled skips cache validation
    /// </summary>
    [Fact]
    public void ValidateConfiguration_CachingDisabled_SkipsCacheValidation()
    {
        // Arrange
        var config = new ContactsProviderConfig
        {
            ClientId = "valid_client_id_12345",
            ClientSecret = "valid_client_secret_12345",
            EnableContactsCaching = false,
            Cache = new CacheConfiguration
            {
                MemoryTtl = TimeSpan.Zero // Would be invalid if caching enabled
            },
            RequestTimeout = TimeSpan.FromSeconds(30),
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

    #endregion

    #region Sanitization Tests

    /// <summary>
    /// Tests GetSanitizedCopy redacts sensitive information
    /// </summary>
    [Fact]
    public void GetSanitizedCopy_RedactsSensitiveInformation()
    {
        // Arrange
        var config = new ContactsProviderConfig
        {
            ClientId = "test_client_id_12345",
            ClientSecret = "secret_value_12345",
            ApplicationName = "Test App"
        };

        // Act
        var sanitized = config.GetSanitizedCopy() as ContactsProviderConfig;

        // Assert
        Assert.NotNull(sanitized);
        Assert.Equal("test_client_id_12345", sanitized.ClientId); // Not sensitive - keep as is
        Assert.Equal("***REDACTED***", sanitized.ClientSecret); // Fully redacted
        Assert.Equal("Test App", sanitized.ApplicationName); // Not redacted
    }

    /// <summary>
    /// Tests GetSanitizedCopy with empty ClientSecret
    /// </summary>
    [Fact]
    public void GetSanitizedCopy_EmptyClientSecret_RedactsCorrectly()
    {
        // Arrange
        var config = new ContactsProviderConfig
        {
            ClientId = "test_client_id",
            ClientSecret = "",
            ApplicationName = "Test App"
        };

        // Act
        var sanitized = config.GetSanitizedCopy() as ContactsProviderConfig;

        // Assert
        Assert.NotNull(sanitized);
        Assert.Equal("test_client_id", sanitized.ClientId);
        Assert.Equal("***REDACTED***", sanitized.ClientSecret); // Empty also gets redacted
    }

    #endregion

    #region Factory Method Tests

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
        var config = ContactsProviderConfig.CreateDevelopmentConfig(clientId, clientSecret);

        // Assert
        Assert.Equal(clientId, config.ClientId);
        Assert.Equal(clientSecret, config.ClientSecret);
        Assert.Equal(TimeSpan.FromSeconds(30), config.RequestTimeout);
        Assert.Equal(3, config.MaxRetries);
        Assert.Equal(TimeSpan.FromMilliseconds(500), config.BaseRetryDelay);
        Assert.Equal(TimeSpan.FromSeconds(30), config.MaxRetryDelay);
        Assert.Equal(500, config.DefaultPageSize);
        Assert.Equal(60, config.TimeoutSeconds);
        Assert.Equal(3, config.MaxRetryAttempts);
        Assert.Equal(500, config.RetryDelayMilliseconds);
        Assert.Equal(TimeSpan.FromHours(1), config.BackgroundSyncInterval);
        Assert.True(config.IsEnabled);

        // Verify cache configuration
        Assert.NotNull(config.Cache);
        Assert.Equal(CacheConfiguration.CreateDevelopmentConfig().MemoryTtl, config.Cache.MemoryTtl);

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
        var config = ContactsProviderConfig.CreateProductionConfig(clientId, clientSecret);

        // Assert
        Assert.Equal(clientId, config.ClientId);
        Assert.Equal(clientSecret, config.ClientSecret);
        Assert.Equal(TimeSpan.FromMinutes(1), config.RequestTimeout);
        Assert.Equal(5, config.MaxRetries);
        Assert.Equal(TimeSpan.FromSeconds(1), config.BaseRetryDelay);
        Assert.Equal(TimeSpan.FromMinutes(1), config.MaxRetryDelay);
        Assert.Equal(1000, config.DefaultPageSize);
        Assert.Equal(120, config.TimeoutSeconds);
        Assert.Equal(5, config.MaxRetryAttempts);
        Assert.Equal(1000, config.RetryDelayMilliseconds);
        Assert.Equal(TimeSpan.FromHours(4), config.BackgroundSyncInterval);
        Assert.True(config.EnableContactsCaching);
        Assert.True(config.EnableBackgroundSync);
        Assert.True(config.EnableTrustSignals);
        Assert.True(config.IsEnabled);

        // Verify cache configuration
        Assert.NotNull(config.Cache);
        Assert.Equal(CacheConfiguration.CreateProductionConfig().MemoryTtl, config.Cache.MemoryTtl);

        // Verify it passes validation
        var result = config.ValidateConfiguration();
        Assert.True(result.IsSuccess);
    }

    #endregion

    #region Cache Configuration Interdependency Tests

    /// <summary>
    /// Tests cache configuration affects overall validation when caching is enabled
    /// </summary>
    [Fact]
    public void CacheConfigurationInterdependency_WhenCachingEnabled_AffectsValidation()
    {
        // Arrange
        var config = new ContactsProviderConfig
        {
            ClientId = "valid_client_id_12345",
            ClientSecret = "valid_client_secret_12345",
            EnableContactsCaching = true,
            Cache = new CacheConfiguration
            {
                MemoryTtl = TimeSpan.FromSeconds(-1), // Invalid negative value
                SqliteTtl = TimeSpan.FromDays(1)
            },
            RequestTimeout = TimeSpan.FromSeconds(30),
            TimeoutSeconds = 60,
            MaxRetryAttempts = 3,
            RetryDelayMilliseconds = 1000,
            IsEnabled = true
        };

        // Act
        var result = config.ValidateConfiguration();

        // Assert
        Assert.True(result.IsFailure);
        // Should fail due to invalid cache configuration
    }

    /// <summary>
    /// Tests trust signal cache expiry affects validation when trust signals are enabled
    /// </summary>
    [Fact]
    public void TrustSignalCacheExpiry_WhenTrustSignalsEnabled_IsValidated()
    {
        // Arrange
        var config = new ContactsProviderConfig
        {
            ClientId = "valid_client_id_12345",
            ClientSecret = "valid_client_secret_12345",
            EnableTrustSignals = true,
            TrustSignalCacheExpiry = TimeSpan.FromDays(1), // Valid
            RequestTimeout = TimeSpan.FromSeconds(30),
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
    /// Tests background sync configuration interdependency with caching
    /// </summary>
    [Fact]
    public void BackgroundSyncConfiguration_InterdependencyWithCaching()
    {
        // Arrange - background sync enabled but caching disabled
        var config = new ContactsProviderConfig
        {
            ClientId = "valid_client_id_12345",
            ClientSecret = "valid_client_secret_12345",
            EnableContactsCaching = false,
            EnableBackgroundSync = true, // This should still work without caching
            BackgroundSyncInterval = TimeSpan.FromHours(1),
            RequestTimeout = TimeSpan.FromSeconds(30),
            TimeoutSeconds = 60,
            MaxRetryAttempts = 3,
            RetryDelayMilliseconds = 1000,
            IsEnabled = true
        };

        // Act
        var result = config.ValidateConfiguration();

        // Assert
        Assert.True(result.IsSuccess); // Should be valid even with caching disabled
    }

    #endregion

    #region Complete Configuration Tests

    /// <summary>
    /// Tests complete valid configuration passes all validation
    /// </summary>
    [Fact]
    public void ValidateConfiguration_CompleteValidConfiguration_ReturnsSuccess()
    {
        // Arrange
        var config = new ContactsProviderConfig
        {
            ClientId = "valid_client_id_12345",
            ClientSecret = "valid_client_secret_12345",
            ApplicationName = "Test Application",
            Scopes = new[] { GoogleOAuthScopes.ContactsReadonly, GoogleOAuthScopes.UserInfoProfile },
            RequestTimeout = TimeSpan.FromMinutes(1),
            MaxRetries = 3,
            BaseRetryDelay = TimeSpan.FromSeconds(1),
            MaxRetryDelay = TimeSpan.FromMinutes(1),
            DefaultPageSize = 1000,
            EnableContactsCaching = true,
            EnableBackgroundSync = true,
            BackgroundSyncInterval = TimeSpan.FromHours(4),
            EnableTrustSignals = true,
            TrustSignalCacheExpiry = TimeSpan.FromDays(1),
            Cache = CacheConfiguration.CreateProductionConfig(),
            TimeoutSeconds = 120,
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
    /// Tests configuration with mixed valid and invalid settings
    /// </summary>
    [Fact]
    public void ValidateConfiguration_MixedValidInvalidSettings_ReturnsFailure()
    {
        // Arrange
        var config = new ContactsProviderConfig
        {
            ClientId = "valid_client_id_12345",
            ClientSecret = "valid_client_secret_12345",
            ApplicationName = "Test Application",
            Scopes = new[] { GoogleOAuthScopes.ContactsReadonly },
            RequestTimeout = TimeSpan.FromMinutes(5), // Invalid - exceeds TimeoutSeconds
            MaxRetries = 3,
            BaseRetryDelay = TimeSpan.FromSeconds(1),
            MaxRetryDelay = TimeSpan.FromMinutes(1),
            DefaultPageSize = 1000,
            TimeoutSeconds = 60, // 1 minute - less than RequestTimeout
            MaxRetryAttempts = 3,
            RetryDelayMilliseconds = 1000,
            IsEnabled = true
        };

        // Act
        var result = config.ValidateConfiguration();

        // Assert
        Assert.True(result.IsFailure);
        Assert.Contains("Request timeout", result.Error.Message);
    }

    #endregion
}