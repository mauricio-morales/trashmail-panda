# TrashMail Panda Constitution

## Core Principles

### I. Provider-Agnostic Architecture (NON-NEGOTIABLE)
All functionality must be implemented through provider interfaces (`IEmailProvider`, `ILLMProvider`, `IStorageProvider`); Providers must implement the `IProvider<TConfig>` base interface with lifecycle management, health checks, and dependency injection; Each provider must be self-contained, independently testable, and configurable; Clear separation of concerns - no direct provider coupling in application logic

### II. Result Pattern Error Handling (NON-NEGOTIABLE)
All provider methods and async operations must use the `Result<T>` pattern instead of throwing exceptions; Exceptions are only for truly exceptional circumstances, not business logic; All error states must be explicit and typed; UI and services must handle both success and failure cases gracefully

### III. Security & Privacy First
Local-first architecture: All email data processing happens on device; Encrypted credentials using OS keychain (DPAPI, macOS Keychain, libsecret); SQLite database with SQLCipher encryption; Never log or expose sensitive data (tokens, email content, API keys); All actions must be reversible from Gmail trash; No cloud dependencies for email processing

### IV. MVVM with Strong Typing
Use CommunityToolkit.Mvvm with ObservableProperty and RelayCommand patterns; All nullable reference types must be properly annotated; No generic object returns - use strongly typed interfaces; Follow Avalonia UI 11 patterns with proper data binding

### V. Configuration Validation & Health Monitoring
All provider configurations must use DataAnnotations validation with IValidateOptions<T>; Implement comprehensive health checks via IHealthCheck; Startup orchestration with parallel provider initialization; Real-time provider status monitoring through dedicated services; Graceful degradation when providers are unavailable

## Security Requirements

### Credential Management
- **SecureStorageManager**: Centralized credential storage with OS keychain integration
- **Zero-password Experience**: Transparent authentication using system-level security
- **Token Rotation**: Automated OAuth token renewal and lifecycle management
- **Security Audit Logging**: All credential operations must be logged for compliance

### Data Protection
- **Encryption at Rest**: All persistent data stored with SQLCipher encryption
- **Sanitized Processing**: Remote content blocked during email analysis
- **Minimal LLM Context**: Only essential data sent to AI providers
- **Master Key Management**: Keys derived from system entropy, never hardcoded

### Database Security
- **Migration System**: All schema changes through versioned migrations
- **Parameterized Queries**: No SQL injection vulnerabilities
- **Auto-upgrade**: Database schema automatically migrated on app start
- **Data Integrity**: All migrations must preserve existing user data

## Development Standards

### Code Quality Gates
- **Formatting**: `dotnet format --verify-no-changes` must pass before commit
- **Testing**: 90% coverage target, 95% for providers, 100% for security components
- **Type Safety**: Nullable reference types enabled with strict mode
- **Build**: `dotnet build --configuration Release` must pass without warnings
- **Code Organization**: One public class/interface/enum/record per file for maintainability

### Testing Strategy
- **Unit Tests**: xUnit with comprehensive provider testing
- **Integration Tests**: End-to-end provider integration (requires real credentials for local testing)
- **Result Pattern Testing**: Test both success and failure paths
- **Mock Strategies**: Use proper mocking for external dependencies

### UI/UX Standards
- **Semantic Colors**: Never use hardcoded RGB values, always use `ProfessionalColors` class
- **Theme Consistency**: Use semantic color names for status indicators
- **Professional Design**: White cards with 8px corners, established typography hierarchy
- **Accessibility**: Support for screen readers and keyboard navigation

## Technology Constraints

### Core Stack (NON-NEGOTIABLE)
- **.NET 9.0**: Target framework for all projects
- **Avalonia UI 11**: Cross-platform desktop UI framework
- **CommunityToolkit.Mvvm**: MVVM implementation with source generators
- **Microsoft.Extensions**: DI, Logging, Hosting, Configuration patterns
- **SQLite + SQLCipher**: Local database with encryption

### Provider Technologies
- **Gmail**: Google.Apis.Gmail.v1 official client with OAuth2
- **OpenAI**: Official OpenAI client with GPT-4o-mini optimization
- **Storage**: Microsoft.Data.Sqlite with SQLitePCLRaw.bundle_e_sqlcipher

### Performance Standards
- **Batch Operations**: All email processing must support batch operations
- **Rate Limiting**: Polly-based retry policies for all external API calls
- **Streaming**: Support for processing large email sets without memory issues
- **Cancellation**: All long-running operations must support CancellationToken

## Governance

### Architecture Decisions
This constitution supersedes all other development practices; Provider architecture decisions require security review; All new providers must implement the full IProvider<TConfig> contract; Breaking changes require migration paths and user communication

### Code Review Requirements
All PRs must verify constitutional compliance; Security-related changes require additional review; Provider implementations require comprehensive test coverage; UI changes must follow semantic color guidelines

### Quality Assurance
Pre-commit hooks enforce formatting standards; CI/CD pipeline validates build, test, and security checks; Integration tests validate provider contracts; Performance benchmarks for email processing operations

**Version**: 1.0.0 | **Ratified**: 2025-09-12 | **Last Amended**: 2025-09-12