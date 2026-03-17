using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using System;
using System.Threading.Tasks;
using TrashMailPanda.Models;
using TrashMailPanda.Services;
using TrashMailPanda.Shared.Security;
using Xunit;

namespace TrashMailPanda.Tests.Integration.OAuth;

/// <summary>
/// Integration tests for OAuth error recovery scenarios
/// These tests require manual simulation of error conditions
/// Tests are skipped by default - run manually to validate error handling
/// </summary>
public class OAuthErrorRecoveryTests
{
    private const string SkipReason = "Manual test - requires simulated failures. " +
                                     "These tests validate error messages and retry logic.";

    [Fact(Skip = SkipReason)]
    public async Task UserDeniesPermission_ShouldDisplayClearErrorMessage()
    {
        // Arrange
        var services = CreateServiceProvider();
        var oauthHandler = services.GetRequiredService<IGoogleOAuthHandler>();

        var clientId = Environment.GetEnvironmentVariable("GMAIL_CLIENT_ID");
        var clientSecret = Environment.GetEnvironmentVariable("GMAIL_CLIENT_SECRET");

        Assert.False(string.IsNullOrEmpty(clientId));
        Assert.False(string.IsNullOrEmpty(clientSecret));

        var config = new OAuthConfiguration
        {
            ClientId = clientId,
            ClientSecret = clientSecret,
            Scopes = new[] { "https://www.googleapis.com/auth/gmail.readonly" },
            RedirectUri = "http://127.0.0.1:8080/oauth/callback",
            Timeout = TimeSpan.FromMinutes(5)
        };

        // Act
        // **MANUAL STEP**: When browser opens, click "Deny" or "Cancel"
        var result = await oauthHandler.AuthenticateAsync(config);

        // Assert
        Assert.False(result.IsSuccess);
        Assert.IsType<Shared.Base.AuthenticationError>(result.Error);

        // Verify error message is user-friendly
        Assert.Contains("denied", result.Error.Message.ToLower());

        // Verify console output showed user-friendly error
        // (Visual inspection required - check for red error message)
    }

    [Fact(Skip = SkipReason)]
    public async Task AuthenticationTimeout_ShouldShowTimeoutMessage()
    {
        // Arrange
        var services = CreateServiceProvider();
        var oauthHandler = services.GetRequiredService<IGoogleOAuthHandler>();

        var clientId = Environment.GetEnvironmentVariable("GMAIL_CLIENT_ID");
        var clientSecret = Environment.GetEnvironmentVariable("GMAIL_CLIENT_SECRET");

        var config = new OAuthConfiguration
        {
            ClientId = clientId!,
            ClientSecret = clientSecret!,
            Scopes = new[] { "https://www.googleapis.com/auth/gmail.readonly" },
            RedirectUri = "http://127.0.0.1:8080/oauth/callback",
            Timeout = TimeSpan.FromSeconds(30) // Short timeout for testing
        };

        // Act
        // **MANUAL STEP**: When browser opens, wait 30+ seconds without authorizing
        var result = await oauthHandler.AuthenticateAsync(config);

        // Assert
        Assert.False(result.IsSuccess);
        Assert.IsType<Shared.Base.ProcessingError>(result.Error);

        // Verify timeout message
        Assert.Contains("timed out", result.Error.Message.ToLower());

        // Verify console showed yellow warning
        // (Visual inspection required)
    }

    [Fact(Skip = SkipReason)]
    public async Task NetworkFailure_DuringTokenExchange_ShouldShowNetworkError()
    {
        // Arrange
        var services = CreateServiceProvider();
        var oauthHandler = services.GetRequiredService<IGoogleOAuthHandler>();

        var clientId = Environment.GetEnvironmentVariable("GMAIL_CLIENT_ID");
        var clientSecret = Environment.GetEnvironmentVariable("GMAIL_CLIENT_SECRET");

        var config = new OAuthConfiguration
        {
            ClientId = clientId!,
            ClientSecret = clientSecret!,
            Scopes = new[] { "https://www.googleapis.com/auth/gmail.readonly" },
            RedirectUri = "http://127.0.0.1:8080/oauth/callback",
            Timeout = TimeSpan.FromMinutes(5)
        };

        // Act
        // **MANUAL STEP**: Disconnect network after authorizing but before token exchange completes
        // (This requires precise timing - may need to pause debugger)
        var result = await oauthHandler.AuthenticateAsync(config);

        // Assert
        if (!result.IsSuccess)
        {
            // Could be NetworkError or timeout
            Assert.True(
                result.Error is Shared.Base.NetworkError ||
                result.Error is Shared.Base.ProcessingError);

            Assert.True(
                result.Error.Message.Contains("network", StringComparison.OrdinalIgnoreCase) ||
                result.Error.Message.Contains("timeout", StringComparison.OrdinalIgnoreCase));
        }
    }

    [Fact(Skip = SkipReason)]
    public async Task InvalidRefreshToken_ShouldClearTokensAndPromptReauth()
    {
        // Arrange
        var services = CreateServiceProvider();
        var oauthHandler = services.GetRequiredService<IGoogleOAuthHandler>();
        var secureStorage = services.GetRequiredService<ISecureStorageManager>();

        // **MANUAL SETUP**: Go to https://myaccount.google.com/permissions
        // and revoke access for "TrashMail Panda" before running this test

        var clientId = Environment.GetEnvironmentVariable("GMAIL_CLIENT_ID");
        var clientSecret = Environment.GetEnvironmentVariable("GMAIL_CLIENT_SECRET");

        var config = new OAuthConfiguration
        {
            ClientId = clientId!,
            ClientSecret = clientSecret!,
            Scopes = new[] { "https://www.googleapis.com/auth/gmail.modify" },
            RedirectUri = "http://127.0.0.1:8080/oauth/callback",
            Timeout = TimeSpan.FromMinutes(5)
        };

        // Get the current (soon-to-be-invalid) refresh token
        var refreshTokenResult = await secureStorage.RetrieveCredentialAsync(
            TrashMailPanda.Providers.Email.Models.GmailStorageKeys.REFRESH_TOKEN);

        Assert.True(refreshTokenResult.IsSuccess);

        // Act
        var result = await oauthHandler.RefreshTokenAsync(refreshTokenResult.Value, config);

        // Assert
        Assert.False(result.IsSuccess);
        Assert.IsType<Shared.Base.AuthenticationError>(result.Error);
        Assert.Contains("revoked", result.Error.Message.ToLower());

        // Verify tokens were cleared
        var clearedAccessToken = await secureStorage.RetrieveCredentialAsync(
            TrashMailPanda.Providers.Email.Models.GmailStorageKeys.ACCESS_TOKEN);
        var clearedRefreshToken = await secureStorage.RetrieveCredentialAsync(
            TrashMailPanda.Providers.Email.Models.GmailStorageKeys.REFRESH_TOKEN);

        // Should be cleared
        Assert.True(!clearedAccessToken.IsSuccess || string.IsNullOrEmpty(clearedAccessToken.Value));
        Assert.True(!clearedRefreshToken.IsSuccess || string.IsNullOrEmpty(clearedRefreshToken.Value));

        // Verify console showed re-authentication prompt
        // (Visual inspection required - should see red error message)
    }

    [Fact(Skip = SkipReason)]
    public async Task BrowserLaunchFailure_ShouldShowManualUrlInstructions()
    {
        // Arrange
        var services = CreateServiceProvider();
        var oauthHandler = services.GetRequiredService<IGoogleOAuthHandler>();

        var clientId = Environment.GetEnvironmentVariable("GMAIL_CLIENT_ID");
        var clientSecret = Environment.GetEnvironmentVariable("GMAIL_CLIENT_SECRET");

        var config = new OAuthConfiguration
        {
            ClientId = clientId!,
            ClientSecret = clientSecret!,
            Scopes = new[] { "https://www.googleapis.com/auth/gmail.readonly" },
            RedirectUri = "http://127.0.0.1:8080/oauth/callback",
            Timeout = TimeSpan.FromMinutes(5)
        };

        // **MANUAL SETUP**: To simulate browser launch failure:
        // - On Windows: Temporarily rename default browser exe
        // - On macOS: Temporarily disable 'open' command (requires root)
        // - On Linux: Temporarily disable 'xdg-open' command

        // Act
        var result = await oauthHandler.AuthenticateAsync(config);

        // Assert
        // May succeed if browser fallback works
        // If it fails, should show manual URL instructions

        // Verify console displayed:
        // - Yellow warning "⚠ Manual authentication required"
        // - Full authorization URL
        // - Step-by-step instructions
        // (Visual inspection required)
    }

    [Fact(Skip = SkipReason)]
    public async Task MissingOAuthCredentials_ShouldShowConfigurationError()
    {
        // Arrange
        var services = CreateServiceProvider();
        var oauthHandler = services.GetRequiredService<IGoogleOAuthHandler>();

        // Act
        var isConfigured = await oauthHandler.IsConfiguredAsync();

        // Assert
        if (!isConfigured.Value)
        {
            // Verify console would show configuration error
            Assert.False(isConfigured.Value);

            // Verify error handler would display appropriate message
            // (Visual inspection test - run with missing credentials)
        }
    }

    [Fact(Skip = SkipReason)]
    public async Task RetryWorkflow_AfterFailure_ShouldSucceed()
    {
        // Arrange
        var services = CreateServiceProvider();
        var oauthHandler = services.GetRequiredService<IGoogleOAuthHandler>();

        var clientId = Environment.GetEnvironmentVariable("GMAIL_CLIENT_ID");
        var clientSecret = Environment.GetEnvironmentVariable("GMAIL_CLIENT_SECRET");

        var config = new OAuthConfiguration
        {
            ClientId = clientId!,
            ClientSecret = clientSecret!,
            Scopes = new[] { "https://www.googleapis.com/auth/gmail.readonly" },
            RedirectUri = "http://127.0.0.1:8080/oauth/callback",
            Timeout = TimeSpan.FromMinutes(5)
        };

        // Act - First attempt (user should deny)
        // **MANUAL STEP**: Click "Deny" when browser opens
        var firstAttempt = await oauthHandler.AuthenticateAsync(config);

        Assert.False(firstAttempt.IsSuccess);

        // Act - Retry (user should approve)
        // **MANUAL STEP**: Click "Allow" when browser opens
        var secondAttempt = await oauthHandler.AuthenticateAsync(config);

        // Assert
        Assert.True(secondAttempt.IsSuccess, $"Retry failed: {secondAttempt.Error?.Message}");
        Assert.NotNull(secondAttempt.Value);
        Assert.NotEmpty(secondAttempt.Value.AccessToken);

        // Cleanup
        await oauthHandler.ClearAuthenticationAsync();
    }

    [Fact(Skip = SkipReason)]
    public async Task ConsoleErrorDisplay_VerifyColorCoding()
    {
        // This is a visual inspection test to verify:
        // 1. Errors are displayed in RED
        // 2. Warnings are displayed in YELLOW
        // 3. Success messages are displayed in GREEN
        // 4. Info messages are displayed in CYAN
        // 5. Technical details are displayed in DIM RED

        // Run various error scenarios and visually verify console output
        // follows the Spectre.Console color scheme

        Assert.True(true, "Visual inspection test - check console colors manually");
    }

    #region Helper Methods

    private ServiceProvider CreateServiceProvider()
    {
        var services = new ServiceCollection();

        // Logging
        services.AddLogging(builder =>
        {
            builder.AddConsole();
            builder.SetMinimumLevel(LogLevel.Debug);
        });

        // OAuth services
        services.AddSingleton<ISecureStorageManager, SecureStorageManager>();
        services.AddTransient<ILocalOAuthCallbackListener, LocalOAuthCallbackListener>();
        services.AddSingleton<Func<ILocalOAuthCallbackListener>>(sp =>
            () => sp.GetRequiredService<ILocalOAuthCallbackListener>());
        services.AddSingleton<IGoogleOAuthHandler, GoogleOAuthHandler>();
        services.AddSingleton<IGoogleTokenValidator, GoogleTokenValidator>();

        return services.BuildServiceProvider();
    }

    #endregion
}
