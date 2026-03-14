# trashmail-panda Development Guidelines

Auto-generated from all feature plans. Last updated: 2026-03-14

## Active Technologies
- SQLite + SQLCipher (existing encrypted storage), extended for email feature vectors and full email archive (054-ml-architecture-design)
- C# 12, .NET 9.0 + Microsoft.Data.Sqlite 9.0.8, SQLitePCLRaw.bundle_e_sqlcipher 2.1.11, System.Text.Json (built-in) (055-ml-data-storage)
- SQLite with SQLCipher encryption (existing encrypted database at `data/app.db`) (055-ml-data-storage)

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
- 055-ml-data-storage: Added C# 12, .NET 9.0 + Microsoft.Data.Sqlite 9.0.8, SQLitePCLRaw.bundle_e_sqlcipher 2.1.11, System.Text.Json (built-in)
- 054-ml-architecture-design: Added .NET 9.0 / C# 12+ + ML.NET (planned), existing provider framework (`IProvider<TConfig>`, `BaseProvider<TConfig>`), Microsoft.Extensions.DI/Logging

- 054-ml-architecture-design: Added .NET 9.0 / C# 12+ + ML.NET (planned), existing provider framework (`IProvider<TConfig>`, `BaseProvider<TConfig>`), Microsoft.Extensions.DI/Logging

<!-- MANUAL ADDITIONS START -->
<!-- MANUAL ADDITIONS END -->
