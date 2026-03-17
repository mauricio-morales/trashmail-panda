namespace TrashMailPanda.Models.Console;

/// <summary>
/// Represents the criticality of a provider during startup initialization.
/// </summary>
public enum ProviderType
{
    /// <summary>
    /// Provider is required for application operation.
    /// Startup will fail if this provider fails to initialize.
    /// </summary>
    Required,

    /// <summary>
    /// Provider is optional for application operation.
    /// Startup will continue even if this provider fails to initialize.
    /// </summary>
    Optional
}
