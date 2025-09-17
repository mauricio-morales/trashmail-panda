using Microsoft.Extensions.Logging;
using TrashMailPanda.Models;
using TrashMailPanda.Providers.Email;
using TrashMailPanda.Shared;
using TrashMailPanda.Shared.Base;
using TrashMailPanda.Shared.Models;
using TrashMailPanda.Shared.Security;

namespace TrashMailPanda.Services;

/// <summary>
/// Bridge service that connects legacy providers to the new IProvider interface
/// Handles zero-configuration setup and separates OAuth client credentials from session tokens
/// </summary>
public class ProviderBridgeService : IProviderBridgeService
{
    private readonly IEmailProvider? _emailProvider;
    private readonly ILLMProvider? _llmProvider;
    private readonly IStorageProvider _storageProvider;
    private readonly IContactsProvider? _contactsProvider;
    private readonly ISecureStorageManager _secureStorageManager;
    private readonly ILogger<ProviderBridgeService> _logger;

    // Provider display information for UI
    private static readonly Dictionary<string, ProviderDisplayInfo> _providerDisplayInfo = new()
    {
        ["Gmail"] = new()
        {
            Name = "Gmail",
            DisplayName = "Gmail",
            Description = "Connect to your Gmail account for email processing and cleanup",
            Type = ProviderType.Email,
            IsRequired = true,
            AllowsMultiple = false,
            Icon = "📧",
            Complexity = SetupComplexity.Moderate,
            EstimatedSetupTimeMinutes = 3,
            Prerequisites = "Gmail account and web browser access"
        },
        ["OpenAI"] = new()
        {
            Name = "OpenAI",
            DisplayName = "OpenAI GPT",
            Description = "AI-powered email classification and smart processing",
            Type = ProviderType.LLM,
            IsRequired = true,
            AllowsMultiple = false,
            Icon = "🤖",
            Complexity = SetupComplexity.Simple,
            EstimatedSetupTimeMinutes = 2,
            Prerequisites = "OpenAI API account and API key"
        },
        ["SQLite"] = new()
        {
            Name = "SQLite",
            DisplayName = "Local Storage",
            Description = "Secure local database for storing email data and settings",
            Type = ProviderType.Storage,
            IsRequired = true,
            AllowsMultiple = false,
            Icon = "💾",
            Complexity = SetupComplexity.Simple,
            EstimatedSetupTimeMinutes = 0,
            Prerequisites = "None - automatically configured"
        },
        ["Contacts"] = new()
        {
            Name = "Contacts",
            DisplayName = "Google Contacts",
            Description = "Enhanced email classification using Google Contacts for trust signals",
            Type = ProviderType.Contacts,
            IsRequired = false,
            AllowsMultiple = false,
            Icon = "👥",
            Complexity = SetupComplexity.Moderate,
            EstimatedSetupTimeMinutes = 2,
            Prerequisites = "Gmail already configured with expanded permissions"
        }
    };

    public ProviderBridgeService(
        IStorageProvider storageProvider,
        ISecureStorageManager secureStorageManager,
        ILogger<ProviderBridgeService> logger,
        IEmailProvider? emailProvider = null,
        ILLMProvider? llmProvider = null,
        IContactsProvider? contactsProvider = null)
    {
        _emailProvider = emailProvider; // Can be null - will be set after secrets are available
        _llmProvider = llmProvider; // Can be null - will be set after secrets are available
        _storageProvider = storageProvider ?? throw new ArgumentNullException(nameof(storageProvider));
        _contactsProvider = contactsProvider; // Can be null - will be set after OAuth expansion
        _secureStorageManager = secureStorageManager ?? throw new ArgumentNullException(nameof(secureStorageManager));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    /// <summary>
    /// Get provider display information for UI
    /// </summary>
    public IReadOnlyDictionary<string, ProviderDisplayInfo> GetProviderDisplayInfo()
    {
        return _providerDisplayInfo;
    }

    /// <summary>
    /// Get Gmail provider status with separated credential handling
    /// </summary>
    public async Task<Result<ProviderStatus>> GetEmailProviderStatusAsync()
    {
        try
        {
            _logger.LogDebug("Checking Gmail provider status");

            var status = new ProviderStatus
            {
                Name = "Gmail",
                LastCheck = DateTime.UtcNow,
                Details = new Dictionary<string, object>
                {
                    { "type", "Gmail" },
                    { "provider_version", "1.0.0" }
                }
            };

            // Check if OAuth client credentials are configured
            var hasClientCredentials = await HasGmailClientCredentialsAsync();

            // Check if session tokens exist and are valid
            var hasValidSession = await HasValidGmailSessionAsync();

            // Determine provider state based on credential availability
            if (!hasClientCredentials)
            {
                // No OAuth client configured - needs initial setup
                status = status with
                {
                    IsHealthy = false,
                    IsInitialized = false,
                    RequiresSetup = true,
                    Status = "OAuth Setup Required",
                    ErrorMessage = "Gmail OAuth client credentials not configured"
                };
            }
            else if (!hasValidSession)
            {
                // Client configured but no valid session - needs user authentication
                status = status with
                {
                    IsHealthy = false,
                    IsInitialized = true, // Client is configured
                    RequiresSetup = false, // Don't need client setup
                    Status = "Authentication Required",
                    ErrorMessage = "Gmail session expired - please sign in again"
                };
            }
            else
            {
                // Both client and session are available - use provider health check like Contacts does
                try
                {
                    _logger.LogDebug("Performing health check for Gmail provider");

                    // Use the email provider's health check method (same approach as Contacts)
                    Result<HealthCheckResult> healthCheckResult;
                    if (_emailProvider is GmailEmailProvider gmailProvider)
                    {
                        // Gmail provider's HealthCheckAsync returns Result<bool>, so we need to convert it
                        var boolHealthResult = await gmailProvider.HealthCheckAsync();
                        if (boolHealthResult.IsSuccess)
                        {
                            healthCheckResult = Result<HealthCheckResult>.Success(
                                boolHealthResult.Value
                                    ? HealthCheckResult.Healthy("Gmail health check passed")
                                    : HealthCheckResult.Unhealthy("Gmail health check failed"));
                        }
                        else
                        {
                            healthCheckResult = Result<HealthCheckResult>.Failure(boolHealthResult.Error);
                        }
                    }
                    else
                    {
                        // Fallback to connectivity test if not a GmailEmailProvider
                        var connectivityResult = await TestGmailConnectivityAsync();
                        if (connectivityResult.IsSuccess)
                        {
                            healthCheckResult = Result<HealthCheckResult>.Success(
                                HealthCheckResult.Healthy("Gmail connectivity verified"));
                        }
                        else
                        {
                            healthCheckResult = Result<HealthCheckResult>.Failure(connectivityResult.Error);
                        }
                    }

                    if (healthCheckResult.IsSuccess)
                    {
                        var healthResult = healthCheckResult.Value;
                        status = status with
                        {
                            IsHealthy = healthResult.Status == HealthStatus.Healthy,
                            IsInitialized = true,
                            RequiresSetup = false,
                            Status = healthResult.Status == HealthStatus.Healthy ? "Connected" : "Authentication Required",
                            ErrorMessage = healthResult.Status != HealthStatus.Healthy ? healthResult.Description : null,
                            Details = new Dictionary<string, object>
                            {
                                { "type", "Gmail" },
                                { "last_check", DateTime.UtcNow },
                                { "health_status", healthResult.Status.ToString() },
                                { "health_description", healthResult.Description ?? "No details" }
                            }
                        };

                        _logger.LogDebug("Gmail provider health check result: {Status}, {Description}",
                            healthResult.Status, healthResult.Description);

                        // If healthy, get authenticated user info
                        if (healthResult.Status == HealthStatus.Healthy && _emailProvider != null)
                        {
                            try
                            {
                                var userResult = await _emailProvider.GetAuthenticatedUserAsync();
                                if (userResult.IsSuccess && userResult.Value != null)
                                {
                                    status = status with
                                    {
                                        AuthenticatedUser = userResult.Value
                                    };
                                    status.Details["user_email"] = userResult.Value.Email;
                                }
                            }
                            catch (Exception ex)
                            {
                                _logger.LogWarning(ex, "Failed to get authenticated user info from Gmail provider");
                            }
                        }
                    }
                    else
                    {
                        // Health check failed
                        status = status with
                        {
                            IsHealthy = false,
                            IsInitialized = false,
                            RequiresSetup = false,
                            Status = "Authentication Required",
                            ErrorMessage = healthCheckResult.Error.Message,
                            Details = new Dictionary<string, object>
                            {
                                { "type", "Gmail" },
                                { "last_check", DateTime.UtcNow },
                                { "error", healthCheckResult.Error.Message }
                            }
                        };

                        _logger.LogDebug("Gmail provider health check failed: {Error}",
                            healthCheckResult.Error.Message);
                    }
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "Exception during Gmail provider health check");
                    status = status with
                    {
                        IsHealthy = false,
                        IsInitialized = false,
                        RequiresSetup = false,
                        Status = "Authentication Required",
                        ErrorMessage = ex.Message,
                        Details = new Dictionary<string, object>
                        {
                            { "type", "Gmail" },
                            { "last_check", DateTime.UtcNow },
                            { "error", ex.Message }
                        }
                    };
                }
            }

            _logger.LogDebug("Gmail provider status: {Status} (Healthy: {IsHealthy})", status.Status, status.IsHealthy);
            return Result<ProviderStatus>.Success(status);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Exception checking Gmail provider status");
            return Result<ProviderStatus>.Failure(new ProcessingError($"Status check failed: {ex.Message}"));
        }
    }

    /// <summary>
    /// Get OpenAI provider status
    /// </summary>
    public async Task<Result<ProviderStatus>> GetLLMProviderStatusAsync()
    {
        try
        {
            _logger.LogDebug("Checking OpenAI provider status");

            var status = new ProviderStatus
            {
                Name = "OpenAI",
                LastCheck = DateTime.UtcNow,
                Details = new Dictionary<string, object>
                {
                    { "type", "OpenAI" },
                    { "model", "gpt-4o-mini" }
                }
            };

            // Check if API key is configured
            var apiKeyResult = await _secureStorageManager.RetrieveCredentialAsync(ProviderCredentialTypes.OpenAIApiKey);

            if (!apiKeyResult.IsSuccess || string.IsNullOrEmpty(apiKeyResult.Value))
            {
                status = status with
                {
                    IsHealthy = false,
                    IsInitialized = false,
                    RequiresSetup = true,
                    Status = "API Key Required",
                    ErrorMessage = "OpenAI API key not configured"
                };
            }
            else
            {
                // Test API key validity
                var connectivityResult = await TestOpenAIConnectivityAsync(apiKeyResult.Value);

                status = status with
                {
                    IsHealthy = connectivityResult.IsSuccess,
                    IsInitialized = true,
                    RequiresSetup = !connectivityResult.IsSuccess, // If test fails, may need reconfiguration
                    Status = connectivityResult.IsSuccess ? "Connected" : "API Key Invalid",
                    ErrorMessage = connectivityResult.IsSuccess ? null : connectivityResult.Error?.Message
                };

                if (connectivityResult.IsSuccess)
                {
                    // Mask API key for display
                    var maskedKey = MaskApiKey(apiKeyResult.Value);
                    status.Details["api_key"] = maskedKey;
                    status.Details["last_test"] = DateTime.UtcNow;
                }
            }

            _logger.LogDebug("OpenAI provider status: {Status} (Healthy: {IsHealthy})", status.Status, status.IsHealthy);
            return Result<ProviderStatus>.Success(status);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Exception checking OpenAI provider status");
            return Result<ProviderStatus>.Failure(new ProcessingError($"Status check failed: {ex.Message}"));
        }
    }

    /// <summary>
    /// Get SQLite storage provider status
    /// </summary>
    public async Task<Result<ProviderStatus>> GetStorageProviderStatusAsync()
    {
        try
        {
            _logger.LogDebug("Checking SQLite provider status");

            // Storage provider should always be available - test database connectivity
            var connectivityResult = await TestStorageConnectivityAsync();

            var status = new ProviderStatus
            {
                Name = "SQLite",
                LastCheck = DateTime.UtcNow,
                IsHealthy = connectivityResult.IsSuccess,
                IsInitialized = true, // Storage is always initialized
                RequiresSetup = false, // Storage never requires user setup
                Status = connectivityResult.IsSuccess ? "Connected" : "Database Error",
                ErrorMessage = connectivityResult.IsSuccess ? null : connectivityResult.Error?.Message,
                Details = new Dictionary<string, object>
                {
                    { "type", "SQLite" },
                    { "encrypted", true }
                }
            };

            if (connectivityResult.IsSuccess && connectivityResult.Value != null)
            {
                status.Details["database_info"] = connectivityResult.Value;
            }

            _logger.LogDebug("SQLite provider status: {Status} (Healthy: {IsHealthy})", status.Status, status.IsHealthy);
            return Result<ProviderStatus>.Success(status);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Exception checking SQLite provider status");
            return Result<ProviderStatus>.Failure(new ProcessingError($"Status check failed: {ex.Message}"));
        }
    }

    /// <summary>
    /// Get Contacts provider status with OAuth scope expansion detection
    /// </summary>
    public async Task<Result<ProviderStatus>> GetContactsProviderStatusAsync()
    {
        try
        {
            _logger.LogDebug("Checking Contacts provider status");

            var status = new ProviderStatus
            {
                Name = "Contacts",
                LastCheck = DateTime.UtcNow,
                Details = new Dictionary<string, object>
                {
                    { "type", "Contacts" },
                    { "provider_version", "1.0.0" },
                    { "source", "Google People API" }
                }
            };

            // Check if Google OAuth client credentials exist (shared with Gmail)
            // Both Gmail and Contacts use the same Google credentials now
            var hasGoogleCredentials = await HasContactsCredentialsAsync(); // This now checks Google credentials
            if (!hasGoogleCredentials)
            {
                status = status with
                {
                    IsHealthy = false,
                    IsInitialized = false,
                    RequiresSetup = true,
                    Status = "Requires Gmail Setup",
                    ErrorMessage = "Google OAuth client credentials must be configured first"
                };

                _logger.LogDebug("Contacts provider requires Google OAuth client credentials to be configured");
                return Result<ProviderStatus>.Success(status);
            }

            // Check if ContactsProvider OAuth credentials are configured separately
            var hasContactsCredentials = await HasContactsCredentialsAsync();

            // Detect if OAuth scope expansion is needed
            var needsScopeExpansion = await DetectOAuthScopeExpansionAsync();

            if (needsScopeExpansion && !hasContactsCredentials)
            {
                // Gmail is configured but needs scope expansion for Contacts
                status = status with
                {
                    IsHealthy = false,
                    IsInitialized = false,
                    RequiresSetup = true,
                    Status = "OAuth Scope Expansion Required",
                    ErrorMessage = "Gmail permissions need to be expanded to include Google People API access for Contacts"
                };
            }
            else if (hasContactsCredentials)
            {
                // Contacts credentials are configured - test connectivity
                var connectivityResult = await TestContactsConnectivityAsync();

                // Determine appropriate status based on error type
                string failureStatus;
                if (!connectivityResult.IsSuccess)
                {
                    // Check if this is an authentication error
                    var errorMessage = connectivityResult.Error?.Message ?? "";
                    if (errorMessage.Contains("not available") || errorMessage.Contains("validate credentials") ||
                        errorMessage.Contains("authentication", StringComparison.OrdinalIgnoreCase) ||
                        errorMessage.Contains("unauthorized", StringComparison.OrdinalIgnoreCase))
                    {
                        failureStatus = "Authentication Required";
                    }
                    else
                    {
                        failureStatus = "Connection Failed";
                    }
                }
                else
                {
                    failureStatus = "Connected";
                }

                status = status with
                {
                    IsHealthy = connectivityResult.IsSuccess,
                    IsInitialized = true,
                    RequiresSetup = false,
                    Status = failureStatus,
                    ErrorMessage = connectivityResult.IsSuccess ? null : connectivityResult.Error?.Message
                };

                if (connectivityResult.IsSuccess)
                {
                    status.Details["contacts_enabled"] = true;
                    status.Details["last_sync"] = "Not implemented"; // Would be actual sync time
                }
            }
            else
            {
                // No ContactsProvider credentials and no scope expansion needed
                status = status with
                {
                    IsHealthy = false,
                    IsInitialized = false,
                    RequiresSetup = true,
                    Status = "Setup Required",
                    ErrorMessage = "Contacts provider has not been configured"
                };
            }

            _logger.LogDebug("Contacts provider status: {Status} (Healthy: {IsHealthy})", status.Status, status.IsHealthy);
            return Result<ProviderStatus>.Success(status);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Exception checking Contacts provider status");
            return Result<ProviderStatus>.Failure(new ProcessingError($"Status check failed: {ex.Message}"));
        }
    }

    /// <summary>
    /// Get status for all providers
    /// </summary>
    public async Task<Dictionary<string, ProviderStatus>> GetAllProviderStatusAsync()
    {
        var results = new Dictionary<string, ProviderStatus>();

        // Execute provider status checks in parallel
        var tasks = new[]
        {
            GetEmailProviderStatusAsync(),
            GetLLMProviderStatusAsync(),
            GetStorageProviderStatusAsync(),
            GetContactsProviderStatusAsync()
        };

        var statuses = await Task.WhenAll(tasks);

        // Collect results
        for (int i = 0; i < statuses.Length; i++)
        {
            if (statuses[i].IsSuccess)
            {
                var providerStatus = statuses[i].Value!;
                results[providerStatus.Name] = providerStatus;
            }
            else
            {
                // Create error status for failed provider
                var providerName = i switch
                {
                    0 => "Gmail",
                    1 => "OpenAI",
                    2 => "SQLite",
                    3 => "Contacts",
                    _ => "Unknown"
                };

                results[providerName] = new ProviderStatus
                {
                    Name = providerName,
                    IsHealthy = false,
                    IsInitialized = false,
                    RequiresSetup = true,
                    Status = "Error",
                    ErrorMessage = statuses[i].Error?.Message ?? "Unknown error",
                    LastCheck = DateTime.UtcNow
                };
            }
        }

        return results;
    }

    /// <summary>
    /// Check if Google OAuth client credentials are configured (shared by Gmail, Contacts, etc.)
    /// </summary>
    private async Task<bool> HasGmailClientCredentialsAsync()
    {
        try
        {
            var clientIdResult = await _secureStorageManager.RetrieveCredentialAsync(ProviderCredentialTypes.GoogleClientId);
            var clientSecretResult = await _secureStorageManager.RetrieveCredentialAsync(ProviderCredentialTypes.GoogleClientSecret);

            return clientIdResult.IsSuccess && !string.IsNullOrEmpty(clientIdResult.Value) &&
                   clientSecretResult.IsSuccess && !string.IsNullOrEmpty(clientSecretResult.Value);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Exception checking Google OAuth client credentials");
            return false;
        }
    }

    /// <summary>
    /// Check if Gmail session tokens exist and are valid
    /// </summary>
    private async Task<bool> HasValidGmailSessionAsync()
    {
        try
        {
            var accessTokenResult = await _secureStorageManager.RetrieveCredentialAsync(ProviderCredentialTypes.GoogleAccessToken);
            var refreshTokenResult = await _secureStorageManager.RetrieveCredentialAsync(ProviderCredentialTypes.GoogleRefreshToken);

            // Need at least a refresh token to maintain session
            if (!refreshTokenResult.IsSuccess || string.IsNullOrEmpty(refreshTokenResult.Value))
            {
                return false;
            }

            // Check token expiry if available
            var expiryResult = await _secureStorageManager.RetrieveCredentialAsync(ProviderCredentialTypes.GoogleTokenExpiry);
            if (expiryResult.IsSuccess && DateTime.TryParse(expiryResult.Value, out var expiry))
            {
                // Check if access token is still valid (with 5-minute buffer)
                if (expiry > DateTime.UtcNow.AddMinutes(5))
                {
                    // Access token is still valid
                    return true;
                }
                // Access token expired, but we have refresh token so provider can handle refresh
                // Return true to allow connectivity test, which will trigger refresh if needed
                return true;
            }

            // Access token exists and we have refresh capability
            return accessTokenResult.IsSuccess && !string.IsNullOrEmpty(accessTokenResult.Value);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Exception checking Gmail session validity");
            return false;
        }
    }

    /// <summary>
    /// Test Gmail connectivity and get authenticated user
    /// </summary>
    private async Task<Result<string>> TestGmailConnectivityAsync()
    {
        try
        {
            // Call actual Gmail provider to get authenticated user info
            if (_emailProvider != null)
            {
                var healthResult = await _emailProvider.HealthCheckAsync();
                if (!healthResult.IsSuccess)
                {
                    return Result<string>.Failure(healthResult.Error);
                }

                // Get authenticated user info from Gmail provider
                var authenticatedUserResult = await _emailProvider.GetAuthenticatedUserAsync();
                if (authenticatedUserResult.IsSuccess && authenticatedUserResult.Value != null && !string.IsNullOrEmpty(authenticatedUserResult.Value.Email))
                {
                    // Store user email in credentials for future reference
                    await _secureStorageManager.StoreCredentialAsync(ProviderCredentialTypes.GoogleUserEmail, authenticatedUserResult.Value.Email);

                    return Result<string>.Success(authenticatedUserResult.Value.Email);
                }
            }

            // Fallback: try to get previously stored email
            var emailResult = await _secureStorageManager.RetrieveCredentialAsync(ProviderCredentialTypes.GoogleUserEmail);
            if (emailResult.IsSuccess && !string.IsNullOrEmpty(emailResult.Value))
            {
                return Result<string>.Success(emailResult.Value);
            }

            return Result<string>.Failure(new NetworkError("Gmail provider not available or user not authenticated"));
        }
        catch (Exception ex)
        {
            return Result<string>.Failure(new NetworkError($"Gmail connectivity test failed: {ex.Message}"));
        }
    }

    /// <summary>
    /// Test OpenAI API connectivity
    /// </summary>
    private async Task<Result<bool>> TestOpenAIConnectivityAsync(string apiKey)
    {
        try
        {
            // TODO: Call actual OpenAI provider health check method
            // For now, validate API key format and simulate API test
            if (!apiKey.StartsWith("sk-", StringComparison.Ordinal) || apiKey.Length < 20)
            {
                return Result<bool>.Failure(new ValidationError("Invalid API key format"));
            }

            await Task.Delay(100); // Simulate API call
            return Result<bool>.Success(true);
        }
        catch (Exception ex)
        {
            return Result<bool>.Failure(new NetworkError($"OpenAI connectivity test failed: {ex.Message}"));
        }
    }

    /// <summary>
    /// Test SQLite storage connectivity
    /// </summary>
    private async Task<Result<Dictionary<string, object>>> TestStorageConnectivityAsync()
    {
        try
        {
            // TODO: Call actual storage provider health check method
            await Task.Delay(50); // Simulate database query

            var dbInfo = new Dictionary<string, object>
            {
                { "connection_status", "Connected" },
                { "encryption", "SQLCipher" },
                { "version", "1.0.0" }
            };

            return Result<Dictionary<string, object>>.Success(dbInfo);
        }
        catch (Exception ex)
        {
            return Result<Dictionary<string, object>>.Failure(new StorageError($"Storage connectivity test failed: {ex.Message}"));
        }
    }

    /// <summary>
    /// Mask API key for secure display
    /// </summary>
    private static string MaskApiKey(string apiKey)
    {
        if (string.IsNullOrEmpty(apiKey) || apiKey.Length <= 8)
        {
            return "sk-****";
        }

        return $"sk-****{apiKey[^4..]}";
    }

    /// <summary>
    /// Check if Google OAuth client credentials are configured (shared by Gmail, Contacts, etc.)
    /// </summary>
    private async Task<bool> HasContactsCredentialsAsync()
    {
        try
        {
            var clientIdResult = await _secureStorageManager.RetrieveCredentialAsync(ProviderCredentialTypes.GoogleClientId);
            var clientSecretResult = await _secureStorageManager.RetrieveCredentialAsync(ProviderCredentialTypes.GoogleClientSecret);

            return clientIdResult.IsSuccess && !string.IsNullOrEmpty(clientIdResult.Value) &&
                   clientSecretResult.IsSuccess && !string.IsNullOrEmpty(clientSecretResult.Value);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Exception checking Google OAuth client credentials");
            return false;
        }
    }

    /// <summary>
    /// Detect if OAuth scope expansion is needed for ContactsProvider
    /// This determines if existing Gmail OAuth tokens need to be expanded to include Google People API scopes
    /// </summary>
    private async Task<bool> DetectOAuthScopeExpansionAsync()
    {
        try
        {
            // If Gmail tokens exist but don't have the required scopes for Google People API,
            // we need scope expansion rather than separate OAuth flow

            var gmailTokenResult = await _secureStorageManager.RetrieveCredentialAsync(ProviderCredentialTypes.GoogleRefreshToken);
            if (!gmailTokenResult.IsSuccess || string.IsNullOrEmpty(gmailTokenResult.Value))
            {
                // No Gmail tokens - no scope expansion possible
                return false;
            }

            // Check if we already have separate ContactsProvider tokens
            var contactsTokenResult = await _secureStorageManager.RetrieveCredentialAsync(ProviderCredentialTypes.ContactsRefreshToken);
            if (contactsTokenResult.IsSuccess && !string.IsNullOrEmpty(contactsTokenResult.Value))
            {
                // Already have separate Contacts tokens - no expansion needed
                return false;
            }

            // Gmail tokens exist but no Contacts tokens - scope expansion would be beneficial
            // In a full implementation, we'd also check the actual scopes in the Gmail tokens
            _logger.LogDebug("Gmail tokens exist without Contacts tokens - scope expansion recommended");
            return true;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Exception detecting OAuth scope expansion need");
            return false;
        }
    }

    /// <summary>
    /// Test ContactsProvider connectivity
    /// </summary>
    private async Task<Result<bool>> TestContactsConnectivityAsync()
    {
        try
        {
            // Test actual ContactsProvider health check
            if (_contactsProvider != null)
            {
                var healthResult = await _contactsProvider.GetContactSignalAsync("test@example.com");
                return healthResult.IsSuccess ? Result<bool>.Success(true) : Result<bool>.Failure(healthResult.Error);
            }

            // Fallback: if ContactsProvider is not available, credentials cannot be tested
            // This should fail since we can't validate OAuth tokens without the provider
            return Result<bool>.Failure(new ValidationError("ContactsProvider not available - cannot validate credentials"));
        }
        catch (Exception ex)
        {
            return Result<bool>.Failure(new NetworkError($"Contacts connectivity test failed: {ex.Message}"));
        }
    }
}

/// <summary>
/// Interface for provider bridge service
/// </summary>
public interface IProviderBridgeService
{
    /// <summary>
    /// Get provider display information for UI
    /// </summary>
    IReadOnlyDictionary<string, ProviderDisplayInfo> GetProviderDisplayInfo();

    /// <summary>
    /// Get email provider status
    /// </summary>
    Task<Result<ProviderStatus>> GetEmailProviderStatusAsync();

    /// <summary>
    /// Get LLM provider status
    /// </summary>
    Task<Result<ProviderStatus>> GetLLMProviderStatusAsync();

    /// <summary>
    /// Get storage provider status
    /// </summary>
    Task<Result<ProviderStatus>> GetStorageProviderStatusAsync();

    /// <summary>
    /// Get contacts provider status
    /// </summary>
    Task<Result<ProviderStatus>> GetContactsProviderStatusAsync();

    /// <summary>
    /// Get status for all providers
    /// </summary>
    Task<Dictionary<string, ProviderStatus>> GetAllProviderStatusAsync();
}