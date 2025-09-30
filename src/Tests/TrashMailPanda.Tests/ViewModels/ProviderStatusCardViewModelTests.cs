using TrashMailPanda.ViewModels;
using TrashMailPanda.Models;
using TrashMailPanda.Shared;
using TrashMailPanda.Shared.Models;
using TrashMailPanda.Services;
using Xunit;

namespace TrashMailPanda.Tests.ViewModels;

public class ProviderStatusCardViewModelTests
{
    private static ProviderDisplayInfo CreateTestDisplayInfo(string name = "TestProvider")
    {
        return new ProviderDisplayInfo
        {
            Name = name,
            DisplayName = $"{name} Display Name",
            Description = $"Description for {name}",
            Icon = "🔧",
            Type = ProviderType.Email,
            IsRequired = true,
            Prerequisites = "Test prerequisites",
            Complexity = SetupComplexity.Simple,
            EstimatedSetupTimeMinutes = 5
        };
    }

    private static ProviderStatus CreateTestStatus(bool isHealthy = false, bool requiresSetup = true)
    {
        return new ProviderStatus
        {
            Name = "TestProvider",
            IsHealthy = isHealthy,
            IsInitialized = !requiresSetup,
            RequiresSetup = requiresSetup,
            Status = isHealthy ? "Connected" : "Setup Required",
            LastCheck = DateTime.UtcNow
        };
    }

    [Fact]
    public void Constructor_ShouldInitializeWithDisplayInfo()
    {
        // Arrange
        var displayInfo = CreateTestDisplayInfo();

        // Act
        var viewModel = new ProviderStatusCardViewModel(displayInfo);

        // Assert
        Assert.Equal(displayInfo.Name, viewModel.ProviderName);
        Assert.Equal(displayInfo.DisplayName, viewModel.ProviderDisplayName);
        Assert.Equal(displayInfo.Description, viewModel.ProviderDescription);
        Assert.Equal(displayInfo.Icon, viewModel.ProviderIcon);
        Assert.False(viewModel.IsHealthy);
        Assert.False(viewModel.RequiresSetup); // Initially false until status is updated
    }

    [Fact]
    public void Constructor_WithNullDisplayInfo_ShouldThrowArgumentNullException()
    {
        // Act & Assert
        Assert.Throws<ArgumentNullException>(() => new ProviderStatusCardViewModel(null!));
    }

    [Fact]
    public void UpdateFromProviderStatus_ShouldUpdateAllProperties()
    {
        // Arrange
        var displayInfo = CreateTestDisplayInfo();
        var viewModel = new ProviderStatusCardViewModel(displayInfo);
        var newStatus = CreateTestStatus(isHealthy: true, requiresSetup: false);

        // Act
        viewModel.UpdateFromProviderStatus(newStatus);

        // Assert
        Assert.True(viewModel.IsHealthy);
        Assert.False(viewModel.RequiresSetup);
        Assert.True(viewModel.IsInitialized);
        Assert.Equal("Connected", viewModel.CurrentStatus);
    }

    [Fact]
    public void UpdateFromProviderStatus_WithNullStatus_ShouldNotUpdate()
    {
        // Arrange
        var displayInfo = CreateTestDisplayInfo();
        var viewModel = new ProviderStatusCardViewModel(displayInfo);
        var originalHealthy = viewModel.IsHealthy;

        // Act
        viewModel.UpdateFromProviderStatus(null!);

        // Assert
        Assert.Equal(originalHealthy, viewModel.IsHealthy);
    }

    [Theory]
    [InlineData(SetupComplexity.Simple, "Quick setup")]
    [InlineData(SetupComplexity.Moderate, "Moderate setup")]
    [InlineData(SetupComplexity.Complex, "Advanced setup")]
    public void SetupComplexity_ShouldReturnCorrectText(SetupComplexity complexity, string expected)
    {
        // Arrange
        var displayInfo = new ProviderDisplayInfo
        {
            Name = "TestProvider",
            DisplayName = "TestProvider Display Name",
            Description = "Description for TestProvider",
            Icon = "🔧",
            Type = ProviderType.Email,
            IsRequired = true,
            Prerequisites = "Test prerequisites",
            Complexity = complexity,
            EstimatedSetupTimeMinutes = 5
        };
        var viewModel = new ProviderStatusCardViewModel(displayInfo);

        // Act
        var result = viewModel.SetupComplexity;

        // Assert
        Assert.Equal(expected, result);
    }

    [Theory]
    [InlineData(0, "Automatic")]
    [InlineData(1, "1 minute")]
    [InlineData(3, "3 minutes")]
    [InlineData(8, "5-10 minutes")]
    [InlineData(15, "10+ minutes")]
    public void EstimatedTime_ShouldReturnCorrectText(int minutes, string expected)
    {
        // Arrange
        var displayInfo = new ProviderDisplayInfo
        {
            Name = "TestProvider",
            DisplayName = "TestProvider Display Name",
            Description = "Description for TestProvider",
            Icon = "🔧",
            Type = ProviderType.Email,
            IsRequired = true,
            Prerequisites = "Test prerequisites",
            Complexity = SetupComplexity.Simple,
            EstimatedSetupTimeMinutes = minutes
        };
        var viewModel = new ProviderStatusCardViewModel(displayInfo);

        // Act
        var result = viewModel.EstimatedTime;

        // Assert
        Assert.Equal(expected, result);
    }

    [Theory]
    [InlineData(true, true, true)]   // Always shows button - text changes based on state
    [InlineData(false, false, true)] // Always shows button - text changes based on state
    [InlineData(false, true, true)]  // Always shows button - text changes based on state
    public void ShowSetupButton_ShouldReturnCorrectValue(bool isHealthy, bool requiresSetup, bool expected)
    {
        // Arrange
        var displayInfo = CreateTestDisplayInfo();
        var viewModel = new ProviderStatusCardViewModel(displayInfo);
        var status = CreateTestStatus(isHealthy: isHealthy, requiresSetup: requiresSetup);

        // Act
        viewModel.UpdateFromProviderStatus(status);

        // Assert
        Assert.Equal(expected, viewModel.ShowSetupButton);
    }

    [Theory]
    [InlineData("OAuth Setup Required", "Setup OAuth")]
    [InlineData("API Key Required", "Enter API Key")]
    [InlineData("Setup Required", "Setup")]
    [InlineData("Authentication Required", "Sign In")]
    [InlineData("API Key Invalid", "Fix API Key")]
    [InlineData("Connection Failed", "Reconnect")]
    public void ActionButtonText_ShouldReturnCorrectText(string statusText, string expected)
    {
        // Arrange
        var displayInfo = CreateTestDisplayInfo();
        var viewModel = new ProviderStatusCardViewModel(displayInfo);
        var requiresSetup = statusText.Contains("Setup") || statusText.Contains("Required");
        var status = new ProviderStatus
        {
            Name = "TestProvider",
            IsHealthy = !requiresSetup,
            IsInitialized = !requiresSetup,
            RequiresSetup = requiresSetup,
            Status = statusText,
            LastCheck = DateTime.UtcNow
        };

        // Act
        viewModel.UpdateFromProviderStatus(status);

        // Assert
        Assert.Equal(expected, viewModel.ActionButtonText);
    }

    [Theory]
    [InlineData("Connected", "✅ Connected and working")]
    [InlineData("Ready", "✅ Ready to use")]
    [InlineData("Healthy", "✅ Operating normally")]
    [InlineData("OAuth Setup Required", "⚙️ OAuth setup needed")]
    [InlineData("API Key Required", "🔑 API key needed")]
    [InlineData("Authentication Required", "🔐 Please sign in")]
    [InlineData("API Key Invalid", "❌ Invalid API key")]
    [InlineData("Connection Failed", "❌ Connection problem")]
    [InlineData("Database Error", "❌ Database issue")]
    [InlineData("Unknown Status", "Unknown Status")]
    public void StatusDisplayText_ShouldReturnCorrectText(string statusText, string expected)
    {
        // Arrange
        var displayInfo = CreateTestDisplayInfo();
        var viewModel = new ProviderStatusCardViewModel(displayInfo);
        var status = new ProviderStatus
        {
            Name = "TestProvider",
            IsHealthy = false,
            IsInitialized = false,
            RequiresSetup = true,
            Status = statusText,
            LastCheck = DateTime.UtcNow
        };

        // Act
        viewModel.UpdateFromProviderStatus(status);
        viewModel.IsLoading = false; // Ensure not loading

        // Assert
        Assert.Equal(expected, viewModel.StatusDisplayText);
    }

    [Fact]
    public void StatusDisplayText_WhenLoading_ShouldReturnLoadingText()
    {
        // Arrange
        var displayInfo = CreateTestDisplayInfo();
        var viewModel = new ProviderStatusCardViewModel(displayInfo);

        // Act
        viewModel.IsLoading = true;

        // Assert
        Assert.Equal("Checking status...", viewModel.StatusDisplayText);
    }

    [Fact]
    public void UpdateSetupState_ShouldUpdateLoadingAndCanConfigure()
    {
        // Arrange
        var displayInfo = CreateTestDisplayInfo();
        var viewModel = new ProviderStatusCardViewModel(displayInfo);
        var setupState = new ProviderSetupState
        {
            ProviderName = "TestProvider",
            CurrentStep = SetupStep.Configuring,
            IsInProgress = true
        };

        // Act
        viewModel.UpdateSetupState(setupState);

        // Assert
        Assert.True(viewModel.IsLoading);
        Assert.False(viewModel.CanConfigureProvider);
        Assert.Contains("Configuring", viewModel.StatusDisplayText);
    }

    [Fact]
    public void SetTemporaryStatusMessage_ShouldUpdateStatusDisplayText()
    {
        // Arrange
        var displayInfo = CreateTestDisplayInfo();
        var viewModel = new ProviderStatusCardViewModel(displayInfo);
        var message = "Test temporary message";

        // Act
        viewModel.SetTemporaryStatusMessage(message);

        // Assert
        Assert.Equal(message, viewModel.StatusDisplayText);
    }

    [Fact]
    public async Task HandleActionCommand_WhenRequiresSetup_ShouldFireSetupRequested()
    {
        // Arrange
        var displayInfo = CreateTestDisplayInfo();
        var viewModel = new ProviderStatusCardViewModel(displayInfo);
        var status = CreateTestStatus(requiresSetup: true);
        viewModel.UpdateFromProviderStatus(status);

        string? firedProvider = null;
        viewModel.SetupRequested += (sender, provider) => firedProvider = provider;

        // Act
        await viewModel.HandleActionCommand.ExecuteAsync(null);

        // Assert
        Assert.Equal("TestProvider", firedProvider);
    }

    [Fact]
    public async Task HandleActionCommand_WhenNotRequiresSetup_ShouldFireConfigurationRequested()
    {
        // Arrange
        var displayInfo = CreateTestDisplayInfo();
        var viewModel = new ProviderStatusCardViewModel(displayInfo);
        var status = CreateTestStatus(isHealthy: true, requiresSetup: false);
        viewModel.UpdateFromProviderStatus(status);

        string? firedProvider = null;
        viewModel.ConfigurationRequested += (sender, provider) => firedProvider = provider;

        // Act
        await viewModel.HandleActionCommand.ExecuteAsync(null);

        // Assert
        Assert.Equal("TestProvider", firedProvider);
    }

    [Fact]
    public async Task RefreshStatusCommand_ShouldFireRefreshRequested()
    {
        // Arrange
        var displayInfo = CreateTestDisplayInfo();
        var viewModel = new ProviderStatusCardViewModel(displayInfo);

        string? firedProvider = null;
        viewModel.RefreshRequested += (sender, provider) => firedProvider = provider;

        // Act
        await viewModel.RefreshStatusCommand.ExecuteAsync(null);

        // Assert
        Assert.Equal("TestProvider", firedProvider);
    }

    [Fact]
    public void RefreshComputedProperties_ShouldTriggerPropertyChanged()
    {
        // Arrange
        var displayInfo = CreateTestDisplayInfo();
        var viewModel = new ProviderStatusCardViewModel(displayInfo);
        var propertyChangedCount = 0;

        viewModel.PropertyChanged += (sender, e) => propertyChangedCount++;

        // Act
        viewModel.RefreshComputedProperties();

        // Assert
        Assert.True(propertyChangedCount > 0);
    }
}