using System;
using System.ComponentModel.DataAnnotations;
using TrashMailPanda.Shared.Models;
using TrashMailPanda.Shared.Base;

namespace TrashMailPanda.Providers.LLM;

/// <summary>
/// Configuration for the OpenAI LLM provider
/// Provides OpenAI-specific configuration including API key, model settings, and rate limiting
/// </summary>
public sealed class OpenAIConfig : BaseProviderConfig
{
    /// <summary>
    /// Gets or sets the provider name identifier
    /// </summary>
    public new string Name { get; set; } = "OpenAI";

    /// <summary>
    /// Gets or sets tags for categorizing and filtering providers
    /// </summary>
    public new List<string> Tags { get; set; } = new() { "llm", "openai", "ai", "classification" };

    /// <summary>
    /// Gets or sets the OpenAI API key for authentication
    /// </summary>
    [Required(ErrorMessage = "OpenAI API key is required")]
    [StringLength(200, MinimumLength = 20, ErrorMessage = "API key must be between 20 and 200 characters")]
    public string ApiKey { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets the OpenAI model to use for chat completions
    /// </summary>
    [Required(ErrorMessage = "Model name is required")]
    [StringLength(100, MinimumLength = 1, ErrorMessage = "Model name must be between 1 and 100 characters")]
    public string Model { get; set; } = "gpt-4o-mini";

    /// <summary>
    /// Gets or sets the maximum number of tokens to generate in responses
    /// </summary>
    [Range(1, 16384, ErrorMessage = "Max tokens must be between 1 and 16384")]
    public int MaxTokens { get; set; } = 2048;

    /// <summary>
    /// Gets or sets the temperature for response randomness (0.0 = deterministic, 1.0 = very random)
    /// </summary>
    [Range(0.0, 2.0, ErrorMessage = "Temperature must be between 0.0 and 2.0")]
    public double Temperature { get; set; } = 0.1;

    /// <summary>
    /// Gets or sets the timeout for individual OpenAI API requests
    /// </summary>
    public TimeSpan RequestTimeout { get; set; } = TimeSpan.FromMinutes(2);

    /// <summary>
    /// Gets or sets the maximum number of requests per minute (rate limiting)
    /// </summary>
    [Range(1, 1000, ErrorMessage = "Rate limit must be between 1 and 1000 requests per minute")]
    public int MaxRequestsPerMinute { get; set; } = 60;

    /// <summary>
    /// Gets or sets the maximum number of tokens per minute (rate limiting)
    /// </summary>
    [Range(1000, 1000000, ErrorMessage = "Token rate limit must be between 1000 and 1000000 tokens per minute")]
    public int MaxTokensPerMinute { get; set; } = 40000;

    /// <summary>
    /// Gets or sets the interval for health check API calls (in minutes)
    /// </summary>
    [Range(1, 60, ErrorMessage = "Health check interval must be between 1 and 60 minutes")]
    public int HealthCheckIntervalMinutes { get; set; } = 5;

    /// <summary>
    /// Gets or sets whether to cache successful health check results
    /// </summary>
    public bool CacheHealthCheckResults { get; set; } = true;

    /// <summary>
    /// Gets or sets the organization ID for OpenAI API requests (optional)
    /// </summary>
    [StringLength(100, ErrorMessage = "Organization ID must be less than 100 characters")]
    public string? OrganizationId { get; set; }

    /// <summary>
    /// Gets or sets whether to treat 429 rate limit errors as healthy (API key is valid)
    /// </summary>
    public bool TreatRateLimitAsHealthy { get; set; } = true;

    /// <summary>
    /// Performs OpenAI-specific configuration validation
    /// </summary>
    /// <returns>A result indicating whether the configuration is valid</returns>
    public override Result ValidateConfiguration()
    {
        // Perform base validation first
        var baseResult = base.ValidateConfiguration();
        if (baseResult.IsFailure)
            return baseResult;

        // OpenAI-specific validation
        if (string.IsNullOrWhiteSpace(ApiKey))
            return Result.Failure(new ValidationError("OpenAI API key cannot be empty"));

        if (!ApiKey.StartsWith("sk-"))
            return Result.Failure(new ValidationError("OpenAI API key must start with 'sk-'"));

        if (string.IsNullOrWhiteSpace(Model))
            return Result.Failure(new ValidationError("OpenAI model name cannot be empty"));

        // Validate timeout configuration
        if (RequestTimeout.TotalSeconds > TimeoutSeconds)
            return Result.Failure(new ValidationError("Request timeout cannot exceed provider timeout"));

        // Validate rate limiting configuration
        if (MaxRequestsPerMinute <= 0)
            return Result.Failure(new ValidationError("Max requests per minute must be greater than 0"));

        if (MaxTokensPerMinute <= 0)
            return Result.Failure(new ValidationError("Max tokens per minute must be greater than 0"));

        return ValidateCustomLogic();
    }

    /// <summary>
    /// Performs additional OpenAI-specific validation
    /// </summary>
    /// <returns>A result indicating whether custom validation passed</returns>
    protected override Result ValidateCustomLogic()
    {
        // Validate model name against known OpenAI models
        var supportedModels = new[]
        {
            "gpt-4o-mini",
            "gpt-4o",
            "gpt-4-turbo",
            "gpt-4",
            "gpt-3.5-turbo",
            "gpt-3.5-turbo-16k"
        };

        if (!supportedModels.Contains(Model))
        {
            return Result.Failure(new ValidationError($"Model '{Model}' is not in the list of supported models. Supported models: {string.Join(", ", supportedModels)}"));
        }

        // Validate health check interval is reasonable
        if (HealthCheckIntervalMinutes < 1)
            return Result.Failure(new ValidationError("Health check interval must be at least 1 minute"));

        // Validate token limits for the selected model
        var maxTokensForModel = Model switch
        {
            "gpt-4o-mini" => 16384,
            "gpt-4o" => 4096,
            "gpt-4-turbo" => 4096,
            "gpt-4" => 8192,
            "gpt-3.5-turbo" => 4096,
            "gpt-3.5-turbo-16k" => 16384,
            _ => 4096
        };

        if (MaxTokens > maxTokensForModel)
        {
            return Result.Failure(new ValidationError($"Max tokens ({MaxTokens}) exceeds model limit ({maxTokensForModel}) for {Model}"));
        }

        return Result.Success();
    }

    /// <summary>
    /// Gets a sanitized copy of the configuration with sensitive information removed
    /// </summary>
    /// <returns>A sanitized copy of the configuration</returns>
    public override BaseProviderConfig GetSanitizedCopy()
    {
        var copy = (OpenAIConfig)MemberwiseClone();
        copy.ApiKey = "sk-***REDACTED***";
        return copy;
    }

    /// <summary>
    /// Creates a configuration for development/testing with safe defaults
    /// </summary>
    /// <param name="apiKey">The OpenAI API key</param>
    /// <returns>A development-ready configuration</returns>
    public static OpenAIConfig CreateDevelopmentConfig(string apiKey)
    {
        return new OpenAIConfig
        {
            ApiKey = apiKey,
            Model = "gpt-4o-mini",
            MaxTokens = 1024,
            Temperature = 0.1,
            RequestTimeout = TimeSpan.FromSeconds(30),
            MaxRequestsPerMinute = 20,
            MaxTokensPerMinute = 10000,
            HealthCheckIntervalMinutes = 5,
            CacheHealthCheckResults = true,
            TreatRateLimitAsHealthy = true,
            TimeoutSeconds = 60,
            MaxRetryAttempts = 3,
            RetryDelayMilliseconds = 1000,
            IsEnabled = true
        };
    }

    /// <summary>
    /// Creates a configuration for production with optimal settings
    /// </summary>
    /// <param name="apiKey">The OpenAI API key</param>
    /// <param name="organizationId">Optional organization ID</param>
    /// <returns>A production-ready configuration</returns>
    public static OpenAIConfig CreateProductionConfig(string apiKey, string? organizationId = null)
    {
        return new OpenAIConfig
        {
            ApiKey = apiKey,
            Model = "gpt-4o-mini",
            MaxTokens = 2048,
            Temperature = 0.1,
            RequestTimeout = TimeSpan.FromMinutes(2),
            MaxRequestsPerMinute = 60,
            MaxTokensPerMinute = 40000,
            HealthCheckIntervalMinutes = 5,
            CacheHealthCheckResults = true,
            TreatRateLimitAsHealthy = true,
            OrganizationId = organizationId,
            TimeoutSeconds = 120,
            MaxRetryAttempts = 5,
            RetryDelayMilliseconds = 1000,
            IsEnabled = true
        };
    }
}