using System.ComponentModel.DataAnnotations;
using TrashMailPanda.Shared.Models;

namespace TrashMailPanda.Models;

/// <summary>
/// Provider display information for UI binding and presentation
/// </summary>
public record ProviderDisplayInfo
{
    /// <summary>
    /// Internal provider name used for identification
    /// </summary>
    [Required]
    public string Name { get; init; } = string.Empty;

    /// <summary>
    /// User-friendly display name
    /// </summary>
    [Required]
    public string DisplayName { get; init; } = string.Empty;

    /// <summary>
    /// Description of what this provider does
    /// </summary>
    [Required]
    public string Description { get; init; } = string.Empty;

    /// <summary>
    /// Type of provider (Storage, Email, LLM)
    /// </summary>
    public ProviderType Type { get; init; }

    /// <summary>
    /// Whether this provider is required for the application to function
    /// </summary>
    public bool IsRequired { get; init; } = true;

    /// <summary>
    /// Whether multiple instances of this provider type are allowed
    /// </summary>
    public bool AllowsMultiple { get; init; } = false;

    /// <summary>
    /// Icon or emoji to display for this provider
    /// </summary>
    public string Icon { get; init; } = string.Empty;

    /// <summary>
    /// Setup complexity level for user guidance
    /// </summary>
    public SetupComplexity Complexity { get; init; } = SetupComplexity.Simple;

    /// <summary>
    /// Estimated setup time in minutes
    /// </summary>
    public int EstimatedSetupTimeMinutes { get; init; } = 2;

    /// <summary>
    /// Additional requirements or prerequisites for setup
    /// </summary>
    public string Prerequisites { get; init; } = string.Empty;
}

/// <summary>
/// Provider setup flow state management
/// Tracks the current state of provider configuration and setup
/// </summary>
public record ProviderSetupState
{
    /// <summary>
    /// Provider name being configured
    /// </summary>
    [Required]
    public string ProviderName { get; init; } = string.Empty;

    /// <summary>
    /// Current step in the setup process
    /// </summary>
    public SetupStep CurrentStep { get; init; } = SetupStep.NotStarted;

    /// <summary>
    /// Whether setup is currently in progress
    /// </summary>
    public bool IsInProgress { get; init; } = false;

    /// <summary>
    /// Current error message if setup failed
    /// </summary>
    public string? ErrorMessage { get; init; }

    /// <summary>
    /// Temporary setup data (e.g., partial credentials during flow)
    /// This data should be cleared after setup completion
    /// </summary>
    public Dictionary<string, object> SetupData { get; init; } = new();

    /// <summary>
    /// Progress percentage for long-running setup operations
    /// </summary>
    public int ProgressPercentage { get; init; } = 0;

    /// <summary>
    /// Whether this setup can be retried
    /// </summary>
    public bool CanRetry { get; init; } = true;

    /// <summary>
    /// Setup attempt count (for limiting retries)
    /// </summary>
    public int AttemptCount { get; init; } = 0;

    /// <summary>
    /// Maximum number of setup attempts before requiring user intervention
    /// </summary>
    public int MaxAttempts { get; init; } = 3;

    /// <summary>
    /// Whether setup requires user interaction (OAuth flow, manual input, etc.)
    /// </summary>
    public bool RequiresUserInteraction { get; init; } = true;

    /// <summary>
    /// Timestamp of the last setup attempt
    /// </summary>
    public DateTime? LastAttempt { get; init; }
}


/// <summary>
/// Setup process steps for tracking configuration progress
/// </summary>
public enum SetupStep
{
    /// <summary>
    /// Setup has not been started
    /// </summary>
    NotStarted,

    /// <summary>
    /// Preparing setup (validating prerequisites, etc.)
    /// </summary>
    Preparing,

    /// <summary>
    /// Gathering user input (credentials, API keys, etc.)
    /// </summary>
    GatheringInput,

    /// <summary>
    /// Performing authentication (OAuth flow, API key validation, etc.)
    /// </summary>
    Authenticating,

    /// <summary>
    /// Configuring provider settings
    /// </summary>
    Configuring,

    /// <summary>
    /// Testing connectivity and configuration
    /// </summary>
    Testing,

    /// <summary>
    /// Finalizing setup and storing credentials securely
    /// </summary>
    Finalizing,

    /// <summary>
    /// Setup completed successfully
    /// </summary>
    Completed,

    /// <summary>
    /// Setup failed - user intervention required
    /// </summary>
    Failed,

    /// <summary>
    /// Setup was cancelled by user
    /// </summary>
    Cancelled
}

/// <summary>
/// Setup complexity levels for user guidance and expectation setting
/// </summary>
public enum SetupComplexity
{
    /// <summary>
    /// Simple setup - one-click or minimal user input
    /// </summary>
    Simple,

    /// <summary>
    /// Moderate setup - requires some user input or external account
    /// </summary>
    Moderate,

    /// <summary>
    /// Complex setup - multiple steps, external configuration required
    /// </summary>
    Complex
}

