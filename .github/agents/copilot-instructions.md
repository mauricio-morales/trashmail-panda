# trashmail-panda Development Guidelines

Auto-generated from all feature plans. Last updated: 2026-03-16

## Active Technologies
- SQLite + SQLCipher (existing encrypted storage), extended for email feature vectors and full email archive (054-ml-architecture-design)
- C# 12, .NET 9.0 + Microsoft.Data.Sqlite 9.0.8, SQLitePCLRaw.bundle_e_sqlcipher 2.1.11, System.Text.Json (built-in) (055-ml-data-storage)
- SQLite with SQLCipher encryption (existing encrypted database at `data/app.db`) (055-ml-data-storage)
- C# 12 / .NET 9.0 + Avalonia UI 11 (to be replaced with console), CommunityToolkit.Mvvm (UI only), Microsoft.Extensions.Hosting/DI/Logging, Spectre.Console (NEW - for console formatting), Google.Apis.Gmail.v1, Polly (057-console-startup-orchestration)
- SQLite with SQLCipher encryption via Microsoft.Data.Sqlite (057-console-startup-orchestration)

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
- 057-console-startup-orchestration: Added C# 12 / .NET 9.0 + Avalonia UI 11 (to be replaced with console), CommunityToolkit.Mvvm (UI only), Microsoft.Extensions.Hosting/DI/Logging, Spectre.Console (NEW - for console formatting), Google.Apis.Gmail.v1, Polly
- 055-ml-data-storage: Added C# 12, .NET 9.0 + Microsoft.Data.Sqlite 9.0.8, SQLitePCLRaw.bundle_e_sqlcipher 2.1.11, System.Text.Json (built-in)
- 054-ml-architecture-design: Added .NET 9.0 / C# 12+ + ML.NET (planned), existing provider framework (`IProvider<TConfig>`, `BaseProvider<TConfig>`), Microsoft.Extensions.DI/Logging


<!-- MANUAL ADDITIONS START -->
<!-- MANUAL ADDITIONS END -->
