using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using System.Threading;
using TrashMailPanda.Providers.Email;
using TrashMailPanda.Providers.LLM;
using TrashMailPanda.Providers.Storage;
using TrashMailPanda.Shared;
using TrashMailPanda.Shared.Security;
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

        // Register SecureTokenDataStore for OAuth token storage
        services.AddSingleton<Google.Apis.Util.Store.IDataStore, SecureTokenDataStore>();

        return services;
    }

    /// <summary>
    /// Add provider services with deferred initialization
    /// </summary>
    private static IServiceCollection AddProviders(this IServiceCollection services)
    {
        // 1. Register singleton semaphore (CRITICAL: shared across ALL storage access)
        //    This prevents SQLite concurrency violations by serializing database operations
        services.AddSingleton<SemaphoreSlim>(sp => new SemaphoreSlim(1, 1));

        // 2. Register DbContext with encrypted SQLite connection
        services.AddDbContext<TrashMailPandaDbContext>((serviceProvider, options) =>
        {
            var config = serviceProvider.GetRequiredService<IConfiguration>();
            var databasePath = config.GetSection("StorageProvider:DatabasePath").Value ?? "./data/app.db";
            var password = config.GetSection("StorageProvider:Password").Value ?? "TrashMailPanda-DefaultKey";

            var connectionString = new SqliteConnectionStringBuilder
            {
                DataSource = databasePath,
                Password = password
            }.ToString();

            options.UseSqlite(connectionString);
        }, ServiceLifetime.Singleton); // Singleton for desktop app - single database instance

        // 3. Register low-level storage repository (uses singleton semaphore)
        services.AddSingleton<IStorageRepository, SqliteStorageRepository>();

        // 4. Register domain services (Phase 3 refactoring - separated concerns)
        services.AddSingleton<TrashMailPanda.Providers.Storage.Services.IUserRulesService, TrashMailPanda.Providers.Storage.Services.UserRulesService>();
        services.AddSingleton<TrashMailPanda.Providers.Storage.Services.IEmailMetadataService, TrashMailPanda.Providers.Storage.Services.EmailMetadataService>();
        services.AddSingleton<TrashMailPanda.Providers.Storage.Services.IClassificationHistoryService, TrashMailPanda.Providers.Storage.Services.ClassificationHistoryService>();
        services.AddSingleton<TrashMailPanda.Providers.Storage.Services.ICredentialStorageService, TrashMailPanda.Providers.Storage.Services.CredentialStorageService>();
        services.AddSingleton<TrashMailPanda.Providers.Storage.Services.IConfigurationService, TrashMailPanda.Providers.Storage.Services.ConfigurationService>();

        // 5. Register EmailArchiveService (ML training data storage)
        services.AddSingleton<IEmailArchiveService>(serviceProvider =>
        {
            var context = serviceProvider.GetRequiredService<TrashMailPandaDbContext>();
            var semaphore = serviceProvider.GetRequiredService<SemaphoreSlim>();
            return new EmailArchiveService(context, semaphore);
        });

        // 6. Legacy IStorageProvider for backward compatibility (uses StorageProviderAdapter)
        //    (will be removed in Phase 4 after all consumers migrate to specific services)
        //    Now delegates to domain services instead of direct database access
        services.AddSingleton<IStorageProvider, StorageProviderAdapter>();

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

        // Add Gmail OAuth authentication service
        services.AddSingleton<IGmailOAuthService, GmailOAuthService>();

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
            provider.GetRequiredService<IGmailOAuthService>(),
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

