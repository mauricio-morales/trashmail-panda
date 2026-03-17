# Data Model: Console Gmail OAuth Flow

**Phase 1 Output** | **Feature**: 056-console-gmail-oauth  
**Date**: March 16, 2026

## Entity Overview

This feature introduces three core entities for managing OAuth authentication flow in a console environment:

1. **OAuthFlowResult** - Represents the outcome of an OAuth authentication attempt
2. **OAuthCallbackData** - Captures OAuth callback parameters from localhost listener
3. **TokenValidationResult** - Validates and describes token state

---

## Entity Definitions

### 1. OAuthFlowResult

**Purpose**: Encapsulates the complete outcome of an OAuth authentication flow, including tokens and user information.

**Lifecycle**: Created after successful OAuth flow completion, stored in OS keychain via SecureStorageManager.

**Relationships**:
- Used by `ConsoleOAuthHandler` to return authentication results
- Consumed by `GmailEmailProvider` for credential initialization
- Referenced by `TokenValidator` for validation operations

```csharp
public record OAuthFlowResult
{
    /// <summary>
    /// OAuth access token (short-lived, typically 1 hour)
    /// </summary>
    public required string AccessToken { get; init; }

    /// <summary>
    /// OAuth refresh token (long-lived, used to obtain new access tokens)
    /// </summary>
    public required string RefreshToken { get; init; }

    /// <summary>
    /// Token expiration time in seconds (e.g., 3600 for 1 hour)
    /// </summary>
    public required long ExpiresInSeconds { get; init; }

    /// <summary>
    /// UTC timestamp when tokens were issued
    /// </summary>
    public required DateTime IssuedUtc { get; init; }

    /// <summary>
    /// OAuth scopes granted by user
    /// </summary>
    public required string[] Scopes { get; init; }

    /// <summary>
    /// User's Gmail email address (retrieved from profile)
    /// </summary>
    public string? UserEmail { get; init; }

    /// <summary>
    /// Token type (typically "Bearer")
    /// </summary>
    public string TokenType { get; init; } = "Bearer";

    /// <summary>
    /// Check if access token is expired based on issued time + expiry
    /// </summary>
    public bool IsAccessTokenExpired() =>
        DateTime.UtcNow >= IssuedUtc.AddSeconds(ExpiresInSeconds);

    /// <summary>
    /// Time remaining until access token expires
    /// </summary>
    public TimeSpan TimeUntilExpiry() =>
        IssuedUtc.AddSeconds(ExpiresInSeconds) - DateTime.UtcNow;
}
```

**Validation Rules**:
- `AccessToken`: Must not be null or whitespace
- `RefreshToken`: Must not be null or whitespace (critical for auto-refresh)
- `ExpiresInSeconds`: Must be > 0 (typically 3600)
- `IssuedUtc`: Must not be in the future
- `Scopes`: Must contain at least one scope (e.g., "https://www.googleapis.com/auth/gmail.modify")

**State Transitions**:
```
New Flow → Tokens Issued → Stored in Keychain
                          ↓
                 Access Token Expired → Refresh → New Access Token
                          ↓
                 Refresh Token Revoked → Re-authentication Required
```

---

### 2. OAuthCallbackData

**Purpose**: Represents OAuth callback parameters received from Google's authorization server via localhost HTTP listener.

**Lifecycle**: Created when OAuth callback URL is invoked, validated immediately, then consumed to exchange authorization code for tokens.

**Relationships**:
- Created by `LocalhostOAuthListener` from HTTP query parameters
- Validated by `ConsoleOAuthHandler` (state matching, error checking)
- Used in token exchange request to Google OAuth endpoint

```csharp
public record OAuthCallbackData
{
    /// <summary>
    /// Authorization code from Google (single-use, short-lived ~10 minutes)
    /// </summary>
    public required string Code { get; init; }

    /// <summary>
    /// State parameter for CSRF protection (must match original request)
    /// </summary>
    public string? State { get; init; }

    /// <summary>
    /// Error code if authorization failed (e.g., "access_denied")
    /// </summary>
    public string? Error { get; init; }

    /// <summary>
    /// Human-readable error description
    /// </summary>
    public string? ErrorDescription { get; init; }

    /// <summary>
    /// Timestamp when callback was received
    /// </summary>
    public DateTime ReceivedAt { get; init; } = DateTime.UtcNow;

    /// <summary>
    /// Check if callback represents an error (user denied, etc.)
    /// </summary>
    public bool IsError => !string.IsNullOrEmpty(Error);

    /// <summary>
    /// Check if callback is valid (has code, no errors)
    /// </summary>
    public bool IsValid => !string.IsNullOrEmpty(Code) && !IsError;
}
```

**Validation Rules**:
- `Code`: Must not be null/empty if no error (required for token exchange)
- `State`: Must match original request state (CSRF protection)
- `Error`: If present, indicates user denial or OAuth failure
- `ReceivedAt`: Used to validate code hasn't expired (10-minute window)

**State Transitions**:
```
HTTP Callback Received → Parse Query Params → Validate State
                                            ↓
                                   [ Has Code? ]
                                    ↓         ↓
                                  Yes        No (Error)
                                    ↓         ↓
                         Exchange for Tokens  Return Failure
```

---

### 3. TokenValidationResult

**Purpose**: Describes the current state of OAuth tokens and whether refresh is needed.

**Lifecycle**: Created during startup token validation or before API calls, guides decision to use existing token, refresh, or re-authenticate.

**Relationships**:
- Created by `TokenValidator` service
- Consumed by `ConsoleOAuthHandler` to determine if re-authentication needed
- Used by `GmailEmailProvider` health check

```csharp
public record TokenValidationResult
{
    /// <summary>
    /// Whether tokens exist in secure storage
    /// </summary>
    public required bool TokensExist { get; init; }

    /// <summary>
    /// Whether access token is expired
    /// </summary>
    public required bool IsAccessTokenExpired { get; init; }

    /// <summary>
    /// Whether refresh token exists (required for auto-refresh)
    /// </summary>
    public required bool HasRefreshToken { get; init; }

    /// <summary>
    /// Time remaining until access token expires (null if no token)
    /// </summary>
    public TimeSpan? TimeUntilExpiry { get; init; }

    /// <summary>
    /// Overall validation status
    /// </summary>
    public TokenStatus Status { get; init; }

    /// <summary>
    /// User-friendly message describing token state
    /// </summary>
    public string Message { get; init; } = string.Empty;

    /// <summary>
    /// Whether automatic refresh can be attempted
    /// </summary>
    public bool CanAutoRefresh => HasRefreshToken && TokensExist;

    /// <summary>
    /// Whether full re-authentication is required
    /// </summary>
    public bool RequiresReAuthentication => !TokensExist || !HasRefreshToken;
}

public enum TokenStatus
{
    /// <summary>Valid access token, no action needed</summary>
    Valid,

    /// <summary>Access token expired, can auto-refresh with refresh token</summary>
    ExpiredCanRefresh,

    /// <summary>No refresh token, must re-authenticate</summary>
    RefreshTokenMissing,

    /// <summary>No tokens found, initial authentication required</summary>
    NotAuthenticated,

    /// <summary>Refresh token revoked or invalid</summary>
    RefreshTokenRevoked
}
```

**Validation Rules**:
- `TokensExist` AND `HasRefreshToken` = Can auto-refresh
- `TokensExist` AND NOT `HasRefreshToken` = Must re-authenticate (broken state)
- NOT `TokensExist` = Initial authentication required

**State Transitions**:
```
NotAuthenticated → OAuth Flow → Valid
         ↓
Valid → Time Passes → ExpiredCanRefresh → Auto-Refresh → Valid
         ↓                                               ↓
         ↓                                    Refresh Fails (invalid_grant)
         ↓                                               ↓
         └──────────────────────────→ RefreshTokenRevoked → Re-authenticate
```

---

## Supporting Value Objects

### PKCEPair

**Purpose**: Represents PKCE code verifier and challenge for OAuth security.

```csharp
public record PKCEPair
{
    /// <summary>
    /// SHA256 hash of code verifier (sent in authorization URL)
    /// </summary>
    public required string CodeChallenge { get; init; }

    /// <summary>
    /// Random code verifier (sent during token exchange to prove identity)
    /// </summary>
    public required string CodeVerifier { get; init; }
}
```

---

### OAuthConfiguration

**Purpose**: Encapsulates OAuth client credentials and configuration.

```csharp
public record OAuthConfiguration
{
    /// <summary>
    /// Google OAuth client ID
    /// </summary>
    public required string ClientId { get; init; }

    /// <summary>
    /// Google OAuth client secret
    /// </summary>
    public required string ClientSecret { get; init; }

    /// <summary>
    /// OAuth scopes to request
    /// </summary>
    public required string[] Scopes { get; init; }

    /// <summary>
    /// Redirect URI (localhost callback)
    /// </summary>
    public required string RedirectUri { get; init; }

    /// <summary>
    /// OAuth flow timeout (default: 5 minutes)
    /// </summary>
    public TimeSpan Timeout { get; init; } = TimeSpan.FromMinutes(5);
}
```

---

## Persistence Strategy

### SecureStorageManager Keys

All OAuth tokens are stored using existing `SecureStorageManager` with OS keychain encryption:

```csharp
// Existing keys from GmailStorageKeys.cs
public static class GmailStorageKeys
{
    public const string GMAIL_ACCESS_TOKEN = "gmail_access_token";
    public const string GMAIL_REFRESH_TOKEN = "gmail_refresh_token";
    public const string GMAIL_TOKEN_EXPIRY = "gmail_token_expiry";
    public const string GMAIL_TOKEN_ISSUED_UTC = "gmail_token_issued_utc";
    public const string GMAIL_USER_EMAIL = "gmail_user_email";
    public const string GMAIL_CLIENT_ID = "gmail_client_id";
    public const string GMAIL_CLIENT_SECRET = "gmail_client_secret";
}
```

### Storage Format

```csharp
// Store OAuthFlowResult after successful authentication
await _secureStorage.StoreCredentialAsync(
    GmailStorageKeys.GMAIL_ACCESS_TOKEN, result.AccessToken);
await _secureStorage.StoreCredentialAsync(
    GmailStorageKeys.GMAIL_REFRESH_TOKEN, result.RefreshToken);
await _secureStorage.StoreCredentialAsync(
    GmailStorageKeys.GMAIL_TOKEN_EXPIRY, result.ExpiresInSeconds.ToString());
await _secureStorage.StoreCredentialAsync(
    GmailStorageKeys.GMAIL_TOKEN_ISSUED_UTC, result.IssuedUtc.ToString("O")); // ISO 8601
await _secureStorage.StoreCredentialAsync(
    GmailStorageKeys.GMAIL_USER_EMAIL, result.UserEmail ?? "");
```

### Retrieval Pattern

```csharp
// Load tokens from secure storage
var accessTokenResult = await _secureStorage.RetrieveCredentialAsync(
    GmailStorageKeys.GMAIL_ACCESS_TOKEN);
var refreshTokenResult = await _secureStorage.RetrieveCredentialAsync(
    GmailStorageKeys.GMAIL_REFRESH_TOKEN);
var expiryResult = await _secureStorage.RetrieveCredentialAsync(
    GmailStorageKeys.GMAIL_TOKEN_EXPIRY);
var issuedResult = await _secureStorage.RetrieveCredentialAsync(
    GmailStorageKeys.GMAIL_TOKEN_ISSUED_UTC);

// Reconstruct OAuthFlowResult
var result = new OAuthFlowResult
{
    AccessToken = accessTokenResult.Value,
    RefreshToken = refreshTokenResult.Value,
    ExpiresInSeconds = long.Parse(expiryResult.Value),
    IssuedUtc = DateTime.Parse(issuedResult.Value, null, DateTimeStyles.RoundtripKind),
    Scopes = new[] { "https://www.googleapis.com/auth/gmail.modify" }
};
```

---

## Data Flow Diagram

```
┌─────────────────────────────────────────────────────────────┐
│ Console OAuth Flow                                          │
└─────────────────────────────────────────────────────────────┘
                      │
                      ▼
         ┌─────────────────────────┐
         │ 1. Check Token Status   │
         │ → TokenValidationResult │
         └─────────────────────────────┘
                      │
          ┌───────────┴──────────┐
          │                      │
      [Valid]              [Not Authenticated]
          │                      │
          ▼                      ▼
    Use Existing      ┌───────────────────────┐
    Credentials       │ 2. Start OAuth Flow   │
                      │ → Generate PKCEPair   │
                      └───────────────────────┘
                                │
                                ▼
                      ┌─────────────────────────┐
                      │ 3. Launch Browser       │
                      │ → Authorization URL     │
                      └─────────────────────────┘
                                │
                                ▼
                      ┌─────────────────────────┐
                      │ 4. Wait for Callback    │
                      │ → OAuthCallbackData     │
                      └─────────────────────────┘
                                │
                    ┌───────────┴────────────┐
                    │                        │
                [Success]                [Error]
                    │                        │
                    ▼                        ▼
          ┌──────────────────┐      Display Error
          │ 5. Exchange Code │      Offer Retry
          │ → OAuthFlowResult│
          └──────────────────┘
                    │
                    ▼
          ┌──────────────────────┐
          │ 6. Store in Keychain │
          │ → SecureStorageManager│
          └──────────────────────┘
                    │
                    ▼
              [Complete]
```

---

## Entity Relationships

```
OAuthConfiguration
    │
    ├── Used by → ConsoleOAuthHandler
    │                    │
    │                    ├── Generates → PKCEPair
    │                    │
    │                    ├── Creates → LocalhostOAuthListener
    │                    │                    │
    │                    │                    └── Returns → OAuthCallbackData
    │                    │
    │                    └── Produces → OAuthFlowResult
    │                                           │
    │                                           └── Stored via → SecureStorageManager
    │
    └── Validated by → TokenValidator
                             │
                             └── Produces → TokenValidationResult
                                                   │
                                                   └── Determines → Authentication Strategy
```

---

## Summary

| Entity | Purpose | Lifecycle | Storage |
|--------|---------|-----------|---------|
| **OAuthFlowResult** | Complete OAuth tokens + metadata | Created after OAuth flow, refreshed periodically | OS Keychain (SecureStorageManager) |
| **OAuthCallbackData** | OAuth callback parameters | Transient (created from HTTP callback, used once) | Not persisted |
| **TokenValidationResult** | Token state assessment | Created on-demand during validation | Not persisted |
| **PKCEPair** | PKCE security credentials | Generated per OAuth flow, used once | Not persisted |
| **OAuthConfiguration** | OAuth client credentials + settings | Loaded from SecureStorageManager at startup | OS Keychain (SecureStorageManager) |

**Key Design Principles**:
- **Immutability**: All entities are `record` types (immutable by default)
- **Explicit nullability**: All properties use nullable reference types (`string?`)
- **Validation methods**: Business logic embedded in entities (`IsAccessTokenExpired()`, `CanAutoRefresh`)
- **No sensitive data in logs**: ToString() implementations never expose tokens
- **Separation of concerns**: Entities only contain data + validation, no external dependencies
