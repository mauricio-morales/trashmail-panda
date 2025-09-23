using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.DependencyInjection;
using System;
using System.Threading.Tasks;
using TrashMailPanda.Views;
using TrashMailPanda.Services;
using TrashMailPanda.Models;
using TrashMailPanda.Shared.Security;
using TrashMailPanda.Shared.Models;

namespace TrashMailPanda.ViewModels;

/// <summary>
/// Main window ViewModel that manages navigation between provider dashboard and email dashboard
/// Serves as the central navigation hub for the application
/// </summary>
public partial class MainWindowViewModel : ViewModelBase
{
    private readonly ProviderStatusDashboardViewModel _providerDashboardViewModel;
    private readonly EmailDashboardViewModel _emailDashboardViewModel;
    private readonly IServiceProvider _serviceProvider;
    private readonly IGoogleOAuthService _googleOAuthService;
    private readonly IDialogService _dialogService;
    private readonly ILogger<MainWindowViewModel> _logger;

    // Navigation State
    [ObservableProperty]
    private ViewModelBase _currentView;

    [ObservableProperty]
    private bool _canAccessMainDashboard = false;

    [ObservableProperty]
    private bool _isNavigating = false;

    [ObservableProperty]
    private string _navigationStatus = string.Empty;

    // Window State
    [ObservableProperty]
    private string _windowTitle = "TrashMail Panda";

    [ObservableProperty]
    private bool _showStatusBar = true;

    public MainWindowViewModel(
        ProviderStatusDashboardViewModel providerDashboardViewModel,
        EmailDashboardViewModel emailDashboardViewModel,
        IServiceProvider serviceProvider,
        IGoogleOAuthService googleOAuthService,
        IDialogService dialogService,
        ILogger<MainWindowViewModel> logger)
    {
        _providerDashboardViewModel = providerDashboardViewModel ?? throw new ArgumentNullException(nameof(providerDashboardViewModel));
        _emailDashboardViewModel = emailDashboardViewModel ?? throw new ArgumentNullException(nameof(emailDashboardViewModel));
        _serviceProvider = serviceProvider ?? throw new ArgumentNullException(nameof(serviceProvider));
        _googleOAuthService = googleOAuthService ?? throw new ArgumentNullException(nameof(googleOAuthService));
        _dialogService = dialogService ?? throw new ArgumentNullException(nameof(dialogService));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));

        // Subscribe to provider dashboard events
        _providerDashboardViewModel.DashboardAccessRequested += OnDashboardAccessRequested;
        _providerDashboardViewModel.ProviderSetupRequested += OnProviderSetupRequested;
        _providerDashboardViewModel.ProviderConfigurationRequested += OnProviderConfigurationRequested;
        _providerDashboardViewModel.ProviderAuthenticationRequested += OnProviderAuthenticationRequested;

        // Subscribe to email dashboard events
        _emailDashboardViewModel.ReturnToDashboardRequested += OnReturnToDashboardRequested;
        _emailDashboardViewModel.EmailProcessingRequested += OnEmailProcessingRequested;

        // Monitor CanAccessMainDashboard changes from provider dashboard
        _providerDashboardViewModel.PropertyChanged += OnProviderDashboardPropertyChanged;

        // Start with provider dashboard as the default view
        _currentView = _providerDashboardViewModel;
        WindowTitle = "TrashMail Panda - Provider Status";
        NavigationStatus = "Provider Status Dashboard";

        _logger.LogInformation("MainWindowViewModel initialized with provider dashboard as default view");
    }

    /// <summary>
    /// Navigate to the provider status dashboard
    /// </summary>
    [RelayCommand]
    private async Task ShowProviderDashboardAsync()
    {
        if (CurrentView == _providerDashboardViewModel || IsNavigating)
            return;

        try
        {
            IsNavigating = true;
            NavigationStatus = "Navigating to Provider Status...";

            _logger.LogInformation("Navigating to provider status dashboard");

            CurrentView = _providerDashboardViewModel;
            WindowTitle = "TrashMail Panda - Provider Status";
            NavigationStatus = "Provider Status Dashboard";

            // Refresh provider status when returning to dashboard
            await _providerDashboardViewModel.RefreshAllProvidersCommand.ExecuteAsync(null);

            _logger.LogInformation("Successfully navigated to provider status dashboard");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Exception navigating to provider status dashboard");
            NavigationStatus = "Navigation failed";
        }
        finally
        {
            IsNavigating = false;
        }
    }

    /// <summary>
    /// Navigate to the main email dashboard (if allowed)
    /// </summary>
    [RelayCommand]
    private async Task ShowMainDashboardAsync()
    {
        if (!CanAccessMainDashboard || CurrentView == _emailDashboardViewModel || IsNavigating)
        {
            if (!CanAccessMainDashboard)
            {
                _logger.LogWarning("Attempted to access main dashboard but providers not ready");
                NavigationStatus = "Providers not ready - setup required";
            }
            return;
        }

        try
        {
            IsNavigating = true;
            NavigationStatus = "Navigating to Email Dashboard...";

            _logger.LogInformation("Navigating to email dashboard");

            CurrentView = _emailDashboardViewModel;
            WindowTitle = "TrashMail Panda - Email Processing";
            NavigationStatus = "Email Processing Dashboard";

            // Initialize the email dashboard
            await _emailDashboardViewModel.InitializeAsync();

            _logger.LogInformation("Successfully navigated to email dashboard");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Exception navigating to email dashboard");
            NavigationStatus = "Navigation failed - returning to provider status";

            // Fallback to provider dashboard on error
            CurrentView = _providerDashboardViewModel;
            WindowTitle = "TrashMail Panda - Provider Status";
        }
        finally
        {
            IsNavigating = false;
        }
    }

    /// <summary>
    /// Handle dashboard access requests from provider dashboard
    /// </summary>
    private async void OnDashboardAccessRequested(object? sender, EventArgs e)
    {
        try
        {
            _logger.LogInformation("Dashboard access requested from provider dashboard");
            await ShowMainDashboardAsync();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Exception handling dashboard access request");
        }
    }

    /// <summary>
    /// Handle return to dashboard requests from email dashboard
    /// </summary>
    private async void OnReturnToDashboardRequested(object? sender, EventArgs e)
    {
        try
        {
            _logger.LogInformation("Return to dashboard requested from email dashboard");
            await ShowProviderDashboardAsync();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Exception handling return to dashboard request");
        }
    }

    /// <summary>
    /// Handle email processing requests from email dashboard
    /// </summary>
    private void OnEmailProcessingRequested(object? sender, EventArgs e)
    {
        try
        {
            _logger.LogInformation("Email processing requested from email dashboard");

            // TODO: Implement email processing workflow
            NavigationStatus = "Email processing workflow starting...";

            // For now, this is a placeholder
            // In the future, this would navigate to the email processing UI
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Exception handling email processing request");
        }
    }

    /// <summary>
    /// Handle provider setup requests from provider dashboard
    /// </summary>
    private async void OnProviderSetupRequested(object? sender, string providerName)
    {
        try
        {
            _logger.LogInformation("Provider setup requested for {Provider}", providerName);

            NavigationStatus = $"Opening {providerName} setup...";

            // Open appropriate setup dialog based on provider type
            switch (providerName?.ToLowerInvariant())
            {
                case "gmail":
                case "contacts":
                case "googleservices":
                    _logger.LogInformation("Opening Google OAuth setup dialog for {Provider}", providerName);
                    var googleResult = await _dialogService.ShowGoogleOAuthSetupAsync();
                    NavigationStatus = googleResult ? "Google OAuth setup completed" : "Google OAuth setup cancelled";
                    break;

                case "openai":
                    _logger.LogInformation("Opening OpenAI API key setup dialog");
                    var openAIResult = await _dialogService.ShowOpenAISetupAsync();
                    NavigationStatus = openAIResult ? "OpenAI setup completed" : "OpenAI setup cancelled";
                    break;

                default:
                    _logger.LogInformation("Generic setup for {Provider} not yet implemented", providerName);
                    NavigationStatus = $"{providerName} setup not yet implemented";
                    await Task.Delay(2000);
                    break;
            }

            // Reset navigation status
            NavigationStatus = IsOnProviderDashboard
                ? "Provider Status Dashboard"
                : "Email Processing Dashboard";
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Exception handling provider setup request for {Provider}", providerName);
            NavigationStatus = "Setup failed - check logs";
        }
    }

    /// <summary>
    /// Handle provider configuration requests from provider dashboard
    /// </summary>
    private async void OnProviderConfigurationRequested(object? sender, string providerName)
    {
        try
        {
            _logger.LogInformation("Provider configuration requested for {Provider}", providerName);

            NavigationStatus = $"Opening {providerName} configuration...";

            // Open appropriate setup dialog based on provider type
            switch (providerName?.ToLowerInvariant())
            {
                case "gmail":
                case "contacts":
                case "googleservices":
                    _logger.LogInformation("Opening Google OAuth setup dialog for {Provider} configuration", providerName);
                    var googleSetupResult = await _dialogService.ShowGoogleOAuthSetupAsync();
                    if (googleSetupResult)
                    {
                        NavigationStatus = "Google OAuth setup completed successfully";
                    }
                    else
                    {
                        NavigationStatus = "Google OAuth setup was cancelled";
                    }
                    break;

                case "openai":
                    _logger.LogInformation("Opening OpenAI API key setup dialog for configuration");
                    var openAISetupResult = await _dialogService.ShowOpenAISetupAsync();
                    if (openAISetupResult)
                    {
                        NavigationStatus = "OpenAI setup completed successfully";
                    }
                    else
                    {
                        NavigationStatus = "OpenAI setup was cancelled";
                    }
                    break;

                default:
                    _logger.LogInformation("Generic configuration for {Provider} not yet implemented", providerName);
                    NavigationStatus = $"{providerName} configuration not yet implemented";
                    await Task.Delay(2000);
                    break;
            }

            // Reset navigation status
            NavigationStatus = IsOnProviderDashboard
                ? "Provider Status Dashboard"
                : "Email Processing Dashboard";
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Exception handling provider configuration request for {Provider}", providerName);
            NavigationStatus = "Configuration failed - check logs";
        }
    }

    /// <summary>
    /// Handle provider authentication requests from provider dashboard
    /// </summary>
    private async void OnProviderAuthenticationRequested(object? sender, string providerName)
    {
        try
        {
            _logger.LogInformation("Provider authentication requested for {Provider}", providerName);

            NavigationStatus = $"Starting {providerName} authentication...";

            switch (providerName?.ToLowerInvariant())
            {
                case "gmail":
                case "contacts":
                case "googleservices":
                    _logger.LogInformation("Initiating unified Google Services OAuth authentication in browser");
                    NavigationStatus = "Opening browser for Google sign-in (Gmail + Contacts)...";

                    // Retrieve Google OAuth client credentials from secure storage
                    var googleCredentials = await GetGoogleOAuthCredentialsAsync();
                    if (googleCredentials == null)
                    {
                        _logger.LogWarning("Google OAuth client credentials not available - redirecting to setup");
                        NavigationStatus = "Google OAuth setup required - opening setup dialog...";
                        await _dialogService.ShowGoogleOAuthSetupAsync();
                        return;
                    }

                    // Use expanded scopes that include both Gmail and People API access
                    var unifiedScopes = new[] {
                        "https://www.googleapis.com/auth/gmail.modify",
                        "https://www.googleapis.com/auth/contacts.readonly",
                        "https://www.googleapis.com/auth/userinfo.profile"
                    };

                    var authResult = await _googleOAuthService.AuthenticateWithBrowserAsync(
                        unifiedScopes, // Use expanded scopes for both Gmail and Contacts
                        "google_", // Use shared prefix - unified Google OAuth tokens
                        googleCredentials.Value.clientId,
                        googleCredentials.Value.clientSecret);

                    if (authResult.IsSuccess)
                    {
                        _logger.LogInformation("Google Services OAuth authentication completed successfully with unified scopes");
                        NavigationStatus = "Google authentication successful - reinitializing services...";

                        // Re-initialize unified Google Services provider to pick up new OAuth tokens
                        var startupOrchestrator = _serviceProvider.GetRequiredService<IStartupOrchestrator>();
                        var reinitResult = await startupOrchestrator.ReinitializeGoogleServicesProviderAsync();

                        if (reinitResult.IsSuccess)
                        {
                            _logger.LogInformation("Google Services provider re-initialized successfully after OAuth");
                            NavigationStatus = "Google Services connected successfully";

                            // Now refresh status to reflect the healthy state
                            await _providerDashboardViewModel.RefreshAllProvidersCommand.ExecuteAsync(null);
                        }
                        else
                        {
                            _logger.LogWarning("Google Services provider re-initialization failed: {Error}", reinitResult.Error?.Message);
                            NavigationStatus = "Google authentication completed but provider initialization failed";
                        }

                        NavigationStatus = "Google Services authentication completed";
                    }
                    else
                    {
                        _logger.LogWarning("Google Services OAuth authentication failed: {Error}", authResult.Error?.Message);
                        NavigationStatus = $"Google Services authentication failed: {authResult.Error?.Message}";
                    }
                    break;

                default:
                    _logger.LogInformation("Authentication for {Provider} not implemented", providerName);
                    NavigationStatus = $"{providerName} authentication not implemented";
                    await Task.Delay(2000);
                    break;
            }

            // Reset navigation status after a delay
            _ = Task.Delay(3000).ContinueWith(_ =>
            {
                NavigationStatus = IsOnProviderDashboard
                    ? "Provider Status Dashboard"
                    : "Email Processing Dashboard";
            });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Exception handling provider authentication request for {Provider}", providerName);
            NavigationStatus = "Authentication failed - check logs";
        }
    }

    /// <summary>
    /// Check if Google OAuth client credentials (client_id, client_secret) already exist in secure storage
    /// </summary>
    private async Task<bool> CheckForExistingGoogleCredentialsAsync()
    {
        try
        {
            var secureStorage = _serviceProvider.GetService<ISecureStorageManager>();
            if (secureStorage == null)
            {
                _logger.LogWarning("SecureStorageManager not available for credential check");
                return false;
            }

            // Check for new Google shared credentials
            var clientIdResult = await secureStorage.RetrieveCredentialAsync(ProviderCredentialTypes.GoogleClientId);
            var clientSecretResult = await secureStorage.RetrieveCredentialAsync(ProviderCredentialTypes.GoogleClientSecret);

            // Also check for old Gmail-specific credentials for debugging
            var oldGmailClientIdResult = await secureStorage.RetrieveCredentialAsync(ProviderCredentialTypes.GmailClientId);
            var oldGmailClientSecretResult = await secureStorage.RetrieveCredentialAsync(ProviderCredentialTypes.GmailClientSecret);

            var hasNewCredentials = clientIdResult.IsSuccess && !string.IsNullOrWhiteSpace(clientIdResult.Value) &&
                                   clientSecretResult.IsSuccess && !string.IsNullOrWhiteSpace(clientSecretResult.Value);

            var hasOldCredentials = oldGmailClientIdResult.IsSuccess && !string.IsNullOrWhiteSpace(oldGmailClientIdResult.Value) &&
                                   oldGmailClientSecretResult.IsSuccess && !string.IsNullOrWhiteSpace(oldGmailClientSecretResult.Value);

            _logger.LogInformation("Credential check - New Google credentials: {HasNew}, Old Gmail credentials: {HasOld}",
                hasNewCredentials, hasOldCredentials);

            if (hasOldCredentials && !hasNewCredentials)
            {
                _logger.LogInformation("Found old Gmail-specific credentials but no new Google shared credentials - user needs to migrate");
            }

            return hasNewCredentials;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Exception checking for existing Google OAuth client credentials");
            return false;
        }
    }

    /// <summary>
    /// Retrieve Google OAuth client credentials from secure storage
    /// </summary>
    private async Task<(string clientId, string clientSecret)?> GetGoogleOAuthCredentialsAsync()
    {
        try
        {
            var secureStorage = _serviceProvider.GetService<ISecureStorageManager>();
            if (secureStorage == null)
            {
                _logger.LogWarning("SecureStorageManager not available for credential retrieval");
                return null;
            }

            var clientIdResult = await secureStorage.RetrieveCredentialAsync(ProviderCredentialTypes.GoogleClientId);
            var clientSecretResult = await secureStorage.RetrieveCredentialAsync(ProviderCredentialTypes.GoogleClientSecret);

            if (!clientIdResult.IsSuccess || !clientSecretResult.IsSuccess ||
                string.IsNullOrWhiteSpace(clientIdResult.Value) || string.IsNullOrWhiteSpace(clientSecretResult.Value))
            {
                _logger.LogWarning("Google OAuth client credentials not available or empty");
                return null;
            }

            _logger.LogDebug("Successfully retrieved Google OAuth client credentials");
            return (clientIdResult.Value, clientSecretResult.Value);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Exception retrieving Google OAuth client credentials");
            return null;
        }
    }


    /// <summary>
    /// Monitor CanAccessMainDashboard changes from provider dashboard
    /// </summary>
    private void OnProviderDashboardPropertyChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(ProviderStatusDashboardViewModel.CanAccessMainDashboard))
        {
            CanAccessMainDashboard = _providerDashboardViewModel.CanAccessMainDashboard;

            _logger.LogDebug("CanAccessMainDashboard updated to {CanAccess}", CanAccessMainDashboard);

            // Update navigation status based on availability
            if (CurrentView == _providerDashboardViewModel)
            {
                NavigationStatus = CanAccessMainDashboard
                    ? "All providers ready - email dashboard available"
                    : "Provider setup or issues detected";
            }
        }
    }

    /// <summary>
    /// Get the current navigation state description
    /// </summary>
    public string CurrentViewDescription
    {
        get
        {
            return CurrentView switch
            {
                ProviderStatusDashboardViewModel => "Provider Status Dashboard",
                EmailDashboardViewModel => "Email Processing Dashboard",
                _ => "Unknown View"
            };
        }
    }

    /// <summary>
    /// Check if we're currently on the provider dashboard
    /// </summary>
    public bool IsOnProviderDashboard => CurrentView == _providerDashboardViewModel;

    /// <summary>
    /// Check if we're currently on the email dashboard
    /// </summary>
    public bool IsOnEmailDashboard => CurrentView == _emailDashboardViewModel;

    /// <summary>
    /// Cleanup when view model is no longer needed
    /// </summary>
    public void Cleanup()
    {
        // Unsubscribe from events
        if (_providerDashboardViewModel != null)
        {
            _providerDashboardViewModel.DashboardAccessRequested -= OnDashboardAccessRequested;
            _providerDashboardViewModel.ProviderSetupRequested -= OnProviderSetupRequested;
            _providerDashboardViewModel.ProviderConfigurationRequested -= OnProviderConfigurationRequested;
            _providerDashboardViewModel.ProviderAuthenticationRequested -= OnProviderAuthenticationRequested;
            _providerDashboardViewModel.PropertyChanged -= OnProviderDashboardPropertyChanged;
        }

        if (_emailDashboardViewModel != null)
        {
            _emailDashboardViewModel.ReturnToDashboardRequested -= OnReturnToDashboardRequested;
            _emailDashboardViewModel.EmailProcessingRequested -= OnEmailProcessingRequested;
        }

        _logger.LogInformation("MainWindowViewModel cleanup completed");
    }
}
