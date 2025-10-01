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
using TrashMailPanda.Providers.Contacts.Models;
using TrashMailPanda.Providers.Contacts.Services;
using TrashMailPanda.Shared.Base;
using TrashMailPanda.Shared.Models;
using TrashMailPanda.Shared;
using Xunit;

namespace TrashMailPanda.Tests.Providers.Contacts;

/// <summary>
/// Comprehensive unit tests for ContactsCacheManager
/// Tests 3-layer caching behavior, cache hits/misses, invalidation, memory/SQLite integration, and performance metrics
/// </summary>
public class ContactsCacheManagerTests : IDisposable
{
    private readonly IMemoryCache _memoryCache;
    private readonly Mock<IStorageProvider> _mockStorageProvider;
    private readonly Mock<IOptions<ContactsProviderConfig>> _mockConfigOptions;
    private readonly Mock<ILogger<ContactsCacheManager>> _mockLogger;
    private readonly ContactsProviderConfig _config;
    private readonly ContactsCacheManager _cacheManager;

    public ContactsCacheManagerTests()
    {
        // Use real MemoryCache since extension methods can't be mocked
        _memoryCache = new MemoryCache(new MemoryCacheOptions());
        _mockStorageProvider = new Mock<IStorageProvider>();
        _mockConfigOptions = new Mock<IOptions<ContactsProviderConfig>>();
        _mockLogger = new Mock<ILogger<ContactsCacheManager>>();

        _config = ContactsProviderConfig.CreateDevelopmentConfig("test_client_id", "test_client_secret");
        _config.Cache.MemoryTtl = TimeSpan.FromMinutes(15);
        _mockConfigOptions.Setup(x => x.Value).Returns(_config);

        _cacheManager = new ContactsCacheManager(
            _memoryCache,
            _mockStorageProvider.Object,
            _mockConfigOptions.Object,
            _mockLogger.Object);
    }

    #region Constructor Tests

    /// <summary>
    /// Tests that constructor properly initializes cache manager with dependencies
    /// </summary>
    [Fact]
    public void Constructor_WithValidDependencies_InitializesSuccessfully()
    {
        // Assert
        Assert.NotNull(_cacheManager);
    }

    /// <summary>
    /// Tests that constructor throws ArgumentNullException for null memory cache
    /// </summary>
    [Fact]
    public void Constructor_WithNullMemoryCache_ThrowsArgumentNullException()
    {
        // Act & Assert
        Assert.Throws<ArgumentNullException>(() =>
            new ContactsCacheManager(
                null!,
                _mockStorageProvider.Object,
                _mockConfigOptions.Object,
                _mockLogger.Object));
    }

    /// <summary>
    /// Tests that constructor throws ArgumentNullException for null storage provider
    /// </summary>
    [Fact]
    public void Constructor_WithNullStorageProvider_ThrowsArgumentNullException()
    {
        // Act & Assert
        Assert.Throws<ArgumentNullException>(() =>
            new ContactsCacheManager(
                _memoryCache,
                null!,
                _mockConfigOptions.Object,
                _mockLogger.Object));
    }

    /// <summary>
    /// Tests that constructor throws ArgumentNullException for null configuration
    /// </summary>
    [Fact]
    public void Constructor_WithNullConfig_ThrowsArgumentNullException()
    {
        // Act & Assert
        Assert.Throws<ArgumentNullException>(() =>
            new ContactsCacheManager(
                _memoryCache,
                _mockStorageProvider.Object,
                null!,
                _mockLogger.Object));
    }

    #endregion

    #region Layer 1 Memory Cache Tests

    /// <summary>
    /// Tests GetContactByIdAsync with memory cache hit returns contact from Layer 1
    /// </summary>
    [Fact]
    public async Task GetContactByIdAsync_WithMemoryCacheHit_ReturnsContactFromLayer1()
    {
        // Arrange
        const string contactId = "contact123";
        var cachedContact = CreateTestContact(contactId, "test@example.com");
        var cacheKey = $"contact:{contactId}";

        // Pre-populate memory cache
        _memoryCache.Set(cacheKey, cachedContact, _config.Cache.MemoryTtl);

        // Act
        var result = await _cacheManager.GetContactByIdAsync(contactId);

        // Assert
        Assert.True(result.IsSuccess);
        Assert.NotNull(result.Value);
        Assert.Equal(contactId, result.Value.Id);
        Assert.Equal("test@example.com", result.Value.PrimaryEmail);

        // Verify storage provider was NOT called (memory cache hit)
        _mockStorageProvider.Verify(x => x.GetContactAsync(It.IsAny<string>()), Times.Never);
    }

    /// <summary>
    /// Tests GetContactByEmailAsync with memory cache hit returns contact via Layer 1 email index
    /// </summary>
    [Fact]
    public async Task GetContactByEmailAsync_WithMemoryCacheHit_ReturnsContactViaLayer1EmailIndex()
    {
        // Arrange
        const string email = "test@example.com";
        const string contactId = "contact123";
        var cachedContact = CreateTestContact(contactId, email);
        var emailIndexKey = $"email_idx:{email}";
        var contactKey = $"contact:{contactId}";

        // Pre-populate memory cache with both email index and contact
        _memoryCache.Set(emailIndexKey, contactId, _config.Cache.MemoryTtl);
        _memoryCache.Set(contactKey, cachedContact, _config.Cache.MemoryTtl);

        // Act
        var result = await _cacheManager.GetContactByEmailAsync(email);

        // Assert
        Assert.True(result.IsSuccess);
        Assert.NotNull(result.Value);
        Assert.Equal(contactId, result.Value.Id);
        Assert.Equal(email, result.Value.PrimaryEmail);

        // Verify storage provider was NOT called (memory cache hit)
        _mockStorageProvider.Verify(x => x.GetContactIdByEmailAsync(It.IsAny<string>()), Times.Never);
        _mockStorageProvider.Verify(x => x.GetContactAsync(It.IsAny<string>()), Times.Never);
    }

    /// <summary>
    /// Tests CacheContactAsync stores contact in memory Layer 1
    /// </summary>
    [Fact]
    public async Task CacheContactAsync_StoresContactInMemoryLayer1()
    {
        // Arrange
        var contact = CreateTestContact("contact123", "test@example.com");
        var contactKey = $"contact:{contact.Id}";
        var emailIndexKey = $"email_idx:{contact.PrimaryEmail}";

        SetupSuccessfulSQLiteOperations();

        // Act
        var result = await _cacheManager.CacheContactAsync(contact);

        // Assert
        Assert.True(result.IsSuccess);
        Assert.True(result.Value);

        // Verify memory cache was populated
        var cachedContact = _memoryCache.Get<Contact>(contactKey);
        Assert.NotNull(cachedContact);
        Assert.Equal(contact.Id, cachedContact.Id);
        Assert.Equal(contact.PrimaryEmail, cachedContact.PrimaryEmail);

        var cachedContactId = _memoryCache.Get<string>(emailIndexKey);
        Assert.Equal(contact.Id, cachedContactId);

        // Verify SQLite cache was also used
        _mockStorageProvider.Verify(x => x.SetContactAsync(It.IsAny<BasicContactInfo>()), Times.Once);
    }

    #endregion

    #region Layer 2 SQLite Cache Tests

    /// <summary>
    /// Tests GetContactByIdAsync with memory miss but SQLite hit returns contact from Layer 2
    /// </summary>
    [Fact]
    public async Task GetContactByIdAsync_WithMemoryMissButSQLiteHit_ReturnsContactFromLayer2()
    {
        // Arrange
        const string contactId = "contact123";
        var sqliteContact = CreateTestBasicContactInfo(contactId, "test@example.com");
        var contactKey = $"contact:{contactId}";

        // Ensure memory cache is empty (miss)
        _memoryCache.Remove(contactKey);

        // SQLite cache hit
        _mockStorageProvider.Setup(x => x.GetContactAsync(contactId))
            .ReturnsAsync(sqliteContact);

        // Act
        var result = await _cacheManager.GetContactByIdAsync(contactId);

        // Assert
        Assert.True(result.IsSuccess);
        Assert.NotNull(result.Value);
        Assert.Equal(contactId, result.Value.Id);
        Assert.Equal("test@example.com", result.Value.PrimaryEmail);

        // Verify SQLite hit
        _mockStorageProvider.Verify(x => x.GetContactAsync(contactId), Times.Once);

        // Verify contact was cached back to memory
        var memoryCachedContact = _memoryCache.Get<Contact>(contactKey);
        Assert.NotNull(memoryCachedContact);
        Assert.Equal(contactId, memoryCachedContact.Id);
    }

    /// <summary>
    /// Tests GetContactByEmailAsync with memory miss but SQLite hit via email index
    /// </summary>
    [Fact]
    public async Task GetContactByEmailAsync_WithMemoryMissButSQLiteHit_ReturnsContactViaLayer2EmailIndex()
    {
        // Arrange
        const string email = "test@example.com";
        const string contactId = "contact123";
        var sqliteContact = CreateTestBasicContactInfo(contactId, email);
        var emailIndexKey = $"email_idx:{email}";
        var contactKey = $"contact:{contactId}";

        // Ensure memory cache is empty (miss)
        _memoryCache.Remove(emailIndexKey);
        _memoryCache.Remove(contactKey);

        // SQLite cache hit for email index
        _mockStorageProvider.Setup(x => x.GetContactIdByEmailAsync(email))
            .ReturnsAsync(contactId);
        _mockStorageProvider.Setup(x => x.GetContactAsync(contactId))
            .ReturnsAsync(sqliteContact);

        // Act
        var result = await _cacheManager.GetContactByEmailAsync(email);

        // Assert
        Assert.True(result.IsSuccess);
        Assert.NotNull(result.Value);
        Assert.Equal(contactId, result.Value.Id);
        Assert.Equal(email, result.Value.PrimaryEmail);

        // Verify email index lookup and contact retrieval
        _mockStorageProvider.Verify(x => x.GetContactIdByEmailAsync(email), Times.Once);
        _mockStorageProvider.Verify(x => x.GetContactAsync(contactId), Times.Once);

        // Verify email index was cached back to memory
        var cachedContactId = _memoryCache.Get<string>(emailIndexKey);
        Assert.Equal(contactId, cachedContactId);
    }

    /// <summary>
    /// Tests CacheContactAsync persists contact to SQLite Layer 2
    /// </summary>
    [Fact]
    public async Task CacheContactAsync_PersistsContactToSQLiteLayer2()
    {
        // Arrange
        var contact = CreateTestContact("contact123", "test@example.com");
        SetupSuccessfulSQLiteOperations();

        // Act
        var result = await _cacheManager.CacheContactAsync(contact);

        // Assert
        Assert.True(result.IsSuccess);

        // Verify SQLite storage was called
        _mockStorageProvider.Verify(x => x.SetContactAsync(It.Is<BasicContactInfo>(c =>
            c.Id == contact.Id &&
            c.PrimaryEmail == contact.PrimaryEmail &&
            c.DisplayName == contact.DisplayName)), Times.Once);
    }

    #endregion

    #region Layer 3 Remote API Cache Miss Tests

    /// <summary>
    /// Tests GetContactByIdAsync with both cache misses returns null (would trigger remote fetch)
    /// </summary>
    [Fact]
    public async Task GetContactByIdAsync_WithAllCacheMisses_ReturnsNull()
    {
        // Arrange
        const string contactId = "nonexistent_contact";
        var contactKey = $"contact:{contactId}";

        // Ensure memory cache miss
        _memoryCache.Remove(contactKey);

        // SQLite cache miss
        _mockStorageProvider.Setup(x => x.GetContactAsync(contactId))
            .ReturnsAsync((BasicContactInfo?)null);

        // Act
        var result = await _cacheManager.GetContactByIdAsync(contactId);

        // Assert
        Assert.True(result.IsSuccess);
        Assert.Null(result.Value);

        // Verify SQLite was checked
        _mockStorageProvider.Verify(x => x.GetContactAsync(contactId), Times.Once);
    }

    /// <summary>
    /// Tests GetContactByEmailAsync with all cache misses returns null
    /// </summary>
    [Fact]
    public async Task GetContactByEmailAsync_WithAllCacheMisses_ReturnsNull()
    {
        // Arrange
        const string email = "nonexistent@example.com";
        var emailIndexKey = $"email_idx:{email}";

        // Ensure memory cache miss
        _memoryCache.Remove(emailIndexKey);

        // SQLite cache miss
        _mockStorageProvider.Setup(x => x.GetContactIdByEmailAsync(email))
            .ReturnsAsync((string?)null);

        // Act
        var result = await _cacheManager.GetContactByEmailAsync(email);

        // Assert
        Assert.True(result.IsSuccess);
        Assert.Null(result.Value);

        // Verify SQLite was checked
        _mockStorageProvider.Verify(x => x.GetContactIdByEmailAsync(email), Times.Once);
    }

    #endregion

    #region Trust Signal Cache Tests

    /// <summary>
    /// Tests GetTrustSignalAsync with memory cache hit returns trust signal from Layer 1
    /// </summary>
    [Fact]
    public async Task GetTrustSignalAsync_WithMemoryCacheHit_ReturnsTrustSignalFromLayer1()
    {
        // Arrange
        const string contactId = "contact123";
        var trustSignal = CreateTestTrustSignal(contactId, RelationshipStrength.Strong);
        var cacheKey = $"trust:{contactId}";

        // Pre-populate memory cache
        _memoryCache.Set(cacheKey, trustSignal, _config.Cache.MemoryTtl);

        // Act
        var result = await _cacheManager.GetTrustSignalAsync(contactId);

        // Assert
        Assert.True(result.IsSuccess);
        Assert.NotNull(result.Value);
        Assert.Equal(contactId, result.Value.ContactId);
        Assert.Equal(RelationshipStrength.Strong, result.Value.Strength);

        // Verify storage provider was NOT called
        _mockStorageProvider.Verify(x => x.GetTrustSignalAsync(It.IsAny<string>()), Times.Never);
    }

    /// <summary>
    /// Tests GetTrustSignalAsync with SQLite hit returns trust signal from Layer 2
    /// </summary>
    [Fact]
    public async Task GetTrustSignalAsync_WithSQLiteHit_ReturnsTrustSignalFromLayer2()
    {
        // Arrange
        const string contactId = "contact123";
        var trustSignalInfo = CreateTestTrustSignalInfo(contactId, RelationshipStrength.Strong);
        var cacheKey = $"trust:{contactId}";

        // Ensure memory cache miss
        _memoryCache.Remove(cacheKey);

        // SQLite cache hit
        _mockStorageProvider.Setup(x => x.GetTrustSignalAsync(contactId))
            .ReturnsAsync(trustSignalInfo);

        // Act
        var result = await _cacheManager.GetTrustSignalAsync(contactId);

        // Assert
        Assert.True(result.IsSuccess);
        Assert.NotNull(result.Value);
        Assert.Equal(contactId, result.Value.ContactId);
        Assert.Equal(RelationshipStrength.Strong, result.Value.Strength);

        // Verify SQLite hit
        _mockStorageProvider.Verify(x => x.GetTrustSignalAsync(contactId), Times.Once);

        // Verify trust signal was cached back to memory
        var cachedTrustSignal = _memoryCache.Get<TrustSignal>(cacheKey);
        Assert.NotNull(cachedTrustSignal);
        Assert.Equal(contactId, cachedTrustSignal.ContactId);
    }

    /// <summary>
    /// Tests CacheTrustSignalAsync stores trust signal in both cache layers
    /// </summary>
    [Fact]
    public async Task CacheTrustSignalAsync_StoresTrustSignalInBothLayers()
    {
        // Arrange
        var trustSignal = CreateTestTrustSignal("contact123", RelationshipStrength.Strong);
        var cacheKey = $"trust:{trustSignal.ContactId}";

        SetupSuccessfulSQLiteOperations();

        // Act
        var result = await _cacheManager.CacheTrustSignalAsync(trustSignal);

        // Assert
        Assert.True(result.IsSuccess);
        Assert.True(result.Value);

        // Verify memory cache was populated
        var cachedTrustSignal = _memoryCache.Get<TrustSignal>(cacheKey);
        Assert.NotNull(cachedTrustSignal);
        Assert.Equal(trustSignal.ContactId, cachedTrustSignal.ContactId);
        Assert.Equal(trustSignal.Strength, cachedTrustSignal.Strength);

        // Verify SQLite cache was used
        _mockStorageProvider.Verify(x => x.SetTrustSignalAsync(It.IsAny<TrustSignalInfo>()), Times.Once);
    }

    #endregion

    #region Batch Operations Tests

    /// <summary>
    /// Tests CacheContactsBatchAsync successfully caches multiple contacts
    /// </summary>
    [Fact]
    public async Task CacheContactsBatchAsync_WithMultipleContacts_CachesAllSuccessfully()
    {
        // Arrange
        var contacts = new List<Contact>
        {
            CreateTestContact("contact1", "test1@example.com"),
            CreateTestContact("contact2", "test2@example.com"),
            CreateTestContact("contact3", "test3@example.com")
        };

        SetupSuccessfulSQLiteOperations();

        // Act
        var result = await _cacheManager.CacheContactsBatchAsync(contacts);

        // Assert
        Assert.True(result.IsSuccess);
        Assert.Equal(3, result.Value);

        // Verify all contacts were cached
        _mockStorageProvider.Verify(x => x.SetContactAsync(It.IsAny<BasicContactInfo>()), Times.Exactly(3));

        // Verify contacts are in memory cache
        foreach (var contact in contacts)
        {
            var cachedContact = _memoryCache.Get<Contact>($"contact:{contact.Id}");
            Assert.NotNull(cachedContact);
            Assert.Equal(contact.Id, cachedContact.Id);
        }
    }

    /// <summary>
    /// Tests CacheContactsBatchAsync with null contacts returns zero
    /// </summary>
    [Fact]
    public async Task CacheContactsBatchAsync_WithNullContacts_ReturnsZero()
    {
        // Act
        var result = await _cacheManager.CacheContactsBatchAsync(null);

        // Assert
        Assert.True(result.IsSuccess);
        Assert.Equal(0, result.Value);

        _mockStorageProvider.Verify(x => x.SetContactAsync(It.IsAny<BasicContactInfo>()), Times.Never);
    }

    /// <summary>
    /// Tests CacheContactsBatchAsync with empty contacts returns zero
    /// </summary>
    [Fact]
    public async Task CacheContactsBatchAsync_WithEmptyContacts_ReturnsZero()
    {
        // Act
        var result = await _cacheManager.CacheContactsBatchAsync(Enumerable.Empty<Contact>());

        // Assert
        Assert.True(result.IsSuccess);
        Assert.Equal(0, result.Value);

        _mockStorageProvider.Verify(x => x.SetContactAsync(It.IsAny<BasicContactInfo>()), Times.Never);
    }

    /// <summary>
    /// Tests CacheContactsBatchAsync handles partial failures gracefully
    /// </summary>
    [Fact]
    public async Task CacheContactsBatchAsync_WithPartialFailures_HandlesGracefully()
    {
        // Arrange
        var contacts = new List<Contact>
        {
            CreateTestContact("contact1", "test1@example.com"),
            CreateTestContact("contact2", "test2@example.com")
        };

        // Setup first contact to succeed, second to fail
        _mockStorageProvider.SetupSequence(x => x.SetContactAsync(It.IsAny<BasicContactInfo>()))
            .Returns(Task.CompletedTask)
            .ThrowsAsync(new InvalidOperationException("Storage failed"));

        // Act
        var result = await _cacheManager.CacheContactsBatchAsync(contacts);

        // Assert
        Assert.True(result.IsSuccess);
        Assert.Equal(1, result.Value); // Only one succeeded

        _mockStorageProvider.Verify(x => x.SetContactAsync(It.IsAny<BasicContactInfo>()), Times.Exactly(2));
    }

    #endregion

    #region Cache Invalidation Tests

    /// <summary>
    /// Tests InvalidateContactAsync removes contact from both cache layers
    /// </summary>
    [Fact]
    public async Task InvalidateContactAsync_RemovesContactFromBothLayers()
    {
        // Arrange
        const string contactId = "contact123";
        var contact = CreateTestContact(contactId, "test@example.com");
        var contactKey = $"contact:{contactId}";
        var trustKey = $"trust:{contactId}";

        // Pre-populate memory cache
        _memoryCache.Set(contactKey, contact, _config.Cache.MemoryTtl);
        _memoryCache.Set(trustKey, CreateTestTrustSignal(contactId, RelationshipStrength.Strong), _config.Cache.MemoryTtl);

        SetupSuccessfulSQLiteOperations();

        // Act
        var result = await _cacheManager.InvalidateContactAsync(contactId);

        // Assert
        Assert.True(result.IsSuccess);
        Assert.True(result.Value);

        // Verify memory cache removal
        var cachedContact = _memoryCache.Get<Contact>(contactKey);
        Assert.Null(cachedContact);
        var cachedTrustSignal = _memoryCache.Get<TrustSignal>(trustKey);
        Assert.Null(cachedTrustSignal);

        // Verify SQLite removal
        _mockStorageProvider.Verify(x => x.RemoveContactAsync(contactId), Times.Once);
    }

    /// <summary>
    /// Tests InvalidateContactAsync with empty contact ID returns success
    /// </summary>
    [Theory]
    [InlineData("")]
    [InlineData(" ")]
    [InlineData(null)]
    public async Task InvalidateContactAsync_WithEmptyContactId_ReturnsSuccess(string? contactId)
    {
        // Act
        var result = await _cacheManager.InvalidateContactAsync(contactId);

        // Assert
        Assert.True(result.IsSuccess);
        Assert.True(result.Value);

        // Verify no SQLite operations
        _mockStorageProvider.Verify(x => x.RemoveContactAsync(It.IsAny<string>()), Times.Never);
    }

    /// <summary>
    /// Tests ClearCacheAsync clears all cache layers
    /// </summary>
    [Fact]
    public async Task ClearCacheAsync_ClearsAllCacheLayers()
    {
        // Arrange
        SetupSuccessfulSQLiteOperations();

        // Pre-populate some cache data
        var contact = CreateTestContact("contact123", "test@example.com");
        _memoryCache.Set("contact:contact123", contact, _config.Cache.MemoryTtl);

        // Act
        var result = await _cacheManager.ClearCacheAsync();

        // Assert
        Assert.True(result.IsSuccess);
        Assert.True(result.Value);

        // Verify SQLite cache clearing
        _mockStorageProvider.Verify(x => x.ClearContactsCacheAsync(), Times.Once);
    }

    #endregion

    #region Performance Metrics Tests

    /// <summary>
    /// Tests GetCacheStatistics returns accurate performance metrics
    /// </summary>
    [Fact]
    public async Task GetCacheStatistics_ReturnsAccurateMetrics()
    {
        // Arrange - perform some cache operations to generate metrics
        const string contactId = "contact123";
        var contact = CreateTestContact(contactId, "test@example.com");
        var contactKey = $"contact:{contactId}";

        // First lookup - memory miss, SQLite hit
        _memoryCache.Remove(contactKey);
        var sqliteContact = CreateTestBasicContactInfo(contactId, "test@example.com");
        _mockStorageProvider.Setup(x => x.GetContactAsync(contactId)).ReturnsAsync(sqliteContact);

        await _cacheManager.GetContactByIdAsync(contactId);

        // Second lookup - memory hit (now cached)
        await _cacheManager.GetContactByIdAsync(contactId);

        // Third lookup - complete miss
        _mockStorageProvider.Setup(x => x.GetContactAsync("nonexistent")).ReturnsAsync((BasicContactInfo?)null);
        await _cacheManager.GetContactByIdAsync("nonexistent");

        // Act
        var stats = _cacheManager.GetCacheStatistics();

        // Assert
        Assert.NotNull(stats);
        Assert.Equal(3, stats.TotalLookups);
        Assert.Equal(1, stats.MemoryHits);
        Assert.Equal(1, stats.SqliteHits);
        Assert.Equal(1, stats.RemoteFetches);
        Assert.Equal(1.0 / 3.0, stats.MemoryHitRate, 2);
        Assert.Equal(1.0 / 3.0, stats.SqliteHitRate, 2);
        Assert.Equal(2.0 / 3.0, stats.CombinedHitRate, 2);
    }

    /// <summary>
    /// Tests cache statistics are reset after ClearCacheAsync
    /// </summary>
    [Fact]
    public async Task ClearCacheAsync_ResetsStatistics()
    {
        // Arrange - perform operations to generate stats
        await _cacheManager.GetContactByIdAsync("contact123");
        var statsBefore = _cacheManager.GetCacheStatistics();
        Assert.True(statsBefore.TotalLookups > 0);

        SetupSuccessfulSQLiteOperations();

        // Act
        await _cacheManager.ClearCacheAsync();

        // Assert
        var statsAfter = _cacheManager.GetCacheStatistics();
        Assert.Equal(0, statsAfter.TotalLookups);
        Assert.Equal(0, statsAfter.MemoryHits);
        Assert.Equal(0, statsAfter.SqliteHits);
        Assert.Equal(0, statsAfter.RemoteFetches);
        Assert.Equal(0, statsAfter.MemoryHitRate);
        Assert.Equal(0, statsAfter.SqliteHitRate);
        Assert.Equal(0, statsAfter.CombinedHitRate);
    }

    #endregion

    #region Error Handling Tests

    /// <summary>
    /// Tests cache operations handle null input gracefully
    /// </summary>
    [Fact]
    public async Task CacheContactAsync_WithNullContact_ReturnsFailure()
    {
        // Act
        var result = await _cacheManager.CacheContactAsync(null!);

        // Assert
        Assert.True(result.IsFailure);
        Assert.IsType<ValidationError>(result.Error);
        Assert.Contains("Contact cannot be null", result.Error.Message);
    }

    /// <summary>
    /// Tests CacheTrustSignalAsync with null trust signal returns failure
    /// </summary>
    [Fact]
    public async Task CacheTrustSignalAsync_WithNullTrustSignal_ReturnsFailure()
    {
        // Act
        var result = await _cacheManager.CacheTrustSignalAsync(null!);

        // Assert
        Assert.True(result.IsFailure);
        Assert.IsType<ValidationError>(result.Error);
        Assert.Contains("Trust signal cannot be null", result.Error.Message);
    }

    /// <summary>
    /// Tests cache operations handle SQLite failures gracefully
    /// </summary>
    [Fact]
    public async Task CacheContactAsync_WithSQLiteFailure_HandlesGracefully()
    {
        // Arrange
        var contact = CreateTestContact("contact123", "test@example.com");
        _mockStorageProvider.Setup(x => x.SetContactAsync(It.IsAny<BasicContactInfo>()))
            .ThrowsAsync(new InvalidOperationException("SQLite failed"));

        // Act
        var result = await _cacheManager.CacheContactAsync(contact);

        // Assert
        Assert.True(result.IsSuccess); // Cache manager continues even if SQLite fails
        Assert.True(result.Value);

        // Verify contact was still cached in memory
        var cachedContact = _memoryCache.Get<Contact>($"contact:{contact.Id}");
        Assert.NotNull(cachedContact);
        Assert.Equal(contact.Id, cachedContact.Id);
    }

    /// <summary>
    /// Tests GetContactByIdAsync handles SQLite exceptions gracefully
    /// </summary>
    [Fact]
    public async Task GetContactByIdAsync_WithSQLiteException_HandlesGracefully()
    {
        // Arrange
        const string contactId = "contact123";
        var contactKey = $"contact:{contactId}";

        // Ensure memory cache miss
        _memoryCache.Remove(contactKey);

        // SQLite exception
        _mockStorageProvider.Setup(x => x.GetContactAsync(contactId))
            .ThrowsAsync(new InvalidOperationException("SQLite error"));

        // Act
        var result = await _cacheManager.GetContactByIdAsync(contactId);

        // Assert
        Assert.True(result.IsFailure);
        Assert.Contains("Cache lookup failed", result.Error.Message);
    }

    #endregion

    #region Cache Concurrency Tests

    /// <summary>
    /// Tests concurrent cache operations are handled safely
    /// </summary>
    [Fact]
    public async Task ConcurrentCacheOperations_AreSafelyHandled()
    {
        // Arrange
        var contact1 = CreateTestContact("contact1", "test1@example.com");
        var contact2 = CreateTestContact("contact2", "test2@example.com");
        var contact3 = CreateTestContact("contact3", "test3@example.com");

        SetupSuccessfulSQLiteOperations();

        // Act - perform concurrent cache operations
        var tasks = new List<Task>
        {
            _cacheManager.CacheContactAsync(contact1),
            _cacheManager.CacheContactAsync(contact2),
            _cacheManager.CacheContactAsync(contact3),
            _cacheManager.GetContactByIdAsync("contact1"),
            _cacheManager.GetContactByIdAsync("contact2"),
            _cacheManager.GetContactByEmailAsync("test1@example.com")
        };

        await Task.WhenAll(tasks);

        // Assert - all operations should complete without exceptions
        // No assertions needed - just verify no exceptions were thrown
    }

    #endregion

    #region Helper Methods

    private void SetupSuccessfulSQLiteOperations()
    {
        _mockStorageProvider.Setup(x => x.SetContactAsync(It.IsAny<BasicContactInfo>()))
            .Returns(Task.CompletedTask);
        _mockStorageProvider.Setup(x => x.SetTrustSignalAsync(It.IsAny<TrustSignalInfo>()))
            .Returns(Task.CompletedTask);
        _mockStorageProvider.Setup(x => x.RemoveContactAsync(It.IsAny<string>()))
            .Returns(Task.CompletedTask);
        _mockStorageProvider.Setup(x => x.ClearContactsCacheAsync())
            .Returns(Task.CompletedTask);
    }

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

    private static BasicContactInfo CreateTestBasicContactInfo(string id, string email)
    {
        return new BasicContactInfo
        {
            Id = id,
            PrimaryEmail = email,
            AllEmails = new List<string> { email },
            DisplayName = $"Test User {id}",
            GivenName = "Test",
            FamilyName = "User",
            TrustScore = 0.7,
            Strength = RelationshipStrength.Moderate
        };
    }

    private static TrustSignal CreateTestTrustSignal(string contactId, RelationshipStrength strength)
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
            ComputedAt = DateTime.UtcNow,
            LastInteractionDate = DateTime.UtcNow.AddDays(-7),
            InteractionCount = 5,
            Justification = new List<string> { "Found in contacts", "Recent interactions" },
            RecencyScore = 0.8,
            FrequencyScore = 0.6
        };
    }

    private static TrustSignalInfo CreateTestTrustSignalInfo(string contactId, RelationshipStrength strength)
    {
        return new TrustSignalInfo
        {
            ContactId = contactId,
            EmailAddress = "test@example.com",
            Known = true,
            Strength = strength,
            Score = strength switch
            {
                RelationshipStrength.Strong => 0.8,
                RelationshipStrength.Moderate => 0.5,
                RelationshipStrength.Weak => 0.3,
                _ => 0.0
            },
            ComputedAt = DateTime.UtcNow,
            LastInteractionDate = DateTime.UtcNow.AddDays(-7),
            InteractionCount = 5,
            Justification = new List<string> { "Found in contacts", "Recent interactions" },
            SourceType = "Contacts"
        };
    }

    #endregion

    public void Dispose()
    {
        _memoryCache?.Dispose();
    }
}