using System;
using System.ComponentModel;
using System.Net;
using System.Net.Http;
using System.Net.Sockets;
using TrashMailPanda.Services;
using TrashMailPanda.Shared.Base;
using Xunit;

namespace TrashMailPanda.Tests.Unit.Services;

/// <summary>
/// Unit tests for OAuthErrorHandler
/// Tests cover error mapping logic for various exception types
/// Note: Display methods are tested via visual inspection in integration tests
/// </summary>
public class OAuthErrorHandlerTests
{
    #region Exception Mapping Tests

    [Fact]
    public void MapException_HttpRequestException_ShouldMapToNetworkError()
    {
        // Arrange
        var exception = new HttpRequestException("Connection failed");

        // Act
        var (userMessage, technicalDetails, isRetryable) = InvokeMapException(exception);

        // Assert
        Assert.Contains("Network connection failed", userMessage);
        Assert.Contains("internet connection", userMessage);
        Assert.Contains("HTTP error", technicalDetails);
        Assert.True(isRetryable);
    }

    [Fact]
    public void MapException_SocketException_ShouldMapToNetworkError()
    {
        // Arrange
        var exception = new SocketException((int)SocketError.HostNotFound);

        // Act
        var (userMessage, technicalDetails, isRetryable) = InvokeMapException(exception);

        // Assert
        Assert.Contains("Network connection failed", userMessage);
        Assert.Contains("firewall", userMessage);
        Assert.Contains("Socket error", technicalDetails);
        Assert.True(isRetryable);
    }

    [Fact]
    public void MapException_TimeoutException_ShouldMapToTimeout()
    {
        // Arrange
        var exception = new TimeoutException("Operation timed out");

        // Act
        var (userMessage, technicalDetails, isRetryable) = InvokeMapException(exception);

        // Assert
        Assert.Contains("timed out", userMessage);
        Assert.Contains("try again", userMessage);
        Assert.Contains("timeout", technicalDetails);
        Assert.True(isRetryable);
    }

    [Fact]
    public void MapException_Win32Exception_ShouldMapToBrowserLaunchError()
    {
        // Arrange
        var exception = new Win32Exception(2, "File not found"); // ERROR_FILE_NOT_FOUND

        // Act
        var (userMessage, technicalDetails, isRetryable) = InvokeMapException(exception);

        // Assert
        Assert.Contains("browser", userMessage);
        Assert.Contains("manually", userMessage);
        Assert.Contains("System error", technicalDetails);
        Assert.True(isRetryable);
    }

    [Fact]
    public void MapException_InvalidOperationException_ShouldMapToInvalidState()
    {
        // Arrange
        var exception = new InvalidOperationException("Invalid state");

        // Act
        var (userMessage, technicalDetails, isRetryable) = InvokeMapException(exception);

        // Assert
        Assert.Contains("invalid state", userMessage);
        Assert.Contains("restart", userMessage);
        Assert.Contains("Invalid operation", technicalDetails);
        Assert.True(isRetryable);
    }

    [Fact]
    public void MapException_GenericException_ShouldMapToGenericError()
    {
        // Arrange
        var exception = new ArgumentException("Invalid argument");

        // Act
        var (userMessage, technicalDetails, isRetryable) = InvokeMapException(exception);

        // Assert
        Assert.Contains("unexpected error", userMessage);
        Assert.Contains("ArgumentException", technicalDetails);
        Assert.True(isRetryable);
    }

    #endregion

    #region Result Error Mapping Tests

    [Fact]
    public void MapError_AuthenticationErrorWithDenied_ShouldMapToUserDenial()
    {
        // Arrange
        var error = new AuthenticationError("User denied the request");

        // Act
        var (userMessage, technicalDetails, isRetryable) = InvokeMapError(error);

        // Assert
        Assert.Contains("denied", userMessage);
        Assert.Contains("authorize", userMessage);
        Assert.Contains("Authentication error", technicalDetails);
        Assert.True(isRetryable);
    }

    [Fact]
    public void MapError_AuthenticationErrorWithRevoked_ShouldMapToRevokedToken()
    {
        // Arrange
        var error = new AuthenticationError("Token has been revoked");

        // Act
        var (userMessage, technicalDetails, isRetryable) = InvokeMapError(error);

        // Assert
        Assert.Contains("expired", userMessage);
        Assert.Contains("sign in again", userMessage);
        Assert.Contains("Authentication error", technicalDetails);
        Assert.False(isRetryable); // Revoked tokens cannot be retried
    }

    [Fact]
    public void MapError_AuthenticationErrorGeneric_ShouldMapToGenericAuth()
    {
        // Arrange
        var error = new AuthenticationError("Authentication failed");

        // Act
        var (userMessage, technicalDetails, isRetryable) = InvokeMapError(error);

        // Assert
        Assert.Contains("Authentication failed", userMessage);
        Assert.True(isRetryable);
    }

    [Fact]
    public void MapError_NetworkErrorWithTimeout_ShouldMapToTimeout()
    {
        // Arrange
        var error = new NetworkError("Request timeout occurred");

        // Act
        var (userMessage, technicalDetails, isRetryable) = InvokeMapError(error);

        // Assert
        Assert.Contains("timed out", userMessage);
        Assert.Contains("try again", userMessage);
        Assert.True(isRetryable);
    }

    [Fact]
    public void MapError_NetworkErrorWithBrowser_ShouldMapToBrowserError()
    {
        // Arrange
        var error = new NetworkError("Browser launch failed");

        // Act
        var (userMessage, technicalDetails, isRetryable) = InvokeMapError(error);

        // Assert
        Assert.Contains("browser", userMessage);
        Assert.Contains("manually", userMessage);
        Assert.True(isRetryable);
    }

    [Fact]
    public void MapError_NetworkErrorGeneric_ShouldMapToGenericNetwork()
    {
        // Arrange
        var error = new NetworkError("Connection failed");

        // Act
        var (userMessage, technicalDetails, isRetryable) = InvokeMapError(error);

        // Assert
        Assert.Contains("Network error", userMessage);
        Assert.True(isRetryable);
    }

    [Fact]
    public void MapError_ConfigurationErrorWithClient_ShouldMapToOAuthNotConfigured()
    {
        // Arrange
        var error = new ConfigurationError("Client ID not configured");

        // Act
        var (userMessage, technicalDetails, isRetryable) = InvokeMapError(error);

        // Assert
        Assert.Contains("not configured", userMessage);
        Assert.Contains("credentials", userMessage);
        Assert.False(isRetryable); // Configuration errors require manual fix
    }

    [Fact]
    public void MapError_ConfigurationErrorGeneric_ShouldMapToGenericConfig()
    {
        // Arrange
        var error = new ConfigurationError("Invalid configuration");

        // Act
        var (userMessage, technicalDetails, isRetryable) = InvokeMapError(error);

        // Assert
        Assert.Contains("Configuration error", userMessage);
        Assert.False(isRetryable);
    }

    [Fact]
    public void MapError_ProcessingError_ShouldMapToProcessingError()
    {
        // Arrange
        var error = new ProcessingError("Processing failed");

        // Act
        var (userMessage, technicalDetails, isRetryable) = InvokeMapError(error);

        // Assert
        Assert.Contains("Processing error", userMessage);
        Assert.True(isRetryable);
    }

    [Fact]
    public void MapError_GenericProviderError_ShouldMapToGenericError()
    {
        // Arrange
        var error = new ValidationError("Validation failed");

        // Act
        var (userMessage, technicalDetails, isRetryable) = InvokeMapError(error);

        // Assert
        Assert.Contains("Error:", userMessage);
        Assert.True(isRetryable);
    }

    #endregion

    #region Edge Cases

    [Fact]
    public void MapException_NullMessage_ShouldHandleGracefully()
    {
        // Arrange
        var exception = new InvalidOperationException();

        // Act
        var (userMessage, technicalDetails, isRetryable) = InvokeMapException(exception);

        // Assert
        Assert.NotNull(userMessage);
        Assert.NotNull(technicalDetails);
        Assert.True(isRetryable);
    }

    [Fact]
    public void MapError_EmptyMessage_ShouldHandleGracefully()
    {
        // Arrange
        var error = new ProcessingError("");

        // Act
        var (userMessage, technicalDetails, isRetryable) = InvokeMapError(error);

        // Assert
        Assert.NotNull(userMessage);
        Assert.NotNull(technicalDetails);
        Assert.True(isRetryable);
    }

    #endregion

    #region Helper Methods - Reflection to Access Private Static Methods

    /// <summary>
    /// Use reflection to invoke private MapExceptionToUserMessage method
    /// </summary>
    private (string userMessage, string technicalDetails, bool isRetryable) InvokeMapException(Exception exception)
    {
        var method = typeof(OAuthErrorHandler).GetMethod(
            "MapExceptionToUserMessage",
            System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static);

        Assert.NotNull(method);

        var result = method.Invoke(null, new object[] { exception });

        Assert.NotNull(result);

        // C# tuples are converted to ValueTuple<T1, T2, T3>
        var resultType = result.GetType();
        var item1 = resultType.GetField("Item1")?.GetValue(result) as string;
        var item2 = resultType.GetField("Item2")?.GetValue(result) as string;
        var item3 = (bool)(resultType.GetField("Item3")?.GetValue(result) ?? false);

        return (item1 ?? "", item2 ?? "", item3);
    }

    /// <summary>
    /// Use reflection to invoke private MapErrorToUserMessage method
    /// </summary>
    private (string userMessage, string technicalDetails, bool isRetryable) InvokeMapError(ProviderError error)
    {
        var method = typeof(OAuthErrorHandler).GetMethod(
            "MapErrorToUserMessage",
            System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static);

        Assert.NotNull(method);

        var result = method.Invoke(null, new object[] { error });

        Assert.NotNull(result);

        // C# tuples are converted to ValueTuple<T1, T2, T3>
        var resultType = result.GetType();
        var item1 = resultType.GetField("Item1")?.GetValue(result) as string;
        var item2 = resultType.GetField("Item2")?.GetValue(result) as string;
        var item3 = (bool)(resultType.GetField("Item3")?.GetValue(result) ?? false);

        return (item1 ?? "", item2 ?? "", item3);
    }

    #endregion
}
