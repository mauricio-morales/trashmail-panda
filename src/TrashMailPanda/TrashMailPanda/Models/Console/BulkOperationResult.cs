namespace TrashMailPanda.Models.Console;

/// <summary>
/// Result of a bulk operation: successful count and any failed email IDs.
/// A partial success is normal — failures are collected without aborting the batch.
/// </summary>
public sealed record BulkOperationResult(
    int SuccessCount,
    IReadOnlyList<string> FailedIds
);
