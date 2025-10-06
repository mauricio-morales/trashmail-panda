using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using TrashMailPanda.Shared.Models;

namespace TrashMailPanda.Shared.Base;

/// <summary>
/// Abstract base implementation for all providers in the TrashMail Panda system
/// Provides common functionality including lifecycle management, state tracking, metrics collection, and error handling
/// </summary>
/// <typeparam name="TConfig">The configuration type for this provider</typeparam>
public abstract class BaseProvider<TConfig> : IProvider<TConfig>, IDisposable
    where TConfig : BaseProviderConfig
{
    private readonly ILogger _logger;
    private readonly object _stateLock = new();
    private readonly ConcurrentDictionary<string, double> _metrics = new();
    private readonly ConcurrentDictionary<string, object> _metadata = new();
    private readonly List<ProviderError> _recentErrors = new();
    private readonly List<PerformanceDataPoint> _performanceHistory = new();
    private readonly SemaphoreSlim _operationSemaphore;

    private ProviderStateInfo _stateInfo = new();
    private TConfig? _configuration;
    private DateTime _startTime = DateTime.UtcNow;
    private ProviderStatistics _statistics = new();
    private bool _disposed;
    private CancellationTokenSource? _suspensionCancellation;

    /// <summary>
    /// Initializes a new instance of the BaseProvider class
    /// </summary>
    /// <param name="logger">Logger for this provider</param>
    /// <param name="maxConcurrentOperations">Maximum number of concurrent operations (default: 10)</param>
    protected BaseProvider(ILogger logger, int maxConcurrentOperations = 10)
    {
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _operationSemaphore = new SemaphoreSlim(maxConcurrentOperations, maxConcurrentOperations);

        // Initialize metadata
        _metadata[nameof(Version)] = Version;
        _metadata["StartTime"] = _startTime;
        _metadata["MaxConcurrentOperations"] = maxConcurrentOperations;
    }

    /// <summary>
    /// Gets the unique name identifier for this provider
    /// </summary>
    public abstract string Name { get; }

    /// <summary>
    /// Gets the version of this provider implementation
    /// </summary>
    public abstract string Version { get; }

    /// <summary>
    /// Gets the current state of the provider
    /// </summary>
    public ProviderState State
    {
        get
        {
            lock (_stateLock)
            {
                return _stateInfo.State;
            }
        }
    }

    /// <summary>
    /// Gets detailed information about the current provider state
    /// </summary>
    public ProviderStateInfo StateInfo
    {
        get
        {
            lock (_stateLock)
            {
                return _stateInfo;
            }
        }
    }

    /// <summary>
    /// Gets the current configuration for this provider
    /// </summary>
    public TConfig? Configuration
    {
        get
        {
            lock (_stateLock)
            {
                return _configuration;
            }
        }
    }

    /// <summary>
    /// Gets a value indicating whether the provider is currently healthy and can accept operations
    /// </summary>
    public bool IsHealthy => State is ProviderState.Ready or ProviderState.Busy && _stateInfo.ConsecutiveFailures < 3;

    /// <summary>
    /// Gets a value indicating whether the provider can accept new operations
    /// </summary>
    public bool CanAcceptOperations => _stateInfo.CanAcceptOperations && !_disposed;

    /// <summary>
    /// Gets the logger instance for this provider
    /// </summary>
    protected ILogger Logger => _logger;

    /// <summary>
    /// Gets the timestamp of the last successful operation
    /// </summary>
    public DateTime? LastSuccessfulOperation => _stateInfo.LastSuccessfulOperation;

    /// <summary>
    /// Gets metrics and performance data for this provider
    /// </summary>
    public IReadOnlyDictionary<string, double> Metrics => _metrics.ToDictionary(kvp => kvp.Key, kvp => kvp.Value);

    /// <summary>
    /// Gets additional metadata about this provider
    /// </summary>
    public IReadOnlyDictionary<string, object> Metadata => _metadata.ToDictionary(kvp => kvp.Key, kvp => kvp.Value);

    /// <summary>
    /// Event fired when the provider state changes
    /// </summary>
    public event EventHandler<ProviderStateChangedEventArgs>? StateChanged;

    /// <summary>
    /// Event fired when a provider operation completes (success or failure)
    /// </summary>
    public event EventHandler<ProviderOperationEventArgs>? OperationCompleted;

    /// <summary>
    /// Event fired when provider metrics are updated
    /// </summary>
    public event EventHandler<ProviderMetricsEventArgs>? MetricsUpdated;

    /// <summary>
    /// Initializes the provider with the specified configuration
    /// </summary>
    /// <param name="config">The configuration to use for initialization</param>
    /// <param name="cancellationToken">Cancellation token for the operation</param>
    /// <returns>A result indicating whether initialization was successful</returns>
    public async Task<Result<bool>> InitializeAsync(TConfig config, CancellationToken cancellationToken = default)
    {
        if (_disposed)
            return Result<bool>.Failure(new InvalidOperationError("Provider has been disposed"));

        var stopwatch = System.Diagnostics.Stopwatch.StartNew();
        var operationName = "Initialize";

        try
        {
            // Validate state transition
            var transitionResult = ValidateStateTransition(State, ProviderState.Initializing);
            if (transitionResult.IsFailure)
                return Result<bool>.Failure(transitionResult.Error);

            // Set initializing state
            UpdateState(ProviderState.Initializing, "Provider initialization started");

            _logger.LogInformation("Initializing provider {ProviderName} (Version: {Version})", Name, Version);

            // Validate configuration
            _logger.LogDebug("Validating configuration for provider {ProviderName}", Name);
            var validationResult = await ValidateConfigurationAsync(config, cancellationToken);
            if (validationResult.IsFailure)
            {
                var error = new ConfigurationError($"Configuration validation failed: {validationResult.Error.Message}");
                UpdateStateWithError(error, "Configuration validation failed");
                RecordOperationCompleted(operationName, false, stopwatch.Elapsed, error);
                return Result<bool>.Failure(error);
            }

            // Store configuration
            lock (_stateLock)
            {
                _configuration = config;
            }

            // Perform provider-specific initialization
            _logger.LogDebug("Performing provider-specific initialization for {ProviderName}", Name);
            var initResult = await PerformInitializationAsync(config, cancellationToken);
            if (initResult.IsFailure)
            {
                UpdateStateWithError(initResult.Error, "Provider-specific initialization failed");
                RecordOperationCompleted(operationName, false, stopwatch.Elapsed, initResult.Error);
                return initResult;
            }

            // Update state to ready
            UpdateState(ProviderState.Ready, "Provider initialization completed successfully");
            RecordSuccessfulOperation("Provider initialized successfully");
            RecordOperationCompleted(operationName, true, stopwatch.Elapsed);

            _logger.LogInformation("Provider {ProviderName} initialized successfully in {Duration}ms",
                Name, stopwatch.ElapsedMilliseconds);

            return Result<bool>.Success(true);
        }
        catch (Exception ex)
        {
            var error = ex.ToProviderError($"Unexpected error during provider initialization: {ex.Message}");
            UpdateStateWithError(error, "Unexpected initialization error");
            RecordOperationCompleted(operationName, false, stopwatch.Elapsed, error);
            _logger.LogError(ex, "Unexpected error initializing provider {ProviderName}", Name);
            return Result<bool>.Failure(error);
        }
        finally
        {
            stopwatch.Stop();
        }
    }

    /// <summary>
    /// Shuts down the provider gracefully, cleaning up resources
    /// </summary>
    /// <param name="cancellationToken">Cancellation token for the operation</param>
    /// <returns>A result indicating whether shutdown was successful</returns>
    public async Task<Result<bool>> ShutdownAsync(CancellationToken cancellationToken = default)
    {
        if (_disposed)
            return Result<bool>.Success(true); // Already disposed

        var stopwatch = System.Diagnostics.Stopwatch.StartNew();
        var operationName = "Shutdown";

        try
        {
            // Special case: if provider is uninitialized, we can go directly to shutdown
            if (State == ProviderState.Uninitialized)
            {
                UpdateState(ProviderState.Shutdown, "Provider shutdown completed (was uninitialized)");
                RecordOperationCompleted(operationName, true, stopwatch.Elapsed);
                _logger.LogInformation("Provider {ProviderName} was uninitialized, shutdown completed immediately",
                    Name);
                return Result<bool>.Success(true);
            }

            // Validate state transition
            var transitionResult = ValidateStateTransition(State, ProviderState.ShuttingDown);
            if (transitionResult.IsFailure)
                return Result<bool>.Failure(transitionResult.Error);

            UpdateState(ProviderState.ShuttingDown, "Provider shutdown started");
            _logger.LogInformation("Shutting down provider {ProviderName}", Name);

            // Cancel any suspension
            _suspensionCancellation?.Cancel();

            // Wait for ongoing operations to complete with timeout
            var waitTimeout = TimeSpan.FromSeconds(30);
            var waitResult = await _operationSemaphore.WaitAsync(waitTimeout, cancellationToken);
            if (!waitResult)
            {
                _logger.LogWarning("Timeout waiting for operations to complete during shutdown of {ProviderName}", Name);
            }

            // Perform provider-specific shutdown
            var shutdownResult = await PerformShutdownAsync(cancellationToken);
            if (shutdownResult.IsFailure)
            {
                _logger.LogWarning("Provider-specific shutdown failed for {ProviderName}: {Error}", Name, shutdownResult.Error.Message);
            }

            // Update final state
            UpdateState(ProviderState.Shutdown, "Provider shutdown completed");
            RecordOperationCompleted(operationName, true, stopwatch.Elapsed);

            _logger.LogInformation("Provider {ProviderName} shut down successfully in {Duration}ms",
                Name, stopwatch.ElapsedMilliseconds);

            return Result<bool>.Success(true);
        }
        catch (Exception ex)
        {
            var error = ex.ToProviderError($"Unexpected error during provider shutdown: {ex.Message}");
            RecordOperationCompleted(operationName, false, stopwatch.Elapsed, error);
            _logger.LogError(ex, "Unexpected error shutting down provider {ProviderName}", Name);
            return Result<bool>.Failure(error);
        }
        finally
        {
            stopwatch.Stop();
        }
    }

    /// <summary>
    /// Validates the provided configuration without initializing the provider
    /// </summary>
    /// <param name="config">The configuration to validate</param>
    /// <param name="cancellationToken">Cancellation token for the operation</param>
    /// <returns>A result indicating whether the configuration is valid</returns>
    public virtual async Task<Result<bool>> ValidateConfigurationAsync(TConfig config, CancellationToken cancellationToken = default)
    {
        try
        {
            // Basic validation
            var basicResult = config.ValidateConfiguration();
            if (basicResult.IsFailure)
                return Result<bool>.Failure(basicResult.Error);

            // Provider-specific validation
            var customResult = await ValidateCustomConfigurationAsync(config, cancellationToken);
            if (customResult.IsFailure)
                return customResult;

            return Result<bool>.Success(true);
        }
        catch (Exception ex)
        {
            return Result<bool>.Failure(ex.ToProviderError("Configuration validation failed"));
        }
    }

    /// <summary>
    /// Performs a health check on the provider
    /// </summary>
    /// <param name="cancellationToken">Cancellation token for the operation</param>
    /// <returns>A result containing the health check results</returns>
    public async Task<Result<HealthCheckResult>> HealthCheckAsync(CancellationToken cancellationToken = default)
    {
        Logger.LogInformation("[BASE PROVIDER] HealthCheckAsync called for provider type: {ProviderType}", GetType().Name);
        var stopwatch = System.Diagnostics.Stopwatch.StartNew();

        try
        {
            if (_disposed)
            {
                return Result<HealthCheckResult>.Success(
                    HealthCheckResult.Critical("Provider has been disposed", duration: stopwatch.Elapsed));
            }

            if (State == ProviderState.Error)
            {
                var errorMessage = _stateInfo.Error?.Message ?? "Provider is in error state";
                return Result<HealthCheckResult>.Success(
                    HealthCheckResult.Critical($"Provider is in error state: {errorMessage}", duration: stopwatch.Elapsed));
            }

            if (State != ProviderState.Ready && State != ProviderState.Busy)
            {
                return Result<HealthCheckResult>.Success(
                    HealthCheckResult.Degraded($"Provider state is {State}", duration: stopwatch.Elapsed));
            }

            // Perform provider-specific health checks
            Logger.LogInformation("[BASE PROVIDER] About to call PerformHealthCheckAsync for {ProviderType}", GetType().Name);
            var customHealthResult = await PerformHealthCheckAsync(cancellationToken);
            stopwatch.Stop();

            if (customHealthResult.IsFailure)
            {
                return Result<HealthCheckResult>.Success(
                    HealthCheckResult.FromError(customHealthResult.Error, stopwatch.Elapsed));
            }

            var healthResult = customHealthResult.Value with { Duration = stopwatch.Elapsed };
            return Result<HealthCheckResult>.Success(healthResult);
        }
        catch (Exception ex)
        {
            stopwatch.Stop();
            var error = ex.ToProviderError("Health check failed");
            return Result<HealthCheckResult>.Success(
                HealthCheckResult.FromError(error, stopwatch.Elapsed));
        }
    }

    /// <summary>
    /// Updates the provider configuration at runtime
    /// </summary>
    /// <param name="config">The new configuration to apply</param>
    /// <param name="cancellationToken">Cancellation token for the operation</param>
    /// <returns>A result indicating whether the configuration update was successful</returns>
    public virtual async Task<Result<bool>> UpdateConfigurationAsync(TConfig config, CancellationToken cancellationToken = default)
    {
        try
        {
            // Validate the new configuration
            var validationResult = await ValidateConfigurationAsync(config, cancellationToken);
            if (validationResult.IsFailure)
                return validationResult;

            // Apply the configuration update
            var updateResult = await ApplyConfigurationUpdateAsync(config, cancellationToken);
            if (updateResult.IsFailure)
                return updateResult;

            // Store the new configuration
            lock (_stateLock)
            {
                _configuration = config;
            }

            _logger.LogInformation("Configuration updated successfully for provider {ProviderName}", Name);
            return Result<bool>.Success(true);
        }
        catch (Exception ex)
        {
            var error = ex.ToProviderError("Configuration update failed");
            _logger.LogError(ex, "Failed to update configuration for provider {ProviderName}", Name);
            return Result<bool>.Failure(error);
        }
    }

    /// <summary>
    /// Resets the provider to a clean state, typically after an error
    /// </summary>
    /// <param name="cancellationToken">Cancellation token for the operation</param>
    /// <returns>A result indicating whether the reset was successful</returns>
    public virtual async Task<Result<bool>> ResetAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            _logger.LogInformation("Resetting provider {ProviderName}", Name);

            // Clear error state
            lock (_stateLock)
            {
                _recentErrors.Clear();
                _statistics = _statistics with { ResetCount = _statistics.ResetCount + 1, StatisticsResetAt = DateTime.UtcNow };
            }

            // Perform provider-specific reset
            var resetResult = await PerformResetAsync(cancellationToken);
            if (resetResult.IsFailure)
                return resetResult;

            // Re-initialize if we have configuration
            if (_configuration != null)
            {
                return await InitializeAsync(_configuration, cancellationToken);
            }

            UpdateState(ProviderState.Uninitialized, "Provider reset completed");
            return Result<bool>.Success(true);
        }
        catch (Exception ex)
        {
            var error = ex.ToProviderError("Provider reset failed");
            _logger.LogError(ex, "Failed to reset provider {ProviderName}", Name);
            return Result<bool>.Failure(error);
        }
    }

    /// <summary>
    /// Suspends the provider temporarily (e.g., due to rate limiting)
    /// </summary>
    /// <param name="duration">Optional duration for the suspension</param>
    /// <param name="reason">Reason for the suspension</param>
    /// <param name="cancellationToken">Cancellation token for the operation</param>
    /// <returns>A result indicating whether the suspension was successful</returns>
    public Task<Result<bool>> SuspendAsync(TimeSpan? duration = null, string? reason = null, CancellationToken cancellationToken = default)
    {
        try
        {
            var suspensionReason = reason ?? "Provider suspended";
            UpdateState(ProviderState.Suspended, suspensionReason);

            if (duration.HasValue)
            {
                _suspensionCancellation?.Cancel();
                _suspensionCancellation = new CancellationTokenSource();

                _logger.LogInformation("Provider {ProviderName} suspended for {Duration}: {Reason}",
                    Name, duration.Value, suspensionReason);

                // Auto-resume after the specified duration
                _ = Task.Delay(duration.Value, _suspensionCancellation.Token)
                    .ContinueWith(async _ =>
                    {
                        if (!_suspensionCancellation.Token.IsCancellationRequested)
                        {
                            await ResumeAsync();
                        }
                    }, TaskScheduler.Default);
            }
            else
            {
                _logger.LogInformation("Provider {ProviderName} suspended indefinitely: {Reason}", Name, suspensionReason);
            }

            return Task.FromResult(Result<bool>.Success(true));
        }
        catch (Exception ex)
        {
            var error = ex.ToProviderError("Provider suspension failed");
            _logger.LogError(ex, "Failed to suspend provider {ProviderName}", Name);
            return Task.FromResult(Result<bool>.Failure(error));
        }
    }

    /// <summary>
    /// Resumes the provider from a suspended state
    /// </summary>
    /// <param name="cancellationToken">Cancellation token for the operation</param>
    /// <returns>A result indicating whether the resume was successful</returns>
    public Task<Result<bool>> ResumeAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            if (State != ProviderState.Suspended)
            {
                return Task.FromResult(Result<bool>.Failure(new InvalidOperationError("Provider is not in suspended state")));
            }

            _suspensionCancellation?.Cancel();
            _suspensionCancellation = null;

            UpdateState(ProviderState.Ready, "Provider resumed from suspension");
            _logger.LogInformation("Provider {ProviderName} resumed from suspension", Name);

            return Task.FromResult(Result<bool>.Success(true));
        }
        catch (Exception ex)
        {
            var error = ex.ToProviderError("Provider resume failed");
            _logger.LogError(ex, "Failed to resume provider {ProviderName}", Name);
            return Task.FromResult(Result<bool>.Failure(error));
        }
    }

    /// <summary>
    /// Gets comprehensive diagnostic information about the provider
    /// </summary>
    /// <param name="cancellationToken">Cancellation token for the operation</param>
    /// <returns>A result containing diagnostic information</returns>
    public Task<Result<ProviderDiagnostics>> GetDiagnosticsAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            var diagnostics = new ProviderDiagnostics
            {
                ProviderName = Name,
                Version = Version,
                StateInfo = StateInfo,
                Configuration = Configuration?.GetSanitizedCopy(),
                Metrics = Metrics.ToDictionary(kvp => kvp.Key, kvp => kvp.Value),
                Statistics = _statistics with { Uptime = DateTime.UtcNow - _startTime },
                RecentErrors = _recentErrors.TakeLast(10).ToList(),
                PerformanceHistory = _performanceHistory.TakeLast(100).ToList(),
                Details = Metadata.ToDictionary(kvp => kvp.Key, kvp => kvp.Value)
            };

            return Task.FromResult(Result<ProviderDiagnostics>.Success(diagnostics));
        }
        catch (Exception ex)
        {
            var error = ex.ToProviderError("Failed to collect diagnostics");
            return Task.FromResult(Result<ProviderDiagnostics>.Failure(error));
        }
    }

    /// <summary>
    /// Records a metric value for this provider
    /// </summary>
    /// <param name="name">The metric name</param>
    /// <param name="value">The metric value</param>
    /// <param name="tags">Optional tags for the metric</param>
    public void RecordMetric(string name, double value, Dictionary<string, string>? tags = null)
    {
        if (string.IsNullOrEmpty(name))
            return;

        var metricKey = tags?.Any() == true ? $"{name}[{string.Join(",", tags.Select(t => $"{t.Key}={t.Value}"))}]" : name;
        _metrics[metricKey] = value;

        OnMetricsUpdated(_metrics.ToDictionary(kvp => kvp.Key, kvp => kvp.Value));
    }

    /// <summary>
    /// Gets the current metrics snapshot
    /// </summary>
    /// <returns>A snapshot of current metrics</returns>
    public ProviderMetricsSnapshot GetMetricsSnapshot()
    {
        return new ProviderMetricsSnapshot
        {
            ProviderName = Name,
            Metrics = _metrics.ToDictionary(kvp => kvp.Key, kvp => kvp.Value),
            SnapshotAt = DateTime.UtcNow,
            TimeWindow = DateTime.UtcNow - _startTime,
            Metadata = new Dictionary<string, object>
            {
                { "TotalMetrics", _metrics.Count },
                { "ProviderVersion", Version },
                { "ProviderState", State.ToString() }
            }
        };
    }

    /// <summary>
    /// Clears all recorded metrics
    /// </summary>
    public void ClearMetrics()
    {
        _metrics.Clear();
        _performanceHistory.Clear();
    }

    /// <summary>
    /// Waits for the provider to reach a specific state
    /// </summary>
    /// <param name="targetState">The state to wait for</param>
    /// <param name="timeout">Maximum time to wait</param>
    /// <param name="cancellationToken">Cancellation token for the operation</param>
    /// <returns>A result indicating whether the state was reached within the timeout</returns>
    public async Task<Result<bool>> WaitForStateAsync(ProviderState targetState, TimeSpan timeout, CancellationToken cancellationToken = default)
    {
        var startTime = DateTime.UtcNow;
        var pollingInterval = TimeSpan.FromMilliseconds(100);

        while (DateTime.UtcNow - startTime < timeout && !cancellationToken.IsCancellationRequested)
        {
            if (State == targetState)
                return Result<bool>.Success(true);

            try
            {
                await Task.Delay(pollingInterval, cancellationToken);
            }
            catch (OperationCanceledException)
            {
                return Result<bool>.Failure(new TimeoutError("Operation was cancelled while waiting for state"));
            }
        }

        return Result<bool>.Failure(new TimeoutError($"Timeout waiting for provider to reach state {targetState}"));
    }

    /// <summary>
    /// Abstract method for provider-specific initialization logic
    /// </summary>
    /// <param name="config">The configuration to use</param>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>A result indicating success or failure</returns>
    protected abstract Task<Result<bool>> PerformInitializationAsync(TConfig config, CancellationToken cancellationToken);

    /// <summary>
    /// Abstract method for provider-specific shutdown logic
    /// </summary>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>A result indicating success or failure</returns>
    protected abstract Task<Result<bool>> PerformShutdownAsync(CancellationToken cancellationToken);

    /// <summary>
    /// Virtual method for provider-specific health checks
    /// </summary>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>A result containing health check information</returns>
    protected virtual async Task<Result<HealthCheckResult>> PerformHealthCheckAsync(CancellationToken cancellationToken)
    {
        await Task.CompletedTask; // Default implementation
        return Result<HealthCheckResult>.Success(HealthCheckResult.Healthy("Provider is healthy"));
    }

    /// <summary>
    /// Virtual method for custom configuration validation
    /// </summary>
    /// <param name="config">The configuration to validate</param>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>A result indicating whether validation passed</returns>
    protected virtual async Task<Result<bool>> ValidateCustomConfigurationAsync(TConfig config, CancellationToken cancellationToken)
    {
        await Task.CompletedTask; // Default implementation
        return Result<bool>.Success(true);
    }

    /// <summary>
    /// Virtual method for applying configuration updates
    /// </summary>
    /// <param name="config">The new configuration</param>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>A result indicating success or failure</returns>
    protected virtual async Task<Result<bool>> ApplyConfigurationUpdateAsync(TConfig config, CancellationToken cancellationToken)
    {
        await Task.CompletedTask; // Default implementation
        return Result<bool>.Success(true);
    }

    /// <summary>
    /// Virtual method for provider-specific reset logic
    /// </summary>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>A result indicating success or failure</returns>
    protected virtual async Task<Result<bool>> PerformResetAsync(CancellationToken cancellationToken)
    {
        await Task.CompletedTask; // Default implementation
        return Result<bool>.Success(true);
    }

    /// <summary>
    /// Executes an operation with proper state management and metrics collection
    /// </summary>
    /// <typeparam name="TResult">The result type</typeparam>
    /// <param name="operationName">Name of the operation</param>
    /// <param name="operation">The operation to execute</param>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>The operation result</returns>
    protected async Task<Result<TResult>> ExecuteOperationAsync<TResult>(
        string operationName,
        Func<CancellationToken, Task<Result<TResult>>> operation,
        CancellationToken cancellationToken = default)
    {
        if (!CanAcceptOperations)
        {
            return Result<TResult>.Failure(new InvalidOperationError($"Provider {Name} cannot accept operations in state {State}"));
        }

        var stopwatch = System.Diagnostics.Stopwatch.StartNew();
        var previousState = State;

        await _operationSemaphore.WaitAsync(cancellationToken);

        try
        {
            UpdateState(ProviderState.Busy, $"Executing operation: {operationName}");

            var result = await operation(cancellationToken);

            stopwatch.Stop();

            if (result.IsSuccess)
            {
                RecordSuccessfulOperation($"Operation {operationName} completed successfully");
                RecordOperationCompleted(operationName, true, stopwatch.Elapsed);
                UpdateStatistics(true, stopwatch.Elapsed);
            }
            else
            {
                RecordError(result.Error);
                RecordOperationCompleted(operationName, false, stopwatch.Elapsed, result.Error);
                UpdateStatistics(false, stopwatch.Elapsed);
            }

            // Return to previous state if it was Ready
            if (previousState == ProviderState.Ready)
            {
                UpdateState(ProviderState.Ready, result.IsSuccess ? "Operation completed successfully" : $"Operation failed: {result.Error?.Message}");
            }

            return result;
        }
        catch (Exception ex)
        {
            stopwatch.Stop();
            var error = ex.ToProviderError($"Unexpected error in operation {operationName}");
            RecordError(error);
            RecordOperationCompleted(operationName, false, stopwatch.Elapsed, error);
            UpdateStatistics(false, stopwatch.Elapsed);

            UpdateState(ProviderState.Error, $"Unexpected error in operation {operationName}: {ex.Message}");

            return Result<TResult>.Failure(error);
        }
        finally
        {
            _operationSemaphore.Release();
        }
    }

    /// <summary>
    /// Updates the provider state
    /// </summary>
    /// <param name="newState">The new state</param>
    /// <param name="description">Description of the state change</param>
    protected void UpdateState(ProviderState newState, string description)
    {
        ProviderStateInfo previousStateInfo;
        ProviderStateInfo newStateInfo;

        lock (_stateLock)
        {
            // Validate transition
            var transitionResult = ProviderStateTransitions.ValidateTransition(_stateInfo.State, newState);
            if (transitionResult.IsFailure)
            {
                _logger.LogWarning("Invalid state transition attempted from {FromState} to {ToState} for provider {ProviderName}: {Error}",
                    _stateInfo.State, newState, Name, transitionResult.Error.Message);
                return;
            }

            previousStateInfo = _stateInfo;
            newStateInfo = _stateInfo.WithState(newState, description);
            _stateInfo = newStateInfo;
        }

        _logger.LogDebug("Provider {ProviderName} state changed from {PreviousState} to {NewState}: {Description}",
            Name, previousStateInfo.State, newState, description);

        OnStateChanged(previousStateInfo.State, newStateInfo);
    }

    /// <summary>
    /// Updates the provider state with an error
    /// </summary>
    /// <param name="error">The error that occurred</param>
    /// <param name="description">Description of the error</param>
    protected void UpdateStateWithError(ProviderError error, string description)
    {
        ProviderStateInfo previousStateInfo;
        ProviderStateInfo newStateInfo;

        lock (_stateLock)
        {
            previousStateInfo = _stateInfo;
            newStateInfo = _stateInfo.WithError(error, description);
            _stateInfo = newStateInfo;
        }

        RecordError(error);

        _logger.LogError("Provider {ProviderName} entered error state: {Description} - {Error}",
            Name, description, error.GetDetailedDescription());

        OnStateChanged(previousStateInfo.State, newStateInfo);
    }

    /// <summary>
    /// Records a successful operation
    /// </summary>
    /// <param name="description">Description of the successful operation</param>
    protected void RecordSuccessfulOperation(string description)
    {
        lock (_stateLock)
        {
            _stateInfo = _stateInfo.WithSuccessfulOperation(description);
        }
    }

    /// <summary>
    /// Records an error
    /// </summary>
    /// <param name="error">The error to record</param>
    protected void RecordError(ProviderError error)
    {
        lock (_stateLock)
        {
            _recentErrors.Add(error);

            // Keep only the last 50 errors
            while (_recentErrors.Count > 50)
            {
                _recentErrors.RemoveAt(0);
            }
        }
    }

    /// <summary>
    /// Validates a state transition
    /// </summary>
    /// <param name="fromState">Current state</param>
    /// <param name="toState">Target state</param>
    /// <returns>A result indicating whether the transition is valid</returns>
    protected Result ValidateStateTransition(ProviderState fromState, ProviderState toState)
    {
        return ProviderStateTransitions.ValidateTransition(fromState, toState);
    }

    /// <summary>
    /// Records operation completion for metrics
    /// </summary>
    private void RecordOperationCompleted(string operationName, bool success, TimeSpan duration, ProviderError? error = null)
    {
        // Record performance data point
        lock (_stateLock)
        {
            var dataPoint = new PerformanceDataPoint
            {
                Operation = operationName,
                Duration = duration,
                Success = success,
                Metadata = error != null ? new Dictionary<string, object> { { "Error", error.GetDetailedDescription() } } : new Dictionary<string, object>()
            };

            _performanceHistory.Add(dataPoint);

            // Keep only the last 1000 data points
            while (_performanceHistory.Count > 1000)
            {
                _performanceHistory.RemoveAt(0);
            }
        }

        // Record metrics
        RecordMetric($"operation.{operationName.ToLowerInvariant()}.duration_ms", duration.TotalMilliseconds);
        RecordMetric($"operation.{operationName.ToLowerInvariant()}.success", success ? 1 : 0);

        // Fire event
        OnOperationCompleted(operationName, success, duration, error);
    }

    /// <summary>
    /// Updates provider statistics
    /// </summary>
    private void UpdateStatistics(bool success, TimeSpan duration)
    {
        lock (_stateLock)
        {
            var newTotalOps = _statistics.TotalOperations + 1;
            var newSuccessOps = success ? _statistics.SuccessfulOperations + 1 : _statistics.SuccessfulOperations;
            var newFailedOps = success ? _statistics.FailedOperations : _statistics.FailedOperations + 1;

            // Calculate new average duration
            var totalDurationMs = _statistics.AverageOperationDuration.TotalMilliseconds * _statistics.TotalOperations + duration.TotalMilliseconds;
            var newAverageDuration = TimeSpan.FromMilliseconds(totalDurationMs / newTotalOps);

            _statistics = _statistics with
            {
                TotalOperations = newTotalOps,
                SuccessfulOperations = newSuccessOps,
                FailedOperations = newFailedOps,
                AverageOperationDuration = newAverageDuration
            };
        }
    }

    /// <summary>
    /// Fires the StateChanged event
    /// </summary>
    private void OnStateChanged(ProviderState previousState, ProviderStateInfo newStateInfo)
    {
        try
        {
            StateChanged?.Invoke(this, new ProviderStateChangedEventArgs
            {
                ProviderName = Name,
                PreviousState = previousState,
                NewState = newStateInfo.State,
                StateInfo = newStateInfo
            });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error firing StateChanged event for provider {ProviderName}", Name);
        }
    }

    /// <summary>
    /// Fires the OperationCompleted event
    /// </summary>
    private void OnOperationCompleted(string operationName, bool success, TimeSpan duration, ProviderError? error)
    {
        try
        {
            OperationCompleted?.Invoke(this, new ProviderOperationEventArgs
            {
                ProviderName = Name,
                OperationName = operationName,
                IsSuccess = success,
                Duration = duration,
                Error = error
            });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error firing OperationCompleted event for provider {ProviderName}", Name);
        }
    }

    /// <summary>
    /// Fires the MetricsUpdated event
    /// </summary>
    private void OnMetricsUpdated(Dictionary<string, double> metrics)
    {
        try
        {
            MetricsUpdated?.Invoke(this, new ProviderMetricsEventArgs
            {
                ProviderName = Name,
                Metrics = metrics
            });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error firing MetricsUpdated event for provider {ProviderName}", Name);
        }
    }

    /// <summary>
    /// Disposes of the provider resources
    /// </summary>
    public void Dispose()
    {
        Dispose(disposing: true);
        GC.SuppressFinalize(this);
    }

    /// <summary>
    /// Disposes of the provider resources
    /// </summary>
    /// <param name="disposing">Whether the method is called from Dispose</param>
    protected virtual void Dispose(bool disposing)
    {
        if (!_disposed && disposing)
        {
            _suspensionCancellation?.Cancel();
            _suspensionCancellation?.Dispose();
            _operationSemaphore?.Dispose();
            _disposed = true;
        }
    }
}