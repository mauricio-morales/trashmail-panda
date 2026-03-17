# Service Contracts: Console OAuth Handler

**Feature**: 056-console-gmail-oauth  
**Date**: March 16, 2026

## Overview

This document defines the public interfaces for console-based OAuth authentication services. These contracts establish the API surface for OAuth flow orchestration, token validation, and callback handling.

---

## IConsoleOAuthHandler

**Purpose**: Orchestrates the complete OAuth 2.0 authorization code flow for console applications.

**Responsibilities**:
- Coordinate localhost HTTP listener, browser launch, and token exchange
- Implement PKCE (Proof Key for Code Exchange) security
- Store/retrieve tokens via SecureStorageManager
- Handle errors and provide user-friendly feedback via Spectre.Console

```csharp
namespace TrashMailPanda.Services;

/// <summary>
/// Console-based OAuth 2.0 handler for Gmail authentication
/// Implements authorization code flow with PKCE and localhost callback
/// </summary>
public interface IConsoleOAuthHandler
{
    /// <summary>
    /// Execute complete OAuth authentication flow
    /// </summary>
    /// <param name="configuration">OAuth client credentials and settings</param>
    /// <param name="cancellationToken">Cancellation token for flow timeout</param>
    /// <returns>Result containing OAuth tokens or error</returns>
    /// <remarks>
    /// Flow steps:
    /// 1. Generate PKCE pair (code_verifier + code_challenge)
    /// 2. Start localhost HTTP listener on dynamic port
    /// 3. Build authorization URL with PKCE challenge
    /// 4. Launch system browser with authorization URL
    /// 5. Wait for OAuth callback (up to 5 minutes)
    /// 6. Exchange authorization code for tokens
    /// 7. Store tokens in OS keychain
    /// </remarks>
    Task<Result<OAuthFlowResult>> AuthenticateAsync(
        OAuthConfiguration configuration,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Refresh access token using stored refresh token
    /// </summary>
    /// <param name="refreshToken">Existing refresh token</param>
    /// <param name="clientConfig">OAuth client credentials</param>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>Result containing new OAuth tokens or error</returns>
    /// <remarks>
    /// Returns failure with AuthenticationError if refresh token is revoked (invalid_grant).
    /// Caller should trigger full re-authentication on refresh failure.
    /// </remarks>
    Task<Result<OAuthFlowResult>> RefreshTokenAsync(
        string refreshToken,
        OAuthConfiguration clientConfig,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Validate stored OAuth configuration exists
    /// </summary>
    /// <returns>Result indicating whether OAuth is configured</returns>
    /// <remarks>
    /// Checks for:
    /// - Client ID exists in SecureStorageManager
    /// - Client secret exists in SecureStorageManager
    /// Does NOT validate token freshness - use ITokenValidator for that
    /// </remarks>
    Task<Result<bool>> IsConfiguredAsync();

    /// <summary>
    /// Clear all stored OAuth tokens and configuration
    /// </summary>
    /// <returns>Result indicating success or failure</returns>
    /// <remarks>
    /// Used for:
    /// - User-initiated sign-out
    /// - Refresh token revoked (invalid_grant error)
    /// - Switching Gmail accounts
    /// </remarks>
    Task<Result<bool>> ClearAuthenticationAsync();
}
```

**Error Handling**:
| Error Type | Scenario | Recovery |
|-----------|----------|----------|
| `ConfigurationError` | Client ID/secret missing | Configure OAuth credentials |
| `AuthenticationError` | User denied permissions, invalid_grant | Retry or re-authenticate |
| `NetworkError` | Browser launch failed, network timeout | Retry, check connectivity |
| `ProcessingError` | OAuth flow timeout (5 minutes) | Retry |

---

## ITokenValidator

**Purpose**: Validates OAuth token state and determines if refresh or re-authentication is needed.

**Responsibilities**:
- Check token existence in SecureStorageManager
- Calculate token expiry based on issued time + expiry seconds
- Determine authentication strategy (use existing, refresh, re-authenticate)

```csharp
namespace TrashMailPanda.Services;

/// <summary>
/// Validates OAuth token state and freshness
/// </summary>
public interface ITokenValidator
{
    /// <summary>
    /// Validate current OAuth token state
    /// </summary>
    /// <returns>TokenValidationResult describing token state and required actions</returns>
    /// <remarks>
    /// Checks:
    /// 1. Do tokens exist in SecureStorageManager?
    /// 2. Does refresh token exist (required for auto-refresh)?
    /// 3. Is access token expired (IssuedUtc + ExpiresInSeconds < Now)?
    /// 4. Determines status: Valid, ExpiredCanRefresh, RefreshTokenMissing, NotAuthenticated
    /// </remarks>
    Task<Result<TokenValidationResult>> ValidateAsync();

    /// <summary>
    /// Check if automatic token refresh can be performed
    /// </summary>
    /// <returns>True if refresh token exists and tokens are stored</returns>
    Task<Result<bool>> CanAutoRefreshAsync();

    /// <summary>
    /// Load stored OAuth tokens from SecureStorageManager
    /// </summary>
    /// <returns>OAuthFlowResult with stored tokens or error if not found</returns>
    /// <remarks>
    /// Reconstructs OAuthFlowResult from individual SecureStorageManager entries:
    /// - gmail_access_token
    /// - gmail_refresh_token
    /// - gmail_token_expiry
    /// - gmail_token_issued_utc
    /// - gmail_user_email
    /// </remarks>
    Task<Result<OAuthFlowResult>> LoadStoredTokensAsync();
}
```

**Usage Pattern**:
```csharp
// On application startup:
var validator = serviceProvider.GetRequiredService<ITokenValidator>();
var validationResult = await validator.ValidateAsync();

if (validationResult.IsSuccess)
{
    switch (validationResult.Value.Status)
    {
        case TokenStatus.Valid:
            AnsiConsole.MarkupLine("[green]✓ Gmail authenticated[/]");
            break;

        case TokenStatus.ExpiredCanRefresh:
            var refreshResult = await _oauthHandler.RefreshTokenAsync(...);
            break;

        case TokenStatus.NotAuthenticated:
            var authResult = await _oauthHandler.AuthenticateAsync(...);
            break;

        case TokenStatus.RefreshTokenRevoked:
            await _oauthHandler.ClearAuthenticationAsync();
            var authResult = await _oauthHandler.AuthenticateAsync(...);
            break;
    }
}
```

---

## ILocalOAuthCallbackListener

**Purpose**: Manages localhost HTTP listener for OAuth callback handling.

**Responsibilities**:
- Start HttpListener on dynamic port (OS-assigned)
- Wait for OAuth callback with authorization code
- Parse callback query parameters (code, state, error)
- Validate callback origin (127.0.0.1 only)
- Handle timeout (5-minute maximum)

```csharp
namespace TrashMailPanda.Services;

/// <summary>
/// Localhost HTTP listener for OAuth callback handling
/// Implements temporary HTTP server on 127.0.0.1 with dynamic port
/// </summary>
public interface ILocalOAuthCallbackListener : IAsyncDisposable
{
    /// <summary>
    /// Start HTTP listener on localhost with dynamic port
    /// </summary>
    /// <param name="callbackPath">URL path for OAuth callback (default: "/oauth/callback")</param>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>Port number assigned by OS</returns>
    /// <remarks>
    /// Uses HttpListener with prefix: http://127.0.0.1:0{callbackPath}
    /// Port 0 = OS selects available port dynamically
    /// </remarks>
    Task<Result<int>> StartAsync(
        string callbackPath = "/oauth/callback",
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Get complete redirect URI for OAuth authorization URL
    /// </summary>
    /// <param name="callbackPath">URL path for callback</param>
    /// <returns>Full URL (e.g., http://127.0.0.1:54321/oauth/callback)</returns>
    string GetRedirectUri(string callbackPath = "/oauth/callback");

    /// <summary>
    /// Wait for OAuth callback with timeout
    /// </summary>
    /// <param name="expectedState">Expected state parameter for CSRF validation</param>
    /// <param name="timeout">Maximum wait time (default: 5 minutes)</param>
    /// <returns>OAuthCallbackData with authorization code or error</returns>
    /// <remarks>
    /// Blocks until:
    /// - HTTP callback received with authorization code
    /// - Timeout expires (default 5 minutes)
    /// - Cancellation requested
    /// 
    /// Validates:
    /// - Request origin is 127.0.0.1 (localhost only)
    /// - State parameter matches expectedState (CSRF protection)
    /// - Authorization code is present (no error parameter)
    /// </remarks>
    Task<Result<OAuthCallbackData>> WaitForCallbackAsync(
        string expectedState,
        TimeSpan? timeout = null);

    /// <summary>
    /// Stop HTTP listener and clean up resources
    /// </summary>
    /// <returns>Result indicating success or failure</returns>
    Task<Result<bool>> StopAsync();
}
```

**Security Constraints**:
- **Origin validation**: Only accept requests from `127.0.0.1` or `::1` (IPv6 localhost)
- **Path validation**: Only accept requests to exact callback path
- **State validation**: CSRF protection via state parameter matching
- **Timeout**: Maximum 5-minute wait to prevent indefinite blocking

---

## Supporting Types

### OAuthFlowResult
```csharp
/// <summary>
/// Complete OAuth authentication result with tokens and metadata
/// </summary>
public record OAuthFlowResult
{
    public required string AccessToken { get; init; }
    public required string RefreshToken { get; init; }
    public required long ExpiresInSeconds { get; init; }
    public required DateTime IssuedUtc { get; init; }
    public required string[] Scopes { get; init; }
    public string? UserEmail { get; init; }
    public string TokenType { get; init; } = "Bearer";
}
```

### OAuthCallbackData
```csharp
/// <summary>
/// OAuth callback parameters received from authorization server
/// </summary>
public record OAuthCallbackData
{
    public required string Code { get; init; }
    public string? State { get; init; }
    public string? Error { get; init; }
    public string? ErrorDescription { get; init; }
    public DateTime ReceivedAt { get; init; } = DateTime.UtcNow;
}
```

### TokenValidationResult
```csharp
/// <summary>
/// OAuth token validation state
/// </summary>
public record TokenValidationResult
{
    public required bool TokensExist { get; init; }
    public required bool IsAccessTokenExpired { get; init; }
    public required bool HasRefreshToken { get; init; }
    public TimeSpan? TimeUntilExpiry { get; init; }
    public required TokenStatus Status { get; init; }
    public string Message { get; init; } = string.Empty;
}

public enum TokenStatus
{
    Valid,
    ExpiredCanRefresh,
    RefreshTokenMissing,
    NotAuthenticated,
    RefreshTokenRevoked
}
```

### OAuthConfiguration
```csharp
/// <summary>
/// OAuth client configuration
/// </summary>
public record OAuthConfiguration
{
    public required string ClientId { get; init; }
    public required string ClientSecret { get; init; }
    public required string[] Scopes { get; init; }
    public required string RedirectUri { get; init; }
    public TimeSpan Timeout { get; init; } = TimeSpan.FromMinutes(5);
}
```

---

## Dependency Injection Configuration

```csharp
// Program.cs or Startup.cs
services.AddSingleton<IConsoleOAuthHandler, ConsoleOAuthHandler>();
services.AddSingleton<ITokenValidator, TokenValidator>();
services.AddTransient<ILocalOAuthCallbackListener, LocalOAuthCallbackListener>();

// Existing dependencies these services rely on:
services.AddSingleton<ISecureStorageManager, SecureStorageManager>();
services.AddLogging();
```

---

## Contract Versioning

| Version | Date | Changes |
|---------|------|---------|
| 1.0.0 | 2026-03-16 | Initial contract definition for console OAuth |

**Compatibility Promise**:
- Interface methods will not be removed in minor versions
- New optional parameters may be added with default values
- Breaking changes require major version increment
- Implementations must adhere to Result<T> pattern (no exceptions for business logic)

---

## Testing Contracts

### Unit Test Coverage Requirements
- 90% coverage for all implementations
- Mock ISecureStorageManager for token storage tests
- Test timeout handling with CancellationTokenSource
- Test error mapping (HTTP errors → Result<T> failures)

### Integration Test Scenarios
```csharp
[Fact(Skip = "Requires real OAuth credentials and browser interaction")]
public async Task AuthenticateAsync_WithRealGmail_Success()
{
    // Arrange
    var config = new OAuthConfiguration { ... };
    var handler = new ConsoleOAuthHandler(...);
    
    // Act
    var result = await handler.AuthenticateAsync(config);
    
    // Assert
    Assert.True(result.IsSuccess);
    Assert.NotNull(result.Value.RefreshToken);
}
```

---

## Summary

| Interface | Responsibility | Primary Consumer |
|-----------|---------------|------------------|
| **IConsoleOAuthHandler** | OAuth flow orchestration | Console startup, Gmail provider initialization |
| **ITokenValidator** | Token state validation | Application startup, provider health checks |
| **ILocalOAuthCallbackListener** | HTTP callback server | ConsoleOAuthHandler (internal) |

**Design Principles**:
- **Result<T> pattern**: All async operations return `Result<T>` (no exceptions for business logic)
- **Dependency injection**: All services registered in DI container
- **Immutable DTOs**: All data models are `record` types
- **Explicit nullability**: Nullable reference types enabled
- **Testability**: Interfaces allow mocking for unit tests
- **Security-first**: PKCE, state validation, localhost-only callbacks
