# Console Contracts: User Interaction Patterns

**Feature**: 057-console-startup-orchestration  
**Date**: March 16, 2026

This document defines the console UI contracts for startup orchestration, configuration wizard, and mode selection menu.

---

## 1. Application Startup Contract

### Entry Point: `dotnet run --project src/TrashMailPanda`

**Expected Behavior**:
1. Display welcome banner with application name and version
2. Begin sequential provider initialization (Storage → Gmail)
3. Display real-time status updates for each provider
4. If all required providers succeed → show mode selection menu
5. If any required provider fails → show error recovery menu

**Console Output Format**:

```
╔══════════════════════════════════════════════════════════════╗
║             🐼 TrashMail Panda v1.0.0                        ║
║          AI-Powered Email Triage Assistant                  ║
╚══════════════════════════════════════════════════════════════╝

[14:35:01] Starting application initialization...

┌─────────────────────── Provider Initialization ──────────────────────┐
│ [●] Initializing Storage provider...                                 │
│     ├─ Database: ./data/app.db                                       │
│     ├─ Encryption: SQLCipher                                         │
│     └─ Status: Opening database connection... (3s elapsed)           │
└───────────────────────────────────────────────────────────────────────┘

[14:35:04] ✓ Storage provider initialized successfully (3.2s)
[14:35:04] Performing health check...
[14:35:05] ✓ Storage provider is healthy

┌─────────────────────── Provider Initialization ──────────────────────┐
│ [●] Initializing Gmail provider...                                   │
│     ├─ Account: user@gmail.com                                       │
│     ├─ OAuth: Checking token validity...                             │
│     └─ Status: Authenticating... (2s elapsed)                        │
└───────────────────────────────────────────────────────────────────────┘

[14:35:07] ✓ Gmail provider initialized successfully (2.1s)
[14:35:07] Performing health check...
[14:35:08] ✓ Gmail provider is healthy

┌─────────────────────── Provider Initialization ──────────────────────┐
│ [●] Initializing OpenAI provider...                                  │
│     ├─ Model: gpt-4o-mini                                            │
│     ├─ API Key: ●●●●●●●●●●sk-1234                                    │
│     └─ Status: Validating API key... (1s elapsed)                    │
└───────────────────────────────────────────────────────────────────────┘

[14:35:09] ✓ OpenAI provider initialized successfully (1.3s)
[14:35:09] Performing health check...
[14:35:10] ✓ OpenAI provider is healthy

[14:35:10] ══════════════════════════════════════════════════════════
[14:35:10] ✓ All providers initialized successfully (6.6s total)
[14:35:10] ══════════════════════════════════════════════════════════

Press any key to continue to mode selection...
```

**Color Codes** (using Spectre.Console markup):
- `[green]✓[/]` - Success indicators
- `[blue]ℹ[/]` - Informational messages
- `[yellow]⚠[/]` - Warnings (optional provider skipped)
- `[bold red]✗[/]` - Errors (provider failed)
- `[●]` - Spinner for in-progress operations (animated)

---

## 2. Error Display Contract

### Scenario: Required Provider Initialization Failure

**Trigger**: Storage or Gmail provider fails during initialization or health check

**Console Output Format**:

```
┌─────────────────────── Provider Initialization ──────────────────────┐
│ [●] Initializing Gmail provider...                                   │
│     ├─ Account: user@gmail.com                                       │
│     ├─ OAuth: Checking token validity...                             │
│     └─ Status: Authenticating... (5s elapsed)                        │
└───────────────────────────────────────────────────────────────────────┘

╔══════════════════════════════════════════════════════════════════════╗
║                     ✗ INITIALIZATION FAILED                          ║
╚══════════════════════════════════════════════════════════════════════╝

[bold red]ERROR: Google provider initialization failed[/]

Provider: Google
Status: Failed
Error Category: Authentication
Error Code: AUTH_TOKEN_EXPIRED
Message: OAuth token has expired and refresh token is invalid

[red]Details:[/]
  Your OAuth token is still valid, but this version of TrashMail Panda requires
  additional Google API permissions that weren't requested when you first set up.
  
  [blue]Why the change?[/]
  - New features added: Google Contacts integration
  - Google requires explicit consent for new permissions
  - Your existing Gmail access remains unchanged

[yellow]Recommended Action:[/]
  Reconfigure Google provider to re-authorize with updated scopes
  (Browser will show you exactly what new permissions are being requested)

What would you like to do?
  > Reconfigure Google Provider
    View Detailed Scope Comparison
    Skip Google Contacts (Continue with Gmail Only)
    Exit Application
```

════════════════════════════════════════════════════════════════════════

What would you like to do?
  > Reconfigure Google Provider
    Retry Initialization
    Exit Application

[Use arrow keys to select, Enter to confirm]
```

**Error Detail Levels** (based on `ConsoleDisplayOptions.ErrorDetailLevel`):

**Minimal**:
```
[bold red]ERROR: Gmail provider initialization failed[/]
OAuth token has expired and refresh token is invalid
```

**Standard** (default):
```
[bold red]ERROR: Gmail provider initialization failed[/]

Provider: Gmail
Error Category: Authentication
Error Code: AUTH_TOKEN_EXPIRED
Message: OAuth token has expired and refresh token is invalid
```

**Verbose**:
```
[bold red]ERROR: Gmail provider initialization failed[/]

Provider: Gmail
Error Category: Authentication
Error Code: AUTH_TOKEN_EXPIRED
Message: OAuth token has expired and refresh token is invalid

Exception: TrashMailPanda.Shared.Base.AuthenticationError
  at GmailEmailProvider.InitializeAsync(...)
  at ConsoleStartupOrchestrator.InitializeProviderAsync(...)
  ... [full stack trace]
```

---

## 3. Configuration Wizard Contract

### Entry Point: First-time startup OR "Reconfigure Provider" from error menu

**Expected Behavior**:
1. Display welcome message with setup overview and links
2. Guide user through sequential provider setup (Storage → Google)
3. Validate each configuration step before proceeding
4. Save configurations to OS keychain
5. **Automatically transition** to provider initialization (no restart needed)

**Console Output Format**:

```
╔══════════════════════════════════════════════════════════════════════╗
║            Welcome to TrashMail Panda Setup Wizard                   ║
╚══════════════════════════════════════════════════════════════════════╝

This wizard will guide you through setting up the email providers needed
for TrashMail Panda to operate.

We'll configure the following providers in order:
  1. Storage (Required) - Local database for email metadata
  2. Google (Required) - Gmail and Google Contacts access

Press Enter to continue or Ctrl+C to exit...

────────────────────────────────────────────────────────────────────────

Step 1/3: Storage Provider Setup

The storage provider manages the local encrypted database where
email metadata and classification results are stored.

Database Path (default: ./data/app.db):
> ./data/app.db

Enable encryption with SQLCipher? (Recommended)
  > Yes
    No

[Creating database at ./data/app.db...]
[✓] Database created successfully
[✓] Encryption enabled

────────────────────────────────────────────────────────────────────────

Step 2/2: Google Provider Setup

The Google provider connects to Gmail and Google Contacts via OAuth 2.0.
Your credentials are stored securely in your OS keychain.

[ℹ] First, you'll need to create OAuth 2.0 credentials:

    1. Visit: https://console.cloud.google.com/apis/credentials
    2. Create a project (if you don't have one)
    3. Enable Gmail API: https://console.cloud.google.com/apis/library/gmail.googleapis.com
    4. Create OAuth 2.0 Client ID (Desktop app type)
    5. Copy the Client ID and Client Secret

    [Press Enter when you have your credentials ready]

Enter your OAuth 2.0 Client ID:
> 123456789-abc123def456.apps.googleusercontent.com

Enter your OAuth 2.0 Client Secret:
> GOCSPX-AbCdEf1234567890

[ℹ] Credentials saved. Starting OAuth authorization...
[ℹ] Opening browser for Google account authorization...

[Please sign in to Google and click "Allow" to grant access]
[Waiting for authorization... ●]

[✓] OAuth authorization successful!
[✓] Account: john.doe@gmail.com
[✓] Access granted: Gmail, Google Contacts
[✓] Credentials stored in OS keychain

[Advancing to confirmation step...]

────────────────────────────────────────────────────────────────────────

Setup Complete! 🎉

The following providers have been configured:
  ✓ Storage    - ./data/app.db (encrypted)
  ✓ Google     - john.doe@gmail.com (Gmail + Contacts)

[ℹ] Configuration saved to OS keychain
[ℹ] Starting provider initialization...

[●] Initializing providers...

Configuration saved successfully.

────────────────────────────────────────────────────────────────────────

[14:35:01] Starting application initialization...
[14:35:01] [●] Initializing Storage provider...
[14:35:04] ✓ Storage provider initialized successfully (3.2s)
[14:35:04] ✓ Storage provider is healthy
[14:35:04] [●] Initializing Google provider...
[14:35:07] ✓ Google provider initialized successfully (2.1s)
[14:35:07] ✓ Google provider is healthy
[14:35:08] ✓ All providers initialized successfully (5.3s total)

Press any key to continue to mode selection...
```

**Interactive Prompts** (using Spectre.Console):
- **Text Input**: `TextPrompt<string>("Enter your OpenAI API key:")`
- **Selection**: `SelectionPrompt<string>().AddChoices("Yes", "No")`
- **Confirmation**: `Confirm("Save configuration?")`

---

## 4. Mode Selection Menu Contract

### Entry Point: After successful provider initialization

**Expected Behavior**:
1. Display provider status summary
2. Show available operational modes (filtered by provider health)
3. Navigate with arrow keys, select with Enter  
4. Launch selected mode or exit gracefully

**Console Output Format**:

```
╔══════════════════════════════════════════════════════════════════════╗
║                    🐼 TrashMail Panda Ready                          ║
╚══════════════════════════════════════════════════════════════════════╝

Provider Status:
  ✓ Storage    - Healthy (./data/app.db, 1,234 emails indexed)
  ✓ Google     - Healthy (john.doe@gmail.com, 5,678 unread)

────────────────────────────────────────────────────────────────────────

Select an operational mode:

  > 📧 Email Triage            - Classify and organize emails
    🗂️  Bulk Operations         - Mass email actions (delete/archive)
    ⚙️  Provider Settings       - Reconfigure providers
    🖥️  Launch UI Mode          - Open Avalonia UI application
    ❌ Exit

[Use arrow keys to navigate, Enter to select, Q to quit]
```

**With Optional Provider Unavailable**:

**Note**: AI providers (OpenAI, etc.) are deferred to future implementation. When added, disabled modes will display grayed out with "(Requires [Provider])" indicators.

```

## 5. Timeout Handling Contract

### Scenario: Provider initialization exceeds 30-second timeout

**Console Output Format**:

```
┌─────────────────────── Provider Initialization ──────────────────────┐
│ [●] Initializing Gmail provider...                                   │
│     ├─ Account: user@gmail.com                                       │
│     ├─ OAuth: Waiting for browser authorization...                   │
│     └─ Status: Waiting for callback... (28s remaining)               │
└───────────────────────────────────────────────────────────────────────┘

[Countdown: 28s... 27s... 26s... ... 3s... 2s... 1s...]

╔══════════════════════════════════════════════════════════════════════╗
║                    ⚠ INITIALIZATION TIMEOUT                          ║
╚══════════════════════════════════════════════════════════════════════╝

[yellow]WARNING: Google provider initialization timed out[/]

Provider: Google
Status: Timeout
Elapsed Time: 30.0 seconds

[yellow]Details:[/]
  The provider did not respond within the 30-second timeout limit.
  This usually indicates:
  1. You didn't click "Allow" in the browser authorization page
  2. Network connectivity issues
  3. OAuth callback not received (check browser window)
  4. Firewall blocking the callback on localhost:8080

[yellow]Recommended Action:[/]
  Check your browser - you may still have an authorization page open!
  If you see "Sign in with Google", complete that and click "Allow"

What would you like to do?
  > Retry Initialization
    Reconfigure Google Provider  
    Continue Without Google (app will not be functional)
    Exit Application
```

---

## 6. Ctrl+C Cancellation Contract

### Scenario: User presses Ctrl+C during initialization

**Console Output Format**:

```
┌─────────────────────── Provider Initialization ──────────────────────┐
│ [●] Initializing Gmail provider...                                   │
│     ├─ Account: user@gmail.com                                       │
│     ├─ OAuth: Waiting for browser authorization...                   │
│     └─ Status: Waiting for callback... (28s remaining)               │
└───────────────────────────────────────────────────────────────────────┘

^C
[yellow] Cancellation requested...[/]
[yellow] Shutting down gracefully...[/]

[ℹ] Cleaning up initialized providers:
    ├─ Storage: Closing database connections... ✓
    └─ Google: Revoking temporary session... ✓

[✓] Cleanup complete
[✓] Application shutdown successful

Goodbye! 👋
```

**Graceful Shutdown Requirements**:
- Catch `Console.CancelKeyPress` event
- Cancel all running CancellationTokenSources
- Call `ShutdownAsync()` on initialized providers only (not failed ones)
- Display cleanup progress
- Exit with code 0 (normal exit)

---

## 7. Re-configuration Flow Contract

### Entry Point: "Reconfigure Provider" from error menu OR "Provider Settings" from mode menu

**Console Output Format**:

```
╔══════════════════════════════════════════════════════════════════════╗
║                    Provider Settings                                 ║
╚══════════════════════════════════════════════════════════════════════╝

Current Configuration:
  ✓ Storage    - ./data/app.db (encrypted)
  ✗ Google     - FAILED (OAuth token expired)

Which provider would you like to reconfigure?

  > 📦 Storage Provider
    🌐 Google Provider       [yellow](Currently Failing)[/]
    ← Back to Main Menu

[Selected: Google Provider]

════════════════════════════════════════════════════════════════════════

Reconfiguring Google Provider

Current account: john.doe@gmail.com (credentials expired)

Select an option:
  > Re-authorize Current Account (john.doe@gmail.com)
    Configure New Account (different credentials)
    Remove Google Configuration
    Cancel

[Selected: Re-authorize Current Account]

[ℹ] Starting OAuth re-authorization...
[ℹ] Opening browser for Google authorization...
[ℹ] Please sign in and click "Allow" to grant access

[Waiting for authorization... ●]

[✓] Authorization successful!
[✓] Google provider re-configured successfully!
[✓] Credentials saved to OS keychain

[Retrying provider initialization...]

[●] Initializing Google provider...
[✓] Google provider initialized successfully (2.1s)
[✓] Google provider is healthy

[Returning to Provider Settings menu...]
```

---

## 8. Progress Indicators

**Spectre.Console Components Used**:

### Status Spinner
```csharp
await AnsiConsole.Status()
    .Spinner(Spinner.Known.Dots)
    .Start("Initializing Gmail provider...", async ctx =>
    {
        ctx.Status("Authenticating...");
        // Initialization logic
        ctx.Status("Performing health check...");
        // Health check logic
    });
```

### Progress Bar (for multi-step operations)
```csharp
await AnsiConsole.Progress()
    .Start(async ctx =>
    {
        var task = ctx.AddTask("Initializing providers", maxValue: 3);
        
        task.Description = "Storage...";
        // Initialize storage
        task.Increment(1);
        
        task.Description = "Gmail...";
        // Initialize Gmail
        task.Increment(1);
        
        task.Description = "OpenAI...";
        // Initialize OpenAI
        task.Increment(1);
    });
```

### Table (for provider status summary)
```csharp
var table = new Table();
table.AddColumn("Provider");
table.AddColumn("Status");
table.AddColumn("Details");

table.AddRow("Storage", "[green]Healthy[/]", "./data/app.db");
table.AddRow("Gmail", "[green]Healthy[/]", "john.doe@gmail.com");
table.AddRow("OpenAI", "[yellow]Degraded[/]", "Rate limit: 90%");

AnsiConsole.Write(table);
```

---

## 9. Command-Line Arguments Contract

**Optional**: Support command-line flags for automation/testing

```bash
# Skip interactive prompts (auto-select defaults)
dotnet run --project src/TrashMailPanda -- --non-interactive

# Force re-configuration wizard
dotnet run --project src/TrashMailPanda -- --wizard

# Skip provider initialization (for testing)
dotnet run --project src/TrashMailPanda -- --skip-providers

# Set error detail level
dotnet run --project src/TrashMailPanda -- --error-detail=verbose
```

**Argument Parsing**:
```csharp
// Program.cs
public static void Main(string[] args)
{
    var nonInteractive = args.Contains("--non-interactive");
    var forceWizard = args.Contains("--wizard");
    var skipProviders = args.Contains("--skip-providers");
    
    // ...
}
```

**Note**: This is OPTIONAL for Phase 1 - can be deferred to future enhancement.

---

## Contract Guarantees

1. **Deterministic Output**: Same provider states always produce same console output
2. **Keyboard-Only Navigation**: No mouse required for any interaction
3. **Graceful Degradation**: Color codes automatically stripped if terminal doesn't support ANSI
4. **Timeout Protection**: Every async operation has a timeout to prevent hangs
5. **Cancellation Support**: Ctrl+C always triggers graceful shutdown
6. **Error Visibility**: All errors displayable in bold red with full context
7. **Re-entrancy**: Configuration wizard can be re-launched from error recovery or settings menu
