using Microsoft.Extensions.Logging;
using Moq;
using System;
using System.Threading.Tasks;
using TrashMailPanda.Models;
using TrashMailPanda.Providers.Email.Models;
using TrashMailPanda.Services;
using TrashMailPanda.Shared.Base;
using TrashMailPanda.Shared.Security;
using Xunit;

namespace TrashMailPanda.Tests.Unit.Services;

/// <summary>
/// Unit tests for GoogleTokenValidator
/// Tests cover token expiry calculation, status determination, and LoadStoredTokensAsync logic
/// </summary>
public class TokenValidatorTests
{
    private readonly Mock<ISecureStorageManager> _mockSecureStorage;
    private readonly Mock<ILogger<GoogleTokenValidator>> _mockLogger;
    private readonly GoogleTokenValidator _validator;

    public TokenValidatorTests()
    {
        _mockSecureStorage = new Mock<ISecureStorageManager>();
        _mockLogger = new Mock<ILogger<GoogleTokenValidator>>();
        _validator = new GoogleTokenValidator(_mockSecureStorage.Object, _mockLogger.Object);
    }

    #region LoadStoredTokensAsync Tests

    [Fact]
    public async Task LoadStoredTokensAsync_WithValidTokens_ShouldReturnSuccess()
    {
        // Arrange
        var expiresIn = 3600L;
        var issuedUtc = DateTime.UtcNow.AddMinutes(-30);

        SetupStoredTokens("access-token", "refresh-token", expiresIn, issuedUtc, "user@example.com");

        // Act
        var result = await _validator.LoadStoredTokensAsync();

        // Assert
        Assert.True(result.IsSuccess);
        Assert.NotNull(result.Value);
        Assert.Equal("access-token", result.Value.AccessToken);
        Assert.Equal("refresh-token", result.Value.RefreshToken);
        Assert.Equal(expiresIn, result.Value.ExpiresInSeconds);
        Assert.Equal(issuedUtc, result.Value.IssuedUtc);
        Assert.Equal("user@example.com", result.Value.UserEmail);
    }

    [Fact]
    public async Task LoadStoredTokensAsync_WithMissingAccessToken_ShouldReturnFailure()
    {
        // Arrange
        _mockSecureStorage
            .Setup(x => x.RetrieveCredentialAsync(GmailStorageKeys.ACCESS_TOKEN))
            .ReturnsAsync(SecureStorageResult<string>.Failure("Not found", SecureStorageErrorType.CredentialNotFound));

        SetupStoredTokens(null, "refresh-token", 3600L, DateTime.UtcNow, null);

        // Act
        var result = await _validator.LoadStoredTokensAsync();

        // Assert
        Assert.False(result.IsSuccess);
        Assert.IsType<ConfigurationError>(result.Error);
    }

    [Fact]
    public async Task LoadStoredTokensAsync_WithMissingRefreshToken_ShouldReturnFailure()
    {
        // Arrange
        _mockSecureStorage
            .Setup(x => x.RetrieveCredentialAsync(GmailStorageKeys.REFRESH_TOKEN))
            .ReturnsAsync(SecureStorageResult<string>.Failure("Not found", SecureStorageErrorType.CredentialNotFound));

        SetupStoredTokens("access-token", null, 3600L, DateTime.UtcNow, null);

        // Act
        var result = await _validator.LoadStoredTokensAsync();

        // Assert
        Assert.False(result.IsSuccess);
    }

    [Fact]
    public async Task LoadStoredTokensAsync_WithInvalidExpiryFormat_ShouldReturnFailure()
    {
        // Arrange
        _mockSecureStorage
            .Setup(x => x.RetrieveCredentialAsync(GmailStorageKeys.ACCESS_TOKEN))
            .ReturnsAsync(SecureStorageResult<string>.Success("access-token"));

        _mockSecureStorage
            .Setup(x => x.RetrieveCredentialAsync(GmailStorageKeys.REFRESH_TOKEN))
            .ReturnsAsync(SecureStorageResult<string>.Success("refresh-token"));

        _mockSecureStorage
            .Setup(x => x.RetrieveCredentialAsync(GmailStorageKeys.TOKEN_EXPIRY))
            .ReturnsAsync(SecureStorageResult<string>.Success("not-a-number"));

        _mockSecureStorage
            .Setup(x => x.RetrieveCredentialAsync(GmailStorageKeys.TOKEN_ISSUED_UTC))
            .ReturnsAsync(SecureStorageResult<string>.Success(DateTime.UtcNow.ToString("O")));

        // Act
        var result = await _validator.LoadStoredTokensAsync();

        // Assert
        Assert.False(result.IsSuccess);
        Assert.IsType<ConfigurationError>(result.Error);
        Assert.Contains("expiry", result.Error.Message.ToLower());
    }

    [Fact]
    public async Task LoadStoredTokensAsync_WithInvalidIssuedUtcFormat_ShouldReturnFailure()
    {
        // Arrange
        _mockSecureStorage
            .Setup(x => x.RetrieveCredentialAsync(GmailStorageKeys.ACCESS_TOKEN))
            .ReturnsAsync(SecureStorageResult<string>.Success("access-token"));

        _mockSecureStorage
            .Setup(x => x.RetrieveCredentialAsync(GmailStorageKeys.REFRESH_TOKEN))
            .ReturnsAsync(SecureStorageResult<string>.Success("refresh-token"));

        _mockSecureStorage
            .Setup(x => x.RetrieveCredentialAsync(GmailStorageKeys.TOKEN_EXPIRY))
            .ReturnsAsync(SecureStorageResult<string>.Success("3600"));

        _mockSecureStorage
            .Setup(x => x.RetrieveCredentialAsync(GmailStorageKeys.TOKEN_ISSUED_UTC))
            .ReturnsAsync(SecureStorageResult<string>.Success("not-a-date"));

        // Act
        var result = await _validator.LoadStoredTokensAsync();

        // Assert
        Assert.False(result.IsSuccess);
        Assert.IsType<ConfigurationError>(result.Error);
        Assert.Contains("issued UTC", result.Error.Message);
    }

    [Fact]
    public async Task LoadStoredTokensAsync_WithMissingUserEmail_ShouldSucceedWithNullEmail()
    {
        // Arrange
        var expiresIn = 3600L;
        var issuedUtc = DateTime.UtcNow;

        _mockSecureStorage
            .Setup(x => x.RetrieveCredentialAsync(GmailStorageKeys.ACCESS_TOKEN))
            .ReturnsAsync(SecureStorageResult<string>.Success("access-token"));

        _mockSecureStorage
            .Setup(x => x.RetrieveCredentialAsync(GmailStorageKeys.REFRESH_TOKEN))
            .ReturnsAsync(SecureStorageResult<string>.Success("refresh-token"));

        _mockSecureStorage
            .Setup(x => x.RetrieveCredentialAsync(GmailStorageKeys.TOKEN_EXPIRY))
            .ReturnsAsync(SecureStorageResult<string>.Success(expiresIn.ToString()));

        _mockSecureStorage
            .Setup(x => x.RetrieveCredentialAsync(GmailStorageKeys.TOKEN_ISSUED_UTC))
            .ReturnsAsync(SecureStorageResult<string>.Success(issuedUtc.ToString("O")));

        _mockSecureStorage
            .Setup(x => x.RetrieveCredentialAsync(GmailStorageKeys.USER_EMAIL))
            .ReturnsAsync(SecureStorageResult<string>.Failure("Not found", SecureStorageErrorType.CredentialNotFound));

        // Act
        var result = await _validator.LoadStoredTokensAsync();

        // Assert
        Assert.True(result.IsSuccess);
        Assert.Null(result.Value.UserEmail);
    }

    #endregion

    #region ValidateAsync Tests

    [Fact]
    public async Task ValidateAsync_WithNoTokens_ShouldReturnNotAuthenticated()
    {
        // Arrange
        _mockSecureStorage
            .Setup(x => x.RetrieveCredentialAsync(It.IsAny<string>()))
            .ReturnsAsync(SecureStorageResult<string>.Failure("Not found", SecureStorageErrorType.CredentialNotFound));

        // Act
        var result = await _validator.ValidateAsync();

        // Assert
        Assert.True(result.IsSuccess);
        Assert.False(result.Value.TokensExist);
        Assert.True(result.Value.IsAccessTokenExpired);
        Assert.False(result.Value.HasRefreshToken);
        Assert.Equal(TokenStatus.NotAuthenticated, result.Value.Status);
    }

    [Fact]
    public async Task ValidateAsync_WithValidToken_ShouldReturnValid()
    {
        // Arrange - Token issued 10 minutes ago, expires in 3600 seconds (1 hour)
        var issuedUtc = DateTime.UtcNow.AddMinutes(-10);
        var expiresIn = 3600L;

        SetupStoredTokens("access-token", "refresh-token", expiresIn, issuedUtc, "user@example.com");

        // Act
        var result = await _validator.ValidateAsync();

        // Assert
        Assert.True(result.IsSuccess);
        Assert.True(result.Value.TokensExist);
        Assert.False(result.Value.IsAccessTokenExpired);
        Assert.True(result.Value.HasRefreshToken);
        Assert.Equal(TokenStatus.Valid, result.Value.Status);
        Assert.True(result.Value.TimeUntilExpiry > TimeSpan.Zero);
    }

    [Fact]
    public async Task ValidateAsync_WithExpiredTokenAndRefreshToken_ShouldReturnExpiredCanRefresh()
    {
        // Arrange - Token issued 2 hours ago, expires in 3600 seconds (1 hour)
        var issuedUtc = DateTime.UtcNow.AddHours(-2);
        var expiresIn = 3600L;

        SetupStoredTokens("access-token", "refresh-token", expiresIn, issuedUtc, "user@example.com");

        // Act
        var result = await _validator.ValidateAsync();

        // Assert
        Assert.True(result.IsSuccess);
        Assert.True(result.Value.TokensExist);
        Assert.True(result.Value.IsAccessTokenExpired);
        Assert.True(result.Value.HasRefreshToken);
        Assert.Equal(TokenStatus.ExpiredCanRefresh, result.Value.Status);
        Assert.Equal(TimeSpan.Zero, result.Value.TimeUntilExpiry);
    }

    [Fact]
    public async Task ValidateAsync_WithExpiredTokenNoRefreshToken_ShouldReturnRefreshTokenMissing()
    {
        // Arrange - Token issued 2 hours ago, no refresh token
        var issuedUtc = DateTime.UtcNow.AddHours(-2);
        var expiresIn = 3600L;

        SetupStoredTokens("access-token", "", expiresIn, issuedUtc, "user@example.com");

        // Act
        var result = await _validator.ValidateAsync();

        // Assert
        Assert.True(result.IsSuccess);
        Assert.True(result.Value.TokensExist);
        Assert.True(result.Value.IsAccessTokenExpired);
        Assert.False(result.Value.HasRefreshToken);
        Assert.Equal(TokenStatus.RefreshTokenMissing, result.Value.Status);
    }

    [Fact]
    public async Task ValidateAsync_WithStorageException_ShouldReturnFailure()
    {
        // Arrange
        _mockSecureStorage
            .Setup(x => x.RetrieveCredentialAsync(It.IsAny<string>()))
            .ThrowsAsync(new InvalidOperationException("Storage unavailable"));

        // Act
        var result = await _validator.ValidateAsync();

        // Assert
        Assert.False(result.IsSuccess);
        Assert.IsType<ProcessingError>(result.Error);
    }

    #endregion

    #region CanAutoRefreshAsync Tests

    [Fact]
    public async Task CanAutoRefreshAsync_WithExpiredTokenAndRefreshToken_ShouldReturnTrue()
    {
        // Arrange - Expired token with refresh token
        var issuedUtc = DateTime.UtcNow.AddHours(-2);
        var expiresIn = 3600L;

        SetupStoredTokens("access-token", "refresh-token", expiresIn, issuedUtc, "user@example.com");

        // Act
        var result = await _validator.CanAutoRefreshAsync();

        // Assert
        Assert.True(result.IsSuccess);
        Assert.True(result.Value);
    }

    [Fact]
    public async Task CanAutoRefreshAsync_WithValidToken_ShouldReturnFalse()
    {
        // Arrange - Valid token (not expired yet)
        var issuedUtc = DateTime.UtcNow.AddMinutes(-10);
        var expiresIn = 3600L;

        SetupStoredTokens("access-token", "refresh-token", expiresIn, issuedUtc, "user@example.com");

        // Act
        var result = await _validator.CanAutoRefreshAsync();

        // Assert
        Assert.True(result.IsSuccess);
        Assert.False(result.Value);
    }

    [Fact]
    public async Task CanAutoRefreshAsync_WithNoTokens_ShouldReturnFalse()
    {
        // Arrange
        _mockSecureStorage
            .Setup(x => x.RetrieveCredentialAsync(It.IsAny<string>()))
            .ReturnsAsync(SecureStorageResult<string>.Failure("Not found", SecureStorageErrorType.CredentialNotFound));

        // Act
        var result = await _validator.CanAutoRefreshAsync();

        // Assert
        Assert.True(result.IsSuccess);
        Assert.False(result.Value);
    }

    [Fact]
    public async Task CanAutoRefreshAsync_WithExpiredTokenNoRefresh_ShouldReturnFalse()
    {
        // Arrange - Expired token without refresh token
        var issuedUtc = DateTime.UtcNow.AddHours(-2);
        var expiresIn = 3600L;

        SetupStoredTokens("access-token", "", expiresIn, issuedUtc, "user@example.com");

        // Act
        var result = await _validator.CanAutoRefreshAsync();

        // Assert
        Assert.True(result.IsSuccess);
        Assert.False(result.Value);
    }

    [Fact]
    public async Task CanAutoRefreshAsync_WithStorageException_ShouldReturnFailure()
    {
        // Arrange
        _mockSecureStorage
            .Setup(x => x.RetrieveCredentialAsync(It.IsAny<string>()))
            .ThrowsAsync(new InvalidOperationException("Storage unavailable"));

        // Act
        var result = await _validator.CanAutoRefreshAsync();

        // Assert
        Assert.False(result.IsSuccess);
        Assert.IsType<ProcessingError>(result.Error);
    }

    #endregion

    #region Token Expiry Calculation Tests

    [Theory]
    [InlineData(-10, 3600, true)]  // Issued 10 minutes ago, expires in 1 hour - VALID
    [InlineData(-30, 3600, true)]  // Issued 30 minutes ago, expires in 1 hour - VALID
    [InlineData(-59, 3600, true)]  // Issued 59 minutes ago, expires in 1 hour - VALID
    [InlineData(-61, 3600, false)] // Issued 61 minutes ago, expires in 1 hour - EXPIRED
    [InlineData(-120, 3600, false)] // Issued 2 hours ago, expires in 1 hour - EXPIRED
    public async Task ValidateAsync_TokenExpiryCalculation_ShouldBeAccurate(
        int minutesAgo, long expiresInSeconds, bool shouldBeValid)
    {
        // Arrange
        var issuedUtc = DateTime.UtcNow.AddMinutes(minutesAgo);
        SetupStoredTokens("access-token", "refresh-token", expiresInSeconds, issuedUtc, "user@example.com");

        // Act
        var result = await _validator.ValidateAsync();

        // Assert
        Assert.True(result.IsSuccess);

        if (shouldBeValid)
        {
            Assert.False(result.Value.IsAccessTokenExpired);
            Assert.Equal(TokenStatus.Valid, result.Value.Status);
            Assert.True(result.Value.TimeUntilExpiry > TimeSpan.Zero);
        }
        else
        {
            Assert.True(result.Value.IsAccessTokenExpired);
            Assert.Equal(TokenStatus.ExpiredCanRefresh, result.Value.Status);
            Assert.Equal(TimeSpan.Zero, result.Value.TimeUntilExpiry);
        }
    }

    #endregion

    #region Helper Methods

    private void SetupStoredTokens(
        string? accessToken,
        string? refreshToken,
        long expiresIn,
        DateTime issuedUtc,
        string? userEmail)
    {
        if (accessToken != null)
        {
            _mockSecureStorage
                .Setup(x => x.RetrieveCredentialAsync(GmailStorageKeys.ACCESS_TOKEN))
                .ReturnsAsync(SecureStorageResult<string>.Success(accessToken));
        }

        if (refreshToken != null)
        {
            _mockSecureStorage
                .Setup(x => x.RetrieveCredentialAsync(GmailStorageKeys.REFRESH_TOKEN))
                .ReturnsAsync(SecureStorageResult<string>.Success(refreshToken));
        }

        _mockSecureStorage
            .Setup(x => x.RetrieveCredentialAsync(GmailStorageKeys.TOKEN_EXPIRY))
            .ReturnsAsync(SecureStorageResult<string>.Success(expiresIn.ToString()));

        _mockSecureStorage
            .Setup(x => x.RetrieveCredentialAsync(GmailStorageKeys.TOKEN_ISSUED_UTC))
            .ReturnsAsync(SecureStorageResult<string>.Success(issuedUtc.ToString("O")));

        if (userEmail != null)
        {
            _mockSecureStorage
                .Setup(x => x.RetrieveCredentialAsync(GmailStorageKeys.USER_EMAIL))
                .ReturnsAsync(SecureStorageResult<string>.Success(userEmail));
        }
    }

    #endregion
}
