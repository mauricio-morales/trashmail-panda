# PRP: Unified Google Services Provider Architecture

## Goal

**Feature Goal**: Architect and implement a unified Google Services Provider that consolidates Gmail and Contacts providers under a single OAuth authentication flow while maintaining interface separation for future extensibility.

**Deliverable**: A `GoogleServicesProvider` class that implements both `IEmailProvider` and `IContactsProvider` interfaces, with unified OAuth setup UI and shared token management, while internally delegating to specialized sub-providers.

**Success Definition**:
- Single "Google Services" setup card in the UI with unified OAuth flow
- Shared OAuth tokens stored once and used by both Gmail and Contacts functionality
- Internal provider isolation maintained for future extensibility (IMAP email, macOS contacts)
- All existing Gmail and Contacts functionality preserved
- Simplified user setup experience with no duplicate OAuth flows

## Context

### Architecture Challenge
```yaml
current_problem: |
  Gmail and Contacts providers use separate OAuth flows causing:
  - Duplicate OAuth setup dialogs confusing users
  - Split token storage (gmail_ vs google_ prefixes)
  - Inconsistent authentication states between providers
  - Complex UI flow with multiple Google setup cards

solution_approach: |
  Create GoogleServicesProvider wrapper that:
  - Implements both IEmailProvider and IContactsProvider
  - Manages unified OAuth with shared "google_" token prefix
  - Delegates interface methods to internal GmailEmailProvider and ContactsProvider
  - Presents single UI setup component to users
```

### Key Architectural Files
```yaml
provider_interfaces:
  - file: "src/Shared/TrashMailPanda.Shared/IEmailProvider.cs"
    purpose: "Email provider interface to implement"
    key_methods: ["GetEmailsAsync", "SendEmailAsync", "DeleteEmailsAsync"]

  - file: "src/Shared/TrashMailPanda.Shared/IContactsProvider.cs"
    purpose: "Contacts provider interface to implement"
    key_methods: ["GetContactSignalAsync", "GetTrustSignalAsync", "IsKnownAsync"]

  - file: "src/Shared/TrashMailPanda.Shared/Base/IProvider.cs"
    purpose: "Base provider interface with lifecycle management"
    key_methods: ["InitializeAsync", "HealthCheckAsync", "ShutdownAsync"]

existing_implementations:
  - file: "src/Providers/Email/TrashMailPanda.Providers.Email/GmailEmailProvider.cs"
    purpose: "Current Gmail implementation to wrap"
    oauth_prefix: "gmail_" # Problem: should be "google_"

  - file: "src/Providers/Contacts/TrashMailPanda.Providers.Contacts/ContactsProvider.cs"
    oauth_prefix: "google_" # Already using correct prefix
    delegation_target: "GoogleContactsAdapter"

oauth_service:
  - file: "src/Shared/TrashMailPanda.Shared/Security/GoogleOAuthService.cs"
    purpose: "Shared OAuth service for both Gmail and Contacts"
    key_capability: "Handles token storage with configurable prefixes"

startup_orchestration:
  - file: "src/TrashMailPanda/TrashMailPanda/Services/StartupOrchestrator.cs"
    methods: ["InitializeEmailProviderAsync", "InitializeContactsProviderAsync"]
    problem: "Separate initialization methods need consolidation"
```

### OAuth Token Architecture
```yaml
current_token_structure:
  gmail_tokens:
    prefix: "gmail_"
    scopes: ["https://www.googleapis.com/auth/gmail.readonly", "https://www.googleapis.com/auth/gmail.modify"]

  contacts_tokens:
    prefix: "google_"
    scopes: ["https://www.googleapis.com/auth/contacts.readonly"]

unified_token_structure:
  google_services_tokens:
    prefix: "google_"
    combined_scopes:
      - "https://www.googleapis.com/auth/gmail.readonly"
      - "https://www.googleapis.com/auth/gmail.modify"
      - "https://www.googleapis.com/auth/contacts.readonly"
    advantage: "Single OAuth flow, shared tokens, unified setup"
```

### UI Components to Unify
```yaml
current_ui_components:
  - file: "src/TrashMailPanda/TrashMailPanda/ViewModels/GoogleOAuthSetupViewModel.cs"
    purpose: "Gmail OAuth setup dialog - to be generalized"

  - provider_setup_cards:
    location: "UI provider status area"
    current: "Separate Gmail and Contacts setup cards"
    target: "Single 'Google Services' setup card"

startup_integration:
  - file: "src/TrashMailPanda/TrashMailPanda/Services/StartupOrchestrator.cs"
    current_methods: ["ReinitializeGmailProviderAsync", "ReinitializeContactsProviderAsync"]
    target_method: "ReinitializeGoogleServicesAsync"
```

### Configuration Consolidation
```yaml
provider_configs:
  gmail_config:
    file: "GmailProviderConfig"
    contains: ["ClientId", "ClientSecret", "Scopes", "TimeoutSeconds"]

  contacts_config:
    file: "ContactsProviderConfig"
    contains: ["ClientId", "ClientSecret", "Scopes", "EnableContactsCaching"]

  unified_config:
    target: "GoogleServicesProviderConfig"
    consolidates: ["OAuth credentials", "Combined scopes", "Feature flags"]
    maintains: "Separate timeout/caching settings for each service"
```

## External Research Context

### Google OAuth Best Practices
```yaml
oauth_patterns:
  unified_scope_request:
    pattern: "Request all scopes in single OAuth flow"
    scopes_parameter: "space-separated list of all required scopes"
    user_benefit: "Single consent screen for all permissions"

  incremental_authorization:
    when_to_use: "If building feature-by-feature access"
    current_applicaton: "Not needed - request all scopes upfront"

  token_sharing:
    key_principle: "Single access token valid for all granted scopes"
    implementation: "Use same token for Gmail API and People API calls"

  refresh_token_management:
    requirement: "Store refresh tokens securely for offline access"
    lifecycle: "Use refresh tokens to obtain new access tokens"
    critical_note: "Lost refresh tokens require full re-authentication"

security_requirements:
  token_storage: "Use secure storage for refresh tokens"
  state_validation: "Validate state parameter to prevent CSRF"
  scope_validation: "Check which scopes were actually granted"
  client_secrets: "Never expose client secrets in source code"
```

### Architecture Patterns for Multi-API Wrappers
```yaml
delegation_pattern:
  structure: "Unified provider delegates to specialized sub-providers"
  example: "_googleServices.fetchEmailAsync() => this._gmail.fetchEmailAsync()"
  benefit: "Maintains internal specialization while presenting unified interface"

composition_over_inheritance:
  approach: "GoogleServicesProvider contains GmailEmailProvider and ContactsProvider instances"
  interface_implementation: "Implements both IEmailProvider and IContactsProvider"
  method_forwarding: "Forwards interface method calls to appropriate internal provider"

shared_authentication:
  pattern: "Single OAuth service instance shared between sub-providers"
  token_prefix: "Use consistent 'google_' prefix for all Google services"
  scope_management: "Request combined scopes in single OAuth flow"
```

## Implementation Tasks

### Task 1: Create GoogleServicesProviderConfig
```yaml
priority: 1
dependency: "None"
files_to_create:
  - "src/Providers/GoogleServices/TrashMailPanda.Providers.GoogleServices/GoogleServicesProviderConfig.cs"

configuration_properties:
  oauth_credentials: ["ClientId", "ClientSecret", "RedirectUri"]
  combined_scopes: ["Gmail readonly/modify", "Contacts readonly"]
  feature_flags: ["EnableGmail", "EnableContacts"]
  service_specific: ["GmailTimeoutSeconds", "ContactsCachingEnabled"]

validation_attributes:
  - "[Required] for ClientId and ClientSecret"
  - "[Url] for RedirectUri"
  - "Custom validation for scope combinations"
```

### Task 2: Implement GoogleServicesProvider Core Class
```yaml
priority: 2
dependency: "Task 1"
file_to_create: "src/Providers/GoogleServices/TrashMailPanda.Providers.GoogleServices/GoogleServicesProvider.cs"

class_structure:
  inheritance: "BaseProvider<GoogleServicesProviderConfig>"
  interfaces: ["IEmailProvider", "IContactsProvider"]

internal_providers:
  gmail_provider: "GmailEmailProvider instance for email operations"
  contacts_provider: "ContactsProvider instance for contact operations"

composition_pattern: |
    public class GoogleServicesProvider : BaseProvider<GoogleServicesProviderConfig>, IEmailProvider, IContactsProvider
    {
        private readonly GmailEmailProvider _gmailProvider;
        private readonly ContactsProvider _contactsProvider;
        private readonly IGoogleOAuthService _oauthService;

        // IEmailProvider methods delegate to _gmailProvider
        public async Task<Result<IReadOnlyList<EmailMetadata>>> GetEmailsAsync(...)
            => await _gmailProvider.GetEmailsAsync(...);

        // IContactsProvider methods delegate to _contactsProvider
        public async Task<Result<ContactSignal>> GetContactSignalAsync(...)
            => await _contactsProvider.GetContactSignalAsync(...);
    }

initialization_logic:
  oauth_setup: "Configure shared 'google_' token prefix for both sub-providers"
  provider_creation: "Initialize sub-providers with unified OAuth configuration"
  health_coordination: "Aggregate health status from both sub-providers"
```

### Task 3: Update Token Storage Strategy
```yaml
priority: 3
dependency: "Task 2"
files_to_modify:
  - "src/Providers/Email/TrashMailPanda.Providers.Email/GmailEmailProvider.cs"
    change: "Update OAuth service calls to use 'google_' prefix instead of 'gmail_'"

  - "src/Providers/Contacts/TrashMailPanda.Providers.Contacts/Adapters/GoogleContactsAdapter.cs"
    verify: "Confirm already using 'google_' prefix (line 292)"

token_migration_strategy:
  existing_gmail_tokens: "Migrate from 'gmail_' to 'google_' prefix"
  contacts_tokens: "Already using 'google_' prefix - no migration needed"
  migration_method: "Copy tokens during first GoogleServicesProvider initialization"
```

### Task 4: Create Unified OAuth Setup UI
```yaml
priority: 4
dependency: "Task 3"
ui_files_to_modify:
  - "src/TrashMailPanda/TrashMailPanda/ViewModels/GoogleOAuthSetupViewModel.cs"
    rename_to: "GoogleServicesSetupViewModel.cs"
    update_messaging: "Change from 'Gmail' to 'Google Services'"
    scope_display: "Show combined Gmail + Contacts permissions"

provider_setup_integration:
  status_card: "Single 'Google Services' card instead of separate Gmail/Contacts"
  setup_flow: "One OAuth dialog for both services"
  success_state: "Both Gmail and Contacts show as configured when OAuth completes"
```

### Task 5: Update Dependency Injection Registration
```yaml
priority: 5
dependency: "Task 4"
files_to_modify:
  - di_registration_file: "Location of provider service registration"

registration_changes:
  remove: ["IEmailProvider -> GmailEmailProvider", "IContactsProvider -> ContactsProvider"]
  add: ["IEmailProvider -> GoogleServicesProvider", "IContactsProvider -> GoogleServicesProvider"]
  note: "GoogleServicesProvider implements both interfaces"

singleton_management:
  requirement: "Register GoogleServicesProvider as singleton"
  internal_providers: "Inject GmailEmailProvider and ContactsProvider as dependencies"
```

### Task 6: Update StartupOrchestrator Integration
```yaml
priority: 6
dependency: "Task 5"
file_to_modify: "src/TrashMailPanda/TrashMailPanda/Services/StartupOrchestrator.cs"

method_consolidation:
  remove_methods: ["InitializeEmailProviderAsync", "InitializeContactsProviderAsync"]
  add_method: "InitializeGoogleServicesProviderAsync"

  remove_methods: ["ReinitializeGmailProviderAsync", "ReinitializeContactsProviderAsync"]
  add_method: "ReinitializeGoogleServicesProviderAsync"

initialization_logic:
  single_provider: "Initialize GoogleServicesProvider with unified configuration"
  health_check: "Single health check covers both Gmail and Contacts functionality"
  status_reporting: "Report status for both email and contacts capabilities"
```

### Task 7: Handle Configuration Migration and Backward Compatibility
```yaml
priority: 7
dependency: "Task 6"
migration_strategy:
  appsettings_consolidation:
    current: "Separate GmailProviderConfig and ContactsProviderConfig sections"
    target: "Single GoogleServicesProviderConfig section"

  configuration_mapping:
    gmail_settings: "Map GmailProviderConfig.TimeoutSeconds to GoogleServicesProviderConfig.GmailTimeoutSeconds"
    contacts_settings: "Map ContactsProviderConfig.EnableContactsCaching to GoogleServicesProviderConfig.ContactsCachingEnabled"

  client_credentials:
    current_storage: "ProviderCredentialTypes.GoogleClientId/GoogleClientSecret"
    target_storage: "Same keys - no change needed for credential storage"
```

## Validation Gates

### Functional Validation
```bash
# Test Gmail functionality through GoogleServicesProvider
dotnet test --filter "Category=Integration&FullyQualifiedName~GmailEmailProvider"

# Test Contacts functionality through GoogleServicesProvider
dotnet test --filter "Category=Integration&FullyQualifiedName~ContactsProvider"

# Test OAuth flow with combined scopes
dotnet run --project src/TrashMailPanda # Verify single OAuth setup dialog
```

### Architecture Validation
```bash
# Verify provider registration
dotnet build # Check DI container configuration compiles

# Verify interface implementation
# GoogleServicesProvider should implement both IEmailProvider and IContactsProvider
# Internal calls should delegate to appropriate sub-providers

# Verify token sharing
# Both Gmail and Contacts operations should use same "google_" prefixed tokens
```

### Integration Validation
```bash
# Test startup orchestration
dotnet run --project src/TrashMailPanda
# Verify single Google Services provider initialization
# Confirm both email and contacts functionality available after OAuth

# Test provider health checks
# Verify health status reflects both Gmail and Contacts adapter states
# Confirm unified setup flow in provider status UI
```

## Final Validation Checklist

### OAuth Integration
- [ ] Single OAuth dialog requests combined Gmail + Contacts scopes
- [ ] All Google operations use shared "google_" token prefix
- [ ] No duplicate OAuth flows in UI
- [ ] Token refresh works for both Gmail and Contacts operations

### Interface Implementation
- [ ] GoogleServicesProvider implements IEmailProvider completely
- [ ] GoogleServicesProvider implements IContactsProvider completely
- [ ] Method delegation to sub-providers preserves all functionality
- [ ] Result<T> patterns maintained throughout

### UI Experience
- [ ] Single "Google Services" setup card replaces separate Gmail/Contacts cards
- [ ] OAuth setup dialog shows combined permissions clearly
- [ ] Provider status reflects both Gmail and Contacts health
- [ ] Setup completion enables both email and contacts features

### Code Architecture
- [ ] Internal provider isolation maintained for future extensibility
- [ ] No duplicate code between Gmail and Contacts providers
- [ ] Clean separation between OAuth management and API-specific logic
- [ ] StartupOrchestrator simplified with single Google provider initialization

### Configuration Management
- [ ] GoogleServicesProviderConfig consolidates all Google service settings
- [ ] Existing appsettings.json configurations migrate cleanly
- [ ] Service-specific settings (timeouts, caching) preserved
- [ ] OAuth credentials storage unchanged for backward compatibility

## Confidence Score: 9/10

**High confidence rationale**:
- Well-defined interfaces and existing OAuth service provide solid foundation
- Clear delegation pattern maintains existing functionality while unifying UX
- Comprehensive analysis of current architecture reveals specific integration points
- External research confirms Google OAuth best practices align with proposed approach
- Detailed task breakdown with specific files and methods to modify

**Risk mitigation**: Token migration strategy handles existing installations gracefully while maintaining backward compatibility.