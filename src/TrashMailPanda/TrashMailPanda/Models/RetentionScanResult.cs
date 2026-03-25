using System;
using System.Collections.Generic;

namespace TrashMailPanda.Models;

/// <summary>
/// Immutable result record for a retention enforcement scan.
/// Returned by <see cref="TrashMailPanda.Services.IRetentionEnforcementService.RunScanAsync"/>.
/// </summary>
public readonly record struct RetentionScanResult
{
    /// <summary>Total emails examined (time-bounded label AND is_archived = 1).</summary>
    public int ScannedCount { get; init; }

    /// <summary>Emails successfully deleted from Gmail (age ≥ threshold).</summary>
    public int DeletedCount { get; init; }

    /// <summary>Emails under threshold at scan time — not yet expired.</summary>
    public int SkippedCount { get; init; }

    /// <summary>Gmail IDs where the delete operation failed; does not include skipped emails.</summary>
    public IReadOnlyList<string> FailedIds { get; init; }

    /// <summary>UTC timestamp when the scan completed.</summary>
    public DateTime RanAtUtc { get; init; }

    /// <summary>True when at least one delete operation failed.</summary>
    public bool HasFailures => FailedIds.Count > 0;

    /// <summary>True when at least one email was successfully deleted.</summary>
    public bool AnyDeleted => DeletedCount > 0;
}
