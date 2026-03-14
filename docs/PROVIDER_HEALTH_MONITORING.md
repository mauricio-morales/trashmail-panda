# Provider Health Monitoring Patterns and Best Practices for Enterprise Applications

## Overview

This document provides comprehensive guidance on implementing robust health monitoring, circuit breaker patterns, retry strategies, and resilience patterns for provider systems in enterprise applications. Based on industry best practices from Microsoft's ASP.NET Core Health Checks, Polly resilience library patterns, and enterprise monitoring strategies.

## Table of Contents

1. [Health Check Patterns](#health-check-patterns)
2. [Circuit Breaker Patterns](#circuit-breaker-patterns)
3. [Retry Strategies](#retry-strategies)
4. [Provider State Machines](#provider-state-machines)
5. [Monitoring and Alerting](#monitoring-and-alerting)
6. [Configuration Validation](#configuration-validation)
7. [Dependency Health Management](#dependency-health-management)
8. [Implementation Examples for TrashMail Panda](#implementation-examples-for-transmail-panda)

---

## Health Check Patterns

### Health Check Types

Enterprise applications should implement three distinct types of health checks:

#### 1. Liveness Probes
- **Purpose**: Determine if application has crashed and needs restart
- **Scope**: Minimal checks for basic application functionality
- **Response**: Simple "healthy" or "unhealthy" status
- **Frequency**: High (every 10-30 seconds)

```csharp
public class LivenessHealthCheck : IHealthCheck
{
    public async Task<HealthCheckResult> CheckHealthAsync(
        HealthCheckContext context,
        CancellationToken cancellationToken = default)
    {
        // Basic application health - is the process responsive?
        return await Task.FromResult(HealthCheckResult.Healthy("Application is responsive"));
    }
}
```

#### 2. Readiness Probes
- **Purpose**: Determine if application is ready to receive traffic
- **Scope**: Include dependency checks, startup validation
- **Response**: Detailed status with component breakdown
- **Frequency**: Moderate (every 30-60 seconds)

```csharp
public class ReadinessHealthCheck : IHealthCheck
{
    private readonly IProviderRegistry _providerRegistry;
    
    public async Task<HealthCheckResult> CheckHealthAsync(
        HealthCheckContext context,
        CancellationToken cancellationToken = default)
    {
        var data = new Dictionary<string, object>();
        var issues = new List<string>();
        
        // Check critical dependencies
        var stats = _providerRegistry.GetStatistics();
        data["active_providers"] = stats.ActiveInstances;
        data["total_providers"] = stats.TotalInstances;
        
        if (stats.ActiveInstances == 0)
        {
            issues.Add("No active providers available");
        }
        
        var status = issues.Count == 0 ? HealthStatus.Healthy : HealthStatus.Degraded;
        return new HealthCheckResult(status, string.Join(", ", issues), data: data);
    }
}
```

#### 3. Startup Probes
- **Purpose**: Handle slow-starting containers and complex initialization
- **Scope**: Extended timeouts for startup validation
- **Response**: Progressive startup status
- **Frequency**: During startup only

```csharp
public class StartupHealthCheck : IHealthCheck
{
    private readonly IStartupOrchestrator _orchestrator;
    
    public async Task<HealthCheckResult> CheckHealthAsync(
        HealthCheckContext context,
        CancellationToken cancellationToken = default)
    {
        var progress = _orchestrator.GetProgress();
        var data = new Dictionary<string, object>
        {
            ["startup_step"] = progress.CurrentStep.ToString(),
            ["progress_percentage"] = progress.ProgressPercentage,
            ["is_complete"] = progress.IsComplete
        };
        
        if (progress.HasError)
        {
            return new HealthCheckResult(HealthStatus.Unhealthy, 
                progress.ErrorMessage ?? "Startup failed", data: data);
        }
        
        if (!progress.IsComplete)
        {
            return new HealthCheckResult(HealthStatus.Degraded, 
                "Startup in progress", data: data);
        }
        
        return new HealthCheckResult(HealthStatus.Healthy, 
            "Startup completed successfully", data: data);
    }
}
```

### Health Check Best Practices

1. **Lightweight Checks**: Keep health checks fast (< 5 seconds)
2. **Granular Results**: Provide component-level status information
3. **Dependency Isolation**: Don't fail liveness for dependency issues
4. **Caching**: Cache expensive health check results appropriately
5. **Security**: Protect health endpoints from unauthorized access
6. **Timeouts**: Always implement timeouts for health checks

---

## Circuit Breaker Patterns

Circuit breakers prevent cascading failures and provide graceful degradation when dependencies fail.

### Circuit Breaker States

1. **Closed**: Normal operation, requests flow through
2. **Open**: Failures detected, requests fail immediately
3. **Half-Open**: Testing if dependency recovered

### Implementation with Polly

```csharp
public class ProviderCircuitBreakerService
{
    private readonly IAsyncPolicy<Result<T>> _circuitBreakerPolicy;
    
    public ProviderCircuitBreakerService()
    {
        _circuitBreakerPolicy = Policy
            .HandleResult<Result<T>>(r => r.IsFailure)
            .CircuitBreakerAsync(
                handledEventsAllowedBeforeBreaking: 5,
                durationOfBreak: TimeSpan.FromMinutes(1),
                onBreak: OnCircuitBreakerOpen,
                onReset: OnCircuitBreakerClosed,
                onHalfOpen: OnCircuitBreakerHalfOpen);
    }
    
    public async Task<Result<T>> ExecuteAsync<T>(Func<Task<Result<T>>> operation)
    {
        try
        {
            return await _circuitBreakerPolicy.ExecuteAsync(operation);
        }
        catch (CircuitBreakerOpenException)
        {
            return Result.Failure(new NetworkError("Service temporarily unavailable"));
        }
    }
    
    private void OnCircuitBreakerOpen(Result<T> result, TimeSpan duration)
    {
        // Log circuit breaker opened
        // Update provider state to suspended
        // Trigger alerts
    }
    
    private void OnCircuitBreakerClosed()
    {
        // Log circuit breaker closed
        // Update provider state to ready
    }
    
    private void OnCircuitBreakerHalfOpen()
    {
        // Log circuit breaker testing
    }
}
```

### Circuit Breaker Configuration

```csharp
public class CircuitBreakerOptions
{
    /// <summary>
    /// Number of consecutive failures before opening circuit
    /// </summary>
    public int FailureThreshold { get; set; } = 5;
    
    /// <summary>
    /// Time to wait before attempting to close circuit
    /// </summary>
    public TimeSpan BreakDuration { get; set; } = TimeSpan.FromMinutes(1);
    
    /// <summary>
    /// Minimum number of requests in sampling period
    /// </summary>
    public int MinimumThroughput { get; set; } = 10;
    
    /// <summary>
    /// Failure rate threshold (0.0 to 1.0)
    /// </summary>
    public double FailureRate { get; set; } = 0.5;
    
    /// <summary>
    /// Sampling duration for failure rate calculation
    /// </summary>
    public TimeSpan SamplingDuration { get; set; } = TimeSpan.FromSeconds(30);
}
```

---

## Retry Strategies

Implement sophisticated retry patterns with exponential backoff, jitter, and intelligent failure classification.

### Exponential Backoff with Jitter

```csharp
public class RetryPolicyService
{
    public static IAsyncPolicy<Result<T>> CreateRetryPolicy<T>(RetryOptions options)
    {
        return Policy
            .HandleResult<Result<T>>(r => r.IsFailure && r.Error.IsTransient)
            .WaitAndRetryAsync(
                retryCount: options.MaxRetryCount,
                sleepDurationProvider: retryAttempt => CalculateDelay(retryAttempt, options),
                onRetry: OnRetry);
    }
    
    private static TimeSpan CalculateDelay(int retryAttempt, RetryOptions options)
    {
        // Exponential backoff with jitter
        var exponentialDelay = TimeSpan.FromMilliseconds(
            options.BaseDelayMs * Math.Pow(options.BackoffMultiplier, retryAttempt - 1));
        
        // Add jitter to prevent thundering herd
        var jitter = TimeSpan.FromMilliseconds(
            Random.Shared.NextDouble() * options.JitterMs);
        
        var totalDelay = exponentialDelay.Add(jitter);
        
        // Cap at maximum delay
        return totalDelay > options.MaxDelay ? options.MaxDelay : totalDelay;
    }
    
    private static void OnRetry(Outcome<Result<T>> outcome, int retryCount, TimeSpan delay)
    {
        // Log retry attempt with context
    }
}

public class RetryOptions
{
    public int MaxRetryCount { get; set; } = 3;
    public int BaseDelayMs { get; set; } = 1000;
    public double BackoffMultiplier { get; set; } = 2.0;
    public int JitterMs { get; set; } = 500;
    public TimeSpan MaxDelay { get; set; } = TimeSpan.FromMinutes(1);
}
```

### Failure Classification

```csharp
public static class FailureClassification
{
    private static readonly HashSet<Type> TransientErrors = new()
    {
        typeof(NetworkError),
        typeof(TimeoutError),
        typeof(RateLimitError)
    };
    
    private static readonly HashSet<Type> PermanentErrors = new()
    {
        typeof(AuthenticationError),
        typeof(ConfigurationError),
        typeof(ValidationError)
    };
    
    public static bool IsTransient(ProviderError error)
    {
        return TransientErrors.Contains(error.GetType()) || 
               error.IsTransient;
    }
    
    public static bool RequiresImmediateAction(ProviderError error)
    {
        return PermanentErrors.Contains(error.GetType()) || 
               error.RequiresUserIntervention;
    }
}
```

---

## Provider State Machines

Implement robust state management with validated transitions and comprehensive state tracking.

### Enhanced State Machine

```csharp
public class ProviderStateMachine<TConfig> where TConfig : BaseProviderConfig
{
    private readonly object _stateLock = new();
    private readonly ILogger _logger;
    private ProviderStateInfo _currentState = new();
    private readonly Dictionary<ProviderState, StateHandler> _stateHandlers;
    
    public event EventHandler<ProviderStateChangedEventArgs>? StateChanged;
    
    public ProviderStateMachine(ILogger logger)
    {
        _logger = logger;
        _stateHandlers = new Dictionary<ProviderState, StateHandler>
        {
            [ProviderState.Uninitialized] = new UninitializedStateHandler(),
            [ProviderState.Initializing] = new InitializingStateHandler(),
            [ProviderState.Ready] = new ReadyStateHandler(),
            [ProviderState.Busy] = new BusyStateHandler(),
            [ProviderState.Error] = new ErrorStateHandler(),
            [ProviderState.Suspended] = new SuspendedStateHandler(),
            [ProviderState.ShuttingDown] = new ShuttingDownStateHandler(),
            [ProviderState.Shutdown] = new ShutdownStateHandler()
        };
    }
    
    public async Task<Result<bool>> TransitionToAsync(
        ProviderState targetState, 
        string? reason = null,
        CancellationToken cancellationToken = default)
    {
        lock (_stateLock)
        {
            var validationResult = ProviderStateTransitions.ValidateTransition(
                _currentState.State, targetState);
            
            if (validationResult.IsFailure)
            {
                return validationResult.As<bool>();
            }
        }
        
        var previousState = _currentState;
        
        try
        {
            // Execute state transition logic
            var handler = _stateHandlers[targetState];
            var transitionResult = await handler.EnterStateAsync(_currentState, reason, cancellationToken);
            
            if (transitionResult.IsFailure)
            {
                return transitionResult;
            }
            
            // Update state
            lock (_stateLock)
            {
                _currentState = _currentState.WithState(targetState, reason);
            }
            
            // Fire state change event
            StateChanged?.Invoke(this, new ProviderStateChangedEventArgs
            {
                ProviderName = GetType().Name,
                PreviousState = previousState.State,
                NewState = targetState,
                StateInfo = _currentState
            });
            
            _logger.LogInformation("Provider transitioned from {PreviousState} to {NewState}: {Reason}",
                previousState.State, targetState, reason ?? "No reason specified");
            
            return Result.Success(true);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to transition from {PreviousState} to {NewState}",
                previousState.State, targetState);
            
            return Result.Failure(new ProviderError(
                $"State transition failed: {ex.Message}", ex));
        }
    }
    
    public ProviderStateInfo GetCurrentState()
    {
        lock (_stateLock)
        {
            return _currentState;
        }
    }
}

public abstract class StateHandler
{
    public abstract Task<Result<bool>> EnterStateAsync(
        ProviderStateInfo currentState, 
        string? reason, 
        CancellationToken cancellationToken);
    
    public virtual Task<Result<bool>> ExitStateAsync(
        ProviderStateInfo currentState, 
        CancellationToken cancellationToken)
    {
        return Task.FromResult(Result.Success(true));
    }
}
```

---

## Monitoring and Alerting

Implement comprehensive monitoring with real-time alerts and performance tracking.

### Health Endpoint Monitoring

```csharp
public class ProviderHealthMonitorService : IHostedService, IDisposable
{
    private readonly IServiceProvider _serviceProvider;
    private readonly ILogger<ProviderHealthMonitorService> _logger;
    private readonly Timer? _healthCheckTimer;
    private readonly HealthMonitoringOptions _options;
    
    public ProviderHealthMonitorService(
        IServiceProvider serviceProvider,
        ILogger<ProviderHealthMonitorService> logger,
        IOptions<HealthMonitoringOptions> options)
    {
        _serviceProvider = serviceProvider;
        _logger = logger;
        _options = options.Value;
    }
    
    public Task StartAsync(CancellationToken cancellationToken)
    {
        _healthCheckTimer = new Timer(
            ExecuteHealthChecks, 
            null, 
            TimeSpan.Zero,
            _options.CheckInterval);
        
        return Task.CompletedTask;
    }
    
    private async void ExecuteHealthChecks(object? state)
    {
        try
        {
            using var scope = _serviceProvider.CreateScope();
            var healthCheckService = scope.ServiceProvider.GetRequiredService<HealthCheckService>();
            
            var healthReport = await healthCheckService.CheckHealthAsync();
            
            // Process health report
            await ProcessHealthReportAsync(healthReport);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error during health check execution");
        }
    }
    
    private async Task ProcessHealthReportAsync(HealthReport report)
    {
        var metrics = new Dictionary<string, double>
        {
            ["health_check_duration_ms"] = report.TotalDuration.TotalMilliseconds,
            ["healthy_checks"] = report.Entries.Count(e => e.Value.Status == HealthStatus.Healthy),
            ["degraded_checks"] = report.Entries.Count(e => e.Value.Status == HealthStatus.Degraded),
            ["unhealthy_checks"] = report.Entries.Count(e => e.Value.Status == HealthStatus.Unhealthy)
        };
        
        // Send metrics to monitoring system
        await SendMetricsAsync(metrics);
        
        // Check for alerts
        await CheckAlertsAsync(report);
    }
    
    private async Task CheckAlertsAsync(HealthReport report)
    {
        var criticalFailures = report.Entries
            .Where(e => e.Value.Status == HealthStatus.Unhealthy)
            .ToList();
        
        if (criticalFailures.Any())
        {
            var alertMessage = $"Critical health check failures: {string.Join(", ", criticalFailures.Select(f => f.Key))}";
            await SendAlertAsync(AlertLevel.Critical, alertMessage);
        }
    }
}

public class HealthMonitoringOptions
{
    public TimeSpan CheckInterval { get; set; } = TimeSpan.FromMinutes(1);
    public bool EnableAlerts { get; set; } = true;
    public string[] CriticalHealthChecks { get; set; } = Array.Empty<string>();
}
```

### Performance Monitoring

```csharp
public class ProviderPerformanceMonitor
{
    private readonly ILogger _logger;
    private readonly ConcurrentDictionary<string, PerformanceCounter> _counters = new();
    
    public async Task<T> TrackOperationAsync<T>(
        string operationName, 
        Func<Task<T>> operation,
        Dictionary<string, string>? tags = null)
    {
        var stopwatch = Stopwatch.StartNew();
        var counter = _counters.GetOrAdd(operationName, _ => new PerformanceCounter());
        
        counter.IncrementTotal();
        
        try
        {
            var result = await operation();
            
            stopwatch.Stop();
            counter.RecordSuccess(stopwatch.Elapsed);
            
            RecordMetrics(operationName, stopwatch.Elapsed, true, tags);
            
            return result;
        }
        catch (Exception ex)
        {
            stopwatch.Stop();
            counter.RecordFailure(stopwatch.Elapsed);
            
            RecordMetrics(operationName, stopwatch.Elapsed, false, tags);
            
            _logger.LogError(ex, "Operation {OperationName} failed after {Duration}ms", 
                operationName, stopwatch.Elapsed.TotalMilliseconds);
            
            throw;
        }
    }
    
    private void RecordMetrics(string operationName, TimeSpan duration, bool success, Dictionary<string, string>? tags)
    {
        var metricTags = new Dictionary<string, string>
        {
            ["operation"] = operationName,
            ["success"] = success.ToString().ToLowerInvariant()
        };
        
        if (tags != null)
        {
            foreach (var tag in tags)
            {
                metricTags[tag.Key] = tag.Value;
            }
        }
        
        // Record to metrics system (e.g., Prometheus, Application Insights)
    }
}

public class PerformanceCounter
{
    private long _totalOperations;
    private long _successfulOperations;
    private long _failedOperations;
    private readonly List<TimeSpan> _recentDurations = new();
    private readonly object _lock = new();
    
    public void IncrementTotal() => Interlocked.Increment(ref _totalOperations);
    
    public void RecordSuccess(TimeSpan duration)
    {
        Interlocked.Increment(ref _successfulOperations);
        lock (_lock)
        {
            _recentDurations.Add(duration);
            if (_recentDurations.Count > 100) // Keep last 100 measurements
            {
                _recentDurations.RemoveAt(0);
            }
        }
    }
    
    public void RecordFailure(TimeSpan duration)
    {
        Interlocked.Increment(ref _failedOperations);
    }
    
    public PerformanceMetrics GetMetrics()
    {
        lock (_lock)
        {
            return new PerformanceMetrics
            {
                TotalOperations = _totalOperations,
                SuccessfulOperations = _successfulOperations,
                FailedOperations = _failedOperations,
                SuccessRate = _totalOperations > 0 ? (double)_successfulOperations / _totalOperations : 0,
                AverageDuration = _recentDurations.Count > 0 ? 
                    TimeSpan.FromTicks((long)_recentDurations.Average(d => d.Ticks)) : 
                    TimeSpan.Zero,
                P95Duration = CalculatePercentile(_recentDurations, 0.95),
                P99Duration = CalculatePercentile(_recentDurations, 0.99)
            };
        }
    }
    
    private static TimeSpan CalculatePercentile(List<TimeSpan> durations, double percentile)
    {
        if (!durations.Any()) return TimeSpan.Zero;
        
        var sorted = durations.OrderBy(d => d.Ticks).ToList();
        var index = (int)Math.Ceiling(sorted.Count * percentile) - 1;
        return sorted[Math.Max(0, Math.Min(index, sorted.Count - 1))];
    }
}
```

---

## Configuration Validation

Implement multi-layer configuration validation with comprehensive error reporting.

### DataAnnotations Validation

```csharp
[AttributeUsage(AttributeTargets.Property)]
public class ProviderConfigurationAttribute : ValidationAttribute
{
    public bool Required { get; set; } = true;
    public bool Sensitive { get; set; } = false;
    public string[]? AllowedValues { get; set; }
    
    protected override ValidationResult? IsValid(object? value, ValidationContext validationContext)
    {
        if (Required && (value == null || string.IsNullOrWhiteSpace(value.ToString())))
        {
            return new ValidationResult($"Configuration property '{validationContext.DisplayName}' is required.");
        }
        
        if (AllowedValues != null && value != null)
        {
            var stringValue = value.ToString();
            if (!AllowedValues.Contains(stringValue, StringComparer.OrdinalIgnoreCase))
            {
                return new ValidationResult(
                    $"Configuration property '{validationContext.DisplayName}' must be one of: {string.Join(", ", AllowedValues)}");
            }
        }
        
        return ValidationResult.Success;
    }
}

public class GmailProviderConfig : BaseProviderConfig
{
    [ProviderConfiguration(Required = true, Sensitive = true)]
    [MinLength(10, ErrorMessage = "Client ID must be at least 10 characters")]
    public string ClientId { get; set; } = string.Empty;
    
    [ProviderConfiguration(Required = true, Sensitive = true)]
    [MinLength(10, ErrorMessage = "Client Secret must be at least 10 characters")]
    public string ClientSecret { get; set; } = string.Empty;
    
    [ProviderConfiguration(Required = false)]
    [Url(ErrorMessage = "Redirect URI must be a valid URL")]
    public string RedirectUri { get; set; } = "http://localhost:8080/oauth/callback";
    
    [ProviderConfiguration(AllowedValues = new[] { "readonly", "modify", "compose", "send" })]
    public string[] Scopes { get; set; } = new[] { "readonly" };
}
```

### Multi-Layer Validation

```csharp
public class ProviderConfigurationValidator<TConfig> : IValidateOptions<TConfig>
    where TConfig : BaseProviderConfig
{
    private readonly ILogger<ProviderConfigurationValidator<TConfig>> _logger;
    
    public ProviderConfigurationValidator(ILogger<ProviderConfigurationValidator<TConfig>> logger)
    {
        _logger = logger;
    }
    
    public ValidateOptionsResult Validate(string name, TConfig options)
    {
        var errors = new List<string>();
        
        // Layer 1: DataAnnotations validation
        var annotationResults = ValidateDataAnnotations(options);
        errors.AddRange(annotationResults);
        
        // Layer 2: Business logic validation
        var businessResults = ValidateBusinessLogic(options);
        errors.AddRange(businessResults);
        
        // Layer 3: External dependency validation
        var dependencyResults = ValidateExternalDependencies(options);
        errors.AddRange(dependencyResults);
        
        if (errors.Any())
        {
            var errorMessage = $"Configuration validation failed for {typeof(TConfig).Name}: {string.Join("; ", errors)}";
            _logger.LogError("Configuration validation failed: {Errors}", string.Join(", ", errors));
            return ValidateOptionsResult.Fail(errorMessage);
        }
        
        _logger.LogDebug("Configuration validation passed for {ConfigType}", typeof(TConfig).Name);
        return ValidateOptionsResult.Success;
    }
    
    private List<string> ValidateDataAnnotations(TConfig options)
    {
        var errors = new List<string>();
        var context = new ValidationContext(options);
        var results = new List<ValidationResult>();
        
        if (!Validator.TryValidateObject(options, context, results, validateAllProperties: true))
        {
            errors.AddRange(results.Select(r => r.ErrorMessage ?? "Unknown validation error"));
        }
        
        return errors;
    }
    
    private List<string> ValidateBusinessLogic(TConfig options)
    {
        var errors = new List<string>();
        
        // Implement provider-specific business logic validation
        if (options is GmailProviderConfig gmailConfig)
        {
            if (!gmailConfig.RedirectUri.StartsWith("http://localhost") && 
                !gmailConfig.RedirectUri.StartsWith("https://"))
            {
                errors.Add("Redirect URI must be localhost for desktop apps or HTTPS for web apps");
            }
            
            var requiredScopes = new[] { "https://www.googleapis.com/auth/gmail.readonly" };
            if (!gmailConfig.Scopes.Any(s => requiredScopes.Contains(s)))
            {
                errors.Add("At least one Gmail scope is required");
            }
        }
        
        return errors;
    }
    
    private List<string> ValidateExternalDependencies(TConfig options)
    {
        var errors = new List<string>();
        
        // Validate external dependencies (network connectivity, API availability)
        // This should be lightweight and not block configuration loading
        
        return errors;
    }
}
```

---

## Dependency Health Management

Handle complex dependency chains and cascading failure scenarios.

### Dependency Graph

```csharp
public class DependencyHealthManager
{
    private readonly ILogger<DependencyHealthManager> _logger;
    private readonly Dictionary<string, DependencyNode> _dependencyGraph = new();
    private readonly Timer? _healthCheckTimer;
    
    public DependencyHealthManager(ILogger<DependencyHealthManager> logger)
    {
        _logger = logger;
        _healthCheckTimer = new Timer(CheckDependencyHealth, null, TimeSpan.Zero, TimeSpan.FromMinutes(1));
    }
    
    public void RegisterDependency(string serviceName, string[] dependencies, IHealthCheck healthCheck)
    {
        _dependencyGraph[serviceName] = new DependencyNode
        {
            ServiceName = serviceName,
            Dependencies = dependencies.ToHashSet(),
            HealthCheck = healthCheck,
            LastCheck = DateTime.MinValue,
            Status = HealthStatus.Unknown
        };
    }
    
    public async Task<DependencyHealthReport> GetHealthReportAsync()
    {
        var report = new DependencyHealthReport();
        var checkTasks = new List<Task>();
        
        foreach (var node in _dependencyGraph.Values)
        {
            checkTasks.Add(CheckNodeHealthAsync(node, report));
        }
        
        await Task.WhenAll(checkTasks);
        
        // Calculate overall health based on dependency chain
        report.OverallStatus = CalculateOverallHealth(report);
        
        return report;
    }
    
    private async Task CheckNodeHealthAsync(DependencyNode node, DependencyHealthReport report)
    {
        try
        {
            var result = await node.HealthCheck.CheckHealthAsync(new HealthCheckContext());
            
            node.Status = result.Status switch
            {
                Microsoft.Extensions.Diagnostics.HealthChecks.HealthStatus.Healthy => HealthStatus.Healthy,
                Microsoft.Extensions.Diagnostics.HealthChecks.HealthStatus.Degraded => HealthStatus.Degraded,
                Microsoft.Extensions.Diagnostics.HealthChecks.HealthStatus.Unhealthy => HealthStatus.Unhealthy,
                _ => HealthStatus.Unknown
            };
            
            node.LastCheck = DateTime.UtcNow;
            node.LastError = result.Exception?.Message;
            
            report.ServiceStatus[node.ServiceName] = new ServiceHealthStatus
            {
                Status = node.Status,
                Description = result.Description,
                LastCheck = node.LastCheck,
                Dependencies = node.Dependencies.ToArray(),
                Data = result.Data
            };
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Health check failed for service {ServiceName}", node.ServiceName);
            
            node.Status = HealthStatus.Critical;
            node.LastError = ex.Message;
            
            report.ServiceStatus[node.ServiceName] = new ServiceHealthStatus
            {
                Status = HealthStatus.Critical,
                Description = $"Health check failed: {ex.Message}",
                LastCheck = DateTime.UtcNow,
                Dependencies = node.Dependencies.ToArray()
            };
        }
    }
    
    private HealthStatus CalculateOverallHealth(DependencyHealthReport report)
    {
        if (!report.ServiceStatus.Any())
            return HealthStatus.Unknown;
        
        // If any critical service is down, overall status is critical
        if (report.ServiceStatus.Values.Any(s => s.Status == HealthStatus.Critical))
            return HealthStatus.Critical;
        
        // If any service is unhealthy, overall status is unhealthy
        if (report.ServiceStatus.Values.Any(s => s.Status == HealthStatus.Unhealthy))
            return HealthStatus.Unhealthy;
        
        // If any service is degraded, overall status is degraded
        if (report.ServiceStatus.Values.Any(s => s.Status == HealthStatus.Degraded))
            return HealthStatus.Degraded;
        
        // All services healthy
        return HealthStatus.Healthy;
    }
    
    private async void CheckDependencyHealth(object? state)
    {
        try
        {
            var report = await GetHealthReportAsync();
            
            // Process health report for alerting/logging
            if (report.OverallStatus == HealthStatus.Critical)
            {
                _logger.LogCritical("Dependency health check failed - overall status: {Status}", report.OverallStatus);
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error during dependency health check");
        }
    }
}

public class DependencyNode
{
    public string ServiceName { get; set; } = string.Empty;
    public HashSet<string> Dependencies { get; set; } = new();
    public IHealthCheck HealthCheck { get; set; } = null!;
    public DateTime LastCheck { get; set; }
    public HealthStatus Status { get; set; }
    public string? LastError { get; set; }
}

public class DependencyHealthReport
{
    public HealthStatus OverallStatus { get; set; } = HealthStatus.Unknown;
    public Dictionary<string, ServiceHealthStatus> ServiceStatus { get; } = new();
    public DateTime GeneratedAt { get; } = DateTime.UtcNow;
}

public class ServiceHealthStatus
{
    public HealthStatus Status { get; set; }
    public string Description { get; set; } = string.Empty;
    public DateTime LastCheck { get; set; }
    public string[] Dependencies { get; set; } = Array.Empty<string>();
    public IReadOnlyDictionary<string, object>? Data { get; set; }
}
```

---

## Implementation Examples for TrashMail Panda

### Enhanced Provider Health Check

```csharp
public class EnhancedProviderHealthCheck<TProvider> : IHealthCheck where TProvider : class
{
    private readonly TProvider? _provider;
    private readonly ILogger _logger;
    private readonly CircuitBreakerOptions _circuitBreakerOptions;
    private readonly RetryOptions _retryOptions;
    
    public async Task<HealthCheckResult> CheckHealthAsync(
        HealthCheckContext context,
        CancellationToken cancellationToken = default)
    {
        var providerName = typeof(TProvider).Name;
        var stopwatch = Stopwatch.StartNew();
        
        try
        {
            if (_provider == null)
            {
                return HealthCheckResult.Unhealthy($"Provider {providerName} is not registered");
            }
            
            // Multi-layer health check
            var results = new List<ComponentHealthStatus>();
            
            // Layer 1: Basic availability
            results.Add(await CheckBasicAvailability());
            
            // Layer 2: Configuration validation
            results.Add(await CheckConfigurationHealth());
            
            // Layer 3: Dependency health
            results.Add(await CheckDependencyHealth());
            
            // Layer 4: Performance check
            results.Add(await CheckPerformanceMetrics());
            
            stopwatch.Stop();
            
            var worstStatus = results.Max(r => r.Status);
            var allIssues = results.SelectMany(r => r.Issues).ToList();
            
            return new HealthCheckResult
            {
                Status = worstStatus,
                Description = $"Provider {providerName} health check completed",
                Duration = stopwatch.Elapsed,
                ComponentStatuses = results.ToDictionary(r => r.Name, r => r),
                Issues = allIssues,
                Diagnostics = new Dictionary<string, object>
                {
                    ["provider_type"] = providerName,
                    ["check_layers"] = results.Count,
                    ["total_issues"] = allIssues.Count
                }
            };
        }
        catch (Exception ex)
        {
            stopwatch.Stop();
            _logger.LogError(ex, "Health check failed for provider {ProviderName}", providerName);
            
            return HealthCheckResult.Critical(
                $"Health check failed: {ex.Message}",
                new List<HealthIssue>
                {
                    new()
                    {
                        Level = HealthIssueLevel.Critical,
                        Message = ex.Message,
                        Source = "HealthCheck",
                        Context = new Dictionary<string, object> { ["exception_type"] = ex.GetType().Name }
                    }
                },
                stopwatch.Elapsed);
        }
    }
    
    private async Task<ComponentHealthStatus> CheckBasicAvailability()
    {
        // Check if provider is responsive and in correct state
        if (_provider is IProvider<BaseProviderConfig> provider)
        {
            var canAcceptOps = provider.CanAcceptOperations;
            var isHealthy = provider.IsHealthy;
            
            return new ComponentHealthStatus
            {
                Name = "BasicAvailability",
                Status = (canAcceptOps && isHealthy) ? HealthStatus.Healthy : HealthStatus.Degraded,
                Description = $"Can accept operations: {canAcceptOps}, Is healthy: {isHealthy}",
                Details = new Dictionary<string, object>
                {
                    ["can_accept_operations"] = canAcceptOps,
                    ["is_healthy"] = isHealthy,
                    ["current_state"] = provider.State.ToString()
                }
            };
        }
        
        return new ComponentHealthStatus
        {
            Name = "BasicAvailability",
            Status = HealthStatus.Healthy,
            Description = "Provider is available"
        };
    }
    
    private async Task<ComponentHealthStatus> CheckConfigurationHealth()
    {
        // Validate current configuration
        if (_provider is IProvider<BaseProviderConfig> provider && provider.Configuration != null)
        {
            var validationResult = await provider.ValidateConfigurationAsync(provider.Configuration);
            
            return new ComponentHealthStatus
            {
                Name = "Configuration",
                Status = validationResult.IsSuccess ? HealthStatus.Healthy : HealthStatus.Unhealthy,
                Description = validationResult.IsSuccess ? "Configuration is valid" : $"Configuration invalid: {validationResult.Error?.Message}",
                Issues = validationResult.IsFailure ? new List<HealthIssue>
                {
                    new()
                    {
                        Level = HealthIssueLevel.Error,
                        Message = validationResult.Error?.Message ?? "Configuration validation failed",
                        Source = "ConfigurationValidator"
                    }
                } : new List<HealthIssue>()
            };
        }
        
        return new ComponentHealthStatus
        {
            Name = "Configuration",
            Status = HealthStatus.Unknown,
            Description = "Configuration check not available"
        };
    }
    
    private async Task<ComponentHealthStatus> CheckDependencyHealth()
    {
        // Check external dependencies (network, APIs, etc.)
        var issues = new List<HealthIssue>();
        var status = HealthStatus.Healthy;
        
        try
        {
            // Example: Test network connectivity
            using var client = new HttpClient();
            client.Timeout = TimeSpan.FromSeconds(5);
            
            if (_provider is GmailEmailProvider)
            {
                var response = await client.GetAsync("https://www.googleapis.com/oauth2/v1/certs");
                if (!response.IsSuccessStatusCode)
                {
                    status = HealthStatus.Degraded;
                    issues.Add(new HealthIssue
                    {
                        Level = HealthIssueLevel.Warning,
                        Message = "Gmail API endpoint not responding correctly",
                        Source = "NetworkConnectivityCheck"
                    });
                }
            }
        }
        catch (Exception ex)
        {
            status = HealthStatus.Unhealthy;
            issues.Add(new HealthIssue
            {
                Level = HealthIssueLevel.Error,
                Message = $"Dependency check failed: {ex.Message}",
                Source = "NetworkConnectivityCheck"
            });
        }
        
        return new ComponentHealthStatus
        {
            Name = "Dependencies",
            Status = status,
            Description = "External dependency health check",
            Issues = issues
        };
    }
    
    private async Task<ComponentHealthStatus> CheckPerformanceMetrics()
    {
        // Check performance metrics and thresholds
        if (_provider is IProvider<BaseProviderConfig> provider)
        {
            var metrics = provider.GetMetricsSnapshot();
            var issues = new List<HealthIssue>();
            var status = HealthStatus.Healthy;
            
            // Check for performance thresholds
            if (metrics.Metrics.TryGetValue("average_response_time_ms", out var avgResponseTime))
            {
                if (avgResponseTime > 5000) // 5 seconds threshold
                {
                    status = HealthStatus.Degraded;
                    issues.Add(new HealthIssue
                    {
                        Level = HealthIssueLevel.Warning,
                        Message = $"Average response time is high: {avgResponseTime}ms",
                        Source = "PerformanceMonitor"
                    });
                }
            }
            
            if (metrics.Metrics.TryGetValue("error_rate", out var errorRate))
            {
                if (errorRate > 0.1) // 10% error rate threshold
                {
                    status = HealthStatus.Unhealthy;
                    issues.Add(new HealthIssue
                    {
                        Level = HealthIssueLevel.Error,
                        Message = $"Error rate is too high: {errorRate:P}",
                        Source = "PerformanceMonitor"
                    });
                }
            }
            
            return new ComponentHealthStatus
            {
                Name = "Performance",
                Status = status,
                Description = "Performance metrics check",
                Issues = issues,
                Metrics = metrics.Metrics.ToDictionary(kvp => kvp.Key, kvp => kvp.Value)
            };
        }
        
        return new ComponentHealthStatus
        {
            Name = "Performance",
            Status = HealthStatus.Unknown,
            Description = "Performance metrics not available"
        };
    }
}
```

### OAuth Token Refresh with Circuit Breaker

```csharp
public class GmailTokenRefreshService
{
    private readonly ILogger<GmailTokenRefreshService> _logger;
    private readonly IAsyncPolicy _circuitBreakerPolicy;
    private readonly IAsyncPolicy _retryPolicy;
    
    public GmailTokenRefreshService(ILogger<GmailTokenRefreshService> logger)
    {
        _logger = logger;
        
        _circuitBreakerPolicy = Policy
            .Handle<HttpRequestException>()
            .Or<TaskCanceledException>()
            .CircuitBreakerAsync(
                exceptionsAllowedBeforeBreaking: 3,
                durationOfBreak: TimeSpan.FromMinutes(2),
                onBreak: (exception, duration) => 
                {
                    _logger.LogWarning("Token refresh circuit breaker opened for {Duration} due to: {Exception}", 
                        duration, exception.Message);
                },
                onReset: () => 
                {
                    _logger.LogInformation("Token refresh circuit breaker closed - service recovered");
                });
        
        _retryPolicy = Policy
            .Handle<HttpRequestException>()
            .Or<TaskCanceledException>()
            .WaitAndRetryAsync(
                retryCount: 3,
                sleepDurationProvider: retryAttempt => TimeSpan.FromSeconds(Math.Pow(2, retryAttempt)) + 
                                                    TimeSpan.FromMilliseconds(Random.Shared.Next(0, 1000)),
                onRetry: (outcome, delay, retryCount, context) =>
                {
                    _logger.LogWarning("Token refresh retry attempt {RetryCount} after {Delay}ms delay", 
                        retryCount, delay.TotalMilliseconds);
                });
    }
    
    public async Task<Result<TokenInfo>> RefreshTokenAsync(string refreshToken)
    {
        try
        {
            var combinedPolicy = Policy.WrapAsync(_circuitBreakerPolicy, _retryPolicy);
            
            var tokenInfo = await combinedPolicy.ExecuteAsync(async () =>
            {
                using var client = new HttpClient();
                var request = new HttpRequestMessage(HttpMethod.Post, "https://oauth2.googleapis.com/token");
                
                var parameters = new[]
                {
                    new KeyValuePair<string, string>("client_id", "your_client_id"),
                    new KeyValuePair<string, string>("client_secret", "your_client_secret"),
                    new KeyValuePair<string, string>("refresh_token", refreshToken),
                    new KeyValuePair<string, string>("grant_type", "refresh_token")
                };
                
                request.Content = new FormUrlEncodedContent(parameters);
                
                var response = await client.SendAsync(request);
                
                if (!response.IsSuccessStatusCode)
                {
                    var errorContent = await response.Content.ReadAsStringAsync();
                    throw new HttpRequestException($"Token refresh failed: {response.StatusCode} - {errorContent}");
                }
                
                var json = await response.Content.ReadAsStringAsync();
                var tokenResponse = JsonSerializer.Deserialize<TokenResponse>(json);
                
                return new TokenInfo
                {
                    AccessToken = tokenResponse?.AccessToken ?? throw new InvalidOperationException("No access token received"),
                    RefreshToken = tokenResponse.RefreshToken ?? refreshToken, // Use existing if not provided
                    ExpiresAt = DateTime.UtcNow.AddSeconds(tokenResponse.ExpiresIn ?? 3600),
                    TokenType = tokenResponse.TokenType ?? "Bearer"
                };
            });
            
            _logger.LogDebug("Token refreshed successfully, expires at: {ExpiresAt}", tokenInfo.ExpiresAt);
            return Result.Success(tokenInfo);
        }
        catch (CircuitBreakerOpenException)
        {
            _logger.LogError("Token refresh failed - circuit breaker is open");
            return Result.Failure(new NetworkError("Token refresh service temporarily unavailable"));
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Token refresh failed after all retry attempts");
            return Result.Failure(new AuthenticationError($"Failed to refresh token: {ex.Message}"));
        }
    }
}

public record TokenInfo
{
    public string AccessToken { get; init; } = string.Empty;
    public string RefreshToken { get; init; } = string.Empty;
    public DateTime ExpiresAt { get; init; }
    public string TokenType { get; init; } = "Bearer";
}

public record TokenResponse
{
    [JsonPropertyName("access_token")]
    public string? AccessToken { get; init; }
    
    [JsonPropertyName("refresh_token")]
    public string? RefreshToken { get; init; }
    
    [JsonPropertyName("expires_in")]
    public int? ExpiresIn { get; init; }
    
    [JsonPropertyName("token_type")]
    public string? TokenType { get; init; }
}
```

---

## Summary

This comprehensive guide provides enterprise-grade patterns for:

1. **Health Check Patterns**: Multi-layered health validation with liveness, readiness, and startup probes
2. **Circuit Breaker Patterns**: Preventing cascading failures with intelligent failure detection
3. **Retry Strategies**: Sophisticated retry logic with exponential backoff and jitter
4. **Provider State Machines**: Robust state management with validated transitions
5. **Monitoring and Alerting**: Real-time health monitoring with performance tracking
6. **Configuration Validation**: Multi-layer validation with comprehensive error reporting
7. **Dependency Health Management**: Complex dependency chain management with cascade failure prevention

These patterns provide a solid foundation for building resilient, observable, and maintainable provider systems in enterprise applications like TrashMail Panda.

### Key Implementation Principles

1. **Fail Fast**: Detect failures quickly and provide clear feedback
2. **Graceful Degradation**: Maintain partial functionality when possible
3. **Observable Systems**: Comprehensive logging, metrics, and tracing
4. **Self-Healing**: Automatic recovery mechanisms where appropriate
5. **Defense in Depth**: Multiple layers of protection and validation
6. **Performance Awareness**: Monitor and alert on performance degradation
7. **Security First**: Secure handling of sensitive configuration and tokens

### Integration with TrashMail Panda

The patterns in this document can be directly integrated with TrashMail Panda's existing provider system by:

1. Enhancing the existing `IProvider` interface with circuit breaker capabilities
2. Implementing comprehensive health checks for Gmail, OpenAI, and SQLite providers
3. Adding performance monitoring to the `StartupOrchestrator`
4. Enhancing configuration validation in provider setup
5. Implementing OAuth token refresh with resilience patterns

This approach ensures TrashMail Panda maintains high availability, provides excellent observability, and handles failures gracefully while maintaining data integrity and security.