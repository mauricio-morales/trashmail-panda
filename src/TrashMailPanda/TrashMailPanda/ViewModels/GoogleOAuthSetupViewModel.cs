using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Extensions.Logging;
using System;
using System.Diagnostics;
using System.Threading.Tasks;
using TrashMailPanda.Models;
using TrashMailPanda.Shared.Security;
using TrashMailPanda.Shared;
using TrashMailPanda.Shared.Models;

namespace TrashMailPanda.ViewModels;

/// <summary>
/// ViewModel for Google OAuth setup dialog
/// Handles the two-tier Google OAuth credential system for all Google services (Gmail, Contacts, etc.):
/// 1. OAuth Client credentials (client_id, client_secret)
/// 2. User access tokens (access_token, refresh_token) - generated after login
/// </summary>
public partial class GoogleOAuthSetupViewModel : ViewModelBase
{
    private readonly ISecureStorageManager _secureStorage;
    private readonly ILogger<GoogleOAuthSetupViewModel> _logger;

    // OAuth Client Credentials (First tier - for OAuth app registration)
    [ObservableProperty]
    private string _clientId = string.Empty;

    [ObservableProperty]
    private string _clientSecret = string.Empty;

    [ObservableProperty]
    private string _redirectUri = "http://localhost:8080/oauth/callback";

    // UI State
    [ObservableProperty]
    private string? _validationMessage;

    [ObservableProperty]
    private string _statusMessage = "Please setup the Google OAuth client credentials to continue";

    [ObservableProperty]
    private bool _isValidating = false;

    [ObservableProperty]
    private bool _isValidated = false;

    [ObservableProperty]
    private bool _canSave = false;

    [ObservableProperty]
    private bool _hasExistingCredentials = false;

    [ObservableProperty]
    private string _oauthSetupPhase = "OAuth Client Setup";

    // Dialog result for parent window
    public bool DialogResult { get; private set; }

    // Events
    public event EventHandler? RequestClose;
    public event EventHandler? RequestGoogleSignIn;

    public string SaveButtonText => IsValidating
        ? "Validating..."
        : HasExistingCredentials
            ? "Test & Save"
            : "Save & Test Connection";

    public string ClientCredentialsStatusText => HasExistingCredentials
        ? "✅ Google OAuth client credentials are already saved"
        : "Enter your Google OAuth client credentials";

    public GoogleOAuthSetupViewModel(ISecureStorageManager secureStorage, ILogger<GoogleOAuthSetupViewModel> logger)
    {
        _secureStorage = secureStorage ?? throw new ArgumentNullException(nameof(secureStorage));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));

        // Load existing credentials if available
        _ = Task.Run(LoadExistingCredentialsAsync);

        // Watch for input changes to enable/disable save button
        this.PropertyChanged += OnPropertyChanged;
    }

    private void OnPropertyChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
    {
        if (e.PropertyName is nameof(ClientId) or nameof(ClientSecret))
        {
            UpdateCanSaveState();
        }
    }

    private void UpdateCanSaveState()
    {
        CanSave = !string.IsNullOrWhiteSpace(ClientId) &&
                  !string.IsNullOrWhiteSpace(ClientSecret) &&
                  !IsValidating;
    }

    /// <summary>
    /// Load existing OAuth client credentials from secure storage
    /// </summary>
    private async Task LoadExistingCredentialsAsync()
    {
        try
        {
            _logger.LogDebug("Loading existing Google OAuth client credentials");

            var clientIdResult = await _secureStorage.RetrieveCredentialAsync(ProviderCredentialTypes.GoogleClientId);
            var clientSecretResult = await _secureStorage.RetrieveCredentialAsync(ProviderCredentialTypes.GoogleClientSecret);

            if (clientIdResult.IsSuccess && clientSecretResult.IsSuccess)
            {
                ClientId = clientIdResult.Value ?? string.Empty;
                ClientSecret = clientSecretResult.Value ?? string.Empty;
                HasExistingCredentials = true;
                StatusMessage = "Found existing OAuth client credentials";

                _logger.LogInformation("Loaded existing Google OAuth client credentials");
            }
            else
            {
                HasExistingCredentials = false;
                StatusMessage = "Please setup the Google OAuth client credentials to continue";
                _logger.LogDebug("No existing Google OAuth client credentials found");
            }

            UpdateCanSaveState();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error loading existing Google OAuth client credentials");
            StatusMessage = "Error loading existing credentials";
        }
    }

    /// <summary>
    /// Validate OAuth client credentials and save them
    /// </summary>
    [RelayCommand]
    private async Task ValidateAndSaveAsync()
    {
        if (IsValidating) return;

        try
        {
            IsValidating = true;
            ValidationMessage = null;
            StatusMessage = "Validating OAuth client credentials...";

            // Basic validation
            if (string.IsNullOrWhiteSpace(ClientId))
            {
                ValidationMessage = "Client ID is required";
                return;
            }

            if (string.IsNullOrWhiteSpace(ClientSecret))
            {
                ValidationMessage = "Client Secret is required";
                return;
            }

            if (string.IsNullOrWhiteSpace(RedirectUri))
            {
                ValidationMessage = "Redirect URI is required";
                return;
            }

            // Validate format
            if (!ClientId.EndsWith(".googleusercontent.com"))
            {
                ValidationMessage = "Client ID should end with '.googleusercontent.com'";
                return;
            }

            if (!Uri.TryCreate(RedirectUri, UriKind.Absolute, out var uri))
            {
                ValidationMessage = "Redirect URI must be a valid URL";
                return;
            }

            StatusMessage = "Saving OAuth client credentials...";

            // Save OAuth client credentials to secure storage
            var clientIdSave = await _secureStorage.StoreCredentialAsync(ProviderCredentialTypes.GoogleClientId, ClientId);
            var clientSecretSave = await _secureStorage.StoreCredentialAsync(ProviderCredentialTypes.GoogleClientSecret, ClientSecret);
            var redirectUriSave = await _secureStorage.StoreCredentialAsync("gmail_redirect_uri", RedirectUri);

            if (!clientIdSave.IsSuccess || !clientSecretSave.IsSuccess || !redirectUriSave.IsSuccess)
            {
                ValidationMessage = "Failed to save credentials securely";
                _logger.LogError("Failed to save Gmail OAuth client credentials");
                return;
            }

            StatusMessage = "OAuth client credentials saved successfully!";
            IsValidated = true;
            HasExistingCredentials = true;

            _logger.LogInformation("Gmail OAuth client credentials saved successfully");

            // Wait a moment to show success message
            await Task.Delay(1500);

            // Close dialog with success and trigger Gmail sign-in
            DialogResult = true;
            RequestClose?.Invoke(this, EventArgs.Empty);

            // Trigger Google sign-in after dialog closes
            RequestGoogleSignIn?.Invoke(this, EventArgs.Empty);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Exception during Google OAuth client credential validation");
            ValidationMessage = $"Error during validation: {ex.Message}";
        }
        finally
        {
            IsValidating = false;
        }
    }

    /// <summary>
    /// Open Google Cloud Console to create OAuth credentials
    /// </summary>
    [RelayCommand]
    private async Task OpenGoogleCloudConsoleAsync()
    {
        await Task.CompletedTask;

        try
        {
            const string url = "https://console.cloud.google.com/apis/credentials";

            _logger.LogInformation("Opening Google Cloud Console for OAuth credential setup");

            var processInfo = new ProcessStartInfo
            {
                FileName = url,
                UseShellExecute = true
            };

            Process.Start(processInfo);
            StatusMessage = "Opened Google Cloud Console in your browser";
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to open Google Cloud Console");
            StatusMessage = "Failed to open browser - please visit https://console.cloud.google.com/apis/credentials";
        }
    }

    /// <summary>
    /// Open Gmail API documentation
    /// </summary>
    [RelayCommand]
    private async Task OpenGmailApiDocsAsync()
    {
        await Task.CompletedTask;

        try
        {
            const string url = "https://developers.google.com/gmail/api/guides";

            _logger.LogInformation("Opening Gmail API documentation");

            var processInfo = new ProcessStartInfo
            {
                FileName = url,
                UseShellExecute = true
            };

            Process.Start(processInfo);
            StatusMessage = "Opened Gmail API documentation in your browser";
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to open Gmail API documentation");
            StatusMessage = "Failed to open browser - please visit https://developers.google.com/gmail/api/guides";
        }
    }

    /// <summary>
    /// Cancel setup
    /// </summary>
    [RelayCommand]
    private void Cancel()
    {
        _logger.LogDebug("Gmail OAuth setup cancelled by user");
        DialogResult = false;
        RequestClose?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>
    /// Clear all saved Gmail credentials (for troubleshooting)
    /// </summary>
    [RelayCommand]
    private async Task ClearAllCredentialsAsync()
    {
        try
        {
            _logger.LogInformation("Clearing all Gmail OAuth credentials");

            // Clear OAuth client credentials
            await _secureStorage.RemoveCredentialAsync(ProviderCredentialTypes.GoogleClientId);
            await _secureStorage.RemoveCredentialAsync(ProviderCredentialTypes.GoogleClientSecret);
            await _secureStorage.RemoveCredentialAsync("gmail_redirect_uri");

            // Clear user access tokens (these will be cleaned up by GoogleOAuthService)
            await _secureStorage.RemoveCredentialAsync(ProviderCredentialTypes.GoogleAccessToken);
            await _secureStorage.RemoveCredentialAsync(ProviderCredentialTypes.GoogleRefreshToken);

            // Reset UI state
            ClientId = string.Empty;
            ClientSecret = string.Empty;
            HasExistingCredentials = false;
            IsValidated = false;
            ValidationMessage = null;
            StatusMessage = "All Gmail credentials cleared - setup from scratch";

            UpdateCanSaveState();

            _logger.LogInformation("All Gmail OAuth credentials cleared successfully");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error clearing Gmail OAuth credentials");
            StatusMessage = "Error clearing credentials - check logs";
        }
    }
}