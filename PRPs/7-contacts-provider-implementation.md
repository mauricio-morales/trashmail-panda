name: "ContactsProvider Implementation - Multi-Platform Trust Signal Provider"
description: |

---

## Goal

**Feature Goal**: Implement a provider-agnostic ContactsProvider that retrieves user contacts from Google People API and computes trust signals for email classification, with extensible architecture to support Apple Contacts and Windows People APIs in the future.

**Deliverable**: Complete ContactsProvider implementation with Google People API integration, OAuth scope expansion support, trust signal computation, contact caching, and comprehensive test coverage.

**Success Definition**: Email classification system can quickly determine if senders are "known" with relationship strength indicators, improving classification accuracy by leveraging contact data while maintaining sub-100ms lookup performance.

## User Persona

**Target User**: TrashMail Panda users who want improved email classification accuracy

**Use Case**: During email triage, the AI classification engine queries the ContactsProvider to determine if an email sender is a known contact and their relationship strength, leading to more accurate keep/delete decisions.

**User Journey**: 
1. User completes Gmail OAuth setup (existing)
2. System detects missing contacts scope and prompts for additional permissions  
3. User grants contacts access via OAuth scope expansion
4. ContactsProvider syncs contacts in background
5. During email classification, system instantly determines sender trust level
6. AI makes more informed classification decisions based on contact relationships

**Pain Points Addressed**: 
- False positives: Important emails from contacts marked as spam
- Manual overhead: Having to manually review emails from known senders
- Classification accuracy: Missing context about sender relationships

## Why

- **Enhanced Classification Accuracy**: Leverage contact data to improve AI email classification decisions by 25-40%
- **User Experience**: Reduce false positives for emails from known contacts, decreasing manual review time
- **Future Extensibility**: Establish foundation for multi-platform contact integration (Apple, Windows, Outlook)
- **Architecture Alignment**: Follow TrashMail Panda's provider pattern for consistency and maintainability
- **Privacy Compliance**: Implement contact data handling with GDPR compliance and local-first architecture

## What

### Core Functionality
- Fetch and cache user contacts from Google People API with incremental sync
- Compute trust signals: `isKnown(email)` and `relationshipStrength(email)` 
- OAuth scope expansion from existing Gmail tokens to include contacts permissions
- Multi-platform extensible architecture supporting future Apple/Windows contact sources
- High-performance contact lookup with 3-layer caching (memory + SQLite + remote)
- Contact data normalization and conflict resolution across multiple sources

### Success Criteria

- [ ] ContactsProvider passes all health checks and integrates with startup orchestrator
- [ ] Google People API integration fetches contacts with <30 second sync time for 1000+ contacts
- [ ] Contact lookups achieve <100ms response time for email classification use cases
- [ ] OAuth scope expansion works seamlessly from existing Gmail authentication
- [ ] Trust signal computation provides accurate relationship strength indicators
- [ ] Extensible architecture supports adding Apple/Windows contact adapters without code changes
- [ ] Comprehensive test coverage (>95%) with unit, integration, and security tests
- [ ] Contact caching reduces API calls by >90% for repeated lookups
- [ ] GDPR compliance with data retention policies and deletion capabilities

## All Needed Context

### Context Completeness Check

_This PRP provides complete implementation guidance including: existing provider patterns, OAuth integration approach, Google People API specifics, caching architecture, extensible design patterns, test strategies, and security requirements. An AI agent should have everything needed to implement this successfully._

### Documentation & References

```yaml
# MUST READ - Include these in your context window
- url: https://developers.google.com/people/
  why: Core Google People API documentation for contact retrieval and sync
  critical: Sync token lifecycle (7-day expiration), pagination patterns, rate limiting

- url: https://developers.google.com/people/api/rest/v1/people.connections/list
  why: Primary API endpoint for fetching user contacts with pagination and field selection
  critical: personFields parameter, syncToken usage, pageSize limits (1000 max)

- url: https://developers.google.com/identity/protocols/oauth2/scopes
  why: OAuth scopes required for contacts access and scope expansion patterns
  critical: https://www.googleapis.com/auth/contacts.readonly scope requirement

- file: src/Shared/TrashMailPanda.Shared/Base/IProvider.cs
  why: Core provider interface that ContactsProvider must implement - 443 lines of lifecycle management
  pattern: BaseProvider inheritance with health checks, state management, Result<T> pattern
  gotcha: NEVER throw exceptions - always use Result<T> pattern for async operations

- file: src/Providers/Email/TrashMailPanda.Providers.Email/GmailEmailProvider.cs
  why: Reference implementation for Google API integration and OAuth patterns
  pattern: Constructor DI, ExecuteOperationAsync wrapper, OAuth token management
  gotcha: Rate limiting integration, secure storage patterns, provider state transitions

- file: src/TrashMailPanda/TrashMailPanda/Services/ProviderBridgeService.cs
  why: OAuth state detection and health check integration patterns for scope expansion
  pattern: HasValidSession vs HasClientCredentials separation, authentication state management
  gotcha: Scope expansion detection must trigger "Authentication Required" state

- file: src/Shared/TrashMailPanda.Shared/Security/SecureStorageManager.cs
  why: OAuth token storage patterns using OS keychain integration
  pattern: Encrypted credential storage with automatic cleanup and audit logging
  gotcha: Use specific storage keys for contacts tokens separate from Gmail tokens

- file: src/Tests/TrashMailPanda.Tests/Providers/Email/GmailEmailProviderTests.cs
  why: Comprehensive provider testing patterns for unit tests
  pattern: Mock all dependencies, test constructor validation, provider lifecycle, Result<T> testing
  gotcha: Integration tests require Skip attributes and environment variable checks

- docfile: PRPs/ai_docs/google_people_api_integration.md
  why: Detailed Google People API integration patterns, caching strategies, and performance optimization
  section: Sync token management, contact normalization, trust signal computation
```

### Current Codebase tree (focused on provider architecture)

```bash
src/
├── TrashMailPanda/TrashMailPanda/          # Main Avalonia application
│   ├── Services/
│   │   ├── ProviderBridgeService.cs        # OAuth state detection for scope expansion
│   │   ├── StartupOrchestrator.cs          # Provider health check coordination
│   │   └── ServiceCollectionExtensions.cs # DI registration patterns
│   └── ViewModels/
│       ├── ProviderStatusDashboardViewModel.cs   # Provider dashboard with setup flows
│       └── ProviderStatusCardViewModel.cs         # Individual provider status UI
├── Shared/TrashMailPanda.Shared/           # Shared interfaces and utilities
│   ├── Base/
│   │   ├── IProvider.cs                    # Core provider interface (443 lines)
│   │   ├── BaseProvider.cs                # Abstract provider implementation
│   │   ├── Result.cs                       # Result<T> pattern implementation
│   │   └── ProviderError.cs               # Typed error handling
│   ├── Models/
│   │   ├── ProviderConfig.cs             # Base configuration patterns
│   │   └── HealthCheckResult.cs          # Health check result models
│   ├── Security/
│   │   ├── SecureStorageManager.cs       # OS keychain integration
│   │   └── CredentialEncryption.cs       # Token encryption patterns
│   └── IContactsProvider.cs              # Existing contacts interface (basic)
├── Providers/                             # Provider implementations
│   ├── Email/TrashMailPanda.Providers.Email/
│   │   ├── GmailEmailProvider.cs         # Reference Google API implementation
│   │   ├── GmailProviderConfig.cs        # OAuth configuration patterns
│   │   └── Services/GmailRateLimitHandler.cs  # Rate limiting patterns
│   ├── LLM/TrashMailPanda.Providers.LLM/ # LLM provider for comparison
│   └── Storage/TrashMailPanda.Providers.Storage/  # SQLite with encryption
└── Tests/TrashMailPanda.Tests/           # Test patterns
    ├── Providers/Email/
    │   ├── GmailEmailProviderTests.cs    # Unit test patterns
    │   └── GmailProviderConfigTests.cs  # Configuration validation tests
    └── Integration/Email/
        └── GmailApiIntegrationTests.cs  # Integration test patterns with Skip attributes
```

### Desired Codebase tree with files to be added

```bash
src/
├── Providers/
│   └── Contacts/TrashMailPanda.Providers.Contacts/
│       ├── TrashMailPanda.Providers.Contacts.csproj    # Project file with Google.Apis.PeopleService.v1
│       ├── ContactsProvider.cs                         # Main orchestrator provider
│       ├── ContactsProviderConfig.cs                   # Configuration with contacts OAuth scopes
│       ├── Models/
│       │   ├── Contact.cs                              # Unified contact model
│       │   ├── TrustSignal.cs                          # Trust signal computation results  
│       │   ├── ContactSourceAdapter.cs                # Platform adapter interface
│       │   └── SyncResult.cs                          # Sync operation results
│       ├── Adapters/
│       │   ├── GoogleContactsAdapter.cs               # Google People API adapter
│       │   ├── AppleContactsAdapter.cs                # Future: Apple Contacts (placeholder)
│       │   └── WindowsContactsAdapter.cs              # Future: Windows People (placeholder)
│       ├── Services/
│       │   ├── ContactsCacheManager.cs                # 3-layer caching implementation
│       │   ├── ContactsSyncOrchestrator.cs            # Background sync service
│       │   ├── TrustSignalCalculator.cs               # Relationship strength computation
│       │   ├── ContactsNormalizer.cs                  # Cross-platform data normalization
│       │   └── GooglePeopleRateLimitHandler.cs        # Rate limiting for People API
│       └── Constants/
│           ├── GooglePeopleApiConstants.cs            # API limits and endpoints
│           ├── ContactsStorageKeys.cs                 # Secure storage key definitions
│           └── ContactsErrorMessages.cs               # Standardized error messages
├── Shared/TrashMailPanda.Shared/
│   ├── IContactsProvider.cs                           # Updated interface with extensible methods
│   ├── Models/
│   │   ├── ContactSourceType.cs                       # Enumeration for contact sources
│   │   └── RelationshipStrength.cs                    # Trust level enumeration
│   └── Security/
│       └── IGoogleOAuthService.cs                     # Extracted OAuth service interface
└── Tests/TrashMailPanda.Tests/
    ├── Providers/Contacts/
    │   ├── ContactsProviderTests.cs                   # Unit tests for main provider
    │   ├── ContactsProviderConfigTests.cs             # Configuration validation tests
    │   ├── GoogleContactsAdapterTests.cs              # Adapter unit tests
    │   ├── TrustSignalCalculatorTests.cs              # Trust computation tests
    │   └── ContactsCacheManagerTests.cs               # Caching layer tests
    └── Integration/Contacts/
        ├── GooglePeopleApiIntegrationTests.cs         # Integration tests with Skip attributes
        └── ContactsSyncIntegrationTests.cs            # End-to-end sync testing
```

### Known Gotchas of our codebase & Library Quirks

```csharp
// CRITICAL: TrashMail Panda NEVER throws exceptions - always use Result<T> pattern
// All provider async methods must return Result<T> instead of throwing

// CRITICAL: Google.Apis.PeopleService.v1 requires specific NuGet package version
// Use Google.Apis.PeopleService.v1 Version="1.69.0+" for .NET 9 compatibility

// CRITICAL: OAuth scope expansion requires careful token management
// Don't invalidate existing Gmail tokens when adding contacts scope

// CRITICAL: SQLCipher database requires specific connection string format
// Use Password parameter in SqliteConnectionStringBuilder for encryption

// CRITICAL: Provider registration in DI follows deferred pattern
// ContactsProvider is NOT registered in ServiceCollectionExtensions.AddProviders()
// It's created by application services after OAuth flow completion (like Gmail/LLM providers)

// CRITICAL: Google People API sync tokens expire in 7 days
// Always handle HTTP 410 Gone errors gracefully with full resync

// CRITICAL: People API rate limits are per-user quotas, not per-app
// Implement exponential backoff for 429 errors with jitter

// CRITICAL: Contact emails MUST be normalized to lowercase for consistent lookups
// Use contact.PrimaryEmail?.ToLowerInvariant() for all cache keys

// CRITICAL: BaseProvider<TConfig> ExecuteOperationAsync() wrapper is required
// All public provider operations must use this wrapper for consistent error handling

// CRITICAL: Provider health checks run during startup orchestration
// OAuth scope expansion should be detected in health checks and show provider dashboard

// CRITICAL: Integration tests require Skip attributes by default
// Use [Fact(Skip = "Requires real Google People API credentials")] pattern
```

## Implementation Blueprint

### Data models and structure

Create the core data models to ensure type safety and cross-platform consistency.

```csharp
// Location: src/Providers/Contacts/TrashMailPanda.Providers.Contacts/Models/Contact.cs
public class Contact
{
    public string Id { get; set; } = string.Empty;               // TrashMail Panda unique ID
    public string PrimaryEmail { get; set; } = string.Empty;     // Primary email (normalized lowercase)
    public List<string> AllEmails { get; set; } = new();        // All email addresses
    public string DisplayName { get; set; } = string.Empty;     // Full display name
    public string? GivenName { get; set; }                      // First name
    public string? FamilyName { get; set; }                     // Last name
    public List<string> PhoneNumbers { get; set; } = new();     // Normalized E.164 format
    public string? OrganizationName { get; set; }               // Company/organization
    public string? OrganizationTitle { get; set; }              // Job title
    public string? PhotoUrl { get; set; }                       // Profile photo URL
    public List<SourceIdentity> SourceIdentities { get; set; } = new();  // Multi-source tracking
    public DateTime LastModifiedUtc { get; set; }               // Last modified timestamp
    public DateTime LastSyncedUtc { get; set; }                 // Last sync from source
    public double RelationshipStrength { get; set; }            // Computed trust score 0.0-1.0
}

// Location: src/Providers/Contacts/TrashMailPanda.Providers.Contacts/Models/TrustSignal.cs
public class TrustSignal
{
    public string ContactId { get; set; } = string.Empty;
    public RelationshipStrength Strength { get; set; }
    public double Score { get; set; }                           // 0.0-1.0 numeric score
    public DateTime LastInteractionDate { get; set; }
    public List<string> Justification { get; set; } = new();   // Why this trust level
    public DateTime ComputedAt { get; set; } = DateTime.UtcNow;
}

// Location: src/Shared/TrashMailPanda.Shared/Models/RelationshipStrength.cs
public enum RelationshipStrength
{
    None = 0,     // Unknown contact
    Weak = 1,     // In contacts but limited interaction
    Moderate = 2, // Regular interaction
    Strong = 3,   // Frequent interaction
    Trusted = 4   // High-trust contact (family, work, etc.)
}

// Location: src/Providers/Contacts/TrashMailPanda.Providers.Contacts/ContactsProviderConfig.cs
public sealed class ContactsProviderConfig : BaseProviderConfig
{
    public new string Name { get; set; } = "Contacts";
    public new List<string> Tags { get; set; } = new() { "contacts", "google", "people", "trust" };
    
    [Required(ErrorMessage = "Google People API Client ID is required")]
    [StringLength(200, MinimumLength = 10)]
    public string ClientId { get; set; } = string.Empty;
    
    [Required(ErrorMessage = "Google People API Client Secret is required")]  
    [StringLength(200, MinimumLength = 10)]
    public string ClientSecret { get; set; } = string.Empty;
    
    public string ApplicationName { get; set; } = "TrashMail Panda";
    
    public string[] Scopes { get; set; } = { 
        "https://www.googleapis.com/auth/contacts.readonly",
        "https://www.googleapis.com/auth/userinfo.profile"
    };
    
    [Range(1, 10, ErrorMessage = "Max retries must be between 1 and 10")]
    public int MaxRetries { get; set; } = 5;
    
    public TimeSpan RequestTimeout { get; set; } = TimeSpan.FromMinutes(1);
    public TimeSpan BaseRetryDelay { get; set; } = TimeSpan.FromSeconds(1);
    public TimeSpan MaxRetryDelay { get; set; } = TimeSpan.FromMinutes(1);
    
    [Range(1, 2000, ErrorMessage = "Page size must be between 1 and 2000")]
    public int DefaultPageSize { get; set; } = 1000;
    
    // Cache settings
    public TimeSpan ContactsCacheExpiry { get; set; } = TimeSpan.FromHours(6);
    public bool EnableContactsCaching { get; set; } = true;
}
```

### Implementation Tasks (ordered by dependencies)

```yaml
Task 1: CREATE src/Providers/Contacts/TrashMailPanda.Providers.Contacts/TrashMailPanda.Providers.Contacts.csproj
  - IMPLEMENT: .NET 9 project file with required NuGet packages
  - PACKAGES: Google.Apis.PeopleService.v1 (1.69.0+), Microsoft.Extensions.Caching.Memory, libphonenumber-csharp
  - REFERENCES: TrashMailPanda.Shared project reference
  - FOLLOW pattern: src/Providers/Email/TrashMailPanda.Providers.Email/TrashMailPanda.Providers.Email.csproj
  - PLACEMENT: New provider project in src/Providers/Contacts/

Task 2: CREATE src/Providers/Contacts/TrashMailPanda.Providers.Contacts/Models/
  - IMPLEMENT: Contact.cs, TrustSignal.cs, SourceIdentity.cs, SyncResult.cs, ContactSourceType.cs
  - FOLLOW pattern: src/Shared/TrashMailPanda.Shared/Models/ (data model structure, nullable annotations)
  - NAMING: PascalCase classes, camelCase properties, nullable reference types
  - VALIDATION: Use DataAnnotations for required fields and ranges
  - PLACEMENT: Models in provider-specific Models folder

Task 3: CREATE src/Providers/Contacts/TrashMailPanda.Providers.Contacts/ContactsProviderConfig.cs
  - IMPLEMENT: Configuration class inheriting from BaseProviderConfig with contacts-specific settings
  - FOLLOW pattern: src/Providers/Email/TrashMailPanda.Providers.Email/GmailProviderConfig.cs (DataAnnotations, validation, factory methods)
  - VALIDATION: OAuth scopes, retry settings, page size limits, timeout constraints
  - METHODS: ValidateCustomLogic(), GetSanitizedCopy(), CreateDevelopmentConfig(), CreateProductionConfig()
  - DEPENDENCIES: Import BaseProviderConfig from Shared
  - PLACEMENT: Root of contacts provider project

Task 4: EXTRACT src/Shared/TrashMailPanda.Shared/Security/IGoogleOAuthService.cs and GoogleOAuthService.cs
  - EXTRACT: OAuth logic from GmailEmailProvider into shared service
  - IMPLEMENT: GetAccessTokenAsync(scopes), RevokeTokensAsync(), scope expansion detection
  - FOLLOW pattern: src/TrashMailPanda/TrashMailPanda/Services/GmailOAuthService.cs (existing OAuth implementation)
  - INTEGRATION: Use existing ISecureStorageManager and ISecurityAuditLogger
  - SCOPE_HANDLING: Support contacts scope expansion from existing Gmail tokens
  - PLACEMENT: Shared security services for reuse across providers

Task 5: CREATE src/Providers/Contacts/TrashMailPanda.Providers.Contacts/Adapters/GoogleContactsAdapter.cs
  - IMPLEMENT: IContactSourceAdapter implementation for Google People API
  - FOLLOW pattern: Google People API documentation patterns (sync tokens, pagination, rate limiting)
  - METHODS: FetchContactsAsync(syncToken), IsEnabled property, SourceType property
  - DEPENDENCIES: Use IGoogleOAuthService from Task 4, Google.Apis.PeopleService.v1
  - RATE_LIMITING: Implement exponential backoff for 429 errors, handle 410 Gone for expired sync tokens
  - PLACEMENT: Adapters subfolder for platform-specific implementations

Task 6: CREATE src/Providers/Contacts/TrashMailPanda.Providers.Contacts/Services/ContactsCacheManager.cs
  - IMPLEMENT: 3-layer cache (L1 memory, L2 SQLite, L3 remote) with contact-specific optimizations
  - FOLLOW pattern: Provider caching research patterns (memory cache + SQLite storage)
  - METHODS: GetContactAsync(), SetContactAsync(), InvalidateContactAsync(), WarmCacheAsync()
  - DEPENDENCIES: IMemoryCache, IStorageProvider, semaphore for thread safety
  - PERFORMANCE: Sub-100ms lookup times, cache warming for frequent contacts
  - PLACEMENT: Services subfolder for caching infrastructure

Task 7: CREATE src/Providers/Contacts/TrashMailPanda.Providers.Contacts/Services/TrustSignalCalculator.cs
  - IMPLEMENT: Trust signal computation service with configurable scoring rules
  - CALCULATE: Relationship strength based on contact completeness, source trust, interaction frequency
  - METHODS: CalculateRelationshipStrength(), GetTrustSignal(), UpdateTrustScore()
  - FOLLOW pattern: Scoring algorithm from research (source weight + completeness + interaction)
  - CACHING: Cache computed trust signals to avoid recalculation
  - PLACEMENT: Services subfolder for business logic

Task 8: CREATE src/Providers/Contacts/TrashMailPanda.Providers.Contacts/ContactsProvider.cs
  - IMPLEMENT: Main provider class inheriting BaseProvider<ContactsProviderConfig>
  - IMPLEMENT_INTERFACE: IContactsProvider with GetContactByEmailAsync(), GetRelationshipStrengthAsync()
  - FOLLOW pattern: src/Providers/Email/TrashMailPanda.Providers.Email/GmailEmailProvider.cs (provider lifecycle, health checks)
  - ORCHESTRATION: Coordinate multiple contact source adapters, handle caching, manage sync
  - DEPENDENCIES: All services from previous tasks, dependency injection pattern
  - PLACEMENT: Root of contacts provider project

Task 9: UPDATE src/Shared/TrashMailPanda.Shared/IContactsProvider.cs
  - MODIFY: Extend existing interface to support new extensible methods
  - ADD_METHODS: GetContactByEmailAsync(), GetAllContactsAsync(), GetTrustSignalAsync()
  - FOLLOW pattern: src/Shared/TrashMailPanda.Shared/IEmailProvider.cs (interface structure)
  - INHERITANCE: Ensure implements IProvider<ContactsProviderConfig>
  - BACKWARD_COMPATIBILITY: Preserve existing methods if any
  - PLACEMENT: Update existing shared interface

Task 10: MODIFY src/TrashMailPanda/TrashMailPanda/Services/ProviderBridgeService.cs
  - ADD: GetContactsProviderStatusAsync() method for provider dashboard integration
  - IMPLEMENT: OAuth scope expansion detection for contacts permissions
  - FOLLOW pattern: Existing GetEmailProviderStatusAsync() method (OAuth state detection)
  - SCOPE_VALIDATION: Detect missing contacts scope and trigger "Authentication Required" state
  - HEALTH_INTEGRATION: Support provider dashboard setup flows
  - PLACEMENT: Update existing provider bridge service

Task 11: CREATE comprehensive unit tests
  - IMPLEMENT: ContactsProviderTests.cs, ContactsProviderConfigTests.cs, GoogleContactsAdapterTests.cs
  - FOLLOW pattern: src/Tests/TrashMailPanda.Tests/Providers/Email/GmailEmailProviderTests.cs (Mock usage, dependency injection testing)
  - COVERAGE: Constructor validation, configuration validation, provider lifecycle, Result<T> compliance
  - MOCKING: Use Moq for all dependencies, test isolation
  - ASSERTIONS: Test success/failure scenarios, error types, state transitions
  - PLACEMENT: Tests/Providers/Contacts/ matching provider structure

Task 12: CREATE integration tests with Skip attributes
  - IMPLEMENT: GooglePeopleApiIntegrationTests.cs, ContactsSyncIntegrationTests.cs
  - FOLLOW pattern: src/Tests/TrashMailPanda.Tests/Integration/Email/GmailApiIntegrationTests.cs
  - SKIP_ATTRIBUTES: [Fact(Skip = "Requires real Google People API credentials")] for tests needing real OAuth
  - ENVIRONMENT_VARIABLES: Support GOOGLE_PEOPLE_CLIENT_ID and GOOGLE_PEOPLE_CLIENT_SECRET for local testing
  - REAL_API_TESTING: Test actual People API integration when credentials available
  - PLACEMENT: Tests/Integration/Contacts/ for integration test organization
```

### Implementation Patterns & Key Details

```csharp
// Primary provider orchestrator pattern
public class ContactsProvider : BaseProvider<ContactsProviderConfig>, IContactsProvider
{
    private readonly IEnumerable<IContactSourceAdapter> _sourceAdapters;
    private readonly ContactsCacheManager _cacheManager;
    private readonly TrustSignalCalculator _trustCalculator;
    
    // PATTERN: Constructor dependency injection with null validation
    public ContactsProvider(
        IEnumerable<IContactSourceAdapter> sourceAdapters,
        ContactsCacheManager cacheManager,
        TrustSignalCalculator trustCalculator,
        ILogger<ContactsProvider> logger)
        : base(logger)
    {
        _sourceAdapters = sourceAdapters?.Where(a => a.IsEnabled) ?? throw new ArgumentNullException(nameof(sourceAdapters));
        _cacheManager = cacheManager ?? throw new ArgumentNullException(nameof(cacheManager));
        _trustCalculator = trustCalculator ?? throw new ArgumentNullException(nameof(trustCalculator));
    }

    // PATTERN: All operations use ExecuteOperationAsync wrapper for consistent error handling
    public async Task<Result<Contact>> GetContactByEmailAsync(string emailAddress)
    {
        return await ExecuteOperationAsync("GetContactByEmail", async (cancellationToken) =>
        {
            if (string.IsNullOrWhiteSpace(emailAddress))
                return Result<Contact>.Failure(new ValidationError("Email address cannot be empty"));

            var normalizedEmail = emailAddress.ToLowerInvariant();
            
            // L1/L2 Cache lookup first - critical for performance
            var cachedResult = await _cacheManager.GetContactAsync(normalizedEmail);
            if (cachedResult.IsSuccess)
                return cachedResult;

            // L3 Remote fetch if cache miss
            var remoteResult = await FetchFromSourcesAsync(normalizedEmail);
            if (remoteResult.IsSuccess)
            {
                await _cacheManager.SetContactAsync(normalizedEmail, remoteResult.Value);
            }
            
            return remoteResult;
        });
    }

    // CRITICAL: Override BaseProvider abstract methods
    protected override async Task<Result<bool>> PerformInitializationAsync(ContactsProviderConfig config, CancellationToken cancellationToken)
    {
        // Initialize all enabled source adapters
        // Set up background sync orchestrator
        // Warm cache with high-priority contacts
        return Result.Success(true);
    }

    protected override async Task<Result<HealthCheckResult>> PerformHealthCheckAsync(CancellationToken cancellationToken)
    {
        // Test OAuth scope availability (contacts.readonly)
        // Validate source adapter connectivity
        // Check cache performance
        // Return detailed diagnostics
    }
}

// Google People API adapter implementation pattern
public class GoogleContactsAdapter : IContactSourceAdapter
{
    private readonly IGoogleOAuthService _oauthService;
    private readonly IRateLimitHandler _rateLimitHandler;
    private readonly string[] _requiredScopes = { "https://www.googleapis.com/auth/contacts.readonly" };
    
    // PATTERN: OAuth integration with scope validation
    public async Task<Result<(IEnumerable<Contact> Contacts, string NextSyncToken)>> FetchContactsAsync(string? syncToken = null)
    {
        // Get access token with required scopes
        var tokenResult = await _oauthService.GetAccessTokenAsync(_requiredScopes, CancellationToken.None);
        if (tokenResult.IsFailure)
            return Result<(IEnumerable<Contact>, string)>.Failure(tokenResult.Error);

        // Initialize People API service
        var service = new PeopleServiceService(new BaseClientService.Initializer()
        {
            HttpClientInitializer = CreateCredential(tokenResult.Value),
            ApplicationName = "TrashMail Panda"
        });

        // CRITICAL: Handle sync token expiration (HTTP 410 Gone)
        try
        {
            var request = service.People.Connections.List("people/me");
            request.PersonFields = "names,emailAddresses,phoneNumbers,organizations,photos";
            request.PageSize = 1000;
            
            if (!string.IsNullOrEmpty(syncToken))
                request.SyncToken = syncToken;
            else
                request.RequestSyncToken = true;

            var response = await request.ExecuteAsync();
            var contacts = MapPeopleToContacts(response.Connections);
            
            return Result.Success((contacts, response.NextSyncToken ?? string.Empty));
        }
        catch (GoogleApiException ex) when (ex.HttpStatusCode == HttpStatusCode.Gone)
        {
            // Sync token expired - perform full sync
            return await FetchContactsAsync(syncToken: null);
        }
    }

    // CRITICAL: Contact data normalization for consistent cross-platform lookup
    private Contact MapPersonToContact(Person person)
    {
        var contact = new Contact
        {
            Id = GenerateContactId(person),
            SourceIdentities = new List<SourceIdentity>
            {
                new() { SourceType = ContactSourceType.Google, SourceContactId = person.ResourceName }
            }
        };

        // Normalize email addresses to lowercase
        if (person.EmailAddresses?.Any() == true)
        {
            contact.AllEmails = person.EmailAddresses.Select(e => e.Value.ToLowerInvariant()).ToList();
            contact.PrimaryEmail = contact.AllEmails.First();
        }

        // Normalize phone numbers to E.164 format
        if (person.PhoneNumbers?.Any() == true)
        {
            contact.PhoneNumbers = person.PhoneNumbers
                .Select(p => NormalizePhoneNumber(p.Value))
                .Where(p => !string.IsNullOrEmpty(p))
                .ToList();
        }

        return contact;
    }
}

// Trust signal calculation pattern
public class TrustSignalCalculator
{
    public TrustSignal CalculateTrustSignal(Contact contact)
    {
        var score = 0.0;
        var justifications = new List<string>();

        // Source trust weighting
        if (contact.SourceIdentities.Any(s => s.SourceType == ContactSourceType.Google))
        {
            score += 0.5; // 50% base trust for being in Google Contacts
            justifications.Add("Found in Google Contacts");
        }

        // Contact completeness scoring
        if (!string.IsNullOrEmpty(contact.OrganizationName))
        {
            score += 0.2;
            justifications.Add("Has organization information");
        }

        if (contact.PhoneNumbers.Any())
        {
            score += 0.15;
            justifications.Add("Has phone number");
        }

        // Interaction frequency (future: integrate with email provider)
        // TODO: Query email provider for interaction history

        var strength = score switch
        {
            >= 0.8 => RelationshipStrength.Trusted,
            >= 0.6 => RelationshipStrength.Strong,
            >= 0.3 => RelationshipStrength.Moderate,
            > 0.0 => RelationshipStrength.Weak,
            _ => RelationshipStrength.None
        };

        return new TrustSignal
        {
            ContactId = contact.Id,
            Strength = strength,
            Score = score,
            Justification = justifications,
            ComputedAt = DateTime.UtcNow
        };
    }
}
```

### Integration Points

```yaml
OAUTH_SERVICE:
  - extract from: src/Providers/Email/TrashMailPanda.Providers.Email/GmailEmailProvider.cs
  - create: src/Shared/TrashMailPanda.Shared/Security/IGoogleOAuthService.cs
  - pattern: "Shared OAuth service supporting scope expansion"

PROVIDER_REGISTRATION:
  - modify: src/TrashMailPanda/TrashMailPanda/Services/ServiceCollectionExtensions.cs
  - pattern: "NOT registered in AddProviders() - follows Gmail/LLM deferred registration pattern"
  - note: "Created by application services after OAuth flow completion"

HEALTH_CHECKS:
  - modify: src/TrashMailPanda/TrashMailPanda/Services/ProviderBridgeService.cs
  - add: "GetContactsProviderStatusAsync() method"
  - pattern: "OAuth scope expansion detection similar to GetEmailProviderStatusAsync()"

PROVIDER_DASHBOARD:
  - modify: src/TrashMailPanda/TrashMailPanda/ViewModels/ProviderStatusDashboardViewModel.cs
  - add: "Contacts provider card with OAuth scope expansion UI flow"
  - pattern: "Follow existing provider setup patterns"

DATABASE_SCHEMA:
  - add to: SQLite database via IStorageProvider
  - tables: "contacts, contact_sources, trust_signals, sync_tokens"
  - indexes: "PRIMARY KEY(email), INDEX(relationship_strength), INDEX(last_interaction)"
```

## Validation Loop

### Level 1: Syntax & Style (Immediate Feedback)

```bash
# Run after each file creation - fix before proceeding
dotnet format src/Providers/Contacts/ --verify-no-changes
dotnet build src/Providers/Contacts/TrashMailPanda.Providers.Contacts/
dotnet build # Full solution build

# Expected: Zero errors. Solution builds successfully with all dependencies resolved.
```

### Level 2: Unit Tests (Component Validation)

```bash
# Test each component as it's created
dotnet test src/Tests/TrashMailPanda.Tests/Providers/Contacts/ContactsProviderTests.cs --logger console
dotnet test src/Tests/TrashMailPanda.Tests/Providers/Contacts/ContactsProviderConfigTests.cs --logger console
dotnet test src/Tests/TrashMailPanda.Tests/Providers/Contacts/GoogleContactsAdapterTests.cs --logger console

# Full test suite for contacts provider
dotnet test src/Tests/TrashMailPanda.Tests/Providers/Contacts/ --logger console --verbosity normal

# Coverage validation
dotnet test --collect:"XPlat Code Coverage" --logger console

# Expected: All tests pass. Coverage >95%. Mock dependencies validated.
```

### Level 3: Integration Testing (System Validation)

```bash
# Build and run application for integration testing
dotnet build --configuration Debug
dotnet run --project src/TrashMailPanda

# Provider health check validation during startup
# Should show contacts provider status in provider dashboard
# OAuth scope expansion should be detected if contacts scope missing

# Integration tests with real API (requires environment variables)
export GOOGLE_PEOPLE_CLIENT_ID="your_actual_client_id"
export GOOGLE_PEOPLE_CLIENT_SECRET="your_actual_client_secret"

# Remove Skip attributes and run integration tests
dotnet test src/Tests/TrashMailPanda.Tests/Integration/Contacts/ --logger console

# Provider orchestration testing
dotnet test --filter "Category=Integration" --logger console

# Expected: Provider integrates with startup orchestrator, health checks work, OAuth scope expansion detected
```

### Level 4: Performance & Security Validation

```bash
# Contact lookup performance testing
# Target: <100ms response time for cached lookups
dotnet run --project src/TrashMailPanda --configuration Release

# Security validation
dotnet test --filter "Category=Security" --logger console

# OAuth token security validation
# Ensure contacts tokens stored separately from Gmail tokens
# Verify token encryption and audit logging

# Memory leak testing for cache manager
# Long-running contact sync operations

# Rate limiting validation
# Test Google People API rate limit handling with 429 responses

# Expected: Performance targets met, security validation passes, no memory leaks
```

## Final Validation Checklist

### Technical Validation

- [ ] All 4 validation levels completed successfully
- [ ] All tests pass: `dotnet test src/Tests/TrashMailPanda.Tests/`
- [ ] No build errors: `dotnet build --configuration Release`
- [ ] No formatting issues: `dotnet format --verify-no-changes`
- [ ] NuGet packages resolve: Google.Apis.PeopleService.v1, libphonenumber-csharp

### Feature Validation

- [ ] ContactsProvider integrates with startup orchestrator and provider dashboard
- [ ] OAuth scope expansion detected and handled through provider bridge service
- [ ] Google People API integration works with sync tokens and pagination
- [ ] Contact caching achieves <100ms lookup performance
- [ ] Trust signal computation provides accurate relationship strength
- [ ] Extensible architecture supports adding platform adapters
- [ ] Integration tests work with real Google People API credentials

### Code Quality Validation

- [ ] Follows TrashMail Panda provider patterns (Result<T>, BaseProvider inheritance)
- [ ] File placement matches desired codebase tree structure
- [ ] OAuth integration follows existing Gmail provider patterns
- [ ] Contact data normalization ensures consistent cross-platform lookup
- [ ] Security patterns maintain OS keychain integration and audit logging
- [ ] Test coverage >95% with proper Mock usage and Skip attributes

### Architecture Validation

- [ ] Multi-platform adapter pattern enables future Apple/Windows support
- [ ] ContactsProvider orchestrates multiple source adapters correctly
- [ ] OAuth service extraction enables reuse across Gmail and Contacts providers
- [ ] Caching architecture provides performance with privacy compliance
- [ ] Provider registration follows deferred pattern (not in AddProviders)
- [ ] Health checks integrate with scope expansion detection

---

## Anti-Patterns to Avoid

- ❌ Don't throw exceptions - always use Result<T> pattern
- ❌ Don't hardcode Google-specific logic in ContactsProvider - use adapters  
- ❌ Don't invalidate Gmail tokens when adding contacts scope
- ❌ Don't store contact emails without lowercase normalization
- ❌ Don't skip ExecuteOperationAsync wrapper in provider operations
- ❌ Don't register ContactsProvider in ServiceCollectionExtensions.AddProviders()
- ❌ Don't ignore sync token expiration (HTTP 410 Gone errors)
- ❌ Don't cache sensitive contact data without encryption
- ❌ Don't implement contact sync without rate limiting
- ❌ Don't create integration tests without Skip attributes for missing credentials