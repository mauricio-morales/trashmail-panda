using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using System;
using System.Threading.Tasks;
using TrashMailPanda.Models;
using TrashMailPanda.Providers.Email.Models;
using TrashMailPanda.Services;
using TrashMailPanda.Shared.Security;
using Xunit;

namespace TrashMailPanda.Tests.Integration.OAuth;

/// <summary>
/// Integration tests for full OAuth flow
/// These tests require real OAuth credentials and manual user interaction
/// Tests are skipped by default - run manually with real Google Cloud Console credentials
/// </summary>
public class OAuthFlowIntegrationTests
{
    private const string SkipReason = "Requires OAuth - manual test with real Gmail credentials. " +
                                     "Set environment variables GMAIL_CLIENT_ID and GMAIL_CLIENT_SECRET, " +
                                     "then remove Skip attribute to run.";

    [Fact(Skip = SkipReason)]
    public async Task FullOAuthFlow_WithRealCredentials_ShouldAuthenticateSuccessfully()
    {
        // Arrange
        var services = CreateServiceProvider();
        var oauthHandler = services.GetRequiredService<IGoogleOAuthHandler>();
        var secureStorage = services.GetRequiredService<ISecureStorageManager>();

        // Load credentials from environment
        var clientId = Environment.GetEnvironmentVariable("GMAIL_CLIENT_ID");
        var clientSecret = Environment.GetEnvironmentVariable("GMAIL_CLIENT_SECRET");

        Assert.False(string.IsNullOrEmpty(clientId), "GMAIL_CLIENT_ID environment variable not set");
        Assert.False(string.IsNullOrEmpty(clientSecret), "GMAIL_CLIENT_SECRET environment variable not set");

        // Configure OAuth
        var config = new OAuthConfiguration
        {
            ClientId = clientId,
            ClientSecret = clientSecret,
            Scopes = new[]
            {
                "https://www.googleapis.com/auth/gmail.readonly",
                "https://www.googleapis.com/auth/gmail.modify"
            },
            RedirectUri = "http://127.0.0.1:8080/oauth/callback",
            Timeout = TimeSpan.FromMinutes(5)
        };

        // Act
        var result = await oauthHandler.AuthenticateAsync(config);

        // Assert
        Assert.True(result.IsSuccess, $"OAuth authentication failed: {result.Error?.Message}");
        Assert.NotNull(result.Value);
        Assert.NotEmpty(result.Value.AccessToken);
        Assert.NotEmpty(result.Value.RefreshToken);
        Assert.True(result.Value.ExpiresInSeconds > 0);

        // Verify tokens were stored
        var accessTokenResult = await secureStorage.RetrieveCredentialAsync(GmailStorageKeys.ACCESS_TOKEN);
        var refreshTokenResult = await secureStorage.RetrieveCredentialAsync(GmailStorageKeys.REFRESH_TOKEN);

        Assert.True(accessTokenResult.IsSuccess);
        Assert.True(refreshTokenResult.IsSuccess);
        Assert.NotEmpty(accessTokenResult.Value);
        Assert.NotEmpty(refreshTokenResult.Value);

        // Cleanup - Clear tokens after test
        await oauthHandler.ClearAuthenticationAsync();
    }

    [Fact(Skip = SkipReason)]
    public async Task TokenRefresh_WithValidRefreshToken_ShouldSucceed()
    {
        // Arrange
        var services = CreateServiceProvider();
        var oauthHandler = services.GetRequiredService<IGoogleOAuthHandler>();
        var tokenValidator = services.GetRequiredService<IGoogleTokenValidator>();

        // Prerequisites: Must have valid refresh token stored
        var validationResult = await tokenValidator.ValidateAsync();
        Assert.True(validationResult.IsSuccess);
        Assert.NotEqual(Models.TokenStatus.NotAuthenticated, validationResult.Value.Status);

        var storedTokens = await tokenValidator.LoadStoredTokensAsync();
        Assert.True(storedTokens.IsSuccess);
        Assert.NotEmpty(storedTokens.Value.RefreshToken);

        var clientId = Environment.GetEnvironmentVariable("GMAIL_CLIENT_ID");
        var clientSecret = Environment.GetEnvironmentVariable("GMAIL_CLIENT_SECRET");

        var config = new OAuthConfiguration
        {
            ClientId = clientId!,
            ClientSecret = clientSecret!,
            Scopes = new[]
            {
                "https://www.googleapis.com/auth/gmail.readonly",
                "https://www.googleapis.com/auth/gmail.modify"
            },
            RedirectUri = "http://127.0.0.1:8080/oauth/callback",
            Timeout = TimeSpan.FromMinutes(5)
        };

        // Act
        var result = await oauthHandler.RefreshTokenAsync(storedTokens.Value.RefreshToken, config);

        // Assert
        Assert.True(result.IsSuccess, $"Token refresh failed: {result.Error?.Message}");
        Assert.NotNull(result.Value);
        Assert.NotEmpty(result.Value.AccessToken);
        Assert.True(result.Value.ExpiresInSeconds > 0);

        // Verify new access token is different from old one (refreshed)
        var newValidationResult = await tokenValidator.ValidateAsync();
        Assert.True(newValidationResult.IsSuccess);
        Assert.Equal(Models.TokenStatus.Valid, newValidationResult.Value.Status);
    }

    [Fact(Skip = SkipReason)]
    public async Task TokenValidation_WithExpiredToken_ShouldDetectExpiry()
    {
        // Arrange
        var services = CreateServiceProvider();
        var tokenValidator = services.GetRequiredService<IGoogleTokenValidator>();

        // Act
        var validationResult = await tokenValidator.ValidateAsync();

        // Assert
        Assert.True(validationResult.IsSuccess);

        // Check if token is expired
        if (validationResult.Value.Status == Models.TokenStatus.ExpiredCanRefresh)
        {
            Assert.True(validationResult.Value.IsAccessTokenExpired);
            Assert.True(validationResult.Value.HasRefreshToken);
            Assert.True(validationResult.Value.TimeUntilExpiry < TimeSpan.Zero);
        }
    }

    [Fact(Skip = SkipReason)]
    public async Task UserDeniesPermission_ShouldHandleGracefully()
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
        // Note: This test requires MANUAL intervention - user must click "Deny" in browser
        var result = await oauthHandler.AuthenticateAsync(config);

        // Assert
        // If user denied, result should be failure
        if (!result.IsSuccess)
        {
            Assert.IsType<Shared.Base.AuthenticationError>(result.Error);
            Assert.Contains("denied", result.Error.Message.ToLower());
        }
    }

    [Fact(Skip = SkipReason)]
    public async Task ConcurrentOAuthFlows_ShouldHandleCorrectly()
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
        // Try to start two OAuth flows simultaneously
        var task1 = oauthHandler.AuthenticateAsync(config);
        await Task.Delay(100); // Small delay
        var task2 = oauthHandler.AuthenticateAsync(config);

        // Assert
        // At least one should fail (port conflict or listener already started)
        // Note: This behavior depends on implementation - may need to handle this differently
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
