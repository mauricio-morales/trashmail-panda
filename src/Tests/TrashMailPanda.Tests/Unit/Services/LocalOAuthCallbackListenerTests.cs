using Microsoft.Extensions.Logging;
using Moq;
using System;
using System.Net;
using System.Threading;
using System.Threading.Tasks;
using TrashMailPanda.Models;
using TrashMailPanda.Services;
using TrashMailPanda.Shared.Base;
using Xunit;

namespace TrashMailPanda.Tests.Unit.Services;

/// <summary>
/// Unit tests for LocalOAuthCallbackListener
/// Tests cover HTTP listener startup, port allocation, query parameter parsing, and timeout handling
/// </summary>
public class LocalOAuthCallbackListenerTests
{
    private readonly Mock<ILogger<LocalOAuthCallbackListener>> _mockLogger;

    public LocalOAuthCallbackListenerTests()
    {
        _mockLogger = new Mock<ILogger<LocalOAuthCallbackListener>>();
    }

    #region Startup Tests

    [Fact]
    public async Task StartAsync_ShouldReturnSuccessWithPort()
    {
        // Arrange
        await using var listener = new LocalOAuthCallbackListener(_mockLogger.Object);

        // Act
        var result = await listener.StartAsync("/oauth/callback");

        // Assert
        Assert.True(result.IsSuccess);
        Assert.True(result.Value > 0);
        Assert.InRange(result.Value, 1024, 65535); // Valid port range

        // Cleanup
        await listener.StopAsync();
    }

    [Fact]
    public async Task StartAsync_CalledTwice_ShouldReturnFailure()
    {
        // Arrange
        await using var listener = new LocalOAuthCallbackListener(_mockLogger.Object);

        // Act
        var firstResult = await listener.StartAsync("/oauth/callback");
        var secondResult = await listener.StartAsync("/oauth/callback");

        // Assert
        Assert.True(firstResult.IsSuccess);
        Assert.False(secondResult.IsSuccess);
        Assert.IsType<ConfigurationError>(secondResult.Error);

        // Cleanup
        await listener.StopAsync();
    }

    [Fact]
    public async Task StartAsync_WithCustomCallbackPath_ShouldSucceed()
    {
        // Arrange
        await using var listener = new LocalOAuthCallbackListener(_mockLogger.Object);

        // Act
        var result = await listener.StartAsync("/custom/auth/path");

        // Assert
        Assert.True(result.IsSuccess);
        Assert.True(result.Value > 0);

        // Cleanup
        await listener.StopAsync();
    }

    #endregion

    #region GetRedirectUri Tests

    [Fact]
    public async Task GetRedirectUri_AfterStart_ShouldReturnValidUri()
    {
        // Arrange
        await using var listener = new LocalOAuthCallbackListener(_mockLogger.Object);
        var startResult = await listener.StartAsync("/oauth/callback");

        // Act
        var redirectUri = listener.GetRedirectUri("/oauth/callback");

        // Assert
        Assert.NotNull(redirectUri);
        Assert.StartsWith("http://127.0.0.1:", redirectUri);
        Assert.EndsWith("/oauth/callback", redirectUri);
        Assert.Contains(startResult.Value.ToString(), redirectUri);

        // Cleanup
        await listener.StopAsync();
    }

    [Fact]
    public async Task GetRedirectUri_BeforeStart_ShouldThrowException()
    {
        // Arrange
        await using var listener = new LocalOAuthCallbackListener(_mockLogger.Object);

        // Act & Assert
        Assert.Throws<InvalidOperationException>(() => listener.GetRedirectUri("/oauth/callback"));
    }

    [Fact]
    public async Task GetRedirectUri_WithDifferentPath_ShouldReturnCorrectUri()
    {
        // Arrange
        await using var listener = new LocalOAuthCallbackListener(_mockLogger.Object);
        await listener.StartAsync("/auth");

        // Act
        var redirectUri = listener.GetRedirectUri("/auth");

        // Assert
        Assert.EndsWith("/auth", redirectUri);

        // Cleanup
        await listener.StopAsync();
    }

    #endregion

    #region WaitForCallback Tests

    [Fact]
    public async Task WaitForCallbackAsync_BeforeStart_ShouldReturnFailure()
    {
        // Arrange
        await using var listener = new LocalOAuthCallbackListener(_mockLogger.Object);

        // Act
        var result = await listener.WaitForCallbackAsync("test-state", TimeSpan.FromSeconds(1));

        // Assert
        Assert.False(result.IsSuccess);
        Assert.IsType<ConfigurationError>(result.Error);
    }

    [Fact]
    public async Task WaitForCallbackAsync_WithTimeout_ShouldReturnTimeoutError()
    {
        // Arrange
        await using var listener = new LocalOAuthCallbackListener(_mockLogger.Object);
        await listener.StartAsync("/oauth/callback");

        // Act
        var result = await listener.WaitForCallbackAsync("test-state", TimeSpan.FromMilliseconds(100));

        // Assert
        Assert.False(result.IsSuccess);
        Assert.IsType<ProcessingError>(result.Error);
        Assert.Contains("timed out", result.Error.Message);

        // Cleanup
        await listener.StopAsync();
    }

    [Fact]
    public async Task WaitForCallbackAsync_WithValidCallback_ShouldParseCodeAndState()
    {
        // Arrange
        await using var listener = new LocalOAuthCallbackListener(_mockLogger.Object);
        var startResult = await listener.StartAsync("/oauth/callback");
        var redirectUri = listener.GetRedirectUri("/oauth/callback");

        var expectedState = Guid.NewGuid().ToString("N");
        var authCode = "test-auth-code-123";

        // Act - Simulate callback in background
        var callbackTask = listener.WaitForCallbackAsync(expectedState, TimeSpan.FromSeconds(10));

        // Simulate HTTP request
        using var client = new HttpClient();
        var callbackUrl = $"{redirectUri}?code={authCode}&state={expectedState}";

        await Task.Delay(100); // Give listener time to start waiting
        _ = client.GetAsync(callbackUrl); // Fire and forget

        var result = await callbackTask;

        // Assert
        Assert.True(result.IsSuccess);
        Assert.NotNull(result.Value);
        Assert.Equal(authCode, result.Value.Code);
        Assert.Equal(expectedState, result.Value.State);
        Assert.True(result.Value.IsValid);
        Assert.False(result.Value.IsError);

        // Cleanup
        await listener.StopAsync();
    }

    [Fact]
    public async Task WaitForCallbackAsync_WithErrorCallback_ShouldParseError()
    {
        // Arrange
        await using var listener = new LocalOAuthCallbackListener(_mockLogger.Object);
        var startResult = await listener.StartAsync("/oauth/callback");
        var redirectUri = listener.GetRedirectUri("/oauth/callback");

        var expectedState = Guid.NewGuid().ToString("N");
        var error = "access_denied";
        var errorDescription = "User denied access";

        // Act - Simulate callback in background
        var callbackTask = listener.WaitForCallbackAsync(expectedState, TimeSpan.FromSeconds(10));

        // Simulate HTTP request with error
        using var client = new HttpClient();
        var callbackUrl = $"{redirectUri}?error={error}&error_description={Uri.EscapeDataString(errorDescription)}&state={expectedState}";

        await Task.Delay(100); // Give listener time to start waiting
        _ = client.GetAsync(callbackUrl); // Fire and forget

        var result = await callbackTask;

        // Assert
        Assert.True(result.IsSuccess);
        Assert.NotNull(result.Value);
        Assert.True(result.Value.IsError);
        Assert.Equal(error, result.Value.Error);
        Assert.Equal(errorDescription, result.Value.ErrorDescription);

        // Cleanup
        await listener.StopAsync();
    }

    [Fact]
    public async Task WaitForCallbackAsync_WithStateMismatch_ShouldReturnFailure()
    {
        // Arrange
        await using var listener = new LocalOAuthCallbackListener(_mockLogger.Object);
        var startResult = await listener.StartAsync("/oauth/callback");
        var redirectUri = listener.GetRedirectUri("/oauth/callback");

        var expectedState = "expected-state";
        var receivedState = "wrong-state";
        var authCode = "test-auth-code";

        // Act - Simulate callback in background
        var callbackTask = listener.WaitForCallbackAsync(expectedState, TimeSpan.FromSeconds(10));

        // Simulate HTTP request with wrong state
        using var client = new HttpClient();
        var callbackUrl = $"{redirectUri}?code={authCode}&state={receivedState}";

        await Task.Delay(100); // Give listener time to start waiting
        _ = client.GetAsync(callbackUrl); // Fire and forget

        var result = await callbackTask;

        // Assert
        Assert.False(result.IsSuccess);
        Assert.IsType<AuthenticationError>(result.Error);
        Assert.Contains("state parameter mismatch", result.Error.Message);

        // Cleanup
        await listener.StopAsync();
    }

    [Fact]
    public async Task WaitForCallbackAsync_WithMissingCode_ShouldReturnFailure()
    {
        // Arrange
        await using var listener = new LocalOAuthCallbackListener(_mockLogger.Object);
        var startResult = await listener.StartAsync("/oauth/callback");
        var redirectUri = listener.GetRedirectUri("/oauth/callback");

        var expectedState = Guid.NewGuid().ToString("N");

        // Act - Simulate callback in background
        var callbackTask = listener.WaitForCallbackAsync(expectedState, TimeSpan.FromSeconds(10));

        // Simulate HTTP request without code
        using var client = new HttpClient();
        var callbackUrl = $"{redirectUri}?state={expectedState}";

        await Task.Delay(100); // Give listener time to start waiting
        _ = client.GetAsync(callbackUrl); // Fire and forget

        var result = await callbackTask;

        // Assert
        Assert.False(result.IsSuccess);
        Assert.IsType<AuthenticationError>(result.Error);
        Assert.Contains("code missing", result.Error.Message);

        // Cleanup
        await listener.StopAsync();
    }

    #endregion

    #region Stop Tests

    [Fact]
    public async Task StopAsync_AfterStart_ShouldSucceed()
    {
        // Arrange
        await using var listener = new LocalOAuthCallbackListener(_mockLogger.Object);
        await listener.StartAsync("/oauth/callback");

        // Act
        var result = await listener.StopAsync();

        // Assert
        Assert.True(result.IsSuccess);
        Assert.True(result.Value);
    }

    [Fact]
    public async Task StopAsync_BeforeStart_ShouldSucceed()
    {
        // Arrange
        await using var listener = new LocalOAuthCallbackListener(_mockLogger.Object);

        // Act
        var result = await listener.StopAsync();

        // Assert
        Assert.True(result.IsSuccess);
        Assert.True(result.Value);
    }

    [Fact]
    public async Task StopAsync_CalledMultipleTimes_ShouldSucceed()
    {
        // Arrange
        await using var listener = new LocalOAuthCallbackListener(_mockLogger.Object);
        await listener.StartAsync("/oauth/callback");

        // Act
        var result1 = await listener.StopAsync();
        var result2 = await listener.StopAsync();
        var result3 = await listener.StopAsync();

        // Assert
        Assert.True(result1.IsSuccess);
        Assert.True(result2.IsSuccess);
        Assert.True(result3.IsSuccess);
    }

    #endregion

    #region Disposal Tests

    [Fact]
    public async Task DisposeAsync_ShouldStopListener()
    {
        // Arrange
        var listener = new LocalOAuthCallbackListener(_mockLogger.Object);
        await listener.StartAsync("/oauth/callback");

        // Act
        await listener.DisposeAsync();

        // Assert
        // Should not throw
        Assert.Throws<InvalidOperationException>(() => listener.GetRedirectUri("/oauth/callback"));
    }

    #endregion
}
