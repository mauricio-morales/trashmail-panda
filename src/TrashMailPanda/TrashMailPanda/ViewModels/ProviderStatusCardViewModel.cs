using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Extensions.Logging;
using TrashMailPanda.Models;
using TrashMailPanda.Services;
using TrashMailPanda.Shared;
using TrashMailPanda.Shared.Models;

namespace TrashMailPanda.ViewModels;

/// <summary>
/// ViewModel for individual provider status cards
/// Displays real-time provider health and setup status
/// </summary>
public partial class ProviderStatusCardViewModel : ViewModelBase
{
    private readonly ILogger<ProviderStatusCardViewModel>? _logger;

    // Core provider information
    [ObservableProperty]
    private ProviderDisplayInfo _displayInfo = new();

    [ObservableProperty]
    private ProviderStatus _status = new();

    [ObservableProperty]
    private ProviderSetupState _setupState = new();

    // UI state
    [ObservableProperty]
    private bool _isLoading = false;

    [ObservableProperty]
    private string _lastRefreshTime = string.Empty;

    [ObservableProperty]
    private bool _canConfigureProvider = true;

    [ObservableProperty]
    private string _statusMessage = string.Empty;

    // Computed properties for UI binding
    public string ProviderName => DisplayInfo.Name;
    public string ProviderIcon => DisplayInfo.Icon;
    public string ProviderDisplayName => DisplayInfo.DisplayName;
    public string ProviderDescription => DisplayInfo.Description;

    // Status indicators
    public bool IsHealthy => Status.IsHealthy;
    public bool RequiresSetup => Status.RequiresSetup;
    public bool IsInitialized => Status.IsInitialized;
    public string CurrentStatus => Status.Status ?? "Unknown";
    public string? ErrorMessage => Status.ErrorMessage;

    // Authenticated user information
    public AuthenticatedUserInfo? AuthenticatedUser => Status.AuthenticatedUser;
    public bool HasAuthenticatedUser => AuthenticatedUser != null;
    public string AuthenticatedUserEmail => AuthenticatedUser?.Email ?? string.Empty;
    public string AuthenticatedUserDisplayName => AuthenticatedUser?.DisplayName ?? string.Empty;

    // Setup information
    public string SetupComplexity => DisplayInfo.Complexity switch
    {
        Models.SetupComplexity.Simple => "Quick setup",
        Models.SetupComplexity.Moderate => "Moderate setup",
        Models.SetupComplexity.Complex => "Advanced setup",
        _ => "Setup required"
    };

    public string EstimatedTime => DisplayInfo.EstimatedSetupTimeMinutes switch
    {
        0 => "Automatic",
        1 => "1 minute",
        <= 5 => $"{DisplayInfo.EstimatedSetupTimeMinutes} minutes",
        <= 10 => "5-10 minutes",
        _ => "10+ minutes"
    };

    // Button states and text
    public bool ShowSetupButton => true; // Always show a button - let ActionButtonText determine appropriate action
    public bool ShowStatusDetails => IsInitialized || !string.IsNullOrEmpty(ErrorMessage);
    public bool IsValidating => CurrentStatus == "Validating";
    public bool CanTakeAction => !IsLoading && !IsValidating;

    public string ActionButtonText
    {
        get
        {
            // Check specific status messages first for appropriate actions
            return CurrentStatus switch
            {
                "Validating" => "Please wait...",
                "OAuth Setup Required" => "Setup OAuth",
                "API Key Required" => "Enter API Key",
                "Setup Required" => "Setup",
                "Authentication Required" => "Sign In",
                "API Key Invalid" => "Fix API Key",
                "Connection Failed" => "Reconnect",
                _ => IsHealthy && IsInitialized ? "Reconfigure" : RequiresSetup ? "Configure" : "Fix Issue"
            };
        }
    }

    public string StatusDisplayText
    {
        get
        {
            if (!string.IsNullOrEmpty(StatusMessage))
                return StatusMessage;

            if (IsLoading)
                return "Checking status...";

            return CurrentStatus switch
            {
                "Connected" => "✅ Connected and working",
                "Ready" => "✅ Ready to use",
                "Healthy" => "✅ Operating normally",
                "Validating" => "🔄 Checking provider status...",
                "OAuth Setup Required" => "⚙️ OAuth setup needed",
                "API Key Required" => "🔑 API key needed",
                "Authentication Required" => "🔐 Please sign in",
                "API Key Invalid" => "❌ Invalid API key",
                "Connection Failed" => "❌ Connection problem",
                "Database Error" => "❌ Database issue",
                _ => CurrentStatus ?? "❓ Status unknown"
            };
        }
    }

    // Events for parent communication
    public event EventHandler<string>? SetupRequested;
    public event EventHandler<string>? ConfigurationRequested;
    public event EventHandler<string>? AuthenticationRequested;
    public event EventHandler<string>? RefreshRequested;

    /// <summary>
    /// Initialize the view model with provider information
    /// </summary>
    public ProviderStatusCardViewModel(ProviderDisplayInfo displayInfo, ILogger<ProviderStatusCardViewModel>? logger = null)
    {
        DisplayInfo = displayInfo ?? throw new ArgumentNullException(nameof(displayInfo));
        _logger = logger;

        // Initialize with default status - show as "Validating" while waiting for first health check
        Status = new ProviderStatus
        {
            Name = displayInfo.Name,
            IsHealthy = false,
            IsInitialized = false,
            RequiresSetup = false, // Don't show setup required until we know the actual status
            Status = "Validating",
            LastCheck = DateTime.MinValue
        };

        // Initialize setup state
        SetupState = new ProviderSetupState
        {
            ProviderName = displayInfo.Name,
            CurrentStep = SetupStep.NotStarted
        };

        UpdateLastRefreshTime();
    }

    /// <summary>
    /// Update the view model with new provider status
    /// </summary>
    public void UpdateFromProviderStatus(ProviderStatus newStatus)
    {
        if (newStatus == null)
        {
            _logger?.LogWarning("[CARD UPDATE] UpdateFromProviderStatus called with null status for provider {Provider}", ProviderName);
            return;
        }

        _logger?.LogInformation("[CARD UPDATE] Updating provider {Provider} - Old: Status={OldStatus}, Healthy={OldHealthy}, RequiresSetup={OldSetup}, Initialized={OldInit}",
            ProviderName, CurrentStatus, IsHealthy, RequiresSetup, IsInitialized);

        Status = newStatus;
        UpdateLastRefreshTime();

        _logger?.LogInformation("[CARD UPDATE] Updated provider {Provider} - New: Status={NewStatus}, Healthy={NewHealthy}, RequiresSetup={NewSetup}, Initialized={NewInit}",
            ProviderName, CurrentStatus, IsHealthy, RequiresSetup, IsInitialized);

        // Clear any temporary status messages when status is updated
        if (!IsLoading)
        {
            StatusMessage = string.Empty;
        }

        // Update all computed properties
        OnPropertyChanged(nameof(IsHealthy));
        OnPropertyChanged(nameof(RequiresSetup));
        OnPropertyChanged(nameof(IsInitialized));
        OnPropertyChanged(nameof(CurrentStatus));
        OnPropertyChanged(nameof(ErrorMessage));
        OnPropertyChanged(nameof(HasAuthenticatedUser));
        OnPropertyChanged(nameof(AuthenticatedUserEmail));
        OnPropertyChanged(nameof(AuthenticatedUserDisplayName));
        OnPropertyChanged(nameof(ShowSetupButton));
        OnPropertyChanged(nameof(ShowStatusDetails));
        OnPropertyChanged(nameof(IsValidating));
        OnPropertyChanged(nameof(CanTakeAction));
        OnPropertyChanged(nameof(ActionButtonText));
        OnPropertyChanged(nameof(StatusDisplayText));
    }

    /// <summary>
    /// Update setup state during configuration flows
    /// </summary>
    public void UpdateSetupState(ProviderSetupState newSetupState)
    {
        if (newSetupState == null)
            return;

        SetupState = newSetupState;

        // Update UI state based on setup progress
        IsLoading = newSetupState.IsInProgress;
        CanConfigureProvider = !newSetupState.IsInProgress;

        // Update status message based on setup step
        StatusMessage = GetStatusMessageForSetupStep(newSetupState.CurrentStep);

        OnPropertyChanged(nameof(StatusDisplayText));
    }

    /// <summary>
    /// Main action command - setup or configuration
    /// </summary>
    [RelayCommand]
    private async Task HandleActionAsync()
    {
        if (!CanConfigureProvider || IsLoading || CurrentStatus == "Validating")
            return;

        IsLoading = true;
        StatusMessage = "Preparing...";

        try
        {
            if (CurrentStatus == "Authentication Required")
            {
                StatusMessage = "Initiating authentication...";
                AuthenticationRequested?.Invoke(this, ProviderName);

                // For authentication, we can immediately trigger a refresh instead of the delay
                await Task.Delay(500); // Brief pause for UI feedback
                RefreshRequested?.Invoke(this, ProviderName);

                IsLoading = false;
                StatusMessage = "Authentication started";
            }
            else if (RequiresSetup)
            {
                StatusMessage = "Starting setup...";
                SetupRequested?.Invoke(this, ProviderName);

                // Reset loading state after a reasonable delay since we're not implementing
                // actual dialogs yet - this prevents the button from staying stuck
                await Task.Delay(2000);
                IsLoading = false;
                StatusMessage = "Setup requested";
            }
            else
            {
                StatusMessage = "Opening configuration...";
                ConfigurationRequested?.Invoke(this, ProviderName);

                await Task.Delay(2000);
                IsLoading = false;
                StatusMessage = "Configuration requested";
            }
        }
        catch (Exception ex)
        {
            StatusMessage = $"Error: {ex.Message}";
            IsLoading = false;
        }
    }

    /// <summary>
    /// Refresh provider status command
    /// </summary>
    [RelayCommand]
    private async Task RefreshStatusAsync()
    {
        if (IsLoading)
            return;

        IsLoading = true;
        StatusMessage = "Refreshing...";

        try
        {
            RefreshRequested?.Invoke(this, ProviderName);

            // Give visual feedback for the refresh
            await Task.Delay(500);
        }
        catch (Exception ex)
        {
            StatusMessage = $"Refresh failed: {ex.Message}";
        }
        finally
        {
            IsLoading = false;

            // Clear status message after delay if no errors
            _ = Task.Delay(2000).ContinueWith(_ =>
            {
                if (StatusMessage.StartsWith("Refresh", StringComparison.OrdinalIgnoreCase))
                {
                    StatusMessage = string.Empty;
                    OnPropertyChanged(nameof(StatusDisplayText));
                }
            });
        }
    }

    /// <summary>
    /// Reset provider to force reconfiguration
    /// </summary>
    [RelayCommand]
    private async Task ResetProviderAsync()
    {
        await Task.CompletedTask;

        if (IsLoading)
            return;

        IsLoading = true;
        StatusMessage = "Resetting provider...";

        try
        {
            // Reset setup state
            SetupState = new ProviderSetupState
            {
                ProviderName = ProviderName,
                CurrentStep = SetupStep.NotStarted
            };

            // Reset status
            var resetStatus = Status with
            {
                IsHealthy = false,
                IsInitialized = false,
                RequiresSetup = true,
                Status = "Reset - Setup Required",
                ErrorMessage = null,
                LastCheck = DateTime.UtcNow
            };

            UpdateFromProviderStatus(resetStatus);

            StatusMessage = "Provider reset - setup required";
        }
        catch (Exception ex)
        {
            StatusMessage = $"Reset failed: {ex.Message}";
        }
        finally
        {
            IsLoading = false;
        }
    }

    /// <summary>
    /// Update last refresh time display
    /// </summary>
    private void UpdateLastRefreshTime()
    {
        if (Status.LastCheck == DateTime.MinValue)
        {
            LastRefreshTime = "Never";
        }
        else
        {
            var timeAgo = DateTime.UtcNow - Status.LastCheck;
            LastRefreshTime = timeAgo.TotalMinutes < 1
                ? "Just now"
                : timeAgo.TotalHours < 1
                    ? $"{(int)timeAgo.TotalMinutes} minutes ago"
                    : $"{(int)timeAgo.TotalHours} hours ago";
        }
    }

    /// <summary>
    /// Get status message for current setup step
    /// </summary>
    private static string GetStatusMessageForSetupStep(SetupStep step)
    {
        return step switch
        {
            SetupStep.Preparing => "Preparing setup...",
            SetupStep.GatheringInput => "Gathering configuration...",
            SetupStep.Authenticating => "Authenticating...",
            SetupStep.Configuring => "Configuring provider...",
            SetupStep.Testing => "Testing connection...",
            SetupStep.Finalizing => "Finalizing setup...",
            SetupStep.Completed => "Setup completed!",
            SetupStep.Failed => "Setup failed",
            SetupStep.Cancelled => "Setup cancelled",
            _ => string.Empty
        };
    }

    /// <summary>
    /// Force refresh of computed properties when needed
    /// </summary>
    public void RefreshComputedProperties()
    {
        OnPropertyChanged(nameof(IsHealthy));
        OnPropertyChanged(nameof(RequiresSetup));
        OnPropertyChanged(nameof(IsInitialized));
        OnPropertyChanged(nameof(CurrentStatus));
        OnPropertyChanged(nameof(ErrorMessage));
        OnPropertyChanged(nameof(HasAuthenticatedUser));
        OnPropertyChanged(nameof(AuthenticatedUserEmail));
        OnPropertyChanged(nameof(AuthenticatedUserDisplayName));
        OnPropertyChanged(nameof(ShowSetupButton));
        OnPropertyChanged(nameof(ShowStatusDetails));
        OnPropertyChanged(nameof(IsValidating));
        OnPropertyChanged(nameof(CanTakeAction));
        OnPropertyChanged(nameof(ActionButtonText));
        OnPropertyChanged(nameof(StatusDisplayText));
        OnPropertyChanged(nameof(SetupComplexity));
        OnPropertyChanged(nameof(EstimatedTime));
    }

    /// <summary>
    /// Set temporary status message with auto-clear
    /// </summary>
    public void SetTemporaryStatusMessage(string message, TimeSpan? clearAfter = null)
    {
        StatusMessage = message;
        OnPropertyChanged(nameof(StatusDisplayText));

        var delay = clearAfter ?? TimeSpan.FromSeconds(3);
        _ = Task.Delay(delay).ContinueWith(_ =>
        {
            if (StatusMessage == message) // Only clear if message hasn't changed
            {
                StatusMessage = string.Empty;
                OnPropertyChanged(nameof(StatusDisplayText));
            }
        });
    }
}