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
    /// Ordered by <c>ExtractedAt</c> descending (most recently scanned first).
    /// </summary>
    Task<Result<IReadOnlyList<EmailFeatureVector>>> GetUntriagedAsync(
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
}
