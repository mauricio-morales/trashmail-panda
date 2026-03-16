using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using TrashMailPanda.Shared;
using TrashMailPanda.Shared.Base;

namespace TrashMailPanda.Providers.Storage.Services;

/// <summary>
/// Domain service implementation for managing email classification history and analytics.
/// </summary>
public class ClassificationHistoryService : IClassificationHistoryService
{
    private readonly IStorageRepository _repository;
    private readonly ILogger<ClassificationHistoryService> _logger;

    public ClassificationHistoryService(IStorageRepository repository, ILogger<ClassificationHistoryService> logger)
    {
        _repository = repository ?? throw new ArgumentNullException(nameof(repository));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    public async Task<Result<IReadOnlyList<ClassificationHistoryItem>>> GetHistoryAsync(
        HistoryFilters? filters = null,
        CancellationToken cancellationToken = default)
    {
        try
        {
            _logger.LogDebug("Retrieving classification history with filters: {Filters}", filters != null ? "applied" : "none");

            Result<IEnumerable<ClassificationHistoryItem>> result;

            if (filters == null)
            {
                // No filters - get all
                result = await _repository.GetAllAsync<ClassificationHistoryItem>(cancellationToken);
            }
            else
            {
                // Apply filters via query
                result = await _repository.QueryAsync<ClassificationHistoryItem>(
                    item =>
                        (filters.After == null || item.Timestamp >= filters.After) &&
                        (filters.Before == null || item.Timestamp <= filters.Before) &&
                        (filters.Classifications == null || filters.Classifications.Count == 0 || filters.Classifications.Contains(item.Classification)) &&
                        (filters.UserActions == null || filters.UserActions.Count == 0 ||
                         (item.UserAction != null && filters.UserActions.Any(ua => ua.ToString() == item.UserAction))),
                    cancellationToken);
            }

            if (!result.IsSuccess)
            {
                return Result<IReadOnlyList<ClassificationHistoryItem>>.Failure(result.Error);
            }

            var historyList = result.Value.ToList();

            // Apply limit if specified
            if (filters?.Limit > 0)
            {
                historyList = historyList.Take(filters.Limit.Value).ToList();
            }

            _logger.LogDebug("Retrieved {Count} classification history items", historyList.Count);

            return Result<IReadOnlyList<ClassificationHistoryItem>>.Success(historyList);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to retrieve classification history");
            return Result<IReadOnlyList<ClassificationHistoryItem>>.Failure(
                new StorageError($"Failed to retrieve classification history: {ex.Message}"));
        }
    }

    public async Task<Result<bool>> AddClassificationResultAsync(
        ClassificationHistoryItem result,
        CancellationToken cancellationToken = default)
    {
        try
        {
            if (result == null)
            {
                return Result<bool>.Failure(new ValidationError("Classification result cannot be null"));
            }

            if (string.IsNullOrWhiteSpace(result.EmailId))
            {
                return Result<bool>.Failure(new ValidationError("Email ID cannot be empty"));
            }

            if (string.IsNullOrWhiteSpace(result.Classification))
            {
                return Result<bool>.Failure(new ValidationError("Classification cannot be empty"));
            }

            _logger.LogDebug("Adding classification result for email {EmailId}: {Classification}",
                result.EmailId, result.Classification);

            var addResult = await _repository.AddAsync(result, cancellationToken);

            if (!addResult.IsSuccess)
            {
                return Result<bool>.Failure(addResult.Error);
            }

            _logger.LogInformation("Added classification result for email {EmailId}", result.EmailId);

            return Result<bool>.Success(true);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to add classification result for email {EmailId}", result?.EmailId);
            return Result<bool>.Failure(new StorageError($"Failed to add classification result: {ex.Message}"));
        }
    }

    public async Task<Result<ClassificationMetrics>> GetAccuracyMetricsAsync(
        DateTime? startDate = null,
        DateTime? endDate = null,
        CancellationToken cancellationToken = default)
    {
        try
        {
            _logger.LogDebug("Calculating classification accuracy metrics (start: {StartDate}, end: {EndDate})",
                startDate?.ToString("yyyy-MM-dd") ?? "any", endDate?.ToString("yyyy-MM-dd") ?? "any");

            // Get filtered history
            var historyResult = await _repository.QueryAsync<ClassificationHistoryItem>(
                item =>
                    (startDate == null || item.Timestamp >= startDate) &&
                    (endDate == null || item.Timestamp <= endDate),
                cancellationToken);

            if (!historyResult.IsSuccess)
            {
                return Result<ClassificationMetrics>.Failure(historyResult.Error);
            }

            var history = historyResult.Value.ToList();

            if (history.Count == 0)
            {
                _logger.LogDebug("No classification history found for metrics calculation");
                return Result<ClassificationMetrics>.Success(new ClassificationMetrics
                {
                    Precision = 0,
                    Recall = 0,
                    F1Score = 0,
                    TotalClassifications = 0,
                    CorrectClassifications = 0
                });
            }

            // Calculate metrics
            var totalClassifications = history.Count;
            var correctClassifications = history.Count(item => string.IsNullOrEmpty(item.UserAction));
            var incorrectClassifications = history.Count(item => !string.IsNullOrEmpty(item.UserAction));

            var precision = totalClassifications > 0
                ? (double)correctClassifications / totalClassifications
                : 0;

            // For recall and F1, we'd need positive/negative labels
            // Using simple accuracy-based metrics for now
            var recall = precision; // Simplified - would need actual positive/negative counts
            var f1Score = precision > 0 || recall > 0
                ? 2 * (precision * recall) / (precision + recall)
                : 0;

            var metrics = new ClassificationMetrics
            {
                Precision = precision,
                Recall = recall,
                F1Score = f1Score,
                TotalClassifications = totalClassifications,
                CorrectClassifications = correctClassifications
            };

            _logger.LogInformation("Calculated metrics: Precision={Precision:P2}, Recall={Recall:P2}, F1={F1:P2}, Total={Total}",
                metrics.Precision, metrics.Recall, metrics.F1Score, metrics.TotalClassifications);

            return Result<ClassificationMetrics>.Success(metrics);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to calculate accuracy metrics");
            return Result<ClassificationMetrics>.Failure(
                new StorageError($"Failed to calculate accuracy metrics: {ex.Message}"));
        }
    }

    public async Task<Result<UserFeedbackStats>> GetFeedbackStatsAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            _logger.LogDebug("Retrieving user feedback statistics");

            var historyResult = await _repository.GetAllAsync<ClassificationHistoryItem>(cancellationToken);

            if (!historyResult.IsSuccess)
            {
                return Result<UserFeedbackStats>.Failure(historyResult.Error);
            }

            var history = historyResult.Value.ToList();

            var totalFeedback = history.Count;
            var positiveFeedback = history.Count(item => string.IsNullOrEmpty(item.UserAction));
            var negativeFeedback = history.Count(item => !string.IsNullOrEmpty(item.UserAction));

            var feedbackByClassification = history
                .GroupBy(item => item.Classification)
                .ToDictionary(
                    g => g.Key,
                    g => g.Count());

            var stats = new UserFeedbackStats
            {
                TotalFeedback = totalFeedback,
                PositiveFeedback = positiveFeedback,
                NegativeFeedback = negativeFeedback,
                FeedbackByClassification = feedbackByClassification
            };

            _logger.LogInformation("Feedback stats: Total={Total}, Positive={Positive}, Negative={Negative}",
                stats.TotalFeedback, stats.PositiveFeedback, stats.NegativeFeedback);

            return Result<UserFeedbackStats>.Success(stats);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to retrieve feedback statistics");
            return Result<UserFeedbackStats>.Failure(
                new StorageError($"Failed to retrieve feedback statistics: {ex.Message}"));
        }
    }

    public async Task<Result<int>> DeleteHistoryOlderThanAsync(
        DateTime olderThan,
        CancellationToken cancellationToken = default)
    {
        try
        {
            _logger.LogDebug("Deleting classification history older than {Date}", olderThan.ToString("yyyy-MM-dd"));

            // Find old records
            var oldRecordsResult = await _repository.QueryAsync<ClassificationHistoryItem>(
                item => item.Timestamp < olderThan,
                cancellationToken);

            if (!oldRecordsResult.IsSuccess)
            {
                return Result<int>.Failure(oldRecordsResult.Error);
            }

            var oldRecords = oldRecordsResult.Value.ToList();

            if (oldRecords.Count == 0)
            {
                _logger.LogDebug("No classification history found older than {Date}", olderThan.ToString("yyyy-MM-dd"));
                return Result<int>.Success(0);
            }

            // Delete in batch
            var deleteResult = await _repository.DeleteRangeAsync(oldRecords, cancellationToken);

            if (!deleteResult.IsSuccess)
            {
                return Result<int>.Failure(deleteResult.Error);
            }

            _logger.LogInformation("Deleted {Count} classification history records older than {Date}",
                deleteResult.Value, olderThan.ToString("yyyy-MM-dd"));

            return deleteResult;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to delete old classification history");
            return Result<int>.Failure(new StorageError($"Failed to delete old classification history: {ex.Message}"));
        }
    }
}
