# QuickStart Guide: Console Gmail OAuth

**Feature**: 056-console-gmail-oauth  
**Audience**: Developers integrating console OAuth  
**Time to complete**: 5-10 minutes

## Overview

This guide shows you how to integrate console-based Gmail OAuth authentication into your application, from initial setup to token refresh.

---

## Prerequisites

- .NET 9.0 SDK installed
- TrashMail Panda solution loaded in IDE
- Google Cloud Console project with OAuth 2.0 credentials
  - **Client ID** and **Client Secret** for a Desktop application
  - Redirect URI configured as `http://127.0.0.1:*` (wildcard port)

---

## Step 1: Install Dependencies

Add Spectre.Console to the main project:

```bash
cd src/TrashMailPanda/TrashMailPanda
dotnet add package Spectre.Console
```

Verify dependencies in `TrashMailPanda.csproj`:
```xml
<ItemGroup>
  <PackageReference Include="Spectre.Console" Version="0.48.0" />
  <PackageReference Include="Google.Apis.Gmail.v1" Version="1.67.0.3477" />
  <PackageReference Include="Google.Apis.Auth.OAuth2" Version="1.67.0" />
</ItemGroup>
```

---

## Step 2: Configure OAuth Credentials

### Option A: Store via Console UI (Recommended for Users)

On first run, the application will prompt:

```
[yellow]Gmail OAuth not configured[/]
Would you like to configure now? (Y/n): Y

Enter Gmail OAuth Client ID: YOUR_CLIENT_ID
Enter Gmail OAuth Client Secret: YOUR_CLIENT_SECRET

[green]✓ Credentials saved securely to OS keychain[/]
```

### Option B: Pre-configure via SecureStorageManager (For Testing)

```csharp
// In your test setup or initialization code
var secureStorage = serviceProvider.GetRequiredService<ISecureStorageManager>();

await secureStorage.StoreCredentialAsync(
    GmailStorageKeys.GMAIL_CLIENT_ID, 
    "YOUR_CLIENT_ID.apps.googleusercontent.com");

await secureStorage.StoreCredentialAsync(
    GmailStorageKeys.GMAIL_CLIENT_SECRET, 
    "YOUR_CLIENT_SECRET");
```

---

## Step 3: Check Authentication Status

On application startup, validate OAuth state:

```csharp
using Spectre.Console;
using TrashMailPanda.Services;
using TrashMailPanda.Shared.Base;

public async Task<bool> CheckGmailAuthenticationAsync(IServiceProvider services)
{
    var tokenValidator = services.GetRequiredService<ITokenValidator>();
    var validationResult = await tokenValidator.ValidateAsync();

    if (!validationResult.IsSuccess)
    {
        AnsiConsole.MarkupLine("[red]Error validating tokens[/]");
        return false;
    }

    var status = validationResult.Value;

    switch (status.Status)
    {
        case TokenStatus.Valid:
            AnsiConsole.MarkupLine("[green]✓ Gmail authenticated[/]");
            AnsiConsole.MarkupLine($"[dim]Token valid for {status.TimeUntilExpiry:hh\\:mm\\:ss}[/]");
            return true;

        case TokenStatus.ExpiredCanRefresh:
            AnsiConsole.MarkupLine("[yellow]⚠ Access token expired, refreshing...[/]");
            return await RefreshTokenAsync(services);

        case TokenStatus.NotAuthenticated:
            AnsiConsole.MarkupLine("[yellow]Gmail authentication required[/]");
            return await InitialAuthenticationAsync(services);

        case TokenStatus.RefreshTokenRevoked:
            AnsiConsole.MarkupLine("[red]✗ Refresh token revoked - re-authentication required[/]");
            await ClearTokensAndReauthenticateAsync(services);
            return false;

        default:
            AnsiConsole.MarkupLine("[red]Unknown token status[/]");
            return false;
    }
}
```

---

## Step 4: Initial OAuth Authentication

Trigger the full OAuth flow when no valid tokens exist:

```csharp
using Spectre.Console;
using TrashMailPanda.Services;
using TrashMailPanda.Models;

public async Task<bool> InitialAuthenticationAsync(IServiceProvider services)
{
    var oauthHandler = services.GetRequiredService<IConsoleOAuthHandler>();
    var secureStorage = services.GetRequiredService<ISecureStorageManager>();

    // 1. Load OAuth client credentials
    var clientIdResult = await secureStorage.RetrieveCredentialAsync(
        GmailStorageKeys.GMAIL_CLIENT_ID);
    var clientSecretResult = await secureStorage.RetrieveCredentialAsync(
        GmailStorageKeys.GMAIL_CLIENT_SECRET);

    if (!clientIdResult.IsSuccess || !clientSecretResult.IsSuccess)
    {
        AnsiConsole.MarkupLine("[red]✗ OAuth client credentials not configured[/]");
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

    // 3. Start OAuth flow
    AnsiConsole.MarkupLine("[cyan]ℹ Starting Gmail OAuth authentication...[/]");
    AnsiConsole.MarkupLine("[yellow]Note: You have 5 minutes to complete authentication[/]");

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

        AnsiConsole.MarkupLine($"[dim]Access token expires in: {tokens.ExpiresInSeconds / 3600.0:F1} hours[/]");
        return true;
    }
    else
    {
        AnsiConsole.MarkupLine($"[bold red]✗ Authentication failed:[/] [red]{authResult.Error.Message}[/]");

        // Offer retry
        if (AnsiConsole.Confirm("[cyan]Would you like to try again?[/]"))
        {
            return await InitialAuthenticationAsync(services);
        }

        return false;
    }
}
```

**What happens during OAuth flow:**

1. **Console output**: `[blue]Opening browser for Gmail authentication...[/]`
2. **Browser opens**: System default browser navigates to Google consent screen
3. **User authorizes**: Clicks "Allow" on Gmail permissions dialog
4. **Callback received**: Google redirects to `http://127.0.0.1:{port}/oauth/callback?code=...`
5. **Token exchange**: Authorization code exchanged for access + refresh tokens
6. **Secure storage**: Tokens saved to OS keychain via `SecureStorageManager`
7. **Success message**: `[green]✓ Gmail authentication successful[/]`

---

## Step 5: Automatic Token Refresh

When access token expires but refresh token is valid:

```csharp
public async Task<bool> RefreshTokenAsync(IServiceProvider services)
{
    var oauthHandler = services.GetRequiredService<IConsoleOAuthHandler>();
    var tokenValidator = services.GetRequiredService<ITokenValidator>();
    var secureStorage = services.GetRequiredService<ISecureStorageManager>();

    // 1. Load stored refresh token
    var storedTokensResult = await tokenValidator.LoadStoredTokensAsync();
    if (!storedTokensResult.IsSuccess)
    {
        AnsiConsole.MarkupLine("[red]✗ Could not load stored tokens[/]");
        return false;
    }

    var storedTokens = storedTokensResult.Value;

    // 2. Load OAuth client config
    var clientIdResult = await secureStorage.RetrieveCredentialAsync(
        GmailStorageKeys.GMAIL_CLIENT_ID);
    var clientSecretResult = await secureStorage.RetrieveCredentialAsync(
        GmailStorageKeys.GMAIL_CLIENT_SECRET);

    var config = new OAuthConfiguration
    {
        ClientId = clientIdResult.Value,
        ClientSecret = clientSecretResult.Value,
        Scopes = storedTokens.Scopes,
        RedirectUri = "http://127.0.0.1:0/oauth/callback" // Not used for refresh
    };

    // 3. Attempt refresh
    AnsiConsole.Status()
        .Spinner(Spinner.Known.Dots)
        .Start("[cyan]Refreshing access token...[/]", ctx =>
        {
            var refreshResult = await oauthHandler.RefreshTokenAsync(
                storedTokens.RefreshToken,
                config);

            if (refreshResult.IsSuccess)
            {
                AnsiConsole.MarkupLine("[green]✓ Token refreshed successfully[/]");
                return true;
            }
            else if (refreshResult.Error is AuthenticationError authError && 
                     authError.Message.Contains("invalid_grant"))
            {
                // Refresh token revoked - need full re-auth
                AnsiConsole.MarkupLine("[red]✗ Refresh token revoked[/]");
                AnsiConsole.MarkupLine("[cyan]Starting full re-authentication...[/]");
                
                await oauthHandler.ClearAuthenticationAsync();
                return await InitialAuthenticationAsync(services);
            }
            else
            {
                AnsiConsole.MarkupLine($"[red]✗ Refresh failed: {refreshResult.Error.Message}[/]");
                return false;
            }
        });
}
```

---

## Step 6: Using Tokens with Gmail API

After successful authentication, tokens are automatically used by Gmail provider:

```csharp
public async Task<Result<List<Email>>> FetchEmailsAsync(IServiceProvider services)
{
    var emailProvider = services.GetRequiredService<IEmailProvider>();

    // Gmail provider automatically loads tokens from SecureStorageManager
    var healthResult = await emailProvider.HealthCheckAsync();
    
    if (!healthResult.IsSuccess)
    {
        AnsiConsole.MarkupLine("[red]✗ Gmail provider not ready[/]");
        return Result<List<Email>>.Failure(healthResult.Error);
    }

    // Fetch emails - provider handles token refresh transparently
    var emailsResult = await emailProvider.GetEmailsAsync(
        maxResults: 50,
        query: "is:unread");

    if (emailsResult.IsSuccess)
    {
        AnsiConsole.MarkupLine($"[green]✓ Retrieved {emailsResult.Value.Count} emails[/]");
    }
    else
    {
        AnsiConsole.MarkupLine($"[red]✗ Failed to fetch emails: {emailsResult.Error.Message}[/]");
    }

    return emailsResult;
}
```

---

## Step 7: Error Handling Patterns

### Common Error Scenarios

```csharp
public async Task<Result<bool>> HandleOAuthErrorsAsync(Result<OAuthFlowResult> authResult)
{
    if (authResult.IsSuccess)
        return Result<bool>.Success(true);

    var error = authResult.Error;

    switch (error)
    {
        case AuthenticationError authError when authError.Message.Contains("access_denied"):
            AnsiConsole.MarkupLine("[red]✗ You denied access to Gmail[/]");
            AnsiConsole.MarkupLine("[cyan]Grant permissions to use TrashMail Panda[/]");
            return Result<bool>.Failure(authError);

        case NetworkError netError:
            AnsiConsole.MarkupLine($"[red]✗ Network error: {netError.Message}[/]");
            AnsiConsole.MarkupLine("[cyan]Check your internet connection and try again[/]");
            return Result<bool>.Failure(netError);

        case ConfigurationError configError:
            AnsiConsole.MarkupLine($"[red]✗ Configuration error: {configError.Message}[/]");
            AnsiConsole.MarkupLine("[yellow]Run configuration setup:[/] settings → Gmail OAuth");
            return Result<bool>.Failure(configError);

        case ProcessingError procError when procError.Message.Contains("timeout"):
            AnsiConsole.MarkupLine("[yellow]⚠ Authentication timed out (5 minutes)[/]");
            
            if (AnsiConsole.Confirm("[cyan]Try again?[/]"))
            {
                // Retry the flow
                return await InitialAuthenticationAsync(_services);
            }
            return Result<bool>.Failure(procError);

        default:
            AnsiConsole.MarkupLine($"[red]✗ Unexpected error: {error.Message}[/]");
            return Result<bool>.Failure(error);
    }
}
```

---

## Complete Example: Application Startup

```csharp
public class Program
{
    public static async Task<int> Main(string[] args)
    {
        // 1. Setup DI container
        var services = ConfigureServices();
        
        // 2. Display banner
        AnsiConsole.Write(
            new FigletText("TrashMail Panda")
                .LeftJustified()
                .Color(Color.Cyan));

        AnsiConsole.MarkupLine("[dim]Version 2.0 - ML Edition[/]\n");

        // 3. Initialize providers
        AnsiConsole.MarkupLine("[blue]Checking provider status...[/]");
        
        var gmailReady = await CheckGmailAuthenticationAsync(services);
        var storageReady = await CheckStorageProviderAsync(services);

        if (!gmailReady || !storageReady)
        {
            AnsiConsole.MarkupLine("\n[yellow]⚠ Configuration required before continuing[/]");
            return 1; // Exit code: configuration error
        }

        // 4. Provider health dashboard
        var table = new Table()
            .Title("[cyan]Provider Status[/]")
            .AddColumn("[green]Provider[/]")
            .AddColumn("[green]Status[/]");

        table.AddRow("Gmail", "[green]✓ Connected[/]");
        table.AddRow("Storage", "[green]✓ Ready[/]");
        table.AddRow("ML Model", "[yellow]⚠ Training required[/]");

        AnsiConsole.Write(table);

        // 5. Main application workflow
        AnsiConsole.MarkupLine("\n[bold]Select mode:[/]");
        var mode = AnsiConsole.Prompt(
            new SelectionPrompt<string>()
                .AddChoices("Runtime Mode", "Training Mode", "Settings", "Quit"));

        return mode switch
        {
            "Runtime Mode" => await RunRuntimeModeAsync(services),
            "Training Mode" => await RunTrainingModeAsync(services),
            "Settings" => await ShowSettingsMenuAsync(services),
            _ => 0
        };
    }

    private static ServiceProvider ConfigureServices()
    {
        var services = new ServiceCollection();

        // Logging
        services.AddLogging(builder =>
        {
            builder.AddConsole();
            builder.SetMinimumLevel(LogLevel.Information);
        });

        // Security & Storage
        services.AddSingleton<ISecureStorageManager, SecureStorageManager>();
        services.AddSingleton<ICredentialEncryption, CredentialEncryption>();

        // OAuth Services
        services.AddSingleton<IConsoleOAuthHandler, ConsoleOAuthHandler>();
        services.AddSingleton<ITokenValidator, TokenValidator>();
        services.AddTransient<ILocalOAuthCallbackListener, LocalOAuthCallbackListener>();

        // Providers
        services.AddSingleton<IEmailProvider, GmailEmailProvider>();
        services.AddSingleton<IStorageProvider, SqliteStorageProvider>();

        return services.BuildServiceProvider();
    }
}
```

---

## Troubleshooting

### Issue: Browser doesn't open

**Symptoms**: `[red]✗ Could not open browser[/]`

**Solutions**:
1. Check if default browser is configured in OS
2. Manually open URL displayed in console
3. Use device code flow (future enhancement)

---

### Issue: "invalid_grant" error on refresh

**Symptoms**: `[red]✗ Refresh token revoked[/]`

**Cause**: User revoked app access in Google Account settings OR token expired

**Solution**: 
```csharp
await oauthHandler.ClearAuthenticationAsync();
await InitialAuthenticationAsync(services); // Full re-auth required
```

---

### Issue: Timeout after 5 minutes

**Symptoms**: `[yellow]⚠ Authentication timed out[/]`

**Cause**: User didn't complete authorization within 5 minutes

**Solution**: Retry the flow - timeout automatically cleans up listener

---

## Next Steps

- **Production deployment**: Set up Google Cloud Console OAuth consent screen
- **Multi-account support**: Implement account switcher (future)
- **Device code flow**: Fallback for headless environments (future)
- **Token rotation service**: Proactive refresh before expiry (already implemented in `TokenRotationService`)

---

## Additional Resources

- [OAuth 2.0 for Native Apps (RFC 8252)](https://www.rfc-editor.org/rfc/rfc8252)
- [PKCE Best Practices (RFC 7636)](https://www.rfc-editor.org/rfc/rfc7636)
- [Google OAuth 2.0 Documentation](https://developers.google.com/identity/protocols/oauth2/native-app)
- [Spectre.Console Documentation](https://spectreconsole.net/)
- Project docs: `docs/providers/GMAIL_OAUTH_IMPLEMENTATION.md`
