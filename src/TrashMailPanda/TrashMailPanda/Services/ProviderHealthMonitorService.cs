using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using System.Threading;

namespace TrashMailPanda.Services;

/// <summary>
/// Background service for continuous provider health monitoring
/// Monitors provider health and updates status in real-time
/// </summary>
public class ProviderHealthMonitorService : BackgroundService
{
    private readonly IProviderBridgeService _providerBridgeService;
    private readonly IProviderStatusService _providerStatusService;
    private readonly ILogger<ProviderHealthMonitorService> _logger;

    // Health check intervals
    private static readonly TimeSpan HealthCheckInterval = TimeSpan.FromMinutes(2);
    private static readonly TimeSpan InitialDelay = TimeSpan.FromSeconds(5);

    private readonly object _monitoringLock = new();
    private bool _isFirstRun = true;
    private DateTime _lastHealthCheck = DateTime.MinValue;

    public ProviderHealthMonitorService(
        IProviderBridgeService providerBridgeService,
        IProviderStatusService providerStatusService,
        ILogger<ProviderHealthMonitorService> logger)
    {
        _providerBridgeService = providerBridgeService ?? throw new ArgumentNullException(nameof(providerBridgeService));
        _providerStatusService = providerStatusService ?? throw new ArgumentNullException(nameof(providerStatusService));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    /// <summary>
    /// Background service execution loop
    /// </summary>
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation("Provider health monitoring service started");

        try
        {
            // Initial delay to allow application startup to complete
            await Task.Delay(InitialDelay, stoppingToken);

            // Perform initial health check immediately
            await PerformHealthCheckCycleAsync(isInitialCheck: true);

            // Start monitoring loop with health check intervals
            using var timer = new PeriodicTimer(HealthCheckInterval);

            while (await timer.WaitForNextTickAsync(stoppingToken))
            {
                await PerformHealthCheckCycleAsync(isInitialCheck: false);
            }
        }
        catch (OperationCanceledException)
        {
            _logger.LogInformation("Provider health monitoring service cancelled");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Provider health monitoring service encountered an error");
        }
    }

    /// <summary>
    /// Perform a complete health check cycle for all providers
    /// </summary>
    private async Task PerformHealthCheckCycleAsync(bool isInitialCheck)
    {
        try
        {
            var now = DateTime.UtcNow;

            _logger.LogDebug("Performing full provider health check cycle");

            // Get updated status for all providers
            var providerStatuses = await _providerBridgeService.GetAllProviderStatusAsync();

            // Update the provider status service with new information
            await UpdateProviderStatusesAsync(providerStatuses);

            lock (_monitoringLock)
            {
                _lastHealthCheck = now;
                _isFirstRun = false;
            }

            _logger.LogDebug("Completed provider health check cycle - checked {Count} providers",
                providerStatuses.Count);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Exception during provider health check cycle");
        }
    }

    /// <summary>
    /// Update provider status service with new status information
    /// </summary>
    private async Task UpdateProviderStatusesAsync(Dictionary<string, ProviderStatus> providerStatuses)
    {
        var updateTasks = providerStatuses.Select(async kvp =>
        {
            try
            {
                var providerName = kvp.Key;
                var newStatus = kvp.Value;

                // Get current status to compare
                var currentStatus = await _providerStatusService.GetProviderStatusAsync(providerName);

                // Check if status has changed significantly
                if (HasStatusChanged(currentStatus, newStatus))
                {
                    _logger.LogInformation("Provider {Provider} status changed: {OldStatus} → {NewStatus} (Healthy: {IsHealthy})",
                        providerName,
                        currentStatus?.Status ?? "Unknown",
                        newStatus.Status,
                        newStatus.IsHealthy);

                    // Trigger the provider status service to update and fire events
                    await _providerStatusService.RefreshProviderStatusAsync();
                }
                else
                {
                    _logger.LogTrace("Provider {Provider} status unchanged: {Status}", providerName, newStatus.Status);
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Exception updating status for provider {Provider}", kvp.Key);
            }
        });

        await Task.WhenAll(updateTasks);
    }

    /// <summary>
    /// Determine if provider status has changed significantly
    /// </summary>
    private static bool HasStatusChanged(ProviderStatus? currentStatus, ProviderStatus newStatus)
    {
        if (currentStatus == null)
        {
            return true; // First status check
        }

        // Check significant status changes
        return currentStatus.IsHealthy != newStatus.IsHealthy ||
               currentStatus.IsInitialized != newStatus.IsInitialized ||
               currentStatus.RequiresSetup != newStatus.RequiresSetup ||
               currentStatus.Status != newStatus.Status ||
               !string.Equals(currentStatus.ErrorMessage, newStatus.ErrorMessage, StringComparison.Ordinal);
    }

    /// <summary>
    /// Manually trigger a health check cycle (for testing or on-demand checks)
    /// </summary>
    public async Task TriggerHealthCheckAsync()
    {
        _logger.LogInformation("Manual health check triggered");
        await PerformHealthCheckCycleAsync(isInitialCheck: true);
    }

    /// <summary>
    /// Get health monitoring statistics
    /// </summary>
    public HealthMonitoringStats GetMonitoringStats()
    {
        lock (_monitoringLock)
        {
            return new HealthMonitoringStats
            {
                IsRunning = !_isFirstRun,
                LastHealthCheck = _lastHealthCheck,
                NextScheduledCheck = _lastHealthCheck.Add(HealthCheckInterval),
                HealthCheckInterval = HealthCheckInterval,
                QuickCheckInterval = HealthCheckInterval // Now using same interval for both
            };
        }
    }

    public override async Task StopAsync(CancellationToken cancellationToken)
    {
        _logger.LogInformation("Stopping provider health monitoring service");
        await base.StopAsync(cancellationToken);
        _logger.LogInformation("Provider health monitoring service stopped");
    }
}

/// <summary>
/// Health monitoring statistics for diagnostics
/// </summary>
public record HealthMonitoringStats
{
    /// <summary>
    /// Whether the monitoring service is currently running
    /// </summary>
    public bool IsRunning { get; init; }

    /// <summary>
    /// Timestamp of the last health check
    /// </summary>
    public DateTime LastHealthCheck { get; init; }

    /// <summary>
    /// Timestamp of the next scheduled health check
    /// </summary>
    public DateTime NextScheduledCheck { get; init; }

    /// <summary>
    /// Interval between full health checks
    /// </summary>
    public TimeSpan HealthCheckInterval { get; init; }

    /// <summary>
    /// Interval between quick status refreshes
    /// </summary>
    public TimeSpan QuickCheckInterval { get; init; }

    /// <summary>
    /// Time until next health check
    /// </summary>
    public TimeSpan TimeUntilNextCheck => NextScheduledCheck > DateTime.UtcNow
        ? NextScheduledCheck - DateTime.UtcNow
        : TimeSpan.Zero;
}

/// <summary>
/// Interface for provider health monitoring service
/// </summary>
public interface IProviderHealthMonitorService
{
    /// <summary>
    /// Manually trigger a health check cycle
    /// </summary>
    Task TriggerHealthCheckAsync();

    /// <summary>
    /// Get health monitoring statistics
    /// </summary>
    HealthMonitoringStats GetMonitoringStats();
}