namespace TrashMailPanda.Models.Console;

/// <summary>
/// Represents the health status of a provider after health check.
/// Maps to Microsoft.Extensions.Diagnostics.HealthChecks.HealthStatus.
/// </summary>
public enum HealthStatus
{
    /// <summary>
    /// Provider is fully operational.
    /// </summary>
    Healthy,

    /// <summary>
    /// Provider is operational but experiencing issues.
    /// </summary>
    Degraded,

    /// <summary>
    /// Provider is not operational.
    /// </summary>
    Critical,

    /// <summary>
    /// Health status not yet determined or health check not performed.
    /// </summary>
    Unknown
}
