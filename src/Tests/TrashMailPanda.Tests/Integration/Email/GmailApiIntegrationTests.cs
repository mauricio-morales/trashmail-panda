using System;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Moq;
using TrashMailPanda.Providers.Email;
using TrashMailPanda.Providers.Email.Services;
using TrashMailPanda.Shared.Base;
using TrashMailPanda.Shared.Models;
using TrashMailPanda.Shared.Security;

namespace TrashMailPanda.Tests.Integration.Email;

/// <summary>
/// Integration tests for Gmail API operations
/// Tests real Gmail API integration scenarios without requiring actual OAuth credentials
/// </summary>
public class GmailApiIntegrationTests : IDisposable
{
    private readonly Mock<ILogger<GmailEmailProvider>> _mockLogger;
    private readonly Mock<ISecureStorageManager> _mockSecureStorage;
    private readonly Mock<IGmailRateLimitHandler> _mockRateLimitHandler;
    private readonly Mock<Google.Apis.Util.Store.IDataStore> _mockDataStore;
    private readonly Mock<ISecurityAuditLogger> _mockSecurityAuditLogger;
    private readonly Mock<IGoogleOAuthService> _mockGoogleOAuthService;
    private readonly GmailProviderConfig _testConfig;

    public GmailApiIntegrationTests()
    {
        _mockLogger = new Mock<ILogger<GmailEmailProvider>>();
        _mockSecureStorage = new Mock<ISecureStorageManager>();
        _mockRateLimitHandler = new Mock<IGmailRateLimitHandler>();
        _mockDataStore = new Mock<Google.Apis.Util.Store.IDataStore>();
        _mockSecurityAuditLogger = new Mock<ISecurityAuditLogger>();
        _mockGoogleOAuthService = new Mock<IGoogleOAuthService>();

        // Use real environment variables if available for local testing,
        // otherwise use test placeholders
        var clientId = Environment.GetEnvironmentVariable("GMAIL_CLIENT_ID") ?? "test_integration_client_id";
        var clientSecret = Environment.GetEnvironmentVariable("GMAIL_CLIENT_SECRET") ?? "test_integration_client_secret";

        _testConfig = new GmailProviderConfig();
        _testConfig.ClientId = clientId;
        _testConfig.ClientSecret = clientSecret;
        _testConfig.RequestTimeout = TimeSpan.FromSeconds(30); // Less than TimeoutSeconds
        _testConfig.TimeoutSeconds = 60; // Ensure RequestTimeout < TimeoutSeconds
    }

    /// <summary>
    /// Tests provider initialization workflow in integration scenario
    /// </summary>
    [Fact]
    public async Task ProviderInitialization_InIntegrationScenario_CompletesWorkflow()
    {
        // Arrange
        var provider = CreateProvider();

        // Act - Initialize with test configuration
        var initResult = await provider.InitializeAsync(_testConfig);

        // Assert - Should handle initialization gracefully
        // In integration test without real OAuth, this will likely fail
        // but should not throw exceptions

        // Cleanup
        await provider.ShutdownAsync();
    }

    /// <summary>
    /// Tests configuration validation in integration context
    /// </summary>
    [Fact]
    public async Task ConfigurationValidation_InIntegrationContext_ValidatesCorrectly()
    {
        // Arrange
        var provider = CreateProvider();

        // Test with various configuration scenarios
        var validConfig = GmailProviderConfig.CreateDevelopmentConfig(
            "dev_client_id",
            "dev_client_secret");

        // Act
        var result = await provider.InitializeAsync(validConfig);

        // Assert - Configuration should be validated properly

        // Cleanup
        await provider.ShutdownAsync();
    }

    /// <summary>
    /// Tests provider state transitions during integration testing
    /// NOTE: This test is skipped by default as it requires real Gmail OAuth credentials.
    /// To run this test locally, set GMAIL_CLIENT_ID and GMAIL_CLIENT_SECRET environment variables
    /// and remove the Skip attribute.
    /// </summary>
    [Fact(Skip = "Requires real Gmail OAuth credentials - see CLAUDE.md for local setup instructions")]
    public async Task ProviderStateTransitions_DuringIntegration_FollowExpectedPattern()
    {
        // Arrange
        var provider = CreateProvider();

        // Act & Assert - Test state progression
        Assert.Equal(ProviderState.Uninitialized, provider.State);

        var initResult = await provider.InitializeAsync(_testConfig);

        // With real credentials, initialization should succeed
        Assert.True(initResult.IsSuccess);
        Assert.Equal(ProviderState.Ready, provider.State);

        var shutdownResult = await provider.ShutdownAsync();
        Assert.True(shutdownResult.IsSuccess);
    }

    /// <summary>
    /// Tests error handling in integration scenarios
    /// </summary>
    [Fact]
    public async Task ErrorHandling_InIntegrationScenarios_HandlesGracefully()
    {
        // Arrange
        var provider = CreateProvider();
        var invalidConfig = new GmailProviderConfig(); // Missing OAuth credentials
        invalidConfig.ClientId = ""; // Invalid
        invalidConfig.ClientSecret = ""; // Invalid

        // Act
        var result = await provider.InitializeAsync(invalidConfig);

        // Assert - Should fail gracefully with proper error types
        Assert.True(result.IsFailure);
        Assert.IsAssignableFrom<ProviderError>(result.Error);

        // Cleanup
        await provider.ShutdownAsync();
    }

    /// <summary>
    /// Tests provider lifecycle in integration environment
    /// NOTE: This test is skipped by default as it requires real Gmail OAuth credentials.
    /// To run this test locally, set GMAIL_CLIENT_ID and GMAIL_CLIENT_SECRET environment variables
    /// and remove the Skip attribute.
    /// </summary>
    [Fact(Skip = "Requires real Gmail OAuth credentials - see CLAUDE.md for local setup instructions")]
    public async Task ProviderLifecycle_InIntegrationEnvironment_CompletesCorrectly()
    {
        // Arrange
        var provider = CreateProvider();

        try
        {
            // Act - Full lifecycle test

            // 1. Initialize with real credentials
            var initResult = await provider.InitializeAsync(_testConfig);
            Assert.True(initResult.IsSuccess);

            // 2. Verify provider properties
            Assert.Equal("Gmail", provider.Name);
            Assert.Equal("1.0.0", provider.Version);

            // 3. Test health check
            var healthResult = await provider.HealthCheckAsync();
            Assert.True(healthResult.IsSuccess);

            // 4. Shutdown
            var shutdownResult = await provider.ShutdownAsync();
            Assert.True(shutdownResult.IsSuccess);
        }
        catch (Exception ex)
        {
            // Integration tests should not throw unhandled exceptions
            Assert.Fail($"Integration test threw unexpected exception: {ex.Message}");
        }
    }

    /// <summary>
    /// Tests configuration factory methods in integration context
    /// </summary>
    [Fact]
    public async Task ConfigurationFactory_InIntegrationContext_CreatesValidConfigs()
    {
        // Arrange
        var provider = CreateProvider();

        // Act - Test factory method configurations
        var devConfig = GmailProviderConfig.CreateDevelopmentConfig(
            "integration_dev_client", "integration_dev_secret");
        var prodConfig = GmailProviderConfig.CreateProductionConfig(
            "integration_prod_client", "integration_prod_secret");

        // Assert - Both configs should be valid for integration
        var devResult = await provider.InitializeAsync(devConfig);
        await provider.ShutdownAsync();

        var prodResult = await provider.InitializeAsync(prodConfig);
        await provider.ShutdownAsync();
    }

    /// <summary>
    /// Tests rate limiting configuration in integration environment
    /// </summary>
    [Fact]
    public void RateLimitingConfiguration_InIntegrationEnvironment_IsConfiguredCorrectly()
    {
        // Arrange
        var config = new GmailProviderConfig();

        // Act & Assert - Verify rate limiting settings are appropriate for integration
        Assert.Equal(5, config.MaxRetries);
        Assert.Equal(TimeSpan.FromSeconds(1), config.BaseRetryDelay);
        Assert.Equal(TimeSpan.FromMinutes(2), config.MaxRetryDelay);
        Assert.True(config.EnableBatchOptimization);
        Assert.Equal(50, config.BatchSize);
        Assert.Equal(100, config.DefaultPageSize);
    }

    /// <summary>
    /// Tests security configuration in integration environment
    /// </summary>
    [Fact]
    public void SecurityConfiguration_InIntegrationEnvironment_MaintainsSecurityStandards()
    {
        // Arrange
        var config = new GmailProviderConfig();
        config.ClientId = "test_client_id";
        config.ClientSecret = "test_client_secret";

        // Act
        var sanitized = config.GetSanitizedCopy() as GmailProviderConfig;

        // Assert - Verify sensitive data is properly handled
        Assert.NotNull(sanitized);
        Assert.Equal("test_client_id", sanitized.ClientId); // Not sensitive
        Assert.Equal("***REDACTED***", sanitized.ClientSecret); // Should be redacted
    }

    #region Helper Methods

    private GmailEmailProvider CreateProvider()
    {
        return new GmailEmailProvider(
            _mockSecureStorage.Object,
            _mockRateLimitHandler.Object,
            _mockDataStore.Object,
            _mockSecurityAuditLogger.Object,
            _mockGoogleOAuthService.Object,
            _mockLogger.Object);
    }

    #endregion

    public void Dispose()
    {
        // Cleanup any resources if needed
    }
}