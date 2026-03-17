# Implementation Plan: Console-based Gmail OAuth Flow

**Branch**: `056-console-gmail-oauth` | **Date**: March 16, 2026 | **Spec**: [spec.md](spec.md)
**Input**: Feature specification from `/specs/056-console-gmail-oauth/spec.md`

**Note**: This template is filled in by the `/speckit.plan` command. See `.specify/templates/plan-template.md` for the execution workflow.

## Summary

Implement a console-based Gmail OAuth 2.0 authentication flow that enables users to authenticate with Gmail through their default browser, with OAuth callbacks handled via a temporary localhost HTTP listener. The system will securely store refresh tokens using the existing OS keychain infrastructure (SecureStorageManager), support automatic token validation/refresh, and provide clear colored console feedback throughout the authentication process using Spectre.Console. This feature enables seamless Gmail authentication for console-based workflows and transitions toward local ML architecture.

## Technical Context

**Language/Version**: C# 12 / .NET 9.0  
**Primary Dependencies**: 
- Avalonia UI 11 (existing desktop app framework)
- Google.Apis.Gmail.v1 (existing Gmail API integration)
- Google.Apis.Auth.OAuth2 (existing OAuth implementation)
- Spectre.Console (new - for colored console output and interaction)
- Microsoft.Extensions.Hosting/DI/Logging (existing infrastructure)

**Storage**: SQLite with SQLCipher encryption + OS keychain (DPAPI/macOS Keychain/libsecret) via SecureStorageManager  
**Testing**: xUnit with Moq (existing test infrastructure), manual OAuth flow testing with real Gmail API  
**Target Platform**: Cross-platform desktop (Windows, macOS, Linux) with console output support  
**Project Type**: Desktop application with hybrid GUI/console capabilities  
**Performance Goals**: 
- OAuth flow completion: <90 seconds end-to-end
- Token validation/refresh: <3 seconds
- HTTP callback listener startup: <500ms

**Constraints**: 
- OAuth timeout: 5 minutes maximum for user interaction
- Browser launch timeout: 10 seconds
- Token storage must use OS keychain (no plaintext)
- Console output must support ANSI colors on all platforms

**Scale/Scope**: Single-user desktop application, ~500 LOC for console OAuth handler, 3 new services, reuse existing GmailOAuthService patterns

## Constitution Check

*GATE: Must pass before Phase 0 research. Re-check after Phase 1 design.*

### Principle I: Provider-Agnostic Architecture
✅ **PASS** - Reuses existing `IEmailProvider` (GmailEmailProvider) and `ISecureStorageManager` abstractions. Console OAuth handler will be independent of provider implementation, using existing interfaces.

### Principle II: Result Pattern
✅ **PASS** - All new console OAuth methods will return `Result<T>`. No exceptions thrown from service methods. Error handling via `AuthenticationError`, `NetworkError`, `ConfigurationError`.

### Principle III: Security First
✅ **PASS** - OAuth tokens stored via existing `SecureStorageManager` (OS keychain). No logging of sensitive tokens. HTTP listener uses localhost only with timeout protection. Implements OAuth 2.0 PKCE flow for additional security.

### Principle IV: MVVM with CommunityToolkit.Mvvm
⚠️ **N/A** - This is console-based, not Avalonia UI. No ViewModels required. Console interaction uses Spectre.Console for colored output and prompts.

### Principle V: One Public Type Per File
✅ **PASS** - Each new service class (ConsoleOAuthHandler, TokenValidator, etc.) will be in separate files.

### Principle VI: Strict Null Safety
✅ **PASS** - Nullable reference types enabled. All parameters and return types will have explicit nullability.

### Principle VII: Test Coverage & Quality Gates
✅ **PASS** - Target 90% coverage for OAuth handler logic. Integration tests will be marked with `[Fact(Skip = "Requires OAuth")]` for manual testing. Unit tests for token validation, error handling, configuration checks.

### Additional Context
This feature is **Issue #3** in the broader architectural shift (see `/docs/architecture/ARCHITECTURE_SHIFT_TO_LOCAL_ML.md`). It establishes console-based authentication patterns that will be reused for the full console TUI application and enables later training data collection from Gmail archives/deleted/spam folders.

---

### Post-Design Re-Evaluation ✅

**All principles still satisfied after Phase 1 design:**

1. ✅ **Provider-Agnostic**: Design uses existing `IEmailProvider`, `ISecureStorageManager` interfaces - no direct dependencies on Gmail specifics
2. ✅ **Result Pattern**: All interface contracts defined with `Task<Result<T>>` return types in `contracts/service-contracts.md`
3. ✅ **Security First**: 
   - PKCE implementation verified in research.md
   - State parameter CSRF protection documented
   - OS keychain storage confirmed via SecureStorageManager
   - No token logging enforced in design
4. ✅ **One Type Per File**: Interface contracts define 3 separate interfaces (IConsoleOAuthHandler, ITokenValidator, ILocalOAuthCallbackListener)
5. ✅ **Null Safety**: All entity definitions in data-model.md use explicit nullability (`string?`, `required string`)
6. ✅ **Testing**: Quickstart.md includes unit test patterns, integration test patterns with proper Skip attributes

**No design changes needed** - proceeding to implementation phase.

## Project Structure

### Documentation (this feature)

```text
specs/056-console-gmail-oauth/
├── plan.md              # This file (/speckit.plan command output)
├── research.md          # Phase 0 output (/speckit.plan command)
├── data-model.md        # Phase 1 output (/speckit.plan command)
├── quickstart.md        # Phase 1 output (/speckit.plan command)
├── contracts/           # Phase 1 output (/speckit.plan command)
└── tasks.md             # Phase 2 output (/speckit.tasks command - NOT created by /speckit.plan)
```

### Source Code (repository root)

```text
src/
├── TrashMailPanda/
│   └── TrashMailPanda/                    # Main Avalonia app (existing)
│       ├── Services/
│       │   ├── GmailOAuthService.cs       # Existing OAuth service (browser-based)
│       │   └── ConsoleOAuthHandler.cs     # NEW: Console OAuth handler
│       ├── Models/
│       │   └── OAuthFlowResult.cs         # NEW: OAuth flow result model
│       └── Program.cs                     # Entry point - add console mode detection
├── Shared/
│   └── TrashMailPanda.Shared/              # Shared types and utilities (existing)
│       ├── Security/
│       │   └── SecureStorageManager.cs    # Existing - reuse for token storage
│       └── Base/
│           └── Result.cs                  # Existing - Result<T> pattern
└── Providers/
    └── Email/
        └── TrashMailPanda.Providers.Email/ # Existing Gmail provider
            ├── GmailEmailProvider.cs       # Existing - OAuth initialization
            └── Models/
                └── GmailStorageKeys.cs     # Existing - token key constants

tests/
└── TrashMailPanda.Tests/                   # Existing test project
    ├── Unit/
    │   └── Services/
    │       └── ConsoleOAuthHandlerTests.cs # NEW: Unit tests
    └── Integration/
        └── Console/
            └── OAuthFlowTests.cs           # NEW: Integration tests (skipped by default)
```

**Structure Decision**: 
Reusing existing Avalonia application project (`TrashMailPanda.csproj`) and adding console OAuth handler as a service. This enables:
1. Shared dependency injection infrastructure
2. Reuse of existing providers (Email, Storage, LLM)
3. Future transition to dedicated console project (Issue #8 in architectural shift)
4. Minimal project churn - single new service class with supporting models

**Rationale**: The Avalonia app already has OutputType=WinExe but can support console I/O. Adding Spectre.Console as a new dependency enables colored console output without requiring a separate console project at this stage. When the full console TUI is implemented (Issue #8), this OAuth handler will be moved to the new `TrashMailPanda.Console` project.

## Complexity Tracking

> **No constitution violations detected** - all principles satisfied. No simpler alternatives rejected.
