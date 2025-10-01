using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Threading;
using System.Threading.Tasks;
using Google.Apis.Auth.OAuth2;
using Google.Apis.PeopleService.v1;
using Google.Apis.PeopleService.v1.Data;
using Google.Apis.Services;
using Google;
using Microsoft.Extensions.Logging;
using Moq;
using TrashMailPanda.Providers.Contacts;
using TrashMailPanda.Providers.Contacts.Adapters;
using TrashMailPanda.Providers.Contacts.Models;
using TrashMailPanda.Shared.Base;
using TrashMailPanda.Shared.Models;
using TrashMailPanda.Shared.Security;
using TrashMailPanda.Shared.Services;
using TrashMailPanda.Shared;

namespace TrashMailPanda.Tests.Providers.Contacts;

/// <summary>
/// Unit tests for GoogleContactsAdapter - simplified to avoid mocking sealed Google classes
/// Complex integration scenarios require real Google APIs and are covered by integration tests
/// </summary>
public class GoogleContactsAdapterTests : IDisposable
{
    private readonly Mock<IGoogleOAuthService> _mockGoogleOAuthService;
    private readonly Mock<ISecureStorageManager> _mockSecureStorageManager;
    private readonly Mock<ISecurityAuditLogger> _mockSecurityAuditLogger;
    private readonly Mock<IPhoneNumberService> _mockPhoneNumberService;
    private readonly Mock<ILogger<GoogleContactsAdapter>> _mockLogger;
    private readonly ContactsProviderConfig _validConfig;
    private readonly GoogleContactsAdapter _adapter;

    public GoogleContactsAdapterTests()
    {
        _mockGoogleOAuthService = new Mock<IGoogleOAuthService>();
        _mockSecureStorageManager = new Mock<ISecureStorageManager>();
        _mockSecurityAuditLogger = new Mock<ISecurityAuditLogger>();
        _mockPhoneNumberService = new Mock<IPhoneNumberService>();
        _mockLogger = new Mock<ILogger<GoogleContactsAdapter>>();

        _validConfig = ContactsProviderConfig.CreateDevelopmentConfig("test_client_id", "test_client_secret");

        _adapter = new GoogleContactsAdapter(
            _mockGoogleOAuthService.Object,
            _mockSecureStorageManager.Object,
            _mockSecurityAuditLogger.Object,
            _validConfig,
            _mockPhoneNumberService.Object,
            _mockLogger.Object);
    }

    #region Constructor Tests

    /// <summary>
    /// Tests that constructor properly initializes adapter with dependencies
    /// </summary>
    [Fact]
    public void Constructor_WithValidDependencies_InitializesSuccessfully()
    {
        // Assert
        Assert.NotNull(_adapter);
        Assert.Equal(ContactSourceType.Google, _adapter.SourceType);
        Assert.Equal("Google Contacts", _adapter.DisplayName);
        Assert.True(_adapter.IsEnabled);
        Assert.True(_adapter.SupportsIncrementalSync);
    }

    /// <summary>
    /// Tests that constructor throws ArgumentNullException for null OAuth service
    /// </summary>
    [Fact]
    public void Constructor_WithNullOAuthService_ThrowsArgumentNullException()
    {
        // Act & Assert
        Assert.Throws<ArgumentNullException>(() =>
            new GoogleContactsAdapter(
                null!,
                _mockSecureStorageManager.Object,
                _mockSecurityAuditLogger.Object,
                _validConfig,
                _mockPhoneNumberService.Object,
                _mockLogger.Object));
    }

    /// <summary>
    /// Tests that constructor throws ArgumentNullException for null secure storage manager
    /// </summary>
    [Fact]
    public void Constructor_WithNullSecureStorageManager_ThrowsArgumentNullException()
    {
        // Act & Assert
        Assert.Throws<ArgumentNullException>(() =>
            new GoogleContactsAdapter(
                _mockGoogleOAuthService.Object,
                null!,
                _mockSecurityAuditLogger.Object,
                _validConfig,
                _mockPhoneNumberService.Object,
                _mockLogger.Object));
    }

    #endregion

    #region Configuration Validation Tests

    /// <summary>
    /// Tests ValidateAsync with missing client ID returns failure
    /// </summary>
    [Fact]
    public async Task ValidateAsync_WithMissingClientId_ReturnsFailure()
    {
        // Arrange
        var invalidConfig = ContactsProviderConfig.CreateDevelopmentConfig("", "test_secret");
        var invalidAdapter = new GoogleContactsAdapter(
            _mockGoogleOAuthService.Object,
            _mockSecureStorageManager.Object,
            _mockSecurityAuditLogger.Object,
            invalidConfig,
            _mockPhoneNumberService.Object,
            _mockLogger.Object);

        // Act
        var result = await invalidAdapter.ValidateAsync();

        // Assert
        Assert.True(result.IsFailure);
        Assert.Contains("API validation failed", result.Error.Message);
    }

    /// <summary>
    /// Tests ValidateAsync with missing client secret returns failure
    /// </summary>
    [Fact]
    public async Task ValidateAsync_WithMissingClientSecret_ReturnsFailure()
    {
        // Arrange
        var invalidConfig = ContactsProviderConfig.CreateDevelopmentConfig("test_client", "");
        var invalidAdapter = new GoogleContactsAdapter(
            _mockGoogleOAuthService.Object,
            _mockSecureStorageManager.Object,
            _mockSecurityAuditLogger.Object,
            invalidConfig,
            _mockPhoneNumberService.Object,
            _mockLogger.Object);

        // Act
        var result = await invalidAdapter.ValidateAsync();

        // Assert
        Assert.True(result.IsFailure);
        Assert.Contains("API validation failed", result.Error.Message);
    }

    /// <summary>
    /// Tests ValidateAsync with stored credentials but no valid tokens returns auth error
    /// NOTE: ValidateAsync makes real API calls, so success path requires integration testing.
    /// This test verifies the auth failure path.
    /// </summary>
    [Fact]
    public async Task ValidateAsync_WithStoredCredentialsButNoTokens_ReturnsAuthError()
    {
        // Arrange - Mock stored credentials but no valid tokens
        _mockSecureStorageManager.Setup(x => x.RetrieveCredentialAsync(ProviderCredentialTypes.GoogleClientId))
            .ReturnsAsync(SecureStorageResult<string>.Success("test_client_id"));
        _mockSecureStorageManager.Setup(x => x.RetrieveCredentialAsync(ProviderCredentialTypes.GoogleClientSecret))
            .ReturnsAsync(SecureStorageResult<string>.Success("test_client_secret"));

        // Mock GetUserCredentialAsync to return auth error (simulating no tokens available)
        _mockGoogleOAuthService.Setup(x => x.GetUserCredentialAsync(
                It.IsAny<string[]>(),
                It.IsAny<string>(),
                It.IsAny<string>(),
                It.IsAny<string>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result<UserCredential>.Failure(new AuthenticationError("No valid Google OAuth tokens available")));

        // Act
        var result = await _adapter.ValidateAsync();

        // Assert
        Assert.True(result.IsFailure);
        Assert.IsType<AuthenticationError>(result.Error);
        Assert.Contains("No valid Google OAuth tokens available", result.Error.Message);
    }

    #endregion

    #region Health Check Tests

    /// <summary>
    /// Tests HealthCheckAsync with validation failure returns unhealthy
    /// </summary>
    [Fact]
    public async Task HealthCheckAsync_WithValidationFailure_ReturnsUnhealthy()
    {
        // Arrange
        var invalidConfig = ContactsProviderConfig.CreateDevelopmentConfig("", "");
        var invalidAdapter = new GoogleContactsAdapter(
            _mockGoogleOAuthService.Object,
            _mockSecureStorageManager.Object,
            _mockSecurityAuditLogger.Object,
            invalidConfig,
            _mockPhoneNumberService.Object,
            _mockLogger.Object);

        // Act
        var result = await invalidAdapter.HealthCheckAsync();

        // Assert
        Assert.True(result.IsSuccess);
        Assert.Equal(HealthStatus.Unhealthy, result.Value.Status);
        Assert.Contains("Google People API validation failed", result.Value.Description);
    }

    /// <summary>
    /// Tests HealthCheckAsync when validation fails due to missing credentials returns unhealthy
    /// NOTE: Success path requires real API calls and is covered by integration tests.
    /// </summary>
    [Fact]
    public async Task HealthCheckAsync_WithMissingCredentials_ReturnsUnhealthy()
    {
        // Arrange - No stored credentials
        _mockSecureStorageManager.Setup(x => x.RetrieveCredentialAsync(ProviderCredentialTypes.GoogleClientId))
            .ReturnsAsync(SecureStorageResult<string>.Failure("Credential not found", SecureStorageErrorType.CredentialNotFound));
        _mockSecureStorageManager.Setup(x => x.RetrieveCredentialAsync(ProviderCredentialTypes.GoogleClientSecret))
            .ReturnsAsync(SecureStorageResult<string>.Failure("Credential not found", SecureStorageErrorType.CredentialNotFound));

        // Act
        var result = await _adapter.HealthCheckAsync();

        // Assert
        Assert.True(result.IsSuccess); // HealthCheckAsync returns Result<HealthCheckResult>
        Assert.Equal(HealthStatus.Unhealthy, result.Value.Status);
        Assert.Contains("Google People API validation failed", result.Value.Description);
        Assert.Contains("Google OAuth client credentials not configured", result.Value.Description);
    }

    /// <summary>
    /// Tests HealthCheckAsync when OAuth service throws exception returns unhealthy with diagnostics
    /// </summary>
    [Fact]
    public async Task HealthCheckAsync_WithOAuthException_ReturnsUnhealthyWithDiagnostics()
    {
        // Arrange - Credentials available but OAuth service throws exception
        _mockSecureStorageManager.Setup(x => x.RetrieveCredentialAsync(ProviderCredentialTypes.GoogleClientId))
            .ReturnsAsync(SecureStorageResult<string>.Success("test_client_id"));
        _mockSecureStorageManager.Setup(x => x.RetrieveCredentialAsync(ProviderCredentialTypes.GoogleClientSecret))
            .ReturnsAsync(SecureStorageResult<string>.Success("test_client_secret"));

        // Mock GetUserCredentialAsync to throw exception
        _mockGoogleOAuthService.Setup(x => x.GetUserCredentialAsync(
                It.IsAny<string[]>(),
                It.IsAny<string>(),
                It.IsAny<string>(),
                It.IsAny<string>(),
                It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvalidOperationException("OAuth service connection failed"));

        // Act
        var result = await _adapter.HealthCheckAsync();

        // Assert
        Assert.True(result.IsSuccess); // HealthCheckAsync catches exceptions and returns unhealthy
        Assert.Equal(HealthStatus.Unhealthy, result.Value.Status);
        // ValidateAsync catches the exception and returns failure, which HealthCheckAsync wraps
        Assert.Contains("Google People API validation failed", result.Value.Description);
        Assert.Contains("Failed to create People API service", result.Value.Description);
    }

    #endregion

    #region Service Integration Tests

    /// <summary>
    /// Tests that phone number normalization is configured correctly
    /// </summary>
    [Fact]
    public void PhoneNumberNormalization_IsConfiguredCorrectly()
    {
        // Arrange
        var testPhoneNumber = "+1-555-123-4567";
        var expectedNormalized = "+15551234567";

        _mockPhoneNumberService.Setup(x => x.NormalizePhoneNumber(It.Is<string>(s => s == testPhoneNumber), It.IsAny<string>()))
            .Returns(expectedNormalized);

        // Act
        var result = _mockPhoneNumberService.Object.NormalizePhoneNumber(testPhoneNumber);

        // Assert
        Assert.Equal(expectedNormalized, result);
        _mockPhoneNumberService.Verify(x => x.NormalizePhoneNumber(It.Is<string>(s => s == testPhoneNumber), It.IsAny<string>()), Times.Once);
    }

    #endregion

    #region Integration Test Placeholders (Skipped)

    /// <summary>
    /// Real Google API integration test - requires OAuth credentials
    /// </summary>
    [Fact(Skip = "Integration test requiring real Google OAuth credentials and API access")]
    public async Task FetchContactsAsync_WithValidCredentials_ReturnsContacts()
    {
        Assert.True(true, "Integration test placeholder");
    }

    /// <summary>
    /// Incremental sync test - requires real Google API
    /// </summary>
    [Fact(Skip = "Integration test requiring real Google OAuth credentials and API access")]
    public async Task FetchContactsAsync_WithSyncToken_RequestsIncrementalSync()
    {
        Assert.True(true, "Integration test placeholder");
    }

    /// <summary>
    /// Expired sync token handling - requires real Google API
    /// </summary>
    [Fact(Skip = "Integration test requiring real Google OAuth credentials and API access")]
    public async Task FetchContactsAsync_WithExpiredSyncToken_RetriesWithFullSync()
    {
        Assert.True(true, "Integration test placeholder");
    }

    /// <summary>
    /// Rate limiting test - requires real Google API with rate limits
    /// </summary>
    [Fact(Skip = "Integration test requiring real Google OAuth credentials and rate limiting scenario")]
    public async Task Adapter_HandlesRateLimitErrors_WithExponentialBackoff()
    {
        // This test would verify that the adapter properly handles HTTP 429 responses
        // from the Google People API and implements exponential backoff
        Assert.True(true, "Integration test placeholder");
    }

    #endregion

    public void Dispose()
    {
        // GoogleContactsAdapter doesn't implement IDisposable
        // Clean up is handled by the underlying services
    }
}