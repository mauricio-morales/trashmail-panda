name: "Complete GoogleServicesProvider Integration - Fix Missing DI Registration and Unified UI"
description: |

---

## Goal

**Feature Goal**: Complete the integration of GoogleServicesProvider as the primary implementation for both IEmailProvider and IContactsProvider interfaces, replacing separate Gmail and Contacts provider registrations with a unified provider architecture.

**Deliverable**: Fully integrated GoogleServicesProvider that serves as the single implementation for both email and contacts interfaces, with unified UI showing a single "Google Services" provider card and seamless OAuth setup flow.

**Success Definition**: Users see a single "Google Services" provider card instead of separate Gmail/Contacts cards, complete OAuth setup once for both services, and automatic UI refresh shows both services connected without manual intervention.

## User Persona

**Target User**: TrashMail Panda application users who need to authenticate their Google account to access both Gmail and Contacts services.

**Use Case**: User opens the application, sees provider status dashboard, clicks setup on "Google Services" card, completes single OAuth flow, and immediately sees both Gmail and Contacts services connected and operational.

**User Journey**:
1. User opens application and sees "Google Services" provider card showing "Setup Required"
2. User clicks "Setup" button on Google Services card
3. Single OAuth dialog opens explaining access to Gmail and Contacts
4. User completes OAuth flow in browser
5. Dialog automatically closes and provider card immediately updates to show "Connected"
6. Both Gmail and Contacts functionality is available without additional setup

**Pain Points Addressed**:
- Eliminates confusion from separate Gmail and Contacts provider cards
- Removes duplicate OAuth flows for related Google services
- Prevents manual refresh requirements after authentication
- Provides clear understanding of unified Google services access

## Why

- **Business Value**: Simplified user onboarding reduces support requests and improves user experience by eliminating redundant authentication flows
- **Integration with Existing Features**: GoogleServicesProvider is already implemented but not integrated as the primary provider path
- **Problems This Solves**:
  - Fixes architectural inconsistency where unified provider exists but isn't used
  - Eliminates user confusion from separate Google service provider cards
  - Resolves duplicate OAuth setup flows for the same underlying Google account
  - Addresses missing automatic UI refresh after authentication completion

## What

Transform the current separate Gmail and Contacts provider architecture into a unified Google Services provider system with single authentication and unified UI representation.

### Success Criteria

- [ ] Single "Google Services" provider card replaces separate Gmail and Contacts cards in UI
- [ ] GoogleServicesProvider is registered as the implementation for both IEmailProvider and IContactsProvider interfaces
- [ ] StartupOrchestrator uses unified GoogleServicesProvider initialization instead of separate provider initialization
- [ ] OAuth setup dialog shows unified "Google Services" messaging instead of Gmail-specific messaging
- [ ] Single OAuth flow grants access to both Gmail and Contacts APIs
- [ ] Automatic UI refresh after OAuth completion shows both services connected
- [ ] All existing Gmail and Contacts functionality works through unified provider
- [ ] Health checks aggregate status from both sub-providers appropriately

**Deferred Setup & OAuth State Management:**
- [ ] GoogleServicesProvider instantiates successfully even when no OAuth tokens are available
- [ ] Provider operations gracefully handle all OAuth states: NotConfigured, ConfiguredNoAuth, AuthExpired, AuthValid, AuthPartial, TokenRefreshing
- [ ] UI displays appropriate status and actions for each OAuth state with clear user guidance
- [ ] Token refresh happens automatically when tokens expire, with fallback to re-authentication if refresh fails
- [ ] Partial authentication scenarios are handled gracefully with clear user feedback
- [ ] Provider operations start working immediately once valid tokens become available (deferred activation)
- [ ] Setup button text and visibility adapts to current OAuth state ("Configure", "Authenticate", "Re-authenticate", "Complete Setup")
- [ ] Health status reflects granular OAuth states instead of generic "healthy/unhealthy"

## All Needed Context

### Context Completeness Check

_"If someone knew nothing about this codebase, would they have everything needed to implement this successfully?"_ - YES, this PRP provides specific file locations, exact patterns to follow, concrete implementation examples, and comprehensive validation steps.

### Documentation & References

```yaml
# MUST READ - Critical external patterns
- url: https://learn.microsoft.com/en-us/dotnet/core/extensions/dependency-injection
  why: Multi-interface registration patterns using factory method approach
  critical: Use factory pattern to register single class against multiple interfaces

- url: https://andrewlock.net/how-to-register-a-service-with-multiple-interfaces-for-in-asp-net-core-di/
  why: Specific implementation of multi-interface DI registration
  critical: Forward interface registrations to shared singleton instance

- url: https://docs.avaloniaui.net/docs/guides/data-binding/inotifypropertychanged
  why: Avalonia MVVM patterns for real-time UI updates
  critical: Use CommunityToolkit.Mvvm ObservableProperty for automatic UI refresh

- url: https://learn.microsoft.com/en-us/dotnet/communitytoolkit/mvvm/generators/observableproperty
  why: CommunityToolkit.Mvvm patterns for property change notification
  critical: Use NotifyCanExecuteChangedFor for command state updates

# CRITICAL IMPLEMENTATION PATTERNS
- file: src/TrashMailPanda/TrashMailPanda/Services/ServiceCollectionExtensions.cs
  why: DI registration patterns and provider setup
  pattern: Factory method registration for multi-interface providers
  gotcha: Must register concrete type first, then forward interfaces to shared instance

- file: src/Providers/GoogleServices/TrashMailPanda.Providers.GoogleServices/GoogleServicesProvider.cs
  why: Complete implementation of unified provider with delegation pattern
  pattern: Delegation to sub-providers with feature flag checks
  gotcha: Configuration validation must check enabled services before delegation

- file: src/TrashMailPanda/TrashMailPanda/Services/StartupOrchestrator.cs
  why: Provider initialization and health check patterns
  pattern: Sequential startup with cancellation support and health aggregation
  gotcha: Unified provider replaces individual provider initialization methods

- file: src/TrashMailPanda/TrashMailPanda/ViewModels/ProviderStatusDashboardViewModel.cs
  why: MVVM patterns for provider status card collection
  pattern: ObservableCollection updates with PropertyChanged notifications
  gotcha: Provider card filtering logic must recognize unified provider type

- file: src/TrashMailPanda/TrashMailPanda/Views/GoogleOAuthSetupDialog.axaml
  why: OAuth dialog UI and messaging patterns
  pattern: Unified messaging for multiple service access
  gotcha: Scope presentation must explain both Gmail and Contacts access
```

### Current Codebase Tree

```bash
src/
├── Providers/
│   ├── GoogleServices/                    # ✅ GoogleServicesProvider implementation exists
│   │   └── TrashMailPanda.Providers.GoogleServices/
│   │       ├── GoogleServicesProvider.cs  # ✅ Complete implementation ready
│   │       └── GoogleServicesProviderConfig.cs
│   ├── Email/                             # Individual providers (will become internal)
│   └── Contacts/                          # Individual providers (will become internal)
└── TrashMailPanda/
    └── TrashMailPanda/
        ├── Services/
        │   ├── ServiceCollectionExtensions.cs  # ❌ NEEDS DI registration fix
        │   └── StartupOrchestrator.cs          # ❌ NEEDS unified initialization
        ├── ViewModels/
        │   ├── ProviderStatusDashboardViewModel.cs  # ❌ NEEDS unified card logic
        │   └── MainWindowViewModel.cs              # ❌ NEEDS unified OAuth handler
        └── Views/
            ├── Controls/ProviderStatusCard.axaml    # ❌ NEEDS Google Services logo
            └── GoogleOAuthSetupDialog.axaml         # ❌ NEEDS unified messaging
```

### Desired Codebase Tree

```bash
# No new files needed - all changes are modifications to existing files
src/TrashMailPanda/TrashMailPanda/Services/ServiceCollectionExtensions.cs  # ✅ Fixed DI registration
src/TrashMailPanda/TrashMailPanda/Services/StartupOrchestrator.cs          # ✅ Unified initialization
src/TrashMailPanda/TrashMailPanda/ViewModels/ProviderStatusDashboardViewModel.cs  # ✅ Single Google Services card
src/TrashMailPanda/TrashMailPanda/ViewModels/MainWindowViewModel.cs        # ✅ Unified OAuth handler
src/TrashMailPanda/TrashMailPanda/Views/Controls/ProviderStatusCard.axaml  # ✅ Google Services branding
src/TrashMailPanda/TrashMailPanda/Views/GoogleOAuthSetupDialog.axaml       # ✅ Unified messaging
```

### Known Gotchas & Library Quirks

```csharp
// CRITICAL: Current DI registration creates CIRCULAR DEPENDENCY
// GoogleServicesProvider constructor requires IEmailProvider/IContactsProvider
// But those interfaces point back to GoogleServicesProvider!
services.AddSingleton<IEmailProvider, GmailEmailProvider>();
services.AddSingleton<IContactsProvider, ContactsProvider>();
services.AddSingleton<GoogleServicesProvider>(provider =>
{
    var gmailProvider = provider.GetRequiredService<IEmailProvider>() as GmailEmailProvider; // CIRCULAR!
    // ...
});

// CORRECT: Create dependencies DIRECTLY without interface resolution
services.AddSingleton<GoogleServicesProvider>(provider =>
{
    // Create sub-providers directly with their dependencies
    var gmailProvider = new GmailEmailProvider(/* direct dependencies */);
    var contactsProvider = new ContactsProvider(/* direct dependencies */);
    // ... other dependencies
    return new GoogleServicesProvider(gmailProvider, contactsProvider, ...);
});
// Then forward interfaces to unified provider
services.AddSingleton<IEmailProvider>(provider => provider.GetRequiredService<GoogleServicesProvider>());

// CRITICAL: OAuth State Management - GoogleServicesProvider MUST support deferred setup
// Provider must instantiate successfully even when NO tokens are available
// Once tokens become available, operations should dynamically start working
public enum GoogleOAuthState
{
    NotConfigured,     // No ClientId/ClientSecret in secure storage
    ConfiguredNoAuth,  // Has ClientId/Secret but no access/refresh tokens
    AuthExpired,       // Has tokens but they're expired/invalid
    AuthValid,         // Has valid access/refresh tokens
    AuthPartial,       // Some scopes granted, others denied
    TokenRefreshing    // Currently attempting token refresh
}

// CRITICAL: Deferred Provider Operations Pattern - MUST NOT FAIL during instantiation
public async Task<Result<bool>> ConnectAsync()
{
    var authState = await GetOAuthStateAsync();

    return authState switch
    {
        GoogleOAuthState.NotConfigured =>
            Result<bool>.Failure(new ConfigurationError("Google OAuth client credentials not configured")),
        GoogleOAuthState.ConfiguredNoAuth =>
            Result<bool>.Failure(new AuthenticationError("OAuth authentication required - please complete setup")),
        GoogleOAuthState.AuthExpired =>
            await HandleExpiredTokensAsync(),
        GoogleOAuthState.AuthValid =>
            await PerformActualConnectionAsync(),
        GoogleOAuthState.AuthPartial =>
            Result<bool>.Failure(new AuthenticationError("Incomplete permissions - please re-authenticate")),
        GoogleOAuthState.TokenRefreshing =>
            Result<bool>.Failure(new OperationError("Authentication in progress - please wait")),
        _ =>
            Result<bool>.Failure(new InvalidOperationError($"Unknown auth state: {authState}"))
    };
}

// CRITICAL: Token Refresh with Graceful Degradation
private async Task<Result<bool>> HandleExpiredTokensAsync()
{
    try
    {
        var refreshResult = await RefreshTokensAsync();
        if (refreshResult.IsSuccess)
        {
            return await ConnectAsync(); // Retry with refreshed tokens
        }

        // Refresh failed - clear invalid tokens and require re-auth
        await ClearInvalidTokensAsync();
        return Result<bool>.Failure(new AuthenticationError("Token refresh failed - re-authentication required"));
    }
    catch (Exception ex)
    {
        return Result<bool>.Failure(new OperationError("Token refresh error", ex));
    }
}

// CRITICAL: Provider Health Check reflects detailed OAuth state
protected override async Task<Result<HealthCheckResult>> PerformHealthCheckAsync(CancellationToken cancellationToken)
{
    var authState = await GetOAuthStateAsync();
    var (status, description) = authState switch
    {
        GoogleOAuthState.NotConfigured => (HealthStatus.Unhealthy, "Google OAuth not configured"),
        GoogleOAuthState.ConfiguredNoAuth => (HealthStatus.Unhealthy, "Authentication required"),
        GoogleOAuthState.AuthExpired => (HealthStatus.Degraded, "Tokens expired - refresh needed"),
        GoogleOAuthState.AuthValid => (HealthStatus.Healthy, "Connected and authenticated"),
        GoogleOAuthState.AuthPartial => (HealthStatus.Degraded, "Partial access - some scopes missing"),
        GoogleOAuthState.TokenRefreshing => (HealthStatus.Degraded, "Refreshing authentication"),
        _ => (HealthStatus.Unhealthy, "Unknown authentication state")
    };

    return Result<HealthCheckResult>.Success(new HealthCheckResult(status, description));
}

// CRITICAL: ProviderBridgeService._providerDisplayInfo hardcodes "Gmail" and "Contacts"
// UI will ALWAYS show separate cards until this dictionary is updated
// Must REMOVE Gmail/Contacts entries and ADD GoogleServices entry

// CRITICAL: StartupStep enum missing InitializingGoogleServices value
// Must add to enum before referencing in startup sequence

// CRITICAL: Provider Status must reflect OAuth state granularity for UI
public string GetDetailedStatusMessage()
{
    return await GetOAuthStateAsync() switch
    {
        GoogleOAuthState.NotConfigured => "Google OAuth Configuration Required",
        GoogleOAuthState.ConfiguredNoAuth => "Google Authentication Required",
        GoogleOAuthState.AuthExpired => "Google Re-authentication Required",
        GoogleOAuthState.AuthValid => "Google Services Connected",
        GoogleOAuthState.AuthPartial => "Google Partial Access - Setup Incomplete",
        GoogleOAuthState.TokenRefreshing => "Refreshing Google Authentication...",
        _ => "Google Services - Unknown State"
    };
}

// CRITICAL: Avalonia MVVM requires CommunityToolkit.Mvvm patterns
[ObservableProperty]
[NotifyCanExecuteChangedFor(nameof(RefreshCommand))]
private string providerStatus = "Initializing...";                  // ✅ CORRECT

// CRITICAL: StartupOrchestrator has unified provider method already implemented
await ReinitializeGoogleServicesProviderAsync(cancellationToken);   // ✅ USE THIS

// CRITICAL: Result<T> pattern - never throw exceptions from providers
return Result<bool>.Success(true);   // ✅ CORRECT
throw new Exception();               // ❌ NEVER DO THIS
```

## Implementation Blueprint

### Data Models and Structure

All required data models already exist - GoogleServicesProvider and GoogleServicesProviderConfig are fully implemented with comprehensive validation and delegation patterns.

```csharp
// ✅ Already implemented in GoogleServicesProviderConfig.cs
public class GoogleServicesProviderConfig : BaseProviderConfig
{
    public bool EnableGmail { get; set; } = true;
    public bool EnableContacts { get; set; } = true;
    public List<string> Scopes => GetCombinedScopes();  // Unified OAuth scopes
    // ... comprehensive configuration with validation
}
```

### Implementation Tasks (ordered by dependencies)

```yaml
Task 0: ADD StartupStep.InitializingGoogleServices to src/TrashMailPanda/TrashMailPanda/Services/StartupStep.cs
  - MODIFY: StartupStep enum - add InitializingGoogleServices between InitializingSecurity and InitializingEmailProvider
  - IMPLEMENT: New enum value for unified provider initialization
  - FOLLOW pattern: Existing enum ordering pattern
  - NAMING: StartupStep.InitializingGoogleServices
  - CRITICAL: Must be done first or compilation will fail

Task 1: FIX src/TrashMailPanda/TrashMailPanda/Services/ServiceCollectionExtensions.cs
  - MODIFY: Lines 115, 142, 146-160 - Fix circular dependency in DI registration
  - IMPLEMENT: Direct dependency creation without interface resolution
  - FOLLOW pattern: Enterprise factory pattern avoiding service locator anti-pattern
  - CRITICAL: Create sub-providers directly with their dependencies, not through IServiceProvider
  - GOTCHA: Current registration causes infinite loop during DI resolution
  - DEPENDENCIES: Task 0 enum value must exist

Task 2: UPDATE src/TrashMailPanda/TrashMailPanda/Services/ProviderBridgeService.cs
  - MODIFY: _providerDisplayInfo dictionary - REMOVE Gmail and Contacts entries, ADD GoogleServices
  - IMPLEMENT: GoogleServices provider display information with sub-service status
  - FOLLOW pattern: Existing provider display info structure
  - CRITICAL: Without this, UI will show no provider cards or wrong cards
  - DEPENDENCIES: Task 1 DI registration must be working

Task 3: UPDATE src/TrashMailPanda/TrashMailPanda/Services/StartupOrchestrator.cs
  - MODIFY: ExecuteStartupSequenceAsync method - REPLACE individual initialization with unified
  - IMPLEMENT: Use StartupStep.InitializingGoogleServices and InitializeGoogleServicesProviderAsync
  - FOLLOW pattern: Existing startup step pattern with proper cancellation support
  - CRITICAL: Must REMOVE individual provider initialization calls completely
  - DEPENDENCIES: Task 2 provider bridge service must expose GoogleServices

Task 4: UPDATE src/TrashMailPanda/TrashMailPanda/ViewModels/ProviderStatusDashboardViewModel.cs
  - MODIFY: InitializeProviderCards method - will now automatically get GoogleServices from bridge service
  - IMPLEMENT: Enhanced provider card with OAuth state-aware status display
  - FOLLOW pattern: Existing ObservableCollection pattern with PropertyChanged notifications
  - CRITICAL: Card must show different states: "Configuration Required", "Authentication Required", "Re-authentication Required", "Connected", "Partial Access", "Refreshing..."
  - DEPENDENCIES: Task 3 startup integration must work

Task 5: UPDATE src/TrashMailPanda/TrashMailPanda/ViewModels/MainWindowViewModel.cs
  - MODIFY: OnAuthenticateProviderCommand method - add "googleservices" case
  - IMPLEMENT: Unified OAuth handler using ReinitializeGoogleServicesProviderAsync
  - FOLLOW pattern: Existing provider authentication cases with proper error handling
  - NAMING: Use "googleservices" as provider identifier consistently
  - DEPENDENCIES: Task 4 UI cards must display GoogleServices correctly

Task 6: UPDATE src/TrashMailPanda/TrashMailPanda/Views/Controls/ProviderStatusCard.axaml
  - MODIFY: Provider logo section - add Google Services logo and OAuth state indicators
  - IMPLEMENT: OAuth state-aware UI showing different visual states for each GoogleOAuthState
  - FOLLOW pattern: Existing logo display with IsVisible converter pattern
  - CRITICAL: Support visual states for NotConfigured, ConfiguredNoAuth, AuthExpired, AuthValid, AuthPartial, TokenRefreshing
  - NAMING: Use "GoogleServices" for provider name comparison
  - DEPENDENCIES: Task 5 OAuth handler must be implemented

Task 7: UPDATE src/TrashMailPanda/TrashMailPanda/Views/GoogleOAuthSetupDialog.axaml
  - MODIFY: Dialog title and messaging - change from "Gmail" to "Google Services Setup"
  - IMPLEMENT: Unified scope explanation covering Gmail and Contacts access clearly
  - FOLLOW pattern: Existing dialog layout with professional color system
  - CRITICAL: Explain what permissions are being granted for which services
  - DEPENDENCIES: Task 6 UI cards must display correctly
```

### Implementation Patterns & Key Details

```csharp
// Task 0: StartupStep Enum Pattern - StartupStep.cs
public enum StartupStep
{
    Initializing,
    InitializingStorage,
    InitializingSecurity,
    InitializingGoogleServices,  // NEW: Add this between Security and EmailProvider
    InitializingEmailProvider,   // Keep for potential future providers
    InitializingContactsProvider,
    InitializingLLMProvider,
    CheckingProviderHealth,
    Ready,
    Failed
}

// Task 1: DI Registration Pattern - ServiceCollectionExtensions.cs (FIXED CIRCULAR DEPENDENCY)
private static IServiceCollection AddProviders(this IServiceCollection services)
{
    // Create GoogleServicesProvider with DIRECT dependency creation (no interface resolution)
    services.AddSingleton<GoogleServicesProvider>(provider =>
    {
        // Create sub-providers DIRECTLY with their own dependencies
        var gmailRateLimitHandler = provider.GetRequiredService<IGmailRateLimitHandler>();
        var googleOAuthService = provider.GetRequiredService<IGoogleOAuthService>();
        var secureStorageManager = provider.GetRequiredService<ISecureStorageManager>();
        var gmailLogger = provider.GetRequiredService<ILogger<GmailEmailProvider>>();
        var contactsLogger = provider.GetRequiredService<ILogger<ContactsProvider>>();
        var unifiedLogger = provider.GetRequiredService<ILogger<GoogleServicesProvider>>();

        // Create sub-providers directly (CRITICAL: not through interface resolution)
        var gmailProvider = new GmailEmailProvider(
            gmailRateLimitHandler, googleOAuthService, secureStorageManager, gmailLogger);
        var contactsProvider = new ContactsProvider(
            googleOAuthService, secureStorageManager, contactsLogger);

        return new GoogleServicesProvider(
            gmailProvider, contactsProvider, googleOAuthService,
            secureStorageManager, unifiedLogger);
    });

    // Forward interface registrations to shared unified instance
    services.AddSingleton<IEmailProvider>(provider =>
        provider.GetRequiredService<GoogleServicesProvider>());
    services.AddSingleton<IContactsProvider>(provider =>
        provider.GetRequiredService<GoogleServicesProvider>());

    return services;
}

// Task 2: Provider Display Info Pattern - ProviderBridgeService.cs
private static readonly Dictionary<string, ProviderDisplayInfo> _providerDisplayInfo = new()
{
    // REMOVE these entries:
    // ["Gmail"] = new() { ... },
    // ["Contacts"] = new() { ... },

    // ADD unified GoogleServices entry:
    ["GoogleServices"] = new()
    {
        Name = "GoogleServices",
        DisplayName = "Google Services",
        Description = "Connect Gmail and Contacts with unified Google account authentication",
        Type = ProviderType.Communication,  // Use existing type
        IsRequired = true,
        AllowsMultiple = false,
        Icon = "🔗", // Unified services icon
        Complexity = SetupComplexity.Moderate,
        EstimatedSetupTimeMinutes = 3,
        Prerequisites = "Google account and web browser access",
        Features = new List<string> { "Gmail Access", "Contacts Access", "Unified OAuth" }
    },
    // Keep other providers unchanged:
    ["OpenAI"] = new() { ... },
    ["SQLite"] = new() { ... }
};

// Task 3: Startup Pattern - StartupOrchestrator.cs
private async Task ExecuteStartupSequenceAsync(CancellationToken cancellationToken)
{
    // Steps 1-2: Storage and Security (unchanged)
    UpdateProgress(StartupStep.InitializingStorage, "Initializing storage provider", 1);
    await InitializeStorageAsync(cancellationToken);

    UpdateProgress(StartupStep.InitializingSecurity, "Initializing security services", 2);
    await InitializeSecurityAsync(cancellationToken);

    // NEW Step 3: Unified Google Services Provider (REPLACES individual initialization)
    UpdateProgress(StartupStep.InitializingGoogleServices, "Initializing Google Services provider", 3);
    await InitializeGoogleServicesProviderAsync(cancellationToken);  // Use existing method

    // REMOVE these individual calls:
    // await InitializeEmailProviderAsync(cancellationToken);
    // await InitializeContactsProviderAsync(cancellationToken);

    // Step 4: LLM Provider (moved up, index adjusted)
    UpdateProgress(StartupStep.InitializingLLMProvider, "Initializing LLM provider", 4);
    await InitializeLLMProviderAsync(cancellationToken);

    // Step 5: Health Checks
    UpdateProgress(StartupStep.CheckingProviderHealth, "Checking provider health", 5);
    await PerformHealthChecksAsync(cancellationToken);

    // Step 6: Complete
    UpdateProgress(StartupStep.Ready, "Startup complete", 6, isComplete: true);
}

// Task 4: MVVM Pattern - ProviderStatusDashboardViewModel.cs
private void InitializeProviderCards()
{
    var providerDisplayInfo = _providerBridgeService.GetProviderDisplayInfo();

    foreach (var providerInfo in providerDisplayInfo.Values.OrderBy(p => p.Type))
    {
        // PATTERN: Now automatically gets GoogleServices from bridge service (after Task 2)
        // No filtering needed - will show GoogleServices, OpenAI, SQLite cards
        var cardViewModel = new ProviderStatusCardViewModel(providerInfo);

        // Enhanced card for GoogleServices with sub-service indicators
        if (providerInfo.Name == "GoogleServices")
        {
            // Add properties for Gmail/Contacts sub-service status
            cardViewModel.ShowSubServiceStatus = true;
            cardViewModel.SubServices = new[] { "Gmail", "Contacts" };
        }

        // Wire events following existing pattern...
        cardViewModel.ProviderStatusChanged += OnProviderStatusChanged;
        cardViewModel.RefreshRequested += OnRefreshRequested;
        ProviderCards.Add(cardViewModel);
    }
}

// Task 5: OAuth Handler Pattern - MainWindowViewModel.cs
private async Task OnAuthenticateProviderAsync(string providerName)
{
    switch (providerName.ToLower())
    {
        case "googleservices":  // NEW: Unified OAuth case
            _logger.LogInformation("Opening Google Services OAuth setup for unified authentication");
            try
            {
                NavigationStatus = "Setting up Google Services authentication...";
                var dialogResult = await _dialogService.ShowGoogleOAuthSetupAsync();

                if (dialogResult)
                {
                    NavigationStatus = "Initializing Google Services...";

                    // Use unified reinitialization (CRITICAL - calls both Gmail and Contacts)
                    var reinitResult = await _startupOrchestrator.ReinitializeGoogleServicesProviderAsync();

                    if (reinitResult.IsSuccess)
                    {
                        NavigationStatus = "Google Services connected successfully - Gmail and Contacts ready";

                        // Automatic UI refresh (no manual refresh needed)
                        await _providerDashboardViewModel.RefreshAllProvidersCommand.ExecuteAsync(null);
                    }
                    else
                    {
                        NavigationStatus = $"Google Services setup failed: {reinitResult.Error?.Message}";
                        _logger.LogError("Google Services reinitialization failed: {Error}", reinitResult.Error?.Message);
                    }
                }
                else
                {
                    NavigationStatus = "Google Services setup cancelled";
                }
            }
            catch (Exception ex)
            {
                NavigationStatus = "Google Services setup error - please try again";
                _logger.LogError(ex, "Error during Google Services authentication");
            }
            break;

        // Keep existing cases for other providers (OpenAI, etc.)
        case "openai":
            // ... existing OpenAI handler
            break;
    }
}

// Task 6: OAuth State-Aware Provider Card UI Pattern - ProviderStatusCard.axaml
<UserControl>
    <!-- Provider Logo Section -->
    <Image Source="/Assets/Logos/google-services-logo.png"
           IsVisible="{Binding ProviderName, Converter={StaticResource StringEqualsConverter}, ConverterParameter=GoogleServices}"
           Width="32" Height="32"/>

    <!-- OAuth State-Aware Status Display (CRITICAL: Different visual states) -->
    <StackPanel Orientation="Horizontal" Spacing="4"
                IsVisible="{Binding ProviderName, Converter={StaticResource StringEqualsConverter}, ConverterParameter=GoogleServices}">

        <!-- NotConfigured State -->
        <Border Classes="status-error" Background="{DynamicResource StatusError}"
                IsVisible="{Binding IsNotConfigured}" CornerRadius="3" Padding="4,2">
            <TextBlock Text="⚙️ Config Required" FontSize="10" Foreground="White"/>
        </Border>

        <!-- ConfiguredNoAuth State -->
        <Border Classes="status-warning" Background="{DynamicResource StatusWarning}"
                IsVisible="{Binding IsConfiguredNoAuth}" CornerRadius="3" Padding="4,2">
            <TextBlock Text="🔐 Auth Required" FontSize="10" Foreground="White"/>
        </Border>

        <!-- AuthExpired State -->
        <Border Classes="status-warning" Background="{DynamicResource StatusWarning}"
                IsVisible="{Binding IsAuthExpired}" CornerRadius="3" Padding="4,2">
            <TextBlock Text="🔄 Re-auth Required" FontSize="10" Foreground="White"/>
        </Border>

        <!-- TokenRefreshing State -->
        <Border Classes="status-info" Background="{DynamicResource StatusInfo}"
                IsVisible="{Binding IsTokenRefreshing}" CornerRadius="3" Padding="4,2">
            <TextBlock Text="🔄 Refreshing..." FontSize="10" Foreground="White"/>
        </Border>

        <!-- AuthValid State - Show sub-service breakdown -->
        <StackPanel Orientation="Horizontal" Spacing="2" IsVisible="{Binding IsAuthValid}">
            <Border Classes="status-success" Background="{DynamicResource StatusSuccess}"
                    IsVisible="{Binding IsGmailHealthy}" CornerRadius="3" Padding="4,2">
                <TextBlock Text="Gmail ✅" FontSize="10" Foreground="White"/>
            </Border>
            <Border Classes="status-success" Background="{DynamicResource StatusSuccess}"
                    IsVisible="{Binding IsContactsHealthy}" CornerRadius="3" Padding="4,2">
                <TextBlock Text="Contacts ✅" FontSize="10" Foreground="White"/>
            </Border>
        </StackPanel>

        <!-- AuthPartial State -->
        <Border Classes="status-warning" Background="{DynamicResource StatusWarning}"
                IsVisible="{Binding IsAuthPartial}" CornerRadius="3" Padding="4,2">
            <TextBlock Text="⚠️ Partial Access" FontSize="10" Foreground="White"/>
        </Border>
    </StackPanel>

    <!-- Provider Status Text with OAuth State Details -->
    <TextBlock Text="{Binding DetailedStatusMessage}"
               Foreground="{DynamicResource TextSecondary}"
               FontSize="12" TextWrapping="Wrap"
               IsVisible="{Binding ProviderName, Converter={StaticResource StringEqualsConverter}, ConverterParameter=GoogleServices}"/>
</UserControl>

// CRITICAL: ProviderStatusCardViewModel OAuth State Properties
public partial class ProviderStatusCardViewModel : ViewModelBase
{
    [ObservableProperty] private bool _isNotConfigured;
    [ObservableProperty] private bool _isConfiguredNoAuth;
    [ObservableProperty] private bool _isAuthExpired;
    [ObservableProperty] private bool _isAuthValid;
    [ObservableProperty] private bool _isAuthPartial;
    [ObservableProperty] private bool _isTokenRefreshing;
    [ObservableProperty] private bool _isGmailHealthy;
    [ObservableProperty] private bool _isContactsHealthy;
    [ObservableProperty] private string _detailedStatusMessage = string.Empty;

    public void UpdateOAuthState(GoogleOAuthState state, bool gmailHealthy, bool contactsHealthy)
    {
        // Reset all state flags
        IsNotConfigured = state == GoogleOAuthState.NotConfigured;
        IsConfiguredNoAuth = state == GoogleOAuthState.ConfiguredNoAuth;
        IsAuthExpired = state == GoogleOAuthState.AuthExpired;
        IsAuthValid = state == GoogleOAuthState.AuthValid;
        IsAuthPartial = state == GoogleOAuthState.AuthPartial;
        IsTokenRefreshing = state == GoogleOAuthState.TokenRefreshing;

        // Update sub-service health (only relevant when AuthValid)
        IsGmailHealthy = gmailHealthy && IsAuthValid;
        IsContactsHealthy = contactsHealthy && IsAuthValid;

        // Update detailed status message
        DetailedStatusMessage = state switch
        {
            GoogleOAuthState.NotConfigured => "Google OAuth configuration required",
            GoogleOAuthState.ConfiguredNoAuth => "Click 'Setup' to authenticate with Google",
            GoogleOAuthState.AuthExpired => "Authentication expired - please re-authenticate",
            GoogleOAuthState.AuthValid => $"Connected - Gmail {(gmailHealthy ? "✅" : "❌")} Contacts {(contactsHealthy ? "✅" : "❌")}",
            GoogleOAuthState.AuthPartial => "Partial access granted - some features may be limited",
            GoogleOAuthState.TokenRefreshing => "Refreshing authentication tokens...",
            _ => "Unknown authentication state"
        };

        // Update setup button visibility and text
        ShowSetupButton = state != GoogleOAuthState.AuthValid && state != GoogleOAuthState.TokenRefreshing;
        SetupButtonText = state switch
        {
            GoogleOAuthState.NotConfigured => "Configure",
            GoogleOAuthState.ConfiguredNoAuth => "Authenticate",
            GoogleOAuthState.AuthExpired => "Re-authenticate",
            GoogleOAuthState.AuthPartial => "Complete Setup",
            _ => "Setup"
        };
    }
}

// Task 7: OAuth Dialog Pattern - GoogleOAuthSetupDialog.axaml
<Window Title="Google Services Setup" ...>
    <StackPanel Spacing="16">
        <TextBlock Text="Google Services Setup" FontSize="18" FontWeight="Bold"
                   Foreground="{DynamicResource TextPrimary}"/>

        <TextBlock TextWrapping="Wrap" Foreground="{DynamicResource TextSecondary}">
            This will connect your Google account to provide access to:
            • Gmail - for email triage and management
            • Contacts - for enhanced email context and organization

            You'll complete one authentication process that grants access to both services.
        </TextBlock>

        <TextBlock Text="Required Permissions:" FontWeight="SemiBold" Foreground="{DynamicResource TextPrimary}"/>
        <TextBlock TextWrapping="Wrap" Foreground="{DynamicResource TextSecondary}">
            • Read, modify, and manage your Gmail messages
            • Read your contacts for email context
            • Offline access to maintain connection
        </TextBlock>
    </StackPanel>
</Window>
```

### Integration Points

```yaml
CONFIGURATION:
  - section: GoogleServicesProvider in appsettings.json (already exists)
  - pattern: EnableGmail/EnableContacts feature flags control sub-provider activation
  - critical: Unified OAuth scopes combine Gmail and Contacts permissions

OAUTH_TOKENS:
  - storage: "google_" token prefix for unified credential storage
  - migration: GoogleTokenMigrationService handles transition from "gmail_" prefix
  - critical: Single OAuth flow populates tokens for both services

HEALTH_CHECKS:
  - aggregation: GoogleServicesProvider.HealthCheckAsync combines sub-provider health
  - status: Healthy only when both enabled services are operational
  - critical: UI shows unified status with sub-service breakdown

UI_BRANDING:
  - logo: Add Google Services logo to ProviderStatusCard.axaml
  - messaging: "Google Services Setup" instead of "Gmail OAuth Setup"
  - critical: Maintain professional color system from ProfessionalColors.cs
```

## Validation Loop

### Level 1: Syntax & Style (Immediate Feedback)

```bash
# Run after each file modification - fix before proceeding
dotnet build --verbosity minimal                     # Compilation check
dotnet format --verify-no-changes                    # Code formatting validation

# Expected: Zero build errors, no formatting issues
```

### Level 2: Unit Tests (Component Validation)

```bash
# Test provider registration and startup integration
dotnet test --filter "FullyQualifiedName~GoogleServicesProvider" -v
dotnet test --filter "FullyQualifiedName~StartupOrchestrator" -v

# Test UI components and MVVM integration
dotnet test --filter "FullyQualifiedName~ProviderStatusDashboard" -v
dotnet test --filter "FullyQualifiedName~MainWindowViewModel" -v

# Full test suite validation
dotnet test --configuration Debug -v

# Expected: All existing tests pass, provider integration tests successful
```

### Level 3: Integration Testing (System Validation)

```bash
# Application startup validation with unified provider
dotnet run --project src/TrashMailPanda --verbosity normal

# OAuth flow integration testing
# Manual: Open app → See single "Google Services" card → Click Setup → Complete OAuth → Verify both services connected

# Provider health check validation
dotnet test --filter "Category=Integration" --logger console

# Expected:
# - Single Google Services card appears instead of separate Gmail/Contacts cards
# - OAuth setup dialog shows unified messaging
# - Both Gmail and Contacts functionality available after single OAuth
# - Automatic UI refresh shows both services connected
```

### Level 4: User Experience Validation

```bash
# Complete user journey testing
echo "1. Start application and verify single Google Services provider card"
echo "2. Click setup on Google Services card"
echo "3. Complete OAuth flow in browser"
echo "4. Verify automatic UI refresh shows both services connected"
echo "5. Test Gmail functionality (email operations)"
echo "6. Test Contacts functionality (contact operations)"
echo "7. Verify provider health status aggregation"

# Performance validation - startup time should improve with unified provider
time dotnet run --project src/TrashMailPanda

# Expected: Startup completes faster with unified provider initialization
```

## Final Validation Checklist

### Technical Validation

- [ ] All 4 validation levels completed successfully
- [ ] Build succeeds: `dotnet build --configuration Release`
- [ ] No linting errors: `dotnet format --verify-no-changes`
- [ ] All tests pass: `dotnet test --configuration Release`
- [ ] Application starts without errors

### Feature Validation

- [ ] Single "Google Services" provider card replaces Gmail and Contacts cards
- [ ] OAuth setup dialog shows unified "Google Services" messaging
- [ ] Single OAuth flow grants access to both Gmail and Contacts APIs
- [ ] Automatic UI refresh after OAuth completion shows both services connected
- [ ] All existing Gmail functionality works through unified provider
- [ ] All existing Contacts functionality works through unified provider
- [ ] Provider health checks aggregate status appropriately
- [ ] Error cases handled gracefully with clear user messaging

### Architecture Validation

- [ ] GoogleServicesProvider registered as both IEmailProvider and IContactsProvider
- [ ] StartupOrchestrator uses unified initialization instead of separate providers
- [ ] DI container resolves interfaces to GoogleServicesProvider singleton
- [ ] No circular dependencies or registration conflicts
- [ ] Health check integration working properly
- [ ] Configuration validation working correctly

### User Experience Validation

- [ ] Clear user journey from setup to operational state
- [ ] No manual refresh required after OAuth completion
- [ ] Professional visual design maintains existing aesthetic
- [ ] Error messages are clear and actionable
- [ ] Setup process is intuitive and streamlined

---

## Anti-Patterns to Avoid

- ❌ Don't register both individual providers AND GoogleServicesProvider as interface implementations
- ❌ Don't skip the factory pattern - direct registration will cause DI conflicts
- ❌ Don't modify GoogleServicesProvider implementation - it's already correct
- ❌ Don't create new UI components - modify existing ones to show unified provider
- ❌ Don't break existing OAuth token storage - migration is already implemented
- ❌ Don't ignore health check aggregation - use existing GoogleServicesProvider.HealthCheckAsync
- ❌ Don't hardcode provider names - use consistent "GoogleServices" identifier