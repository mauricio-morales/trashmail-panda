using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Extensions.Logging;
using System;
using System.Threading.Tasks;
using TrashMailPanda.Models;
using TrashMailPanda.Shared.Security;
using TrashMailPanda.Shared;
using TrashMailPanda.Shared.Models;

namespace TrashMailPanda.ViewModels;

/// <summary>
/// ViewModel for OpenAI API key setup dialog
/// Handles secure storage of API keys and validation
/// </summary>
public partial class OpenAISetupViewModel : ViewModelBase
{
    private readonly ISecureStorageManager _secureStorage;
    private readonly ILogger<OpenAISetupViewModel> _logger;

    [ObservableProperty]
    private string _apiKey = string.Empty;

    [ObservableProperty]
    private string? _validationMessage;

    [ObservableProperty]
    private string _statusMessage = "Please setup your OpenAI API key to continue";

    [ObservableProperty]
    private bool _isValidating = false;

    [ObservableProperty]
    private bool _isValidated = false;

    [ObservableProperty]
    private bool _canSave = false;

    // Dialog result for parent window
    public bool DialogResult { get; private set; }

    // Events
    public event EventHandler? RequestClose;

    public string SaveButtonText => IsValidating ? "Validating..." : (IsValidated ? "Save & Close" : "Validate & Save");

    public OpenAISetupViewModel(ISecureStorageManager secureStorage, ILogger<OpenAISetupViewModel> logger)
    {
        _secureStorage = secureStorage ?? throw new ArgumentNullException(nameof(secureStorage));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));

        // Subscribe to property changes to update validation
        PropertyChanged += OnPropertyChanged;
    }

    private void OnPropertyChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(ApiKey))
        {
            ValidateApiKeyFormat();
        }
    }

    /// <summary>
    /// Validate API key format and update UI state
    /// </summary>
    private void ValidateApiKeyFormat()
    {
        ValidationMessage = null;
        IsValidated = false;

        if (string.IsNullOrWhiteSpace(ApiKey))
        {
            CanSave = false;
            return;
        }

        // Basic format validation for OpenAI API keys
        if (!ApiKey.StartsWith("sk-", StringComparison.OrdinalIgnoreCase))
        {
            ValidationMessage = "OpenAI API keys should start with 'sk-'";
            CanSave = false;
            return;
        }

        if (ApiKey.Length < 20)
        {
            ValidationMessage = "API key appears too short";
            CanSave = false;
            return;
        }

        // Format looks good
        CanSave = true;
        ValidationMessage = null;
    }

    /// <summary>
    /// Open OpenAI API keys dashboard in browser
    /// </summary>
    [RelayCommand]
    private async Task OpenOpenAIDashboardAsync()
    {
        await Task.CompletedTask;

        try
        {
            var url = "https://platform.openai.com/api-keys";
            System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
            {
                FileName = url,
                UseShellExecute = true
            });

            _logger.LogInformation("Opened OpenAI dashboard URL: {Url}", url);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to open OpenAI dashboard");
            StatusMessage = "Could not open browser. Please navigate to platform.openai.com/api-keys manually.";
        }
    }

    /// <summary>
    /// Validate the API key and save to secure storage
    /// </summary>
    [RelayCommand]
    private async Task ValidateAndSaveAsync()
    {
        if (!CanSave || IsValidating)
            return;

        IsValidating = true;
        ValidationMessage = null;
        StatusMessage = "Validating API key...";

        try
        {
            _logger.LogInformation("Starting OpenAI API key validation and save process");

            // Basic validation first
            ValidateApiKeyFormat();
            if (!CanSave)
            {
                return;
            }

            // TODO: Implement actual API validation by making a test call to OpenAI
            // For now, we'll just save the key after format validation
            await Task.Delay(1000); // Simulate API call

            // Save to secure storage
            var result = await _secureStorage.StoreCredentialAsync(
                ProviderCredentialTypes.OpenAIApiKey,
                ApiKey.Trim());

            if (result.IsSuccess)
            {
                IsValidated = true;
                StatusMessage = "API key saved successfully";
                ValidationMessage = null;
                DialogResult = true;

                _logger.LogInformation("OpenAI API key saved successfully to secure storage");

                // Close dialog after short delay to show success message
                await Task.Delay(1500);
                RequestClose?.Invoke(this, EventArgs.Empty);
            }
            else
            {
                ValidationMessage = $"Failed to save API key: {result.ErrorMessage}";
                _logger.LogError("Failed to save OpenAI API key: {Error}", result.ErrorMessage);
            }
        }
        catch (Exception ex)
        {
            ValidationMessage = "An error occurred while saving the API key";
            _logger.LogError(ex, "Exception during OpenAI API key validation and save");
        }
        finally
        {
            IsValidating = false;
            if (!IsValidated)
            {
                StatusMessage = string.Empty;
            }
        }
    }

    /// <summary>
    /// Cancel the dialog
    /// </summary>
    [RelayCommand]
    private void Cancel()
    {
        _logger.LogInformation("OpenAI setup dialog cancelled by user");
        DialogResult = false;
        RequestClose?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>
    /// Load existing API key if available (for editing)
    /// </summary>
    public async Task LoadExistingApiKeyAsync()
    {
        try
        {
            var result = await _secureStorage.RetrieveCredentialAsync(ProviderCredentialTypes.OpenAIApiKey);
            if (result.IsSuccess && !string.IsNullOrEmpty(result.Value))
            {
                // Show masked version for security
                var maskedKey = MaskApiKey(result.Value);
                StatusMessage = $"Current API key: {maskedKey}";
                _logger.LogInformation("Loaded existing OpenAI API key for editing");
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Could not load existing OpenAI API key");
        }
    }

    /// <summary>
    /// Mask API key for display (show first 7 and last 4 characters)
    /// </summary>
    private static string MaskApiKey(string apiKey)
    {
        if (string.IsNullOrEmpty(apiKey) || apiKey.Length < 12)
            return "sk-****";

        return $"{apiKey[..7]}...{apiKey[^4..]}";
    }
}