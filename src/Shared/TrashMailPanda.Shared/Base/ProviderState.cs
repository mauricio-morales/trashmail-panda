using System;
using System.Collections.Generic;

namespace TrashMailPanda.Shared.Base;

/// <summary>
/// Represents the current state of a provider in its lifecycle
/// </summary>
public enum ProviderState
{
    /// <summary>
    /// Provider has not been initialized yet
    /// </summary>
    Uninitialized = 0,

    /// <summary>
    /// Provider is currently being initialized
    /// </summary>
    Initializing = 1,

    /// <summary>
    /// Provider has been successfully initialized and is ready for operations
    /// </summary>
    Ready = 2,

    /// <summary>
    /// Provider is currently processing an operation
    /// </summary>
    Busy = 3,

    /// <summary>
    /// Provider encountered an error and may need re-initialization
    /// </summary>
    Error = 4,

    /// <summary>
    /// Provider is currently being shut down
    /// </summary>
    ShuttingDown = 5,

    /// <summary>
    /// Provider has been shut down and is no longer available
    /// </summary>
    Shutdown = 6,

    /// <summary>
    /// Provider is temporarily suspended (e.g., due to rate limiting)
    /// </summary>
    Suspended = 7
}

/// <summary>
/// Provides information about the current state of a provider
/// </summary>
public sealed record ProviderStateInfo
{
    /// <summary>
    /// Gets the current state of the provider
    /// </summary>
    public ProviderState State { get; init; } = ProviderState.Uninitialized;

    /// <summary>
    /// Gets the timestamp when the provider entered this state
    /// </summary>
    public DateTime StateChangedAt { get; init; } = DateTime.UtcNow;

    /// <summary>
    /// Gets a human-readable description of the current state
    /// </summary>
    public string Description { get; init; } = string.Empty;

    /// <summary>
    /// Gets additional context about the current state
    /// </summary>
    public Dictionary<string, object> Context { get; init; } = new();

    /// <summary>
    /// Gets the error information if the provider is in an error state
    /// </summary>
    public ProviderError? Error { get; init; }

    /// <summary>
    /// Gets the last successful operation timestamp
    /// </summary>
    public DateTime? LastSuccessfulOperation { get; init; }

    /// <summary>
    /// Gets the number of consecutive failures (resets on success)
    /// </summary>
    public int ConsecutiveFailures { get; init; } = 0;

    /// <summary>
    /// Gets a value indicating whether the provider can accept operations
    /// </summary>
    public bool CanAcceptOperations => State is ProviderState.Ready or ProviderState.Busy;

    /// <summary>
    /// Gets a value indicating whether the provider requires re-initialization
    /// </summary>
    public bool RequiresReinitialization => State is ProviderState.Error or ProviderState.Shutdown;

    /// <summary>
    /// Gets a value indicating whether the provider is in a terminal state
    /// </summary>
    public bool IsTerminal => State is ProviderState.Shutdown;

    /// <summary>
    /// Gets a value indicating whether the provider is transitioning states
    /// </summary>
    public bool IsTransitioning => State is ProviderState.Initializing or ProviderState.ShuttingDown;

    /// <summary>
    /// Creates a new ProviderStateInfo with the specified state
    /// </summary>
    /// <param name="state">The new state</param>
    /// <param name="description">Optional description of the state change</param>
    /// <param name="context">Optional additional context</param>
    /// <returns>A new ProviderStateInfo instance</returns>
    public ProviderStateInfo WithState(ProviderState state, string? description = null, Dictionary<string, object>? context = null)
    {
        return this with
        {
            State = state,
            StateChangedAt = DateTime.UtcNow,
            Description = description ?? GetDefaultDescription(state),
            Context = context ?? new Dictionary<string, object>(),
            // Reset error if transitioning to a non-error state
            Error = state == ProviderState.Error ? Error : null
        };
    }

    /// <summary>
    /// Creates a new ProviderStateInfo with an error state
    /// </summary>
    /// <param name="error">The error that occurred</param>
    /// <param name="description">Optional description of the error</param>
    /// <returns>A new ProviderStateInfo instance in error state</returns>
    public ProviderStateInfo WithError(ProviderError error, string? description = null)
    {
        return this with
        {
            State = ProviderState.Error,
            StateChangedAt = DateTime.UtcNow,
            Description = description ?? $"Error: {error.Message}",
            Error = error,
            ConsecutiveFailures = ConsecutiveFailures + 1
        };
    }

    /// <summary>
    /// Creates a new ProviderStateInfo marking a successful operation
    /// </summary>
    /// <param name="description">Optional description of the successful operation</param>
    /// <returns>A new ProviderStateInfo instance with updated success timestamp</returns>
    public ProviderStateInfo WithSuccessfulOperation(string? description = null)
    {
        return this with
        {
            LastSuccessfulOperation = DateTime.UtcNow,
            ConsecutiveFailures = 0,
            Description = description ?? Description
        };
    }

    /// <summary>
    /// Gets the default description for a provider state
    /// </summary>
    /// <param name="state">The provider state</param>
    /// <returns>A default description for the state</returns>
    private static string GetDefaultDescription(ProviderState state) => state switch
    {
        ProviderState.Uninitialized => "Provider has not been initialized",
        ProviderState.Initializing => "Provider is initializing",
        ProviderState.Ready => "Provider is ready for operations",
        ProviderState.Busy => "Provider is processing operations",
        ProviderState.Error => "Provider encountered an error",
        ProviderState.ShuttingDown => "Provider is shutting down",
        ProviderState.Shutdown => "Provider has been shut down",
        ProviderState.Suspended => "Provider is temporarily suspended",
        _ => $"Provider is in {state} state"
    };

    /// <summary>
    /// Returns a string representation of the provider state info
    /// </summary>
    /// <returns>A string representation</returns>
    public override string ToString()
    {
        var result = $"{State}: {Description}";
        if (Error != null)
            result += $" (Error: {Error.Message})";
        return result;
    }
}

/// <summary>
/// State transition definitions for provider lifecycle management
/// </summary>
public static class ProviderStateTransitions
{
    /// <summary>
    /// Defines valid state transitions for providers
    /// </summary>
    private static readonly Dictionary<ProviderState, HashSet<ProviderState>> ValidTransitions = new()
    {
        [ProviderState.Uninitialized] = new() { ProviderState.Initializing, ProviderState.Shutdown },
        [ProviderState.Initializing] = new() { ProviderState.Ready, ProviderState.Error, ProviderState.Shutdown },
        [ProviderState.Ready] = new() { ProviderState.Busy, ProviderState.Error, ProviderState.ShuttingDown, ProviderState.Suspended },
        [ProviderState.Busy] = new() { ProviderState.Busy, ProviderState.Ready, ProviderState.Error, ProviderState.ShuttingDown, ProviderState.Suspended },
        [ProviderState.Error] = new() { ProviderState.Initializing, ProviderState.Shutdown },
        [ProviderState.ShuttingDown] = new() { ProviderState.Shutdown },
        [ProviderState.Shutdown] = new() { ProviderState.Initializing },
        [ProviderState.Suspended] = new() { ProviderState.Ready, ProviderState.Error, ProviderState.ShuttingDown }
    };

    /// <summary>
    /// Determines if a state transition is valid
    /// </summary>
    /// <param name="fromState">The current state</param>
    /// <param name="toState">The target state</param>
    /// <returns>True if the transition is valid</returns>
    public static bool IsValidTransition(ProviderState fromState, ProviderState toState)
    {
        return ValidTransitions.TryGetValue(fromState, out var validStates) && validStates.Contains(toState);
    }

    /// <summary>
    /// Gets all valid next states for the current state
    /// </summary>
    /// <param name="currentState">The current state</param>
    /// <returns>A collection of valid next states</returns>
    public static IEnumerable<ProviderState> GetValidNextStates(ProviderState currentState)
    {
        return ValidTransitions.TryGetValue(currentState, out var validStates) ? validStates : Enumerable.Empty<ProviderState>();
    }

    /// <summary>
    /// Validates a state transition and returns an error if invalid
    /// </summary>
    /// <param name="fromState">The current state</param>
    /// <param name="toState">The target state</param>
    /// <returns>A validation result</returns>
    public static Result ValidateTransition(ProviderState fromState, ProviderState toState)
    {
        if (IsValidTransition(fromState, toState))
            return Result.Success();

        return Result.Failure(new ValidationError(
            $"Invalid state transition from {fromState} to {toState}",
            $"Valid transitions from {fromState}: {string.Join(", ", GetValidNextStates(fromState))}"));
    }
}