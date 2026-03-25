using System;
using System.Net;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Google.Apis.Auth.OAuth2.Responses;
using Google.Apis.Requests;
using Google;
using TrashMailPanda.Shared.Base;
using TrashMailPanda.Shared.Models;
using TrashMailPanda.Providers.Email.Models;

namespace TrashMailPanda.Providers.Email.Services;

/// <summary>
/// Gmail rate limiting handler with exponential backoff retry logic
/// Implements robust retry strategies for Gmail API operations
/// </summary>
public class GmailRateLimitHandler : IGmailRateLimitHandler
{
    private readonly ILogger<GmailRateLimitHandler> _logger;
    private readonly IOptionsMonitor<GmailProviderConfig> _configOptions;
    private readonly Random _random = new();

    /// <summary>
    /// Initializes a new instance of the GmailRateLimitHandler
    /// </summary>
    /// <param name="configOptions">The Gmail provider configuration options</param>
    /// <param name="logger">Logger for the rate limit handler</param>
    public GmailRateLimitHandler(IOptionsMonitor<GmailProviderConfig> configOptions, ILogger<GmailRateLimitHandler> logger)
    {
        _configOptions = configOptions ?? throw new ArgumentNullException(nameof(configOptions));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    /// <inheritdoc />
    public async Task<Result<T>> ExecuteWithRetryAsync<T>(
        Func<Task<T>> operation,
        CancellationToken cancellationToken = default)
    {
        var wrappedOperation = async () =>
        {
            try
            {
                var result = await operation();
                return Result<T>.Success(result);
            }
            catch (GoogleApiException gex) when (gex.HttpStatusCode == System.Net.HttpStatusCode.NotFound)
            {
                return Result<T>.Failure(new NotFoundError(
                    $"Gmail API resource not found: {gex.Message}",
                    gex.Error?.ToString(), gex));
            }
            catch (TokenResponseException tre)
            {
                if (tre.Error?.Error == "invalid_grant")
                {
                    _logger.LogError(
                        "Gmail OAuth refresh token revoked by Google (invalid_grant) — re-authentication required. " +
                        "Description: {Desc}",
                        tre.Error?.ErrorDescription);
                    return Result<T>.Failure(new AuthenticationError(
                        $"Gmail refresh token revoked (invalid_grant): {tre.Error?.ErrorDescription}. Re-authentication required.",
                        tre.Error?.ToString(), tre));
                }

                _logger.LogError(tre,
                    "Gmail OAuth token error: {OAuthError} — {Desc}",
                    tre.Error?.Error, tre.Error?.ErrorDescription);
                return Result<T>.Failure(new AuthenticationError(
                    $"Gmail OAuth token error ({tre.Error?.Error}): {tre.Error?.ErrorDescription ?? tre.Message}",
                    tre.Error?.ToString(), tre));
            }
            catch (Exception ex)
            {
                return Result<T>.Failure(ex.ToProviderError());
            }
        };

        return await ExecuteWithRetryAsync(wrappedOperation, cancellationToken);
    }

    /// <inheritdoc />
    public async Task<Result<T>> ExecuteWithRetryAsync<T>(
        Func<Task<Result<T>>> operation,
        CancellationToken cancellationToken = default)
    {
        var config = _configOptions.CurrentValue;
        var maxAttempts = config.MaxRetries;
        var baseDelay = config.BaseRetryDelay;
        var maxDelay = config.MaxRetryDelay;

        ProviderError? lastError = null;

        for (int attempt = 0; attempt < maxAttempts; attempt++)
        {
            try
            {
                if (cancellationToken.IsCancellationRequested)
                    return Result<T>.Failure(new TimeoutError("Operation was cancelled"));

                _logger.LogDebug("Executing Gmail API operation, attempt {Attempt}/{MaxAttempts}",
                    attempt + 1, maxAttempts);

                var result = await operation();

                if (result.IsSuccess)
                {
                    if (attempt > 0)
                    {
                        _logger.LogInformation("Gmail API operation succeeded after {Attempts} attempts", attempt + 1);
                    }
                    return result;
                }

                lastError = result.Error;

                // Check if this is a retryable error
                if (!ShouldRetryError(result.Error, attempt, maxAttempts))
                {
                    if (result.Error is not NotFoundError)
                        _logger.LogWarning("Gmail API operation failed with non-retryable error: {Error}",
                            result.Error.GetDetailedDescription());
                    return result;
                }

                if (attempt < maxAttempts - 1) // Don't delay on the last attempt
                {
                    var delay = CalculateDelayForError(result.Error, attempt, baseDelay, maxDelay);
                    _logger.LogInformation("Gmail API rate limited, retrying in {DelayMs}ms (attempt {Attempt}/{MaxAttempts})",
                        delay.TotalMilliseconds, attempt + 1, maxAttempts);

                    await Task.Delay(delay, cancellationToken);
                }
            }
            catch (GoogleApiException gex) when (IsRateLimitException(gex))
            {
                lastError = CreateRateLimitError(gex);

                if (attempt < maxAttempts - 1)
                {
                    var delay = CalculateDelayForException(gex, attempt, baseDelay, maxDelay);
                    _logger.LogWarning("Gmail API rate limit exception, retrying in {DelayMs}ms (attempt {Attempt}/{MaxAttempts}): {Message}",
                        delay.TotalMilliseconds, attempt + 1, maxAttempts, gex.Message);

                    await Task.Delay(delay, cancellationToken);
                }
                else
                {
                    _logger.LogError("Gmail API rate limit exceeded after {MaxAttempts} attempts: {Message}",
                        maxAttempts, gex.Message);
                }
            }
            catch (GoogleApiException gex) when (IsAuthException(gex))
            {
                var authError = new AuthenticationError($"Gmail API authentication failed: {gex.Message}",
                    gex.Error?.ToString(), gex);
                _logger.LogError("Gmail API authentication error: {Error}", authError.GetDetailedDescription());
                return Result<T>.Failure(authError);
            }
            catch (GoogleApiException gex)
            {
                var networkError = new NetworkError($"Gmail API error: {gex.Message}",
                    gex.Error?.ToString(), gex);
                lastError = networkError;

                if (!networkError.IsTransient || attempt >= maxAttempts - 1)
                {
                    _logger.LogError("Gmail API non-retryable error: {Error}", networkError.GetDetailedDescription());
                    return Result<T>.Failure(networkError);
                }

                var delay = CalculateDelay(attempt, baseDelay, maxDelay);
                _logger.LogWarning("Gmail API network error, retrying in {DelayMs}ms: {Message}",
                    delay.TotalMilliseconds, gex.Message);

                await Task.Delay(delay, cancellationToken);
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                return Result<T>.Failure(new TimeoutError("Operation was cancelled"));
            }
            catch (Exception ex)
            {
                var unexpectedError = ex.ToProviderError("Unexpected error during Gmail API operation");
                _logger.LogError(ex, "Unexpected error during Gmail API operation: {Error}",
                    unexpectedError.GetDetailedDescription());
                return Result<T>.Failure(unexpectedError);
            }
        }

        var finalError = lastError ?? new TimeoutError("Operation failed after all retry attempts");
        return Result<T>.Failure(finalError);
    }

    /// <inheritdoc />
    public TimeSpan CalculateDelay(int attemptNumber, TimeSpan baseDelay, TimeSpan maxDelay)
    {
        // Exponential backoff with jitter
        var exponentialDelay = TimeSpan.FromMilliseconds(
            baseDelay.TotalMilliseconds * Math.Pow(GmailRateLimitConstants.BACKOFF_MULTIPLIER, attemptNumber));

        // Add jitter to avoid thundering herd
        var jitter = _random.NextDouble() * GmailRateLimitConstants.JITTER_FACTOR;
        var jitteredDelay = TimeSpan.FromMilliseconds(
            exponentialDelay.TotalMilliseconds * (1.0 + jitter));

        // Cap at maximum delay
        return jitteredDelay > maxDelay ? maxDelay : jitteredDelay;
    }

    /// <summary>
    /// Determines if an error should trigger a retry
    /// </summary>
    private static bool ShouldRetryError(ProviderError error, int attemptNumber, int maxAttempts)
    {
        if (attemptNumber >= maxAttempts - 1)
            return false;

        return error switch
        {
            RateLimitError => true,
            QuotaExceededError => true,
            NetworkError => true,
            TimeoutError => true,
            ServiceUnavailableError => true,
            AuthenticationError => false,
            ValidationError => false,
            _ => false
        };
    }

    /// <summary>
    /// Calculates delay for a specific error type
    /// </summary>
    private TimeSpan CalculateDelayForError(ProviderError error, int attemptNumber, TimeSpan baseDelay, TimeSpan maxDelay)
    {
        return error switch
        {
            RateLimitError rateLimitError when rateLimitError.RetryAfter.HasValue =>
                rateLimitError.RetryAfter.Value,
            _ => CalculateDelay(attemptNumber, baseDelay, maxDelay)
        };
    }

    /// <summary>
    /// Calculates delay for a Gmail API exception
    /// </summary>
    private TimeSpan CalculateDelayForException(GoogleApiException exception, int attemptNumber, TimeSpan baseDelay, TimeSpan maxDelay)
    {
        // Try to extract Retry-After header if present
        if (TryGetRetryAfterDelay(exception, out var retryAfter))
        {
            return retryAfter;
        }

        return CalculateDelay(attemptNumber, baseDelay, maxDelay);
    }

    /// <summary>
    /// Checks if a GoogleApiException represents a rate limiting error
    /// </summary>
    private static bool IsRateLimitException(GoogleApiException exception)
    {
        return exception.HttpStatusCode == HttpStatusCode.TooManyRequests ||
               exception.Error?.Code == GmailRateLimitConstants.HTTP_TOO_MANY_REQUESTS ||
               (exception.Error?.Code == GmailRateLimitConstants.HTTP_FORBIDDEN &&
                IsQuotaExceededError(exception));
    }

    /// <summary>
    /// Checks if a GoogleApiException represents an authentication error
    /// </summary>
    private static bool IsAuthException(GoogleApiException exception)
    {
        return exception.HttpStatusCode == HttpStatusCode.Unauthorized ||
               exception.Error?.Code == GmailRateLimitConstants.HTTP_UNAUTHORIZED;
    }

    /// <summary>
    /// Checks if a 403 error is actually a quota exceeded error
    /// </summary>
    private static bool IsQuotaExceededError(GoogleApiException exception)
    {
        var message = exception.Message?.ToLowerInvariant() ?? "";
        var errorMessage = exception.Error?.Message?.ToLowerInvariant() ?? "";

        return message.Contains("quota") || message.Contains("rate") ||
               errorMessage.Contains("quota") || errorMessage.Contains("rate");
    }

    /// <summary>
    /// Creates a rate limit error from a GoogleApiException
    /// </summary>
    private static RateLimitError CreateRateLimitError(GoogleApiException exception)
    {
        var retryAfter = TryGetRetryAfterDelay(exception, out var delay) ? (TimeSpan?)delay : null;

        return new RateLimitError(
            $"Gmail API rate limit exceeded: {exception.Message}",
            exception.Error?.ToString(),
            exception)
        {
            RetryAfter = retryAfter
        };
    }

    /// <summary>
    /// Attempts to extract retry delay from HTTP headers
    /// </summary>
    private static bool TryGetRetryAfterDelay(GoogleApiException exception, out TimeSpan delay)
    {
        delay = TimeSpan.Zero;

        // Try to extract from HTTP response headers if available
        // Note: GoogleApiException doesn't always expose headers directly
        // This is a placeholder for when the information is available

        // Default to a reasonable delay if no specific guidance is provided
        if (exception.HttpStatusCode == HttpStatusCode.TooManyRequests)
        {
            delay = TimeSpan.FromSeconds(60); // Default 60 second delay for 429 errors
            return true;
        }

        return false;
    }
}