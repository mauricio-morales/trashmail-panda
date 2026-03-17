# GitHub Copilot Instructions - TrashMail Panda

> **Note**: This project uses [spec-kit](.specify/) for feature planning. Core engineering principles are defined in [`.specify/memory/constitution.md`](.specify/memory/constitution.md).

## Project Overview

TrashMail Panda is an AI-powered email triage assistant for Gmail users, built with:
- **.NET 9.0** + **C# 12** (nullable reference types enabled)
- **Console-first TUI** (Terminal User Interface with Spectre.Console)
- **Local ML models** (transitioning from OpenAI to ML.NET for privacy)
- **Provider-agnostic architecture** (IEmailProvider, ILLMProvider, IStorageProvider)

**Architecture Evolution**: Moving from Avalonia desktop UI to console-based TUI to support:
- Lightweight, scriptable interface
- MCP (Model Context Protocol) integration
- Privacy-first local ML classification
- Cross-platform terminal experience

## Critical Patterns (Enforced by CI/CD)

### 1. Result Pattern (MANDATORY)
```csharp
// ✅ CORRECT - All providers return Result<T>
public async Task<Result<Email[]>> GetEmailsAsync()
{
    try
    {
        var emails = await _gmailService.FetchAsync();
        return Result.Success(emails);
    }
    catch (Exception ex)
    {
        return Result.Failure(new NetworkError($"Failed: {ex.Message}"));
    }
}

// ❌ NEVER throw from providers
public async Task<Email[]> GetEmailsAsync() // Wrong return type
{
    throw new InvalidOperationException(); // Never do this
}
```

### 2. One Public Type Per File
```csharp
// ✅ File: EmailService.cs
public class EmailService { }

// ❌ Don't put multiple public types in one file
public class EmailService { }
public class EmailHelper { } // Move to separate file
```

### 3. Console Output with Spectre.Console
```csharp
// ✅ Use Spectre.Console for colored output
using Spectre.Console;

AnsiConsole.MarkupLine("[green]✓ Operation successful[/]");
AnsiConsole.MarkupLine("[red]✗ Error occurred[/]");
AnsiConsole.MarkupLine("[yellow]⚠ Warning message[/]");

// ✅ Use semantic color scheme
// Green: Success, Yellow: Warning, Red: Error, Blue: Info, Cyan: Actions
```

### 4. Console UI Colors (Semantic Markup)
```csharp
// ✅ Use semantic color markup in console
AnsiConsole.MarkupLine("[green]✓[/] Provider ready");
AnsiConsole.MarkupLine("[bold red]✗[/] [red]Connection failed[/]");
AnsiConsole.MarkupLine("[cyan]→[/] Processing...");

// ❌ Don't use raw ANSI codes
Console.WriteLine("\u001b[32mSuccess\u001b[0m"); // Use Spectre.Console instead
```

### 5. Provider Implementation
```csharp
// ✅ All providers implement IProvider<TConfig>
public class MyProvider : IProvider<MyConfig>
{
    public async Task<Result<bool>> InitializeAsync(MyConfig config, CancellationToken ct)
    {
        // Provider setup
        return Result.Success(true);
    }

    public async Task<Result<bool>> HealthCheckAsync()
    {
        // Health validation
    }
}
```

### 6. Dependency Injection
```csharp
// ✅ Register in DI container
services.AddSingleton<IEmailProvider, GmailEmailProvider>();
services.AddOptions<GmailConfig>().ValidateDataAnnotations();
```

### 7. Console OAuth Pattern
```csharp
// ✅ OAuth flow with colored console output
public async Task<Result<OAuthFlowResult>> AuthenticateAsync()
{
    AnsiConsole.MarkupLine("[blue]ℹ Opening browser for authentication...[/]");
    
    var result = await _oauthHandler.AuthenticateAsync(config);
    
    if (result.IsSuccess)
    {
        AnsiConsole.MarkupLine("[green]✓ Authentication successful![/]");
        return result;
    }
    
    OAuthErrorHandler.DisplayError(result.Error, allowRetry: true);
    return result;
}
```

### 8. Security Rules
- **NEVER** log sensitive data (tokens, emails, API keys)
- **NEVER** commit secrets to version control
- Use `SecureStorageManager` for token storage (OS keychain)
- All database queries: parameterized statements only
- OAuth tokens: OS keychain (DPAPI/Keychain/libsecret), not database

## Project Structure

```
src/
├── TrashMailPanda/          # Main console app
│   ├── Services/            # OAuth, app services
│   ├── Models/              # OAuthFlowResult, OAuthConfiguration, etc.
│   └── Program.cs           # Console entry point
├── Shared/                  # Cross-cutting concerns
│   └── TrashMailPanda.Shared/
│       ├── Base/            # IProvider, Result<T>
│       ├── Security/        # SecureStorageManager
│       └── Models/          # DTOs
├── Providers/               # External integrations
│   ├── Email/               # GmailEmailProvider
│   ├── LLM/                 # OpenAIProvider (transitioning to ML.NET)
│   └── Storage/             # SqliteStorageProvider
└── Tests/                   # xUnit tests
```

## Console OAuth Services

- **IGoogleOAuthHandler** - Orchestrates OAuth 2.0 flow with PKCE
- **IGoogleTokenValidator** - Validates and refreshes tokens
- **ILocalOAuthCallbackListener** - HTTP listener for OAuth callbacks (localhost)
- **OAuthErrorHandler** - User-friendly error messages with Spectre.Console
- **PKCEGenerator** - Generates SHA256 code challenge/verifier pairs

**Storage Keys**: `gmail_client_id`, `gmail_client_secret`, `gmail_access_token`, `gmail_refresh_token`, `gmail_token_expiry`, `gmail_token_issued_utc`, `gmail_user_email`

**Documentation**: See [docs/oauth/GMAIL_OAUTH_CONSOLE_SETUP.md](docs/oauth/GMAIL_OAUTH_CONSOLE_SETUP.md)

## Quick Reference

| Task | Command |
|------|---------|
| Run app | `dotnet run --project src/TrashMailPanda` |
| Build | `dotnet build` |
| Format | `dotnet format` |
| Test | `dotnet test` |
| CI validation | `dotnet build -c Release && dotnet test && dotnet format --verify-no-changes` |

## Key Files

- **Constitution**: [`.specify/memory/constitution.md`](.specify/memory/constitution.md) - Engineering principles
- **Architecture**: Check provider interfaces in `src/Shared/TrashMailPanda.Shared/Base/`
- **Security**: `src/Shared/TrashMailPanda.Shared/Security/` - Encryption, keychain
- **OAuth Guide**: `docs/oauth/GMAIL_OAUTH_CONSOLE_SETUP.md` - Complete OAuth setup
- **ML Architecture**: `docs/architecture/ARCHITECTURE_SHIFT_TO_LOCAL_ML.md` - Console-first design

## Common Mistakes to Avoid

1. ❌ Throwing exceptions from providers → ✅ Return `Result.Failure()`
2. ❌ Hardcoding colors in Spectre.Console → ✅ Use semantic markup: `[green]`, `[red]`, `[cyan]`
3. ❌ Multiple public classes per file → ✅ One per file
4. ❌ Nullable warnings → ✅ Explicit `string?` or `string` types
5. ❌ Secrets in config → ✅ Environment variables + OS keychain
6. ❌ No tests → ✅ Maintain 90%+ coverage
7. ❌ Logging token values → ✅ Log operations only, never sensitive data

## Testing Categories

```csharp
[Trait("Category", "Unit")]        // Fast, no I/O
[Trait("Category", "Integration")] // Requires real APIs
[Trait("Category", "Security")]    // Security-specific
[Trait("Platform", "Windows")]     // Platform-specific
```

Integration tests requiring OAuth are skipped by default:
```csharp
[Fact(Skip = "Requires OAuth - see CLAUDE.md for setup")]
```

## Spec-Kit Workflow

```bash
# Create new feature
speckit specify "Add email filtering by sender"
speckit plan
speckit tasks
speckit implement

# Update constitution
speckit constitution
```

For detailed architectural guidance, see [`.specify/memory/constitution.md`](.specify/memory/constitution.md).
