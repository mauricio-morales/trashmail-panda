using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using System.Collections.ObjectModel;
using Microsoft.Extensions.Logging;
using TrashMailPanda.Models;
using TrashMailPanda.Services;
using TrashMailPanda.Shared;

namespace TrashMailPanda.ViewModels;

/// <summary>
/// Main dashboard ViewModel that coordinates all provider status cards
/// Handles real-time updates and dashboard gate functionality
/// </summary>
public partial class ProviderStatusDashboardViewModel : ViewModelBase
{
    private readonly IProviderBridgeService _providerBridgeService;
    private readonly IProviderStatusService _providerStatusService;
    private readonly ILogger<ProviderStatusDashboardViewModel> _logger;

    // Provider cards collection
    [ObservableProperty]
    private ObservableCollection<ProviderStatusCardViewModel> _providerCards = new();

    // Overall dashboard state
    [ObservableProperty]
    private bool _isLoading = true;

    [ObservableProperty]
    private bool _canAccessMainDashboard = false;

    [ObservableProperty]
    private string _overallStatus = "Checking providers...";

    [ObservableProperty]
    private int _healthyProviderCount = 0;

    [ObservableProperty]
    private int _totalProviderCount = 0;

    [ObservableProperty]
    private DateTime _lastRefresh = DateTime.MinValue;

    [ObservableProperty]
    private bool _hasErrors = false;

    [ObservableProperty]
    private string _errorSummary = string.Empty;

    // UI state
    [ObservableProperty]
    private bool _isRefreshing = false;

    [ObservableProperty]
    private bool _showDetailedStatus = true;

    [ObservableProperty]
    private string _refreshStatusMessage = string.Empty;

    // Computed properties
    public string HealthyProvidersText => $"{HealthyProviderCount} of {TotalProviderCount} providers healthy";

    public string OverallHealthStatus => CanAccessMainDashboard
        ? "All systems operational"
        : HasErrors
            ? "Issues detected"
            : "Setup required";

    public string LastRefreshText => LastRefresh == DateTime.MinValue
        ? "Never updated"
        : $"Last updated: {FormatTimeAgo(DateTime.UtcNow - LastRefresh)}";

    public bool AllProvidersHealthy => TotalProviderCount > 0 && HealthyProviderCount == TotalProviderCount;

    // Events
    public event EventHandler? DashboardAccessRequested;
    public event EventHandler<string>? ProviderSetupRequested;
    public event EventHandler<string>? ProviderConfigurationRequested;
    public event EventHandler<string>? ProviderAuthenticationRequested;

    public ProviderStatusDashboardViewModel(
        IProviderBridgeService providerBridgeService,
        IProviderStatusService providerStatusService,
        ILogger<ProviderStatusDashboardViewModel> logger)
    {
        _providerBridgeService = providerBridgeService ?? throw new ArgumentNullException(nameof(providerBridgeService));
        _providerStatusService = providerStatusService ?? throw new ArgumentNullException(nameof(providerStatusService));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));

        // Subscribe to provider status changes
        _providerStatusService.ProviderStatusChanged += OnProviderStatusChanged;

        // Initialize provider cards
        InitializeProviderCards();

        // Start initial status check asynchronously but don't fire and forget
        // Use Task.Run to ensure tests can complete constructor before async initialization changes state
        _ = Task.Run(InitializeAsync);
    }

    /// <summary>
    /// Initialize async components - called from constructor
    /// </summary>
    private async void InitializeAsync()
    {
        try
        {
            await RefreshAllProvidersAsync();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Exception during async initialization");
            HasErrors = true;
            OverallStatus = "Failed to initialize";
        }
    }

    /// <summary>
    /// Initialize provider cards from bridge service information
    /// </summary>
    private void InitializeProviderCards()
    {
        try
        {
            var providerDisplayInfo = _providerBridgeService.GetProviderDisplayInfo();

            ProviderCards.Clear();

            foreach (var providerInfo in providerDisplayInfo.Values.OrderBy(p => p.Type))
            {
                var cardViewModel = new ProviderStatusCardViewModel(providerInfo, _logger as ILogger<ProviderStatusCardViewModel>);

                // Enhanced card for GoogleServices with sub-service indicators
                // Note: Enhanced sub-service functionality can be added when ProviderStatusCardViewModel supports metadata

                // Subscribe to card events
                cardViewModel.SetupRequested += OnProviderSetupRequested;
                cardViewModel.ConfigurationRequested += OnProviderConfigurationRequested;
                cardViewModel.AuthenticationRequested += OnProviderAuthenticationRequested;
                cardViewModel.RefreshRequested += OnProviderRefreshRequested;

                ProviderCards.Add(cardViewModel);
            }

            TotalProviderCount = ProviderCards.Count;

            _logger.LogInformation("Initialized {Count} provider cards", TotalProviderCount);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Exception initializing provider cards");
            OverallStatus = "Failed to initialize providers";
            HasErrors = true;
        }
    }

    /// <summary>
    /// Handle provider status change events from the status service
    /// </summary>
    private async void OnProviderStatusChanged(object? sender, ProviderStatusChangedEventArgs e)
    {
        try
        {
            _logger.LogInformation("[UI EVENT] Received status change for provider {Provider}: {Status} (Healthy: {IsHealthy}, RequiresSetup: {RequiresSetup}, Initialized: {IsInitialized})",
                e.ProviderName, e.Status?.Status, e.Status?.IsHealthy, e.Status?.RequiresSetup, e.Status?.IsInitialized);

            // Find the corresponding card and update it
            var card = ProviderCards.FirstOrDefault(c => c.ProviderName == e.ProviderName);
            if (card != null && e.Status != null)
            {
                _logger.LogInformation("[UI UPDATE] Updating card for provider {Provider} - before: Status={OldStatus}, Healthy={OldHealthy}",
                    e.ProviderName, card.CurrentStatus, card.IsHealthy);

                card.UpdateFromProviderStatus(e.Status);

                _logger.LogInformation("[UI UPDATE] Updated card for provider {Provider} - after: Status={NewStatus}, Healthy={NewHealthy}",
                    e.ProviderName, card.CurrentStatus, card.IsHealthy);
            }
            else
            {
                _logger.LogWarning("[UI ERROR] No card found for provider {Provider} or status is null. Total cards: {CardCount}",
                    e.ProviderName, ProviderCards.Count);
            }

            // Update overall dashboard state
            await UpdateOverallDashboardStateAsync();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Exception handling provider status change for {Provider}", e.ProviderName);
        }
    }

    /// <summary>
    /// Refresh all provider statuses
    /// </summary>
    [RelayCommand]
    private async Task RefreshAllProvidersAsync()
    {
        // If already refreshing, simply return to avoid concurrent refreshes
        if (IsRefreshing)
        {
            _logger.LogDebug("RefreshAllProvidersAsync called while already refreshing, skipping");
            return;
        }

        IsRefreshing = true;
        IsLoading = true;
        RefreshStatusMessage = "Refreshing all providers...";

        try
        {
            _logger.LogInformation("Starting manual refresh of all providers");

            // Get updated status from all providers
            var allStatuses = await _providerBridgeService.GetAllProviderStatusAsync();

            // Update each card with new status
            foreach (var statusPair in allStatuses)
            {
                var providerName = statusPair.Key;
                var status = statusPair.Value;

                var card = ProviderCards.FirstOrDefault(c => c.ProviderName == providerName);
                if (card != null)
                {
                    card.UpdateFromProviderStatus(status);
                    RefreshStatusMessage = $"Updated {providerName}...";

                    // Small delay to show progress (reduced for faster tests)
                    await Task.Delay(10);
                }
            }

            LastRefresh = DateTime.UtcNow;
            RefreshStatusMessage = "Refresh completed";

            // Update overall state
            await UpdateOverallDashboardStateAsync();

            _logger.LogInformation("Completed manual refresh of all providers");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Exception during provider refresh");
            RefreshStatusMessage = $"Refresh failed: {ex.Message}";
            HasErrors = true;
        }
        finally
        {
            IsLoading = false;
            IsRefreshing = false;

            // Clear status message after delay (reduced for faster tests)
            _ = Task.Delay(500).ContinueWith(_ =>
            {
                RefreshStatusMessage = string.Empty;
                OnPropertyChanged(nameof(RefreshStatusMessage));
            });
        }
    }

    /// <summary>
    /// Update overall dashboard state based on provider statuses
    /// </summary>
    private async Task UpdateOverallDashboardStateAsync()
    {
        await Task.CompletedTask;

        try
        {
            // Count healthy providers
            var healthyCount = ProviderCards.Count(c => c.IsHealthy);
            var totalCount = ProviderCards.Count;

            HealthyProviderCount = healthyCount;
            TotalProviderCount = totalCount;

            // Check if all providers are healthy and initialized
            var allHealthy = ProviderCards.All(c => c.IsHealthy && c.IsInitialized);
            var hasRequiredSetup = ProviderCards.Any(c => c.RequiresSetup && c.DisplayInfo.IsRequired);
            var hasErrors = ProviderCards.Any(c => !string.IsNullOrEmpty(c.ErrorMessage));

            CanAccessMainDashboard = allHealthy && !hasRequiredSetup;
            HasErrors = hasErrors || healthyCount == 0;

            // Update status messages
            if (CanAccessMainDashboard)
            {
                OverallStatus = "✅ All systems ready - you can access the main dashboard";
            }
            else if (hasRequiredSetup)
            {
                var setupCount = ProviderCards.Count(c => c.RequiresSetup);
                OverallStatus = $"⚙️ {setupCount} provider{(setupCount > 1 ? "s" : "")} require setup";
            }
            else if (hasErrors)
            {
                var errorCount = ProviderCards.Count(c => !string.IsNullOrEmpty(c.ErrorMessage));
                OverallStatus = $"❌ {errorCount} provider{(errorCount > 1 ? "s" : "")} have errors";
            }
            else
            {
                OverallStatus = "🔄 Checking provider status...";
            }

            // Generate error summary
            var errorMessages = ProviderCards
                .Where(c => !string.IsNullOrEmpty(c.ErrorMessage))
                .Select(c => $"{c.ProviderDisplayName}: {c.ErrorMessage}")
                .ToList();

            ErrorSummary = errorMessages.Any()
                ? string.Join(Environment.NewLine, errorMessages)
                : string.Empty;

            // Update computed properties
            OnPropertyChanged(nameof(HealthyProvidersText));
            OnPropertyChanged(nameof(OverallHealthStatus));
            OnPropertyChanged(nameof(LastRefreshText));
            OnPropertyChanged(nameof(AllProvidersHealthy));

            _logger.LogDebug("Dashboard state updated: {HealthyCount}/{TotalCount} healthy, CanAccess={CanAccess}",
                healthyCount, totalCount, CanAccessMainDashboard);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Exception updating overall dashboard state");
        }
    }

    /// <summary>
    /// Navigate to main dashboard (if allowed)
    /// </summary>
    [RelayCommand]
    private async Task AccessMainDashboardAsync()
    {
        await Task.CompletedTask;

        if (!CanAccessMainDashboard)
        {
            _logger.LogWarning("Attempted to access main dashboard but providers not ready");
            return;
        }

        try
        {
            _logger.LogInformation("Accessing main dashboard - all providers healthy");
            DashboardAccessRequested?.Invoke(this, EventArgs.Empty);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Exception accessing main dashboard");
        }
    }

    /// <summary>
    /// Handle provider setup requests from cards
    /// </summary>
    private void OnProviderSetupRequested(object? sender, string providerName)
    {
        _logger.LogInformation("Setup requested for provider {Provider}", providerName);
        ProviderSetupRequested?.Invoke(this, providerName);
    }

    /// <summary>
    /// Handle provider configuration requests from cards
    /// </summary>
    private void OnProviderConfigurationRequested(object? sender, string providerName)
    {
        _logger.LogInformation("Configuration requested for provider {Provider}", providerName);
        ProviderConfigurationRequested?.Invoke(this, providerName);
    }

    /// <summary>
    /// Handle provider authentication requests from cards
    /// </summary>
    private void OnProviderAuthenticationRequested(object? sender, string providerName)
    {
        _logger.LogInformation("Authentication requested for provider {Provider}", providerName);
        ProviderAuthenticationRequested?.Invoke(this, providerName);
    }

    /// <summary>
    /// Handle provider refresh requests from cards
    /// </summary>
    private async void OnProviderRefreshRequested(object? sender, string providerName)
    {
        try
        {
            _logger.LogInformation("Refresh requested for provider {Provider}", providerName);

            // Refresh specific provider
            await _providerStatusService.RefreshProviderStatusAsync();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Exception refreshing provider {Provider}", providerName);
        }
    }

    /// <summary>
    /// Toggle detailed status view
    /// </summary>
    [RelayCommand]
    private void ToggleDetailedStatus()
    {
        ShowDetailedStatus = !ShowDetailedStatus;
    }

    /// <summary>
    /// Setup all required providers that need configuration
    /// </summary>
    [RelayCommand]
    private async Task SetupAllRequiredProvidersAsync()
    {
        try
        {
            var requiresSetup = ProviderCards
                .Where(c => c.RequiresSetup && c.DisplayInfo.IsRequired)
                .ToList();

            if (!requiresSetup.Any())
            {
                return;
            }

            _logger.LogInformation("Setting up {Count} required providers", requiresSetup.Count);

            foreach (var card in requiresSetup)
            {
                ProviderSetupRequested?.Invoke(this, card.ProviderName);

                // Small delay between setup requests (reduced for faster tests)
                await Task.Delay(50);
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Exception setting up required providers");
        }
    }

    /// <summary>
    /// Format time ago for display
    /// </summary>
    private static string FormatTimeAgo(TimeSpan timeAgo)
    {
        return timeAgo.TotalMinutes < 1
            ? "just now"
            : timeAgo.TotalHours < 1
                ? $"{(int)timeAgo.TotalMinutes} minutes ago"
                : timeAgo.TotalDays < 1
                    ? $"{(int)timeAgo.TotalHours} hours ago"
                    : $"{(int)timeAgo.TotalDays} days ago";
    }

    /// <summary>
    /// Cleanup when view model is no longer needed
    /// </summary>
    public void Cleanup()
    {
        // Unsubscribe from events
        _providerStatusService.ProviderStatusChanged -= OnProviderStatusChanged;

        // Dispose provider cards
        foreach (var card in ProviderCards)
        {
            card.SetupRequested -= OnProviderSetupRequested;
            card.ConfigurationRequested -= OnProviderConfigurationRequested;
            card.AuthenticationRequested -= OnProviderAuthenticationRequested;
            card.RefreshRequested -= OnProviderRefreshRequested;
        }
    }
}