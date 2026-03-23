using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.ML;
using System.IO;
using System.Threading;
using TrashMailPanda.Providers.Email;
using TrashMailPanda.Providers.LLM;
using TrashMailPanda.Providers.ML;
using TrashMailPanda.Providers.ML.Classification;
using TrashMailPanda.Providers.ML.Config;
using TrashMailPanda.Providers.ML.Training;
using TrashMailPanda.Providers.ML.Versioning;
using TrashMailPanda.Providers.Storage;
using TrashMailPanda.Shared;
using TrashMailPanda.Shared.Security;
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

        // Register SecureTokenDataStore for OAuth token storage with logger
        services.AddSingleton<Google.Apis.Util.Store.IDataStore>(sp =>
        {
            var secureStorage = sp.GetRequiredService<ISecureStorageManager>();
            var loggerFactory = sp.GetRequiredService<ILoggerFactory>();
            var logger = loggerFactory.CreateLogger<SecureTokenDataStore>();
            return new SecureTokenDataStore(secureStorage, logger);
        });

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

            // NOTE: Do NOT call GetService<ISecureStorageManager>() here — it creates a circular
            // dependency: DbContext factory → SecureStorageManager → CredentialEncryption →
            // IStorageProvider → StorageProviderAdapter → TrashMailPandaDbContext → factory again.
            // Use the OS-standard path directly. The path can be overridden via appsettings.json.
            var configuredPath = config.GetSection("StorageProvider:DatabasePath").Value;
            var databasePath = !string.IsNullOrWhiteSpace(configuredPath)
                ? configuredPath
                : StorageProviderConfig.GetOsDefaultPath();

            // Ensure the directory exists before opening the connection
            var directory = Path.GetDirectoryName(databasePath);
            if (!string.IsNullOrEmpty(directory))
                Directory.CreateDirectory(directory);

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
            var logger = serviceProvider.GetRequiredService<ILogger<EmailArchiveService>>();
            return new EmailArchiveService(context, semaphore, logger);
        });

        // 6. Legacy IStorageProvider for backward compatibility (uses StorageProviderAdapter)
        //    (will be removed in Phase 4 after all consumers migrate to specific services)
        //    Now delegates to domain services instead of direct database access
        services.AddSingleton<IStorageProvider, StorageProviderAdapter>();

        // 7. Register Email provider dependencies
        services.AddSingleton<TrashMailPanda.Providers.Email.Services.IGmailRateLimitHandler, TrashMailPanda.Providers.Email.Services.GmailRateLimitHandler>();

        // 8. Register IEmailProvider (Gmail) - required for console startup flow
        //    Uses options pattern for configuration from appsettings.json
        services.AddSingleton<IEmailProvider, GmailEmailProvider>();
        services.AddSingleton<GmailEmailProvider>(sp =>
            (GmailEmailProvider)sp.GetRequiredService<IEmailProvider>());
        services.Configure<GmailProviderConfig>(services.BuildServiceProvider().GetRequiredService<IConfiguration>().GetSection("EmailProvider"));

        // 9. Register training-data repositories (US1, US2, US3, US4)
        services.AddSingleton<TrashMailPanda.Providers.Storage.ITrainingEmailRepository,
            TrashMailPanda.Providers.Storage.TrainingEmailRepository>();
        services.AddSingleton<TrashMailPanda.Providers.Storage.ILabelTaxonomyRepository,
            TrashMailPanda.Providers.Storage.LabelTaxonomyRepository>();
        services.AddSingleton<TrashMailPanda.Providers.Storage.ILabelAssociationRepository,
            TrashMailPanda.Providers.Storage.LabelAssociationRepository>();
        services.AddSingleton<TrashMailPanda.Providers.Storage.IScanProgressRepository,
            TrashMailPanda.Providers.Storage.ScanProgressRepository>();

        // 10. Register training-data service (US1) and scan command
        services.AddSingleton<TrashMailPanda.Shared.Base.ITrainingSignalAssigner,
            TrashMailPanda.Providers.Email.Services.TrainingSignalAssigner>();
        services.AddSingleton<TrashMailPanda.Providers.Email.Services.IGmailTrainingDataService,
            TrashMailPanda.Providers.Email.Services.GmailTrainingDataService>();
        services.AddSingleton<TrashMailPanda.Services.GmailTrainingScanCommand>();

        // 11. Register ML model infrastructure (feature #059)
        services.AddMLServices();
        services.AddSingleton<TrainingConsoleService>();

        // LLM provider is NOT registered here - will be added when AI features are implemented

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

        // Add Google OAuth services (for Gmail authentication)
        services.AddSingleton<IGoogleOAuthHandler, GoogleOAuthHandler>();
        services.AddSingleton<IGoogleTokenValidator, GoogleTokenValidator>();
        services.AddTransient<ILocalOAuthCallbackListener, LocalOAuthCallbackListener>();

        // Register factory for LocalOAuthCallbackListener
        services.AddSingleton<Func<ILocalOAuthCallbackListener>>(sp =>
            () => sp.GetRequiredService<ILocalOAuthCallbackListener>());

        // Add console services (for console-first architecture)
        services.AddSingleton<TrashMailPanda.Services.Console.ConsoleStatusDisplay>();
        services.AddSingleton<TrashMailPanda.Services.Console.ConsoleStartupOrchestrator>();
        services.AddSingleton<TrashMailPanda.Services.Console.ConfigurationWizard>();
        services.AddSingleton<TrashMailPanda.Services.Console.ModeSelectionMenu>();

        // Console TUI services (feature #060)
        services.AddSingleton<IEmailTriageService, EmailTriageService>();
        services.AddSingleton<IBulkOperationService, BulkOperationService>();
        services.AddSingleton<TrashMailPanda.Services.Console.IEmailTriageConsoleService,
            TrashMailPanda.Services.Console.EmailTriageConsoleService>();
        services.AddSingleton<TrashMailPanda.Services.Console.IBulkOperationConsoleService,
            TrashMailPanda.Services.Console.BulkOperationConsoleService>();
        services.AddSingleton<TrashMailPanda.Services.Console.IProviderSettingsConsoleService,
            TrashMailPanda.Services.Console.ProviderSettingsConsoleService>();
        services.AddSingleton<TrashMailPanda.Services.Console.IConsoleHelpPanel,
            TrashMailPanda.Services.Console.ConsoleHelpPanel>();

        // UI-agnostic service contracts (feature #061)
        services.AddSingleton<IClassificationService, ClassificationService>();
        services.AddSingleton<IApplicationOrchestrator, ApplicationOrchestrator>();
        services.AddSingleton<TrashMailPanda.Services.Console.ConsoleEventRenderer>();

        // Feature #064 — Runtime classification with user feedback
        services.AddSingleton<IAutoApplyService, AutoApplyService>();
        services.AddSingleton<IModelQualityMonitor, ModelQualityMonitor>();
        services.AddScoped<IAutoApplyUndoService, AutoApplyUndoService>();

        // Add background health monitoring service
        services.AddHostedService<ProviderHealthMonitorService>();

        return services;
    }

    /// <summary>
    /// Register ML.NET model training and classification services (feature #059).
    /// </summary>
    private static IServiceCollection AddMLServices(this IServiceCollection services)
    {
        // Shared MLContext — deterministic seed for reproducible results
        services.AddSingleton<MLContext>(_ => new MLContext(seed: 42));

        // Raw SqliteConnection for ModelVersionRepository (ADO.NET, not EF Core)
        services.AddSingleton<SqliteConnection>(sp =>
        {
            var config = sp.GetRequiredService<IConfiguration>();
            var configuredPath = config.GetSection("StorageProvider:DatabasePath").Value;
            var databasePath = !string.IsNullOrWhiteSpace(configuredPath)
                ? configuredPath
                : StorageProviderConfig.GetOsDefaultPath();

            var password = config.GetSection("StorageProvider:Password").Value
                           ?? "TrashMailPanda-DefaultKey";

            var connectionString = new SqliteConnectionStringBuilder
            {
                DataSource = databasePath,
                Password = password,
            }.ToString();

            var connection = new SqliteConnection(connectionString);
            connection.Open();
            return connection;
        });

        // Core ML infrastructure
        services.AddSingleton<FeaturePipelineBuilder>();
        services.AddSingleton<ActionModelTrainer>();
        services.AddSingleton<ActionClassifier>();
        services.AddSingleton<ModelVersionRepository>();
        services.AddSingleton<ModelVersionPruner>();
        services.AddSingleton<IncrementalUpdateService>();

        // ML model provider and training pipeline
        services.AddSingleton(sp => new MLModelProviderConfig { Name = "MLModelProvider" });
        services.AddOptions<MLModelProviderConfig>()
            .Configure(c => c.Name = "MLModelProvider")
            .ValidateDataAnnotations();
        services.AddSingleton<IMLModelProvider, MLModelProvider>();
        services.AddTransient<IModelTrainingPipeline, ModelTrainingPipeline>();

        return services;
    }
}

