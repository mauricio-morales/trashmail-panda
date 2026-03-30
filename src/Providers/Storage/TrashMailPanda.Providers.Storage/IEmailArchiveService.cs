using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using TrashMailPanda.Shared.Base;
using TrashMailPanda.Providers.Storage.Models;

namespace TrashMailPanda.Providers.Storage;

/// <summary>
/// Provides email archive and feature storage operations for ML training.
/// Extends the IStorageProvider with ML-specific storage capabilities.
/// </summary>
/// <remarks>
/// This interface defines the public contract for storing, retrieving, and managing
/// email feature vectors and complete email archives. All operations follow the
/// Result&lt;T&gt; pattern and never throw exceptions.
/// 
/// Storage is managed with configurable limits (default 50GB). Automatic cleanup
/// removes oldest full email archives when capacity is reached, while preserving
/// feature vectors for training.
/// </remarks>
public interface IEmailArchiveService
{
    // ============================================================
    // Feature Vector Storage
    // ============================================================

    /// <summary>
    /// Stores a single email feature vector.
    /// </summary>
    /// <param name="feature">The feature vector to store</param>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>
    /// Success: true if stored successfully
    /// Failure: ValidationError if feature is invalid, StorageError if database operation fails
    /// </returns>
    Task<Result<bool>> StoreFeatureAsync(
        EmailFeatureVector feature,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Stores multiple email feature vectors in a single batch transaction.
    /// Optimized for bulk inserts during archive scanning.
    /// </summary>
    /// <param name="features">Collection of feature vectors to store</param>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>
    /// Success: number of features stored
    /// Failure: ValidationError if any feature is invalid, StorageError if database operation fails
    /// </returns>
    Task<Result<int>> StoreFeaturesBatchAsync(
        IEnumerable<EmailFeatureVector> features,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Retrieves a single feature vector by email ID.
    /// </summary>
    /// <param name="emailId">The email ID to retrieve</param>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>
    /// Success: EmailFeatureVector if found, null if not found
    /// Failure: StorageError if database operation fails
    /// </returns>
    Task<Result<EmailFeatureVector?>> GetFeatureAsync(
        string emailId,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Retrieves all feature vectors for ML training.
    /// </summary>
    /// <param name="schemaVersion">Optional schema version filter (null = all versions)</param>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>
    /// Success: Collection of all stored feature vectors
    /// Failure: StorageError if database operation fails
    /// </returns>
    Task<Result<IEnumerable<EmailFeatureVector>>> GetAllFeaturesAsync(
        int? schemaVersion = null,
        CancellationToken cancellationToken = default);

    // ============================================================
    // Email Archive Storage
    // ============================================================

    /// <summary>
    /// Stores a complete email archive entry.
    /// Subject to storage capacity limits.
    /// </summary>
    /// <param name="archive">The email archive to store</param>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>
    /// Success: true if stored successfully
    /// Failure: ValidationError if archive is invalid, 
    ///          QuotaExceededError if storage limit reached,
    ///          StorageError if database operation fails
    /// </returns>
    Task<Result<bool>> StoreArchiveAsync(
        EmailArchiveEntry archive,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Stores multiple email archives in a single batch transaction.
    /// Checks storage capacity before inserting.
    /// </summary>
    /// <param name="archives">Collection of email archives to store</param>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>
    /// Success: number of archives stored (may be less than input if quota reached)
    /// Failure: ValidationError if any archive is invalid, StorageError if database operation fails
    /// </returns>
    Task<Result<int>> StoreArchivesBatchAsync(
        IEnumerable<EmailArchiveEntry> archives,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Retrieves a single email archive by email ID.
    /// </summary>
    /// <param name="emailId">The email ID to retrieve</param>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>
    /// Success: EmailArchiveEntry if found, null if not found
    /// Failure: StorageError if database operation fails
    /// </returns>
    Task<Result<EmailArchiveEntry?>> GetArchiveAsync(
        string emailId,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Deletes email archive for a specific email ID.
    /// Feature vector is preserved.
    /// </summary>
    /// <param name="emailId">The email ID to delete</param>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>
    /// Success: true if deleted, false if not found
    /// Failure: StorageError if database operation fails
    /// </returns>
    Task<Result<bool>> DeleteArchiveAsync(
        string emailId,
        CancellationToken cancellationToken = default);

    // ============================================================
    // Storage Monitoring & Quota
    // ============================================================

    /// <summary>
    /// Gets current storage usage statistics.
    /// </summary>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>
    /// Success: StorageQuota with current usage metrics
    /// Failure: StorageError if database operation fails
    /// </returns>
    Task<Result<StorageQuota>> GetStorageUsageAsync(
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Updates the configured storage limit.
    /// </summary>
    /// <param name="limitBytes">New storage limit in bytes</param>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>
    /// Success: true if updated successfully
    /// Failure: ValidationError if limit &lt;= 0, StorageError if database operation fails
    /// </returns>
    Task<Result<bool>> UpdateStorageLimitAsync(
        long limitBytes,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Checks if cleanup should be triggered based on current usage.
    /// </summary>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>
    /// Success: true if usage >= trigger threshold (default 90%)
    /// Failure: StorageError if database operation fails
    /// </returns>
    Task<Result<bool>> ShouldTriggerCleanupAsync(
        CancellationToken cancellationToken = default);

    // ============================================================
    // Schema Version Checks
    // ============================================================

    /// <summary>
    /// Returns <c>true</c> if any feature rows exist with a schema version older than
    /// <paramref name="currentVersion"/>, indicating that a full re-scan is required.
    /// Returns <c>false</c> when the table is empty (fresh install — no re-scan needed).
    /// </summary>
    Task<Result<bool>> HasOutdatedFeaturesAsync(int currentVersion, CancellationToken ct = default);

    // ============================================================
    // Triage Queue & Training Labels
    // ============================================================

    /// <summary>
    /// Sets the explicit training label for the given email's feature vector.
    /// Also sets <c>UserCorrected = 1</c> when the user overrode an AI recommendation.
    /// Returns <c>Success(false)</c> if no feature vector exists for the email ID.
    /// MUST only be called after the corresponding Gmail action has succeeded.
    /// </summary>
    Task<Result<bool>> SetTrainingLabelAsync(
        string emailId,
        string label,
        bool userCorrected,
        CancellationToken ct = default);

    /// <summary>
    /// Returns the count of feature vectors with an explicit training label.
    /// Used to seed <c>EmailTriageSession.LabeledCount</c> at session start.
    /// </summary>
    Task<Result<int>> CountLabeledAsync(CancellationToken ct = default);

    /// <summary>
    /// Returns a page of feature vectors with <c>training_label IS NULL</c> (untriaged queue).
    /// Only includes Inbox and Archive emails — Sent and Trash folders are excluded because
    /// they represent definitive user intent and should not be re-triaged.
    /// Ordered by: Inbox first (priority 1), Archive second (priority 2),
    /// then by email recency (<c>EmailAgeDays ASC</c>, most recent first within each group).
    /// </summary>
    Task<Result<IReadOnlyList<EmailFeatureVector>>> GetUntriagedAsync(
        int pageSize,
        int offset,
        CancellationToken ct = default);

    /// <summary>
    /// Returns a page of archived, already-labeled emails where
    /// <c>user_corrected = 0</c> and <c>EmailAgeDays &lt;= maxAgeDays</c>,
    /// eligible for re-triage. These are emails whose archive label was assigned
    /// (by the AI or a previous user pass) but may now be worth deleting given
    /// the passage of time.
    /// Ordered by <c>EmailAgeDays ASC</c> (most recently archived first).
    /// The pool naturally shrinks as each reviewed email is marked <c>user_corrected = 1</c>.
    /// </summary>
    Task<Result<IReadOnlyList<EmailFeatureVector>>> GetRetriagedCandidatesAsync(
        int maxAgeDays,
        int pageSize,
        int offset,
        CancellationToken ct = default);

    // ============================================================
    // Automatic Cleanup
    // ============================================================

    /// <summary>
    /// Executes automatic cleanup to reduce storage usage.
    /// Removes oldest non-user-corrected email archives first, then user-corrected if needed.
    /// Feature vectors are always preserved.
    /// </summary>
    /// <param name="targetPercent">Target usage percentage after cleanup (default 80%)</param>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>
    /// Success: number of archives deleted
    /// Failure: StorageError if database operation fails
    /// </returns>
    Task<Result<int>> ExecuteCleanupAsync(
        int targetPercent = 80,
        CancellationToken cancellationToken = default);

    // ============================================================
    // Bootstrap / Seeding
    // ============================================================

    /// <summary>
    /// Labels un-labeled Starred or Important emails as 'Keep' training examples.
    /// Idempotent: only updates rows where <c>training_label IS NULL</c>, so existing
    /// user corrections and AI-assigned labels are never overwritten.
    /// </summary>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>
    /// Success: number of feature rows updated
    /// Failure: StorageError if database operation fails
    /// </returns>
    Task<Result<int>> BootstrapStarredImportantLabelsAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Returns the count of total <c>user_corrected=1</c> rows grouped by <c>training_label</c>.
    /// Used by <see cref="Services.IModelQualityMonitor"/> for quality tracking and retrain suggestions.
    /// </summary>
    Task<Result<IReadOnlyDictionary<string, int>>> GetUserCorrectedCountsByLabelAsync(
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Returns Gmail message IDs that exist in <c>training_emails</c> but have no corresponding
    /// row in <c>email_features</c>. These are emails that were captured by the incremental
    /// History sync but never had feature vectors built (pre-fix data).
    /// </summary>
    /// <param name="accountId">Account to scope the query.</param>
    /// <param name="limit">Maximum number of IDs to return per call (for batching).</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>List of orphaned email IDs, oldest-first (by ImportedAt).</returns>
    Task<Result<IReadOnlyList<string>>> GetOrphanedTrainingEmailIdsAsync(
        string accountId,
        int limit = 500,
        CancellationToken cancellationToken = default);
}
