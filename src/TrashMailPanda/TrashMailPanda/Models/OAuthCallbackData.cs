namespace TrashMailPanda.Models;

/// <summary>
/// Represents OAuth callback parameters received from authorization server
/// </summary>
public record OAuthCallbackData
{
    /// <summary>
    /// Authorization code from Google (single-use, short-lived ~10 minutes)
    /// </summary>
    public string? Code { get; init; }

    /// <summary>
    /// State parameter for CSRF protection (must match original request)
    /// </summary>
    public string? State { get; init; }

    /// <summary>
    /// Error code if authorization failed (e.g., "access_denied")
    /// </summary>
    public string? Error { get; init; }

    /// <summary>
    /// Human-readable error description
    /// </summary>
    public string? ErrorDescription { get; init; }

    /// <summary>
    /// Timestamp when callback was received
    /// </summary>
    public DateTime ReceivedAt { get; init; } = DateTime.UtcNow;

    /// <summary>
    /// Check if callback represents an error (user denied, etc.)
    /// </summary>
    public bool IsError => !string.IsNullOrEmpty(Error);

    /// <summary>
    /// Check if callback is valid (has code, no errors)
    /// </summary>
    public bool IsValid => !string.IsNullOrEmpty(Code) && !IsError;
}
