using System;
using System.Threading;
using System.Threading.Tasks;
using TrashMailPanda.Models;
using TrashMailPanda.Shared.Base;

namespace TrashMailPanda.Services;

/// <summary>
/// Retention enforcement service for time-bounded archive labels.
/// Scans <c>email_features</c> for archived emails whose retention threshold has elapsed
/// and deletes them from Gmail.
/// <para>
/// IMPORTANT: <c>training_label</c> is NEVER modified by this service.
/// The content-based label (e.g. "Archive for 30d") is preserved for ML training integrity.
/// </para>
/// </summary>
public interface IRetentionEnforcementService
{
    /// <summary>
    /// Executes a full retention scan: fetch archived feature vectors with time-bounded labels,
    /// compute elapsed days for each, delete expired emails from Gmail, and persist
    /// <c>last_scan_utc</c> on completion (even partial success).
    /// </summary>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>
    /// Success: <see cref="RetentionScanResult"/> with counts and any failed IDs.
    /// Failure: <see cref="StorageError"/> if the feature archive cannot be read.
    /// </returns>
    Task<Result<RetentionScanResult>> RunScanAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Reads <c>last_scan_utc</c> from the config store.
    /// </summary>
    /// <returns>
    /// Success: UTC timestamp of the last completed scan, or <c>null</c> if no scan has ever run.
    /// Failure: <see cref="StorageError"/> on database error.
    /// </returns>
    Task<Result<DateTime?>> GetLastScanTimeAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Returns <c>true</c> when the time elapsed since the last scan meets or exceeds
    /// <see cref="TrashMailPanda.Models.RetentionEnforcementOptions.PromptThresholdDays"/>.
    /// Always returns <c>true</c> if no scan has ever run.
    /// </summary>
    Task<Result<bool>> ShouldPromptAsync(CancellationToken cancellationToken = default);
}
