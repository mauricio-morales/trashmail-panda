using System;
using System.Collections.Generic;

namespace TrashMailPanda.Shared.Base;

/// <summary>
/// Base class for all provider errors in the Result pattern
/// Provides consistent error handling across all provider implementations
/// </summary>
public abstract record ProviderError(string Message, string? Details = null, Exception? InnerException = null)
{
    /// <summary>
    /// Gets the error category for classification
    /// </summary>
    public abstract string Category { get; }

    /// <summary>
    /// Gets the error code for programmatic handling
    /// </summary>
    public abstract string ErrorCode { get; }

    /// <summary>
    /// Gets additional context information about the error
    /// </summary>
    public virtual Dictionary<string, object> Context { get; init; } = new();

    /// <summary>
    /// Gets the timestamp when the error occurred
    /// </summary>
    public DateTime Timestamp { get; init; } = DateTime.UtcNow;

    /// <summary>
    /// Gets a value indicating whether this error is considered transient (retryable)
    /// </summary>
    public virtual bool IsTransient => false;

    /// <summary>
    /// Gets a value indicating whether this error requires user intervention
    /// </summary>
    public virtual bool RequiresUserIntervention => false;

    /// <summary>
    /// Returns a user-friendly error message
    /// </summary>
    /// <returns>A user-friendly error message</returns>
    public virtual string GetUserFriendlyMessage()
    {
        return Message;
    }

    /// <summary>
    /// Returns a detailed error description for logging/debugging
    /// </summary>
    /// <returns>A detailed error description</returns>
    public virtual string GetDetailedDescription()
    {
        var description = $"{Category}.{ErrorCode}: {Message}";
        if (!string.IsNullOrEmpty(Details))
            description += $" | Details: {Details}";
        if (InnerException != null)
            description += $" | Inner: {InnerException.Message}";
        return description;
    }
}

/// <summary>
/// Error that occurs during provider configuration or setup
/// </summary>
public sealed record ConfigurationError(string Message, string? Details = null, Exception? InnerException = null)
    : ProviderError(Message, Details, InnerException)
{
    public override string Category => "Configuration";
    public override string ErrorCode => "CONFIG_ERROR";
    public override bool RequiresUserIntervention => true;

    public override string GetUserFriendlyMessage()
    {
        return $"Configuration issue: {Message}. Please check your settings and try again.";
    }
}

/// <summary>
/// Error that occurs during provider initialization
/// </summary>
public sealed record InitializationError(string Message, string? Details = null, Exception? InnerException = null)
    : ProviderError(Message, Details, InnerException)
{
    public override string Category => "Initialization";
    public override string ErrorCode => "INIT_ERROR";
    public override bool RequiresUserIntervention => true;

    public override string GetUserFriendlyMessage()
    {
        return $"Initialization failed: {Message}. Please restart the application or check your configuration.";
    }
}

/// <summary>
/// Error that occurs during authentication or authorization
/// </summary>
public sealed record AuthenticationError(string Message, string? Details = null, Exception? InnerException = null)
    : ProviderError(Message, Details, InnerException)
{
    public override string Category => "Authentication";
    public override string ErrorCode => "AUTH_ERROR";
    public override bool RequiresUserIntervention => true;

    public override string GetUserFriendlyMessage()
    {
        return $"Authentication failed: {Message}. Please sign in again or check your credentials.";
    }
}

/// <summary>
/// Error that occurs when OAuth token is missing required scopes
/// </summary>
public sealed record InsufficientScopesError(string Message, string? Details = null, Exception? InnerException = null)
    : ProviderError(Message, Details, InnerException)
{
    public override string Category => "Authentication";
    public override string ErrorCode => "INSUFFICIENT_SCOPES";
    public override bool RequiresUserIntervention => true;

    public override string GetUserFriendlyMessage()
    {
        return $"Insufficient permissions: {Message}. Please re-authorize to grant additional permissions.";
    }
}

/// <summary>
/// Error that occurs during network operations
/// </summary>
public sealed record NetworkError(string Message, string? Details = null, Exception? InnerException = null)
    : ProviderError(Message, Details, InnerException)
{
    public override string Category => "Network";
    public override string ErrorCode => "NET_ERROR";
    public override bool IsTransient => true;

    public override string GetUserFriendlyMessage()
    {
        return $"Network error: {Message}. Please check your internet connection and try again.";
    }
}

/// <summary>
/// Error that occurs when a resource is not found
/// </summary>
public sealed record NotFoundError(string Message, string? Details = null, Exception? InnerException = null)
    : ProviderError(Message, Details, InnerException)
{
    public override string Category => "NotFound";
    public override string ErrorCode => "NOT_FOUND";

    public override string GetUserFriendlyMessage()
    {
        return $"Not found: {Message}";
    }
}

/// <summary>
/// Error that occurs when an operation is not permitted
/// </summary>
public sealed record UnauthorizedError(string Message, string? Details = null, Exception? InnerException = null)
    : ProviderError(Message, Details, InnerException)
{
    public override string Category => "Authorization";
    public override string ErrorCode => "UNAUTHORIZED";
    public override bool RequiresUserIntervention => true;

    public override string GetUserFriendlyMessage()
    {
        return $"Access denied: {Message}. You may need additional permissions or to sign in again.";
    }
}

/// <summary>
/// Error that occurs when a quota or rate limit is exceeded
/// </summary>
public sealed record QuotaExceededError(string Message, string? Details = null, Exception? InnerException = null)
    : ProviderError(Message, Details, InnerException)
{
    public override string Category => "Quota";
    public override string ErrorCode => "QUOTA_EXCEEDED";
    public override bool IsTransient => true;

    public override string GetUserFriendlyMessage()
    {
        return $"Quota exceeded: {Message}. Please wait and try again later, or check your usage limits.";
    }
}

/// <summary>
/// Error that occurs when input data is invalid
/// </summary>
public sealed record ValidationError(string Message, string? Details = null, Exception? InnerException = null)
    : ProviderError(Message, Details, InnerException)
{
    public override string Category => "Validation";
    public override string ErrorCode => "VALIDATION_ERROR";
    public override bool RequiresUserIntervention => true;

    public override string GetUserFriendlyMessage()
    {
        return $"Invalid input: {Message}. Please check your data and try again.";
    }
}

/// <summary>
/// Error that occurs when an operation is attempted in an invalid state
/// </summary>
public sealed record InvalidOperationError(string Message, string? Details = null, Exception? InnerException = null)
    : ProviderError(Message, Details, InnerException)
{
    public override string Category => "InvalidOperation";
    public override string ErrorCode => "INVALID_OPERATION";
    public override bool RequiresUserIntervention => true;

    public override string GetUserFriendlyMessage()
    {
        return $"Invalid operation: {Message}. The operation cannot be performed at this time.";
    }
}

/// <summary>
/// Error that occurs during data processing or transformation
/// </summary>
public sealed record ProcessingError(string Message, string? Details = null, Exception? InnerException = null)
    : ProviderError(Message, Details, InnerException)
{
    public override string Category => "Processing";
    public override string ErrorCode => "PROCESSING_ERROR";

    public override string GetUserFriendlyMessage()
    {
        return $"Processing failed: {Message}. The operation could not be completed.";
    }
}

/// <summary>
/// Error that occurs during storage operations
/// </summary>
public sealed record StorageError(string Message, string? Details = null, Exception? InnerException = null)
    : ProviderError(Message, Details, InnerException)
{
    public override string Category => "Storage";
    public override string ErrorCode => "STORAGE_ERROR";

    public override string GetUserFriendlyMessage()
    {
        return $"Storage error: {Message}. There may be a problem with data storage.";
    }
}

/// <summary>
/// Error that occurs when an operation times out
/// </summary>
public sealed record TimeoutError(string Message, string? Details = null, Exception? InnerException = null)
    : ProviderError(Message, Details, InnerException)
{
    public override string Category => "Timeout";
    public override string ErrorCode => "TIMEOUT";
    public override bool IsTransient => true;

    public override string GetUserFriendlyMessage()
    {
        return $"Operation timed out: {Message}. Please try again.";
    }
}

/// <summary>
/// Error that occurs when a feature is not supported
/// </summary>
public sealed record UnsupportedOperationError(string Message, string? Details = null, Exception? InnerException = null)
    : ProviderError(Message, Details, InnerException)
{
    public override string Category => "Unsupported";
    public override string ErrorCode => "UNSUPPORTED";

    public override string GetUserFriendlyMessage()
    {
        return $"Unsupported operation: {Message}. This feature is not available.";
    }
}

/// <summary>
/// Error that occurs when an external service is unavailable
/// </summary>
public sealed record ServiceUnavailableError(string Message, string? Details = null, Exception? InnerException = null)
    : ProviderError(Message, Details, InnerException)
{
    public override string Category => "Service";
    public override string ErrorCode => "SERVICE_UNAVAILABLE";
    public override bool IsTransient => true;

    public override string GetUserFriendlyMessage()
    {
        return $"Service unavailable: {Message}. The external service is temporarily unavailable.";
    }
}

/// <summary>
/// Error that occurs for unexpected or unknown conditions
/// </summary>
public sealed record UnknownError(string Message, string? Details = null, Exception? InnerException = null)
    : ProviderError(Message, Details, InnerException)
{
    public override string Category => "Unknown";
    public override string ErrorCode => "UNKNOWN_ERROR";

    public override string GetUserFriendlyMessage()
    {
        return $"An unexpected error occurred: {Message}. Please try again or contact support.";
    }
}

/// <summary>
/// Extension methods for working with provider errors
/// </summary>
public static class ProviderErrorExtensions
{
    /// <summary>
    /// Creates a ProviderError from an exception
    /// </summary>
    /// <param name="exception">The exception to convert</param>
    /// <param name="message">Optional custom message</param>
    /// <returns>A ProviderError based on the exception type</returns>
    public static ProviderError ToProviderError(this Exception exception, string? message = null)
    {
        var errorMessage = message ?? exception.Message;
        var details = exception.StackTrace;

        return exception switch
        {
            ArgumentException => new ValidationError(errorMessage, details, exception),
            UnauthorizedAccessException => new UnauthorizedError(errorMessage, details, exception),
            TimeoutException => new TimeoutError(errorMessage, details, exception),
            System.Net.NetworkInformation.NetworkInformationException => new NetworkError(errorMessage, details, exception),
            System.Net.Http.HttpRequestException => new NetworkError(errorMessage, details, exception),
            System.IO.FileNotFoundException => new NotFoundError(errorMessage, details, exception),
            System.IO.DirectoryNotFoundException => new NotFoundError(errorMessage, details, exception),
            InvalidOperationException => new ProcessingError(errorMessage, details, exception),
            NotSupportedException => new UnsupportedOperationError(errorMessage, details, exception),
            _ => new UnknownError(errorMessage, details, exception)
        };
    }

    /// <summary>
    /// Determines if an error should trigger a retry
    /// </summary>
    /// <param name="error">The error to check</param>
    /// <param name="attemptCount">The current attempt count</param>
    /// <param name="maxAttempts">The maximum number of attempts</param>
    /// <returns>True if the operation should be retried</returns>
    public static bool ShouldRetry(this ProviderError error, int attemptCount, int maxAttempts = 3)
    {
        return error.IsTransient && attemptCount < maxAttempts;
    }
}