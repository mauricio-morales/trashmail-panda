using System.Net.Http;
using Microsoft.Extensions.Logging;
using Spectre.Console;
using TrashMailPanda.Shared.Base;

namespace TrashMailPanda.Services;

/// <summary>
/// Handles OAuth errors and provides user-friendly messaging
/// </summary>
public static class OAuthErrorHandler
{
    /// <summary>
    /// Display user-friendly error message with retry option
    /// </summary>
    /// <param name="exception">The exception that occurred</param>
    /// <param name="allowRetry">Whether to offer retry option</param>
    /// <param name="logger">Optional logger for technical details</param>
    public static void DisplayError(Exception exception, bool allowRetry = true, ILogger? logger = null)
    {
        var (userMessage, technicalDetails, isRetryable) = MapExceptionToUserMessage(exception);

        // Log technical details if logger provided
        logger?.LogError(exception, "OAuth error: {UserMessage}", userMessage);

        // Display user-friendly error
        AnsiConsole.MarkupLine("[bold red]✗ Error:[/] [red]{0}[/]", Markup.Escape(userMessage));

        if (!string.IsNullOrEmpty(technicalDetails))
        {
            AnsiConsole.MarkupLine("[dim red]{0}[/]", Markup.Escape(technicalDetails));
        }

        // Offer retry if applicable
        if (allowRetry && isRetryable)
        {
            AnsiConsole.MarkupLine("[cyan]You can try again or contact support if the problem persists.[/]");
        }
        else if (!isRetryable)
        {
            AnsiConsole.MarkupLine("[yellow]⚠ This error requires manual intervention.[/]");
        }
    }

    /// <summary>
    /// Display error from Result pattern
    /// </summary>
    public static void DisplayError(ProviderError error, bool allowRetry = true, ILogger? logger = null)
    {
        var (userMessage, technicalDetails, isRetryable) = MapErrorToUserMessage(error);

        logger?.LogError("OAuth error: {Type} - {Message}", error.GetType().Name, error.Message);

        AnsiConsole.MarkupLine("[bold red]✗ Error:[/] [red]{0}[/]", Markup.Escape(userMessage));

        if (!string.IsNullOrEmpty(technicalDetails))
        {
            AnsiConsole.MarkupLine("[dim red]{0}[/]", Markup.Escape(technicalDetails));
        }

        if (allowRetry && isRetryable)
        {
            AnsiConsole.MarkupLine("[cyan]You can try again or contact support if the problem persists.[/]");
        }
        else if (!isRetryable)
        {
            AnsiConsole.MarkupLine("[yellow]⚠ This error requires manual intervention.[/]");
        }
    }

    /// <summary>
    /// Ask user if they want to retry after an error
    /// </summary>
    public static bool PromptRetry(string context = "the operation")
    {
        return AnsiConsole.Confirm($"[cyan]Would you like to retry {context}?[/]", defaultValue: true);
    }

    /// <summary>
    /// Display manual URL fallback when browser launch fails
    /// </summary>
    public static void DisplayManualUrlInstructions(string authUrl)
    {
        AnsiConsole.MarkupLine("[yellow]⚠ Manual authentication required[/]");
        AnsiConsole.WriteLine();
        AnsiConsole.MarkupLine("[cyan]1. Open your browser and visit:[/]");
        AnsiConsole.WriteLine($"   {authUrl}");
        AnsiConsole.WriteLine();
        AnsiConsole.MarkupLine("[cyan]2. Authorize the application[/]");
        AnsiConsole.MarkupLine("[cyan]3. You will be redirected back to the application[/]");
        AnsiConsole.WriteLine();
    }

    /// <summary>
    /// Map exception to user-friendly message
    /// </summary>
    private static (string userMessage, string technicalDetails, bool isRetryable) MapExceptionToUserMessage(Exception exception)
    {
        return exception switch
        {
            // Network/connectivity errors
            HttpRequestException httpEx => (
                "Network connection failed. Please check your internet connection.",
                $"HTTP error: {httpEx.Message}",
                true
            ),

            System.Net.Sockets.SocketException socketEx => (
                "Network connection failed. Please check your internet connection and firewall settings.",
                $"Socket error: {socketEx.Message}",
                true
            ),

            TimeoutException => (
                "The operation timed out. Please try again.",
                "Request exceeded timeout threshold",
                true
            ),

            // OAuth-specific errors
            Google.GoogleApiException googleEx when googleEx.Error?.Errors?.Any(e => e.Reason == "invalid_grant") == true => (
                "Your authentication has expired or been revoked. Please sign in again.",
                "Refresh token invalid or revoked",
                false
            ),

            Google.GoogleApiException googleEx when googleEx.HttpStatusCode == System.Net.HttpStatusCode.Unauthorized => (
                "Authentication failed. Please check your credentials and try again.",
                $"Unauthorized: {googleEx.Message}",
                true
            ),

            Google.GoogleApiException googleEx when googleEx.HttpStatusCode == System.Net.HttpStatusCode.Forbidden => (
                "Access denied. Please check your OAuth permissions.",
                $"Forbidden: {googleEx.Message}",
                false
            ),

            Google.GoogleApiException googleEx when ((int)googleEx.HttpStatusCode) == 429 => (
                "Too many requests. Please wait a moment and try again.",
                "Rate limit exceeded",
                true
            ),

            // Process/system errors
            System.ComponentModel.Win32Exception win32Ex => (
                "Failed to open browser. Please open the authentication URL manually.",
                $"System error: {win32Ex.Message}",
                true
            ),

            InvalidOperationException invOpEx => (
                "Operation failed due to invalid state. Please restart the authentication process.",
                $"Invalid operation: {invOpEx.Message}",
                true
            ),

            // Generic fallback
            _ => (
                "An unexpected error occurred during authentication.",
                $"{exception.GetType().Name}: {exception.Message}",
                true
            )
        };
    }

    /// <summary>
    /// Map Result error to user-friendly message
    /// </summary>
    private static (string userMessage, string technicalDetails, bool isRetryable) MapErrorToUserMessage(ProviderError error)
    {
        return error switch
        {
            AuthenticationError authError => (
                authError.Message.Contains("denied", StringComparison.OrdinalIgnoreCase)
                    ? "You denied the authentication request. Please authorize the application to continue."
                    : authError.Message.Contains("revoked", StringComparison.OrdinalIgnoreCase)
                    ? "Your authentication has expired. Please sign in again."
                    : $"Authentication failed: {authError.Message}",
                $"Authentication error: {authError.Message}",
                !authError.Message.Contains("revoked", StringComparison.OrdinalIgnoreCase)
            ),

            NetworkError netError => (
                netError.Message.Contains("timeout", StringComparison.OrdinalIgnoreCase)
                    ? "The operation timed out. Please try again."
                    : netError.Message.Contains("browser", StringComparison.OrdinalIgnoreCase)
                    ? "Failed to open browser. You can open the authentication URL manually."
                    : $"Network error: {netError.Message}",
                $"Network error: {netError.Message}",
                true
            ),

            ConfigurationError configError => (
                configError.Message.Contains("Client", StringComparison.OrdinalIgnoreCase)
                    ? "OAuth is not configured. Please set up your Gmail credentials."
                    : $"Configuration error: {configError.Message}",
                $"Configuration error: {configError.Message}",
                false
            ),

            ProcessingError procError => (
                $"Processing error: {procError.Message}",
                procError.Message,
                true
            ),

            _ => (
                $"Error: {error.Message}",
                error.Message,
                true
            )
        };
    }
}
