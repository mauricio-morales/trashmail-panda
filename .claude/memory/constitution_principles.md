---
name: Constitution Principles
description: Authoritative engineering standards from .specify/memory/constitution.md (v2.0.0, ratified 2026-03-14, last amended 2026-03-23)
type: project
---

These are NON-NEGOTIABLE rules enforced by CI/CD. Constitution v2.0.0 — Avalonia/MVVM principle was removed in this version.

**I. Provider-Agnostic Architecture**
All external integrations MUST go through provider interfaces. Every provider implements `IProvider<TConfig>` with: `InitializeAsync`, `ShutdownAsync`, `ValidateConfigurationAsync`, `HealthCheckAsync`. Config uses DataAnnotations + `IValidateOptions<T>`. Dependencies via Microsoft.Extensions.DependencyInjection.

**II. Result Pattern (NON-NEGOTIABLE)**
All provider/service methods return `Result<T>` or `Result`. No throwing from provider code. Use `Result.Success()` / `Result.Failure()`. Error types: `ConfigurationError`, `AuthenticationError`, `NetworkError`, `ValidationError`, `InitializationError`. Each error has: `Message`, `ErrorCode`, `Category`, `IsTransient`, `RequiresUserIntervention`. Callers inspect `result.IsSuccess` — never wrap in try/catch.

**III. Security First (NON-NEGOTIABLE)**
- Tokens/API keys/email content: never logged, never plaintext on disk, never in VCS
- OAuth tokens + API keys: OS keychain only (DPAPI / macOS Keychain / libsecret)
- DB: SQLCipher encryption at rest
- All DB queries: parameterized statements only
- External API calls: HTTPS with cert validation
- All credential ops recorded by `SecurityAuditLogger`

**IV. One Public Type Per File**
Exactly one public class/interface/enum/record per file. Internal helpers in same file are ok. Global usings in `GlobalUsings.cs`.

**V. Strict Null Safety**
`<Nullable>enable</Nullable>` in every project. All params/return types must have explicit nullability. No `object` as catch-all — use generics or specific types.

**VI. Test Coverage & Quality Gates**
- Global: 90% coverage; Providers: 95%; Security components: 100%
- Unit tests: xUnit with `[Trait("Category", "Unit")]`
- Integration tests needing real APIs: gated with `Skip` attributes
- Pre-commit hook: `dotnet format`
- CI/CD: build + test + format + security audit on every PR

**PR Checklist:**
- Result pattern — no `catch` blocks in providers
- One public type per file
- Nullable annotations present
- Tests included, coverage maintained
- `dotnet format --verify-no-changes` passes
- No secrets in committed files

**How to apply:** Check every code suggestion against these six principles before writing. Flag any violation immediately.
