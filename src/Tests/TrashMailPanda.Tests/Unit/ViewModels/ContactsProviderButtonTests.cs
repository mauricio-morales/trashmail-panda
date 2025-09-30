using Xunit;
using TrashMailPanda.ViewModels;
using TrashMailPanda.Models;
using TrashMailPanda.Shared.Models;
using TrashMailPanda.Services;

namespace TrashMailPanda.Tests.Unit.ViewModels;

/// <summary>
/// Unit tests for Contacts provider button functionality in ProviderStatusCardViewModel
/// Tests button visibility, text, and click behavior for Contacts provider
/// </summary>
public class ContactsProviderButtonTests
{
    private static ProviderDisplayInfo CreateContactsDisplayInfo()
    {
        return new ProviderDisplayInfo
        {
            Name = "Contacts",
            DisplayName = "Google Contacts",
            Description = "Access your Google Contacts for enhanced email classification",
            Type = ProviderType.Contacts,
            Icon = "📞",
            IsRequired = false
        };
    }

    private static ProviderStatus CreateContactsStatus(bool isHealthy = true, bool requiresSetup = false, string status = "Ready")
    {
        return new ProviderStatus
        {
            Name = "Contacts",
            IsHealthy = isHealthy,
            RequiresSetup = requiresSetup,
            Status = status,
            LastCheck = DateTime.UtcNow
        };
    }

    [Theory]
    [InlineData(true, false, true)]   // Always shows button - text changes based on state
    [InlineData(false, true, true)]   // Always shows button - text changes based on state
    [InlineData(false, false, true)]  // Always shows button - text changes based on state
    [InlineData(true, true, true)]    // Always shows button - text changes based on state
    public void ContactsProvider_ShowSetupButton_ShouldReturnCorrectValue(bool isHealthy, bool requiresSetup, bool expected)
    {
        // Arrange
        var displayInfo = CreateContactsDisplayInfo();
        var viewModel = new ProviderStatusCardViewModel(displayInfo);
        var status = CreateContactsStatus(isHealthy: isHealthy, requiresSetup: requiresSetup);

        // Act
        viewModel.UpdateFromProviderStatus(status);

        // Assert
        Assert.Equal(expected, viewModel.ShowSetupButton);
    }

    [Theory]
    [InlineData("OAuth Setup Required", "Configure Contacts")]
    [InlineData("Permission Denied", "Grant Access")]
    [InlineData("Authentication Required", "Configure Contacts")]
    [InlineData("Connection Failed", "Reconnect")]
    [InlineData("Ready", "Configure")]
    public void ContactsProvider_ActionButtonText_ShouldReturnCorrectText(string statusText, string expected)
    {
        // Arrange
        var displayInfo = CreateContactsDisplayInfo();
        var viewModel = new ProviderStatusCardViewModel(displayInfo);
        var status = CreateContactsStatus(status: statusText, requiresSetup: true);

        // Act
        viewModel.UpdateFromProviderStatus(status);

        // Assert
        Assert.Equal(expected, viewModel.ActionButtonText);
    }

    [Fact]
    public async Task ContactsProvider_HandleActionCommand_WhenRequiresSetup_ShouldFireSetupRequested()
    {
        // Arrange
        var displayInfo = CreateContactsDisplayInfo();
        var viewModel = new ProviderStatusCardViewModel(displayInfo);
        viewModel.UpdateFromProviderStatus(CreateContactsStatus(requiresSetup: true));

        string? firedProvider = null;
        viewModel.SetupRequested += (sender, provider) => firedProvider = provider;

        // Act
        await viewModel.HandleActionCommand.ExecuteAsync(null);

        // Assert
        Assert.Equal("Contacts", firedProvider);
    }

    [Fact]
    public async Task ContactsProvider_HandleActionCommand_WhenHealthy_ShouldFireConfigurationRequested()
    {
        // Arrange
        var displayInfo = CreateContactsDisplayInfo();
        var viewModel = new ProviderStatusCardViewModel(displayInfo);
        viewModel.UpdateFromProviderStatus(CreateContactsStatus(isHealthy: true, requiresSetup: false));

        string? firedProvider = null;
        viewModel.ConfigurationRequested += (sender, provider) => firedProvider = provider;

        // Act
        await viewModel.HandleActionCommand.ExecuteAsync(null);

        // Assert
        Assert.Equal("Contacts", firedProvider);
    }

    [Fact]
    public async Task ContactsProvider_RefreshStatusCommand_ShouldFireRefreshRequested()
    {
        // Arrange
        var displayInfo = CreateContactsDisplayInfo();
        var viewModel = new ProviderStatusCardViewModel(displayInfo);

        string? firedProvider = null;
        viewModel.RefreshRequested += (sender, provider) => firedProvider = provider;

        // Act
        await viewModel.RefreshStatusCommand.ExecuteAsync(null);

        // Assert
        Assert.Equal("Contacts", firedProvider);
    }

    [Fact]
    public void ContactsProvider_DisplayInfo_ShouldMatchExpectedValues()
    {
        // Arrange
        var displayInfo = CreateContactsDisplayInfo();
        var viewModel = new ProviderStatusCardViewModel(displayInfo);

        // Act & Assert
        Assert.Equal("Contacts", viewModel.ProviderName);
        Assert.Equal("Google Contacts", viewModel.ProviderDisplayName);
        Assert.Equal("Access your Google Contacts for enhanced email classification", viewModel.ProviderDescription);
        Assert.Equal("📞", viewModel.ProviderIcon);
        Assert.Equal(ProviderType.Contacts, displayInfo.Type);
    }
}