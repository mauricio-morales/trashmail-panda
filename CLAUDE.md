# CLAUDE.md

> **📌 Current workflow**: Claude Code + [spec-kit](.specify/)
>
> **See also**:
>
> - [`.claude/memory/`](.claude/memory/) - Claude Code project memory (constitution, architecture, patterns)
> - [`.specify/memory/constitution.md`](.specify/memory/constitution.md) - Authoritative engineering principles (v2.0.0)
> - [`docs/oauth/GMAIL_OAUTH_CONSOLE_SETUP.md`](docs/oauth/GMAIL_OAUTH_CONSOLE_SETUP.md) - Console OAuth setup
> - [`docs/architecture/ARCHITECTURE_SHIFT_TO_LOCAL_ML.md`](docs/architecture/ARCHITECTURE_SHIFT_TO_LOCAL_ML.md) - ML architecture plan

---

This file provides guidance to Claude Code (claude.ai/code) when working with code in this repository.

## Project Overview

TrashMail Panda is an open-source, AI-powered email triage assistant for Gmail users, built with .NET 9.0 / C# 12. It helps users train a personal local ML model that classifies their email based on their own habits — no cloud services, no generic models.

**Architecture**: Console-first TUI using Spectre.Console. Avalonia desktop UI has been fully removed (feature 063-remove-avalonia-ui). The permanent architecture is:

- Spectre.Console TUI + `IApplicationOrchestrator` service layer
- Local ML classification via ML.NET (transitioning from OpenAI)
- MCP (Model Context Protocol) compatible — designed for AI assistant integration
- Privacy-first: emails and training data never leave the user's machine

**Key Technologies**: Spectre.Console, Microsoft.Extensions.Hosting/DI/Logging, Microsoft.Data.Sqlite + SQLitePCLRaw.bundle_e_sqlcipher, Google.Apis.Gmail.v1, System.Text.Json, Polly

## Development Commands

### Essential Daily Commands

- `dotnet run --project src/TrashMailPanda` - Start development server with hot reload
- `dotnet build` - Build the application for production
- `dotnet format --verify-no-changes` - Verify code formatting
- `dotnet format` - Format code with .NET formatter
- `dotnet test` - Run xUnit tests (use `dotnet test --watch` for watch mode)

### Git Hooks Setup (One-time per developer)

- `./setup-hooks.sh` - Configure git hooks for automatic code formatting on commit
- **Pre-commit Hook**: Automatically runs `dotnet format` before each commit
- **Bypass Hook**: Use `git commit --no-verify` to skip formatting check temporarily

### CI/CD Validation (Before Push)

- `dotnet build --configuration Release` - Fast validation: build + test
- `dotnet test --configuration Release` - Full test suite
- `dotnet format --verify-no-changes` - Code quality checks only
- `dotnet tool restore && dotnet security-scan` - Security audit

### VS Code Tasks (Cmd+Shift+P → "Tasks: Run Task")

- **🔍 Full CI/CD Check** (`Ctrl+Shift+C`) - Complete validation
- **⚡ Quick Check** (`Ctrl+Shift+Q`) - Fast build/test
- **🧪 Code Quality Check** (`Ctrl+Shift+T`) - Formatting and analysis
- **🧽 Clean & Restore** - Clean bin/obj and restore packages

### Testing Commands

- `dotnet test --collect:"XPlat Code Coverage"` - Generate coverage report
- `dotnet test --filter Category=Integration` - Run integration tests
- `dotnet test --filter Category=Unit` - Run unit tests only
- `dotnet test --verbosity normal` - Detailed test output

### Build Commands

- `dotnet publish -c Release -r win-x64` - Windows x64 build
- `dotnet publish -c Release -r osx-x64` - macOS x64 build  
- `dotnet publish -c Release -r linux-x64` - Linux x64 build
- `dotnet clean` - Remove build artifacts

## Architecture Overview

### Provider-Agnostic Design

The application uses a sophisticated provider pattern with IProvider interface providing common functionality:

1. **IEmailProvider** (`src/Providers/Email/`) - Gmail API integration (future: IMAP)
2. **ILLMProvider** (`src/Providers/LLM/`) - OpenAI GPT-4o-mini (future: Claude, local)
3. **IStorageProvider** (`src/Providers/Storage/`) - SQLite with SQLCipher encryption

### Base Provider Architecture

All providers implement the `IProvider<TConfig>` interface (`src/Shared/Base/IProvider.cs`) which provides:

- **Lifecycle Management**: Initialize, shutdown, configuration updates with dependency injection
- **State Management**: Centralized initialization state tracking
- **Performance Monitoring**: Built-in metrics collection and caching with IMetrics
- **Dependency Management**: Constructor injection with IServiceProvider
- **Health Checks**: Enhanced health checks with IHealthCheck integration
- **Configuration Validation**: DataAnnotations validation with startup checks

### MVVM State Management Architecture

The application uses **CommunityToolkit.Mvvm** for robust UI state management:

- **StartupViewModel** (`src/ViewModels/StartupViewModel.cs`) - Manages application startup flow
- **ObservableObject Integration** - INotifyPropertyChanged implementation with source generators
- **Provider Status Management** - Tracks individual provider health and setup requirements
- **Async Command Handling** - Built-in async command execution with cancellation

Key states: `Initializing`, `CheckingProviders`, `DashboardReady`, `SetupRequired`, `SetupTimeout`

### Startup Orchestration System

Coordinates provider health checks and app readiness:

- **StartupOrchestrator** (`src/Services/StartupOrchestrator.cs`) - Orchestrates provider checks
- **Console Output** - Spectre.Console for colored status messages
- **Parallel Provider Checks** - All provider health checks run concurrently with Task.WhenAll
- **Individual Timeouts** - Each provider has independent timeout handling with CancellationToken

### Project Structure

```
src/
├── TrashMailPanda/         # Main console application
│   ├── Services/            # OAuth, app services
│   ├── Models/              # OAuthFlowResult, OAuthConfiguration, etc.
│   └── Program.cs           # Console entry point
├── Shared/                 # Shared types and utilities
│   ├── Base/               # IProvider architecture
│   ├── Models/             # Data transfer objects
│   ├── Extensions/         # Extension methods
│   ├── Security/           # SecureStorageManager, encryption
│   └── Utils/              # Shared utilities & provider initialization
├── Providers/              # Provider implementations
│   ├── Email/              # Gmail provider
│   ├── LLM/                # OpenAI provider (transitioning to ML.NET)
│   └── Storage/            # SQLite provider
└── Tests/                  # xUnit test projects
    ├── Unit/               # Unit tests
    ├── Integration/        # Integration tests
    └── Fixtures/           # Test fixtures
```

### Result Pattern

**CRITICAL**: All provider methods use the `Result<T>` pattern instead of throwing exceptions:

```csharp
// ✅ Correct
var result = await provider.GetEmailsAsync();
if (result.IsSuccess)
{
    Console.WriteLine(result.Value);
}
else
{
    Console.WriteLine(result.Error);
}

// ❌ Never do this
try
{
    var emails = await provider.GetEmailsAsync();
}
catch (Exception ex)
{
    // Providers don't throw!
}
```

## Console OAuth Patterns

**IMPORTANT FOR AI AGENTS**: TrashMail Panda uses console-based OAuth flows with Spectre.Console for colored terminal output.

### OAuth Services

- **IGoogleOAuthHandler** (`src/Services/IGoogleOAuthHandler.cs`) - Orchestrates OAuth 2.0 flow with PKCE
- **IGoogleTokenValidator** (`src/Services/IGoogleTokenValidator.cs`) - Validates and refreshes tokens
- **ILocalOAuthCallbackListener** (`src/Services/ILocalOAuthCallbackListener.cs`) - HTTP listener for callbacks
- **OAuthErrorHandler** (`src/Services/OAuthErrorHandler.cs`) - User-friendly error messages
- **PKCEGenerator** (`src/Services/PKCEGenerator.cs`) - Generates SHA256 code challenge/verifier pairs

### Console OAuth Flow

```csharp
// Check authentication status
var tokenValidator = services.GetRequiredService<IGoogleTokenValidator>();
var validationResult = await tokenValidator.ValidateAsync();

if (validationResult.Value.Status == TokenStatus.NotAuthenticated)
{
    // Start OAuth flow
    var oauthHandler = services.GetRequiredService<IGoogleOAuthHandler>();
    var authResult = await oauthHandler.AuthenticateAsync(config);
    
    if (authResult.IsSuccess)
    {
        AnsiConsole.MarkupLine("[green]✓ Authentication successful![/]");
    }
    else
    {
        OAuthErrorHandler.DisplayError(authResult.Error, allowRetry: true);
    }
}
else if (validationResult.Value.Status == TokenStatus.ExpiredCanRefresh)
{
    // Auto-refresh token
    var oauthHandler = services.GetRequiredService<IGoogleOAuthHandler>();
    var refreshResult = await oauthHandler.RefreshTokenAsync(refreshToken, config);
}
```

### Colored Console Output

Use Spectre.Console for all console output:

```csharp
// Success
AnsiConsole.MarkupLine("[green]✓ Operation successful[/]");

// Error
AnsiConsole.MarkupLine("[bold red]✗ Error:[/] [red]{message}[/]");

// Warning
AnsiConsole.MarkupLine("[yellow]⚠ Warning message[/]");

// Info
AnsiConsole.MarkupLine("[blue]ℹ Information[/]");

// Action
AnsiConsole.MarkupLine("[cyan]→ Processing...[/]");

// Status spinner
await AnsiConsole.Status()
    .Spinner(Spinner.Known.Dots)
    .StartAsync("[cyan]Waiting for authorization...[/]", async ctx =>
    {
        // Long-running operation
    });
```

### OAuth Token Storage

**CRITICAL**: Tokens are stored in OS keychain, NEVER in database:

- **macOS**: Keychain Access (via `security` command)
- **Windows**: DPAPI (`ProtectedData` API)
- **Linux**: libsecret (via `secret-tool` command)

**Storage Keys**:
- `gmail_client_id` - OAuth client ID from Google Cloud Console
- `gmail_client_secret` - OAuth client secret
- `gmail_access_token` - Short-lived access token (1 hour)
- `gmail_refresh_token` - Long-lived refresh token
- `gmail_token_expiry` - Expiry in seconds
- `gmail_token_issued_utc` - Issue timestamp (ISO 8601)
- `gmail_user_email` - Authenticated user email

### Error Handling Patterns

```csharp
// Use OAuthErrorHandler for consistent error display
try
{
    var result = await oauthHandler.AuthenticateAsync(config);
    
    if (!result.IsSuccess)
    {
        OAuthErrorHandler.DisplayError(result.Error, allowRetry: true, logger);
        return false;
    }
}
catch (Exception ex)
{
    OAuthErrorHandler.DisplayError(ex, allowRetry: true, logger);
    
    // Offer retry
    if (OAuthErrorHandler.PromptRetry("authentication"))
    {
        return await AuthenticateAsync(); // Retry
    }
}
```

## Avalonia MVVM Patterns (Legacy)

> **Note**: Avalonia UI is being phased out in favor of console TUI. These patterns are retained for reference during the transition.

### ViewModels and Services

- **IApplicationService** (`src/Services/IApplicationService.cs`) - Provides access to application services
- **IProviderStatusService** (`src/Services/IProviderStatusService.cs`) - Real-time provider health monitoring  
- **ObservableObject** (from CommunityToolkit.Mvvm) - MVVM base class for property change notification

### State Management (Legacy)

- **CommunityToolkit.Mvvm**: Observable properties and commands for UI binding
- **Provider Status**: Real-time health monitoring and setup requirements
- **Dialog Management**: Coordinated setup flows for different providers with Avalonia dialogs
- **Error Handling**: Graceful error handling with Result<T> pattern and user notifications

### View Architecture (Legacy)

- **StartupView**: Central orchestrator for application startup
- **ProviderSetupUserControl**: Reusable provider status and setup controls
- **Dialog Coordination**: OpenAI and Gmail setup dialogs with view model coordination

## Key Development Patterns

### C# Configuration

- **Nullable reference types enabled** - All nullable checks active
- **Global using statements**: Common namespaces in GlobalUsings.cs
- **No object types** - Use proper typing with generics
- **Explicit return types** preferred for clarity
- **One public class per file** - ALWAYS maintain one public class/interface/enum/record per file for better organization and maintainability

### Error Handling

- Use `Result<T, TError>` for all async operations
- Import error utilities from `Shared.Base`
- Error classes: `ConfigurationError`, `AuthenticationError`, `NetworkError`, `ValidationError`
- Use `Result.Success()` and `Result.Failure()` factory methods

### Provider Implementation

When implementing new providers, follow the IProvider architecture:

1. **Implement IProvider**: `class MyProvider : IProvider<MyConfig>`
2. **Implement Interface Methods**:
   - `InitializeAsync(config)`: Provider-specific setup
   - `ShutdownAsync()`: Cleanup and resource deallocation
   - `ValidateConfigurationAsync(config)`: Config validation logic
   - `HealthCheckAsync()`: Provider health assessment
3. **Use Dependency Injection**:
   - Constructor injection for dependencies
   - Register as singleton or scoped based on needs
   - Use IOptions<T> pattern for configuration
4. **Configuration**:
   - Define typed configuration with DataAnnotations validation
   - Use IOptionsMonitor<T> for configuration changes
   - Implement IValidateOptions<T> for complex validation
5. **State Management**: Use internal state tracking with thread safety
6. **Performance**: Enable metrics with IMetrics integration

### MVVM Development

When creating new view models:

1. **Define Properties**: Use ObservableProperty attribute for data binding
2. **Type Safety**: Use strong typing with nullable reference types
3. **Commands**: Use RelayCommand and AsyncRelayCommand for user actions
4. **Validation**: Implement INotifyDataErrorInfo for validation feedback
5. **State Changes**: Use PropertyChanged.Fody for automatic notifications
6. **Integration**: Use dependency injection for service access

### Security Considerations

- **Never log or expose sensitive data** (tokens, emails, API keys)
- Use OS keychain APIs (DPAPI, macOS Keychain, libsecret) for secure token storage
- All database operations use parameterized queries with Entity Framework
- Implement proper rate limiting with Polly for external APIs

## Provider Health Monitoring & Debugging

**IMPORTANT FOR AI AGENTS**: TrashMail Panda includes comprehensive provider health monitoring built into the application startup process for debugging and validation.

### Provider Validation Commands

**Primary debugging commands:**

```bash
# Run application with detailed provider logging
dotnet run --project src/TrashMailPanda --verbosity detailed

# Build and run with configuration validation
dotnet build --configuration Debug
dotnet run --project src/TrashMailPanda

# Run tests to validate provider implementations
dotnet test --logger console --verbosity normal

# Test specific provider implementations
dotnet test --filter "FullyQualifiedName~GmailEmailProvider"
dotnet test --filter "FullyQualifiedName~OpenAIProvider"
dotnet test --filter "FullyQualifiedName~SqliteStorageProvider"
```

### Provider Health Check Features

- **Startup Orchestration**: All providers are health-checked during application startup
- **Dependency Injection Validation**: Provider dependencies are validated at container build time
- **Configuration Validation**: DataAnnotations validation for all provider configurations
- **Result Pattern**: Consistent error reporting without exceptions
- **Logging Integration**: Microsoft.Extensions.Logging for comprehensive diagnostics

### Environment Variables Required

For provider validation and operation:

```bash
# Gmail Provider (required for Gmail integration)
GMAIL_CLIENT_ID=your_gmail_oauth_client_id
GMAIL_CLIENT_SECRET=your_gmail_oauth_client_secret

# OpenAI Provider (required for AI classification)
OPENAI_API_KEY=your_openai_api_key

# Optional overrides
GMAIL_REDIRECT_URI=http://localhost:8080/oauth/callback
DATABASE_PATH=./data/app.db
```

### Local Integration Testing Setup

**IMPORTANT**: Most integration tests are skipped by default as they require real OAuth credentials. To run them locally:

#### 1. Gmail OAuth Setup

1. **Create Google Cloud Project**:
   - Go to [Google Cloud Console](https://console.cloud.google.com/)
   - Create a new project or select existing one
   - Enable the Gmail API

2. **Create OAuth 2.0 Credentials**:
   - Go to "Credentials" → "Create Credentials" → "OAuth 2.0 Client IDs"
   - Application type: "Desktop application"
   - Note the Client ID and Client Secret

3. **Set Environment Variables**:
   ```bash
   export GMAIL_CLIENT_ID="your_actual_client_id"
   export GMAIL_CLIENT_SECRET="your_actual_client_secret"
   ```

4. **Enable Integration Tests**:
   - Edit `src/Tests/TrashMailPanda.Tests/Integration/Email/GmailApiIntegrationTests.cs`
   - Remove the `Skip = "..."` attribute from test methods
   - Run: `dotnet test --filter "Category=Integration"`

#### 2. OpenAI Testing Setup

1. **Get OpenAI API Key**:
   - Sign up at [OpenAI Platform](https://platform.openai.com/)
   - Create an API key

2. **Set Environment Variable**:
   ```bash
   export OPENAI_API_KEY="your_openai_api_key"
   ```

#### 3. Running Integration Tests

```bash
# Run all tests (skips integration tests requiring real credentials)
dotnet test

# Run only unit tests
dotnet test --filter "Category=Unit"

# Run integration tests (requires environment variables set)
dotnet test --filter "Category=Integration"

# Run specific Gmail integration tests
dotnet test --filter "FullyQualifiedName~GmailApiIntegrationTests"
```

**WARNING**: Integration tests will:
- Open browser windows for OAuth flows
- Make real API calls to Gmail/OpenAI
- Consume API quotas/credits
- Require active internet connection

### Provider Debugging Examples

```bash
# 1. Run with detailed logging to see provider initialization
dotnet run --project src/TrashMailPanda --configuration Debug

# 2. Test provider health checks
dotnet test --filter "Category=Integration" --logger console

# 3. Validate configuration and dependencies
dotnet build --verbosity diagnostic

# 4. Run with specific log levels
DOTNET_ENVIRONMENT=Development dotnet run --project src/TrashMailPanda
```

### Troubleshooting Provider Issues

**Issue: Provider initialization failures**

- Check application logs for detailed error messages
- Verify environment variables are set correctly
- Run `dotnet build` to check for compilation issues

**Issue: OAuth setup required**

- Launch the application normally: `dotnet run --project src/TrashMailPanda`
- Complete OAuth flows through the UI
- Credentials are securely stored using OS keychain

**Issue: Network connectivity problems**

- Check internet connection and firewall settings
- Verify API endpoints are accessible
- Review provider-specific rate limiting and quotas

## Advanced Security Architecture

The application implements a comprehensive multi-layer security system:

### SecureStorageManager (`src/Shared/TrashMailPanda.Shared/Security/SecureStorageManager.cs`)

- **ZERO-PASSWORD Experience**: Uses OS-level security (keychain) for transparent authentication
- **Hybrid Storage**: Combines OS keychain with encrypted SQLite for optimal security
- **Automatic Token Rotation**: Built-in lifecycle management for OAuth tokens
- **Security Audit Logging**: Comprehensive logging for compliance and monitoring
- **Recovery Procedures**: Handles corrupted storage scenarios gracefully

### CredentialEncryption (`src/Shared/TrashMailPanda.Shared/Security/CredentialEncryption.cs`)

- **OS Keychain Integration**: Platform-specific secure storage (DPAPI, macOS Keychain, libsecret)
- **Encryption at Rest**: SQLCipher encryption for database storage
- **Master Key Management**: Derived from system entropy
- **Token Rotation Service**: Automated credential renewal

### SecurityAuditLogger (`src/Shared/TrashMailPanda.Shared/Security/SecurityAuditLogger.cs`)

- **Operation Logging**: All credential operations are audited
- **Security Event Tracking**: Failed authentications, unauthorized access attempts
- **Compliance Features**: Audit trails for security compliance requirements

### OAuth Management

- **Gmail OAuth Integration**: Handles Gmail OAuth2 flow with refresh tokens via Google.Apis.Gmail
- **Secure Token Storage**: OS keychain storage with automatic cleanup
- **Configuration Validation**: DataAnnotations validation for OAuth settings

## Provider Initialization System

Robust provider initialization with dependency injection:

### Features

- **Dependency Injection**: Microsoft.Extensions.DependencyInjection for provider lifecycle
- **Startup Orchestration**: StartupOrchestrator coordinates provider health checks
- **Configuration Validation**: DataAnnotations and IValidateOptions<T> for config validation
- **Health Checks**: IHealthCheck integration for provider monitoring
- **State Management**: Centralized provider state tracking with MVVM

### Key Services

- **StartupOrchestrator**: Coordinates parallel provider initialization
- **ProviderStatusService**: Real-time provider health monitoring
- **ApplicationService**: Central access to all application services

### Validation Patterns

- **Configuration Validation**: IValidateOptions<T> with DataAnnotations
- **Health Checks**: Built-in health check infrastructure
- **Result Pattern**: Consistent error handling without exceptions

## MVVM Architecture

Avalonia MVVM pattern with CommunityToolkit.Mvvm:

### ViewModels

- **MainWindowViewModel**: Primary application view model
- **WelcomeWizardViewModel**: Provider setup and onboarding
- **StartupViewModel**: Application startup orchestration (if implemented)

### Services Integration

- **IApplicationService**: Central service access point
- **IProviderStatusService**: Real-time provider health monitoring
- **IStartupOrchestrator**: Provider initialization coordination

### MVVM Patterns

```csharp
// ObservableProperty for data binding
[ObservableProperty]
private string status = "Initializing...";

// RelayCommand for user actions
[RelayCommand]
private async Task RefreshProvidersAsync()
{
    // Implementation with proper error handling
}
```

## Database & Storage

### SQLite Database

- Uses `Microsoft.Data.Sqlite` with SQLCipher encryption
- Database file: `data/app.db` (encrypted)
- Migration system in place - check provider `InitializeAsync()` methods
- Always use parameterized queries to prevent SQL injection

### Database Schema Management

**CRITICAL**: All database schema changes must be managed through migrations for auto-upgrade on app start.

- **Migration Pattern**: Database schema is automatically upgraded on every app start
- **Version Control**: Schema changes must include proper migration scripts
- **Backward Compatibility**: Migrations must handle existing data gracefully
- **No Manual Schema Changes**: Never manually ALTER tables - always use migration system
- **Storage Provider Integration**: SQLite provider handles migrations via `migrate()` method
- **Data Integrity**: All migrations must preserve existing user data and credentials

Example migration pattern:

```csharp
public async Task<Result<bool>> InitializeAsync(StorageConfig config)
{
    // Check current schema version
    // Apply incremental migrations
    // Validate data integrity after migration
    return Result.Success(true);
}
```

### Data Models

Key tables/entities:

- `user_rules` - Email classification rules
- `email_metadata` - Processed email information
- `classification_history` - AI classification results
- `encrypted_tokens` - OAuth tokens and API keys
- `config` - Application configuration

**ML Data Storage (Schema Version 5)**:

- `email_features` - 38-feature vectors for ML training (never auto-deleted)
- `email_archive` - Complete email content for compliance (subject to cleanup)
- `storage_quota` - Storage usage tracking and cleanup management

### EmailArchiveService Usage Patterns

**IMPORTANT**: For ML data storage, use `IEmailArchiveService` (not IStorageProvider).

#### Basic Feature Storage

```csharp
// Inject IEmailArchiveService
public class EmailProcessor
{
    private readonly IEmailArchiveService _archiveService;
    
    public EmailProcessor(IEmailArchiveService archiveService)
    {
        _archiveService = archiveService;
    }
    
    // Store single feature vector
    public async Task<Result<bool>> ProcessEmailAsync(Email email)
    {
        var feature = new EmailFeatureVector
        {
            EmailId = email.Id,
            SenderDomain = ExtractDomain(email.From),
            SpfResult = email.Headers["SPF"] ?? "none",
            DkimResult = email.Headers["DKIM"] ?? "none",
            SubjectLength = email.Subject.Length,
            // ... 35 more features
            FeatureSchemaVersion = FeatureSchema.CurrentVersion,
            ExtractedAt = DateTime.UtcNow,
            UserCorrected = 0
        };
        
        return await _archiveService.StoreFeatureAsync(feature);
    }
}
```

#### Batch Operations (Recommended for Performance)

```csharp
// Batch store feature vectors - optimized
var result = await _archiveService.StoreFeaturesBatchAsync(features);
// Stores 1000 vectors in ~2-3 seconds with single transaction

// Batch store archives - with quota checking
var count = await _archiveService.StoreArchivesBatchAsync(archives);
// May store fewer than requested if quota reached
```

#### Storage Monitoring & Cleanup

```csharp
// Check current usage
var quotaResult = await _archiveService.GetStorageUsageAsync();
var quota = quotaResult.Value;
Console.WriteLine($"Using {quota.CurrentBytes}/{quota.LimitBytes} bytes");
Console.WriteLine($"Archives: {quota.ArchiveCount}, Features: {quota.FeatureCount}");
Console.WriteLine($"User-corrected: {quota.UserCorrectedCount}");

// Check if cleanup needed (>90% usage)
var needsCleanup = await _archiveService.ShouldTriggerCleanupAsync();

// Execute cleanup (target 80% usage)
if (needsCleanup.Value)
{
    var deleted = await _archiveService.ExecuteCleanupAsync(targetPercent: 80);
    Console.WriteLine($"Deleted {deleted.Value} oldest archives");
}
```

#### User Correction Preservation

```csharp
// Mark email as user-corrected (protected from cleanup)
var feature = new EmailFeatureVector
{
    EmailId = email.Id,
    // ... other features
    UserCorrected = 1  // Protected - 95% retention rate guaranteed
};

await _archiveService.StoreFeatureAsync(feature);
```

#### Dependency Injection Setup

```csharp
// Program.cs
services.AddSingleton<SqliteConnection>( sp =>
{
    var connection = new SqliteConnection("Data Source=app.db");
    connection.Open();
    return connection;
});

services.AddSingleton<IEmailArchiveService>(sp =>
{
    var connection = sp.GetRequiredService<SqliteConnection>();
    return new EmailArchiveService(connection);
});
```

#### Key Behaviors

- **Features never deleted**: Always preserved for ML training
- **Archives auto-cleanup**: Removes oldest when quota exceeded (default 90% trigger)
- **Two-phase cleanup**: Deletes non-corrected first, then user-corrected if needed
- **Result pattern**: All methods return `Result<T>` (never throw exceptions)
- **Parameterized SQL**: All queries use parameters (no string concatenation)
- **Thread-safe**: Uses connection-level locking with SemaphoreSlim

#### Common Patterns

```csharp
// Pattern: Store both feature + archive
var featureResult = await _archiveService.StoreFeatureAsync(feature);
if (featureResult.IsSuccess)
{
    var archiveResult = await _archiveService.StoreArchiveAsync(archive);
    // Archive failure is non-fatal (quota may be exceeded)
}

// Pattern: Retrieve for ML training
var featuresResult = await _archiveService.GetAllFeaturesAsync(
    schemaVersion: FeatureSchema.CurrentVersion);
var features = featuresResult.Value; // List<EmailFeatureVector>

// Pattern: Delete archive but keep feature
var deleted = await _archiveService.DeleteArchiveAsync(emailId);
// Feature vector remains for training
```

#### Performance Notes

- Feature storage: <100ms per single insert
- Batch storage: 1000 features in <5s
- Batch retrieval: 1000 vectors in <500ms
- Storage monitoring: <100ms (uses SQLite dbstat)

For complete examples, see `specs/055-ml-data-storage/quickstart.md`.

## Testing Strategy

### Test Structure

- Unit tests: `src/Tests/TrashMailPanda.Tests/`
- Provider tests: Individual test classes for each provider
- Integration tests: End-to-end provider integration testing
- Fixture tests: Test data and mock setups

### Coverage Requirements

- Global: 90% coverage target
- Provider implementations: 95% coverage required
- Critical security components: 100% coverage required
- Use `dotnet test --collect:"XPlat Code Coverage"` to check

## Security & Encryption

### Credential Storage

- OAuth tokens encrypted using OS keychain (DPAPI, macOS Keychain, libsecret)
- Master key derived from system entropy
- Secure storage via SecureStorageManager
- Security audit logging for all credential operations

### Email Safety

- Never permanently delete emails without explicit user approval
- All actions are reversible from Gmail trash
- Content sanitization during processing
- Rate limiting to respect API quotas

## Common Issues & Solutions

### Build and Compilation Errors

- Run `dotnet build` to see all compilation errors
- Use `dotnet format` to fix code formatting issues
- Check nullable reference type warnings with strict mode enabled
- Ensure all project references are correct in .csproj files

### Provider Issues

- **Provider Initialization Failures**: Check provider constructor dependencies are registered in DI
- **Result Pattern**: All async operations must return `Result<T>` types - never throw exceptions
- **Health Checks**: Providers implement health check methods for diagnostics
- **Configuration Validation**: Use DataAnnotations for configuration validation
- **Dependency Injection**: Ensure all provider dependencies are registered in ServiceCollection

### Security Issues

- **Token Storage**: Use `SecureStorageManager` for all credential operations
- **Audit Logging**: Check `SecurityAuditLogger` for credential operation logs
- **Encryption**: Verify `CredentialEncryption` setup if storage operations fail
- **OAuth Flows**: Use Google.Apis.Gmail OAuth flow for Gmail authentication

### Database Issues

- Check SQLCipher encryption setup if database won't open
- Use provider `InitializeAsync()` methods to diagnose issues
- Check database migrations if schema errors occur
- Verify Microsoft.Data.Sqlite configuration

### MVVM and UI Issues

- **Data Binding**: Ensure ObservableProperty attributes are used correctly
- **Commands**: Use RelayCommand and AsyncRelayCommand for user actions
- **View Models**: Register view models in DI container
- **Result Handling**: UI should handle Result<T> pattern from services

## Build & Deployment

### .NET Build Process

1. `dotnet restore` - Restore NuGet packages
2. `dotnet build` - Compile the application
3. `dotnet publish -c Release -r <rid>` - Create platform-specific builds

### Platform Support

- Windows: `win-x64` runtime identifier
- macOS: `osx-x64` and `osx-arm64` runtime identifiers  
- Linux: `linux-x64` runtime identifier
- Cross-platform: Avalonia UI provides native look and feel

## Environment Variables & Config

### Configuration Files

- C# Projects: `.csproj` files with .NET 9.0 target framework
- Application Configuration: `appsettings.json` and environment-specific variants
- User Secrets: Microsoft.Extensions.Configuration.UserSecrets for development
- Solution: `TrashMailPanda.sln` with all project references

### Runtime Configuration

- App settings stored in encrypted SQLite
- User preferences in local database
- No environment variables for secrets (use OS keychain)

## Integration Points

### Gmail API

- OAuth2 flow with refresh tokens
- Batch operations for efficiency
- Respectful rate limiting
- Folder/label management

### OpenAI API

- GPT-4o-mini for cost optimization
- Structured prompts for email classification
- Token usage tracking
- Error handling for rate limits/quotas

## Avalonia UI Patterns

**CRITICAL**: This project uses Avalonia UI 11 with MVVM patterns:

```xml
<!-- ✅ Correct - Avalonia XAML syntax -->
<Grid ColumnDefinitions="*,Auto,*" RowDefinitions="Auto,*,Auto">
  <TextBlock Grid.Column="1" Grid.Row="0" Text="{Binding Title}" />
  <ContentControl Grid.Column="1" Grid.Row="1" Content="{Binding CurrentView}" />
</Grid>
```

```csharp
// ✅ Correct - MVVM with CommunityToolkit.Mvvm
[ObservableProperty]
private string title = "TrashMail Panda";

[RelayCommand]
private async Task RefreshAsync()
{
    // Implementation
}
```

Use proper MVVM patterns with ObservableProperty and RelayCommand attributes.

## UI Design & Theme Guidelines

**CRITICAL RULE**: NEVER use hardcoded RGB values (#RRGGBB) in XAML or code. This breaks theming and makes maintenance impossible. Always use semantic color resources.

**IMPORTANT**: Always use the semantic color definitions from the theme resource dictionary for all UI styling decisions. This ensures consistent theming across the application.

### Color System - Use Semantic Names
- **AccentBlue**: `#3A7BD5` - Primary accent color for buttons and highlights
- **BackgroundPrimary**: `#F7F8FA` - Main application background
- **CardBackground**: `#FFFFFF` - Card and dialog backgrounds
- **TextPrimary/Secondary/Tertiary**: Professional text hierarchy
- **StatusSuccess/Warning/Error/Info/Neutral**: Provider status indicators

### Status Color Usage
```csharp
// ✅ Correct - Use semantic helpers
var statusColor = ProfessionalColors.GetStatusColor("Authentication Required");
var healthColor = ProfessionalColors.GetHealthStatusColor(isHealthy);

// ❌ Wrong - Don't hardcode colors
var color = Color.Parse("#E57373");
```

### Key UI Principles
- **Always use `ProfessionalColors` class** instead of hardcoding hex values
- **Semantic naming**: Use status-appropriate colors via `GetStatusColor()` method
- **Consistent card styling**: White backgrounds with 8px rounded corners and soft shadows
- **Professional typography**: Use established text color hierarchy
- **Preserve button classes**: Maintain `.primary`, `.secondary`, `.link` semantic classes

**Reference Files**:
- `src/TrashMailPanda/TrashMailPanda/Theming/ProfessionalColors.cs` - Central color definitions
- `PRPs/47-improve-app-color-theme-and-styling.md` - Complete design specifications

## Performance Considerations

### Email Processing

- Batch operations for Gmail API efficiency
- Streaming for large email sets
- Progress tracking for long operations
- Cancellation support for user interruption

### Database Operations

- Use prepared statements for repeated queries
- Implement proper indexing
- Transaction management for consistency
- Connection pooling where needed

## Debugging Tips

### Common Debug Commands

- `dotnet build` to check compilation issues
- Check VS Code Problems panel for C# and Avalonia issues
- Use Avalonia DevTools for UI debugging
- Use Visual Studio debugger for step-through debugging

### Provider Debugging

- **Health Checks**: Use built-in provider health check methods
- **Logging**: Microsoft.Extensions.Logging with configurable log levels
- **Configuration**: Verify appsettings.json and environment variables
- **Result Pattern**: Check Result<T>.IsSuccess and Result<T>.Error properties

### MVVM Debugging

- **Property Changes**: ObservableProperty attributes automatically notify UI
- **Command Execution**: RelayCommand and AsyncRelayCommand handle exceptions
- **View Model State**: Use debugger to inspect view model properties
- **Data Binding**: Avalonia binding system provides error information

### Security Debugging

- **Audit Logs**: Check SecurityAuditLogger for credential operations
- **Storage Status**: Verify SecureStorageManager initialization
- **Encryption**: Check CredentialEncryption health status
- **Database**: Verify SQLCipher encryption is working

### Log Analysis

- Microsoft.Extensions.Logging integration with configurable providers
- Console logging with structured output
- Error tracking through Result pattern without exceptions
- Provider health monitoring with detailed diagnostics
- Security audit trails in encrypted database
