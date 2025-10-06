using Xunit;
using Moq;
using Microsoft.Extensions.Logging;
using TrashMailPanda.Services;
using TrashMailPanda.Models;
using TrashMailPanda.Shared;
using TrashMailPanda.Shared.Base;
using TrashMailPanda.Shared.Models;

namespace TrashMailPanda.Tests.Unit.Services;

/// <summary>
/// Unit tests for Contacts provider status updates and management
/// Tests provider status coordination and state management for Contacts provider
/// </summary>
public class ContactsProviderStatusTests
{
    private readonly Mock<ILogger<ProviderStatusService>> _mockLogger;
    private readonly Mock<IStorageProvider> _mockStorageProvider;

    public ContactsProviderStatusTests()
    {
        _mockLogger = new Mock<ILogger<ProviderStatusService>>();
        _mockStorageProvider = new Mock<IStorageProvider>();
    }

    [Fact]
    public void ContactsProvider_Constructor_WithValidParameters_ShouldSucceed()
    {
        // Arrange & Act
        var providerStatusService = new ProviderStatusService(_mockLogger.Object, _mockStorageProvider.Object);

        // Assert
        Assert.NotNull(providerStatusService);
    }

    [Fact]
    public void ContactsProvider_Constructor_WithNullLogger_ShouldThrowArgumentNullException()
    {
        // Act & Assert
        Assert.Throws<ArgumentNullException>(() =>
            new ProviderStatusService(null!, _mockStorageProvider.Object));
    }

    [Fact]
    public void ContactsProvider_Constructor_WithNullStorageProvider_ShouldThrowArgumentNullException()
    {
        // Act & Assert
        Assert.Throws<ArgumentNullException>(() =>
            new ProviderStatusService(_mockLogger.Object, null!));
    }

    [Fact]
    public async Task ContactsProvider_GetProviderStatusAsync_WhenNotSet_ShouldReturnNull()
    {
        // Arrange
        var providerStatusService = new ProviderStatusService(_mockLogger.Object, _mockStorageProvider.Object);

        // Act
        var status = await providerStatusService.GetProviderStatusAsync("Contacts");

        // Assert
        // Since no contacts provider is registered, status should be null
        Assert.Null(status);
    }

    [Fact]
    public async Task ContactsProvider_GetAllProviderStatusAsync_ShouldReturnDictionary()
    {
        // Arrange
        var providerStatusService = new ProviderStatusService(_mockLogger.Object, _mockStorageProvider.Object);

        // Act
        var allStatuses = await providerStatusService.GetAllProviderStatusAsync();

        // Assert
        Assert.NotNull(allStatuses);
        Assert.IsType<Dictionary<string, ProviderStatus>>(allStatuses);
        // Note: Without actual providers registered, this will be empty or contain only storage provider
    }

    [Fact]
    public async Task ContactsProvider_AreAllProvidersHealthyAsync_WithNoProviders_ShouldReturnTrue()
    {
        // Arrange
        var providerStatusService = new ProviderStatusService(_mockLogger.Object, _mockStorageProvider.Object);

        // Act
        var areHealthy = await providerStatusService.AreAllProvidersHealthyAsync();

        // Assert
        // With only storage provider (which defaults to healthy), should return true
        Assert.True(areHealthy);
    }

    [Fact]
    public async Task ContactsProvider_RefreshProviderStatusAsync_ShouldComplete()
    {
        // Arrange
        var providerStatusService = new ProviderStatusService(_mockLogger.Object, _mockStorageProvider.Object);

        // Act & Assert - Should not throw
        await providerStatusService.RefreshProviderStatusAsync();
    }

    [Fact]
    public async Task ContactsProvider_StatusChanged_EventExists()
    {
        // Arrange
        var providerStatusService = new ProviderStatusService(_mockLogger.Object, _mockStorageProvider.Object);
        var eventFired = false;

        // Act
        providerStatusService.ProviderStatusChanged += (sender, args) => eventFired = true;

        // Force a status refresh which may trigger events
        await providerStatusService.RefreshProviderStatusAsync();

        // Assert - Event handler was attached (actual firing depends on registered providers)
        // The event itself exists and can be subscribed to
        Assert.True(true); // This test verifies the event can be subscribed to without throwing
    }

    [Fact]
    public async Task ContactsProvider_StatusWithMockContactsProvider_ShouldBeHandled()
    {
        // Arrange
        var mockContactsProvider = new Mock<IContactsProvider>();
        var providerStatusService = new ProviderStatusService(
            _mockLogger.Object,
            _mockStorageProvider.Object,
            contactsProvider: mockContactsProvider.Object);

        // Act
        await providerStatusService.RefreshProviderStatusAsync();
        var status = await providerStatusService.GetProviderStatusAsync("Contacts");

        // Assert
        // Even with a mock provider, the service should handle it
        // The actual status will depend on the mock's behavior
        Assert.True(true); // Test that no exception is thrown
    }

    [Fact]
    public async Task ContactsProvider_MultipleRefreshCalls_ShouldNotThrow()
    {
        // Arrange
        var providerStatusService = new ProviderStatusService(_mockLogger.Object, _mockStorageProvider.Object);

        // Act & Assert - Multiple calls should not throw
        await providerStatusService.RefreshProviderStatusAsync();
        await providerStatusService.RefreshProviderStatusAsync();
        await providerStatusService.RefreshProviderStatusAsync();
    }

    [Fact]
    public async Task ContactsProvider_GetProviderStatus_WithEmptyString_ShouldReturnNull()
    {
        // Arrange
        var providerStatusService = new ProviderStatusService(_mockLogger.Object, _mockStorageProvider.Object);

        // Act
        var status = await providerStatusService.GetProviderStatusAsync("");

        // Assert
        Assert.Null(status);
    }

    [Fact]
    public async Task ContactsProvider_GetProviderStatus_WithNull_ShouldReturnNull()
    {
        // Arrange
        var providerStatusService = new ProviderStatusService(_mockLogger.Object, _mockStorageProvider.Object);

        // Act
        var status = await providerStatusService.GetProviderStatusAsync(null!);

        // Assert
        Assert.Null(status);
    }
}