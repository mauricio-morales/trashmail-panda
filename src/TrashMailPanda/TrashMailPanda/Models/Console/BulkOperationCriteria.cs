namespace TrashMailPanda.Models.Console;

/// <summary>
/// Encapsulates filter parameters for selecting emails in a bulk operation.
/// At least one filter must be non-null before execution.
/// </summary>
public sealed class BulkOperationCriteria
{
    /// <summary>Filter by sender address or domain (e.g. "@newsletter.com").</summary>
    public string? Sender { get; init; }

    /// <summary>Filter by Gmail label name.</summary>
    public string? Label { get; init; }

    public DateTime? DateFrom { get; init; }
    public DateTime? DateTo { get; init; }

    /// <summary>Include only emails whose size is at most this many bytes.</summary>
    public long? SizeBytes { get; init; }

    /// <summary>Include only emails where AI confidence score meets or exceeds this threshold (0.0–1.0).</summary>
    public float? AiConfidenceThreshold { get; init; }
}
