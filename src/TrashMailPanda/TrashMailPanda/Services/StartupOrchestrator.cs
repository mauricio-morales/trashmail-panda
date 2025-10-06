using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using TrashMailPanda.Shared;
using TrashMailPanda.Shared.Base;
using TrashMailPanda.Shared.Models;
using TrashMailPanda.Shared.Security;
using TrashMailPanda.Providers.Email;
using TrashMailPanda.Providers.Contacts;
using TrashMailPanda.Providers.Contacts.Models;
using TrashMailPanda.Providers.GoogleServices;

namespace TrashMailPanda.Services;

/// <summary>
/// Orchestrates the application startup sequence with provider initialization
/// </summary>
public class StartupOrchestrator : IStartupOrchestrator
{
    private readonly ILogger<StartupOrchestrator> _logger;
    private readonly IStorageProvider _storageProvider;
    private readonly ISecureStorageManager _secureStorageManager;
    private readonly IEmailProvider? _emailProvider;
    private readonly ILLMProvider? _llmProvider;
    private readonly IContactsProvider? _contactsProvider;
    private readonly IProviderStatusService _providerStatusService;
    private readonly IProviderBridgeService _providerBridgeService;
    private readonly IServiceProvider _serviceProvider;

    private StartupProgress _currentProgress = new() { CurrentStep = StartupStep.Initializing };
    private readonly object _progressLock = new();

    public event EventHandler<StartupProgressChangedEventArgs>? ProgressChanged;

    private const int TotalSteps = 6;
    private const int DefaultTimeoutMinutes = 5;

    public StartupOrchestrator(
        ILogger<StartupOrchestrator> logger,
        IStorageProvider storageProvider,
        ISecureStorageManager secureStorageManager,
        IProviderStatusService providerStatusService,
        IProviderBridgeService providerBridgeService,
        IServiceProvider serviceProvider,
        IEmailProvider? emailProvider = null,
        ILLMProvider? llmProvider = null,
        IContactsProvider? contactsProvider = null)
    {
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _storageProvider = storageProvider ?? throw new ArgumentNullException(nameof(storageProvider));
        _secureStorageManager = secureStorageManager ?? throw new ArgumentNullException(nameof(secureStorageManager));
        _emailProvider = emailProvider; // Can be null - will be created after secrets are available
        _llmProvider = llmProvider; // Can be null - will be created after secrets are available
        _contactsProvider = contactsProvider; // Can be null - will be created after secrets are available
        _providerStatusService = providerStatusService ?? throw new ArgumentNullException(nameof(providerStatusService));
        _providerBridgeService = providerBridgeService ?? throw new ArgumentNullException(nameof(providerBridgeService));
        _serviceProvider = serviceProvider ?? throw new ArgumentNullException(nameof(serviceProvider));
    }

    public async Task<StartupResult> ExecuteStartupAsync(CancellationToken cancellationToken = default)
    {
        var stopwatch = Stopwatch.StartNew();
        _logger.LogInformation("Starting application startup orchestration");

        // Apply timeout if none provided
        using var timeoutCts = new CancellationTokenSource(TimeSpan.FromMinutes(DefaultTimeoutMinutes));
        using var combinedCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, timeoutCts.Token);

        try
        {
            // Execute startup sequence
            await ExecuteStartupSequenceAsync(combinedCts.Token);

            stopwatch.Stop();

            var successResult = new StartupResult
            {
                IsSuccess = true,
                Status = "Startup completed successfully",
                Duration = stopwatch.Elapsed,
                Details = new Dictionary<string, object>
                {
                    { "total_steps", TotalSteps },
                    { "completed_steps", TotalSteps },
                    { "final_step", StartupStep.Ready }
                }
            };

            _logger.LogInformation("Application startup completed successfully in {Duration}", stopwatch.Elapsed);
            return successResult;
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            return CreateFailureResult(stopwatch.Elapsed, StartupFailureReason.Cancelled, "Startup was cancelled");
        }
        catch (OperationCanceledException) when (timeoutCts.Token.IsCancellationRequested)
        {
            return CreateFailureResult(stopwatch.Elapsed, StartupFailureReason.Timeout, $"Startup timed out after {DefaultTimeoutMinutes} minutes");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Exception during startup orchestration");
            return CreateFailureResult(stopwatch.Elapsed, StartupFailureReason.UnknownError, ex.Message);
        }
    }

    public StartupProgress GetProgress()
    {
        lock (_progressLock)
        {
            return _currentProgress;
        }
    }

    /// <summary>
    /// Re-initializes the Gmail provider with stored OAuth credentials
    /// Used after successful OAuth authentication to pick up new tokens
    /// </summary>
    public async Task<Result<bool>> ReinitializeGmailProviderAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            _logger.LogInformation("Re-initializing Gmail provider with stored credentials");
            await InitializeEmailProviderAsync(cancellationToken);
            _logger.LogInformation("Gmail provider re-initialization completed successfully");
            return Result<bool>.Success(true);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to re-initialize Gmail provider");
            return Result<bool>.Failure(new InitializationError($"Gmail re-initialization failed: {ex.Message}", ex.ToString(), ex));
        }
    }

    /// <summary>
    /// Re-initializes the Contacts provider with stored OAuth credentials
    /// Used after successful OAuth authentication to pick up new tokens
    /// </summary>
    public async Task<Result<bool>> ReinitializeContactsProviderAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            _logger.LogInformation("Re-initializing Contacts provider with stored credentials");
            await InitializeContactsProviderAsync(cancellationToken);
            _logger.LogInformation("Contacts provider re-initialization completed successfully");
            return Result<bool>.Success(true);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to re-initialize Contacts provider");
            return Result<bool>.Failure(new InitializationError($"Contacts re-initialization failed: {ex.Message}", ex.ToString(), ex));
        }
    }

    /// <summary>
    /// Re-initializes the unified Google Services provider with stored OAuth credentials
    /// Used after successful OAuth authentication to pick up new tokens for both Gmail and Contacts
    /// </summary>
    public async Task<Result<bool>> ReinitializeGoogleServicesProviderAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            _logger.LogInformation("Re-initializing unified Google Services provider with stored credentials");

            // Attempt token migration first
            var migrationService = _serviceProvider.GetService<IGoogleTokenMigrationService>();
            if (migrationService != null)
            {
                var migrationNeededResult = await migrationService.IsMigrationNeededAsync(cancellationToken);
                if (migrationNeededResult.IsSuccess && migrationNeededResult.Value)
                {
                    _logger.LogInformation("Token migration needed, performing migration from gmail_ to google_ prefix");
                    var migrationResult = await migrationService.MigrateTokensAsync(preserveLegacyTokens: false, cancellationToken);
                    if (migrationResult.IsSuccess)
                    {
                        _logger.LogInformation("Token migration completed successfully: {TokensMigrated} migrated, {TokensSkipped} skipped",
                            migrationResult.Value.TokensMigrated, migrationResult.Value.TokensSkipped);
                    }
                    else
                    {
                        _logger.LogWarning("Token migration failed: {Error}", migrationResult.Error?.Message);
                    }
                }
            }

            await InitializeGoogleServicesProviderAsync(cancellationToken);
            _logger.LogInformation("Google Services provider re-initialization completed successfully");
            return Result<bool>.Success(true);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to re-initialize Google Services provider");
            return Result<bool>.Failure(new InitializationError($"Google Services re-initialization failed: {ex.Message}", ex.ToString(), ex));
        }
    }

    private async Task ExecuteStartupSequenceAsync(CancellationToken cancellationToken)
    {
        // Check cancellation before starting
        cancellationToken.ThrowIfCancellationRequested();

        // Step 1: Initialize Storage
        UpdateProgress(StartupStep.InitializingStorage, "Initializing storage provider", 1);
        await InitializeStorageAsync(cancellationToken);
        cancellationToken.ThrowIfCancellationRequested();

        // Step 2: Initialize Security
        UpdateProgress(StartupStep.InitializingSecurity, "Initializing security services", 2);
        await InitializeSecurityAsync(cancellationToken);
        cancellationToken.ThrowIfCancellationRequested();

        // Step 3: Initialize Google Services Provider (unified Gmail and Contacts)
        UpdateProgress(StartupStep.InitializingGoogleServices, "Initializing Google Services provider", 3);
        await InitializeGoogleServicesProviderAsync(cancellationToken);
        cancellationToken.ThrowIfCancellationRequested();

        // Step 4: Initialize LLM Provider
        UpdateProgress(StartupStep.InitializingLLMProvider, "Initializing LLM provider", 4);
        await InitializeLLMProviderAsync(cancellationToken);
        cancellationToken.ThrowIfCancellationRequested();

        // Step 5: Health Checks
        UpdateProgress(StartupStep.CheckingProviderHealth, "Checking provider health", 5);
        await PerformHealthChecksAsync(cancellationToken);
        cancellationToken.ThrowIfCancellationRequested();

        // Step 6: Complete
        UpdateProgress(StartupStep.Ready, "Startup complete", TotalSteps, isComplete: true);
    }

    private async Task InitializeStorageAsync(CancellationToken cancellationToken)
    {
        try
        {
            _logger.LogDebug("Initializing storage provider");
            await _storageProvider.InitAsync();
            _logger.LogDebug("Storage provider initialized successfully");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to initialize storage provider");
            UpdateProgressWithError(StartupStep.InitializingStorage, "Storage initialization failed", ex.Message);
            throw new InvalidOperationException("Storage initialization failed", ex);
        }
    }

    private async Task InitializeSecurityAsync(CancellationToken cancellationToken)
    {
        try
        {
            _logger.LogDebug("Initializing security services");
            var result = await _secureStorageManager.InitializeAsync();

            if (!result.IsSuccess)
            {
                throw new InvalidOperationException($"Security initialization failed: {result.ErrorMessage}");
            }

            _logger.LogDebug("Security services initialized successfully");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to initialize security services");
            UpdateProgressWithError(StartupStep.InitializingSecurity, "Security initialization failed", ex.Message);
            throw new InvalidOperationException("Security initialization failed", ex);
        }
    }

    private async Task InitializeEmailProviderAsync(CancellationToken cancellationToken)
    {
        try
        {
            _logger.LogInformation("[GMAIL INIT] Starting Gmail provider initialization");

            if (_emailProvider != null)
            {
                _logger.LogInformation("[GMAIL INIT] Gmail provider instance found, attempting to initialize with stored credentials");

                // Cast to IProvider to access the proper initialization method
                if (_emailProvider is IProvider<GmailProviderConfig> gmailProvider)
                {
                    _logger.LogInformation("[GMAIL INIT] Gmail provider cast successful, creating configuration");

                    // Get Gmail configuration from DI - this loads from appsettings.json
                    var configOptions = _serviceProvider.GetRequiredService<IOptions<GmailProviderConfig>>();
                    var config = configOptions.Value;

                    _logger.LogInformation("[GMAIL INIT] DEBUG - Config timeout values: RequestTimeout={RequestTimeout}s, TimeoutSeconds={TimeoutSeconds}s",
                        config.RequestTimeout.TotalSeconds, config.TimeoutSeconds);

                    // TEMPORARY FIX: Manually set TimeoutSeconds to be larger than RequestTimeout to pass validation
                    config.TimeoutSeconds = 180;  // Set to match appsettings.json value
                    _logger.LogInformation("[GMAIL INIT] DEBUG - Fixed timeout values: RequestTimeout={RequestTimeout}s, TimeoutSeconds={TimeoutSeconds}s",
                        config.RequestTimeout.TotalSeconds, config.TimeoutSeconds);

                    // Load Google OAuth client credentials from secure storage
                    _logger.LogInformation("[GMAIL INIT] Loading Google OAuth client credentials from storage");
                    var clientIdResult = await _secureStorageManager.RetrieveCredentialAsync(ProviderCredentialTypes.GoogleClientId);
                    var clientSecretResult = await _secureStorageManager.RetrieveCredentialAsync(ProviderCredentialTypes.GoogleClientSecret);

                    _logger.LogInformation("[GMAIL INIT] Client ID result: {ClientIdSuccess}, Client Secret result: {ClientSecretSuccess}",
                        clientIdResult.IsSuccess, clientSecretResult.IsSuccess);

                    if (clientIdResult.IsSuccess && clientSecretResult.IsSuccess)
                    {
                        config.ClientId = clientIdResult.Value;
                        config.ClientSecret = clientSecretResult.Value;

                        _logger.LogInformation("[GMAIL INIT] Config loaded with OAuth credentials (ID length: {IdLength}), calling InitializeAsync",
                            config.ClientId?.Length ?? 0);

                        // Actually initialize the provider with stored OAuth tokens
                        var initResult = await gmailProvider.InitializeAsync(config, cancellationToken);

                        _logger.LogInformation("[GMAIL INIT] InitializeAsync completed - Success: {Success}, Error: {Error}",
                            initResult.IsSuccess,
                            initResult.IsFailure ? initResult.Error?.Message : "None");

                        if (initResult.IsSuccess)
                        {
                            _logger.LogInformation("[GMAIL INIT] ✅ Gmail provider initialized successfully with stored credentials");

                            // Check provider health immediately after initialization
                            var healthResult = await gmailProvider.HealthCheckAsync(cancellationToken);
                            _logger.LogInformation("[GMAIL INIT] Health check after init - Healthy: {IsHealthy}, Status: {Status}",
                                healthResult.IsSuccess && healthResult.Value.IsHealthy,
                                healthResult.IsSuccess ? healthResult.Value.Status : "Failed");
                        }
                        else
                        {
                            _logger.LogWarning("[GMAIL INIT] ❌ Gmail provider initialization failed: {Error}", initResult.Error?.Message);
                            _logger.LogWarning("[GMAIL INIT] Error details: {ErrorDetails}", initResult.Error?.ToString());
                            // Don't throw - this means provider needs authentication but that's handled by UI
                        }
                    }
                    else
                    {
                        _logger.LogWarning("[GMAIL INIT] ❌ Gmail OAuth client credentials not available - provider needs setup");
                        _logger.LogWarning("[GMAIL INIT] Client ID error: {ClientIdError}, Client Secret error: {ClientSecretError}",
                            clientIdResult.ErrorMessage, clientSecretResult.ErrorMessage);
                    }
                }
                else
                {
                    _logger.LogWarning("[GMAIL INIT] ❌ Email provider is not a Gmail provider - cannot initialize");
                    _logger.LogWarning("[GMAIL INIT] Provider type: {ProviderType}", _emailProvider.GetType().FullName);
                }
            }
            else
            {
                _logger.LogWarning("[GMAIL INIT] ❌ No email provider instance registered - skipping initialization");
            }

            _logger.LogInformation("[GMAIL INIT] Gmail provider initialization process completed");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "[GMAIL INIT] ❌ Exception during Gmail provider initialization");
            UpdateProgressWithError(StartupStep.InitializingEmailProvider, "Email provider initialization failed", ex.Message);
            throw new InvalidOperationException("Email provider initialization failed", ex);
        }
    }

    private async Task InitializeContactsProviderAsync(CancellationToken cancellationToken)
    {
        try
        {
            _logger.LogInformation("[CONTACTS INIT] Starting contacts provider initialization");

            if (_contactsProvider != null)
            {
                _logger.LogInformation("[CONTACTS INIT] Contacts provider instance found, attempting to initialize with stored credentials");

                // Cast to IProvider to access the proper initialization method
                if (_contactsProvider is IProvider<ContactsProviderConfig> contactsProviderTyped)
                {
                    _logger.LogInformation("[CONTACTS INIT] Contacts provider cast successful, creating configuration");

                    // Get Contacts configuration from DI - this loads from appsettings.json
                    var configOptions = _serviceProvider.GetRequiredService<IOptions<ContactsProviderConfig>>();
                    var config = configOptions.Value;

                    // TEMPORARY FIX: Manually set TimeoutSeconds to pass validation
                    config.TimeoutSeconds = 120;  // Set to match appsettings.json value

                    // Load Google OAuth client credentials from secure storage
                    _logger.LogInformation("[CONTACTS INIT] Loading Google OAuth client credentials from storage");
                    var clientIdResult = await _secureStorageManager.RetrieveCredentialAsync(ProviderCredentialTypes.GoogleClientId);
                    var clientSecretResult = await _secureStorageManager.RetrieveCredentialAsync(ProviderCredentialTypes.GoogleClientSecret);

                    _logger.LogInformation("[CONTACTS INIT] Client ID result: {ClientIdSuccess}, Client Secret result: {ClientSecretSuccess}",
                        clientIdResult.IsSuccess, clientSecretResult.IsSuccess);

                    if (clientIdResult.IsSuccess && clientSecretResult.IsSuccess)
                    {
                        config.ClientId = clientIdResult.Value;
                        config.ClientSecret = clientSecretResult.Value;

                        _logger.LogInformation("[CONTACTS INIT] Config loaded with OAuth credentials (ID length: {IdLength}), calling InitializeAsync",
                            config.ClientId?.Length ?? 0);

                        // Actually initialize the provider with stored OAuth tokens
                        var initResult = await contactsProviderTyped.InitializeAsync(config, cancellationToken);

                        _logger.LogInformation("[CONTACTS INIT] InitializeAsync completed - Success: {Success}, Error: {Error}",
                            initResult.IsSuccess,
                            initResult.IsFailure ? initResult.Error?.Message : "None");

                        if (initResult.IsSuccess)
                        {
                            _logger.LogInformation("[CONTACTS INIT] ✅ Contacts provider initialized successfully with stored credentials");

                            // Check provider health immediately after initialization
                            var healthResult = await contactsProviderTyped.HealthCheckAsync(cancellationToken);
                            _logger.LogInformation("[CONTACTS INIT] Health check after init - Healthy: {IsHealthy}, Status: {Status}",
                                healthResult.IsSuccess && healthResult.Value.IsHealthy,
                                healthResult.IsSuccess ? healthResult.Value.Status : "Failed");
                        }
                        else
                        {
                            _logger.LogWarning("[CONTACTS INIT] ❌ Contacts provider initialization failed: {Error}", initResult.Error?.Message);
                            _logger.LogWarning("[CONTACTS INIT] Error details: {ErrorDetails}", initResult.Error?.ToString());
                            // Don't throw - this means provider needs authentication but that's handled by UI
                        }
                    }
                    else
                    {
                        _logger.LogWarning("[CONTACTS INIT] ❌ Contacts OAuth client credentials not available - provider needs setup");
                        _logger.LogWarning("[CONTACTS INIT] Client ID error: {ClientIdError}, Client Secret error: {ClientSecretError}",
                            clientIdResult.ErrorMessage, clientSecretResult.ErrorMessage);
                    }
                }
                else
                {
                    _logger.LogWarning("[CONTACTS INIT] ❌ Contacts provider is not a typed provider - cannot initialize");
                    _logger.LogWarning("[CONTACTS INIT] Provider type: {ProviderType}", _contactsProvider.GetType().FullName);
                }
            }
            else
            {
                _logger.LogWarning("[CONTACTS INIT] ❌ No contacts provider instance registered - skipping initialization");
            }

            _logger.LogInformation("[CONTACTS INIT] Contacts provider initialization process completed");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "[CONTACTS INIT] ❌ Exception during contacts provider initialization");
            UpdateProgressWithError(StartupStep.InitializingContactsProvider, "Contacts provider initialization failed", ex.Message);
            throw new InvalidOperationException("Contacts provider initialization failed", ex);
        }
    }

    private async Task InitializeLLMProviderAsync(CancellationToken cancellationToken)
    {
        try
        {
            _logger.LogDebug("Initializing LLM provider");

            // Check LLM provider status through bridge service
            var llmStatusResult = await _providerBridgeService.GetLLMProviderStatusAsync();

            if (llmStatusResult.IsSuccess)
            {
                var status = llmStatusResult.Value!;
                _logger.LogDebug("LLM provider status: {Status} (Healthy: {IsHealthy})",
                    status.Status, status.IsHealthy);

                // LLM provider initialization is considered successful if status can be retrieved
                // The actual health status will be handled by the provider status service
                _logger.LogDebug("LLM provider initialized successfully");
            }
            else
            {
                _logger.LogWarning("LLM provider status check failed: {Error}", llmStatusResult.Error?.Message);
                // Don't throw - let the provider status service handle unhealthy providers
                _logger.LogDebug("LLM provider initialization completed with warnings");
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to initialize LLM provider");
            UpdateProgressWithError(StartupStep.InitializingLLMProvider, "LLM provider initialization failed", ex.Message);
            throw new InvalidOperationException("LLM provider initialization failed", ex);
        }
    }

    private async Task InitializeGoogleServicesProviderAsync(CancellationToken cancellationToken)
    {
        try
        {
            _logger.LogInformation("[GOOGLE SERVICES INIT] Starting unified Google Services provider initialization");

            // Get the unified GoogleServicesProvider from DI
            var googleServicesProvider = _serviceProvider.GetService<GoogleServicesProvider>();

            if (googleServicesProvider != null)
            {
                _logger.LogInformation("[GOOGLE SERVICES INIT] Google Services provider instance found, attempting to initialize with stored credentials");

                // Cast to IProvider to access the proper initialization method
                if (googleServicesProvider is IProvider<GoogleServicesProviderConfig> unifiedProvider)
                {
                    _logger.LogInformation("[GOOGLE SERVICES INIT] Google Services provider cast successful, creating configuration");

                    // Get GoogleServices configuration from DI - this loads from appsettings.json
                    var configOptions = _serviceProvider.GetRequiredService<IOptions<GoogleServicesProviderConfig>>();
                    var config = configOptions.Value;

                    // Load Google OAuth client credentials from secure storage
                    _logger.LogInformation("[GOOGLE SERVICES INIT] Loading Google OAuth client credentials from storage");
                    var clientIdResult = await _secureStorageManager.RetrieveCredentialAsync(ProviderCredentialTypes.GoogleClientId);
                    var clientSecretResult = await _secureStorageManager.RetrieveCredentialAsync(ProviderCredentialTypes.GoogleClientSecret);

                    _logger.LogInformation("[GOOGLE SERVICES INIT] Client ID result: {ClientIdSuccess}, Client Secret result: {ClientSecretSuccess}",
                        clientIdResult.IsSuccess, clientSecretResult.IsSuccess);

                    if (clientIdResult.IsSuccess && clientSecretResult.IsSuccess)
                    {
                        config.ClientId = clientIdResult.Value;
                        config.ClientSecret = clientSecretResult.Value;

                        _logger.LogInformation("[GOOGLE SERVICES INIT] Config loaded with OAuth credentials (ID length: {IdLength}), calling InitializeAsync",
                            config.ClientId?.Length ?? 0);

                        // Actually initialize the unified provider with stored OAuth tokens
                        var initResult = await unifiedProvider.InitializeAsync(config, cancellationToken);

                        _logger.LogInformation("[GOOGLE SERVICES INIT] InitializeAsync completed - Success: {Success}, Error: {Error}",
                            initResult.IsSuccess,
                            initResult.IsFailure ? initResult.Error?.Message : "None");

                        if (initResult.IsSuccess)
                        {
                            _logger.LogInformation("[GOOGLE SERVICES INIT] ✅ Google Services provider initialized successfully with stored credentials");

                            // Check provider health immediately after initialization
                            var healthResult = await unifiedProvider.HealthCheckAsync(cancellationToken);
                            _logger.LogInformation("[GOOGLE SERVICES INIT] Health check after init - Healthy: {IsHealthy}, Status: {Status}",
                                healthResult.IsSuccess && healthResult.Value.Status == HealthStatus.Healthy,
                                healthResult.IsSuccess ? healthResult.Value.Status : "Failed");
                        }
                        else
                        {
                            _logger.LogWarning("[GOOGLE SERVICES INIT] ❌ Google Services provider initialization failed: {Error}", initResult.Error?.Message);
                            _logger.LogWarning("[GOOGLE SERVICES INIT] Error details: {ErrorDetails}", initResult.Error?.ToString());
                            // Don't throw - this means provider needs authentication but that's handled by UI
                        }
                    }
                    else
                    {
                        _logger.LogWarning("[GOOGLE SERVICES INIT] ❌ Google OAuth client credentials not available - provider needs setup");
                        _logger.LogWarning("[GOOGLE SERVICES INIT] Client ID error: {ClientIdError}, Client Secret error: {ClientSecretError}",
                            clientIdResult.ErrorMessage, clientSecretResult.ErrorMessage);
                    }
                }
                else
                {
                    _logger.LogWarning("[GOOGLE SERVICES INIT] ❌ Google Services provider is not a typed provider - cannot initialize");
                    _logger.LogWarning("[GOOGLE SERVICES INIT] Provider type: {ProviderType}", googleServicesProvider.GetType().FullName);
                }
            }
            else
            {
                _logger.LogWarning("[GOOGLE SERVICES INIT] ❌ No unified Google Services provider instance registered - startup configuration issue");
            }

            _logger.LogInformation("[GOOGLE SERVICES INIT] Google Services provider initialization process completed");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "[GOOGLE SERVICES INIT] ❌ Exception during Google Services provider initialization");
            UpdateProgressWithError(StartupStep.InitializingGoogleServices, "Google Services provider initialization failed", ex.Message);
            throw new InvalidOperationException("Google Services provider initialization failed", ex);
        }
    }

    private async Task PerformHealthChecksAsync(CancellationToken cancellationToken)
    {
        try
        {
            _logger.LogDebug("Performing provider health checks");

            // Get comprehensive provider status from bridge service
            var allProviderStatuses = await _providerBridgeService.GetAllProviderStatusAsync();

            // Log status of each provider
            foreach (var statusPair in allProviderStatuses)
            {
                var providerName = statusPair.Key;
                var status = statusPair.Value;

                _logger.LogInformation("Provider {Provider}: {Status} (Healthy: {IsHealthy}, Setup Required: {RequiresSetup})",
                    providerName, status.Status, status.IsHealthy, status.RequiresSetup);
            }

            // Update provider status service with fresh data
            await _providerStatusService.RefreshProviderStatusAsync();

            // Note: We don't fail startup if providers are unhealthy or require setup
            // The provider status dashboard will handle guiding users through setup
            var healthyCount = allProviderStatuses.Count(kvp => kvp.Value.IsHealthy);
            var totalCount = allProviderStatuses.Count;

            _logger.LogInformation("Provider health check completed: {HealthyCount}/{TotalCount} providers healthy",
                healthyCount, totalCount);

            if (healthyCount == 0)
            {
                _logger.LogWarning("No providers are currently healthy - user setup will be required");
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Provider health checks failed");
            UpdateProgressWithError(StartupStep.CheckingProviderHealth, "Health checks failed", ex.Message);
            throw new InvalidOperationException("Health checks failed", ex);
        }
    }

    private void UpdateProgress(StartupStep step, string description, int completedSteps, bool isComplete = false)
    {
        var progress = new StartupProgress
        {
            CurrentStep = step,
            StepName = step.ToString(),
            StepDescription = description,
            CompletedSteps = completedSteps,
            TotalSteps = TotalSteps,
            ProgressPercentage = (double)completedSteps / TotalSteps * 100,
            IsComplete = isComplete,
            HasError = false
        };

        lock (_progressLock)
        {
            _currentProgress = progress;
        }

        OnProgressChanged(progress);
        _logger.LogDebug("Startup progress: {Step} - {Description} ({Completed}/{Total})",
            step, description, completedSteps, TotalSteps);
    }

    private void UpdateProgressWithError(StartupStep step, string description, string errorMessage)
    {
        var progress = new StartupProgress
        {
            CurrentStep = StartupStep.Failed,
            StepName = step.ToString(),
            StepDescription = description,
            CompletedSteps = _currentProgress.CompletedSteps,
            TotalSteps = TotalSteps,
            ProgressPercentage = (double)_currentProgress.CompletedSteps / TotalSteps * 100,
            IsComplete = false,
            HasError = true,
            ErrorMessage = errorMessage
        };

        lock (_progressLock)
        {
            _currentProgress = progress;
        }

        OnProgressChanged(progress);
    }

    private void OnProgressChanged(StartupProgress progress)
    {
        try
        {
            ProgressChanged?.Invoke(this, new StartupProgressChangedEventArgs { Progress = progress });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Exception firing progress changed event");
        }
    }

    private StartupResult CreateFailureResult(TimeSpan duration, StartupFailureReason reason, string errorMessage)
    {
        return new StartupResult
        {
            IsSuccess = false,
            Status = "Startup failed",
            FailureReason = reason,
            ErrorMessage = errorMessage,
            Duration = duration,
            Details = new Dictionary<string, object>
            {
                { "total_steps", TotalSteps },
                { "completed_steps", _currentProgress.CompletedSteps },
                { "failed_step", _currentProgress.CurrentStep }
            }
        };
    }
}