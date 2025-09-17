using Xunit;
using Moq;
using Microsoft.Extensions.Logging;
using TrashMailPanda.Services;
using TrashMailPanda.Models;
using TrashMailPanda.Shared.Base;

namespace TrashMailPanda.Tests.Unit.Services;

/// <summary>
/// Unit tests for Contacts provider status updates and management
/// Tests provider status coordination and state management for Contacts provider
/// </summary>
public class ContactsProviderStatusTests
{
    private readonly Mock<ILogger<ProviderStatusService>> _mockLogger;

    public ContactsProviderStatusTests()
    {
        _mockLogger = new Mock<ILogger<ProviderStatusService>>();
    }

    [Fact]
    public void ContactsProvider_StatusUpdate_ShouldTriggerStatusChangedEvent()
    {
        // Arrange
        var providerStatusService = new ProviderStatusService(_mockLogger.Object);

        var contactsStatus = new ProviderStatus
        {
            Name = "Contacts",
            IsHealthy = true,
            IsInitialized = true,
            RequiresSetup = false,
            Status = "Ready",
            LastCheck = DateTime.UtcNow
        };

        ProviderStatusChangedEventArgs? firedEventArgs = null;
        providerStatusService.StatusChanged += (sender, args) => firedEventArgs = args;

        // Act
        providerStatusService.UpdateProviderStatus(contactsStatus);

        // Assert
        Assert.NotNull(firedEventArgs);
        Assert.Equal("Contacts", firedEventArgs.ProviderName);
        Assert.Equal(contactsStatus, firedEventArgs.NewStatus);
    }

    [Theory]
    [InlineData(true, true, false, "Ready")]
    [InlineData(false, false, true, "Authentication Required")]
    [InlineData(false, true, true, "Connection Failed")]
    [InlineData(true, false, false, "Initializing")]
    public void ContactsProvider_StatusUpdate_ShouldMaintainCorrectState(
        bool isHealthy, bool isInitialized, bool requiresSetup, string expectedStatus)
    {
        // Arrange
        var providerStatusService = new ProviderStatusService(_mockLogger.Object);

        var contactsStatus = new ProviderStatus
        {
            Name = "Contacts",
            IsHealthy = isHealthy,
            IsInitialized = isInitialized,
            RequiresSetup = requiresSetup,
            Status = expectedStatus,
            LastCheck = DateTime.UtcNow
        };

        // Act
        providerStatusService.UpdateProviderStatus(contactsStatus);
        var retrievedStatus = providerStatusService.GetProviderStatus("Contacts");

        // Assert
        Assert.NotNull(retrievedStatus);
        Assert.Equal("Contacts", retrievedStatus.Name);
        Assert.Equal(isHealthy, retrievedStatus.IsHealthy);
        Assert.Equal(isInitialized, retrievedStatus.IsInitialized);
        Assert.Equal(requiresSetup, retrievedStatus.RequiresSetup);
        Assert.Equal(expectedStatus, retrievedStatus.Status);
    }

    [Fact]
    public void ContactsProvider_MultipleStatusUpdates_ShouldMaintainLatestState()
    {
        // Arrange
        var providerStatusService = new ProviderStatusService(_mockLogger.Object);

        var initialStatus = new ProviderStatus
        {
            Name = "Contacts",
            IsHealthy = false,
            RequiresSetup = true,
            Status = "Authentication Required",
            LastCheck = DateTime.UtcNow.AddMinutes(-5)
        };

        var updatedStatus = new ProviderStatus
        {
            Name = "Contacts",
            IsHealthy = true,
            RequiresSetup = false,
            Status = "Ready",
            LastCheck = DateTime.UtcNow
        };

        int eventCount = 0;
        providerStatusService.StatusChanged += (sender, args) => eventCount++;

        // Act
        providerStatusService.UpdateProviderStatus(initialStatus);
        providerStatusService.UpdateProviderStatus(updatedStatus);

        // Assert
        Assert.Equal(2, eventCount);

        var finalStatus = providerStatusService.GetProviderStatus("Contacts");
        Assert.NotNull(finalStatus);
        Assert.True(finalStatus.IsHealthy);
        Assert.False(finalStatus.RequiresSetup);
        Assert.Equal("Ready", finalStatus.Status);
    }

    [Fact]
    public void ContactsProvider_GetStatus_WhenNotSet_ShouldReturnNull()
    {
        // Arrange
        var providerStatusService = new ProviderStatusService(_mockLogger.Object);

        // Act
        var status = providerStatusService.GetProviderStatus("Contacts");

        // Assert
        Assert.Null(status);
    }

    [Fact]
    public void ContactsProvider_GetAllStatuses_ShouldIncludeContactsWhenPresent()
    {
        // Arrange
        var providerStatusService = new ProviderStatusService(_mockLogger.Object);

        var contactsStatus = new ProviderStatus
        {
            Name = "Contacts",
            IsHealthy = true,
            Status = "Ready",
            LastCheck = DateTime.UtcNow
        };

        var gmailStatus = new ProviderStatus
        {
            Name = "Gmail",
            IsHealthy = true,
            Status = "Ready",
            LastCheck = DateTime.UtcNow
        };

        // Act
        providerStatusService.UpdateProviderStatus(contactsStatus);
        providerStatusService.UpdateProviderStatus(gmailStatus);
        var allStatuses = providerStatusService.GetAllProviderStatuses();

        // Assert
        Assert.Contains(allStatuses, s => s.Name == "Contacts");
        Assert.Contains(allStatuses, s => s.Name == "Gmail");
        Assert.Equal(2, allStatuses.Count);
    }

    [Fact]
    public void ContactsProvider_StatusUpdate_WithErrorMessage_ShouldPreserveError()
    {
        // Arrange
        var providerStatusService = new ProviderStatusService(_mockLogger.Object);

        var errorStatus = new ProviderStatus
        {
            Name = "Contacts",
            IsHealthy = false,
            Status = "Error",
            ErrorMessage = "Failed to connect to Google Contacts API",
            LastCheck = DateTime.UtcNow
        };

        // Act
        providerStatusService.UpdateProviderStatus(errorStatus);
        var retrievedStatus = providerStatusService.GetProviderStatus("Contacts");

        // Assert
        Assert.NotNull(retrievedStatus);
        Assert.False(retrievedStatus.IsHealthy);
        Assert.Equal("Error", retrievedStatus.Status);
        Assert.Equal("Failed to connect to Google Contacts API", retrievedStatus.ErrorMessage);
    }

    [Fact]
    public void ContactsProvider_StatusUpdate_ShouldUpdateTimestamp()
    {
        // Arrange
        var providerStatusService = new ProviderStatusService(_mockLogger.Object);
        var beforeUpdate = DateTime.UtcNow;

        var contactsStatus = new ProviderStatus
        {
            Name = "Contacts",
            IsHealthy = true,
            Status = "Ready",
            LastCheck = DateTime.UtcNow
        };

        // Act
        providerStatusService.UpdateProviderStatus(contactsStatus);
        var retrievedStatus = providerStatusService.GetProviderStatus("Contacts");

        // Assert
        Assert.NotNull(retrievedStatus);
        Assert.True(retrievedStatus.LastCheck >= beforeUpdate);
        Assert.True(retrievedStatus.LastCheck <= DateTime.UtcNow);
    }

    [Fact]
    public void ContactsProvider_StatusUpdate_WithDetails_ShouldPreserveDetails()
    {
        // Arrange
        var providerStatusService = new ProviderStatusService(_mockLogger.Object);

        var detailsDict = new Dictionary<string, object>
        {
            { "ContactsCount", 150 },
            { "LastSyncTime", DateTime.UtcNow.AddHours(-2) },
            { "SyncStatus", "Completed" }
        };

        var contactsStatus = new ProviderStatus
        {
            Name = "Contacts",
            IsHealthy = true,
            Status = "Ready",
            Details = detailsDict,
            LastCheck = DateTime.UtcNow
        };

        // Act
        providerStatusService.UpdateProviderStatus(contactsStatus);
        var retrievedStatus = providerStatusService.GetProviderStatus("Contacts");

        // Assert
        Assert.NotNull(retrievedStatus);
        Assert.Equal(150, retrievedStatus.Details["ContactsCount"]);
        Assert.Equal("Completed", retrievedStatus.Details["SyncStatus"]);
        Assert.True(retrievedStatus.Details.ContainsKey("LastSyncTime"));
    }
}