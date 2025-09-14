using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using TrashMailPanda.Providers.Email;
using TrashMailPanda.Providers.LLM;
using TrashMailPanda.Providers.Storage;
using TrashMailPanda.Shared;
using TrashMailPanda.Shared.Security;
using TrashMailPanda.Shared.Services;
using TrashMailPanda.ViewModels;

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

        // Email and LLM providers are NOT registered here
        // They will be created by application services after secrets are captured through UI

        return services;
    }

    /// <summary>
    /// Add application services
    /// </summary>
    private static IServiceCollection AddApplicationServices(this IServiceCollection services)
    {
        services.AddSingleton<IStartupOrchestrator, StartupOrchestrator>();
        services.AddSingleton<IProviderStatusService, ProviderStatusService>();
        services.AddSingleton<IApplicationService, ApplicationService>();

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
        services.AddTransient<GmailSetupViewModel>();

        // Register MainWindowViewModel with navigation dependencies
        services.AddTransient<MainWindowViewModel>(provider => new MainWindowViewModel(
            provider.GetRequiredService<ProviderStatusDashboardViewModel>(),
            provider.GetRequiredService<EmailDashboardViewModel>(),
            provider,
            provider.GetRequiredService<IGoogleOAuthService>(),
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

