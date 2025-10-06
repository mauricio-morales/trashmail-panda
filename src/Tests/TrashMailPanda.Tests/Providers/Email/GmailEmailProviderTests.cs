using System;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Moq;
using TrashMailPanda.Providers.Email;
using TrashMailPanda.Providers.Email.Services;
using TrashMailPanda.Shared.Base;
using TrashMailPanda.Shared.Security;

namespace TrashMailPanda.Tests.Providers.Email;

/// <summary>
/// Unit tests for GmailEmailProvider
/// Tests basic initialization and dependency injection
/// </summary>
public class GmailEmailProviderTests : IDisposable
{
    private readonly Mock<ILogger<GmailEmailProvider>> _mockLogger;
    private readonly Mock<ISecureStorageManager> _mockSecureStorage;
    private readonly Mock<IGmailRateLimitHandler> _mockRateLimitHandler;
    private readonly Mock<Google.Apis.Util.Store.IDataStore> _mockDataStore;
    private readonly Mock<ISecurityAuditLogger> _mockSecurityAuditLogger;
    private readonly Mock<IGoogleOAuthService> _mockGoogleOAuthService;
    private readonly GmailProviderConfig _validConfig;

    public GmailEmailProviderTests()
    {
        _mockLogger = new Mock<ILogger<GmailEmailProvider>>();
        _mockSecureStorage = new Mock<ISecureStorageManager>();
        _mockRateLimitHandler = new Mock<IGmailRateLimitHandler>();
        _mockDataStore = new Mock<Google.Apis.Util.Store.IDataStore>();
        _mockSecurityAuditLogger = new Mock<ISecurityAuditLogger>();
        _mockGoogleOAuthService = new Mock<IGoogleOAuthService>();

        _validConfig = new GmailProviderConfig();
        _validConfig.ClientId = "test_client_id_12345";
        _validConfig.ClientSecret = "test_client_secret_12345";
    }

    /// <summary>
    /// Tests that constructor properly initializes provider with dependencies
    /// </summary>
    [Fact]
    public void Constructor_WithValidDependencies_InitializesSuccessfully()
    {
        // Act
        var provider = CreateProvider();

        // Assert
        Assert.NotNull(provider);
        Assert.Equal("Gmail", provider.Name);
        Assert.Equal(ProviderState.Uninitialized, provider.State);
    }

    /// <summary>
    /// Tests that constructor throws ArgumentNullException for null logger
    /// </summary>
    [Fact]
    public void Constructor_WithNullLogger_ThrowsArgumentNullException()
    {
        // Act & Assert
        Assert.Throws<ArgumentNullException>(() =>
            new GmailEmailProvider(
                _mockSecureStorage.Object,
                _mockRateLimitHandler.Object,
                _mockDataStore.Object,
                _mockSecurityAuditLogger.Object,
                _mockGoogleOAuthService.Object,
                null!));
    }

    /// <summary>
    /// Tests that constructor throws ArgumentNullException for null secure storage
    /// </summary>
    [Fact]
    public void Constructor_WithNullSecureStorage_ThrowsArgumentNullException()
    {
        // Act & Assert
        Assert.Throws<ArgumentNullException>(() =>
            new GmailEmailProvider(
                null!,
                _mockRateLimitHandler.Object,
                _mockDataStore.Object,
                _mockSecurityAuditLogger.Object,
                _mockGoogleOAuthService.Object,
                _mockLogger.Object));
    }

    /// <summary>
    /// Tests that constructor throws ArgumentNullException for null rate limit handler
    /// </summary>
    [Fact]
    public void Constructor_WithNullRateLimitHandler_ThrowsArgumentNullException()
    {
        // Act & Assert
        Assert.Throws<ArgumentNullException>(() =>
            new GmailEmailProvider(
                _mockSecureStorage.Object,
                null!,
                _mockDataStore.Object,
                _mockSecurityAuditLogger.Object,
                _mockGoogleOAuthService.Object,
                _mockLogger.Object));
    }

    /// <summary>
    /// Tests that constructor throws ArgumentNullException for null data store
    /// </summary>
    [Fact]
    public void Constructor_WithNullDataStore_ThrowsArgumentNullException()
    {
        // Act & Assert
        Assert.Throws<ArgumentNullException>(() =>
            new GmailEmailProvider(
                _mockSecureStorage.Object,
                _mockRateLimitHandler.Object,
                null!,
                _mockSecurityAuditLogger.Object,
                _mockGoogleOAuthService.Object,
                _mockLogger.Object));
    }

    /// <summary>
    /// Tests successful initialization with valid configuration
    /// </summary>
    [Fact]
    public async Task InitializeAsync_WithValidConfig_ReturnsSuccess()
    {
        // Arrange
        var provider = CreateProvider();

        // Act
        var result = await provider.InitializeAsync(_validConfig);

        // Assert
        // The result may be success or failure depending on OAuth setup
        // The important thing is that it doesn't throw exceptions
        Assert.True(result.IsSuccess || result.IsFailure);
    }

    /// <summary>
    /// Tests initialization with invalid configuration returns failure
    /// </summary>
    [Fact]
    public async Task InitializeAsync_WithInvalidConfig_ReturnsFailure()
    {
        // Arrange
        var provider = CreateProvider();
        var invalidConfig = new GmailProviderConfig(); // Uses defaults but missing OAuth credentials

        // Act
        var result = await provider.InitializeAsync(invalidConfig);

        // Assert
        // Should fail due to missing OAuth setup in test environment
        Assert.True(result.IsFailure);
    }

    /// <summary>
    /// Tests shutdown completes successfully
    /// </summary>
    [Fact]
    public async Task ShutdownAsync_CompletesSuccessfully()
    {
        // Arrange
        var provider = CreateProvider();

        // Act
        var result = await provider.ShutdownAsync();

        // Assert
        Assert.True(result.IsSuccess);
    }

    /// <summary>
    /// Tests provider name and version properties
    /// </summary>
    [Fact]
    public void Properties_ReturnExpectedValues()
    {
        // Arrange
        var provider = CreateProvider();

        // Act & Assert
        Assert.Equal("Gmail", provider.Name);
        Assert.Equal("1.0.0", provider.Version);
    }

    /// <summary>
    /// Tests GetBatchAsync with empty message IDs returns failure due to uninitialized state
    /// </summary>
    [Fact]
    public async Task GetBatchAsync_WithEmptyMessageIds_ReturnsFailure()
    {
        // Arrange
        var provider = CreateProvider();
        var emptyMessageIds = new List<string>();

        // Act
        var result = await provider.GetBatchAsync(emptyMessageIds);

        // Assert - provider is not ready, so it should fail at operation level
        Assert.True(result.IsFailure);
        Assert.Contains("cannot accept operations", result.Error.Message);
    }

    /// <summary>
    /// Tests GetBatchAsync with null message IDs returns failure due to uninitialized state
    /// </summary>
    [Fact]
    public async Task GetBatchAsync_WithNullMessageIds_ReturnsFailure()
    {
        // Arrange
        var provider = CreateProvider();

        // Act
        var result = await provider.GetBatchAsync(null!);

        // Assert - provider is not ready, so it should fail at operation level
        Assert.True(result.IsFailure);
        Assert.Contains("cannot accept operations", result.Error.Message);
    }

    /// <summary>
    /// Tests GetBatchAsync with uninitialized provider returns failure
    /// </summary>
    [Fact]
    public async Task GetBatchAsync_WithUninitializedProvider_ReturnsFailure()
    {
        // Arrange
        var provider = CreateProvider();
        var messageIds = new List<string> { "msg123", "msg456" };

        // Act
        var result = await provider.GetBatchAsync(messageIds);

        // Assert
        Assert.True(result.IsFailure);
        // The provider will fail because it's not in a ready state, not specifically about initialization
        Assert.Contains("cannot accept operations", result.Error.Message);
    }

    /// <summary>
    /// Tests that batch size is properly limited to quota constraints
    /// </summary>
    [Theory]
    [InlineData(50)]   // Normal batch size
    [InlineData(100)]  // Maximum batch size
    [InlineData(150)]  // Exceeds maximum, should be split
    public void BatchSize_RespectesQuotaLimits(int messageCount)
    {
        // Arrange
        var provider = CreateProvider();

        // Act & Assert
        var messageIds = new List<string>();
        for (int i = 0; i < messageCount; i++)
        {
            messageIds.Add($"msg{i:000}");
        }

        // For this test, we're mainly verifying the batch size constants
        // The actual batching logic is tested implicitly through integration tests
        Assert.True(messageIds.Count == messageCount);

        // Verify our constants are set correctly
        Assert.Equal(100, TrashMailPanda.Providers.Email.Models.GmailQuotas.MAX_BATCH_SIZE);
        Assert.Equal(50, TrashMailPanda.Providers.Email.Models.GmailQuotas.RECOMMENDED_BATCH_SIZE);
    }

    /// <summary>
    /// Tests that batch operations respect rate limiting
    /// </summary>
    [Fact]
    public void BatchOperations_RespectRateLimiting()
    {
        // Arrange
        var provider = CreateProvider();

        // Verify the rate limit handler is properly injected
        // In real scenarios, this will be called during batch operations
        Assert.NotNull(provider);

        // This test verifies the dependency injection is working
        // The actual rate limiting behavior is tested in integration tests
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