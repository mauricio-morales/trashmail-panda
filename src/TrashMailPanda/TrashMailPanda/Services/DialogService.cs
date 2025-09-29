using System;
using System.Threading.Tasks;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using TrashMailPanda.ViewModels;
using TrashMailPanda.Views;

namespace TrashMailPanda.Services;

/// <summary>
/// Avalonia UI implementation of IDialogService
/// Handles dialog display with proper parent window management and modal behavior
/// </summary>
public class DialogService : IDialogService
{
    private readonly IServiceProvider _serviceProvider;
    private readonly ILogger<DialogService> _logger;

    public DialogService(IServiceProvider serviceProvider, ILogger<DialogService> logger)
    {
        _serviceProvider = serviceProvider ?? throw new ArgumentNullException(nameof(serviceProvider));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    /// <summary>
    /// Show the Google OAuth setup dialog for Gmail, Contacts, and other Google services
    /// </summary>
    public async Task<bool> ShowGoogleOAuthSetupAsync()
    {
        try
        {
            _logger.LogInformation("Opening Google OAuth setup dialog");

            var viewModel = _serviceProvider.GetRequiredService<GoogleOAuthSetupViewModel>();
            var dialog = new GoogleOAuthSetupDialog(viewModel);

            // Subscribe to Google sign-in event for post-setup actions
            var setupCompleted = false;
            viewModel.RequestGoogleSignIn += async (sender, args) =>
            {
                _logger.LogInformation("Google OAuth setup completed - triggering provider refresh");
                setupCompleted = true;

                // Small delay to ensure credentials are fully persisted
                await Task.Delay(100);

                // Trigger refresh when credentials are saved
                await TriggerProviderRefreshAsync();
            };

            // Show dialog modal
            var parentWindow = GetMainWindow();
            var result = false;

            if (parentWindow != null)
            {
                result = await dialog.ShowDialog<bool>(parentWindow);
            }
            else
            {
                _logger.LogWarning("No parent window found, showing dialog without parent");
                dialog.Show();
                // For non-modal fallback, we assume success if viewModel indicates it
                result = viewModel.DialogResult;
            }

            if (result)
            {
                _logger.LogInformation("Google OAuth setup dialog completed with success result");

                // If refresh wasn't already triggered by the event, trigger it now
                if (!setupCompleted)
                {
                    _logger.LogInformation("Credentials were saved but event didn't fire - triggering refresh now");
                    await TriggerProviderRefreshAsync();
                }
            }
            else
            {
                _logger.LogInformation("Google OAuth setup was cancelled by user");
            }

            return result;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Exception occurred while showing Google OAuth setup dialog");
            await ShowErrorAsync("Setup Error", $"Failed to open Google OAuth setup: {ex.Message}");
            return false;
        }
    }

    /// <summary>
    /// Show the OpenAI API key setup dialog
    /// </summary>
    public async Task<bool> ShowOpenAISetupAsync()
    {
        try
        {
            _logger.LogInformation("Opening OpenAI API key setup dialog");

            var viewModel = _serviceProvider.GetRequiredService<OpenAISetupViewModel>();
            await viewModel.LoadExistingApiKeyAsync();

            var dialog = new OpenAISetupDialog(viewModel);

            // Subscribe to close event - set dialog result based on view model state
            viewModel.RequestClose += (sender, args) =>
            {
                dialog.Close(viewModel.DialogResult);
            };

            // Show dialog modal
            var parentWindow = GetMainWindow();
            var result = false;

            if (parentWindow != null)
            {
                result = await dialog.ShowDialog<bool>(parentWindow);
            }
            else
            {
                _logger.LogWarning("No parent window found, showing OpenAI dialog without parent");
                dialog.Show();
                result = viewModel.DialogResult;
            }

            if (result)
            {
                _logger.LogInformation("OpenAI setup completed successfully");
                // Note: Provider refresh is now handled by the OpenAISetupViewModel itself
                // for immediate UI feedback during the save process
            }
            else
            {
                _logger.LogInformation("OpenAI setup was cancelled by user");
            }

            return result;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Exception occurred while showing OpenAI setup dialog");
            await ShowErrorAsync("Setup Error", $"Failed to open OpenAI setup: {ex.Message}");
            return false;
        }
    }

    /// <summary>
    /// Show a generic confirmation dialog
    /// </summary>
    public async Task<bool> ShowConfirmationAsync(string title, string message, string confirmText = "OK", string cancelText = "Cancel")
    {
        try
        {
            var parentWindow = GetMainWindow();
            if (parentWindow != null)
            {
                // Use Avalonia's built-in message box for simple confirmations
                var messageBox = new Window
                {
                    Title = title,
                    Width = 400,
                    Height = 200,
                    WindowStartupLocation = WindowStartupLocation.CenterOwner,
                    CanResize = false
                };

                // For now, return true as a placeholder
                // In a full implementation, you'd create a proper confirmation dialog
                _logger.LogInformation("Showing confirmation dialog: {Title} - {Message}", title, message);
                return true;
            }

            return false;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Exception showing confirmation dialog");
            return false;
        }
    }

    /// <summary>
    /// Show an information dialog
    /// </summary>
    public async Task ShowInformationAsync(string title, string message)
    {
        try
        {
            _logger.LogInformation("Showing information dialog: {Title} - {Message}", title, message);

            // For now, just log the information
            // In a full implementation, you'd show an actual info dialog
            await Task.CompletedTask;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Exception showing information dialog");
        }
    }

    /// <summary>
    /// Show an error dialog
    /// </summary>
    public async Task ShowErrorAsync(string title, string message)
    {
        try
        {
            _logger.LogError("Showing error dialog: {Title} - {Message}", title, message);

            // For now, just log the error
            // In a full implementation, you'd show an actual error dialog
            await Task.CompletedTask;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Exception showing error dialog");
        }
    }

    /// <summary>
    /// Get the main application window for proper modal dialog parenting
    /// </summary>
    private Window? GetMainWindow()
    {
        try
        {
            if (Avalonia.Application.Current?.ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
            {
                return desktop.MainWindow;
            }

            _logger.LogWarning("Could not determine main window - not running in desktop mode");
            return null;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Exception getting main window");
            return null;
        }
    }

    /// <summary>
    /// Trigger provider status refresh after successful dialog completion
    /// </summary>
    private async Task TriggerProviderRefreshAsync()
    {
        try
        {
            _logger.LogInformation("Attempting to refresh provider status dashboard");

            // Get the provider dashboard view model and trigger refresh
            var dashboardViewModel = _serviceProvider.GetService<ProviderStatusDashboardViewModel>();
            if (dashboardViewModel != null)
            {
                _logger.LogInformation("Found ProviderStatusDashboardViewModel - executing refresh command");
                await dashboardViewModel.RefreshAllProvidersCommand.ExecuteAsync(null);
                _logger.LogInformation("Provider status refresh completed successfully");
            }
            else
            {
                _logger.LogWarning("Could not get ProviderStatusDashboardViewModel for refresh - provider status may not update");
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Exception triggering provider refresh after dialog completion");
        }
    }
}