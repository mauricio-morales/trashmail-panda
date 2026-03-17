using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using System;
using System.Threading;
using System.Threading.Tasks;
using TrashMailPanda.Models;
using TrashMailPanda.Providers.Email.Models;
using TrashMailPanda.Services;
using TrashMailPanda.Shared.Security;
using Xunit;

namespace TrashMailPanda.Tests.Integration.OAuth;

/// <summary>
/// Integration tests for OAuth token refresh scenarios
/// These tests require valid stored refresh tokens from prior authentication
/// Tests are skipped by default - run manually after initial OAuth setup
/// </summary>
public class TokenRefreshIntegrationTests
{
    private const string SkipReason = "Requires OAuth - manual test with expired token. " +
                                     "Prerequisites: Valid refresh token stored from prior authentication. " +
                                     "Set GMAIL_CLIENT_ID and GMAIL_CLIENT_SECRET environment variables.";

    [Fact(Skip = SkipReason)]
    public async Task AutoRefresh_WithExpiredAccessToken_ShouldRefreshSuccessfully()
    {
        // Arrange
        var services = CreateServiceProvider();
        var tokenValidator = services.GetRequiredService<IGoogleTokenValidator>();
        var oauthHandler = services.GetRequiredService<IGoogleOAuthHandler>();

        // Verify token is expired
        var validationResult = await tokenValidator.ValidateAsync();
        Assert.True(validationResult.IsSuccess);

        if (validationResult.Value.Status == TokenStatus.Valid)
        {
            // Wait for token to expire (or manually expire it for testing)
            Assert.Fail("Test requires expired access token. Wait for token expiration or manually set expired token.");
        }

        Assert.Equal(TokenStatus.ExpiredCanRefresh, validationResult.Value.Status);
        Assert.True(validationResult.Value.CanAutoRefresh);

        // Load stored tokens
        var tokensResult = await tokenValidator.LoadStoredTokensAsync();
        Assert.True(tokensResult.IsSuccess);
        Assert.NotEmpty(tokensResult.Value.RefreshToken);

        var clientId = Environment.GetEnvironmentVariable("GMAIL_CLIENT_ID");
        var clientSecret = Environment.GetEnvironmentVariable("GMAIL_CLIENT_SECRET");

        Assert.False(string.IsNullOrEmpty(clientId));
        Assert.False(string.IsNullOrEmpty(clientSecret));

        var config = new OAuthConfiguration
        {
            ClientId = clientId,
            ClientSecret = clientSecret,
            Scopes = new[] { "https://www.googleapis.com/auth/gmail.modify" },
            RedirectUri = "http://127.0.0.1:8080/oauth/callback",
            Timeout = TimeSpan.FromMinutes(5)
        };

        // Act
        var refreshResult = await oauthHandler.RefreshTokenAsync(tokensResult.Value.RefreshToken, config);

        // Assert
        Assert.True(refreshResult.IsSuccess, $"Token refresh failed: {refreshResult.Error?.Message}");
        Assert.NotNull(refreshResult.Value);
        Assert.NotEmpty(refreshResult.Value.AccessToken);
        Assert.True(refreshResult.Value.ExpiresInSeconds > 0);

        // Verify new token is valid
        var newValidation = await tokenValidator.ValidateAsync();
        Assert.True(newValidation.IsSuccess);
        Assert.Equal(TokenStatus.Valid, newValidation.Value.Status);
        Assert.False(newValidation.Value.IsAccessTokenExpired);
    }

    [Fact(Skip = SkipReason)]
    public async Task RefreshWorkflow_FullCycle_ShouldWork()
    {
        // Arrange
        var services = CreateServiceProvider();
        var tokenValidator = services.GetRequiredService<IGoogleTokenValidator>();
        var oauthHandler = services.GetRequiredService<IGoogleOAuthHandler>();

        // Step 1: Check current token status
        var initialValidation = await tokenValidator.ValidateAsync();
        Assert.True(initialValidation.IsSuccess);

        if (initialValidation.Value.Status == TokenStatus.NotAuthenticated)
        {
            Assert.Fail("Test requires prior authentication. Run full OAuth flow first.");
        }

        // Step 2: Check if auto-refresh is possible
        var canAutoRefresh = await tokenValidator.CanAutoRefreshAsync();
        Assert.True(canAutoRefresh.IsSuccess);

        if (!canAutoRefresh.Value)
        {
            Assert.Fail("Cannot auto-refresh - token might be valid or refresh token missing.");
        }

        // Step 3: Load tokens and refresh
        var tokensResult = await tokenValidator.LoadStoredTokensAsync();
        Assert.True(tokensResult.IsSuccess);

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

        // Act
        var refreshResult = await oauthHandler.RefreshTokenAsync(tokensResult.Value.RefreshToken, config);

        // Assert
        Assert.True(refreshResult.IsSuccess);

        // Verify refresh cleared the auto-refresh condition
        var finalValidation = await tokenValidator.ValidateAsync();
        Assert.True(finalValidation.IsSuccess);
        Assert.Equal(TokenStatus.Valid, finalValidation.Value.Status);

        var finalCanAutoRefresh = await tokenValidator.CanAutoRefreshAsync();
        Assert.True(finalCanAutoRefresh.IsSuccess);
        Assert.False(finalCanAutoRefresh.Value); // Should be false now - token is fresh
    }

    [Fact(Skip = SkipReason)]
    public async Task RefreshWithRevokedToken_ShouldClearTokensAndFail()
    {
        // Arrange
        var services = CreateServiceProvider();
        var oauthHandler = services.GetRequiredService<IGoogleOAuthHandler>();
        var secureStorage = services.GetRequiredService<ISecureStorageManager>();

        // Note: This test requires manually revoking the refresh token via Google Account settings
        // Go to: https://myaccount.google.com/permissions
        // Revoke access for "TrashMail Panda"

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

        // Use a known revoked token (or revoke before running test)
        var revokedToken = "known-revoked-refresh-token";

        // Act
        var refreshResult = await oauthHandler.RefreshTokenAsync(revokedToken, config);

        // Assert
        Assert.False(refreshResult.IsSuccess);
        Assert.IsType<Shared.Base.AuthenticationError>(refreshResult.Error);
        Assert.Contains("revoked", refreshResult.Error.Message.ToLower());

        // Verify tokens were cleared
        var accessTokenResult = await secureStorage.RetrieveCredentialAsync(GmailStorageKeys.ACCESS_TOKEN);
        var refreshTokenResult = await secureStorage.RetrieveCredentialAsync(GmailStorageKeys.REFRESH_TOKEN);

        // Should be cleared or missing
        Assert.True(!accessTokenResult.IsSuccess || string.IsNullOrEmpty(accessTokenResult.Value));
        Assert.True(!refreshTokenResult.IsSuccess || string.IsNullOrEmpty(refreshTokenResult.Value));
    }

    [Fact(Skip = SkipReason)]
    public async Task RefreshTimeout_ShouldReturnError()
    {
        // Arrange
        var services = CreateServiceProvider();
        var tokenValidator = services.GetRequiredService<IGoogleTokenValidator>();
        var oauthHandler = services.GetRequiredService<IGoogleOAuthHandler>();

        var tokensResult = await tokenValidator.LoadStoredTokensAsync();
        Assert.True(tokensResult.IsSuccess);

        var clientId = Environment.GetEnvironmentVariable("GMAIL_CLIENT_ID");
        var clientSecret = Environment.GetEnvironmentVariable("GMAIL_CLIENT_SECRET");

        var config = new OAuthConfiguration
        {
            ClientId = clientId!,
            ClientSecret = clientSecret!,
            Scopes = new[] { "https://www.googleapis.com/auth/gmail.modify" },
            RedirectUri = "http://127.0.0.1:8080/oauth/callback",
            Timeout = TimeSpan.FromMilliseconds(1) // Very short timeout to force timeout
        };

        // Use cancellation token with ultra-short timeout
        using var cts = new CancellationTokenSource(TimeSpan.FromMilliseconds(10));

        // Act
        var refreshResult = await oauthHandler.RefreshTokenAsync(tokensResult.Value.RefreshToken, config, cts.Token);

        // Assert
        Assert.False(refreshResult.IsSuccess);
        // Might be timeout or cancellation error
    }

    [Fact(Skip = SkipReason)]
    public async Task MultipleRefreshCalls_ShouldHandleCorrectly()
    {
        // Arrange
        var services = CreateServiceProvider();
        var tokenValidator = services.GetRequiredService<IGoogleTokenValidator>();
        var oauthHandler = services.GetRequiredService<IGoogleOAuthHandler>();

        var tokensResult = await tokenValidator.LoadStoredTokensAsync();
        Assert.True(tokensResult.IsSuccess);

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

        // Act - Refresh twice in succession
        var firstRefresh = await oauthHandler.RefreshTokenAsync(tokensResult.Value.RefreshToken, config);
        Assert.True(firstRefresh.IsSuccess);

        // Load the new refresh token
        var updatedTokens = await tokenValidator.LoadStoredTokensAsync();
        Assert.True(updatedTokens.IsSuccess);

        // Refresh again with the new refresh token
        var secondRefresh = await oauthHandler.RefreshTokenAsync(updatedTokens.Value.RefreshToken, config);

        // Assert
        Assert.True(secondRefresh.IsSuccess);

        // Both should produce valid access tokens
        Assert.NotEmpty(firstRefresh.Value.AccessToken);
        Assert.NotEmpty(secondRefresh.Value.AccessToken);

        // Access tokens should be different (new refresh generated new token)
        Assert.NotEqual(firstRefresh.Value.AccessToken, secondRefresh.Value.AccessToken);
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
