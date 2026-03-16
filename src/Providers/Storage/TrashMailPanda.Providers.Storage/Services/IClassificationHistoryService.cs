using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using TrashMailPanda.Shared;
using TrashMailPanda.Shared.Base;

namespace TrashMailPanda.Providers.Storage.Services;

/// <summary>
/// Domain service for managing email classification history and analytics.
/// Tracks AI classification decisions and user feedback for model training.
/// </summary>
public interface IClassificationHistoryService
{
    /// <summary>
    /// Retrieves classification history with optional filters.
    /// </summary>
    /// <param name="filters">Optional filtering criteria</param>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>
    /// Success: Collection of classification history items
    /// Failure: StorageError if database operation fails
    /// </returns>
    Task<Result<IReadOnlyList<ClassificationHistoryItem>>> GetHistoryAsync(
        HistoryFilters? filters = null,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Adds a new classification result to the history.
    /// </summary>
    /// <param name="result">The classification result to record</param>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>
    /// Success: true if added successfully
    /// Failure: ValidationError if result is invalid, StorageError if database operation fails
    /// </returns>
    Task<Result<bool>> AddClassificationResultAsync(
        ClassificationHistoryItem result,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Retrieves classification accuracy metrics.
    /// </summary>
    /// <param name="startDate">Optional start date for metrics calculation</param>
    /// <param name="endDate">Optional end date for metrics calculation</param>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>
    /// Success: Accuracy metrics including precision, recall, F1 score
    /// Failure: StorageError if database operation fails
    /// </returns>
    Task<Result<ClassificationMetrics>> GetAccuracyMetricsAsync(
        System.DateTime? startDate = null,
        System.DateTime? endDate = null,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Retrieves user feedback statistics for model improvement.
    /// </summary>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>
    /// Success: User feedback statistics by classification type
    /// Failure: StorageError if database operation fails
    /// </returns>
    Task<Result<UserFeedbackStats>> GetFeedbackStatsAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Deletes classification history older than the specified date.
    /// Used for data retention policy compliance.
    /// </summary>
    /// <param name="olderThan">Delete records older than this date</param>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>
    /// Success: Number of records deleted
    /// Failure: StorageError if database operation fails
    /// </returns>
    Task<Result<int>> DeleteHistoryOlderThanAsync(
        System.DateTime olderThan,
        CancellationToken cancellationToken = default);
}

/// <summary>
/// Classification accuracy metrics.
/// </summary>
public record ClassificationMetrics
{
    public double Precision { get; init; }
    public double Recall { get; init; }
    public double F1Score { get; init; }
    public int TotalClassifications { get; init; }
    public int CorrectClassifications { get; init; }
}

/// <summary>
/// User feedback statistics for model training.
/// </summary>
public record UserFeedbackStats
{
    public int TotalFeedback { get; init; }
    public int PositiveFeedback { get; init; }
    public int NegativeFeedback { get; init; }
    public Dictionary<string, int> FeedbackByClassification { get; init; } = new();
}
