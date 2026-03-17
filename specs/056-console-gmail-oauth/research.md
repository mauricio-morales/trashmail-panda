# Research: Console-based Gmail OAuth Flow

**Phase 0 Output** | **Date**: March 16, 2026  
**Feature**: Console-based Gmail OAuth with localhost callback

## Research Overview

This document consolidates research findings for implementing OAuth 2.0 authorization code flow in a console application with localhost HTTP callback, colored console output, and automatic token management.

---

## 1. Console UI Framework: Spectre.Console

### Decision
**Use Spectre.Console v0.48.0+** for colored console output and interactive prompts.

### Rationale
- Native .NET 9.0 support, cross-platform (Windows/macOS/Linux)
- Rich markup syntax for semantic colors: `[green]✓[/]`, `[bold red]✗[/]`, `[cyan]info[/]`
- Built-in interactive prompts (confirm, text input, selection menus)
- Progress bars and spinners for long-running operations
- Automatic ANSI capability detection with graceful fallback
- Respects `NO_COLOR` environment variable for accessibility
- Lightweight (~3 MB), zero external dependencies

### Alternatives Considered
- **Terminal.Gui**: More complex TUI framework - overkill for OAuth prompts, but may use for full console app (Issue #8 in architecture shift)
- **Console.WriteLine with ANSI codes**: Manual color management - error-prone and platform-dependent
- **No colors**: Poor UX - critical errors not visually distinct

### Implementation Pattern
```csharp
// Add to TrashMailPanda.csproj
<PackageReference Include="Spectre.Console" Version="0.48.0" />

// Usage for OAuth flow
AnsiConsole.MarkupLine("[blue]Opening browser for Gmail authentication...[/]");
AnsiConsole.Status()
    .Spinner(Spinner.Known.Dots)
    .Start("[cyan]Waiting for authorization...[/]", ctx => {
        // Wait for OAuth callback
    });
AnsiConsole.MarkupLine("[green]✓ Authentication successful[/]");

// Error handling with bold red
AnsiConsole.MarkupLine("[bold red]✗ Error:[/] [red]{error.Message}[/]");
```

### Color Strategy (from architectural guidelines)
- **Green** (`[green]✓[/]`): Success states, successful operations
- **Bold Red** (`[bold red]✗[/]`): Critical errors, authentication failures
- **Yellow** (`[yellow]⚠[/]`): Warnings, optional configuration
- **Blue** (`[blue]ℹ[/]`): Informational messages, progress updates
- **Cyan** (`[cyan]`): Highlights, email subjects, important data
- **Magenta** (`[magenta]`): Metrics, performance stats

---

## 2. Localhost OAuth Callback: HttpListener

### Decision
**Use `System.Net.HttpListener`** with dynamic port allocation (port 0 = OS assigns available port).

### Rationale
- **Native**: Built into .NET framework, no additional dependencies
- **Lightweight**: ~50KB vs Kestrel (~500KB+)
- **Fast startup**: <10ms vs Kestrel (~50-100ms)
- **Simple API**: Perfect for temporary localhost callback server
- **Cross-platform**: Works on Windows (http.sys), macOS/Linux (HttpListener implementation)

### Alternatives Considered
- **Kestrel**: ASP.NET Core web server - too complex for OAuth callback, designed for full web apps
- **TCP listener**: Lower-level networking - would need HTTP parsing manually
- **External HTTP server**: Out-of-process dependency - adds complexity

### Implementation Pattern
```csharp
public class LocalhostOAuthListener : IAsyncDisposable
{
    private readonly HttpListener _listener = new();
    
    public async Task<int> StartAsync(CancellationToken ct = default)
    {
        // Port 0 = OS picks available port dynamically
        _listener.Prefixes.Add("http://127.0.0.1:0/oauth/callback");
        _listener.Start();
        
        // Extract assigned port
        var endpoint = (IPEndPoint?)_listener.LocalEndpoint;
        int port = endpoint?.Port ?? 8080;
        
        return port;
    }
    
    public async Task<(string code, string state)> WaitForCallbackAsync(TimeSpan timeout)
    {
        using var cts = new CancellationTokenSource(timeout);
        var context = await _listener.GetContextAsync();
        var queryParams = context.Request.QueryString;
        
        return (queryParams["code"], queryParams["state"]);
    }
}
```

### Security Implementation
- **Localhost only**: Listen on `127.0.0.1` (not `0.0.0.0`) to prevent external access
- **Dynamic port**: Avoids port conflicts with other applications
- **Timeout protection**: 5-minute maximum wait for OAuth callback
- **State validation**: CSRF protection via state parameter matching
- **PKCE required**: Proof Key for Code Exchange (SHA256 challenge) prevents authorization code interception

---

## 3. Browser Launch: Cross-Platform Process.Start

### Decision
**Use `Process.Start` with platform-specific URL handlers** via `RuntimeInformation.IsOSPlatform`.

### Rationale
- **Native**: Uses OS default browser (user's preferred browser)
- **Security**: System browser is more trusted than embedded browser views
- **Cross-platform**: Standard pattern across Windows/macOS/Linux
- **No dependencies**: Built into .NET

### Implementation Pattern
```csharp
public static async Task<Result<bool>> LaunchBrowserAsync(string url)
{
    try
    {
        if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
        {
            Process.Start(new ProcessStartInfo(url) { UseShellExecute = true });
        }
        else if (RuntimeInformation.IsOSPlatform(OSPlatform.OSX))
        {
            Process.Start("open", url);
        }
        else if (RuntimeInformation.IsOSPlatform(OSPlatform.Linux))
        {
            // Try xdg-open first, fallback to firefox/chromium
            Process.Start("xdg-open", url);
        }
        
        AnsiConsole.MarkupLine("[green]✓ Browser opened[/]");
        return Result<bool>.Success(true);
    }
    catch (Exception ex)
    {
        AnsiConsole.MarkupLine("[bold red]✗ Could not open browser[/]");
        return Result<bool>.Failure(new NetworkError($"Browser launch failed: {ex.Message}"));
    }
}
```

### Fallback Strategy
If browser launch fails, display console instructions:
```
[bold red]✗ Could not open browser automatically[/]
[yellow]Manual authentication required:[/]
[cyan]1. Open your browser and visit:[/]
   https://accounts.google.com/o/oauth2/v2/auth?...
[cyan]2. Authorize the application[/]
[cyan]3. You will be redirected to: http://127.0.0.1:{port}/oauth/callback[/]
```

---

## 4. Token Validation & Automatic Refresh

### Decision
**Use `Google.Apis.Auth.OAuth2.UserCredential`** with local expiry checks and automatic refresh via existing `SecureStorageManager`.

### Rationale
- **No unnecessary API calls**: Check `TokenResponse.IsStale` locally (compares `DateTime.UtcNow` vs stored `IssuedUtc + ExpiresInSeconds`)
- **Transparent refresh**: Google client library auto-refreshes on next API call if stale
- **Existing integration**: Reuses `GmailEmailProvider` patterns and `SecureStorageManager`
- **Three-tier caching**: In-memory session cache → Encrypted database → OS keychain

### Token Storage Schema
```csharp
// Storage keys (from existing GmailStorageKeys.cs)
"gmail_access_token"      // Short-lived (1 hour)
"gmail_refresh_token"     // Long-lived (indefinite until revoked)
"gmail_token_expiry"      // ExpiresInSeconds (e.g., "3600")
"gmail_token_issued_utc"  // ISO 8601 timestamp (e.g., "2026-03-16T10:30:00Z")
"gmail_user_email"        // User's Gmail address (for display)
```

### Automatic Refresh Flow
```csharp
// On application startup:
1. Load tokens from SecureStorageManager (OS keychain)
2. Check if access token is stale:
   - IsStale = (IssuedUtc + TimeSpan.FromSeconds(ExpiresInSeconds)) < DateTime.UtcNow
3. If stale:
   - Call userCredential.RefreshTokenAsync() 
   - Google library POSTs to https://oauth2.googleapis.com/token with refresh_token
   - Store new access_token + updated issued_utc
4. If refresh fails with invalid_grant:
   - Refresh token revoked/expired
   - Clear all stored tokens
   - Trigger full OAuth re-authentication
```

### Error Handling Decision Matrix
| Error Code | Meaning | Action | Retryable |
|------------|---------|--------|-----------|
| `401` | Token invalid/expired | Try refresh | Yes (once) |
| `invalid_grant` | Refresh token revoked | Clear tokens, re-auth | No |
| `invalid_client` | Client credentials wrong | Check config, fail | No |
| `429` | Rate limited | Exponential backoff | Yes (3x) |
| `5xx` | Server error | Exponential backoff | Yes (3x) |

---

## 5. OAuth Security Implementation: PKCE

### Decision
**Implement PKCE (Proof Key for Code Exchange)** with SHA256 code challenge as required by OAuth 2.0 best practices for native apps.

### Rationale
- **Prevents authorization code interception**: Attackers cannot exchange authorization code for tokens without code_verifier
- **RFC 7636 compliant**: Standard for native/public clients (desktop, mobile, console apps)
- **Google recommendation**: Required for desktop OAuth flows
- **No client secret exposure**: Reduces risk of credential leakage

### Implementation Pattern
```csharp
public (string challenge, string verifier) GeneratePKCEPair()
{
    // 1. Generate random 128-byte code_verifier
    using var rng = RandomNumberGenerator.Create();
    var randomBytes = new byte[128];
    rng.GetBytes(randomBytes);
    string codeVerifier = Base64UrlEncode(randomBytes);
    
    // 2. SHA256 hash of verifier = code_challenge
    using var sha256 = SHA256.Create();
    var challengeBytes = sha256.ComputeHash(Encoding.UTF8.GetBytes(codeVerifier));
    string codeChallenge = Base64UrlEncode(challengeBytes);
    
    return (codeChallenge, codeVerifier);
}

// Authorization URL includes code_challenge:
authUrl += $"&code_challenge={codeChallenge}&code_challenge_method=S256";

// Token exchange includes code_verifier:
POST /token
  grant_type=authorization_code
  code={authCode}
  code_verifier={codeVerifier}  // ← CRITICAL: proves caller generated challenge
  client_id={clientId}
  redirect_uri={redirectUri}
```

### Additional Security Measures
- **State parameter**: Random GUID for CSRF protection (validate match on callback)
- **Localhost only**: Listener binds to `127.0.0.1` (not `0.0.0.0`)
- **Timeout protection**: 5-minute max for entire OAuth flow
- **Prompt=consent**: Forces consent screen to ensure refresh_token returned
- **Access_type=offline**: Requests refresh token for long-lived access

---

## 6. Timeout Strategy

### Decision
**Implement three-tier timeout system** with clear user feedback at each stage.

### Timeout Configuration
```csharp
public static class OAuthTimeouts
{
    public static TimeSpan BrowserLaunch => TimeSpan.FromSeconds(10);   // Browser must open
    public static TimeSpan UserAuthorization => TimeSpan.FromMinutes(5); // User completes flow
    public static TimeSpan TokenExchange => TimeSpan.FromSeconds(30);    // Network round-trip
    public static TimeSpan Total => UserAuthorization;                   // Overall limit
}
```

### Rationale
- **Browser launch**: 10 seconds detects "browser not installed" or launch failure quickly
- **User authorization**: 5 minutes allows user to read consent screen, enter password if needed
- **Token exchange**: 30 seconds for HTTPS POST to Google (network latency + processing)

### User Experience
```csharp
// Before timeout
AnsiConsole.MarkupLine("[yellow]Note: You have 5 minutes to complete authentication[/]");

// During wait
AnsiConsole.Status()
    .Spinner(Spinner.Known.Dots)
    .Start("[cyan]Waiting for authorization... (timeout in 5:00)[/]", ctx => { ... });

// On timeout
AnsiConsole.MarkupLine("[yellow]⚠ Authentication timed out after 5 minutes[/]");
AnsiConsole.MarkupLine("[cyan]Would you like to try again? (Y/n):[/]");
```

---

## 7. Error Recovery & User Guidance

### Decision
**Implement user-friendly error messages with recovery options** using Spectre.Console formatting.

### Error Message Pattern
```csharp
public class OAuthErrorHandler
{
    public void DisplayError(Exception ex, bool allowRetry = true)
    {
        var (userMessage, technicalDetails, isRetryable) = MapError(ex);
        
        // Always bold red for errors
        AnsiConsole.MarkupLine($"[bold red]✗ Error:[/] {userMessage}");
        
        if (!string.IsNullOrEmpty(technicalDetails))
        {
            AnsiConsole.MarkupLine($"[dim red]Details: {technicalDetails}[/]");
        }
        
        if (isRetryable && allowRetry)
        {
            AnsiConsole.MarkupLine("[cyan][Press Enter to retry][/]");
        }
        
        _logger.LogError(ex, "OAuth error: {Technical}", technicalDetails);
    }
}
```

### Common Error Scenarios
| Scenario | User Message | Recovery |
|----------|--------------|----------|
| Browser fails to open | `[bold red]✗ Could not open browser[/]`<br>`[yellow]Visit manually:[/] {url}` | Display full URL |
| User denies permissions | `[bold red]✗ You denied access to Gmail[/]`<br>`[cyan]Grant permissions[/] to continue` | Retry flow |
| Network timeout | `[bold red]✗ Network connection failed[/]`<br>`[cyan]Check internet and retry[/]` | Retry flow |
| Refresh token expired | `[yellow]⚠ Gmail session expired[/]`<br>`[cyan]Re-authentication required[/]` | Start new OAuth |
| Invalid client credentials | `[bold red]✗ Invalid OAuth configuration[/]`<br>`[yellow]Check client ID/secret in settings[/]` | No retry - config needed |

---

## 8. Integration with Existing Codebase

### Reuse Existing Components
- **SecureStorageManager**: Store/retrieve OAuth tokens in OS keychain
- **GmailStorageKeys**: Use existing constants (`GMAIL_ACCESS_TOKEN`, `GMAIL_REFRESH_TOKEN`, etc.)
- **Result Pattern**: All methods return `Result<T>` (no exceptions for auth failures)
- **Provider Interfaces**: Integrate with `IEmailProvider` health check system
- **Logging**: Use `ILogger<T>` for diagnostics (never log tokens)

### New Components Required
- **ConsoleOAuthHandler**: Main OAuth orchestration service
- **LocalhostOAuthListener**: HTTP callback server
- **OAuthErrorHandler**: User-friendly error messaging
- **TokenValidator**: Check token validity and trigger refresh
- **ConsoleFormatting**: Centralized Spectre.Console helpers (similar to `ProfessionalColors` for XAML)

### Dependency Injection Registration
```csharp
// Program.cs / Startup configuration
services.AddSingleton<ConsoleOAuthHandler>();
services.AddSingleton<ISecureStorageManager, SecureStorageManager>();
services.AddLogging();

// Existing providers already registered
services.AddSingleton<IEmailProvider, GmailEmailProvider>();
```

---

## 9. Testing Strategy

### Unit Tests (90% coverage target)
- PKCE generation (verify SHA256, base64url encoding)
- Token validation logic (IsStale calculation)
- Error mapping (exception → user message)
- State parameter matching (CSRF detection)
- Timeout handling (CancellationToken propagation)

### Integration Tests (manual OAuth required)
```csharp
[Fact(Skip = "Requires real OAuth credentials and browser interaction")]
public async Task CompleteOAuthFlow_WithRealGmail_Success()
{
    // Setup: Load real client_id/client_secret from environment
    var handler = new ConsoleOAuthHandler(...);
    
    // Act: Run full OAuth flow (opens browser)
    var result = await handler.AuthenticateAsync(...);
    
    // Assert: Tokens stored in OS keychain
    Assert.True(result.IsSuccess);
    var storedRefreshToken = await _secureStorage.RetrieveCredentialAsync("gmail_refresh_token");
    Assert.True(storedRefreshToken.IsSuccess);
}
```

### Manual Testing Checklist
- [ ] OAuth flow completes on Windows (Chrome, Edge, Firefox)
- [ ] OAuth flow completes on macOS (Safari, Chrome)
- [ ] OAuth flow completes on Linux (Firefox, Chromium)
- [ ] Timeout handling after 5 minutes
- [ ] Browser launch failure shows fallback URL
- [ ] User denial displays retry option
- [ ] Refresh token auto-refresh works on startup
- [ ] `invalid_grant` triggers re-authentication
- [ ] Tokens persist across application restarts
- [ ] Console colors render correctly in Terminal.app, Windows Terminal, GNOME Terminal

---

## 10. Performance Targets

| Operation | Target | Rationale |
|-----------|--------|-----------|
| HTTP listener startup | <100ms | HttpListener is lightweight |
| Browser launch | <2s | OS command execution |
| OAuth callback processing | <50ms | Parse query string, validate state |
| Token storage (OS keychain) | <200ms | DPAPI/Keychain API call |
| Token validation (local) | <10ms | DateTime comparison only |
| Token refresh (network) | <3s | HTTPS POST to Google + storage |
| Total OAuth flow | <90s | User interaction dominates |

---

## 11. Migration Path & Architectural Context

**Context**: This feature is **Issue #3** in the broader [ARCHITECTURE_SHIFT_TO_LOCAL_ML.md](docs/architecture/ARCHITECTURE_SHIFT_TO_LOCAL_ML.md).

### Current State (Before)
- Avalonia UI-based OAuth flow via `GmailOAuthService.cs`
- Browser-based callback with `GoogleWebAuthorizationBroker`
- Tokens stored in OS keychain via `SecureStorageManager`

### New State (After This Feature)
- **Addition**: Console-based OAuth handler for CLI workflows
- **Preservation**: Existing Avalonia OAuth still works (backward compatible)
- **Reuse**: Same token storage infrastructure (`SecureStorageManager`)
- **Preparation**: Establishes console interaction patterns for full TUI (Issue #8)

### Future Transition (Issue #8 - Console TUI)
- Move `ConsoleOAuthHandler` to new `TrashMailPanda.Console` project
- Deprecate Avalonia UI (keep for reference, but make console default)
- Full console-based runtime mode (email classification with colored prompts)

**Key Design Principle**: This OAuth handler is **portable** - designed to work in current Avalonia app AND future console-only app.

---

## Summary of Decisions

| Component | Technology Choice | Rationale |
|-----------|-------------------|-----------|
| **Console UI** | Spectre.Console v0.48.0+ | Rich colors, cross-platform, lightweight |
| **HTTP Listener** | System.Net.HttpListener | Native, fast startup, simple API |
| **Browser Launch** | Process.Start + RuntimeInformation | OS default browser, cross-platform |
| **Token Storage** | Existing SecureStorageManager | OS keychain integration, encryption |
| **Token Validation** | Local IsStale check | No API calls, instant validation |
| **Token Refresh** | Google.Apis.Auth.OAuth2 | Automatic refresh, transparent to caller |
| **Security** | PKCE (SHA256) + State param | RFC 7636 compliant, CSRF protection |
| **Timeout** | 5-minute CancellationToken | User-friendly, prevents hang |
| **Error Handling** | Result<T> pattern | Explicit failures, no exceptions |

**All unknowns resolved** - ready for Phase 1: Design.
