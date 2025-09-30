using System;
using System.Collections.Concurrent;
using System.Diagnostics;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using TrashMailPanda.Shared.Base;

namespace TrashMailPanda.Shared.Security;

/// <summary>
/// Token rotation service implementation with timer-based scheduling
/// Automatically rotates provider tokens before they expire
/// </summary>
public class TokenRotationService : ITokenRotationService, IDisposable
{
    private readonly ISecureStorageManager _secureStorageManager;
    private readonly IGoogleOAuthService _googleOAuthService;
    private readonly ILogger<TokenRotationService> _logger;

    private readonly Timer? _rotationTimer;
    private readonly ConcurrentDictionary<string, TokenRotationSettings> _providerSettings;
    private readonly ConcurrentDictionary<string, ProviderRotationStatistics> _providerStatistics;
    private readonly object _operationLock = new();
    private readonly SemaphoreSlim _rotationSemaphore = new(1, 1);

    private bool _isRunning = false;
    private bool _disposed = false;
    private int _totalRotations = 0;
    private int _totalFailures = 0;

    // Default settings
    private static readonly TimeSpan DefaultCheckInterval = TimeSpan.FromHours(6);
    private static readonly TimeSpan DefaultExpiryThreshold = TimeSpan.FromHours(24);

    public bool IsRunning => _isRunning;
    public TimeSpan CheckInterval { get; private set; } = DefaultCheckInterval;
    public int TotalRotations => _totalRotations;

    public event EventHandler<TokenRotatedEventArgs>? TokenRotated;
    public event EventHandler<TokenRotationFailedEventArgs>? TokenRotationFailed;

    public TokenRotationService(
        ISecureStorageManager secureStorageManager,
        IGoogleOAuthService googleOAuthService,
        ILogger<TokenRotationService> logger)
    {
        _secureStorageManager = secureStorageManager ?? throw new ArgumentNullException(nameof(secureStorageManager));
        _googleOAuthService = googleOAuthService ?? throw new ArgumentNullException(nameof(googleOAuthService));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));

        _providerSettings = new ConcurrentDictionary<string, TokenRotationSettings>();
        _providerStatistics = new ConcurrentDictionary<string, ProviderRotationStatistics>();

        // Initialize timer but don't start it yet
        _rotationTimer = new Timer(OnRotationTimerElapsed, null, Timeout.Infinite, Timeout.Infinite);

        // Set default settings for known providers
        InitializeDefaultProviderSettings();
    }

    public Task<Result<bool>> StartRotationSchedulerAsync(CancellationToken cancellationToken = default)
    {
        if (_disposed)
        {
            return Task.FromResult(Result<bool>.Failure(new InvalidOperationError("Service has been disposed")));
        }

        lock (_operationLock)
        {
            if (_isRunning)
            {
                _logger.LogInformation("Token rotation scheduler is already running");
                return Task.FromResult(Result<bool>.Success(true));
            }

            try
            {
                // Start the timer with the configured check interval
                _rotationTimer?.Change(TimeSpan.Zero, CheckInterval);
                _isRunning = true;

                _logger.LogInformation("Token rotation scheduler started with check interval: {Interval}", CheckInterval);
                return Task.FromResult(Result<bool>.Success(true));
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to start token rotation scheduler");
                return Task.FromResult(Result<bool>.Failure(new ProcessingError($"Failed to start scheduler: {ex.Message}", null, ex)));
            }
        }
    }

    public Task<Result<bool>> StopRotationSchedulerAsync(CancellationToken cancellationToken = default)
    {
        lock (_operationLock)
        {
            if (!_isRunning)
            {
                _logger.LogInformation("Token rotation scheduler is already stopped");
                return Task.FromResult(Result<bool>.Success(true));
            }

            try
            {
                // Stop the timer
                _rotationTimer?.Change(Timeout.Infinite, Timeout.Infinite);
                _isRunning = false;

                _logger.LogInformation("Token rotation scheduler stopped");
                return Task.FromResult(Result<bool>.Success(true));
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to stop token rotation scheduler");
                return Task.FromResult(Result<bool>.Failure(new ProcessingError($"Failed to stop scheduler: {ex.Message}", null, ex)));
            }
        }
    }

    public async Task<Result<TokenRotationResult>> RotateTokensAsync(string providerName, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(providerName))
        {
            return Result<TokenRotationResult>.Failure(new ValidationError("Provider name cannot be null or empty"));
        }

        var stopwatch = Stopwatch.StartNew();

        try
        {
            await _rotationSemaphore.WaitAsync(cancellationToken);

            try
            {
                _logger.LogDebug("Starting token rotation for provider: {ProviderName}", providerName);

                // Check if the provider has tokens that need rotation
                var needsRotation = await IsTokenNearExpiryAsync(providerName, cancellationToken);
                if (!needsRotation.IsSuccess)
                {
                    TokenRotationFailed?.Invoke(this, new TokenRotationFailedEventArgs
                    {
                        ProviderName = providerName,
                        ErrorMessage = needsRotation.Error?.Message ?? "Unknown error checking token expiry",
                        IsFinalAttempt = true
                    });

                    return Result<TokenRotationResult>.Failure(new ProcessingError($"Failed to check token expiry: {needsRotation.Error?.Message ?? "Unknown error"}"));
                }

                var result = new TokenRotationResult
                {
                    ProviderName = providerName,
                    AttemptedAt = DateTime.UtcNow,
                    Duration = stopwatch.Elapsed
                };

                if (!needsRotation.Value)
                {
                    result = result with
                    {
                        WasRotated = false,
                        Reason = "Token is not near expiry",
                        Duration = stopwatch.Elapsed
                    };

                    _logger.LogDebug("Token rotation not needed for provider: {ProviderName}", providerName);
                    return Result<TokenRotationResult>.Success(result);
                }

                // Perform the actual token rotation based on provider type
                var rotationResult = providerName.ToLowerInvariant() switch
                {
                    "gmail" => await RotateGmailTokensAsync(cancellationToken),
                    "contacts" => await RotateGmailTokensAsync(cancellationToken), // Contacts use same Google OAuth tokens
                    "googleservices" => await RotateGmailTokensAsync(cancellationToken), // GoogleServices provider
                    "openai" => await RotateOpenAITokensAsync(cancellationToken),
                    _ => Result<TokenRotationResult>.Failure(new UnsupportedOperationError($"Unsupported provider: {providerName}"))
                };

                if (rotationResult.IsSuccess)
                {
                    Interlocked.Increment(ref _totalRotations);
                    UpdateProviderStatistics(providerName, success: true);

                    result = rotationResult.Value! with { Duration = stopwatch.Elapsed };

                    TokenRotated?.Invoke(this, new TokenRotatedEventArgs
                    {
                        ProviderName = providerName,
                        Result = result
                    });

                    _logger.LogInformation("Successfully rotated tokens for provider: {ProviderName}", providerName);
                    return Result<TokenRotationResult>.Success(result);
                }
                else
                {
                    Interlocked.Increment(ref _totalFailures);
                    UpdateProviderStatistics(providerName, success: false);

                    result = result with
                    {
                        WasRotated = false,
                        Reason = "Token rotation failed",
                        ErrorMessage = rotationResult.Error.Message,
                        Duration = stopwatch.Elapsed
                    };

                    TokenRotationFailed?.Invoke(this, new TokenRotationFailedEventArgs
                    {
                        ProviderName = providerName,
                        ErrorMessage = rotationResult.Error?.Message ?? "Unknown error",
                        IsFinalAttempt = true
                    });

                    _logger.LogError("Failed to rotate tokens for provider {ProviderName}: {Error}", providerName, rotationResult.Error);
                    return Result<TokenRotationResult>.Success(result); // Return success with failure details
                }
            }
            finally
            {
                _rotationSemaphore.Release();
            }
        }
        catch (Exception ex)
        {
            Interlocked.Increment(ref _totalFailures);
            UpdateProviderStatistics(providerName, success: false);

            var result = new TokenRotationResult
            {
                ProviderName = providerName,
                WasRotated = false,
                Reason = "Exception during rotation",
                ErrorMessage = ex.Message,
                AttemptedAt = DateTime.UtcNow,
                Duration = stopwatch.Elapsed
            };

            TokenRotationFailed?.Invoke(this, new TokenRotationFailedEventArgs
            {
                ProviderName = providerName,
                ErrorMessage = ex.Message,
                Exception = ex,
                IsFinalAttempt = true
            });

            _logger.LogError(ex, "Exception during token rotation for provider: {ProviderName}", providerName);
            return Result<TokenRotationResult>.Success(result); // Return success with exception details
        }
    }

    public async Task<Result<bool>> IsTokenNearExpiryAsync(string providerName, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(providerName))
        {
            return Result<bool>.Failure(new ValidationError("Provider name cannot be null or empty"));
        }

        try
        {
            // Get provider settings to determine expiry threshold
            var settings = _providerSettings.GetValueOrDefault(providerName, new TokenRotationSettings());

            // For now, implement a simple heuristic - always consider rotation needed for demonstration
            // In a real implementation, you would check actual token expiry times from the provider

            _logger.LogDebug("Checking token expiry for provider: {ProviderName}", providerName);

            // Simplified logic - check if we have stored tokens for this provider
            var hasTokens = providerName.ToLowerInvariant() switch
            {
                "gmail" => await CheckGoogleTokenExpiryAsync(settings.ExpiryThreshold, cancellationToken),
                "contacts" => await CheckGoogleTokenExpiryAsync(settings.ExpiryThreshold, cancellationToken),
                "openai" => await CheckOpenAITokenExpiryAsync(settings.ExpiryThreshold, cancellationToken),
                _ => Result<bool>.Failure(new UnsupportedOperationError($"Unsupported provider: {providerName}"))
            };

            return hasTokens;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Exception checking token expiry for provider: {ProviderName}", providerName);
            return Result<bool>.Failure(new ProcessingError($"Failed to check token expiry: {ex.Message}", null, ex));
        }
    }

    public Task<Result<DateTime?>> GetNextRotationTimeAsync(string providerName, CancellationToken cancellationToken = default)
    {
        try
        {
            if (_providerStatistics.TryGetValue(providerName, out var stats))
            {
                return Task.FromResult(Result<DateTime?>.Success(stats.NextScheduledRotation));
            }

            return Task.FromResult(Result<DateTime?>.Success(null));
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Exception getting next rotation time for provider: {ProviderName}", providerName);
            return Task.FromResult(Result<DateTime?>.Failure(new ProcessingError($"Failed to get next rotation time: {ex.Message}", null, ex)));
        }
    }

    public Task<Result<bool>> ConfigureProviderRotationAsync(string providerName, TokenRotationSettings settings, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(providerName))
        {
            return Task.FromResult(Result<bool>.Failure(new ValidationError("Provider name cannot be null or empty")));
        }

        try
        {
            _providerSettings.AddOrUpdate(providerName, settings, (key, oldValue) => settings);

            // Update statistics with new settings
            _providerStatistics.AddOrUpdate(providerName,
                new ProviderRotationStatistics
                {
                    ProviderName = providerName,
                    IsRotationEnabled = settings.IsEnabled
                },
                (key, oldStats) => oldStats with { IsRotationEnabled = settings.IsEnabled });

            _logger.LogInformation("Updated rotation settings for provider: {ProviderName}", providerName);
            return Task.FromResult(Result<bool>.Success(true));
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Exception configuring rotation for provider: {ProviderName}", providerName);
            return Task.FromResult(Result<bool>.Failure(new ConfigurationError($"Failed to configure rotation: {ex.Message}", null, ex)));
        }
    }

    public Task<Result<TokenRotationStatistics>> GetRotationStatisticsAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            var statistics = new TokenRotationStatistics
            {
                TotalRotations = _totalRotations,
                TotalFailures = _totalFailures,
                AverageRotationTime = TimeSpan.FromSeconds(30), // Placeholder
                LastSuccessfulRotation = GetLastSuccessfulRotationTime(),
                LastFailedRotation = GetLastFailedRotationTime(),
                ProviderStatistics = new Dictionary<string, ProviderRotationStatistics>(_providerStatistics),
                CollectedAt = DateTime.UtcNow
            };

            return Task.FromResult(Result<TokenRotationStatistics>.Success(statistics));
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Exception getting rotation statistics");
            return Task.FromResult(Result<TokenRotationStatistics>.Failure(new ProcessingError($"Failed to get statistics: {ex.Message}", null, ex)));
        }
    }

    private async void OnRotationTimerElapsed(object? state)
    {
        if (!_isRunning || _disposed)
            return;

        try
        {
            await Task.CompletedTask; // Ensure method is properly async
            _logger.LogDebug("Rotation timer elapsed, checking all providers");

            var providers = new[] { "gmail", "openai" };

            foreach (var provider in providers)
            {
                if (_providerSettings.TryGetValue(provider, out var settings) && !settings.IsEnabled)
                {
                    _logger.LogDebug("Rotation disabled for provider: {ProviderName}", provider);
                    continue;
                }

                // Fire and forget - don't wait for individual rotations
                _ = Task.Run(async () =>
                {
                    try
                    {
                        await RotateTokensAsync(provider, CancellationToken.None);
                    }
                    catch (Exception ex)
                    {
                        _logger.LogError(ex, "Exception during scheduled rotation for provider: {ProviderName}", provider);
                    }
                });
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Exception in rotation timer callback");
        }
    }

    private void InitializeDefaultProviderSettings()
    {
        // Gmail settings
        _providerSettings.TryAdd("gmail", new TokenRotationSettings
        {
            IsEnabled = true,
            ExpiryThreshold = TimeSpan.FromHours(24),
            CheckInterval = TimeSpan.FromHours(6),
            MaxRetries = 3,
            RetryDelay = TimeSpan.FromMinutes(15)
        });

        // OpenAI settings  
        _providerSettings.TryAdd("openai", new TokenRotationSettings
        {
            IsEnabled = true,
            ExpiryThreshold = TimeSpan.FromDays(7), // API keys don't typically expire quickly
            CheckInterval = TimeSpan.FromHours(24),
            MaxRetries = 2,
            RetryDelay = TimeSpan.FromMinutes(30)
        });

        // Initialize statistics
        foreach (var kvp in _providerSettings)
        {
            _providerStatistics.TryAdd(kvp.Key, new ProviderRotationStatistics
            {
                ProviderName = kvp.Key,
                IsRotationEnabled = kvp.Value.IsEnabled
            });
        }
    }

    private async Task<Result<bool>> CheckGoogleTokenExpiryAsync(TimeSpan expiryThreshold, CancellationToken cancellationToken)
    {
        try
        {
            // Check if we have valid tokens first
            var scopes = new[] { "https://www.googleapis.com/auth/gmail.readonly", "https://www.googleapis.com/auth/contacts.readonly" };
            var hasValidTokensResult = await _googleOAuthService.HasValidTokensAsync(scopes, "google_", cancellationToken);
            if (!hasValidTokensResult.IsSuccess)
            {
                return Result<bool>.Failure(hasValidTokensResult.Error);
            }

            // If tokens are invalid or missing, they need rotation
            if (!hasValidTokensResult.Value)
            {
                return Result<bool>.Success(true); // Needs rotation
            }

            // Get the actual token expiry time to check against threshold
            var tokenExpiryResult = await _secureStorageManager.RetrieveCredentialAsync("google_token_expiry");
            if (!tokenExpiryResult.IsSuccess || string.IsNullOrEmpty(tokenExpiryResult.Value))
            {
                return Result<bool>.Success(true); // No expiry info, assume needs rotation
            }

            if (!DateTime.TryParse(tokenExpiryResult.Value, out var expiryTime))
            {
                return Result<bool>.Success(true); // Invalid expiry format, assume needs rotation
            }

            // Check if token expires within the threshold
            var timeUntilExpiry = expiryTime - DateTime.UtcNow;
            if (timeUntilExpiry <= expiryThreshold)
            {
                _logger.LogDebug("Google token expires in {TimeUntilExpiry}, threshold is {Threshold} - rotation needed",
                    timeUntilExpiry, expiryThreshold);
                return Result<bool>.Success(true); // Near expiry, needs rotation
            }

            _logger.LogDebug("Google token expires in {TimeUntilExpiry}, threshold is {Threshold} - no rotation needed",
                timeUntilExpiry, expiryThreshold);
            return Result<bool>.Success(false); // Not near expiry
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Exception checking Google token expiry");
            return Result<bool>.Failure(new ProcessingError($"Failed to check Google token expiry: {ex.Message}", null, ex));
        }
    }

    private async Task<Result<bool>> CheckOpenAITokenExpiryAsync(TimeSpan expiryThreshold, CancellationToken cancellationToken)
    {
        // Check if we have OpenAI API key stored
        var tokenExists = await _secureStorageManager.CredentialExistsAsync("openai_api_key");
        if (!tokenExists.IsSuccess || !tokenExists.Value)
        {
            return Result<bool>.Success(false); // No token to rotate
        }

        // OpenAI API keys typically don't expire, so rotation would be user-initiated
        // For demonstration, return false unless it's been a very long time
        return Result<bool>.Success(false);
    }

    private async Task<Result<TokenRotationResult>> RotateGmailTokensAsync(CancellationToken cancellationToken)
    {
        try
        {
            _logger.LogDebug("Starting Gmail token rotation using GoogleOAuthService");

            // Use GoogleOAuthService to validate tokens, which will trigger refresh if needed
            var scopes = new[] { "https://www.googleapis.com/auth/gmail.readonly", "https://www.googleapis.com/auth/contacts.readonly" };
            var hasValidTokensResult = await _googleOAuthService.HasValidTokensAsync(scopes, "google_", cancellationToken);

            if (!hasValidTokensResult.IsSuccess)
            {
                var result = new TokenRotationResult
                {
                    ProviderName = "gmail",
                    WasRotated = false,
                    Reason = "Token validation failed",
                    ErrorMessage = hasValidTokensResult.Error?.Message,
                    AttemptedAt = DateTime.UtcNow
                };

                return Result<TokenRotationResult>.Success(result);
            }

            // Check if tokens are now valid (refresh would have been triggered automatically)
            if (hasValidTokensResult.Value)
            {
                var successResult = new TokenRotationResult
                {
                    ProviderName = "gmail",
                    WasRotated = true,
                    Reason = "Google OAuth tokens validated and refreshed if needed",
                    NewExpiryDate = DateTime.UtcNow.AddHours(1), // Google tokens typically last 1 hour
                    AttemptedAt = DateTime.UtcNow
                };

                _logger.LogInformation("Gmail tokens successfully validated/rotated via GoogleOAuthService");
                return Result<TokenRotationResult>.Success(successResult);
            }
            else
            {
                var result = new TokenRotationResult
                {
                    ProviderName = "gmail",
                    WasRotated = false,
                    Reason = "Tokens remain invalid after validation attempt",
                    AttemptedAt = DateTime.UtcNow
                };

                return Result<TokenRotationResult>.Success(result);
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Exception during Gmail token rotation");

            var result = new TokenRotationResult
            {
                ProviderName = "gmail",
                WasRotated = false,
                Reason = "Exception during rotation",
                ErrorMessage = ex.Message,
                AttemptedAt = DateTime.UtcNow
            };

            return Result<TokenRotationResult>.Success(result);
        }
    }

    private Task<Result<TokenRotationResult>> RotateOpenAITokensAsync(CancellationToken cancellationToken)
    {
        // OpenAI API keys don't typically need rotation unless compromised
        // This would be a manual process initiated by the user

        var result = new TokenRotationResult
        {
            ProviderName = "openai",
            WasRotated = false,
            Reason = "OpenAI API keys don't require automatic rotation"
        };

        return Task.FromResult(Result<TokenRotationResult>.Success(result));
    }

    private void UpdateProviderStatistics(string providerName, bool success)
    {
        var now = DateTime.UtcNow;

        _providerStatistics.AddOrUpdate(providerName,
            new ProviderRotationStatistics
            {
                ProviderName = providerName,
                SuccessfulRotations = success ? 1 : 0,
                FailedRotations = success ? 0 : 1,
                LastSuccessfulRotation = success ? now : null,
                IsRotationEnabled = true
            },
            (key, existing) => existing with
            {
                SuccessfulRotations = existing.SuccessfulRotations + (success ? 1 : 0),
                FailedRotations = existing.FailedRotations + (success ? 0 : 1),
                LastSuccessfulRotation = success ? now : existing.LastSuccessfulRotation
            });
    }

    private DateTime? GetLastSuccessfulRotationTime()
    {
        DateTime? latest = null;
        foreach (var stats in _providerStatistics.Values)
        {
            if (stats.LastSuccessfulRotation.HasValue &&
                (latest == null || stats.LastSuccessfulRotation > latest))
            {
                latest = stats.LastSuccessfulRotation;
            }
        }
        return latest;
    }

    private DateTime? GetLastFailedRotationTime()
    {
        // For simplicity, return null - in a real implementation you'd track failed rotation times
        return null;
    }

    public void Dispose()
    {
        if (_disposed) return;

        _rotationTimer?.Dispose();
        _rotationSemaphore?.Dispose();
        _isRunning = false;

        _disposed = true;
        _logger.LogInformation("Token rotation service disposed");
    }
}