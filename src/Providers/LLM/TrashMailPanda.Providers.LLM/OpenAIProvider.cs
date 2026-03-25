using OpenAI.Chat;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;
using TrashMailPanda.Shared;

namespace TrashMailPanda.Providers.LLM;

/// <summary>
/// OpenAI implementation of ILLMProvider
/// Provides email classification using GPT-4o-mini for cost optimization
/// </summary>
public class OpenAIProvider : ILLMProvider
{
    private ChatClient? _client;
    private readonly string _model = "gpt-4o-mini";

    public string Name => "OpenAI";

    public async Task InitAsync(LLMAuth auth)
    {
        if (auth is not LLMAuth.ApiKey apiKeyAuth)
            throw new ArgumentException("OpenAI provider requires API key authentication", nameof(auth));

        try
        {
            _client = new ChatClient(_model, apiKeyAuth.Key);

            // Test the connection with a simple request
            var testMessages = new List<ChatMessage>
            {
                new SystemChatMessage("You are a helpful assistant."),
                new UserChatMessage("Test connection.")
            };

            var testResponse = await _client.CompleteChatAsync(testMessages);
            if (testResponse.Value == null)
                throw new InvalidOperationException("Failed to connect to OpenAI API - no response received");
        }
        catch (Exception ex)
        {
            throw new InvalidOperationException($"Failed to initialize OpenAI provider: {ex.Message}", ex);
        }
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
}