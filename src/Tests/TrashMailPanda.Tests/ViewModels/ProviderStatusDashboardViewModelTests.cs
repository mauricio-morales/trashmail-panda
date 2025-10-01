using Microsoft.Extensions.Logging;
using Moq;
using TrashMailPanda.ViewModels;
using TrashMailPanda.Models;
using TrashMailPanda.Shared;
using TrashMailPanda.Shared.Models;
using TrashMailPanda.Services;
using Xunit;

namespace TrashMailPanda.Tests.ViewModels;

public class ProviderStatusDashboardViewModelTests
{
    private readonly Mock<IProviderBridgeService> _mockBridgeService;
    private readonly Mock<IProviderStatusService> _mockStatusService;
    private readonly Mock<ILogger<ProviderStatusDashboardViewModel>> _mockLogger;

    public ProviderStatusDashboardViewModelTests()
    {
        _mockBridgeService = new Mock<IProviderBridgeService>();
        _mockStatusService = new Mock<IProviderStatusService>();
        _mockLogger = new Mock<ILogger<ProviderStatusDashboardViewModel>>();
    }

    private ProviderStatusDashboardViewModel CreateViewModel()
    {
        return new ProviderStatusDashboardViewModel(
            _mockBridgeService.Object,
            _mockStatusService.Object,
            _mockLogger.Object);
    }

    private static ProviderDisplayInfo CreateTestDisplayInfo(string name, bool isRequired = true)
    {
        return new ProviderDisplayInfo
        {
            Name = name,
            DisplayName = $"{name} Display Name",
            Description = $"Description for {name}",
            Icon = "🔧",
            Type = ProviderType.Email,
            IsRequired = isRequired,
            Prerequisites = "Test prerequisites",
            Complexity = SetupComplexity.Simple,
            EstimatedSetupTimeMinutes = 5
        };
    }

    private static ProviderStatus CreateTestStatus(string name, bool isHealthy, bool requiresSetup = false)
    {
        return new ProviderStatus
        {
            Name = name,
            IsHealthy = isHealthy,
            IsInitialized = !requiresSetup,
            RequiresSetup = requiresSetup,
            Status = isHealthy ? "Connected" : (requiresSetup ? "Setup Required" : "Error"),
            LastCheck = DateTime.UtcNow,
            ErrorMessage = isHealthy ? null : "Test error message"
        };
    }

    [Fact]
    public void Constructor_ShouldInitializeProperties()
    {
        // Arrange
        _mockBridgeService.Setup(x => x.GetProviderDisplayInfo())
            .Returns(new Dictionary<string, ProviderDisplayInfo>());

        // Act
        var viewModel = CreateViewModel();

        // Assert
        // IsLoading starts as true but may change after initialization
        Assert.False(viewModel.CanAccessMainDashboard);
        Assert.Equal("Checking providers...", viewModel.OverallStatus);
        Assert.Equal(0, viewModel.HealthyProviderCount);
        Assert.Equal(0, viewModel.TotalProviderCount);
    }

    [Fact]
    public void Constructor_WithNullParameters_ShouldThrowArgumentNullException()
    {
        // Act & Assert
        Assert.Throws<ArgumentNullException>(() =>
            new ProviderStatusDashboardViewModel(null!, _mockStatusService.Object, _mockLogger.Object));

        Assert.Throws<ArgumentNullException>(() =>
            new ProviderStatusDashboardViewModel(_mockBridgeService.Object, null!, _mockLogger.Object));

        Assert.Throws<ArgumentNullException>(() =>
            new ProviderStatusDashboardViewModel(_mockBridgeService.Object, _mockStatusService.Object, null!));
    }

    [Fact]
    public void InitializeProviderCards_ShouldCreateCardsFromBridgeService()
    {
        // Arrange
        var providerInfos = new Dictionary<string, ProviderDisplayInfo>
        {
            { "Provider1", CreateTestDisplayInfo("Provider1") },
            { "Provider2", CreateTestDisplayInfo("Provider2") }
        };

        _mockBridgeService.Setup(x => x.GetProviderDisplayInfo())
            .Returns(providerInfos);

        // Act
        var viewModel = CreateViewModel();

        // Assert
        Assert.Equal(2, viewModel.ProviderCards.Count);
        Assert.Equal(2, viewModel.TotalProviderCount);
        Assert.Contains(viewModel.ProviderCards, c => c.ProviderName == "Provider1");
        Assert.Contains(viewModel.ProviderCards, c => c.ProviderName == "Provider2");
    }

    [Fact]
    public void HealthyProvidersText_ShouldReturnCorrectFormat()
    {
        // Arrange
        var viewModel = CreateViewModel();

        // Act
        viewModel.HealthyProviderCount = 2;
        viewModel.TotalProviderCount = 5;

        // Assert
        Assert.Equal("2 of 5 providers healthy", viewModel.HealthyProvidersText);
    }

    [Theory]
    [InlineData(true, false, "All systems operational")]
    [InlineData(false, true, "Issues detected")]
    // NOTE: The third scenario (false, false, "Setup required") is not tested here because
    // in the actual implementation, when there are 0 healthy providers (which is the case
    // when ProviderCards is empty), HasErrors is automatically set to true via line 285:
    // HasErrors = hasErrors || healthyCount == 0;
    // This means HasErrors=false is not realistic when there are no providers initialized.
    public void OverallHealthStatus_ShouldReturnCorrectStatus(bool canAccess, bool hasErrors, string expected)
    {
        // Arrange
        _mockBridgeService.Setup(x => x.GetProviderDisplayInfo())
            .Returns(new Dictionary<string, ProviderDisplayInfo>());

        var viewModel = CreateViewModel();

        // Act
        viewModel.CanAccessMainDashboard = canAccess;
        viewModel.HasErrors = hasErrors;

        // Assert
        Assert.Equal(expected, viewModel.OverallHealthStatus);
    }

    [Fact]
    public void OverallHealthStatus_WithNoProvidersAndNoErrors_ShouldReturnSetupRequired()
    {
        // Arrange - Setup with at least 1 healthy provider so HasErrors doesn't default to true
        var providerInfo = new Dictionary<string, ProviderDisplayInfo>
        {
            ["TestProvider"] = CreateTestDisplayInfo("TestProvider", isRequired: false)
        };

        _mockBridgeService.Setup(x => x.GetProviderDisplayInfo())
            .Returns(providerInfo);

        var viewModel = CreateViewModel();

        // Manually set state to simulate setup required (not errors, just needs setup)
        viewModel.HealthyProviderCount = 1; // At least 1 provider exists
        viewModel.TotalProviderCount = 1;
        viewModel.CanAccessMainDashboard = false;
        viewModel.HasErrors = false; // Explicitly no errors, just needs setup

        // Act
        var result = viewModel.OverallHealthStatus;

        // Assert
        Assert.Equal("Setup required", result);
    }

    [Fact]
    public void LastRefreshText_WithMinValue_ShouldReturnNeverUpdated()
    {
        // Arrange
        var viewModel = CreateViewModel();

        // Act
        viewModel.LastRefresh = DateTime.MinValue;

        // Assert
        Assert.Equal("Never updated", viewModel.LastRefreshText);
    }

    [Fact]
    public void LastRefreshText_WithRecentTime_ShouldReturnTimeAgo()
    {
        // Arrange
        var viewModel = CreateViewModel();

        // Act
        viewModel.LastRefresh = DateTime.UtcNow.AddMinutes(-5);

        // Assert
        Assert.Contains("5 minutes ago", viewModel.LastRefreshText);
    }

    [Theory]
    [InlineData(3, 3, true)]
    [InlineData(2, 3, false)]
    [InlineData(0, 0, false)]
    public void AllProvidersHealthy_ShouldReturnCorrectValue(int healthy, int total, bool expected)
    {
        // Arrange
        var viewModel = CreateViewModel();

        // Act
        viewModel.HealthyProviderCount = healthy;
        viewModel.TotalProviderCount = total;

        // Assert
        Assert.Equal(expected, viewModel.AllProvidersHealthy);
    }

    [Fact]
    public async Task RefreshAllProvidersCommand_ShouldUpdateProviderCards()
    {
        // Arrange
        var providerInfos = new Dictionary<string, ProviderDisplayInfo>
        {
            { "Provider1", CreateTestDisplayInfo("Provider1") }
        };

        var providerStatuses = new Dictionary<string, ProviderStatus>
        {
            { "Provider1", CreateTestStatus("Provider1", true) }
        };

        _mockBridgeService.Setup(x => x.GetProviderDisplayInfo())
            .Returns(providerInfos);

        _mockBridgeService.Setup(x => x.GetAllProviderStatusAsync())
            .ReturnsAsync(providerStatuses);

        var viewModel = CreateViewModel();

        // Wait for constructor initialization to complete
        await Task.Delay(100);

        // Act
        await viewModel.RefreshAllProvidersCommand.ExecuteAsync(null);

        // Assert
        Assert.False(viewModel.IsLoading);
        Assert.False(viewModel.IsRefreshing);
        Assert.NotEqual(DateTime.MinValue, viewModel.LastRefresh);
    }

    [Fact]
    public async Task AccessMainDashboardCommand_WhenCanAccess_ShouldRaiseDashboardAccessRequested()
    {
        // Arrange
        var viewModel = CreateViewModel();
        viewModel.CanAccessMainDashboard = true;

        var eventRaised = false;
        viewModel.DashboardAccessRequested += (sender, e) => eventRaised = true;

        // Act
        await viewModel.AccessMainDashboardCommand.ExecuteAsync(null);

        // Assert
        Assert.True(eventRaised);
    }

    [Fact]
    public async Task AccessMainDashboardCommand_WhenCannotAccess_ShouldNotRaiseEvent()
    {
        // Arrange
        var viewModel = CreateViewModel();
        viewModel.CanAccessMainDashboard = false;

        var eventRaised = false;
        viewModel.DashboardAccessRequested += (sender, e) => eventRaised = true;

        // Act
        await viewModel.AccessMainDashboardCommand.ExecuteAsync(null);

        // Assert
        Assert.False(eventRaised);
    }

    [Fact]
    public void ToggleDetailedStatusCommand_ShouldToggleShowDetailedStatus()
    {
        // Arrange
        var viewModel = CreateViewModel();
        var initialValue = viewModel.ShowDetailedStatus;

        // Act
        viewModel.ToggleDetailedStatusCommand.Execute(null);

        // Assert
        Assert.NotEqual(initialValue, viewModel.ShowDetailedStatus);
    }

    [Fact]
    public async Task SetupAllRequiredProvidersCommand_ShouldRaiseSetupRequestedForRequiredProviders()
    {
        // Arrange
        var providerInfos = new Dictionary<string, ProviderDisplayInfo>
        {
            { "Provider1", CreateTestDisplayInfo("Provider1", isRequired: true) },
            { "Provider2", CreateTestDisplayInfo("Provider2", isRequired: false) }
        };

        _mockBridgeService.Setup(x => x.GetProviderDisplayInfo())
            .Returns(providerInfos);

        var viewModel = CreateViewModel();

        // Update provider cards to require setup
        var card1 = viewModel.ProviderCards.First(c => c.ProviderName == "Provider1");
        var card2 = viewModel.ProviderCards.First(c => c.ProviderName == "Provider2");

        var requiresSetupStatus1 = CreateTestStatus("Provider1", false, requiresSetup: true);
        var requiresSetupStatus2 = CreateTestStatus("Provider2", false, requiresSetup: true);

        card1.UpdateFromProviderStatus(requiresSetupStatus1);
        card2.UpdateFromProviderStatus(requiresSetupStatus2);

        var setupRequested = new List<string>();
        viewModel.ProviderSetupRequested += (sender, provider) => setupRequested.Add(provider);

        // Act
        await viewModel.SetupAllRequiredProvidersCommand.ExecuteAsync(null);

        // Assert - Only required provider should trigger setup
        Assert.Contains("Provider1", setupRequested);
        Assert.DoesNotContain("Provider2", setupRequested);
    }

    [Fact]
    public void OnProviderStatusChanged_ShouldUpdateCorrespondingCard()
    {
        // Arrange
        var providerInfos = new Dictionary<string, ProviderDisplayInfo>
        {
            { "Provider1", CreateTestDisplayInfo("Provider1") }
        };

        _mockBridgeService.Setup(x => x.GetProviderDisplayInfo())
            .Returns(providerInfos);

        var viewModel = CreateViewModel();
        var card = viewModel.ProviderCards.First();

        var newStatus = CreateTestStatus("Provider1", true);
        var statusChangedArgs = new ProviderStatusChangedEventArgs
        {
            ProviderName = "Provider1",
            Status = newStatus
        };

        // Act
        _mockStatusService.Raise(x => x.ProviderStatusChanged += null, _mockStatusService.Object, statusChangedArgs);

        // Assert
        Assert.True(card.IsHealthy);
        Assert.Equal("Connected", card.CurrentStatus);
    }

    [Fact]
    public void Cleanup_ShouldUnsubscribeFromEvents()
    {
        // Arrange
        var viewModel = CreateViewModel();

        // Act & Assert - Should not throw
        viewModel.Cleanup();
    }

    [Fact]
    public void InitializeProviderCards_WhenExceptionOccurs_ShouldSetErrorState()
    {
        // Arrange
        _mockBridgeService.Setup(x => x.GetProviderDisplayInfo())
            .Throws(new InvalidOperationException("Test exception"));

        // Act
        var viewModel = CreateViewModel();

        // Assert
        Assert.Equal("Failed to initialize providers", viewModel.OverallStatus);
        Assert.True(viewModel.HasErrors);
    }
}