using Microsoft.Extensions.Logging;
using TrashMailPanda.Models;
using TrashMailPanda.Providers.Email.Models;
using TrashMailPanda.Shared.Base;
using TrashMailPanda.Shared.Security;

namespace TrashMailPanda.Services;

/// <summary>
/// Validates Google OAuth token state and freshness for Gmail
/// </summary>
public class GoogleTokenValidator : IGoogleTokenValidator
{
    private readonly ISecureStorageManager _secureStorage;
    private readonly ILogger<GoogleTokenValidator> _logger;

    public GoogleTokenValidator(
        ISecureStorageManager secureStorage,
        ILogger<GoogleTokenValidator> logger)
    {
        _secureStorage = secureStorage;
        _logger = logger;
    }

    /// <summary>
    /// Validate current OAuth token state
    /// </summary>
    public async Task<Result<TokenValidationResult>> ValidateAsync()
    {
        try
        {
            _logger.LogDebug("Validating OAuth token state");

            // Load stored tokens
            var tokensResult = await LoadStoredTokensAsync();

            if (!tokensResult.IsSuccess)
            {
                // No tokens exist
                return Result<TokenValidationResult>.Success(new TokenValidationResult
                {
                    TokensExist = false,
                    IsAccessTokenExpired = true,
                    HasRefreshToken = false,
                    Status = TokenStatus.NotAuthenticated,
                    Message = "No OAuth tokens found - authentication required"
                });
            }

            var tokens = tokensResult.Value;

            // Check if refresh token exists
            var hasRefreshToken = !string.IsNullOrEmpty(tokens.RefreshToken);

            // Check if access token is expired
            var isExpired = tokens.IsAccessTokenExpired();
            var timeUntilExpiry = isExpired ? TimeSpan.Zero : tokens.TimeUntilExpiry();

            // Determine status
            TokenStatus status;
            string message;

            if (!isExpired)
            {
                status = TokenStatus.Valid;
                message = $"Access token valid for {timeUntilExpiry.TotalMinutes:F0} minutes";
            }
            else if (hasRefreshToken)
            {
                status = TokenStatus.ExpiredCanRefresh;
                message = "Access token expired - can auto-refresh";
            }
            else
            {
                status = TokenStatus.RefreshTokenMissing;
                message = "Access token expired and no refresh token - re-authentication required";
            }

            var result = new TokenValidationResult
            {
                TokensExist = true,
                IsAccessTokenExpired = isExpired,
                HasRefreshToken = hasRefreshToken,
                TimeUntilExpiry = timeUntilExpiry,
                Status = status,
                Message = message
            };

            _logger.LogDebug("Token validation complete - Status: {Status}", status);

            return Result<TokenValidationResult>.Success(result);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error validating OAuth tokens");
            return Result<TokenValidationResult>.Failure(
                new ProcessingError($"Token validation failed: {ex.Message}"));
        }
    }

    /// <summary>
    /// Check if automatic token refresh can be performed
    /// </summary>
    public async Task<Result<bool>> CanAutoRefreshAsync()
    {
        try
        {
            var validationResult = await ValidateAsync();

            if (!validationResult.IsSuccess)
            {
                return Result<bool>.Success(false);
            }

            var canRefresh = validationResult.Value.CanAutoRefresh;

            _logger.LogDebug("Can auto-refresh: {CanRefresh}", canRefresh);

            return Result<bool>.Success(canRefresh);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error checking auto-refresh capability");
            return Result<bool>.Failure(
                new ProcessingError($"Failed to check auto-refresh: {ex.Message}"));
        }
    }

    /// <summary>
    /// Load stored OAuth tokens from SecureStorageManager
    /// </summary>
    public async Task<Result<OAuthFlowResult>> LoadStoredTokensAsync()
    {
        try
        {
            _logger.LogDebug("Loading OAuth tokens from secure storage");

            // Load all token components
            var accessTokenResult = await _secureStorage.RetrieveCredentialAsync(GmailStorageKeys.ACCESS_TOKEN);
            var refreshTokenResult = await _secureStorage.RetrieveCredentialAsync(GmailStorageKeys.REFRESH_TOKEN);
            var expiryResult = await _secureStorage.RetrieveCredentialAsync(GmailStorageKeys.TOKEN_EXPIRY);
            var issuedUtcResult = await _secureStorage.RetrieveCredentialAsync(GmailStorageKeys.TOKEN_ISSUED_UTC);
            var userEmailResult = await _secureStorage.RetrieveCredentialAsync(GmailStorageKeys.USER_EMAIL);

            // Check if required tokens exist
            if (!accessTokenResult.IsSuccess || !refreshTokenResult.IsSuccess ||
                !expiryResult.IsSuccess || !issuedUtcResult.IsSuccess)
            {
                _logger.LogDebug("OAuth tokens not found in secure storage");
                return Result<OAuthFlowResult>.Failure(
                    new ConfigurationError("OAuth tokens not found in storage"));
            }

            // Parse token expiry
            if (!long.TryParse(expiryResult.Value, out var expiresInSeconds))
            {
                _logger.LogWarning("Invalid token expiry value: {Value}", expiryResult.Value);
                return Result<OAuthFlowResult>.Failure(
                    new ConfigurationError("Invalid token expiry value in storage"));
            }

            // Parse issued UTC timestamp
            if (!DateTime.TryParse(issuedUtcResult.Value, out var issuedUtc))
            {
                _logger.LogWarning("Invalid issued UTC value: {Value}", issuedUtcResult.Value);
                return Result<OAuthFlowResult>.Failure(
                    new ConfigurationError("Invalid issued UTC value in storage"));
            }

            // Construct OAuthFlowResult
            var result = new OAuthFlowResult
            {
                AccessToken = accessTokenResult.Value,
                RefreshToken = refreshTokenResult.Value,
                ExpiresInSeconds = expiresInSeconds,
                IssuedUtc = issuedUtc,
                Scopes = new[] { "https://www.googleapis.com/auth/gmail.modify" },
                UserEmail = userEmailResult.IsSuccess ? userEmailResult.Value : null
            };

            _logger.LogDebug("OAuth tokens loaded successfully - Expires: {Expiry}, Issued: {Issued}",
                expiresInSeconds, issuedUtc);

            return Result<OAuthFlowResult>.Success(result);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error loading OAuth tokens");
            return Result<OAuthFlowResult>.Failure(
                new ProcessingError($"Failed to load tokens: {ex.Message}"));
        }
    }
}
