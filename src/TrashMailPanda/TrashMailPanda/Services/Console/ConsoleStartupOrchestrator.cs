using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.DependencyInjection;
using TrashMailPanda.Models.Console;
using TrashMailPanda.Shared;
using TrashMailPanda.Shared.Base;
using TrashMailPanda.Shared.Models;
using TrashMailPanda.Shared.Security;
using TrashMailPanda.Providers.Email;
using TrashMailPanda.Providers.Email.Models;
using TrashMailPanda.Providers.Storage;

namespace TrashMailPanda.Services.Console;

/// <summary>
/// Orchestrates sequential provider initialization during console application startup.
/// Implements single-threaded sequential initialization with health checks.
/// </summary>
public class ConsoleStartupOrchestrator
{
    private readonly IStorageProvider _storageProvider;
    private readonly IEmailProvider _emailProvider;
    private readonly ISecureStorageManager _secureStorage;
    private readonly TrashMailPandaDbContext _dbContext;
    private readonly ConsoleStatusDisplay _statusDisplay;
    private readonly ConsoleDisplayOptions _displayOptions;
    private readonly IServiceProvider _serviceProvider;
    private readonly ILogger<ConsoleStartupOrchestrator> _logger;

    private StartupSequenceState? _sequenceState;
    private CancellationTokenSource? _cancellationTokenSource;

    /// <summary>
    /// Initializes a new instance of the <see cref="ConsoleStartupOrchestrator"/> class.
    /// </summary>
    public ConsoleStartupOrchestrator(
        IStorageProvider storageProvider,
        IEmailProvider emailProvider,
        ISecureStorageManager secureStorage,
        TrashMailPandaDbContext dbContext,
        ConsoleStatusDisplay statusDisplay,
        IOptions<ConsoleDisplayOptions> displayOptions,
        IServiceProvider serviceProvider,
        ILogger<ConsoleStartupOrchestrator> logger)
    {
        _storageProvider = storageProvider ?? throw new ArgumentNullException(nameof(storageProvider));
        _emailProvider = emailProvider ?? throw new ArgumentNullException(nameof(emailProvider));
        _secureStorage = secureStorage ?? throw new ArgumentNullException(nameof(secureStorage));
        _dbContext = dbContext ?? throw new ArgumentNullException(nameof(dbContext));
        _statusDisplay = statusDisplay ?? throw new ArgumentNullException(nameof(statusDisplay));
        _displayOptions = displayOptions?.Value ?? throw new ArgumentNullException(nameof(displayOptions));
        _serviceProvider = serviceProvider ?? throw new ArgumentNullException(nameof(serviceProvider));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    /// <summary>
    /// Initializes all providers sequentially with health checks.
    /// </summary>
    /// <param name="cancellationToken">Cancellation token for the operation.</param>
    /// <returns>Startup sequence state with overall status.</returns>
    public async Task<StartupSequenceState> InitializeProvidersAsync(
        CancellationToken cancellationToken = default)
    {
        _cancellationTokenSource = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);

        // Register Ctrl+C handler
        global::System.Console.CancelKeyPress += OnCancelKeyPress;

        try
        {
            _sequenceState = new StartupSequenceState
            {
                ProviderStates = new List<ProviderInitializationState>
                {
                    new() { ProviderName = "Storage", ProviderType = ProviderType.Required },
                    new() { ProviderName = "Gmail", ProviderType = ProviderType.Required }
                },
                StartTime = DateTime.UtcNow
            };

            _logger.LogInformation("Starting provider initialization sequence");

            // Run EF migrations first so the encrypted_credentials table (and all others) exist
            // before CredentialEncryption tries to use them during SecureStorageManager.InitializeAsync().
            await _dbContext.Database.MigrateAsync(_cancellationTokenSource!.Token);

            // Ensure secure storage is initialized before reading Gmail credentials
            // (idempotent — safe to call even if CheckProviderSetupNeeded already initialized it)
            await _secureStorage.InitializeAsync();

            // Sequential initialization: Storage → Gmail
            await InitializeProviderAsync(_storageProvider, 0, _cancellationTokenSource.Token);

            // Handle Storage provider failure (required)
            if (_sequenceState.ProviderStates[0].Status != InitializationStatus.Ready &&
                _sequenceState.ProviderStates[0].ProviderType == ProviderType.Required)
            {
                var handled = await HandleProviderFailureAsync(0, _cancellationTokenSource.Token);
                if (!handled)
                {
                    // User chose to exit or failure unrecoverable
                    _sequenceState.CompletionTime = DateTime.UtcNow;
                    _sequenceState.OverallStatus = SequenceStatus.Failed;
                    _statusDisplay.DisplayStartupSummary(_sequenceState);
                    return _sequenceState;
                }
            }

            if (_sequenceState.ProviderStates[0].Status == InitializationStatus.Ready)
            {
                await InitializeProviderAsync(_emailProvider, 1, _cancellationTokenSource.Token);

                // Handle Gmail provider failure (required)
                if (_sequenceState.ProviderStates[1].Status != InitializationStatus.Ready &&
                    _sequenceState.ProviderStates[1].ProviderType == ProviderType.Required)
                {
                    var handled = await HandleProviderFailureAsync(1, _cancellationTokenSource.Token);
                    if (!handled)
                    {
                        // User chose to exit or failure unrecoverable
                        _sequenceState.CompletionTime = DateTime.UtcNow;
                        _sequenceState.OverallStatus = SequenceStatus.Failed;
                        _statusDisplay.DisplayStartupSummary(_sequenceState);
                        return _sequenceState;
                    }
                }
            }

            // Determine overall status
            _sequenceState.CompletionTime = DateTime.UtcNow;
            _sequenceState.OverallStatus = DetermineOverallStatus();

            _statusDisplay.DisplayStartupSummary(_sequenceState);

            return _sequenceState;
        }
        finally
        {
            global::System.Console.CancelKeyPress -= OnCancelKeyPress;
            _cancellationTokenSource?.Dispose();
        }
    }

    /// <summary>
    /// Initializes a single provider with timeout and health check.
    /// </summary>
    private async Task InitializeProviderAsync(
        object provider,
        int providerIndex,
        CancellationToken cancellationToken)
    {
        var state = _sequenceState!.ProviderStates[providerIndex];
        state.Status = InitializationStatus.Initializing;
        state.StartTime = DateTime.UtcNow;

        _statusDisplay.DisplayProviderInitializing(state);

        using var timeoutCts = new CancellationTokenSource(TimeSpan.FromSeconds(30));
        using var linkedCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, timeoutCts.Token);

        try
        {
            // Call initialization method based on provider type
            if (provider is IStorageProvider storageProvider)
            {
                // IStorageProvider uses InitAsync() - returns void Task
                await storageProvider.InitAsync();
            }
            else if (provider is IEmailProvider emailProvider)
            {
                // GmailEmailProvider extends BaseProvider<GmailProviderConfig> and must have
                // InitializeAsync(config) called to transition from Uninitialized → Ready before
                // any operations (including ConnectAsync) can be accepted.
                if (emailProvider is GmailEmailProvider gmailProvider)
                {
                    var clientIdResult = await _secureStorage.RetrieveCredentialAsync(GmailStorageKeys.CLIENT_ID);
                    var clientSecretResult = await _secureStorage.RetrieveCredentialAsync(GmailStorageKeys.CLIENT_SECRET);

                    var clientId = clientIdResult.IsSuccess ? clientIdResult.Value : null;
                    var clientSecret = clientSecretResult.IsSuccess ? clientSecretResult.Value : null;

                    if (string.IsNullOrEmpty(clientId) || string.IsNullOrEmpty(clientSecret))
                    {
                        state.Status = InitializationStatus.Failed;
                        state.Error = new ConfigurationError("Gmail client credentials not found in secure storage. Please run the configuration wizard.");
                        state.CompletionTime = DateTime.UtcNow;

                        _statusDisplay.DisplayProviderFailed(state);
                        _logger.LogError("Provider Gmail initialization failed: client credentials not configured");

                        return;
                    }

                    var gmailConfig = GmailProviderConfig.CreateProductionConfig(clientId, clientSecret);

                    var initResult = await gmailProvider.InitializeAsync(gmailConfig, linkedCts.Token);

                    if (!initResult.IsSuccess)
                    {
                        state.Status = InitializationStatus.Failed;
                        state.Error = initResult.Error;
                        state.CompletionTime = DateTime.UtcNow;

                        _statusDisplay.DisplayProviderFailed(state);
                        _logger.LogError("Provider {ProviderName} initialization failed: {Error}",
                            state.ProviderName, initResult.Error?.Message);

                        return;
                    }
                }
                else
                {
                    // Fallback for other IEmailProvider implementations
                    var connectResult = await emailProvider.ConnectAsync();

                    if (!connectResult.IsSuccess)
                    {
                        state.Status = InitializationStatus.Failed;
                        state.Error = connectResult.Error;
                        state.CompletionTime = DateTime.UtcNow;

                        _statusDisplay.DisplayProviderFailed(state);
                        _logger.LogError("Provider {ProviderName} connection failed: {Error}",
                            state.ProviderName, connectResult.Error?.Message);

                        return;
                    }
                }
            }
            else
            {
                throw new InvalidOperationException($"Unknown provider type: {provider.GetType().Name}");
            }

            state.CompletionTime = DateTime.UtcNow;
            _statusDisplay.DisplayProviderSuccess(state);

            // Perform health check
            await PerformHealthCheckAsync(provider, providerIndex, linkedCts.Token);
        }
        catch (OperationCanceledException) when (timeoutCts.IsCancellationRequested)
        {
            state.Status = InitializationStatus.Timeout;
            state.CompletionTime = DateTime.UtcNow;
            state.Error = new TimeoutError($"Provider initialization exceeded 30 second timeout");

            _statusDisplay.DisplayProviderFailed(state);
            _logger.LogError("Provider {ProviderName} initialization timed out", state.ProviderName);
        }
        catch (OperationCanceledException)
        {
            state.Status = InitializationStatus.Failed;
            state.CompletionTime = DateTime.UtcNow;
            _logger.LogWarning("Provider {ProviderName} initialization cancelled", state.ProviderName);
        }
        catch (Exception ex)
        {
            state.Status = InitializationStatus.Failed;
            state.CompletionTime = DateTime.UtcNow;
            state.Error = ex.ToProviderError($"Unexpected error initializing provider {state.ProviderName}");

            _statusDisplay.DisplayProviderFailed(state);
            _logger.LogError(ex, "Unexpected error initializing provider {ProviderName}", state.ProviderName);
        }
    }

    /// <summary>
    /// Performs health check on a provider immediately after initialization.
    /// </summary>
    private async Task PerformHealthCheckAsync(
        object provider,
        int providerIndex,
        CancellationToken cancellationToken)
    {
        var state = _sequenceState!.ProviderStates[providerIndex];
        state.Status = InitializationStatus.HealthChecking;

        using var timeoutCts = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        using var linkedCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, timeoutCts.Token);

        try
        {
            // IStorageProvider has no health check - mark as healthy
            if (provider is IStorageProvider)
            {
                state.HealthStatus = Models.Console.HealthStatus.Healthy;
                state.Status = InitializationStatus.Ready;
                _statusDisplay.DisplayHealthCheckStatus(state);
                return;
            }

            // IEmailProvider has HealthCheckAsync() -> Result<bool>
            if (provider is IEmailProvider emailProvider)
            {
                var healthResult = await emailProvider.HealthCheckAsync();

                if (!healthResult.IsSuccess || !healthResult.Value)
                {
                    state.Status = InitializationStatus.Failed;
                    state.HealthStatus = Models.Console.HealthStatus.Critical;
                    state.Error = healthResult.Error;

                    _statusDisplay.DisplayProviderFailed(state);
                    _logger.LogError("Provider {ProviderName} health check failed: {Error}",
                        state.ProviderName, healthResult.Error?.Message);
                    return;
                }

                // Health check passed
                state.HealthStatus = Models.Console.HealthStatus.Healthy;
                state.Status = InitializationStatus.Ready;
                _statusDisplay.DisplayHealthCheckStatus(state);
            }
        }
        catch (OperationCanceledException) when (timeoutCts.IsCancellationRequested)
        {
            state.Status = InitializationStatus.Timeout;
            state.HealthStatus = Models.Console.HealthStatus.Unknown;
            state.Error = new TimeoutError("Health check exceeded 10 second timeout");

            _statusDisplay.DisplayProviderFailed(state);
            _logger.LogError("Provider {ProviderName} health check timed out", state.ProviderName);
        }
    }

    /// <summary>
    /// Handles Ctrl+C cancellation.
    /// </summary>
    private void OnCancelKeyPress(object? sender, ConsoleCancelEventArgs e)
    {
        e.Cancel = true; // Prevent immediate termination

        _logger.LogWarning("Startup cancellation requested by user");

        if (_sequenceState != null)
        {
            _sequenceState.OverallStatus = SequenceStatus.Cancelled;
        }

        _cancellationTokenSource?.Cancel();
    }

    /// <summary>
    /// Determines the overall startup sequence status.
    /// </summary>
    private SequenceStatus DetermineOverallStatus()
    {
        if (_sequenceState == null)
        {
            return SequenceStatus.Failed;
        }

        if (_sequenceState.OverallStatus == SequenceStatus.Cancelled)
        {
            return SequenceStatus.Cancelled;
        }

        var requiredProviders = _sequenceState.ProviderStates
            .Where(p => p.ProviderType == ProviderType.Required);

        if (requiredProviders.All(p => p.Status == InitializationStatus.Ready))
        {
            return SequenceStatus.Completed;
        }

        return SequenceStatus.Failed;
    }

    /// <summary>
    /// Checks if provider setup is needed (e.g., missing configuration in secure storage).
    /// </summary>
    /// <returns>True if setup wizard is needed.</returns>
    public async Task<bool> CheckProviderSetupNeeded()
    {
        _logger.LogInformation("Checking if provider configuration exists");

        try
        {
            // Run EF migrations first so the encrypted_credentials table exists
            // before CredentialEncryption tries to use it during SecureStorageManager.InitializeAsync().
            await _dbContext.Database.MigrateAsync();

            // Ensure secure storage is initialized — it must be ready before the wizard runs
            // (same singleton instance is used by ConfigurationWizard for StoreCredentialAsync)
            var storageInitResult = await _secureStorage.InitializeAsync();
            if (!storageInitResult.IsSuccess)
            {
                _logger.LogError("Failed to initialize secure storage: {Error}", storageInitResult.ErrorMessage);
                return true; // Can't verify config — treat as setup needed
            }

            // Check if first-time setup has been completed
            var setupCompletedResult = await _secureStorage.CredentialExistsAsync("setup_completed");

            if (!setupCompletedResult.IsSuccess || !setupCompletedResult.Value)
            {
                _logger.LogInformation("First-time setup not completed - wizard required");
                return true;
            }

            // Verify required storage configuration
            var storagePathResult = await _secureStorage.CredentialExistsAsync("storage_database_path");
            if (!storagePathResult.IsSuccess || !storagePathResult.Value)
            {
                _logger.LogWarning("Storage configuration missing - wizard required");
                return true;
            }

            // Verify required Gmail OAuth credentials
            var gmailClientIdResult = await _secureStorage.CredentialExistsAsync("gmail_client_id");
            var gmailClientSecretResult = await _secureStorage.CredentialExistsAsync("gmail_client_secret");

            if (!gmailClientIdResult.IsSuccess || !gmailClientIdResult.Value ||
                !gmailClientSecretResult.IsSuccess || !gmailClientSecretResult.Value)
            {
                _logger.LogWarning("Gmail OAuth credentials missing - wizard required");
                return true;
            }

            _logger.LogInformation("All required configurations exist - skipping wizard");
            return false;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error checking provider configuration status");
            // If we can't verify configuration, assume wizard is needed for safety
            return true;
        }
    }

    /// <summary>
    /// Handles provider initialization failure with error recovery options.
    /// </summary>
    /// <param name="providerIndex">Index of the failed provider.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>True if failure was handled and provider is now ready, false if unrecoverable or user chose to exit.</returns>
    private async Task<bool> HandleProviderFailureAsync(int providerIndex, CancellationToken cancellationToken)
    {
        var state = _sequenceState!.ProviderStates[providerIndex];
        var provider = providerIndex == 0 ? (object)_storageProvider : _emailProvider;

        _logger.LogWarning("Handling failure for required provider: {ProviderName}", state.ProviderName);

        // Display provider-specific error guidance
        if (state.Error != null)
        {
            var (message, hint) = _statusDisplay.GetProviderSpecificErrorMessage(state.Error.ErrorCode, state.ProviderName);
            Spectre.Console.AnsiConsole.WriteLine();
            Spectre.Console.AnsiConsole.MarkupLine($"{ConsoleColors.PromptOption}⚠ {message}{ConsoleColors.Close}");
            Spectre.Console.AnsiConsole.MarkupLine($"{ConsoleColors.Dim}{hint}{ConsoleColors.Close}");
        }

        // Show error recovery menu
        var choice = _statusDisplay.DisplayErrorRecoveryMenu(state.ProviderName);

        if (choice.Contains("Retry"))
        {
            return await RetryProviderInitializationAsync(provider, providerIndex, cancellationToken);
        }
        else if (choice.Contains("Reconfigure"))
        {
            return await ReconfigureProviderAsync(providerIndex, cancellationToken);
        }
        else // Exit
        {
            _logger.LogInformation("User chose to exit after {ProviderName} failure", state.ProviderName);
            return false;
        }
    }

    /// <summary>
    /// Retries initialization for a specific provider.
    /// </summary>
    /// <param name="provider">The provider to retry.</param>
    /// <param name="providerIndex">Index of the provider.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>True if retry succeeded, false otherwise.</returns>
    private async Task<bool> RetryProviderInitializationAsync(
        object provider,
        int providerIndex,
        CancellationToken cancellationToken)
    {
        var state = _sequenceState!.ProviderStates[providerIndex];
        _logger.LogInformation("Retrying initialization for {ProviderName}", state.ProviderName);

        Spectre.Console.AnsiConsole.WriteLine();
        Spectre.Console.AnsiConsole.MarkupLine($"{ConsoleColors.Highlight}⟳{ConsoleColors.Close} Retrying provider initialization...");
        Spectre.Console.AnsiConsole.WriteLine();

        // Reset state for retry
        state.Status = InitializationStatus.NotStarted;
        state.Error = null;
        state.StartTime = null;
        state.CompletionTime = null;

        // Retry initialization
        await InitializeProviderAsync(provider, providerIndex, cancellationToken);

        if (state.Status == InitializationStatus.Ready)
        {
            _logger.LogInformation("Retry succeeded for {ProviderName}", state.ProviderName);
            return true;
        }

        _logger.LogWarning("Retry failed for {ProviderName}", state.ProviderName);

        // Offer another attempt
        Spectre.Console.AnsiConsole.WriteLine();
        if (Spectre.Console.AnsiConsole.Confirm($"{ConsoleColors.Warning}Retry again?{ConsoleColors.Close}", defaultValue: false))
        {
            return await RetryProviderInitializationAsync(provider, providerIndex, cancellationToken);
        }

        return false;
    }

    /// <summary>
    /// Re-invokes the configuration wizard for a specific provider.
    /// </summary>
    /// <param name="providerIndex">Index of the provider to reconfigure.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>True if reconfiguration succeeded and provider is ready, false otherwise.</returns>
    private async Task<bool> ReconfigureProviderAsync(int providerIndex, CancellationToken cancellationToken)
    {
        var state = _sequenceState!.ProviderStates[providerIndex];
        _logger.LogInformation("Starting reconfiguration for {ProviderName}", state.ProviderName);

        Spectre.Console.AnsiConsole.WriteLine();
        Spectre.Console.AnsiConsole.MarkupLine($"{ConsoleColors.Highlight}⚙{ConsoleColors.Close} Reconfiguring [bold]{state.ProviderName}[/] provider...");
        Spectre.Console.AnsiConsole.WriteLine();

        try
        {
            // Get ConfigurationWizard from DI
            var wizard = _serviceProvider.GetRequiredService<ConfigurationWizard>();

            // Run wizard for specific provider
            // For now, we run the full wizard - in future could add provider-specific methods
            var wizardResult = await wizard.RunAsync(cancellationToken);

            if (!wizardResult)
            {
                _logger.LogWarning("User cancelled reconfiguration for {ProviderName}", state.ProviderName);
                return false;
            }

            // Configuration updated - retry initialization
            _logger.LogInformation("Reconfiguration completed, retrying initialization for {ProviderName}", state.ProviderName);
            var provider = providerIndex == 0 ? (object)_storageProvider : _emailProvider;
            return await RetryProviderInitializationAsync(provider, providerIndex, cancellationToken);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error during reconfiguration for {ProviderName}", state.ProviderName);
            Spectre.Console.AnsiConsole.MarkupLine($"{ConsoleColors.Error}✗ Reconfiguration failed: {ex.Message}{ConsoleColors.Close}");
            return false;
        }
    }
}
