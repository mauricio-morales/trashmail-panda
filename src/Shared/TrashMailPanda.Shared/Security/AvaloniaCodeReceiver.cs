using System;
using System.Collections.Specialized;
using System.Diagnostics;
using System.IO;
using System.Net;
using System.Net.Sockets;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Web;
using Google.Apis.Auth.OAuth2;
using Google.Apis.Auth.OAuth2.Requests;
using Google.Apis.Auth.OAuth2.Responses;
using Microsoft.Extensions.Logging;

namespace TrashMailPanda.Shared.Security;

/// <summary>
/// Custom code receiver that works properly with Avalonia and macOS by using platform-specific browser launching
/// </summary>
public class AvaloniaCodeReceiver : ICodeReceiver
{
    private readonly ILogger<AvaloniaCodeReceiver> _logger;
    private string _redirectUri;
    private readonly int _port;

    public AvaloniaCodeReceiver(ILogger<AvaloniaCodeReceiver> logger, int port = 0)
    {
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _port = port;
        // Don't set _redirectUri here if port is 0 - we'll set it when we know the actual port
        _redirectUri = _port == 0 ? string.Empty : $"http://127.0.0.1:{_port}/";
    }

    public string RedirectUri
    {
        get
        {
            // If redirect URI is empty (port was 0), we need to determine the actual port first
            if (string.IsNullOrEmpty(_redirectUri))
            {
                var actualPort = GetAvailablePort();
                _redirectUri = $"http://127.0.0.1:{actualPort}/";
                _logger.LogInformation("Redirect URI set to: {RedirectUri}", _redirectUri);
            }
            return _redirectUri;
        }
    }

    public async Task<AuthorizationCodeResponseUrl> ReceiveCodeAsync(AuthorizationCodeRequestUrl url, CancellationToken taskCancellationToken)
    {
        var authorizationUrl = url.Build().ToString();
        _logger.LogInformation("Starting OAuth authorization with URL: {Url}", authorizationUrl);

        using var listener = new HttpListener();

        // Use the RedirectUri property which handles port determination
        var actualRedirectUri = this.RedirectUri;
        var actualPort = int.Parse(actualRedirectUri.Split(':')[2].TrimEnd('/'));

        listener.Prefixes.Add(actualRedirectUri);

        try
        {
            listener.Start();
            _logger.LogInformation("OAuth callback server started on {RedirectUri}", actualRedirectUri);

            // The authorization URL should already have the correct redirect URI since we set it properly
            var finalAuthUrl = authorizationUrl;

            // Open browser using cross-platform method
            var browserOpened = await OpenBrowserAsync(finalAuthUrl);
            if (!browserOpened)
            {
                _logger.LogError("Failed to open browser for OAuth authorization");
                throw new InvalidOperationException("Failed to open browser for OAuth authorization");
            }

            _logger.LogInformation("Browser opened successfully for OAuth authorization");

            // Wait for the callback
            var context = await listener.GetContextAsync();
            var request = context.Request;
            var response = context.Response;

            // Send success page
            var responseString = @"
                <html>
                <head><title>Authentication Complete</title></head>
                <body>
                    <h1>Authentication Complete</h1>
                    <p>You can close this browser window and return to the application.</p>
                    <script>window.close();</script>
                </body>
                </html>";

            var buffer = Encoding.UTF8.GetBytes(responseString);
            response.ContentLength64 = buffer.Length;
            response.ContentType = "text/html";
            await response.OutputStream.WriteAsync(buffer, 0, buffer.Length);
            response.OutputStream.Close();

            // Parse the authorization response
            var query = request.Url?.Query;
            if (string.IsNullOrEmpty(query))
            {
                throw new InvalidOperationException("No query parameters received in OAuth callback");
            }

            var queryParams = HttpUtility.ParseQueryString(query);

            // Convert NameValueCollection to Dictionary
            var paramDict = new Dictionary<string, string>();
            foreach (string key in queryParams.Keys)
            {
                if (key != null)
                {
                    paramDict[key] = queryParams[key] ?? "";
                }
            }

            _logger.LogInformation("OAuth callback received with parameters");
            return new AuthorizationCodeResponseUrl(paramDict);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error during OAuth authorization flow");
            throw;
        }
        finally
        {
            try
            {
                listener.Stop();
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Error stopping OAuth callback server");
            }
        }
    }

    private async Task<bool> OpenBrowserAsync(string url)
    {
        try
        {
            _logger.LogInformation("Attempting to open browser with URL: {Url}", url.Substring(0, Math.Min(url.Length, 100)) + "...");

            if (RuntimeInformation.IsOSPlatform(OSPlatform.OSX))
            {
                // macOS
                _logger.LogInformation("Opening browser on macOS using 'open' command");
                var startInfo = new ProcessStartInfo
                {
                    FileName = "open",
                    Arguments = $"\"{url}\"",
                    UseShellExecute = false,
                    CreateNoWindow = true
                };
                using var process = Process.Start(startInfo);
                if (process != null)
                {
                    await process.WaitForExitAsync();
                    _logger.LogInformation("Browser process completed with exit code: {ExitCode}", process.ExitCode);
                    return process.ExitCode == 0;
                }
                return false;
            }
            else if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
            {
                // Windows
                _logger.LogInformation("Opening browser on Windows");
                var startInfo = new ProcessStartInfo
                {
                    FileName = url,
                    UseShellExecute = true
                };
                using var process = Process.Start(startInfo);
                return process != null;
            }
            else if (RuntimeInformation.IsOSPlatform(OSPlatform.Linux))
            {
                // Linux
                _logger.LogInformation("Opening browser on Linux using 'xdg-open' command");
                var startInfo = new ProcessStartInfo
                {
                    FileName = "xdg-open",
                    Arguments = $"\"{url}\"",
                    UseShellExecute = false,
                    CreateNoWindow = true
                };
                using var process = Process.Start(startInfo);
                if (process != null)
                {
                    await process.WaitForExitAsync();
                    return process.ExitCode == 0;
                }
                return false;
            }
            else
            {
                _logger.LogWarning("Unknown platform - attempting default browser opening");
                var startInfo = new ProcessStartInfo
                {
                    FileName = url,
                    UseShellExecute = true
                };
                using var process = Process.Start(startInfo);
                return process != null;
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Exception occurred while trying to open browser");
            return false;
        }
    }

    private static int GetAvailablePort()
    {
        var listener = new TcpListener(IPAddress.Loopback, 0);
        try
        {
            listener.Start();
            var port = ((IPEndPoint)listener.LocalEndpoint).Port;
            return port;
        }
        finally
        {
            listener.Stop();
        }
    }
}