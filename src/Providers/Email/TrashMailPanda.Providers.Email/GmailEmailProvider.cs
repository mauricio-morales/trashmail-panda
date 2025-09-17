using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Google.Apis.Auth.OAuth2;
using Google.Apis.Auth.OAuth2.Flows;
using Google.Apis.Auth.OAuth2.Responses;
using Google.Apis.Gmail.v1;
using Google.Apis.Gmail.v1.Data;
using Google.Apis.Services;
using Google.Apis.Util.Store;
using Google.Apis.Requests;
using Google;
using TrashMailPanda.Shared;
using TrashMailPanda.Shared.Base;
using TrashMailPanda.Shared.Models;
using TrashMailPanda.Shared.Security;
using TrashMailPanda.Providers.Email.Models;
using TrashMailPanda.Providers.Email.Services;

namespace TrashMailPanda.Providers.Email;

/// <summary>
/// Gmail implementation of IEmailProvider using BaseProvider pattern
/// Provides Gmail-specific email operations with robust error handling and rate limiting
/// 
/// IMPORTANT - BATCH OPERATIONS LIMITATION:
/// Gmail API does not provide true bulk message retrieval operations. The "batch" methods 
/// in this provider use HTTP batch requests (Google.Apis.Requests.BatchRequest) to bundle 
/// multiple individual API calls into single HTTP requests. This improves network performance 
/// by reducing round-trips but does NOT reduce quota consumption - each message still costs 
/// 5 quota units regardless of batching.
/// 
/// Available Gmail API batch operations are limited to:
/// - batchModify: Modify labels on multiple messages
/// - batchDelete: Delete multiple messages
/// 
/// For message retrieval, only individual Users.Messages.Get requests are available,
/// which this provider optimizes using HTTP batching for better network performance.
/// </summary>
public class GmailEmailProvider : BaseProvider<GmailProviderConfig>, IEmailProvider
{
    private readonly ISecureStorageManager _secureStorageManager;
    private readonly IGmailRateLimitHandler _rateLimitHandler;
    private readonly IDataStore _dataStore;
    private readonly ISecurityAuditLogger _securityAuditLogger;
    private readonly IGoogleOAuthService _googleOAuthService;
    private GmailService? _gmailService;
    private UserCredential? _credential;

    /// <summary>
    /// Gets the provider name
    /// </summary>
    public override string Name => "Gmail";

    /// <summary>
    /// Gets the provider version
    /// </summary>
    public override string Version => "1.0.0";

    /// <summary>
    /// Initializes a new instance of the GmailEmailProvider
    /// </summary>
    /// <param name="secureStorageManager">Secure storage manager for OAuth tokens</param>
    /// <param name="rateLimitHandler">Rate limiting handler for API calls</param>
    /// <param name="dataStore">Secure data store for OAuth token persistence</param>
    /// <param name="securityAuditLogger">Security audit logger for credential operations</param>
    /// <param name="logger">Logger for the provider</param>
    public GmailEmailProvider(
        ISecureStorageManager secureStorageManager,
        IGmailRateLimitHandler rateLimitHandler,
        IDataStore dataStore,
        ISecurityAuditLogger securityAuditLogger,
        IGoogleOAuthService googleOAuthService,
        ILogger<GmailEmailProvider> logger)
        : base(logger)
    {
        _secureStorageManager = secureStorageManager ?? throw new ArgumentNullException(nameof(secureStorageManager));
        _rateLimitHandler = rateLimitHandler ?? throw new ArgumentNullException(nameof(rateLimitHandler));
        _dataStore = dataStore ?? throw new ArgumentNullException(nameof(dataStore));
        _securityAuditLogger = securityAuditLogger ?? throw new ArgumentNullException(nameof(securityAuditLogger));
        _googleOAuthService = googleOAuthService ?? throw new ArgumentNullException(nameof(googleOAuthService));
    }

    #region BaseProvider Implementation

    /// <summary>
    /// Performs Gmail-specific initialization including OAuth authentication
    /// </summary>
    /// <param name="config">Gmail provider configuration</param>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>A result indicating success or failure</returns>
    protected override async Task<Result<bool>> PerformInitializationAsync(GmailProviderConfig config, CancellationToken cancellationToken)
    {
        try
        {
            // Initialize secure storage
            var storageResult = await _secureStorageManager.InitializeAsync();
            if (!storageResult.IsSuccess)
            {
                return Result<bool>.Failure(new InitializationError(
                    $"Failed to initialize secure storage: {storageResult.ErrorMessage}"));
            }

            // Attempt to retrieve existing credentials
            var credentialResult = await TryRetrieveStoredCredentialsAsync(config);
            if (credentialResult.IsFailure)
            {
                return Result<bool>.Failure(credentialResult.Error);
            }

            // Create Gmail service
            var serviceResult = await CreateGmailServiceAsync(config, cancellationToken);
            if (serviceResult.IsFailure)
            {
                return Result<bool>.Failure(serviceResult.Error);
            }

            _gmailService = serviceResult.Value;

            // Test the connection
            var testResult = await TestGmailConnectionAsync(cancellationToken);
            if (testResult.IsFailure)
            {
                return testResult;
            }

            return Result<bool>.Success(true);
        }
        catch (Exception ex)
        {
            return Result<bool>.Failure(ex.ToProviderError("Gmail provider initialization failed"));
        }
    }

    /// <summary>
    /// Performs Gmail-specific shutdown cleanup
    /// </summary>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>A result indicating success or failure</returns>
    protected override async Task<Result<bool>> PerformShutdownAsync(CancellationToken cancellationToken)
    {
        try
        {
            _gmailService?.Dispose();
            _gmailService = null;
            _credential = null;

            await Task.CompletedTask; // Placeholder for any async cleanup
            return Result<bool>.Success(true);
        }
        catch (Exception ex)
        {
            return Result<bool>.Failure(ex.ToProviderError("Gmail provider shutdown failed"));
        }
    }

    /// <summary>
    /// Performs Gmail-specific health checks
    /// </summary>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>Health check result</returns>
    protected override async Task<Result<HealthCheckResult>> PerformHealthCheckAsync(CancellationToken cancellationToken)
    {
        try
        {
            if (_gmailService == null)
            {
                return Result<HealthCheckResult>.Success(
                    HealthCheckResult.Critical("Gmail service not initialized"));
            }

            // Test basic connectivity by getting user profile
            var profileResult = await _rateLimitHandler.ExecuteWithRetryAsync(async () =>
            {
                var profile = await _gmailService.Users.GetProfile(GmailApiConstants.USER_ID_ME).ExecuteAsync(cancellationToken);
                return profile;
            }, cancellationToken);

            if (profileResult.IsFailure)
            {
                return Result<HealthCheckResult>.Success(
                    HealthCheckResult.FromError(profileResult.Error, TimeSpan.Zero));
            }

            var profile = profileResult.Value;
            var healthData = new Dictionary<string, object>
            {
                { "EmailAddress", profile.EmailAddress ?? "Unknown" },
                { "MessagesTotal", profile.MessagesTotal ?? 0 },
                { "ThreadsTotal", profile.ThreadsTotal ?? 0 },
                { "HistoryId", profile.HistoryId ?? 0 }
            };

            return Result<HealthCheckResult>.Success(
                HealthCheckResult.Healthy("Gmail API connection successful") with
                {
                    Diagnostics = healthData
                });
        }
        catch (Exception ex)
        {
            return Result<HealthCheckResult>.Success(
                HealthCheckResult.FromError(ex.ToProviderError("Health check failed"), TimeSpan.Zero));
        }
    }

    #endregion

    #region IEmailProvider Implementation

    /// <summary>
    /// Connect to Gmail using OAuth2 authentication
    /// </summary>
    /// <returns>A result indicating success or failure</returns>
    public async Task<Result<bool>> ConnectAsync()
    {
        return await ExecuteOperationAsync("Connect", async (cancellationToken) =>
        {
            if (Configuration == null)
            {
                return Result<bool>.Failure(new ConfigurationError("Provider not initialized with configuration"));
            }

            // Check if already connected
            if (_gmailService != null)
            {
                return Result<bool>.Success(true);
            }

            // Attempt OAuth flow
            var authResult = await PerformOAuthFlowAsync(Configuration, cancellationToken);
            if (authResult.IsFailure)
            {
                return authResult;
            }

            return Result<bool>.Success(true);
        });
    }

    /// <summary>
    /// List emails with filtering and pagination options
    /// </summary>
    /// <param name="options">Search and filter options</param>
    /// <returns>A result containing the list of email summaries</returns>
    public async Task<Result<IReadOnlyList<EmailSummary>>> ListAsync(ListOptions options)
    {
        return await ExecuteOperationAsync("List", async (cancellationToken) =>
        {
            if (_gmailService == null)
            {
                return Result<IReadOnlyList<EmailSummary>>.Failure(
                    new InvalidOperationError(GmailErrorMessages.SERVICE_NOT_INITIALIZED));
            }

            var listResult = await _rateLimitHandler.ExecuteWithRetryAsync(async () =>
            {
                var request = _gmailService.Users.Messages.List(GmailApiConstants.USER_ID_ME);

                // Apply search options
                if (!string.IsNullOrEmpty(options.Query))
                    request.Q = options.Query;

                if (options.MaxResults.HasValue)
                    request.MaxResults = Math.Min(options.MaxResults.Value, GmailQuotas.MAX_LIST_RESULTS);
                else
                    request.MaxResults = Configuration?.DefaultPageSize ?? GmailQuotas.DEFAULT_LIST_RESULTS;

                if (!string.IsNullOrEmpty(options.PageToken))
                    request.PageToken = options.PageToken;

                if (options.LabelIds?.Any() == true)
                    request.LabelIds = options.LabelIds.ToList();

                var response = await request.ExecuteAsync(cancellationToken);
                return response;
            }, cancellationToken);

            if (listResult.IsFailure)
            {
                return Result<IReadOnlyList<EmailSummary>>.Failure(listResult.Error);
            }

            var messageList = listResult.Value;
            if (messageList.Messages == null || !messageList.Messages.Any())
            {
                return Result<IReadOnlyList<EmailSummary>>.Success(Array.Empty<EmailSummary>());
            }

            // Get detailed information for each message in batches
            var summaries = await GetMessageSummariesAsync(messageList.Messages, cancellationToken);
            return Result<IReadOnlyList<EmailSummary>>.Success(summaries);
        });
    }

    /// <summary>
    /// Get full email content including headers and body
    /// </summary>
    /// <param name="id">Email ID</param>
    /// <returns>A result containing the complete email details</returns>
    public async Task<Result<EmailFull>> GetAsync(string id)
    {
        return await ExecuteOperationAsync("Get", async (cancellationToken) =>
        {
            if (_gmailService == null)
            {
                return Result<EmailFull>.Failure(
                    new InvalidOperationError(GmailErrorMessages.SERVICE_NOT_INITIALIZED));
            }

            if (string.IsNullOrWhiteSpace(id))
            {
                return Result<EmailFull>.Failure(
                    new ValidationError(string.Format(GmailErrorMessages.INVALID_MESSAGE_ID, id)));
            }

            var messageResult = await _rateLimitHandler.ExecuteWithRetryAsync(async () =>
            {
                var request = _gmailService.Users.Messages.Get(GmailApiConstants.USER_ID_ME, id);
                request.Format = UsersResource.MessagesResource.GetRequest.FormatEnum.Full;

                var message = await request.ExecuteAsync(cancellationToken);
                return message;
            }, cancellationToken);

            if (messageResult.IsFailure)
            {
                return Result<EmailFull>.Failure(messageResult.Error);
            }

            var emailFull = MapToEmailFull(messageResult.Value);
            return Result<EmailFull>.Success(emailFull);
        });
    }

    /// <summary>
    /// Perform batch operations on multiple emails (labels, trash, etc.)
    /// </summary>
    /// <param name="request">Batch modification request</param>
    /// <returns>A result indicating success or failure</returns>
    public async Task<Result<bool>> BatchModifyAsync(BatchModifyRequest request)
    {
        return await ExecuteOperationAsync("BatchModify", async (cancellationToken) =>
        {
            if (_gmailService == null)
            {
                return Result<bool>.Failure(
                    new InvalidOperationError(GmailErrorMessages.SERVICE_NOT_INITIALIZED));
            }

            if (request.EmailIds == null || !request.EmailIds.Any())
            {
                return Result<bool>.Success(true); // Nothing to do
            }

            // Split into batches if necessary
            var batchSize = Configuration?.BatchSize ?? GmailQuotas.RECOMMENDED_BATCH_SIZE;
            var batches = request.EmailIds.Batch(batchSize);

            foreach (var batch in batches)
            {
                var batchResult = await _rateLimitHandler.ExecuteWithRetryAsync(async () =>
                {
                    var batchRequest = new BatchModifyMessagesRequest
                    {
                        Ids = batch.ToList()
                    };

                    if (request.AddLabelIds?.Any() == true)
                        batchRequest.AddLabelIds = request.AddLabelIds.ToList();

                    if (request.RemoveLabelIds?.Any() == true)
                        batchRequest.RemoveLabelIds = request.RemoveLabelIds.ToList();

                    await _gmailService.Users.Messages.BatchModify(batchRequest, GmailApiConstants.USER_ID_ME)
                        .ExecuteAsync(cancellationToken);

                    return true;
                }, cancellationToken);

                if (batchResult.IsFailure)
                {
                    return Result<bool>.Failure(batchResult.Error);
                }
            }

            return Result<bool>.Success(true);
        });
    }

    /// <summary>
    /// Hard delete email (use sparingly, prefer trash)
    /// </summary>
    /// <param name="id">Email ID</param>
    /// <returns>A result indicating success or failure</returns>
    public async Task<Result<bool>> DeleteAsync(string id)
    {
        return await ExecuteOperationAsync("Delete", async (cancellationToken) =>
        {
            if (_gmailService == null)
            {
                return Result<bool>.Failure(
                    new InvalidOperationError(GmailErrorMessages.SERVICE_NOT_INITIALIZED));
            }

            if (string.IsNullOrWhiteSpace(id))
            {
                return Result<bool>.Failure(
                    new ValidationError(string.Format(GmailErrorMessages.INVALID_MESSAGE_ID, id)));
            }

            var deleteResult = await _rateLimitHandler.ExecuteWithRetryAsync(async () =>
            {
                await _gmailService.Users.Messages.Delete(GmailApiConstants.USER_ID_ME, id)
                    .ExecuteAsync(cancellationToken);
                return true;
            }, cancellationToken);

            return deleteResult;
        });
    }

    /// <summary>
    /// Report email as spam (provider-dependent)
    /// </summary>
    /// <param name="id">Email ID</param>
    /// <returns>A result indicating success or failure</returns>
    public async Task<Result<bool>> ReportSpamAsync(string id)
    {
        return await ExecuteOperationAsync("ReportSpam", async (cancellationToken) =>
        {
            if (_gmailService == null)
            {
                return Result<bool>.Failure(
                    new InvalidOperationError(GmailErrorMessages.SERVICE_NOT_INITIALIZED));
            }

            var spamResult = await _rateLimitHandler.ExecuteWithRetryAsync(async () =>
            {
                var modifyRequest = new ModifyMessageRequest
                {
                    AddLabelIds = new List<string> { GmailLabels.SPAM },
                    RemoveLabelIds = new List<string> { GmailLabels.INBOX }
                };

                await _gmailService.Users.Messages.Modify(modifyRequest, GmailApiConstants.USER_ID_ME, id)
                    .ExecuteAsync(cancellationToken);

                return true;
            }, cancellationToken);

            return spamResult;
        });
    }

    /// <summary>
    /// Report email as phishing (provider-dependent)
    /// </summary>
    /// <param name="id">Email ID</param>
    /// <returns>A result indicating success or failure</returns>
    public async Task<Result<bool>> ReportPhishingAsync(string id)
    {
        return await ExecuteOperationAsync("ReportPhishing", async (cancellationToken) =>
        {
            if (_gmailService == null)
            {
                return Result<bool>.Failure(
                    new InvalidOperationError(GmailErrorMessages.SERVICE_NOT_INITIALIZED));
            }

            // Gmail doesn't have explicit phishing API, so we fall back to spam labeling
            var phishingResult = await _rateLimitHandler.ExecuteWithRetryAsync(async () =>
            {
                var modifyRequest = new ModifyMessageRequest
                {
                    AddLabelIds = new List<string> { GmailLabels.SPAM },
                    RemoveLabelIds = new List<string> { GmailLabels.INBOX }
                };

                await _gmailService.Users.Messages.Modify(modifyRequest, GmailApiConstants.USER_ID_ME, id)
                    .ExecuteAsync(cancellationToken);

                return true;
            }, cancellationToken);

            return phishingResult;
        });
    }

    /// <summary>
    /// Get authenticated user information
    /// </summary>
    /// <returns>A result containing authenticated user details or null if not authenticated</returns>
    public async Task<Result<AuthenticatedUserInfo?>> GetAuthenticatedUserAsync()
    {
        return await ExecuteOperationAsync("GetAuthenticatedUser", async (cancellationToken) =>
        {
            if (_gmailService == null)
            {
                return Result<AuthenticatedUserInfo?>.Success(null);
            }

            var profileResult = await _rateLimitHandler.ExecuteWithRetryAsync(async () =>
            {
                var profile = await _gmailService.Users.GetProfile(GmailApiConstants.USER_ID_ME)
                    .ExecuteAsync(cancellationToken);
                return profile;
            }, cancellationToken);

            if (profileResult.IsFailure)
            {
                return Result<AuthenticatedUserInfo?>.Success(null);
            }

            var profile = profileResult.Value;
            var userInfo = new AuthenticatedUserInfo
            {
                Email = profile.EmailAddress ?? string.Empty,
                MessagesTotal = (int)(profile.MessagesTotal ?? 0),
                ThreadsTotal = (int)(profile.ThreadsTotal ?? 0),
                HistoryId = profile.HistoryId?.ToString() ?? string.Empty
            };

            return Result<AuthenticatedUserInfo?>.Success(userInfo);
        });
    }

    /// <summary>
    /// Check provider health status
    /// </summary>
    /// <returns>Health check result</returns>
    public async Task<Result<bool>> HealthCheckAsync()
    {
        var healthResult = await PerformHealthCheckAsync(CancellationToken.None);
        if (healthResult.IsFailure)
        {
            return Result<bool>.Failure(healthResult.Error);
        }

        var health = healthResult.Value;
        return health.Status == HealthStatus.Healthy ?
            Result<bool>.Success(true) :
            Result<bool>.Failure(new ServiceUnavailableError(health.Description));
    }

    /// <summary>
    /// Get multiple emails in batch for improved performance
    /// 
    /// IMPORTANT: Gmail API does not provide true bulk message retrieval. This method uses 
    /// HTTP batch requests to bundle multiple individual Users.Messages.Get calls into 
    /// a single HTTP request, reducing network overhead but still consuming quota for 
    /// each individual message (5 quota units per message).
    /// 
    /// Performance benefits come from reduced HTTP round-trips, not reduced API quota usage.
    /// </summary>
    /// <param name="messageIds">List of message IDs to retrieve</param>
    /// <returns>A result containing the list of retrieved email summaries</returns>
    public async Task<Result<IReadOnlyList<EmailSummary>>> GetBatchAsync(IReadOnlyList<string> messageIds)
    {
        return await ExecuteOperationAsync("GetBatch", async (cancellationToken) =>
        {
            if (_gmailService == null)
            {
                return Result<IReadOnlyList<EmailSummary>>.Failure(
                    new InvalidOperationError(GmailErrorMessages.SERVICE_NOT_INITIALIZED));
            }

            if (messageIds == null || messageIds.Count == 0)
            {
                return Result<IReadOnlyList<EmailSummary>>.Success(Array.Empty<EmailSummary>());
            }

            var messagesResult = await GetMessagesBatchAsync(messageIds, cancellationToken);
            if (messagesResult.IsFailure)
            {
                return Result<IReadOnlyList<EmailSummary>>.Failure(messagesResult.Error);
            }

            var summaries = messagesResult.Value.Select(MapToEmailSummary).ToList();
            return Result<IReadOnlyList<EmailSummary>>.Success(summaries);
        });
    }

    #endregion

    #region Private Helper Methods

    /// <summary>
    /// Attempts to retrieve stored OAuth credentials
    /// </summary>
    private async Task<Result<bool>> TryRetrieveStoredCredentialsAsync(GmailProviderConfig config)
    {
        try
        {
            var scopes = new[] { GoogleOAuthScopes.GmailModify };

            // Check if we have valid tokens for Google services (shared between Gmail and Contacts)
            var hasValidTokensResult = await _googleOAuthService.HasValidTokensAsync(
                scopes, "google_", CancellationToken.None);

            if (hasValidTokensResult.IsFailure)
            {
                Logger.LogDebug("Failed to check token validity: {Error}", hasValidTokensResult.Error.Message);
                return Result<bool>.Success(false);
            }

            if (!hasValidTokensResult.Value)
            {
                Logger.LogDebug("No valid stored Gmail credentials found");
                return Result<bool>.Success(false);
            }

            // Get the UserCredential from the shared OAuth service
            var credentialResult = await _googleOAuthService.GetUserCredentialAsync(
                scopes, "google_", config.ClientId, config.ClientSecret, CancellationToken.None);

            if (credentialResult.IsFailure)
            {
                Logger.LogWarning("Failed to get UserCredential: {Error}", credentialResult.Error.Message);
                return Result<bool>.Success(false);
            }

            _credential = credentialResult.Value;
            Logger.LogDebug("Successfully retrieved stored Gmail credentials via shared OAuth service");
            return Result<bool>.Success(true);
        }
        catch (Exception ex)
        {
            return Result<bool>.Failure(ex.ToProviderError("Failed to retrieve stored credentials"));
        }
    }

    /// <summary>
    /// Creates a UserCredential from stored access and refresh tokens
    /// </summary>
    private async Task<Result<UserCredential>> CreateUserCredentialFromTokensAsync(
        GmailProviderConfig config,
        string accessToken,
        string refreshToken)
    {
        try
        {
            var clientSecrets = new ClientSecrets
            {
                ClientId = config.ClientId,
                ClientSecret = config.ClientSecret
            };

            // Create the authorization code flow
            var flow = new GoogleAuthorizationCodeFlow(new GoogleAuthorizationCodeFlow.Initializer
            {
                ClientSecrets = clientSecrets,
                Scopes = config.Scopes,
                DataStore = _dataStore
            });

            // Retrieve stored token expiry and issued time
            var tokenExpiryResult = await _secureStorageManager.RetrieveCredentialAsync(GmailStorageKeys.TOKEN_EXPIRY);
            var tokenIssuedResult = await _secureStorageManager.RetrieveCredentialAsync(GmailStorageKeys.TOKEN_ISSUED_UTC);

            var expiresInSeconds = 3600; // Default to 1 hour if not stored
            var issuedUtc = DateTime.UtcNow.AddHours(-1); // Default to 1 hour ago (making it stale by default)

            if (tokenExpiryResult.IsSuccess &&
                int.TryParse(tokenExpiryResult.Value, out var storedExpiry))
            {
                expiresInSeconds = storedExpiry;
            }

            if (tokenIssuedResult.IsSuccess &&
                DateTime.TryParse(tokenIssuedResult.Value, out var storedIssuedUtc))
            {
                issuedUtc = storedIssuedUtc;
            }

            // Create token response from stored tokens
            var tokenResponse = new TokenResponse
            {
                AccessToken = accessToken,
                RefreshToken = refreshToken,
                ExpiresInSeconds = expiresInSeconds,
                IssuedUtc = issuedUtc,
                TokenType = "Bearer"
            };

            // Create UserCredential from the token response
            var userCredential = new UserCredential(flow, "user", tokenResponse);

            // Test if the token is still valid by attempting to refresh if needed
            if (tokenResponse.IsStale)
            {
                var refreshResult = await userCredential.RefreshTokenAsync(CancellationToken.None);
                if (!refreshResult)
                {
                    return Result<UserCredential>.Failure(
                        new AuthenticationError("Failed to refresh expired token"));
                }

                // Update stored tokens with refreshed values
                var storeResult = await StoreCredentialsAsync(userCredential);
                if (storeResult.IsFailure)
                {
                    Logger.LogWarning("Failed to store refreshed credentials: {Error}", storeResult.Error.Message);
                }
            }

            return Result<UserCredential>.Success(userCredential);
        }
        catch (Exception ex)
        {
            return Result<UserCredential>.Failure(
                ex.ToProviderError("Failed to create credential from stored tokens"));
        }
    }

    /// <summary>
    /// Clears all stored OAuth tokens from secure storage
    /// </summary>
    /// <returns>A result indicating success or failure of the clear operation</returns>
    private async Task<Result<bool>> ClearStoredTokensAsync()
    {
        var errors = new List<string>();
        var operationsAttempted = 0;
        var operationsSucceeded = 0;

        var keys = new[]
        {
            GmailStorageKeys.ACCESS_TOKEN,
            GmailStorageKeys.REFRESH_TOKEN,
            GmailStorageKeys.TOKEN_EXPIRY,
            GmailStorageKeys.TOKEN_ISSUED_UTC,
            GmailStorageKeys.TOKEN_TYPE
        };

        foreach (var key in keys)
        {
            operationsAttempted++;
            try
            {
                await _secureStorageManager.RemoveCredentialAsync(key);
                operationsSucceeded++;

                // Log successful credential removal
                await _securityAuditLogger.LogCredentialOperationAsync(new CredentialOperationEvent
                {
                    Operation = "Remove",
                    CredentialKey = key,
                    Success = true,
                    UserContext = "Gmail Provider",
                    Platform = Environment.OSVersion.Platform.ToString()
                });
            }
            catch (Exception ex)
            {
                var errorMessage = $"Failed to remove credential {key}: {ex.Message}";
                errors.Add(errorMessage);

                Logger.LogError(ex, "Failed to remove stored credential {Key}", key);
                RecordMetric("token_cleanup_errors", 1);

                // Log failed credential removal
                await _securityAuditLogger.LogCredentialOperationAsync(new CredentialOperationEvent
                {
                    Operation = "Remove",
                    CredentialKey = key,
                    Success = false,
                    ErrorMessage = ex.Message,
                    UserContext = "Gmail Provider",
                    Platform = Environment.OSVersion.Platform.ToString()
                });
            }
        }

        if (errors.Count == 0)
        {
            RecordMetric("token_cleanup_success", 1);
            return Result<bool>.Success(true);
        }

        // If some operations succeeded, it's a partial success
        if (operationsSucceeded > 0)
        {
            var partialMessage = $"Cleared {operationsSucceeded}/{operationsAttempted} credentials. Errors: {string.Join("; ", errors)}";
            Logger.LogWarning("Partial success clearing stored tokens: {Message}", partialMessage);
            return Result<bool>.Success(true); // Partial success is still success for OAuth flow continuation
        }

        // Complete failure
        var fullErrorMessage = $"Failed to clear any stored credentials: {string.Join("; ", errors)}";
        return Result<bool>.Failure(new InvalidOperationError(fullErrorMessage));
    }

    /// <summary>
    /// Performs OAuth2 authentication flow using shared GoogleOAuthService
    /// </summary>
    private async Task<Result<bool>> PerformOAuthFlowAsync(GmailProviderConfig config, CancellationToken cancellationToken)
    {
        try
        {
            // Use shared GoogleOAuthService for consistent token management
            var authResult = await _googleOAuthService.AuthenticateWithBrowserAsync(
                config.Scopes,
                "google_", // Use consistent storage key prefix
                config.ClientId,
                config.ClientSecret,
                cancellationToken);

            if (authResult.IsFailure)
            {
                return authResult;
            }

            // Get the credential from the shared service
            var credentialResult = await _googleOAuthService.GetUserCredentialAsync(
                config.Scopes,
                "google_",
                config.ClientId,
                config.ClientSecret,
                cancellationToken);

            if (credentialResult.IsFailure)
            {
                return Result<bool>.Failure(credentialResult.Error);
            }

            _credential = credentialResult.Value;
            return Result<bool>.Success(true);
        }
        catch (Exception ex)
        {
            return Result<bool>.Failure(ex.ToProviderError("OAuth authentication failed"));
        }
    }

    /// <summary>
    /// Creates a Gmail service instance using shared GoogleOAuthService
    /// </summary>
    private async Task<Result<GmailService>> CreateGmailServiceAsync(GmailProviderConfig config, CancellationToken cancellationToken)
    {
        try
        {
            if (_credential == null)
            {
                // First try to get existing credential from shared GoogleOAuthService
                var credentialResult = await _googleOAuthService.GetUserCredentialAsync(
                    config.Scopes,
                    "google_",
                    config.ClientId,
                    config.ClientSecret,
                    cancellationToken);

                if (credentialResult.IsSuccess)
                {
                    _credential = credentialResult.Value;
                }
                else
                {
                    // No existing credential, perform OAuth flow
                    var authResult = await PerformOAuthFlowAsync(config, cancellationToken);
                    if (authResult.IsFailure)
                    {
                        return Result<GmailService>.Failure(authResult.Error);
                    }
                }
            }

            var service = new GmailService(new BaseClientService.Initializer()
            {
                HttpClientInitializer = _credential,
                ApplicationName = config.ApplicationName
            });

            return Result<GmailService>.Success(service);
        }
        catch (Exception ex)
        {
            return Result<GmailService>.Failure(ex.ToProviderError("Failed to create Gmail service"));
        }
    }

    /// <summary>
    /// Tests the Gmail connection
    /// </summary>
    private async Task<Result<bool>> TestGmailConnectionAsync(CancellationToken cancellationToken)
    {
        try
        {
            if (_gmailService == null)
            {
                return Result<bool>.Failure(new InvalidOperationError("Gmail service not created"));
            }

            var result = await _rateLimitHandler.ExecuteWithRetryAsync(async () =>
            {
                var profile = await _gmailService.Users.GetProfile(GmailApiConstants.USER_ID_ME)
                    .ExecuteAsync(cancellationToken);
                return profile != null && !string.IsNullOrEmpty(profile.EmailAddress);
            }, cancellationToken);

            return result;
        }
        catch (Exception ex)
        {
            return Result<bool>.Failure(ex.ToProviderError("Gmail connection test failed"));
        }
    }

    /// <summary>
    /// Stores OAuth credentials securely
    /// </summary>
    /// <param name="credential">The user credential to store</param>
    /// <returns>A result indicating success or failure of the storage operation</returns>
    private async Task<Result<bool>> StoreCredentialsAsync(UserCredential credential)
    {
        if (credential?.Token == null)
        {
            return Result<bool>.Failure(new ValidationError("Credential or token is null"));
        }

        var errors = new List<string>();
        var operationsAttempted = 0;
        var operationsSucceeded = 0;

        try
        {
            // Store access token
            operationsAttempted++;
            await _secureStorageManager.StoreCredentialAsync(
                GmailStorageKeys.ACCESS_TOKEN,
                credential.Token.AccessToken);
            operationsSucceeded++;

            await _securityAuditLogger.LogCredentialOperationAsync(new CredentialOperationEvent
            {
                Operation = "Store",
                CredentialKey = GmailStorageKeys.ACCESS_TOKEN,
                Success = true,
                UserContext = "Gmail Provider",
                Platform = Environment.OSVersion.Platform.ToString()
            });

            // Store refresh token if available
            if (!string.IsNullOrEmpty(credential.Token.RefreshToken))
            {
                operationsAttempted++;
                await _secureStorageManager.StoreCredentialAsync(
                    GmailStorageKeys.REFRESH_TOKEN,
                    credential.Token.RefreshToken);
                operationsSucceeded++;

                await _securityAuditLogger.LogCredentialOperationAsync(new CredentialOperationEvent
                {
                    Operation = "Store",
                    CredentialKey = GmailStorageKeys.REFRESH_TOKEN,
                    Success = true,
                    UserContext = "Gmail Provider",
                    Platform = Environment.OSVersion.Platform.ToString()
                });
            }

            // Store token expiry
            operationsAttempted++;
            await _secureStorageManager.StoreCredentialAsync(
                GmailStorageKeys.TOKEN_EXPIRY,
                credential.Token.ExpiresInSeconds?.ToString() ?? "0");
            operationsSucceeded++;

            await _securityAuditLogger.LogCredentialOperationAsync(new CredentialOperationEvent
            {
                Operation = "Store",
                CredentialKey = GmailStorageKeys.TOKEN_EXPIRY,
                Success = true,
                UserContext = "Gmail Provider",
                Platform = Environment.OSVersion.Platform.ToString()
            });

            // Store token issued UTC time
            operationsAttempted++;
            await _secureStorageManager.StoreCredentialAsync(
                GmailStorageKeys.TOKEN_ISSUED_UTC,
                credential.Token.IssuedUtc.ToString("O")); // Use ISO 8601 format for consistent parsing
            operationsSucceeded++;

            await _securityAuditLogger.LogCredentialOperationAsync(new CredentialOperationEvent
            {
                Operation = "Store",
                CredentialKey = GmailStorageKeys.TOKEN_ISSUED_UTC,
                Success = true,
                UserContext = "Gmail Provider",
                Platform = Environment.OSVersion.Platform.ToString()
            });

            RecordMetric("credential_storage_success", 1);
            return Result<bool>.Success(true);
        }
        catch (Exception ex)
        {
            var errorMessage = $"Failed to store credential: {ex.Message}";
            Logger.LogError(ex, "Failed to store OAuth credentials");
            RecordMetric("credential_storage_errors", 1);

            // Log the failed operation
            await _securityAuditLogger.LogCredentialOperationAsync(new CredentialOperationEvent
            {
                Operation = "Store",
                CredentialKey = "Multiple",
                Success = false,
                ErrorMessage = ex.Message,
                UserContext = "Gmail Provider",
                Platform = Environment.OSVersion.Platform.ToString()
            });

            // If some operations succeeded, it's a partial success
            if (operationsSucceeded > 0)
            {
                var partialMessage = $"Stored {operationsSucceeded}/{operationsAttempted} credentials. Error: {errorMessage}";
                Logger.LogWarning("Partial success storing credentials: {Message}", partialMessage);
                return Result<bool>.Success(true); // Partial success is acceptable
            }

            return Result<bool>.Failure(new InvalidOperationError(errorMessage));
        }
    }

    /// <summary>
    /// Gets message summaries for a list of messages using efficient batch requests
    /// </summary>
    private async Task<IReadOnlyList<EmailSummary>> GetMessageSummariesAsync(
        IList<Message> messages,
        CancellationToken cancellationToken)
    {
        if (messages == null || !messages.Any())
        {
            return Array.Empty<EmailSummary>();
        }

        var messageIds = messages.Select(m => m.Id).ToList();
        var messagesResult = await GetMessagesBatchAsync(messageIds, cancellationToken);

        if (messagesResult.IsFailure)
        {
            // If batch retrieval fails, return empty list to gracefully handle the error
            return Array.Empty<EmailSummary>();
        }

        return messagesResult.Value.Select(MapToEmailSummary).ToList();
    }

    /// <summary>
    /// Retrieves multiple messages efficiently using Gmail API batch requests
    /// 
    /// NOTE: This is NOT a true bulk API operation. The Gmail API does not provide 
    /// bulk message retrieval. This method uses HTTP batch requests to bundle 
    /// multiple individual Users.Messages.Get calls into fewer HTTP requests.
    /// 
    /// Each message still consumes 5 quota units, but network latency is reduced
    /// by batching up to 100 individual requests per HTTP call.
    /// </summary>
    private async Task<Result<IReadOnlyList<Message>>> GetMessagesBatchAsync(
        IReadOnlyList<string> messageIds,
        CancellationToken cancellationToken)
    {
        if (messageIds == null || !messageIds.Any())
        {
            return Result<IReadOnlyList<Message>>.Success(Array.Empty<Message>());
        }

        if (_gmailService == null)
        {
            return Result<IReadOnlyList<Message>>.Failure(
                new InvalidOperationError(GmailErrorMessages.SERVICE_NOT_INITIALIZED));
        }

        var allMessages = new List<Message>();

        // Split messageIds into batches of maximum allowed size
        var batchSize = Math.Min(GmailQuotas.MAX_BATCH_SIZE, Configuration?.BatchSize ?? GmailQuotas.RECOMMENDED_BATCH_SIZE);
        var batches = messageIds.Batch(batchSize);

        foreach (var batch in batches)
        {
            var batchResult = await ExecuteMessageBatchAsync(batch.ToList(), cancellationToken);
            if (batchResult.IsFailure)
            {
                return Result<IReadOnlyList<Message>>.Failure(batchResult.Error);
            }
            allMessages.AddRange(batchResult.Value);
        }

        return Result<IReadOnlyList<Message>>.Success(allMessages);
    }

    /// <summary>
    /// Executes a single batch request for message retrieval
    /// 
    /// TECHNICAL DETAIL: Uses Google.Apis.Requests.BatchRequest to combine multiple 
    /// Users.Messages.Get requests into a single HTTP request. This is purely an HTTP 
    /// optimization - each individual message request is still processed separately 
    /// by the Gmail API and consumes full quota (5 units per message).
    /// 
    /// Benefits: Reduced HTTP round-trips and connection overhead
    /// Limitations: Same quota consumption as individual requests
    /// </summary>
    private async Task<Result<IReadOnlyList<Message>>> ExecuteMessageBatchAsync(
        IReadOnlyList<string> messageIds,
        CancellationToken cancellationToken)
    {
        var retrievedMessages = new List<Message>();
        var errors = new List<string>();

        var result = await _rateLimitHandler.ExecuteWithRetryAsync(async () =>
        {
            var batchRequest = new BatchRequest(_gmailService!);

            // Define callback for handling batch responses
            BatchRequest.OnResponse<Message> callback = (message, error, index, httpResponse) =>
            {
                if (error != null)
                {
                    var messageId = index < messageIds.Count ? messageIds[index] : "unknown";
                    errors.Add($"Error retrieving message {messageId}: {error.Message}");
                    RecordMetric("batch_message_errors", 1);
                }
                else if (message != null)
                {
                    retrievedMessages.Add(message);
                }
            };

            // Queue individual Get requests for each message ID
            for (int i = 0; i < messageIds.Count; i++)
            {
                var getRequest = _gmailService!.Users.Messages.Get(GmailApiConstants.USER_ID_ME, messageIds[i]);
                // Use metadata format for efficiency - contains headers and basic info without full body
                getRequest.Format = UsersResource.MessagesResource.GetRequest.FormatEnum.Metadata;

                batchRequest.Queue<Message>(getRequest, callback);
            }

            // Execute the entire batch
            await batchRequest.ExecuteAsync(cancellationToken);

            // Log any errors but don't fail the entire operation
            if (errors.Count > 0)
            {
                // Note: Errors are tracked in metrics, detailed logging can be added later
                RecordMetric("batch_total_errors", errors.Count);
            }

            RecordMetric("batch_messages_retrieved", retrievedMessages.Count);
            RecordMetric("batch_requests_executed", 1);

            return true;
        }, cancellationToken);

        if (result.IsFailure)
        {
            return Result<IReadOnlyList<Message>>.Failure(result.Error);
        }

        return Result<IReadOnlyList<Message>>.Success(retrievedMessages);
    }

    /// <summary>
    /// Maps a Gmail Message to EmailSummary
    /// </summary>
    private static EmailSummary MapToEmailSummary(Message message)
    {
        var headers = message.Payload?.Headers?.ToDictionary(h => h.Name, h => h.Value) ?? new Dictionary<string, string>();

        return new EmailSummary
        {
            Id = message.Id,
            ThreadId = message.ThreadId,
            LabelIds = message.LabelIds?.ToArray() ?? Array.Empty<string>(),
            Snippet = message.Snippet ?? string.Empty,
            HistoryId = (long)(message.HistoryId ?? 0),
            InternalDate = (long)(message.InternalDate ?? 0),
            Subject = headers.GetValueOrDefault("Subject", string.Empty),
            From = headers.GetValueOrDefault("From", string.Empty),
            To = headers.GetValueOrDefault("To", string.Empty),
            ReceivedDate = DateTimeOffset.FromUnixTimeMilliseconds((long)(message.InternalDate ?? 0)).DateTime,
            HasAttachments = HasAttachments(message.Payload),
            SizeEstimate = (long)(message.SizeEstimate ?? 0)
        };
    }

    /// <summary>
    /// Maps a Gmail Message to EmailFull
    /// </summary>
    private static EmailFull MapToEmailFull(Message message)
    {
        var headers = message.Payload?.Headers?.ToDictionary(h => h.Name, h => h.Value) ?? new Dictionary<string, string>();
        var bodyText = GetBodyText(message.Payload);
        var bodyHtml = GetBodyHtml(message.Payload);
        var attachments = GetAttachments(message.Payload);

        return new EmailFull
        {
            Id = message.Id,
            ThreadId = message.ThreadId,
            LabelIds = message.LabelIds?.ToArray() ?? Array.Empty<string>(),
            Snippet = message.Snippet ?? string.Empty,
            HistoryId = (long)(message.HistoryId ?? 0),
            InternalDate = (long)(message.InternalDate ?? 0),
            Headers = headers,
            BodyText = bodyText,
            BodyHtml = bodyHtml,
            Attachments = attachments,
            SizeEstimate = (long)(message.SizeEstimate ?? 0)
        };
    }

    /// <summary>
    /// Checks if a message has attachments
    /// </summary>
    private static bool HasAttachments(MessagePart? payload)
    {
        if (payload == null) return false;

        if (payload.Parts != null)
        {
            return payload.Parts.Any(part =>
                !string.IsNullOrEmpty(part.Filename) ||
                (part.Body?.AttachmentId != null));
        }

        return !string.IsNullOrEmpty(payload.Filename) || (payload.Body?.AttachmentId != null);
    }

    /// <summary>
    /// Extracts plain text body from message
    /// </summary>
    private static string? GetBodyText(MessagePart? payload)
    {
        if (payload == null) return null;

        if (payload.MimeType == GmailMimeTypes.TEXT_PLAIN && payload.Body?.Data != null)
        {
            return DecodeBase64String(payload.Body.Data);
        }

        if (payload.Parts != null)
        {
            foreach (var part in payload.Parts)
            {
                var text = GetBodyText(part);
                if (!string.IsNullOrEmpty(text))
                    return text;
            }
        }

        return null;
    }

    /// <summary>
    /// Extracts HTML body from message
    /// </summary>
    private static string? GetBodyHtml(MessagePart? payload)
    {
        if (payload == null) return null;

        if (payload.MimeType == GmailMimeTypes.TEXT_HTML && payload.Body?.Data != null)
        {
            return DecodeBase64String(payload.Body.Data);
        }

        if (payload.Parts != null)
        {
            foreach (var part in payload.Parts)
            {
                var html = GetBodyHtml(part);
                if (!string.IsNullOrEmpty(html))
                    return html;
            }
        }

        return null;
    }

    /// <summary>
    /// Extracts attachments from message
    /// </summary>
    private static IReadOnlyList<EmailAttachment> GetAttachments(MessagePart? payload)
    {
        var attachments = new List<EmailAttachment>();
        CollectAttachments(payload, attachments);
        return attachments;
    }

    /// <summary>
    /// Recursively collects attachments from message parts
    /// </summary>
    private static void CollectAttachments(MessagePart? part, List<EmailAttachment> attachments)
    {
        if (part == null) return;

        if (!string.IsNullOrEmpty(part.Filename) || part.Body?.AttachmentId != null)
        {
            attachments.Add(new EmailAttachment
            {
                FileName = part.Filename ?? "unknown",
                MimeType = part.MimeType ?? GmailMimeTypes.APPLICATION_OCTET_STREAM,
                Size = part.Body?.Size ?? 0,
                AttachmentId = part.Body?.AttachmentId ?? string.Empty
            });
        }

        if (part.Parts != null)
        {
            foreach (var childPart in part.Parts)
            {
                CollectAttachments(childPart, attachments);
            }
        }
    }

    /// <summary>
    /// Decodes Gmail's URL-safe base64 encoding
    /// </summary>
    private static string DecodeBase64String(string base64String)
    {
        try
        {
            base64String = base64String.Replace('-', '+').Replace('_', '/');

            switch (base64String.Length % 4)
            {
                case 2: base64String += "=="; break;
                case 3: base64String += "="; break;
            }

            var bytes = Convert.FromBase64String(base64String);
            return Encoding.UTF8.GetString(bytes);
        }
        catch
        {
            return string.Empty;
        }
    }

    #endregion
}

/// <summary>
/// Extension methods for batch processing
/// </summary>
internal static class EnumerableExtensions
{
    /// <summary>
    /// Splits an enumerable into batches of the specified size
    /// </summary>
    public static IEnumerable<IEnumerable<T>> Batch<T>(this IEnumerable<T> source, int batchSize)
    {
        var batch = new List<T>(batchSize);
        foreach (var item in source)
        {
            batch.Add(item);
            if (batch.Count == batchSize)
            {
                yield return batch;
                batch = new List<T>(batchSize);
            }
        }
        if (batch.Count > 0)
            yield return batch;
    }
}