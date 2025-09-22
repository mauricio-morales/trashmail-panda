using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using TrashMailPanda.Providers.Email;
using TrashMailPanda.Providers.Email.Models;
using TrashMailPanda.Providers.LLM;
using TrashMailPanda.Providers.Storage;
using TrashMailPanda.Providers.Contacts;
using TrashMailPanda.Providers.Contacts.Models;
using TrashMailPanda.Providers.Contacts.Services;
using TrashMailPanda.Providers.Contacts.Adapters;
using TrashMailPanda.Providers.Email.Services;
using TrashMailPanda.Providers.GoogleServices;
using TrashMailPanda.Shared;
using TrashMailPanda.Shared.Security;
using TrashMailPanda.Shared.Services;
using TrashMailPanda.ViewModels;
using Microsoft.Extensions.Caching.Memory;

namespace TrashMailPanda.Services;

/// <summary>
/// Extension methods for configuring dependency injection services
/// </summary>
public static class ServiceCollectionExtensions
{
    /// <summary>
    /// Add all TrashMail Panda services to the dependency injection container
    /// </summary>
    public static IServiceCollection AddTrashMailPandaServices(this IServiceCollection services, IConfiguration configuration)
    {
        // Add logging
        services.AddLogging(builder =>
        {
            builder.AddConfiguration(configuration.GetSection("Logging"));
            builder.AddConsole();
            builder.AddDebug();
        });

        // Add configuration options
        services.AddOptions();
        services.Configure<EmailProviderConfig>(configuration.GetSection("EmailProvider"));
        services.Configure<LLMProviderConfig>(configuration.GetSection("LLMProvider"));
        services.Configure<StorageProviderConfig>(configuration.GetSection("StorageProvider"));
        services.Configure<ContactsProviderConfig>(configuration.GetSection("ContactsProvider"));
        services.Configure<GoogleServicesProviderConfig>(configuration.GetSection("GoogleServicesProvider"));

        // Add security services
        services.AddSecurityServices();

        // Add providers
        services.AddProviders();

        // Add application services
        services.AddApplicationServices();

        // Add view models
        services.AddViewModels();

        return services;
    }

    /// <summary>
    /// Add security-related services
    /// </summary>
    private static IServiceCollection AddSecurityServices(this IServiceCollection services)
    {
        services.AddSingleton<IMasterKeyManager, MasterKeyManager>();
        services.AddSingleton<ICredentialEncryption, CredentialEncryption>();
        services.AddSingleton<ISecureStorageManager, SecureStorageManager>();
        services.AddSingleton<ISecurityAuditLogger, SecurityAuditLogger>();
        services.AddSingleton<ITokenRotationService, TokenRotationService>();
        services.AddSingleton<IGoogleTokenMigrationService, GoogleTokenMigrationService>();
        services.AddSingleton<IConfigurationMigrationService, ConfigurationMigrationService>();

        // Register unified Google OAuth service for all Google APIs (Gmail, Contacts, etc.)
        services.AddSingleton<IGoogleOAuthService, GoogleOAuthService>();

        // Register SecureTokenDataStore for OAuth token storage
        services.AddSingleton<Google.Apis.Util.Store.IDataStore>(provider =>
            new SecureTokenDataStore(
                provider.GetRequiredService<ISecureStorageManager>(),
                provider.GetRequiredService<ILogger<SecureTokenDataStore>>()));

        // Register phone number service for optimal performance
        services.AddSingleton<IPhoneNumberService, PhoneNumberService>();

        return services;
    }

    /// <summary>
    /// Add provider services with deferred initialization
    /// </summary>
    private static IServiceCollection AddProviders(this IServiceCollection services)
    {
        // Storage provider can be constructed immediately with default path
        // but initialization will be deferred until security services are ready
        services.AddSingleton<IStorageProvider>(serviceProvider =>
        {
            var config = serviceProvider.GetRequiredService<IConfiguration>();
            var databasePath = config.GetSection("StorageProvider:DatabasePath").Value ?? "./data/app.db";
            var password = config.GetSection("StorageProvider:Password").Value ?? "TrashMailPanda-DefaultKey";
            return new SqliteStorageProvider(databasePath, password);
        });

        // Register providers as singletons - they exist immediately but operational state
        // depends on credential availability at runtime (runtime credential-dependent pattern)

        // Gmail provider dependencies
        services.AddSingleton<IGmailRateLimitHandler, GmailRateLimitHandler>();

        // Register Gmail Email Provider - will check credentials at runtime
        services.AddSingleton<IEmailProvider, GmailEmailProvider>();

        // Contacts provider dependencies
        services.AddSingleton<ContactsCacheManager>();
        services.AddSingleton<TrustSignalCalculator>();
        services.AddSingleton<IMemoryCache, MemoryCache>();

        // Register GoogleContactsAdapter with config resolution
        services.AddSingleton<GoogleContactsAdapter>(provider =>
        {
            var config = provider.GetRequiredService<IOptions<ContactsProviderConfig>>().Value;
            var googleOAuthService = provider.GetRequiredService<IGoogleOAuthService>();
            var secureStorageManager = provider.GetRequiredService<ISecureStorageManager>();
            var securityAuditLogger = provider.GetRequiredService<ISecurityAuditLogger>();
            var phoneNumberService = provider.GetRequiredService<IPhoneNumberService>();
            var logger = provider.GetRequiredService<ILogger<GoogleContactsAdapter>>();

            return new GoogleContactsAdapter(
                googleOAuthService,
                secureStorageManager,
                securityAuditLogger,
                config,
                phoneNumberService,
                logger);
        });

        // Register Contacts Provider - will check credentials at runtime
        services.AddSingleton<IContactsProvider, ContactsProvider>();

        // Register GoogleServicesProvider as a unified provider for both Gmail and Contacts
        // This uses the delegation pattern to provide both IEmailProvider and IContactsProvider interfaces
        services.AddSingleton<GoogleServicesProvider>(provider =>
        {
            var gmailProvider = provider.GetRequiredService<IEmailProvider>() as GmailEmailProvider;
            var contactsProvider = provider.GetRequiredService<IContactsProvider>() as ContactsProvider;
            var googleOAuthService = provider.GetRequiredService<IGoogleOAuthService>();
            var secureStorageManager = provider.GetRequiredService<ISecureStorageManager>();
            var logger = provider.GetRequiredService<ILogger<GoogleServicesProvider>>();

            return new GoogleServicesProvider(
                gmailProvider!,
                contactsProvider!,
                googleOAuthService,
                secureStorageManager,
                logger);
        });

        // LLM provider can be added here if needed

        return services;
    }

    /// <summary>
    /// Add application services
    /// </summary>
    private static IServiceCollection AddApplicationServices(this IServiceCollection services)
    {
        // Register StartupOrchestrator with explicit provider injection
        services.AddSingleton<IStartupOrchestrator>(provider =>
            new StartupOrchestrator(
                provider.GetRequiredService<ILogger<StartupOrchestrator>>(),
                provider.GetRequiredService<IStorageProvider>(),
                provider.GetRequiredService<ISecureStorageManager>(),
                provider.GetRequiredService<IProviderStatusService>(),
                provider.GetRequiredService<IProviderBridgeService>(),
                provider,
                provider.GetRequiredService<IEmailProvider>(), // Explicit injection of Gmail provider
                null, // LLM provider not implemented yet
                provider.GetRequiredService<IContactsProvider>() // Explicit injection of Contacts provider
            ));
        services.AddSingleton<IProviderStatusService, ProviderStatusService>();
        services.AddSingleton<IApplicationService, ApplicationService>();

        // Add dialog service for proper MVVM dialog management
        services.AddSingleton<IDialogService, DialogService>();

        // Add provider bridge service for connecting legacy providers to new architecture
        services.AddSingleton<IProviderBridgeService, ProviderBridgeService>();

        // Add background health monitoring service
        services.AddHostedService<ProviderHealthMonitorService>();

        return services;
    }

    /// <summary>
    /// Add view models
    /// </summary>
    private static IServiceCollection AddViewModels(this IServiceCollection services)
    {
        // Register core view models as transients (new instance per request)
        services.AddTransient<WelcomeWizardViewModel>();

        // Add provider status dashboard ViewModels
        services.AddTransient<ProviderStatusDashboardViewModel>();
        // Note: ProviderStatusCardViewModel is created directly by the dashboard ViewModel
        // so it doesn't need DI registration

        // Add email dashboard ViewModel
        services.AddTransient<EmailDashboardViewModel>();

        // Add setup dialog ViewModels
        services.AddTransient<OpenAISetupViewModel>();
        services.AddTransient<GoogleOAuthSetupViewModel>();

        // Register MainWindowViewModel with navigation dependencies
        services.AddTransient<MainWindowViewModel>(provider => new MainWindowViewModel(
            provider.GetRequiredService<ProviderStatusDashboardViewModel>(),
            provider.GetRequiredService<EmailDashboardViewModel>(),
            provider,
            provider.GetRequiredService<IGoogleOAuthService>(),
            provider.GetRequiredService<IDialogService>(),
            provider.GetRequiredService<ILogger<MainWindowViewModel>>()
        ));

        return services;
    }

    /// <summary>
    /// Add the hosted service that will run the startup orchestration
    /// </summary>
    public static IServiceCollection AddStartupOrchestration(this IServiceCollection services)
    {
        services.AddHostedService<StartupHostedService>();
        return services;
    }
}

