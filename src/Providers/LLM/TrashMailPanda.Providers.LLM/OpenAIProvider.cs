using OpenAI.Chat;
using OpenAI;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using TrashMailPanda.Shared;
using TrashMailPanda.Shared.Base;
using TrashMailPanda.Shared.Models;

namespace TrashMailPanda.Providers.LLM;

/// <summary>
/// OpenAI implementation of ILLMProvider using BaseProvider architecture
/// Provides email classification using GPT-4o-mini for cost optimization with intelligent health checks
/// </summary>
public class OpenAIProvider : BaseProvider<OpenAIConfig>, ILLMProvider
{
    private ChatClient? _client;
    private OpenAIClient? _openAIClient;
    private DateTime? _lastApiCheck;
    private HealthCheckResult? _cachedHealthResult;
    private readonly SemaphoreSlim _healthCheckSemaphore = new(1, 1);

    public override string Name => "OpenAI";

    public override string Version => "1.0.0";

    /// <summary>
    /// Initializes a new instance of the OpenAIProvider
    /// </summary>
    /// <param name="logger">Logger for this provider</param>
    public OpenAIProvider(ILogger<OpenAIProvider> logger) : base(logger)
    {
    }

    /// <summary>
    /// Initializes the OpenAI provider with configuration
    /// This method is called by the BaseProvider during initialization
    /// </summary>
    /// <param name="config">The configuration to initialize with</param>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>Result indicating success or failure</returns>
    protected override async Task<Result<bool>> PerformInitializationAsync(OpenAIConfig config, CancellationToken cancellationToken)
    {
        if (config == null)
            return Result<bool>.Failure(new ValidationError("Configuration is required for OpenAI provider"));

        try
        {
            // Initialize OpenAI clients
            _openAIClient = new OpenAIClient(config.ApiKey);
            _client = _openAIClient.GetChatClient(config.Model);

            Logger.LogInformation("OpenAI provider initialized successfully with model: {Model}", config.Model);
            await Task.CompletedTask; // Keep async for interface compatibility
            return Result<bool>.Success(true);
        }
        catch (Exception ex)
        {
            Logger.LogError(ex, "Failed to initialize OpenAI provider");
            return Result<bool>.Failure(ex.ToProviderError("Failed to initialize OpenAI provider"));
        }
    }

    /// <summary>
    /// Legacy InitAsync method for ILLMProvider compatibility
    /// This method creates a temporary config and initializes the provider
    /// </summary>
    /// <param name="auth">Authentication information</param>
    public async Task InitAsync(LLMAuth auth)
    {
        if (auth is not LLMAuth.ApiKey apiKeyAuth)
            throw new ArgumentException("OpenAI provider requires API key authentication", nameof(auth));

        // Create a temporary configuration for legacy compatibility
        var config = OpenAIConfig.CreateDevelopmentConfig(apiKeyAuth.Key);
        var initResult = await InitializeAsync(config);

        if (initResult.IsFailure)
            throw new InvalidOperationException($"Failed to initialize OpenAI provider: {initResult.Error?.Message}");
    }

    /// <summary>
    /// Performs tiered health checks for the OpenAI provider
    /// Tier 1: Format validation, Tier 2: Cached results, Tier 3: API validation (rate limited)
    /// </summary>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>Health check result</returns>
    protected override async Task<Result<HealthCheckResult>> PerformHealthCheckAsync(CancellationToken cancellationToken)
    {
        // Tier 1: Format validation (always)
        if (Configuration?.ApiKey == null || !Configuration.ApiKey.StartsWith("sk-"))
        {
            return Result<HealthCheckResult>.Success(
                HealthCheckResult.Critical("Invalid API key format"));
        }

        // Tier 2: Cached result (if recent)
        if (_cachedHealthResult != null && _lastApiCheck.HasValue &&
            DateTime.UtcNow - _lastApiCheck.Value < TimeSpan.FromMinutes(Configuration.HealthCheckIntervalMinutes))
        {
            Logger.LogDebug("Returning cached OpenAI health check result");
            return Result<HealthCheckResult>.Success(_cachedHealthResult);
        }

        // Tier 3: API validation (rate limited)
        if (!_healthCheckSemaphore.Wait(0)) // Non-blocking
        {
            Logger.LogDebug("OpenAI health check already in progress, returning cached result");
            return Result<HealthCheckResult>.Success(_cachedHealthResult ??
                HealthCheckResult.Degraded("Health check in progress"));
        }

        try
        {
            return await PerformApiHealthCheckAsync(cancellationToken);
        }
        finally
        {
            _healthCheckSemaphore.Release();
        }
    }

    /// <summary>
    /// Performs actual API health check with intelligent error classification
    /// </summary>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>Health check result</returns>
    private async Task<Result<HealthCheckResult>> PerformApiHealthCheckAsync(CancellationToken cancellationToken)
    {
        // Skip API check if we've checked recently
        if (_lastApiCheck.HasValue && DateTime.UtcNow - _lastApiCheck.Value < TimeSpan.FromMinutes(Configuration.HealthCheckIntervalMinutes))
        {
            return Result<HealthCheckResult>.Success(_cachedHealthResult ??
                HealthCheckResult.Healthy("API key format valid"));
        }

        try
        {
            if (_openAIClient == null)
            {
                return Result<HealthCheckResult>.Success(
                    HealthCheckResult.Degraded("Provider not initialized"));
            }

            // Use lightweight chat completion for health check (minimal tokens)
            Logger.LogDebug("Performing OpenAI API health check");
            var testMessages = new List<ChatMessage>
            {
                new SystemChatMessage("You are a test."),
                new UserChatMessage("Hi")
            };
            var testResponse = await _client.CompleteChatAsync(testMessages, cancellationToken: cancellationToken);

            _lastApiCheck = DateTime.UtcNow;
            _cachedHealthResult = HealthCheckResult.Healthy("API connection validated");

            Logger.LogDebug("OpenAI API health check successful");
            return Result<HealthCheckResult>.Success(_cachedHealthResult);
        }
        catch (Exception ex) when (ex.Message.Contains("429") || ex.Message.Contains("insufficient_quota"))
        {
            // Rate limited = valid key, temporarily unavailable = HEALTHY
            _lastApiCheck = DateTime.UtcNow;
            var healthStatus = Configuration.TreatRateLimitAsHealthy ? HealthStatus.Healthy : HealthStatus.Degraded;
            _cachedHealthResult = new HealthCheckResult
            {
                Status = healthStatus,
                Description = "API key valid (rate limited)",
                CheckedAt = DateTime.UtcNow,
                Duration = TimeSpan.Zero,
                Diagnostics = new Dictionary<string, object>
                {
                    { "error_type", "rate_limit" },
                    { "api_key_valid", true },
                    { "message", ex.Message }
                }
            };

            Logger.LogDebug("OpenAI API rate limited, treating as {Status}: {Message}", healthStatus, ex.Message);
            return Result<HealthCheckResult>.Success(_cachedHealthResult);
        }
        catch (Exception ex) when (ex.Message.Contains("401") || ex.Message.Contains("invalid") || ex.Message.Contains("Unauthorized"))
        {
            // Invalid key = CRITICAL
            _cachedHealthResult = HealthCheckResult.Critical($"Invalid API key: {ex.Message}");
            Logger.LogWarning("OpenAI API authentication failed: {Message}", ex.Message);
            return Result<HealthCheckResult>.Success(_cachedHealthResult);
        }
        catch (Exception ex)
        {
            // Network/other errors = DEGRADED (don't cache these)
            Logger.LogWarning(ex, "OpenAI API health check failed with unexpected error");
            return Result<HealthCheckResult>.Success(
                HealthCheckResult.Degraded($"API check failed: {ex.Message}"));
        }
    }

    /// <summary>
    /// Test the API connection with a minimal request to validate the API key.
    /// This method is provided for legacy compatibility and explicit testing.
    /// </summary>
    public async Task<bool> TestConnectionAsync()
    {
        var healthResult = await PerformHealthCheckAsync(CancellationToken.None);
        return healthResult.IsSuccess &&
               healthResult.Value != null &&
               (healthResult.Value.Status == HealthStatus.Healthy || healthResult.Value.Status == HealthStatus.Degraded);
    }

    public async Task<ClassifyOutput> ClassifyEmailsAsync(ClassifyInput input)
    {
        if (_client == null)
            throw new InvalidOperationException("OpenAI provider not initialized. Call InitAsync first.");

        var systemPrompt = BuildSystemPrompt(input.UserRulesSnapshot);
        var userPrompt = BuildUserPrompt(input.Emails);

        var messages = new List<ChatMessage>
        {
            new SystemChatMessage(systemPrompt),
            new UserChatMessage(userPrompt)
        };

        try
        {
            var response = await _client.CompleteChatAsync(messages);
            var content = response.Value?.Content?[0]?.Text;

            if (string.IsNullOrEmpty(content))
                throw new InvalidOperationException("OpenAI API returned empty response");

            return ParseClassificationResponse(content, input.Emails);
        }
        catch (Exception ex)
        {
            // Provide more specific error messages for common issues
            if (ex.Message.Contains("429") || ex.Message.Contains("insufficient_quota"))
            {
                throw new InvalidOperationException("OpenAI API quota exceeded. Please check your billing details and rate limits.", ex);
            }
            if (ex.Message.Contains("401") || ex.Message.Contains("Unauthorized"))
            {
                throw new InvalidOperationException("OpenAI API key is invalid or expired. Please check your API key.", ex);
            }

            throw new InvalidOperationException($"OpenAI classification failed: {ex.Message}", ex);
        }
    }

    public async Task<IReadOnlyList<string>> SuggestSearchQueriesAsync(QueryContext context)
    {
        if (_client == null)
            throw new InvalidOperationException("OpenAI provider not initialized. Call InitAsync first.");

        var systemPrompt = @"You are an expert at Gmail search queries. Generate Gmail search queries that help users find specific types of emails efficiently.
Return only the search query strings, one per line, without explanations.";

        var userPrompt = BuildQuerySuggestionPrompt(context);

        var messages = new List<ChatMessage>
        {
            new SystemChatMessage(systemPrompt),
            new UserChatMessage(userPrompt)
        };

        try
        {
            var response = await _client.CompleteChatAsync(messages);
            var content = response.Value?.Content?[0]?.Text;

            if (string.IsNullOrEmpty(content))
                return Array.Empty<string>();

            return content.Split('\n', StringSplitOptions.RemoveEmptyEntries)
                         .Select(q => q.Trim())
                         .Where(q => !string.IsNullOrEmpty(q))
                         .Take(5) // Limit to 5 suggestions
                         .ToArray();
        }
        catch
        {
            // Return fallback suggestions if API fails
            return new[] { "category:promotions", "has:attachment", "is:unread older_than:30d" };
        }
    }

    public async Task<GroupOutput> GroupForBulkAsync(GroupingInput input)
    {
        if (_client == null)
            throw new InvalidOperationException("OpenAI provider not initialized. Call InitAsync first.");

        var systemPrompt = @"You are an expert at grouping emails for bulk operations. 
Analyze the classified emails and group them by similar characteristics for efficient bulk processing.
Focus on sender domains, list IDs, email types, and common patterns.
Return JSON with bulk groups containing: id, simpleLabel, emailCount, actionType.";

        var userPrompt = BuildGroupingPrompt(input.ClassifiedEmails);

        var messages = new List<ChatMessage>
        {
            new SystemChatMessage(systemPrompt),
            new UserChatMessage(userPrompt)
        };

        try
        {
            var response = await _client.CompleteChatAsync(messages);
            var content = response.Value?.Content?[0]?.Text;

            if (string.IsNullOrEmpty(content))
                return new GroupOutput { BulkGroups = Array.Empty<BulkGroup>() };

            return ParseGroupingResponse(content);
        }
        catch
        {
            // Return simple grouping fallback
            return CreateFallbackGrouping(input.ClassifiedEmails);
        }
    }

    private static string BuildSystemPrompt(UserRules userRules)
    {
        return @"You are TrashMail Panda, an AI email classification assistant. 
Classify emails into these categories: keep, newsletter, promotion, spam, dangerous_phishing, unknown.

Always respond with valid JSON in this exact format:
{
  ""classifications"": [
    {
      ""emailId"": ""id"",
      ""classification"": ""keep"",
      ""likelihood"": ""very_likely"",
      ""confidence"": 0.95,
      ""reasons"": [""Known sender"", ""Personal communication""],
      ""bulkKey"": ""from:sender@domain.com"",
      ""unsubscribeMethod"": { ""type"": ""http_link"", ""value"": ""https://unsubscribe.example.com"" }
    }
  ]
}

Classification guidelines:
- keep: Important emails from contacts, receipts, 2FA codes, work communications
- newsletter: Legitimate newsletters with List-Unsubscribe headers  
- promotion: Marketing emails, deals, promotional content
- spam: Unwanted bulk emails, suspicious mass mailings
- dangerous_phishing: Credential harvesting, brand impersonation, malicious content
- unknown: Uncertain classifications requiring manual review

Likelihood values: very_likely, likely, unsure
Confidence: 0.0-1.0 numeric score";
    }

    private static string BuildUserPrompt(IReadOnlyList<EmailClassificationInput> emails)
    {
        var emailsJson = emails.Select(email => new
        {
            id = email.Id,
            from = email.Headers.GetValueOrDefault("From", ""),
            subject = email.Headers.GetValueOrDefault("Subject", ""),
            to = email.Headers.GetValueOrDefault("To", ""),
            listId = email.Headers.GetValueOrDefault("List-ID", ""),
            listUnsubscribe = email.Headers.GetValueOrDefault("List-Unsubscribe", ""),
            bodyText = email.BodyText?.Length > 1000 ? email.BodyText.Substring(0, 1000) : email.BodyText,
            hasListUnsubscribe = email.ProviderSignals?.HasListUnsubscribe,
            contactStrength = email.ContactSignal?.Strength.ToString()
        });

        return $"Classify these emails:\n{JsonSerializer.Serialize(emailsJson, new JsonSerializerOptions { WriteIndented = true })}";
    }

    private static string BuildQuerySuggestionPrompt(QueryContext context)
    {
        var prompt = "Suggest Gmail search queries for: ";

        if (!string.IsNullOrEmpty(context.Intent))
            prompt += context.Intent;
        else if (context.Keywords?.Any() == true)
            prompt += string.Join(", ", context.Keywords);
        else
            prompt += "general email management";

        return prompt;
    }

    private static string BuildGroupingPrompt(IReadOnlyList<ClassifyItem> emails)
    {
        var emailsJson = emails.Select(email => new
        {
            emailId = email.EmailId,
            classification = email.Classification.ToString().ToLowerInvariant(),
            bulkKey = email.BulkKey,
            reasons = email.Reasons
        });

        return $"Group these classified emails for bulk operations:\n{JsonSerializer.Serialize(emailsJson, new JsonSerializerOptions { WriteIndented = true })}";
    }

    private static ClassifyOutput ParseClassificationResponse(string jsonResponse, IReadOnlyList<EmailClassificationInput> originalEmails)
    {
        try
        {
            // Extract JSON from response (remove any markdown formatting)
            var json = ExtractJsonFromResponse(jsonResponse);

            using var document = JsonDocument.Parse(json);
            var root = document.RootElement;

            if (!root.TryGetProperty("classifications", out var classificationsElement))
                throw new InvalidOperationException("Response missing 'classifications' property");

            var items = new List<ClassifyItem>();

            foreach (var classification in classificationsElement.EnumerateArray())
            {
                var emailId = classification.GetProperty("emailId").GetString() ?? string.Empty;
                var classificationStr = classification.GetProperty("classification").GetString() ?? "unknown";
                var likelihoodStr = classification.GetProperty("likelihood").GetString() ?? "unsure";
                var confidence = classification.TryGetProperty("confidence", out var confidenceElement) ? confidenceElement.GetDouble() : 0.5;

                var reasons = new List<string>();
                if (classification.TryGetProperty("reasons", out var reasonsElement))
                {
                    reasons.AddRange(reasonsElement.EnumerateArray().Select(r => r.GetString() ?? string.Empty));
                }

                var bulkKey = classification.TryGetProperty("bulkKey", out var bulkKeyElement) ? bulkKeyElement.GetString() ?? string.Empty : string.Empty;

                UnsubscribeMethod? unsubscribeMethod = null;
                if (classification.TryGetProperty("unsubscribeMethod", out var unsubscribeElement))
                {
                    var typeStr = unsubscribeElement.TryGetProperty("type", out var typeElement) ? typeElement.GetString() : null;
                    var value = unsubscribeElement.TryGetProperty("value", out var valueElement) ? valueElement.GetString() : null;

                    if (Enum.TryParse<UnsubscribeType>(typeStr, true, out var unsubscribeType))
                    {
                        unsubscribeMethod = new UnsubscribeMethod { Type = unsubscribeType, Value = value };
                    }
                }

                if (Enum.TryParse<EmailClassification>(classificationStr, true, out var emailClassification) &&
                    Enum.TryParse<Likelihood>(likelihoodStr, true, out var likelihood))
                {
                    items.Add(new ClassifyItem
                    {
                        EmailId = emailId,
                        Classification = emailClassification,
                        Likelihood = likelihood,
                        Confidence = confidence,
                        Reasons = reasons,
                        BulkKey = bulkKey,
                        UnsubscribeMethod = unsubscribeMethod
                    });
                }
            }

            return new ClassifyOutput { Items = items };
        }
        catch (Exception)
        {
            // Fallback: create basic classifications for all emails
            var fallbackItems = originalEmails.Select(email => new ClassifyItem
            {
                EmailId = email.Id,
                Classification = EmailClassification.Unknown,
                Likelihood = Likelihood.Unsure,
                Confidence = 0.1,
                Reasons = new[] { "AI classification failed" },
                BulkKey = $"from:{ExtractDomain(email.Headers.GetValueOrDefault("From", ""))}"
            }).ToArray();

            return new ClassifyOutput { Items = fallbackItems };
        }
    }

    private static GroupOutput ParseGroupingResponse(string jsonResponse)
    {
        try
        {
            var json = ExtractJsonFromResponse(jsonResponse);
            using var document = JsonDocument.Parse(json);
            var root = document.RootElement;

            var groups = new List<BulkGroup>();

            if (root.TryGetProperty("bulkGroups", out var groupsElement))
            {
                foreach (var group in groupsElement.EnumerateArray())
                {
                    var id = group.TryGetProperty("id", out var idElement) ? idElement.GetString() ?? string.Empty : Guid.NewGuid().ToString();
                    var label = group.TryGetProperty("simpleLabel", out var labelElement) ? labelElement.GetString() ?? "Unknown group" : "Unknown group";
                    var count = group.TryGetProperty("emailCount", out var countElement) ? countElement.GetInt32() : 0;
                    var actionTypeStr = group.TryGetProperty("actionType", out var actionElement) ? actionElement.GetString() : "Keep";

                    if (Enum.TryParse<BulkActionType>(actionTypeStr, true, out var actionType))
                    {
                        groups.Add(new BulkGroup
                        {
                            Id = id,
                            SimpleLabel = label,
                            EmailCount = count,
                            ActionType = actionType,
                            Undoable = actionType != BulkActionType.Delete
                        });
                    }
                }
            }

            return new GroupOutput { BulkGroups = groups };
        }
        catch
        {
            return new GroupOutput { BulkGroups = Array.Empty<BulkGroup>() };
        }
    }

    private static GroupOutput CreateFallbackGrouping(IReadOnlyList<ClassifyItem> emails)
    {
        var groups = emails
            .Where(e => e.Classification != EmailClassification.Keep)
            .GroupBy(e => e.Classification)
            .Select(g => new BulkGroup
            {
                Id = Guid.NewGuid().ToString(),
                SimpleLabel = $"{g.Key} emails ({g.Count()} items)",
                EmailCount = g.Count(),
                ActionType = g.Key == EmailClassification.Newsletter ? BulkActionType.UnsubscribeAndDelete : BulkActionType.Delete,
                Undoable = true
            })
            .ToArray();

        return new GroupOutput { BulkGroups = groups };
    }

    private static string ExtractJsonFromResponse(string response)
    {
        // Remove markdown code block formatting if present
        response = response.Trim();
        if (response.StartsWith("```json"))
            response = response.Substring(7);
        if (response.StartsWith("```"))
            response = response.Substring(3);
        if (response.EndsWith("```"))
            response = response.Substring(0, response.Length - 3);

        return response.Trim();
    }

    private static string ExtractDomain(string email)
    {
        if (string.IsNullOrEmpty(email))
            return "unknown";

        var atIndex = email.LastIndexOf('@');
        if (atIndex > 0 && atIndex < email.Length - 1)
            return email.Substring(atIndex + 1).Trim('>', ' ');

        return "unknown";
    }

    /// <summary>
    /// Performs provider cleanup when shutting down
    /// </summary>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>Result indicating success or failure</returns>
    protected override async Task<Result<bool>> PerformShutdownAsync(CancellationToken cancellationToken)
    {
        try
        {
            _client = null;
            _openAIClient = null;
            _cachedHealthResult = null;
            _lastApiCheck = null;

            Logger.LogInformation("OpenAI provider shut down successfully");
            await Task.CompletedTask; // Keep async for interface compatibility
            return Result<bool>.Success(true);
        }
        catch (Exception ex)
        {
            Logger.LogError(ex, "Error during OpenAI provider shutdown");
            return Result<bool>.Failure(ex.ToProviderError("Failed to shutdown OpenAI provider"));
        }
    }

    /// <summary>
    /// Disposes of provider resources
    /// </summary>
    /// <param name="disposing">Whether this is being called from Dispose()</param>
    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            _healthCheckSemaphore?.Dispose();
        }
        base.Dispose(disposing);
    }
}