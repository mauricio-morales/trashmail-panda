namespace TrashMailPanda.Models.Console;

/// <summary>
/// Live-fetched email details used to enrich the triage card display.
/// Obtained via a Gmail API call at card render time (format=FULL).
/// </summary>
public record EmailTriageDetails(
    /// <summary>Full RFC 5322 From header value, e.g. "Jane Doe &lt;jane@example.com&gt;"</summary>
    string From,

    /// <summary>Gmail thread ID — used to construct the browser deep-link URL.</summary>
    string ThreadId,

    /// <summary>Plain-text body, decoded from the message payload. Null if unavailable.</summary>
    string? BodyText);
