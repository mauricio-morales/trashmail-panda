# Quickstart: Console Startup Orchestration

**Feature**: 057-console-startup-orchestration  
**Date**: March 16, 2026  
**Audience**: Developers and power users

This guide shows how to use the console-based startup system for TrashMail Panda.

---

## Installation & Prerequisites

### 1. Install Dependencies

```bash
cd /Users/mmorales/Dev/trashmail-panda

# Install Spectre.Console NuGet package
dotnet add src/TrashMailPanda/TrashMailPanda package Spectre.Console

# Restore packages
dotnet restore
```

### 2. Verify Environment

```bash
# Check .NET version (must be 9.0+)
dotnet --version

# Verify build succeeds
dotnet build

# Check for ANSI color support in terminal
dotnet run --project src/TrashMailPanda -- --check-terminal
```

**Terminal Requirements**:
- ANSI color support (most modern terminals: iTerm2, Windows Terminal, GNOME Terminal)
- Minimum width: 80 characters
- Keyboard input support (arrow keys, Enter, Ctrl+C)

---

## Quick Start: First-Time Setup

### Step 1: Run Application

```bash
dotnet run --project src/TrashMailPanda
```

**Expected Output**:
```
╔══════════════════════════════════════════════════════════════╗
║             🐼 TrashMail Panda v1.0.0                        ║
║          AI-Powered Email Triage Assistant                  ║
╚══════════════════════════════════════════════════════════════╝

No configuration detected - starting setup wizard...
```

### Step 2: Complete Configuration Wizard

Follow the interactive prompts to configure each provider:

**Storage Provider** (Required):
- Accept default database path: `./data/app.db`
- Enable encryption: Select "Yes"
- Wait for database creation: ~2 seconds

**Google Provider** (Required):
- Wizard displays instructions for creating OAuth credentials (see "Setting Up Google OAuth Credentials" section below)
- Paste your Client ID when prompted
- Paste your Client Secret when prompted  
- Browser opens automatically for Google OAuth
- Sign in to your Google account
- Click "Allow" to grant TrashMail Panda access to Gmail
- Return to terminal - authorization complete!
- Credentials stored securely in OS keychain (encrypted)
- **Setup complete!** Application automatically continues...

### Step 3: Automatic Initialization

After wizard completes, the app automatically initializes all providers:

**Console Output**:
```
[14:35:01] ✓ Configuration saved successfully!
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

### Step 4: Select Operational Mode

```
Select an operational mode:

  > 📧 Email Triage
    🗂️  Bulk Operations
    ⚙️  Provider Settings
    🖥️  Launch UI Mode
    ❌ Exit
```

Use arrow keys to navigate, Enter to select.

---

## Common Workflows

### Workflow 1: Re-authorize Google After Token Expiry or Scope Changes

**Scenario**: Google provider fails with "OAuth token expired" or "Insufficient OAuth scopes"

**Why This Happens**:
- Token expired after extended inactivity
- You revoked access in Google Account settings
- **Scope mismatch**: App now requires additional permissions (e.g., Google Contacts API added in new version)

**Steps**:
1. Application shows error with specific reason:
   - `[bold red]ERROR: Google provider initialization failed[/]`
   - `Error Code: AUTH_TOKEN_EXPIRED` OR `Error Code: INSUFFICIENT_SCOPES`
   - If scope mismatch: `Missing scopes: https://www.googleapis.com/auth/contacts.readonly`
2. Select from menu: `Reconfigure Google Provider`
3. Select: `Re-authorize Current Account (john.doe@gmail.com)`
4. **Important**: Browser shows updated permission request with new scopes
5. Review new permissions requested (e.g., "Access to Google Contacts")
6. Click "Allow" to grant additional access
7. Return to terminal - credentials saved
8. Application automatically retries initialization
9. Verify: Google shows `✓ Healthy` status with all required scopes

**Command-Line Alternative**:
```bash
# Force re-run configuration wizard
dotnet run --project src/TrashMailPanda -- --wizard
```

---

### Workflow 2: Future AI Provider Integration

**Scenario**: When AI providers (OpenAI, local ML) are added in the future

**Design Notes**:
- Console orchestration is designed to be extensible
- New providers can be added to initialization sequence in `ConsoleStartupOrchestrator`
- Configuration wizard will be extended with additional setup steps
- Mode selection menu will dynamically enable/disable AI modes based on provider health
- AI provider setup deferred to maintain focus on core email operations

---

### Workflow 3: Debugging Provider Initialization Failures

**Scenario**: Storage provider fails during startup

**Steps**:
1. Application shows error with details:
   ```
   [bold red]ERROR: Storage provider initialization failed[/]
   
   Error Code: DB_LOCKED
   Message: Database file is locked by another process
   ```

2. Check for processes using database:
   ```bash
   lsof ./data/app.db
   ```

3. Kill blocking process or delete lock file:
   ```bash
   rm ./data/app.db-shm
   rm ./data/app.db-wal
   ```

4. Select from menu: `Retry Initialization`

5. If still failing, select: `Reconfigure Storage Provider`

**View Full Error Details**:
```bash
# Run with verbose error logging
dotnet run --project src/TrashMailPanda -- --error-detail=verbose

# Check application log file
cat logs/trashmail-panda-$(date +%Y%m%d).log
```

---

### Workflow 4: Running in Headless/CI Environment

**Scenario**: Startup in Docker container or SSH session without browser

**Challenge**: Gmail OAuth requires browser for authorization

**Solution 1: Pre-configure on Local Machine**:
```bash
# On local machine with browser:
1. Run setup wizard interactively: dotnet run --project src/TrashMailPanda
2. Paste your Google OAuth Client ID and Secret when prompted
3. Complete OAuth flow in browser
4. Credentials are automatically stored in OS keychain

# In headless environment:
5. Copy the same database file (contains encrypted references)
6. Ensure OS keychain is accessible or use service account (below)
```

**Solution 2: Use Service Account** (Future Enhancement):
- Create a Google Cloud Service Account with Gmail API access
- Download JSON key file
- Set `GOOGLE_SERVICE_ACCOUNT_JSON` environment variable (path to key file)
- Application auto-detects and uses service account flow (no browser needed)
- **Note**: Service accounts have limitations for personal Gmail access

---

### Workflow 5: Monitoring Startup Performance

**Scenario**: Startup feels slow, want to identify bottleneck

**Enable Timing Display**:
Edit `appsettings.json`:
```json
{
  "ConsoleDisplayOptions": {
    "ShowTimestamps": true,
    "ShowDuration": true
  }
}
```

**Run Application**:
```bash
dotnet run --project src/TrashMailPanda
```

**Analyze Output**:
```
[14:35:01] Starting application initialization...
[14:35:01] [●] Initializing Storage provider...
[14:35:04] ✓ Storage provider initialized successfully (3.2s)
[14:35:04] ✓ Storage provider is healthy
[14:35:04] [●] Initializing Gmail provider...
[14:35:07] ✓ Gmail provider initialized successfully (2.1s)
[14:35:07] ✓ Gmail provider is healthy
[14:35:08] ✓ All providers initialized successfully (5.3s total)
```

**Identify Bottleneck**:
- Storage initialization: 3.2s → Check database size with `ANALYZE` command
- Gmail initialization: 2.1s → Network latency to Gmail API
- Health checks: <1s → Normal

**Optimization Options**:
- Storage: Run `VACUUM` to compact database
- Gmail: No optimization available (network-bound)

---

## Configuration Reference

### appsettings.json Structure

```json
{
  "Logging": {
    "LogLevel": {
      "Default": "Information",
      "TrashMailPanda.Services.Console": "Debug"
    }
  },
  
  "ConsoleDisplayOptions": {
    "ShowTimestamps": true,
    "ShowDuration": true,
    "UseColors": true,
    "StatusRefreshInterval": "00:00:00.200",
    "ErrorDetailLevel": "Standard"
  },
  
  "StorageConfig": {
    "DatabasePath": "./data/app.db",
    "EnableEncryption": true,
    "ConnectionTimeout": 30
  },
  
  "GoogleConfig": {
    "RequiredScopes": [
      "https://www.googleapis.com/auth/gmail.modify",
      "https://www.googleapis.com/auth/contacts.readonly"
    ],
    "ApplicationName": "TrashMail Panda"
  },
  
  "ProviderTimeouts": {
    "StorageInitialization": "00:00:30",
    "GoogleInitialization": "00:00:30",
    "HealthCheck": "00:00:10"
  }
}
```

**Configuration Notes**:
- `GoogleConfig.RequiredScopes`: Array of OAuth 2.0 scopes required for Google APIs
  - Current version requires: Gmail API (read/write), Google Contacts API (read-only)
  - Health checks validate token has ALL scopes - missing scopes trigger re-authorization
  - Scope changes between versions handled automatically (user prompted to re-authorize)

### Setting Up Google OAuth Credentials

**For First-Time Users**:

The setup wizard will guide you through creating Google OAuth credentials. Here's what you'll need:

1. **Go to Google Cloud Console**: https://console.cloud.google.com/
2. **Create a Project** (or select existing one):
   - Click "Select a Project" → "New Project"
   - Enter project name: "TrashMail Panda"
   - Click "Create"

3. **Enable Gmail API**:
   - Go to "APIs & Services" → "Enable APIs and Services"
   - Search for "Gmail API"
   - Click "Enable"

4. **Create OAuth 2.0 Credentials**:
   - Go to "APIs & Services" → "Credentials"
   - Click "Create Credentials" → "OAuth 2.0 Client IDs"
   - Configure consent screen if prompted:
     - User Type: "External" (for personal accounts)
     - App name: "TrashMail Panda"
     - User support email: Your email
     - Developer contact: Your email
     - Click "Save and Continue" through remaining steps
   - Application type: "Desktop app"
   - Name: "TrashMail Panda Desktop"
   - Click "Create"

5. **Copy Your Credentials**:
   - You'll see a dialog with Client ID and Client Secret
   - Keep this window open - you'll need these during setup

**During Setup Wizard**:
- The wizard will ask you to paste your Client ID and Client Secret
- Then it will open your browser for authorization
- Sign in to your Google account and click "Allow"
- That's it! Credentials are stored securely in your OS keychain

**Environment Variables** (For Advanced Users / CI/CD):
- `GOOGLE_CLIENT_ID`: OAuth 2.0 client ID (from step 5 above)
- `GOOGLE_CLIENT_SECRET`: OAuth 2.0 client secret (from step 5 above)
- `GOOGLE_REDIRECT_URI`: OAuth callback URL (default: `http://localhost:8080/oauth/callback`)
- `DATABASE_PATH`: Override default database location
- `NO_COLOR=1`: Disable ANSI color codes (for log file redirection)
- `TERM=dumb`: Disable interactive prompts (for CI/CD)

---

## Troubleshooting

### Issue: "Provider initialization timed out"

**Symptoms**:
```
[yellow]WARNING: Google provider initialization timed out[/]
Elapsed Time: 30.0 seconds
```

**Causes**:
1. **Didn't click "Allow" in browser**: Authorization page still waiting
2. **Network connectivity issues**: Check internet connection
3. **OAuth callback not received**: Browser didn't redirect back to app
4. **Firewall blocking localhost**: Port 8080 callback blocked

**Solutions**:
```bash
# 1. Check if authorization page is still open in browser
# Go back to browser and click "Allow" to complete OAuth flow

# 2. Check network connectivity
ping -c 3 www.googleapis.com

# 3. Verify localhost callback port is available
lsof -i :8080  # Should be empty or show TrashMail Panda

# 4. Increase timeout in appsettings.json (if needed)
{
  "ProviderTimeouts": {
    "GoogleInitialization": "00:01:00"  # 60 seconds
  }
}

# 5. Retry initialization
# Select: Retry Initialization from error menu
```

---

### Issue: "Insufficient OAuth scopes"

**Symptoms**:
```
[bold red]ERROR: Google provider initialization failed[/]
Error Code: INSUFFICIENT_SCOPES
Message: OAuth token is valid but missing required scopes
Missing Scopes:
  - https://www.googleapis.com/auth/contacts.readonly
```

**Why This Happens**:
- You're using credentials from an older version of TrashMail Panda
- New features require additional Google API permissions
- Example: App added Google Contacts integration, but your token only has Gmail access

**Solutions**:
```bash
# 1. Select "Reconfigure Google Provider" from error menu
# 2. Select "Re-authorize Current Account"
# 3. Browser opens showing NEW permissions request
# 4. Review the additional permissions:
#    - Original: "Read, compose, send, and permanently delete all your email from Gmail"
#    - NEW: "See and download your contacts" (example)
# 5. Click "Allow" to grant the new permissions
# 6. Return to terminal - app automatically retries with updated token
```

**Why Not Automatic?**
Google requires explicit user consent for scope changes - the app cannot silently upgrade permissions. This protects your privacy.

**Check Current Scopes** (Advanced):
```bash
# View token scopes stored in keychain (macOS)
security find-generic-password -s "TrashMailPanda.Google" -w | base64 -d | jq .scopes

# Expected output for current version:
[
  "https://www.googleapis.com/auth/gmail.modify",
  "https://www.googleapis.com/auth/contacts.readonly"  # Added in future version
]
```

---

### Issue: "Database file is locked"

**Symptoms**:
```
[bold red]ERROR: Storage provider initialization failed[/]
Error Code: DB_LOCKED
Message: Database file is locked by another process
```

**Causes**:
- Another TrashMail Panda instance running
- SQLite browser tool has database open
- Crashed process left lock files

**Solutions**:
```bash
# 1. Find process using database
lsof ./data/app.db

# 2. Kill blocking process
kill -9 <PID>

# 3. Remove SQLite lock files
rm ./data/app.db-shm
rm ./data/app.db-wal

# 4. Retry initialization
dotnet run --project src/TrashMailPanda
```

---

### Issue: "No configuration detected - starting setup wizard" (unexpected)

**Symptoms**: Setup wizard runs even though you already configured providers

**Causes**:
- Configuration files deleted
- OS keychain credentials cleared
- Running from different working directory

**Solutions**:
```bash
# 1. Check if configuration exists
ls -la ./data/app.db
cat appsettings.json

# 2. Verify keychain credentials (macOS)
security find-generic-password -s "TrashMailPanda.Google"

# 3. Check working directory
pwd  # Should be /Users/mmorales/Dev/trashmail-panda

# 4. Re-run setup wizard if credentials lost
dotnet run --project src/TrashMailPanda -- --wizard
```

---

### Issue: Terminal displays garbled characters instead of colors

**Symptoms**: See `\x1b[31m` instead of red text

**Causes**:
- Terminal doesn't support ANSI color codes
- `TERM` environment variable incorrectly set

**Solutions**:
```bash
# 1. Check TERM variable
echo $TERM
# Should be: xterm-256color, screen-256color, etc.

# 2. Set TERM explicitly
export TERM=xterm-256color

# 3. Disable colors as workaround
export NO_COLOR=1
dotnet run --project src/TrashMailPanda

# 4. Update appsettings.json
{
  "ConsoleDisplayOptions": {
    "UseColors": false
  }
}
```

---

## Testing

### Manual Testing Checklist

**First-Time Setup**:
- [ ] Delete `./data/app.db` and OS keychain credentials
- [ ] Run application → Setup wizard starts automatically
- [ ] Complete Storage setup → Database created
- [ ] Complete Google setup → Browser OAuth flow works
- [ ] Wizard completes → App automatically initializes all providers
- [ ] Mode selection menu displays → All modes available

**Provider Failure Scenarios**:
- [ ] Expire Google OAuth token manually → Error menu shows "Reconfigure Google"
- [ ] Corrupt database file → Error menu shows "Reconfigure Storage"
- [ ] Timeout simulation (set timeout to 1 second) → Timeout error shown

**Error Recovery**:
- [ ] Select "Retry Initialization" → Provider re-attempts initialization
- [ ] Select "Reconfigure Provider" → Wizard re-runs for that provider
- [ ] Select "Exit" → Application shuts down gracefully

**Mode Selection**:
- [ ] All providers healthy → All modes available
- [ ] Select "Email Triage" → Mode starts correctly
- [ ] Press 'Q' → Application exits

### Automated Testing

**Unit Tests**:
```bash
# Run console orchestration tests
dotnet test --filter "FullyQualifiedName~ConsoleStartupOrchestratorTests"

# Run configuration wizard tests
dotnet test --filter "FullyQualifiedName~ConfigurationWizardTests"

# Run mode selection tests
dotnet test --filter "FullyQualifiedName~ModeSelectionMenuTests"
```

**Integration Tests** (require real providers):
```bash
# Set up test environment (for automated/CI testing only)
export GOOGLE_CLIENT_ID="test_client_id"
export GOOGLE_CLIENT_SECRET="test_client_secret"

# Run integration tests
dotnet test --filter "Category=Integration&FullyQualifiedName~Console"
```

**Performance Tests**:
```bash
# Measure startup time (target: <10 seconds for 2 providers)
time dotnet run --project src/TrashMailPanda -- --non-interactive

# Expected output:
# real    0m5.300s
# user    0m1.000s
# sys     0m0.300s
```

---

## Advanced Usage

### Custom Console Themes

Create `themes/custom.json`:
```json
{
  "SuccessColor": "green",
  "ErrorColor": "red bold",
  "WarningColor": "yellow",
  "InfoColor": "blue",
  "ProviderColors": {
    "Storage": "cyan",
    "Gmail": "magenta",
    "OpenAI": "green"
  }
}
```

Load theme:
```bash
dotnet run --project src/TrashMailPanda -- --theme=themes/custom.json
```

### Scripting Startup for Automation

**Non-Interactive Startup**:
```bash
#!/bin/bash
# startup.sh - Automated startup script

set -e

# Pre-check: Verify credentials exist
if [ ! -f ./data/app.db ]; then
    echo "ERROR: Database not configured. Run setup wizard first."
    exit 1
fi

# Run application in non-interactive mode
dotnet run --project src/TrashMailPanda -- \
    --non-interactive \
    --error-detail=minimal \
    --skip-mode-menu

# Exit code: 0 = success, 1 = provider failure
exit $?
```

**CI/CD Integration**:
```yaml
# .github/workflows/integration-test.yml
- name: Test Console Startup
  run: |
    dotnet run --project src/TrashMailPanda -- --non-interactive || exit 1
  env:
    GOOGLE_CLIENT_ID: ${{ secrets.GOOGLE_CLIENT_ID }}
    GOOGLE_CLIENT_SECRET: ${{ secrets.GOOGLE_CLIENT_SECRET }}
    DATABASE_PATH: ./test-data/app.db
```

---

## Next Steps

After successful startup:
1. **Select Email Triage Mode** → Start organizing your inbox
2. **Configure Rules** → Set up automatic email handling rules
3. **Launch UI Mode** → Switch to Avalonia visual interface (if available)
4. **Explore Future AI Features** → When AI providers are integrated, unlock smart classification

For more information:
- Architecture: See `specs/057-console-startup-orchestration/data-model.md`
- Development: See `specs/057-console-startup-orchestration/plan.md`
- API Reference: See `specs/057-console-startup-orchestration/contracts/console-commands.md`
