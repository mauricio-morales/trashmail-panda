<!--
  Sync Impact Report
  ==================
  Version change: 0.0.0 → 1.0.0 (MAJOR — initial ratification)
  Modified principles: N/A (first version)
  Added sections:
    - Core Principles (7 principles)
    - Technology & Quality Standards
    - Development Workflow & Review
    - Governance
  Removed sections: N/A
  Templates requiring updates:
    - .specify/templates/plan-template.md ✅ (no changes needed;
      Constitution Check section dynamically references this file)
    - .specify/templates/spec-template.md ✅ (no changes needed;
      generic template, no principle-specific references)
    - .specify/templates/tasks-template.md ✅ (no changes needed;
      generic template, no principle-specific references)
  Follow-up TODOs: None
-->

# TrashMail Panda Constitution

## Core Principles

### I. Provider-Agnostic Architecture

All external integrations MUST be abstracted through provider
interfaces. No feature may depend directly on a specific email
service, LLM vendor, or storage engine.

- Every provider MUST implement `IProvider<TConfig>` with lifecycle
  methods: `InitializeAsync`, `ShutdownAsync`,
  `ValidateConfigurationAsync`, `HealthCheckAsync`.
- Provider configuration MUST use `DataAnnotations` validation and
  `IValidateOptions<T>` for startup checks.
- Dependencies MUST be resolved through
  `Microsoft.Extensions.DependencyInjection`.

**Rationale**: Enables future provider additions (IMAP, local ML,
PostgreSQL) without modifying consuming code.

### II. Result Pattern (NON-NEGOTIABLE)

All provider and service methods MUST return `Result<T>` or `Result`.
Throwing exceptions from provider code is prohibited.

- Use `Result.Success()` and `Result.Failure()` factory methods.
- Error types: `ConfigurationError`, `AuthenticationError`,
  `NetworkError`, `ValidationError`, `InitializationError`.
- Each error includes `Message`, `ErrorCode`, `Category`,
  `IsTransient`, and `RequiresUserIntervention`.
- Callers MUST inspect `result.IsSuccess` — never wrap calls in
  `try/catch`.

**Rationale**: Deterministic error handling without hidden control
flow; every failure path is explicit and typed.

### III. Security First (NON-NEGOTIABLE)

Sensitive data (tokens, API keys, email content) MUST never be logged,
serialized to disk in plaintext, or committed to version control.

- OAuth tokens and API keys MUST be stored via OS keychain
  (DPAPI / macOS Keychain / libsecret).
- Database storage MUST use SQLCipher encryption at rest.
- All database queries MUST use parameterized statements.
- External API calls MUST use HTTPS with certificate validation.
- All credential operations MUST be recorded by
  `SecurityAuditLogger`.

**Rationale**: User email data is highly sensitive; defense-in-depth
prevents credential leakage across every storage layer.

### IV. MVVM with CommunityToolkit.Mvvm

All UI MUST follow the MVVM pattern using `CommunityToolkit.Mvvm`.

- ViewModels MUST extend `ObservableObject` and use
  `[ObservableProperty]` for data binding.
- User actions MUST use `[RelayCommand]` /
  `[AsyncRelayCommand]`.
- No business logic in XAML code-behind.
- UI colors MUST use `ProfessionalColors` semantic helpers — hardcoded
  RGB hex values are prohibited.

**Rationale**: Separation of concerns enables testable UI logic and
consistent theming across the application.

### V. One Public Type Per File

Each source file MUST contain exactly one public class, interface,
enum, or record.

- Internal helper types colocated in the same file are permitted.
- Global using statements reside in `GlobalUsings.cs`.

**Rationale**: Keeps file responsibility clear, simplifies navigation,
and reduces merge conflicts.

### VI. Strict Null Safety

Nullable reference types MUST be enabled in every project
(`<Nullable>enable</Nullable>`).

- All parameter and return types MUST have explicit nullability
  annotations.
- `object` MUST NOT be used as a catch-all type; prefer
  generics or specific types.

**Rationale**: Eliminates null-reference exceptions at compile time
and documents intent in signatures.

### VII. Test Coverage & Quality Gates

All code changes MUST pass automated quality checks before merge.

- Global coverage target: 90%.
- Provider implementations: 95% coverage.
- Security components: 100% coverage.
- Unit tests use xUnit with `[Trait("Category", "Unit")]`.
- Integration tests requiring real APIs MUST be gated with
  `Skip` attributes and documented credential setup.
- Pre-commit hook runs `dotnet format` (whitespace, style,
  analyzers).
- CI/CD validates: build, test, format, security audit on every PR.

**Rationale**: Automated enforcement prevents regressions and keeps
the codebase consistently formatted and secure.

## Technology & Quality Standards

- **.NET 9.0** with **C# 12+**, targeting `net9.0`.
- **Avalonia UI 11** for cross-platform desktop rendering.
- **Microsoft.Extensions.Hosting/DI/Logging/Configuration** for
  application infrastructure.
- **Microsoft.Data.Sqlite + SQLitePCLRaw.bundle_e_sqlcipher** for
  encrypted local storage.
- **Google.Apis.Gmail.v1** for Gmail integration.
- **Polly** for resilience and rate-limiting on external APIs.
- **xUnit + Moq + coverlet** for testing and coverage.
- Schema changes MUST go through the migration system — no manual
  `ALTER TABLE` statements.
- Builds MUST succeed with `dotnet build --configuration Release`
  and `dotnet format --verify-no-changes` before push.

## Development Workflow & Review

- Run `./setup-hooks.sh` once to install the pre-commit formatting
  hook.
- Daily workflow: `dotnet run --project src/TrashMailPanda`,
  `dotnet build`, `dotnet test`, `dotnet format`.
- Before push: `dotnet build -c Release && dotnet test -c Release
  && dotnet format --verify-no-changes`.
- CI/CD runs multi-platform builds (Ubuntu, Windows, macOS) with
  platform-specific security tests (libsecret, DPAPI, Keychain).
- PR checklist:
  - Result pattern — no `catch` blocks in providers.
  - One public type per file.
  - No hardcoded colors.
  - Nullable annotations present.
  - Tests included; coverage maintained.
  - `dotnet format --verify-no-changes` passes.
  - No secrets in committed files.
- Use `CLAUDE.md` as the primary runtime development guidance file.

## Governance

This constitution is the authoritative source for project-wide
engineering standards. All other documents (CLAUDE.md, README.md,
PRPs, specs) MUST remain consistent with these principles.

- **Amendments** require: (1) a written proposal describing the
  change, (2) rationale for the change, and (3) an update to this
  document with incremented version.
- **Versioning** follows SemVer: MAJOR for principle
  removals/redefinitions, MINOR for new principles or material
  expansions, PATCH for clarifications and typo fixes.
- **Compliance** is verified via CI/CD quality gates and the PR
  review checklist above.
- **Complexity justification**: any deviation from these principles
  MUST be documented in the feature's `plan.md` under
  "Complexity Tracking" with a rejected-simpler-alternative.

**Version**: 1.0.0 | **Ratified**: 2026-03-14 | **Last Amended**: 2026-03-14
