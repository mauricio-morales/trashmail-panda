# trashmail-panda Development Guidelines

Auto-generated from all feature plans. Last updated: 2026-03-21

## Active Technologies
- SQLite + SQLCipher (existing encrypted storage), extended for email feature vectors and full email archive (054-ml-architecture-design)
- C# 12, .NET 9.0 + Microsoft.Data.Sqlite 9.0.8, SQLitePCLRaw.bundle_e_sqlcipher 2.1.11, System.Text.Json (built-in) (055-ml-data-storage)
- SQLite with SQLCipher encryption (existing encrypted database at `data/app.db`) (055-ml-data-storage)
- C# 12 / .NET 9.0 (056-console-gmail-oauth)
- SQLite with SQLCipher encryption + OS keychain (DPAPI/macOS Keychain/libsecret) via SecureStorageManager (056-console-gmail-oauth)
- C# 12 / .NET 9.0 + Avalonia UI 11 (to be replaced with console), CommunityToolkit.Mvvm (UI only), Microsoft.Extensions.Hosting/DI/Logging, Spectre.Console (NEW - for console formatting), Google.Apis.Gmail.v1, Polly (057-console-startup-orchestration)
- SQLite with SQLCipher encryption via Microsoft.Data.Sqlite (057-console-startup-orchestration)
- .NET 9.0 / C# 12 (nullable reference types enabled) + Spectre.Console 0.48.0, Microsoft.Extensions.Hosting/DI/Logging v9.0.8, CommunityToolkit.Mvvm 8.2.1, TrashMailPanda.Providers.ML, TrashMailPanda.Providers.Email, TrashMailPanda.Providers.Storage (060-console-tui-spectre)
- SQLite via EF Core (`TrashMailPandaDbContext`); `email_features` column migration (`training_label TEXT NULL`); `IEmailArchiveService` for training signals + queue (add `SetTrainingLabelAsync`, `CountLabeledAsync`, `GetUntriagedAsync`) (060-console-tui-spectre)
- C# 12 / .NET 9.0 (`net9.0`) + Microsoft.Extensions.Hosting/DI/Logging, Spectre.Console 0.48, CommunityToolkit.Mvvm 8.2, Avalonia 11.3 (conditionally), ML.NET, Serilog (061-backend-ui-abstraction)
- SQLite with SQLCipher via EF Core + Microsoft.Data.Sqlite (061-backend-ui-abstraction)
- C# 12 / .NET 9.0 (nullable reference types enabled) + Spectre.Console (TUI), Microsoft.Extensions.DI/Logging, ML.NET, Google.Apis.Gmail.v1, Polly (062-runtime-classification-feedback)
- SQLite with SQLCipher encryption (existing `email_features` table, `IEmailArchiveService`) (062-runtime-classification-feedback)

- .NET 9.0 / C# 12+ + ML.NET (planned), existing provider framework (`IProvider<TConfig>`, `BaseProvider<TConfig>`), Microsoft.Extensions.DI/Logging (054-ml-architecture-design)

## Project Structure

```text
src/
tests/
```

## Commands

# Add commands for .NET 9.0 / C# 12+

## Code Style

.NET 9.0 / C# 12+: Follow standard conventions

## Recent Changes
- 062-runtime-classification-feedback: Added C# 12 / .NET 9.0 (nullable reference types enabled) + Spectre.Console (TUI), Microsoft.Extensions.DI/Logging, ML.NET, Google.Apis.Gmail.v1, Polly
- 062-runtime-classification-feedback: Added [if applicable, e.g., PostgreSQL, CoreData, files or N/A]
- 061-backend-ui-abstraction: Added C# 12 / .NET 9.0 (`net9.0`) + Microsoft.Extensions.Hosting/DI/Logging, Spectre.Console 0.48, CommunityToolkit.Mvvm 8.2, Avalonia 11.3 (conditionally), ML.NET, Serilog


<!-- MANUAL ADDITIONS START -->

## Console OAuth Technologies (056-console-gmail-oauth)

- **Spectre.Console v0.48.0+**: Console UI framework for colored output, interactive prompts, progress bars
  - Color markup: `[green]✓[/]`, `[bold red]✗[/]`, `[cyan]info[/]`
  - Interactive: `AnsiConsole.Confirm()`, `AnsiConsole.Prompt()`, `SelectionPrompt<T>()`
  - Progress: `AnsiConsole.Status().Spinner()`, `AnsiConsole.Progress()`
  
- **System.Net.HttpListener**: Localhost OAuth callback server
  - Dynamic port allocation (port 0 = OS assigns)
  - 127.0.0.1 only (no external access)
  - PKCE (Proof Key for Code Exchange) security with SHA256
  
- **Google.Apis.Gmail.v1 + Google.Apis.Auth.OAuth2**: Gmail OAuth integration
  - Browser-based OAuth 2.0 authorization code flow
  - Automatic token refresh via UserCredential
  - SecureStorageManager integration for OS keychain storage

<!-- MANUAL ADDITIONS END -->
