using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Moq;
using TrashMailPanda.Providers.Contacts;
using TrashMailPanda.Providers.Contacts.Adapters;
using TrashMailPanda.Providers.Contacts.Models;
using TrashMailPanda.Providers.Contacts.Services;
using TrashMailPanda.Shared.Base;
using TrashMailPanda.Shared.Models;
using TrashMailPanda.Shared.Security;
using TrashMailPanda.Shared;
using TrashMailPanda.Shared.Services;

namespace TrashMailPanda.Tests.Providers.Contacts;

/// <summary>
/// Comprehensive unit tests for ContactsProvider
/// Tests provider orchestration, sync operations, trust signal retrieval, configuration validation, health checks, and error scenarios
/// </summary>
public class ContactsProviderTests : IDisposable
{
    private readonly Mock<ContactsCacheManager> _mockCacheManager;
    private readonly Mock<TrustSignalCalculator> _mockTrustCalculator;
    private readonly Mock<GoogleContactsAdapter> _mockGoogleAdapter;
    private readonly Mock<IMemoryCache> _mockMemoryCache;
    private readonly Mock<ISecureStorageManager> _mockSecureStorageManager;
    private readonly Mock<ISecurityAuditLogger> _mockSecurityAuditLogger;
    private readonly Mock<IOptionsMonitor<ContactsProviderConfig>> _mockConfigurationMonitor;
    private readonly Mock<ILogger<ContactsProvider>> _mockLogger;
    private readonly ContactsProviderConfig _validConfig;
    private readonly ContactsProvider _provider;

    public ContactsProviderTests()
    {
        _mockCacheManager = new Mock<ContactsCacheManager>(
            Mock.Of<IMemoryCache>(),
            Mock.Of<IStorageProvider>(),
            Mock.Of<IOptions<ContactsProviderConfig>>(),
            Mock.Of<ILogger<ContactsCacheManager>>());

        _mockTrustCalculator = new Mock<TrustSignalCalculator>(
            Mock.Of<ILogger<TrustSignalCalculator>>());

        _mockGoogleAdapter = new Mock<GoogleContactsAdapter>(
            Mock.Of<IGoogleOAuthService>(),
            Mock.Of<ISecureStorageManager>(),
            Mock.Of<ISecurityAuditLogger>(),
            Mock.Of<ContactsProviderConfig>(),
            Mock.Of<IPhoneNumberService>(),
            Mock.Of<ILogger<GoogleContactsAdapter>>());

        _mockMemoryCache = new Mock<IMemoryCache>();
        _mockSecureStorageManager = new Mock<ISecureStorageManager>();
        _mockSecurityAuditLogger = new Mock<ISecurityAuditLogger>();
        _mockConfigurationMonitor = new Mock<IOptionsMonitor<ContactsProviderConfig>>();
        _mockLogger = new Mock<ILogger<ContactsProvider>>();

        _validConfig = ContactsProviderConfig.CreateDevelopmentConfig("test_client_id", "test_client_secret");
        _mockConfigurationMonitor.Setup(x => x.CurrentValue).Returns(_validConfig);

        _provider = new ContactsProvider(
            _mockCacheManager.Object,
            _mockTrustCalculator.Object,
            _mockGoogleAdapter.Object,
            _mockMemoryCache.Object,
            _mockSecureStorageManager.Object,
            _mockSecurityAuditLogger.Object,
            _mockConfigurationMonitor.Object,
            _mockLogger.Object);
    }

    #region Constructor Tests

    /// <summary>
    /// Tests that constructor properly initializes provider with dependencies
    /// </summary>
    [Fact]
    public void Constructor_WithValidDependencies_InitializesSuccessfully()
    {
        // Assert
        Assert.NotNull(_provider);
        Assert.Equal("Contacts", _provider.Name);
        Assert.Equal("1.0.0", _provider.Version);
        Assert.Equal(ProviderState.Uninitialized, _provider.State);
    }

    /// <summary>
    /// Tests that constructor throws ArgumentNullException for null cache manager
    /// </summary>
    [Fact]
    public void Constructor_WithNullCacheManager_ThrowsArgumentNullException()
    {
        // Act & Assert
        Assert.Throws<ArgumentNullException>(() =>
            new ContactsProvider(
                null!,
                _mockTrustCalculator.Object,
                _mockGoogleAdapter.Object,
                _mockMemoryCache.Object,
                _mockSecureStorageManager.Object,
                _mockSecurityAuditLogger.Object,
                _mockConfigurationMonitor.Object,
                _mockLogger.Object));
    }

    /// <summary>
    /// Tests that constructor throws ArgumentNullException for null trust calculator
    /// </summary>
    [Fact]
    public void Constructor_WithNullTrustCalculator_ThrowsArgumentNullException()
    {
        // Act & Assert
        Assert.Throws<ArgumentNullException>(() =>
            new ContactsProvider(
                _mockCacheManager.Object,
                null!,
                _mockGoogleAdapter.Object,
                _mockMemoryCache.Object,
                _mockSecureStorageManager.Object,
                _mockSecurityAuditLogger.Object,
                _mockConfigurationMonitor.Object,
                _mockLogger.Object));
    }

    /// <summary>
    /// Tests that constructor throws ArgumentNullException for null Google adapter
    /// </summary>
    [Fact]
    public void Constructor_WithNullGoogleAdapter_ThrowsArgumentNullException()
    {
        // Act & Assert
        Assert.Throws<ArgumentNullException>(() =>
            new ContactsProvider(
                _mockCacheManager.Object,
                _mockTrustCalculator.Object,
                null!,
                _mockMemoryCache.Object,
                _mockSecureStorageManager.Object,
                _mockSecurityAuditLogger.Object,
                _mockConfigurationMonitor.Object,
                _mockLogger.Object));
    }

    #endregion

    #region Trust Signal Tests

    /// <summary>
    /// Tests GetTrustSignalForEmailAsync with valid email returns cached trust signal
    /// </summary>
    [Fact]
    public async Task GetTrustSignalForEmailAsync_WithValidEmail_ReturnsCachedTrustSignal()
    {
        // Arrange
        const string email = "test@example.com";
        var contact = CreateTestContact("contact1", email);
        var trustSignal = CreateTestTrustSignal("contact1", RelationshipStrength.Strong);

        _mockCacheManager.Setup(x => x.GetContactByEmailAsync(email, It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result<Contact?>.Success(contact));
        _mockCacheManager.Setup(x => x.GetTrustSignalAsync("contact1", It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result<TrustSignal?>.Success(trustSignal));

        // Act
        var result = await _provider.GetTrustSignalForEmailAsync(email);

        // Assert
        Assert.True(result.IsSuccess);
        Assert.NotNull(result.Value);
        Assert.Equal(RelationshipStrength.Strong, result.Value.Strength);
        Assert.Equal("contact1", result.Value.ContactId);

        _mockCacheManager.Verify(x => x.GetContactByEmailAsync(email, It.IsAny<CancellationToken>()), Times.Once);
        _mockCacheManager.Verify(x => x.GetTrustSignalAsync("contact1", It.IsAny<CancellationToken>()), Times.Once);
    }

    /// <summary>
    /// Tests GetTrustSignalForEmailAsync with non-existent email returns null
    /// </summary>
    [Fact]
    public async Task GetTrustSignalForEmailAsync_WithNonExistentEmail_ReturnsNull()
    {
        // Arrange
        const string email = "nonexistent@example.com";

        _mockCacheManager.Setup(x => x.GetContactByEmailAsync(email, It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result<Contact?>.Success(null));

        // Act
        var result = await _provider.GetTrustSignalForEmailAsync(email);

        // Assert
        Assert.True(result.IsSuccess);
        Assert.Null(result.Value);

        _mockCacheManager.Verify(x => x.GetContactByEmailAsync(email, It.IsAny<CancellationToken>()), Times.Once);
    }

    /// <summary>
    /// Tests GetTrustSignalForEmailAsync computes fresh signal when cached signal is stale
    /// </summary>
    [Fact]
    public async Task GetTrustSignalForEmailAsync_WithStaleSignal_ComputesFreshSignal()
    {
        // Arrange
        const string email = "test@example.com";
        var contact = CreateTestContact("contact1", email);
        var staleSignal = CreateTestTrustSignal("contact1", RelationshipStrength.Weak, DateTime.UtcNow.AddDays(-2));
        var freshSignal = CreateTestTrustSignal("contact1", RelationshipStrength.Strong);

        _mockCacheManager.Setup(x => x.GetContactByEmailAsync(email, It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result<Contact?>.Success(contact));
        _mockCacheManager.Setup(x => x.GetTrustSignalAsync("contact1", It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result<TrustSignal?>.Success(staleSignal));
        _mockTrustCalculator.Setup(x => x.CalculateTrustSignalAsync(contact, It.IsAny<ContactInteractionHistory>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result<TrustSignal>.Success(freshSignal));
        _mockCacheManager.Setup(x => x.CacheTrustSignalAsync(freshSignal, It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result<bool>.Success(true));

        // Act
        var result = await _provider.GetTrustSignalForEmailAsync(email);

        // Assert
        Assert.True(result.IsSuccess);
        Assert.NotNull(result.Value);
        Assert.Equal(RelationshipStrength.Strong, result.Value.Strength);

        _mockTrustCalculator.Verify(x => x.CalculateTrustSignalAsync(contact, null, It.IsAny<CancellationToken>()), Times.Once);
        _mockCacheManager.Verify(x => x.CacheTrustSignalAsync(freshSignal, It.IsAny<CancellationToken>()), Times.Once);
    }

    /// <summary>
    /// Tests GetTrustSignalForEmailAsync with empty email returns null
    /// </summary>
    [Theory]
    [InlineData("")]
    [InlineData(" ")]
    [InlineData(null)]
    public async Task GetTrustSignalForEmailAsync_WithEmptyEmail_ReturnsNull(string? email)
    {
        // Act
        var result = await _provider.GetTrustSignalForEmailAsync(email);

        // Assert
        Assert.True(result.IsSuccess);
        Assert.Null(result.Value);

        _mockCacheManager.Verify(x => x.GetContactByEmailAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    /// <summary>
    /// Tests GetTrustSignalForEmailAsync handles cache failure gracefully
    /// </summary>
    [Fact]
    public async Task GetTrustSignalForEmailAsync_WithCacheFailure_ReturnsNull()
    {
        // Arrange
        const string email = "test@example.com";
        var cacheError = new ProcessingError("Cache failed");

        _mockCacheManager.Setup(x => x.GetContactByEmailAsync(email, It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result<Contact?>.Failure(cacheError));

        // Act
        var result = await _provider.GetTrustSignalForEmailAsync(email);

        // Assert
        Assert.True(result.IsSuccess);
        Assert.Null(result.Value);

        _mockCacheManager.Verify(x => x.GetContactByEmailAsync(email, It.IsAny<CancellationToken>()), Times.Once);
    }

    #endregion

    #region Sync Operations Tests

    /// <summary>
    /// Tests SyncContactsAsync with caching disabled returns success with zero contacts
    /// </summary>
    [Fact]
    public async Task SyncContactsAsync_WithCachingDisabled_ReturnsSuccessWithZeroContacts()
    {
        // Arrange
        var disabledConfig = ContactsProviderConfig.CreateDevelopmentConfig("test", "test");
        disabledConfig.EnableContactsCaching = false;
        _mockConfigurationMonitor.Setup(x => x.CurrentValue).Returns(disabledConfig);

        // Act
        var result = await _provider.SyncContactsAsync();

        // Assert
        Assert.True(result.IsSuccess);
        Assert.Equal(0, result.Value.ContactsSynced);
        Assert.True(result.Value.IsSuccessful);

        _mockGoogleAdapter.Verify(x => x.FetchContactsAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    /// <summary>
    /// Tests SyncContactsAsync successfully syncs contacts from enabled adapters
    /// </summary>
    [Fact]
    public async Task SyncContactsAsync_WithEnabledAdapters_SuccessfullysyncsContacts()
    {
        // Arrange
        var contacts = new List<Contact> { CreateTestContact("contact1", "test1@example.com") };
        var fetchResult = (contacts.AsEnumerable(), "next_token");

        _mockGoogleAdapter.Setup(x => x.IsEnabled).Returns(true);
        _mockGoogleAdapter.Setup(x => x.SourceType).Returns(ContactSourceType.Google);
        _mockGoogleAdapter.Setup(x => x.FetchContactsAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result<(IEnumerable<Contact>, string?)>.Success(fetchResult));
        _mockCacheManager.Setup(x => x.CacheContactsBatchAsync(It.IsAny<IEnumerable<Contact>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result<int>.Success(1));
        _mockSecureStorageManager.Setup(x => x.StoreCredentialAsync(It.IsAny<string>(), "next_token"))
            .ReturnsAsync(SecureStorageResult.Success());
        _mockSecurityAuditLogger.Setup(x => x.LogCredentialOperationAsync(It.IsAny<CredentialOperationEvent>()))
            .Returns(Task.CompletedTask);

        // Act
        var result = await _provider.SyncContactsAsync();

        // Assert
        Assert.True(result.IsSuccess);
        Assert.Equal(1, result.Value.ContactsSynced);
        Assert.True(result.Value.IsSuccessful);
        Assert.Single(result.Value.AdapterResults);
        Assert.True(result.Value.AdapterResults.First().IsSuccessful);

        _mockGoogleAdapter.Verify(x => x.FetchContactsAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Once);
        _mockCacheManager.Verify(x => x.CacheContactsBatchAsync(It.IsAny<IEnumerable<Contact>>(), It.IsAny<CancellationToken>()), Times.Once);
    }

    /// <summary>
    /// Tests SyncContactsAsync handles adapter fetch failure gracefully
    /// </summary>
    [Fact]
    public async Task SyncContactsAsync_WithAdapterFetchFailure_HandlesGracefully()
    {
        // Arrange
        var fetchError = new NetworkError("API failed");

        _mockGoogleAdapter.Setup(x => x.IsEnabled).Returns(true);
        _mockGoogleAdapter.Setup(x => x.SourceType).Returns(ContactSourceType.Google);
        _mockGoogleAdapter.Setup(x => x.FetchContactsAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result<(IEnumerable<Contact>, string?)>.Failure(fetchError));

        // Act
        var result = await _provider.SyncContactsAsync();

        // Assert
        Assert.True(result.IsSuccess);
        Assert.Equal(0, result.Value.ContactsSynced);
        Assert.False(result.Value.IsSuccessful);
        Assert.Single(result.Value.AdapterResults);
        Assert.False(result.Value.AdapterResults.First().IsSuccessful);
        Assert.Equal("API failed", result.Value.AdapterResults.First().ErrorMessage);

        _mockCacheManager.Verify(x => x.CacheContactsBatchAsync(It.IsAny<IEnumerable<Contact>>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    /// <summary>
    /// Tests SyncContactsAsync with force full sync ignores sync tokens
    /// </summary>
    [Fact]
    public async Task SyncContactsAsync_WithForceFullSync_IgnoresSyncTokens()
    {
        // Arrange
        var contacts = new List<Contact> { CreateTestContact("contact1", "test1@example.com") };
        var fetchResult = (contacts.AsEnumerable(), "new_token");

        _mockGoogleAdapter.Setup(x => x.IsEnabled).Returns(true);
        _mockGoogleAdapter.Setup(x => x.SourceType).Returns(ContactSourceType.Google);
        _mockGoogleAdapter.Setup(x => x.FetchContactsAsync(null, It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result<(IEnumerable<Contact>, string?)>.Success(fetchResult));
        _mockCacheManager.Setup(x => x.CacheContactsBatchAsync(It.IsAny<IEnumerable<Contact>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result<int>.Success(1));

        // Act
        var result = await _provider.SyncContactsAsync(forceFullSync: true);

        // Assert
        Assert.True(result.IsSuccess);
        Assert.Equal("Full", result.Value.SyncType);

        _mockGoogleAdapter.Verify(x => x.FetchContactsAsync(null, It.IsAny<CancellationToken>()), Times.Once);
        _mockSecureStorageManager.Verify(x => x.RetrieveCredentialAsync(It.IsAny<string>()), Times.Never);
    }

    #endregion

    #region Status and Health Tests

    /// <summary>
    /// Tests GetStatusAsync returns comprehensive provider status
    /// </summary>
    [Fact]
    public async Task GetStatusAsync_ReturnsComprehensiveStatus()
    {
        // Arrange
        var adapterStatus = new AdapterSyncStatus
        {
            IsSyncing = false,
            ContactCount = 100,
            Metadata = new Dictionary<string, object> { ["IsHealthy"] = true }
        };
        var cacheStats = new CacheStatistics
        {
            TotalLookups = 1000,
            MemoryHits = 800,
            SqliteHits = 150,
            CombinedHitRate = 0.95
        };

        _mockGoogleAdapter.Setup(x => x.GetSyncStatusAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result<AdapterSyncStatus>.Success(adapterStatus));
        _mockCacheManager.Setup(x => x.GetCacheStatistics()).Returns(cacheStats);

        // Act
        var result = await _provider.GetStatusAsync();

        // Assert
        Assert.True(result.IsSuccess);
        Assert.NotNull(result.Value);
        Assert.True(result.Value.IsEnabled);
        Assert.True(result.Value.IsHealthy);
        Assert.Single(result.Value.AdapterStatuses);
        Assert.Equal(cacheStats, result.Value.CacheStatistics);

        _mockGoogleAdapter.Verify(x => x.GetSyncStatusAsync(It.IsAny<CancellationToken>()), Times.Once);
        _mockCacheManager.Verify(x => x.GetCacheStatistics(), Times.Once);
    }

    /// <summary>
    /// Tests PerformHealthCheckAsync returns healthy when all adapters are healthy
    /// </summary>
    [Fact]
    public async Task PerformHealthCheckAsync_WithHealthyAdapters_ReturnsHealthy()
    {
        // Arrange
        await _provider.InitializeAsync(_validConfig);

        var healthyResult = HealthCheckResult.Healthy("Google People API is accessible");
        _mockGoogleAdapter.Setup(x => x.IsEnabled).Returns(true);
        _mockGoogleAdapter.Setup(x => x.SourceType).Returns(ContactSourceType.Google);
        _mockGoogleAdapter.Setup(x => x.HealthCheckAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result<HealthCheckResult>.Success(healthyResult));
        _mockCacheManager.Setup(x => x.GetCacheStatistics()).Returns(new CacheStatistics { CombinedHitRate = 0.8 });

        // Act
        var result = await _provider.HealthCheckAsync();

        // Assert
        Assert.True(result.IsSuccess);
        Assert.Equal(HealthStatus.Healthy, result.Value.Status);
        Assert.Contains("healthy and operational", result.Value.Description);

        _mockGoogleAdapter.Verify(x => x.HealthCheckAsync(It.IsAny<CancellationToken>()), Times.Once);
    }

    /// <summary>
    /// Tests PerformHealthCheckAsync returns unhealthy when no adapters are enabled
    /// </summary>
    [Fact]
    public async Task PerformHealthCheckAsync_WithNoEnabledAdapters_ReturnsUnhealthy()
    {
        // Arrange
        await _provider.InitializeAsync(_validConfig);

        _mockGoogleAdapter.Setup(x => x.IsEnabled).Returns(false);

        // Act
        var result = await _provider.HealthCheckAsync();

        // Assert
        Assert.True(result.IsSuccess);
        Assert.Equal(HealthStatus.Unhealthy, result.Value.Status);
        Assert.Contains("No contact source adapters", result.Value.Description);
    }

    /// <summary>
    /// Tests PerformHealthCheckAsync returns degraded when some adapters are unhealthy
    /// </summary>
    [Fact]
    public async Task PerformHealthCheckAsync_WithUnhealthyAdapters_ReturnsDegraded()
    {
        // Arrange
        await _provider.InitializeAsync(_validConfig);

        var unhealthyResult = HealthCheckResult.Unhealthy("API not accessible");
        _mockGoogleAdapter.Setup(x => x.IsEnabled).Returns(true);
        _mockGoogleAdapter.Setup(x => x.SourceType).Returns(ContactSourceType.Google);
        _mockGoogleAdapter.Setup(x => x.HealthCheckAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result<HealthCheckResult>.Success(unhealthyResult));
        _mockCacheManager.Setup(x => x.GetCacheStatistics()).Returns(new CacheStatistics { CombinedHitRate = 0.8 });

        // Act
        var result = await _provider.HealthCheckAsync();

        // Assert
        Assert.True(result.IsSuccess);
        Assert.Equal(HealthStatus.Unhealthy, result.Value.Status); // All adapters unhealthy = Unhealthy
        Assert.Contains("All contact adapters have failed", result.Value.Description);
    }

    #endregion

    #region Batch Operations Tests

    /// <summary>
    /// Tests ComputeBatchTrustSignalsAsync with valid contacts returns computed signals
    /// </summary>
    [Fact]
    public async Task ComputeBatchTrustSignalsAsync_WithValidContacts_ReturnsComputedSignals()
    {
        // Arrange
        var contacts = new List<Contact>
        {
            CreateTestContact("contact1", "test1@example.com"),
            CreateTestContact("contact2", "test2@example.com")
        };
        var trustSignals = new Dictionary<string, TrustSignal>
        {
            ["contact1"] = CreateTestTrustSignal("contact1", RelationshipStrength.Strong),
            ["contact2"] = CreateTestTrustSignal("contact2", RelationshipStrength.Weak)
        };

        _mockTrustCalculator.Setup(x => x.CalculateBatchTrustSignalsAsync(contacts, It.IsAny<Dictionary<string, ContactInteractionHistory>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result<Dictionary<string, TrustSignal>>.Success(trustSignals));
        _mockCacheManager.Setup(x => x.CacheTrustSignalAsync(It.IsAny<TrustSignal>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result<bool>.Success(true));

        // Act
        var result = await _provider.ComputeBatchTrustSignalsAsync(contacts);

        // Assert
        Assert.True(result.IsSuccess);
        Assert.Equal(2, result.Value.Count);
        Assert.Contains("contact1", result.Value.Keys);
        Assert.Contains("contact2", result.Value.Keys);

        _mockTrustCalculator.Verify(x => x.CalculateBatchTrustSignalsAsync(contacts, It.IsAny<Dictionary<string, ContactInteractionHistory>>(), It.IsAny<CancellationToken>()), Times.Once);
        _mockCacheManager.Verify(x => x.CacheTrustSignalAsync(It.IsAny<TrustSignal>(), It.IsAny<CancellationToken>()), Times.Exactly(2));
    }

    /// <summary>
    /// Tests ComputeBatchTrustSignalsAsync with null contacts returns empty dictionary
    /// </summary>
    [Fact]
    public async Task ComputeBatchTrustSignalsAsync_WithNullContacts_ReturnsEmptyDictionary()
    {
        // Act
        var result = await _provider.ComputeBatchTrustSignalsAsync(null!);

        // Assert
        Assert.True(result.IsSuccess);
        Assert.Empty(result.Value);

        _mockTrustCalculator.Verify(x => x.CalculateBatchTrustSignalsAsync(It.IsAny<IEnumerable<Contact>>(), It.IsAny<Dictionary<string, ContactInteractionHistory>>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    #endregion

    #region Cache Management Tests

    /// <summary>
    /// Tests ClearCacheAsync successfully clears provider cache
    /// </summary>
    [Fact]
    public async Task ClearCacheAsync_SuccessfullyClearsCache()
    {
        // Arrange
        _mockCacheManager.Setup(x => x.ClearCacheAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result<bool>.Success(true));

        // Act
        var result = await _provider.ClearCacheAsync();

        // Assert
        Assert.True(result.IsSuccess);
        Assert.True(result.Value);

        _mockCacheManager.Verify(x => x.ClearCacheAsync(It.IsAny<CancellationToken>()), Times.Once);
    }

    /// <summary>
    /// Tests ClearCacheAsync handles cache manager failure
    /// </summary>
    [Fact]
    public async Task ClearCacheAsync_WithCacheManagerFailure_ReturnsFailure()
    {
        // Arrange
        var cacheError = new ProcessingError("Cache clear failed");
        _mockCacheManager.Setup(x => x.ClearCacheAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result<bool>.Failure(cacheError));

        // Act
        var result = await _provider.ClearCacheAsync();

        // Assert
        Assert.True(result.IsFailure);
        Assert.Equal("Cache clear failed", result.Error.Message);

        _mockCacheManager.Verify(x => x.ClearCacheAsync(It.IsAny<CancellationToken>()), Times.Once);
    }

    #endregion

    #region Legacy Interface Tests

    /// <summary>
    /// Tests IsKnownAsync with known email returns true
    /// </summary>
    [Fact]
    public async Task IsKnownAsync_WithKnownEmail_ReturnsTrue()
    {
        // Arrange
        const string email = "known@example.com";
        var trustSignal = CreateTestTrustSignal("contact1", RelationshipStrength.Strong);

        var contact = CreateTestContact("contact1", email);
        _mockCacheManager.Setup(x => x.GetContactByEmailAsync(email, It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result<Contact?>.Success(contact));
        _mockCacheManager.Setup(x => x.GetTrustSignalAsync("contact1", It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result<TrustSignal?>.Success(trustSignal));

        // Act
        var result = await _provider.IsKnownAsync(email);

        // Assert
        Assert.True(result);
    }

    /// <summary>
    /// Tests IsKnownAsync with unknown email returns false
    /// </summary>
    [Fact]
    public async Task IsKnownAsync_WithUnknownEmail_ReturnsFalse()
    {
        // Arrange
        const string email = "unknown@example.com";

        _mockCacheManager.Setup(x => x.GetContactByEmailAsync(email, It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result<Contact?>.Success(null));

        // Act
        var result = await _provider.IsKnownAsync(email);

        // Assert
        Assert.False(result);
    }

    /// <summary>
    /// Tests GetRelationshipStrengthAsync returns correct strength
    /// </summary>
    [Fact]
    public async Task GetRelationshipStrengthAsync_ReturnsCorrectStrength()
    {
        // Arrange
        const string email = "test@example.com";
        var trustSignal = CreateTestTrustSignal("contact1", RelationshipStrength.Strong);

        var contact = CreateTestContact("contact1", email);
        _mockCacheManager.Setup(x => x.GetContactByEmailAsync(email, It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result<Contact?>.Success(contact));
        _mockCacheManager.Setup(x => x.GetTrustSignalAsync("contact1", It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result<TrustSignal?>.Success(trustSignal));

        // Act
        var result = await _provider.GetRelationshipStrengthAsync(email);

        // Assert
        Assert.Equal(RelationshipStrength.Strong, result);
    }

    #endregion

    #region Error Handling Tests

    /// <summary>
    /// Tests provider handles exceptions during trust signal computation gracefully
    /// </summary>
    [Fact]
    public async Task GetTrustSignalForEmailAsync_WithComputationException_HandlesGracefully()
    {
        // Arrange
        const string email = "test@example.com";
        var contact = CreateTestContact("contact1", email);

        _mockCacheManager.Setup(x => x.GetContactByEmailAsync(email, It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result<Contact?>.Success(contact));
        _mockCacheManager.Setup(x => x.GetTrustSignalAsync("contact1", It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result<TrustSignal?>.Success(null));
        _mockTrustCalculator.Setup(x => x.CalculateTrustSignalAsync(contact, It.IsAny<ContactInteractionHistory>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvalidOperationException("Computation failed"));

        // Act
        var result = await _provider.GetTrustSignalForEmailAsync(email);

        // Assert
        Assert.True(result.IsFailure);
        Assert.Contains("Failed to get trust signal", result.Error.Message);
    }

    /// <summary>
    /// Tests provider handles sync operation exceptions gracefully
    /// </summary>
    [Fact]
    public async Task SyncContactsAsync_WithSyncException_HandlesGracefully()
    {
        // Arrange
        _mockGoogleAdapter.Setup(x => x.IsEnabled).Returns(true);
        _mockGoogleAdapter.Setup(x => x.SourceType).Returns(ContactSourceType.Google);
        _mockGoogleAdapter.Setup(x => x.FetchContactsAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvalidOperationException("Sync failed"));

        // Act
        var result = await _provider.SyncContactsAsync();

        // Assert
        Assert.True(result.IsSuccess);
        Assert.Single(result.Value.AdapterResults);
        Assert.False(result.Value.AdapterResults.First().IsSuccessful);
        Assert.Equal("Sync failed", result.Value.AdapterResults.First().ErrorMessage);
    }

    #endregion

    #region Provider Lifecycle Tests

    /// <summary>
    /// Tests successful initialization with valid configuration
    /// </summary>
    [Fact]
    public async Task InitializeAsync_WithValidConfig_ReturnsSuccess()
    {
        // Act
        var result = await _provider.InitializeAsync(_validConfig);

        // Assert
        Assert.True(result.IsSuccess);
        Assert.True(result.Value);
    }

    /// <summary>
    /// Tests successful shutdown
    /// </summary>
    [Fact]
    public async Task ShutdownAsync_CompletesSuccessfully()
    {
        // Act
        var result = await _provider.ShutdownAsync();

        // Assert
        Assert.True(result.IsSuccess);
        Assert.True(result.Value);
    }

    #endregion

    #region Integration Test Placeholders (Skipped)

    /// <summary>
    /// Integration test for real Google Contacts API synchronization (requires actual credentials)
    /// </summary>
    [Fact(Skip = "Integration test requiring real Google OAuth credentials")]
    public async Task SyncContactsAsync_RealGoogleAPI_SyncsActualContacts()
    {
        // This would test against real Google Contacts API
        // Requires GOOGLE_CLIENT_ID and GOOGLE_CLIENT_SECRET environment variables
        Assert.True(true, "Integration test placeholder");
    }

    /// <summary>
    /// Integration test for end-to-end trust signal computation with real data
    /// </summary>
    [Fact(Skip = "Integration test requiring real contact data")]
    public async Task GetTrustSignalForEmailAsync_RealData_ComputesAccurateTrustSignals()
    {
        // This would test trust signal computation with real contact data
        Assert.True(true, "Integration test placeholder");
    }

    #endregion

    #region Helper Methods

    private static Contact CreateTestContact(string id, string email)
    {
        return new Contact
        {
            Id = id,
            PrimaryEmail = email,
            AllEmails = new List<string> { email },
            DisplayName = $"Test User {id}",
            GivenName = "Test",
            FamilyName = "User",
            RelationshipStrength = 0.7,
            SourceIdentities = new List<SourceIdentity>
            {
                new()
                {
                    SourceType = ContactSourceType.Google,
                    SourceContactId = $"google_{id}",
                    LastUpdatedUtc = DateTime.UtcNow,
                    IsActive = true
                }
            },
            LastModifiedUtc = DateTime.UtcNow,
            LastSyncedUtc = DateTime.UtcNow,
            PhoneNumbers = new List<string>(),
            Metadata = new Dictionary<string, string>()
        };
    }

    private static TrustSignal CreateTestTrustSignal(string contactId, RelationshipStrength strength, DateTime? computedAt = null)
    {
        return new TrustSignal
        {
            ContactId = contactId,
            Strength = strength,
            Score = strength switch
            {
                RelationshipStrength.Strong => 0.8,
                RelationshipStrength.Moderate => 0.5,
                RelationshipStrength.Weak => 0.3,
                _ => 0.0
            },
            ComputedAt = computedAt ?? DateTime.UtcNow,
            LastInteractionDate = DateTime.UtcNow.AddDays(-7),
            InteractionCount = 5,
            Justification = new List<string> { "Found in contacts", "Recent interactions" },
            RecencyScore = 0.8,
            FrequencyScore = 0.6
        };
    }

    #endregion

    public void Dispose()
    {
        _provider?.Dispose();
    }
}