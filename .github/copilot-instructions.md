# GitHub Copilot Instructions - TrashMail Panda

> **Note**: This project uses [spec-kit](.specify/) for feature planning. Core engineering principles are defined in [`.specify/memory/constitution.md`](.specify/memory/constitution.md).

## Project Overview

TrashMail Panda is an AI-powered email triage assistant for Gmail users, built with:
- **.NET 9.0** + **C# 12** (nullable reference types enabled)
- **Avalonia UI 11** (cross-platform desktop)
- **CommunityToolkit.Mvvm** (MVVM pattern)
- **Provider-agnostic architecture** (IEmailProvider, ILLMProvider, IStorageProvider)

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

### 3. MVVM with CommunityToolkit
```csharp
// ✅ ViewModel pattern
public partial class MyViewModel : ObservableObject
{
    [ObservableProperty]
    private string _title = "Default";

    [RelayCommand]
    private async Task RefreshAsync()
    {
        // Implementation
    }
}
```

### 4. UI Colors (NEVER Hardcode RGB)
```csharp
// ✅ Use semantic helpers
var color = ProfessionalColors.GetStatusColor("Connected");
var accent = ProfessionalColors.AccentBlue;

// ❌ NEVER hardcode colors - breaks theming
var color = Color.Parse("#E57373"); // Forbidden
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

### 7. Security Rules
- **NEVER** log sensitive data (tokens, emails, API keys)
- **NEVER** commit secrets to version control
- Use `SecureStorageManager` for token storage (OS keychain)
- All database queries: parameterized statements only
- OAuth tokens: OS keychain (DPAPI/Keychain/libsecret), not database

## Project Structure

```
src/
├── TrashMailPanda/          # Main Avalonia app
│   ├── Views/               # XAML views
│   ├── ViewModels/          # MVVM ViewModels
│   ├── Theming/             # ProfessionalColors, styles
│   └── Services/            # App services
├── Shared/                  # Cross-cutting concerns
│   └── TrashMailPanda.Shared/
│       ├── Base/            # IProvider, Result<T>
│       ├── Security/        # SecureStorageManager
│       └── Models/          # DTOs
├── Providers/               # External integrations
│   ├── Email/               # GmailEmailProvider
│   ├── LLM/                 # OpenAIProvider
│   └── Storage/             # SqliteStorageProvider
└── Tests/                   # xUnit tests
```

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
- **Theming**: `src/TrashMailPanda/TrashMailPanda/Theming/ProfessionalColors.cs`

## Common Mistakes to Avoid

1. ❌ Throwing exceptions from providers → ✅ Return `Result.Failure()`
2. ❌ Hardcoding `#RRGGBB` colors → ✅ Use `ProfessionalColors`
3. ❌ Multiple public classes per file → ✅ One per file
4. ❌ Nullable warnings → ✅ Explicit `string?` or `string` types
5. ❌ Secrets in config → ✅ Environment variables + OS keychain
6. ❌ No tests → ✅ Maintain 90%+ coverage

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
