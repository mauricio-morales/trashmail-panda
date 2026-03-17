using System.Net;
using TrashMailPanda.Models;
using TrashMailPanda.Shared.Base;
using Microsoft.Extensions.Logging;

namespace TrashMailPanda.Services;

/// <summary>
/// Localhost HTTP listener for OAuth callback handling
/// </summary>
public class LocalOAuthCallbackListener : ILocalOAuthCallbackListener
{
    private readonly ILogger<LocalOAuthCallbackListener> _logger;
    private HttpListener? _httpListener;
    private int _port;
    private bool _isStarted;

    public LocalOAuthCallbackListener(ILogger<LocalOAuthCallbackListener> logger)
    {
        _logger = logger;
    }

    /// <summary>
    /// Start HTTP listener on localhost with dynamic port
    /// </summary>
    public async Task<Result<int>> StartAsync(
        string callbackPath = "/oauth/callback",
        CancellationToken cancellationToken = default)
    {
        try
        {
            if (_isStarted)
            {
                return Result<int>.Failure(new ConfigurationError("HTTP listener already started"));
            }

            _httpListener = new HttpListener();

            // Try dynamic port allocation (port 0)
            var prefix = $"http://127.0.0.1:0{callbackPath}/";
            _httpListener.Prefixes.Add(prefix);

            _logger.LogDebug("Starting HTTP listener with prefix: {Prefix}", prefix);

            _httpListener.Start();
            _isStarted = true;

            // Extract assigned port from the listener
            // Note: HttpListener doesn't expose the port directly when using port 0
            // We need to extract it from the listening endpoint
            var listenerPrefix = _httpListener.Prefixes.First();
            var uri = new Uri(listenerPrefix);
            _port = uri.Port;

            if (_port == 0)
            {
                // If port is still 0, try to get it from the first available prefix
                // For dynamic port allocation, we'll use a workaround
                _httpListener.Stop();
                _httpListener = new HttpListener();

                // Use available port finder
                _port = FindAvailablePort();
                prefix = $"http://127.0.0.1:{_port}{callbackPath}/";
                _httpListener.Prefixes.Add(prefix);
                _httpListener.Start();
            }

            _logger.LogInformation("HTTP listener started on port {Port}", _port);

            return Result<int>.Success(_port);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to start HTTP listener");
            return Result<int>.Failure(new NetworkError($"Failed to start HTTP listener: {ex.Message}"));
        }
    }

    /// <summary>
    /// Get complete redirect URI for OAuth authorization URL
    /// </summary>
    public string GetRedirectUri(string callbackPath = "/oauth/callback")
    {
        if (!_isStarted || _port == 0)
        {
            throw new InvalidOperationException("HTTP listener not started. Call StartAsync first.");
        }

        return $"http://127.0.0.1:{_port}{callbackPath}";
    }

    /// <summary>
    /// Wait for OAuth callback with timeout
    /// </summary>
    public async Task<Result<OAuthCallbackData>> WaitForCallbackAsync(
        string expectedState,
        TimeSpan? timeout = null)
    {
        if (_httpListener == null || !_isStarted)
        {
            return Result<OAuthCallbackData>.Failure(
                new ConfigurationError("HTTP listener not started. Call StartAsync first."));
        }

        var waitTimeout = timeout ?? TimeSpan.FromMinutes(5);

        try
        {
            using var cts = new CancellationTokenSource(waitTimeout);

            _logger.LogDebug("Waiting for OAuth callback (timeout: {Timeout})", waitTimeout);

            // Wait for incoming request
            var contextTask = _httpListener.GetContextAsync();
            var completedTask = await Task.WhenAny(contextTask, Task.Delay(waitTimeout, cts.Token));

            if (completedTask != contextTask)
            {
                _logger.LogWarning("OAuth callback timed out after {Timeout}", waitTimeout);
                return Result<OAuthCallbackData>.Failure(
                    new ProcessingError($"Authentication timed out after {waitTimeout.TotalMinutes} minutes"));
            }

            var context = await contextTask;
            var request = context.Request;

            // Validate origin is localhost
            if (request.RemoteEndPoint?.Address.ToString() != "127.0.0.1" &&
                request.RemoteEndPoint?.Address.ToString() != "::1")
            {
                _logger.LogWarning("Received OAuth callback from non-localhost origin: {Origin}",
                    request.RemoteEndPoint?.Address);

                await SendResponseAsync(context, 403, "Forbidden");

                return Result<OAuthCallbackData>.Failure(
                    new AuthenticationError("OAuth callback received from non-localhost origin"));
            }

            // Parse query parameters
            var query = request.QueryString;
            var code = query["code"];
            var state = query["state"];
            var error = query["error"];
            var errorDescription = query["error_description"];

            _logger.LogDebug("OAuth callback received - Code: {HasCode}, State: {State}, Error: {Error}",
                !string.IsNullOrEmpty(code), state, error);

            // Validate state parameter
            if (state != expectedState)
            {
                _logger.LogWarning("OAuth state mismatch. Expected: {Expected}, Received: {Received}",
                    expectedState, state);

                await SendResponseAsync(context, 400, "Invalid state parameter");

                return Result<OAuthCallbackData>.Failure(
                    new AuthenticationError("OAuth state parameter mismatch (CSRF protection)"));
            }

            // Check for errors
            if (!string.IsNullOrEmpty(error))
            {
                _logger.LogWarning("OAuth callback returned error: {Error} - {Description}",
                    error, errorDescription);

                await SendResponseAsync(context, 200,
                    $"Authentication failed: {error}. You can close this window.");

                var callbackData = new OAuthCallbackData
                {
                    Code = code,
                    State = state,
                    Error = error,
                    ErrorDescription = errorDescription
                };

                return Result<OAuthCallbackData>.Success(callbackData);
            }

            // Validate authorization code exists
            if (string.IsNullOrEmpty(code))
            {
                _logger.LogWarning("OAuth callback received without authorization code");

                await SendResponseAsync(context, 400, "Authorization code missing");

                return Result<OAuthCallbackData>.Failure(
                    new AuthenticationError("Authorization code missing from OAuth callback"));
            }

            // Send success response to browser
            await SendResponseAsync(context, 200,
                "Authentication successful! You can close this window and return to the application.");

            var result = new OAuthCallbackData
            {
                Code = code,
                State = state,
                Error = error,
                ErrorDescription = errorDescription
            };

            _logger.LogInformation("OAuth callback processed successfully");

            return Result<OAuthCallbackData>.Success(result);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error waiting for OAuth callback");
            return Result<OAuthCallbackData>.Failure(
                new NetworkError($"Error waiting for OAuth callback: {ex.Message}"));
        }
    }

    /// <summary>
    /// Stop HTTP listener and clean up resources
    /// </summary>
    public async Task<Result<bool>> StopAsync()
    {
        try
        {
            if (_httpListener != null && _isStarted)
            {
                _logger.LogDebug("Stopping HTTP listener on port {Port}", _port);

                _httpListener.Stop();
                _httpListener.Close();
                _isStarted = false;

                _logger.LogInformation("HTTP listener stopped");
            }

            return Result<bool>.Success(true);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error stopping HTTP listener");
            return Result<bool>.Failure(new NetworkError($"Failed to stop HTTP listener: {ex.Message}"));
        }
    }

    /// <summary>
    /// Dispose pattern implementation
    /// </summary>
    public async ValueTask DisposeAsync()
    {
        await StopAsync();
        _httpListener?.Close();
        GC.SuppressFinalize(this);
    }

    /// <summary>
    /// Send HTTP response to browser
    /// </summary>
    private async Task SendResponseAsync(HttpListenerContext context, int statusCode, string message)
    {
        try
        {
            var response = context.Response;
            response.StatusCode = statusCode;
            response.ContentType = "text/html; charset=utf-8";

            var html = $@"
<!DOCTYPE html>
<html>
<head>
    <title>TrashMail Panda - OAuth</title>
    <style>
        body {{ font-family: Arial, sans-serif; text-align: center; padding: 50px; }}
        .success {{ color: green; }}
        .error {{ color: red; }}
    </style>
</head>
<body>
    <h1 class=""{(statusCode == 200 ? "success" : "error")}"">{message}</h1>
</body>
</html>";

            var buffer = System.Text.Encoding.UTF8.GetBytes(html);
            response.ContentLength64 = buffer.Length;

            await response.OutputStream.WriteAsync(buffer, 0, buffer.Length);
            response.OutputStream.Close();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error sending HTTP response");
        }
    }

    /// <summary>
    /// Find an available port for HTTP listener
    /// </summary>
    private static int FindAvailablePort()
    {
        var listener = new System.Net.Sockets.TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        var port = ((IPEndPoint)listener.LocalEndpoint).Port;
        listener.Stop();
        return port;
    }
}
