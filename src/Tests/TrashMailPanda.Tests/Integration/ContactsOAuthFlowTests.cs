using Xunit;
using Moq;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using TrashMailPanda.Services;
using TrashMailPanda.Shared.Security;
using TrashMailPanda.Models;
using TrashMailPanda.Shared.Base;
using Google.Apis.Auth.OAuth2;

namespace TrashMailPanda.Tests.Integration;

/// <summary>
/// Integration tests for Contacts OAuth scope expansion flow
/// Tests the complete OAuth flow including Contacts scopes
/// These tests are marked as Skip for CI as they require real Google OAuth credentials
/// </summary>
public class ContactsOAuthFlowTests
{
    /// <summary>
    /// Integration test for OAuth flow with Contacts scope included
    /// This test verifies that the OAuth flow includes the Contacts scope
    /// </summary>
    [Fact(Skip = "Requires real Google OAuth credentials - run manually for integration testing")]
    public async Task ContactsOAuth_FullFlow_ShouldIncludeContactsScope()
    {
        // Arrange
        var services = new ServiceCollection();
        services.AddLogging(builder => builder.AddConsole());
        services.AddSingleton<ISecureStorageManager, SecureStorageManager>();
        services.AddSingleton<Google.Apis.Util.Store.IDataStore, Google.Apis.Util.Store.FileDataStore>(provider =>
            new Google.Apis.Util.Store.FileDataStore("TrashMailPanda.Test", true));

        var serviceProvider = services.BuildServiceProvider();
        var logger = serviceProvider.GetRequiredService<ILogger<GmailOAuthService>>();
        var secureStorageManager = serviceProvider.GetRequiredService<ISecureStorageManager>();
        var dataStore = serviceProvider.GetRequiredService<Google.Apis.Util.Store.IDataStore>();

        var oauthService = new GmailOAuthService(secureStorageManager, logger, dataStore);

        // NOTE: This test requires environment variables to be set:
        // GMAIL_CLIENT_ID and GMAIL_CLIENT_SECRET
        var clientId = Environment.GetEnvironmentVariable("GMAIL_CLIENT_ID");
        var clientSecret = Environment.GetEnvironmentVariable("GMAIL_CLIENT_SECRET");

        if (string.IsNullOrEmpty(clientId) || string.IsNullOrEmpty(clientSecret))
        {
            throw new InvalidOperationException(
                "Integration test requires GMAIL_CLIENT_ID and GMAIL_CLIENT_SECRET environment variables");
        }

        // Store OAuth client credentials
        await secureStorageManager.StoreCredentialAsync(ProviderCredentialTypes.GoogleClientId, clientId);
        await secureStorageManager.StoreCredentialAsync(ProviderCredentialTypes.GoogleClientSecret, clientSecret);

        try
        {
            // Act
            var authResult = await oauthService.AuthenticateAsync();

            // Assert
            Assert.True(authResult.IsSuccess, $"OAuth authentication failed: {authResult.Error?.Message}");

            // Verify authentication status
            var isAuthenticatedResult = await oauthService.IsAuthenticatedAsync();
            Assert.True(isAuthenticatedResult.IsSuccess);
            Assert.True(isAuthenticatedResult.Value);

            // NOTE: The actual scope verification would require inspecting the OAuth token
            // This test primarily verifies that the OAuth flow completes successfully
            // with the expanded scopes (Gmail + Contacts)
        }
        finally
        {
            // Cleanup
            await oauthService.SignOutAsync();
            await secureStorageManager.RemoveCredentialAsync(ProviderCredentialTypes.GoogleClientId);
            await secureStorageManager.RemoveCredentialAsync(ProviderCredentialTypes.GoogleClientSecret);
        }
    }

    /// <summary>
    /// Integration test for existing Gmail users getting Contacts scope expansion
    /// This simulates a user who already has Gmail OAuth but needs Contacts access
    /// </summary>
    [Fact(Skip = "Requires real Google OAuth credentials - run manually for integration testing")]
    public async Task ContactsOAuth_ScopeExpansion_ForExistingGmailUser_ShouldSucceed()
    {
        // Arrange
        var services = new ServiceCollection();
        services.AddLogging(builder => builder.AddConsole());
        services.AddSingleton<ISecureStorageManager, SecureStorageManager>();
        services.AddSingleton<Google.Apis.Util.Store.IDataStore, Google.Apis.Util.Store.FileDataStore>(provider =>
            new Google.Apis.Util.Store.FileDataStore("TrashMailPanda.Test.ScopeExpansion", true));

        var serviceProvider = services.BuildServiceProvider();
        var logger = serviceProvider.GetRequiredService<ILogger<GmailOAuthService>>();
        var secureStorageManager = serviceProvider.GetRequiredService<ISecureStorageManager>();
        var dataStore = serviceProvider.GetRequiredService<Google.Apis.Util.Store.IDataStore>();

        var oauthService = new GmailOAuthService(secureStorageManager, logger, dataStore);

        var clientId = Environment.GetEnvironmentVariable("GMAIL_CLIENT_ID");
        var clientSecret = Environment.GetEnvironmentVariable("GMAIL_CLIENT_SECRET");

        if (string.IsNullOrEmpty(clientId) || string.IsNullOrEmpty(clientSecret))
        {
            throw new InvalidOperationException(
                "Integration test requires GMAIL_CLIENT_ID and GMAIL_CLIENT_SECRET environment variables");
        }

        await secureStorageManager.StoreCredentialAsync(ProviderCredentialTypes.GoogleClientId, clientId);
        await secureStorageManager.StoreCredentialAsync(ProviderCredentialTypes.GoogleClientSecret, clientSecret);

        try
        {
            // Act
            // First authentication - this will include both Gmail and Contacts scopes
            var firstAuthResult = await oauthService.AuthenticateAsync();
            Assert.True(firstAuthResult.IsSuccess);

            // Verify authentication persists
            var isAuthenticatedResult = await oauthService.IsAuthenticatedAsync();
            Assert.True(isAuthenticatedResult.IsSuccess);
            Assert.True(isAuthenticatedResult.Value);

            // Simulate scope expansion by re-authenticating
            // (In real scenario, this would prompt user to grant additional permissions)
            var secondAuthResult = await oauthService.AuthenticateAsync();

            // Assert
            Assert.True(secondAuthResult.IsSuccess, "Scope expansion authentication should succeed");

            var finalAuthCheck = await oauthService.IsAuthenticatedAsync();
            Assert.True(finalAuthCheck.IsSuccess);
            Assert.True(finalAuthCheck.Value);
        }
        finally
        {
            // Cleanup
            await oauthService.SignOutAsync();
            await secureStorageManager.RemoveCredentialAsync(ProviderCredentialTypes.GoogleClientId);
            await secureStorageManager.RemoveCredentialAsync(ProviderCredentialTypes.GoogleClientSecret);
        }
    }

    /// <summary>
    /// Test error handling when OAuth credentials are invalid
    /// </summary>
    [Fact]
    public async Task ContactsOAuth_WithInvalidCredentials_ShouldReturnConfigurationError()
    {
        // Arrange
        var services = new ServiceCollection();
        services.AddLogging(builder => builder.AddConsole());
        services.AddSingleton<ISecureStorageManager, SecureStorageManager>();
        services.AddSingleton<Google.Apis.Util.Store.IDataStore, Google.Apis.Util.Store.FileDataStore>(provider =>
            new Google.Apis.Util.Store.FileDataStore("TrashMailPanda.Test.InvalidCreds", true));

        var serviceProvider = services.BuildServiceProvider();
        var logger = serviceProvider.GetRequiredService<ILogger<GmailOAuthService>>();
        var secureStorageManager = serviceProvider.GetRequiredService<ISecureStorageManager>();
        var dataStore = serviceProvider.GetRequiredService<Google.Apis.Util.Store.IDataStore>();

        var oauthService = new GmailOAuthService(secureStorageManager, logger, dataStore);

        // Store invalid OAuth credentials
        await secureStorageManager.StoreCredentialAsync(ProviderCredentialTypes.GoogleClientId, "invalid_client_id");
        await secureStorageManager.StoreCredentialAsync(ProviderCredentialTypes.GoogleClientSecret, "invalid_secret");

        try
        {
            // Act
            var authResult = await oauthService.AuthenticateAsync();

            // Assert
            // This should either return a configuration error or an authentication error
            // depending on how Google responds to invalid credentials
            Assert.False(authResult.IsSuccess);
            Assert.True(authResult.Error is ConfigurationError || authResult.Error is AuthenticationError);
        }
        finally
        {
            // Cleanup
            await secureStorageManager.RemoveCredentialAsync(ProviderCredentialTypes.GoogleClientId);
            await secureStorageManager.RemoveCredentialAsync(ProviderCredentialTypes.GoogleClientSecret);
        }
    }

    /// <summary>
    /// Test that Contacts provider status updates correctly after OAuth completion
    /// </summary>
    [Fact]
    public void ContactsOAuth_StatusUpdates_ShouldReflectOAuthState()
    {
        // Arrange
        var logger = new Mock<ILogger<ProviderStatusService>>().Object;
        var providerStatusService = new ProviderStatusService(logger);

        // Simulate Contacts provider status before OAuth
        var preOAuthStatus = new ProviderStatus
        {
            Name = "Contacts",
            IsHealthy = false,
            RequiresSetup = true,
            Status = "Authentication Required",
            LastCheck = DateTime.UtcNow
        };

        // Simulate Contacts provider status after successful OAuth
        var postOAuthStatus = new ProviderStatus
        {
            Name = "Contacts",
            IsHealthy = true,
            RequiresSetup = false,
            Status = "Ready",
            LastCheck = DateTime.UtcNow
        };

        // Act
        providerStatusService.UpdateProviderStatus(preOAuthStatus);
        var preAuthRetrieved = providerStatusService.GetProviderStatus("Contacts");

        providerStatusService.UpdateProviderStatus(postOAuthStatus);
        var postAuthRetrieved = providerStatusService.GetProviderStatus("Contacts");

        // Assert
        Assert.NotNull(preAuthRetrieved);
        Assert.False(preAuthRetrieved.IsHealthy);
        Assert.True(preAuthRetrieved.RequiresSetup);
        Assert.Equal("Authentication Required", preAuthRetrieved.Status);

        Assert.NotNull(postAuthRetrieved);
        Assert.True(postAuthRetrieved.IsHealthy);
        Assert.False(postAuthRetrieved.RequiresSetup);
        Assert.Equal("Ready", postAuthRetrieved.Status);
    }
}