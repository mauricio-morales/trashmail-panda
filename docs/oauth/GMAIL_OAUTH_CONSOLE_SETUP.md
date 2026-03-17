# Gmail OAuth Console Setup Guide

**Target Audience**: Developers integrating console-based Gmail OAuth  
**Related Feature**: 056-console-gmail-oauth  
**Last Updated**: March 16, 2026

## Overview

TrashMail Panda uses OAuth 2.0 with PKCE (Proof Key for Code Exchange) for secure Gmail authentication. This guide covers the complete setup process for console-based OAuth flows with colored terminal output using Spectre.Console.

## Architecture

### Components

- **IGoogleOAuthHandler**: Orchestrates the full OAuth flow (browser launch, callback handling, token storage)
- **IGoogleTokenValidator**: Validates existing tokens and determines authentication state
- **ILocalOAuthCallbackListener**: HTTP listener for OAuth callbacks (localhost:dynamic-port)
- **OAuthErrorHandler**: User-friendly error messaging with Spectre.Console formatting
- **SecureStorageManager**: OS keychain integration for token storage

### Security Features

- **PKCE**: SHA256 code challenge/verifier pairs (RFC 7636 compliant)
- **State Parameter**: CSRF protection via random state validation
- **Localhost-only**: Callback listener restricted to 127.0.0.1
- **OS Keychain**: Tokens stored in macOS Keychain, Windows DPAPI, or Linux libsecret
- **No Plaintext**: Tokens never stored in database or config files

## Google Cloud Console Setup

### Step 1: Create OAuth 2.0 Credentials

1. Go to [Google Cloud Console](https://console.cloud.google.com/)
2. Create or select a project
3. Enable Gmail API:
   - Navigate to "APIs & Services" → "Library"
   - Search for "Gmail API"
   - Click "Enable"

4. Create OAuth credentials:
   - Go to "APIs & Services" → "Credentials"
   - Click "Create Credentials" → "OAuth 2.0 Client ID"
   - Application type: **Desktop application**
   - Name: "TrashMail Panda Console"
   - Click "Create"

5. Note your credentials:
   - **Client ID**: `XXXXX.apps.googleusercontent.com`
   - **Client Secret**: `GOCSPX-XXXXX`

### Step 2: Configure Redirect URI

The console OAuth implementation uses dynamic port allocation (`http://127.0.0.1:0/oauth/callback`).

In Google Cloud Console:
- Go to your OAuth client → Edit
- **Authorized redirect URIs**: `http://127.0.0.1:*` (wildcard for any port)

**Note**: Google may not support wildcard ports. If so, you can:
- Add multiple specific URIs: `http://127.0.0.1:8080`, `http://127.0.0.1:8081`, etc.
- Or use a fixed port in configuration

### Step 3: Configure Scopes

Required Gmail API scope:
- `https://www.googleapis.com/auth/gmail.modify` - Read, send, delete, and manage email

For read-only access (testing):
- `https://www.googleapis.com/auth/gmail.readonly` - View email only

## Console Application Integration

### Dependency Injection Setup

Register OAuth services in your DI container:

```csharp
// In ServiceCollectionExtensions.cs or Startup.cs
services.AddSingleton<IGoogleOAuthHandler, GoogleOAuthHandler>();
services.AddSingleton<IGoogleTokenValidator, GoogleTokenValidator>();

// Factory for callback listener (transient - new instance per OAuth flow)
services.AddTransient<ILocalOAuthCallbackListener, LocalOAuthCallbackListener>();
```

### Startup Authentication Check

Check authentication status on application startup:

```csharp
using TrashMailPanda.Services;
using TrashMailPanda.Models;
using Spectre.Console;

public async Task<bool> CheckGmailAuthenticationAsync(
    IGoogleTokenValidator tokenValidator,
    IGoogleOAuthHandler oauthHandler,
    ISecureStorageManager secureStorage)
{
    // 1. Validate existing tokens
    var validationResult = await tokenValidator.ValidateAsync();
    
    if (!validationResult.IsSuccess)
    {
        OAuthErrorHandler.DisplayError(validationResult.Error, allowRetry: false);
        return false;
    }

    var status = validationResult.Value;

    // 2. Handle different token states
    switch (status.Status)
    {
        case TokenStatus.Valid:
            AnsiConsole.MarkupLine("[green]✓ Gmail authenticated[/]");
            AnsiConsole.MarkupLine($"[dim]Token valid for {status.TimeUntilExpiry:hh\\:mm\\:ss}[/]");
            return true;

        case TokenStatus.ExpiredCanRefresh:
            return await RefreshTokenAsync(oauthHandler, secureStorage);

        case TokenStatus.NotAuthenticated:
            return await InitialAuthenticationAsync(oauthHandler, secureStorage);

        case TokenStatus.RefreshTokenRevoked:
            AnsiConsole.MarkupLine("[red]✗ Refresh token revoked - re-authentication required[/]");
            await oauthHandler.ClearAuthenticationAsync();
            return await InitialAuthenticationAsync(oauthHandler, secureStorage);

        default:
            AnsiConsole.MarkupLine("[red]Unknown token status[/]");
            return false;
    }
}
```

### Initial OAuth Flow

Execute the full OAuth flow for new users:

```csharp
private async Task<bool> InitialAuthenticationAsync(
    IGoogleOAuthHandler oauthHandler,
    ISecureStorageManager secureStorage)
{
    // 1. Load OAuth client credentials from secure storage
    var clientIdResult = await secureStorage.RetrieveCredentialAsync("gmail_client_id");
    var clientSecretResult = await secureStorage.RetrieveCredentialAsync("gmail_client_secret");

    if (!clientIdResult.IsSuccess || !clientSecretResult.IsSuccess)
    {
        AnsiConsole.MarkupLine("[red]✗ OAuth credentials not configured[/]");
        AnsiConsole.MarkupLine("[yellow]Run setup to configure Gmail OAuth credentials[/]");
        return false;
    }

    // 2. Build OAuth configuration
    var config = new OAuthConfiguration
    {
        ClientId = clientIdResult.Value,
        ClientSecret = clientSecretResult.Value,
        Scopes = new[] { "https://www.googleapis.com/auth/gmail.modify" },
        RedirectUri = "http://127.0.0.1:0/oauth/callback", // Dynamic port
        Timeout = TimeSpan.FromMinutes(5)
    };

    // 3. Execute OAuth flow
    AnsiConsole.MarkupLine("[bold blue]Starting Gmail OAuth authentication...[/]");
    
    var authResult = await oauthHandler.AuthenticateAsync(config);

    // 4. Handle result
    if (authResult.IsSuccess)
    {
        var tokens = authResult.Value;
        AnsiConsole.MarkupLine("[green]✓ Authentication successful![/]");
        
        if (!string.IsNullOrEmpty(tokens.UserEmail))
        {
            AnsiConsole.MarkupLine($"[cyan]Authenticated as:[/] {tokens.UserEmail}");
        }

        return true;
    }
    else
    {
        OAuthErrorHandler.DisplayError(authResult.Error, allowRetry: true);
        
        // Offer retry
        if (OAuthErrorHandler.PromptRetry("authentication"))
        {
            return await InitialAuthenticationAsync(oauthHandler, secureStorage);
        }

        return false;
    }
}
```

### Token Refresh

Automatically refresh expired tokens:

```csharp
private async Task<bool> RefreshTokenAsync(
    IGoogleOAuthHandler oauthHandler,
    ISecureStorageManager secureStorage)
{
    AnsiConsole.MarkupLine("[yellow]⚠ Access token expired, refreshing...[/]");

    // 1. Load refresh token
    var refreshTokenResult = await secureStorage.RetrieveCredentialAsync("gmail_refresh_token");
    
    if (!refreshTokenResult.IsSuccess)
    {
        AnsiConsole.MarkupLine("[red]✗ Refresh token not found[/]");
        return await InitialAuthenticationAsync(oauthHandler, secureStorage);
    }

    // 2. Load OAuth configuration
    var clientIdResult = await secureStorage.RetrieveCredentialAsync("gmail_client_id");
    var clientSecretResult = await secureStorage.RetrieveCredentialAsync("gmail_client_secret");

    var config = new OAuthConfiguration
    {
        ClientId = clientIdResult.Value,
        ClientSecret = clientSecretResult.Value,
        Scopes = new[] { "https://www.googleapis.com/auth/gmail.modify" }
    };

    // 3. Refresh token
    var refreshResult = await oauthHandler.RefreshTokenAsync(
        refreshTokenResult.Value, 
        config);

    if (refreshResult.IsSuccess)
    {
        AnsiConsole.MarkupLine("[green]✓ Token refreshed successfully[/]");
        return true;
    }
    else
    {
        OAuthErrorHandler.DisplayError(refreshResult.Error, allowRetry: false);
        
        // If refresh failed with invalid_grant, clear tokens and re-authenticate
        if (refreshResult.Error.Message.Contains("revoked"))
        {
            await oauthHandler.ClearAuthenticationAsync();
            return await InitialAuthenticationAsync(oauthHandler, secureStorage);
        }

        return false;
    }
}
```

## Console OAuth Flow Experience

### Successful Flow

```
[bold blue]Starting Gmail OAuth authentication...[/]
[cyan]ℹ Generating security credentials (PKCE)...[/]
[cyan]ℹ Starting local callback listener...[/]
[blue]ℹ Opening browser for Gmail authentication...[/]
[yellow]Note: You have 5.0 minutes to complete authentication[/]

[cyan dots spinner] Waiting for authorization...

[green]✓ Authentication successful![/]
[cyan]Authenticated as:[/] user@gmail.com
```

### Browser Launch Failure (Fallback)

```
[yellow]⚠ Manual authentication required[/]

[cyan]1. Open your browser and visit:[/]
   https://accounts.google.com/o/oauth2/v2/auth?client_id=XXXXX...

[cyan]2. Authorize the application[/]
[cyan]3. You will be redirected back to the application[/]

[cyan dots spinner] Waiting for authorization...
```

### User Denial

```
[bold red]✗ Error:[/] [red]User denied authorization: access_denied[/]
[cyan]You can try again or contact support if the problem persists.[/]

[cyan]Would you like to retry authentication?[/] (yes/no): _
```

### Token Refresh

```
[yellow]⚠ Access token expired, refreshing...[/]
[cyan dots spinner] Refreshing access token...
[green]✓ Access token refreshed successfully[/]
```

### Refresh Token Revoked

```
[red]✗ Refresh token revoked - re-authentication required[/]
[yellow]⚠ Authentication cleared[/]

[bold blue]Starting Gmail OAuth authentication...[/]
...
```

## Storage Architecture

### Token Storage Keys

Tokens are stored in OS keychain with these keys:

- `gmail_access_token` - Short-lived access token (1 hour default)
- `gmail_refresh_token` - Long-lived refresh token (indefinite)
- `gmail_token_expiry` - Expiry seconds (e.g., "3600")
- `gmail_token_issued_utc` - Issue timestamp (ISO 8601)
- `gmail_user_email` - Authenticated user email
- `gmail_client_id` - OAuth client ID (from Google Cloud Console)
- `gmail_client_secret` - OAuth client secret (from Google Cloud Console)

### Keychain Integration

On each platform:

- **macOS**: Keychain Access (`security` command)
- **Windows**: DPAPI (`System.Security.Cryptography.ProtectedData`)
- **Linux**: libsecret (`secret-tool` command)

**Important**: Tokens are NEVER stored in the SQLite database.

## Error Handling

### Common Errors

#### Network Errors

```csharp
// Automatic retry with user-friendly message
HttpRequestException => "Network connection failed. Please check your internet connection."
SocketException => "Network connection failed. Please check firewall settings."
TimeoutException => "The operation timed out. Please try again."
```

#### OAuth Errors

```csharp
// User denied permissions
error=access_denied => "User denied authorization"

// Refresh token invalid
invalid_grant => "Refresh token revoked. Please re-authenticate."

// Rate limiting
429 => "Too many requests. Please wait a moment and try again."
```

#### Browser Launch Errors

```csharp
// Browser failed to open - show manual URL
Win32Exception => Display manual URL with instructions
```

### Error Display

All errors use `OAuthErrorHandler` for consistent formatting:

```csharp
try
{
    // OAuth operation
}
catch (Exception ex)
{
    OAuthErrorHandler.DisplayError(ex, allowRetry: true, logger);
}
```

Displays:
```
[bold red]✗ Error:[/] [red]{user-friendly message}[/]
[dim red]{technical details}[/]
[cyan]You can try again or contact support if the problem persists.[/]
```

## Testing & Validation

### Unit Tests

Test OAuth components in isolation:

```bash
# Test PKCE generation
dotnet test --filter "FullyQualifiedName~PKCEGeneratorTests"

# Test token validation
dotnet test --filter "FullyQualifiedName~GoogleTokenValidatorTests"

# Test callback listener
dotnet test --filter "FullyQualifiedName~LocalOAuthCallbackListenerTests"

# Test OAuth handler
dotnet test --filter "FullyQualifiedName~GoogleOAuthHandlerTests"
```

### Integration Tests

**Note**: Integration tests require real OAuth credentials and are skipped by default.

To run integration tests:

1. Set environment variables:
   ```bash
   export GMAIL_CLIENT_ID="your_client_id"
   export GMAIL_CLIENT_SECRET="your_client_secret"
   ```

2. Enable tests by removing `Skip` attribute:
   ```csharp
   // In OAuthFlowIntegrationTests.cs
   [Fact] // Remove: (Skip = "Requires OAuth...")
   public async Task FullOAuthFlow_ShouldSucceed()
   ```

3. Run integration tests:
   ```bash
   dotnet test --filter "Category=Integration&FullyQualifiedName~OAuth"
   ```

### Manual Testing

Test the complete flow manually:

```bash
# 1. Clear existing tokens
rm -rf ~/Library/Keychains/login.keychain-db  # macOS (cautious!)
# Or use dotnet app: await oauthHandler.ClearAuthenticationAsync()

# 2. Run application
dotnet run --project src/TrashMailPanda

# 3. Trigger OAuth flow
# Application should detect no tokens and initiate authentication

# 4. Complete browser flow
# - Authorize the application
# - Verify callback succeeds
# - Check tokens stored in keychain

# 5. Restart application
# Application should use stored tokens without re-authentication

# 6. Force token refresh
# Wait for token expiry or manually expire token
# Application should auto-refresh without user interaction
```

## Security Best Practices

### DO

- ✅ Store tokens in OS keychain only
- ✅ Use PKCE for all OAuth flows
- ✅ Validate state parameter on callback
- ✅ Use localhost-only callback listener
- ✅ Implement timeout for OAuth flows (5 minutes default)
- ✅ Log OAuth operations (without token values)
- ✅ Handle token refresh automatically
- ✅ Clear revoked tokens immediately

### DON'T

- ❌ Store tokens in database or config files
- ❌ Log token values (access_token, refresh_token)
- ❌ Use HTTP for OAuth (always HTTPS for Google endpoints)
- ❌ Accept callbacks from non-localhost origins
- ❌ Hard-code client secrets in source code
- ❌ Share OAuth credentials between users
- ❌ Skip PKCE validation

## Troubleshooting

### "Browser failed to open"

**Cause**: Process.Start() failed to launch browser

**Solution**: Display manual URL with `OAuthErrorHandler.DisplayManualUrlInstructions(authUrl)`

### "Callback timeout after 5 minutes"

**Cause**: User didn't complete OAuth flow in time

**Solution**: Increase timeout in `OAuthConfiguration.Timeout` or retry

### "State parameter mismatch"

**Cause**: CSRF attack or callback from different OAuth flow

**Solution**: Verify callback URL is localhost, check for multiple concurrent flows

### "Refresh token revoked"

**Cause**: User revoked access in Google Account settings, or token expired

**Solution**: Clear tokens with `ClearAuthenticationAsync()`, re-authenticate

### "Port already in use"

**Cause**: Callback listener port occupied by another process

**Solution**: Use dynamic port allocation (port 0) or configure different port

### "Unauthorized redirect_uri"

**Cause**: Redirect URI not configured in Google Cloud Console

**Solution**: Add `http://127.0.0.1:*` (or specific ports) to Authorized redirect URIs

## References

- [Google OAuth 2.0 Documentation](https://developers.google.com/identity/protocols/oauth2)
- [OAuth 2.0 for Native Apps (RFC 8252)](https://datatracker.ietf.org/doc/html/rfc8252)
- [PKCE (RFC 7636)](https://datatracker.ietf.org/doc/html/rfc7636)
- [Gmail API Scopes](https://developers.google.com/gmail/api/auth/scopes)
- [Spectre.Console Documentation](https://spectreconsole.net/)

## Changelog

- **2026-03-16**: Initial documentation for console-based OAuth flow
