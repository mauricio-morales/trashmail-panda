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
/// Domain service implementation for managing email metadata and classification state.
/// </summary>
public class EmailMetadataService : IEmailMetadataService
{
    private readonly IStorageRepository _repository;
    private readonly ILogger<EmailMetadataService> _logger;

    public EmailMetadataService(IStorageRepository repository, ILogger<EmailMetadataService> logger)
    {
        _repository = repository ?? throw new ArgumentNullException(nameof(repository));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    public async Task<Result<EmailMetadata?>> GetEmailMetadataAsync(string emailId, CancellationToken cancellationToken = default)
    {
        try
        {
            if (string.IsNullOrWhiteSpace(emailId))
            {
                return Result<EmailMetadata?>.Failure(new ValidationError("Email ID cannot be empty"));
            }

            _logger.LogDebug("Retrieving email metadata for {EmailId}", emailId);

            var result = await _repository.GetByIdAsync<EmailMetadata>(emailId, cancellationToken);

            if (!result.IsSuccess)
            {
                return Result<EmailMetadata?>.Failure(result.Error);
            }

            if (result.Value != null)
            {
                _logger.LogDebug("Found metadata for {EmailId}: {Classification}", emailId, result.Value.Classification);
            }
            else
            {
                _logger.LogDebug("No metadata found for {EmailId}", emailId);
            }

            return Result<EmailMetadata?>.Success(result.Value);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to retrieve email metadata for {EmailId}", emailId);
            return Result<EmailMetadata?>.Failure(new StorageError($"Failed to retrieve email metadata: {ex.Message}"));
        }
    }

    public async Task<Result<bool>> SetEmailMetadataAsync(string emailId, EmailMetadata metadata, CancellationToken cancellationToken = default)
    {
        try
        {
            if (string.IsNullOrWhiteSpace(emailId))
            {
                return Result<bool>.Failure(new ValidationError("Email ID cannot be empty"));
            }

            if (metadata == null)
            {
                return Result<bool>.Failure(new ValidationError("Email metadata cannot be null"));
            }

            _logger.LogDebug("Setting email metadata for {EmailId}: {Classification}", emailId, metadata.Classification);

            // Check if metadata already exists
            var existingResult = await _repository.GetByIdAsync<EmailMetadata>(emailId, cancellationToken);

            if (!existingResult.IsSuccess)
            {
                return Result<bool>.Failure(existingResult.Error);
            }

            Result<bool> saveResult;
            if (existingResult.Value != null)
            {
                // Update existing metadata
                metadata.Id = emailId; // Ensure ID is set
                saveResult = await _repository.UpdateAsync(metadata, cancellationToken);
                _logger.LogDebug("Updated existing metadata for {EmailId}", emailId);
            }
            else
            {
                // Add new metadata
                metadata.Id = emailId;
                saveResult = await _repository.AddAsync(metadata, cancellationToken);
                _logger.LogDebug("Added new metadata for {EmailId}", emailId);
            }

            if (!saveResult.IsSuccess)
            {
                return Result<bool>.Failure(saveResult.Error);
            }

            return Result<bool>.Success(true);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to set email metadata for {EmailId}", emailId);
            return Result<bool>.Failure(new StorageError($"Failed to set email metadata: {ex.Message}"));
        }
    }

    public async Task<Result<int>> BulkSetEmailMetadataAsync(IReadOnlyList<EmailMetadataEntry> entries, CancellationToken cancellationToken = default)
    {
        try
        {
            if (entries == null || entries.Count == 0)
            {
                _logger.LogDebug("No metadata entries to save");
                return Result<int>.Success(0);
            }

            _logger.LogDebug("Bulk setting metadata for {Count} emails", entries.Count);

            // Validate all entries first
            foreach (var entry in entries)
            {
                if (string.IsNullOrWhiteSpace(entry.Id))
                {
                    return Result<int>.Failure(new ValidationError("Email ID cannot be empty"));
                }
                if (entry.Metadata == null)
                {
                    return Result<int>.Failure(new ValidationError($"Metadata cannot be null for email {entry.Id}"));
                }
            }

            // Execute in a transaction for atomicity
            var transactionResult = await _repository.ExecuteTransactionAsync(async () =>
            {
                var toAdd = new List<EmailMetadata>();
                var toUpdate = new List<EmailMetadata>();

                // Determine which to add vs update
                foreach (var entry in entries)
                {
                    var existingResult = await _repository.GetByIdAsync<EmailMetadata>(entry.Id, cancellationToken);

                    if (!existingResult.IsSuccess)
                    {
                        throw new InvalidOperationException($"Failed to check existing metadata: {existingResult.Error.Message}");
                    }

                    entry.Metadata.Id = entry.Id; // Ensure ID is set

                    if (existingResult.Value != null)
                    {
                        toUpdate.Add(entry.Metadata);
                    }
                    else
                    {
                        toAdd.Add(entry.Metadata);
                    }
                }

                var addedCount = 0;
                var updatedCount = 0;

                // Batch add new entries
                if (toAdd.Any())
                {
                    var addResult = await _repository.AddRangeAsync(toAdd, cancellationToken);
                    if (!addResult.IsSuccess)
                    {
                        throw new InvalidOperationException($"Failed to add metadata entries: {addResult.Error.Message}");
                    }
                    addedCount = addResult.Value;
                }

                // Batch update existing entries
                if (toUpdate.Any())
                {
                    var updateResult = await _repository.UpdateRangeAsync(toUpdate, cancellationToken);
                    if (!updateResult.IsSuccess)
                    {
                        throw new InvalidOperationException($"Failed to update metadata entries: {updateResult.Error.Message}");
                    }
                    updatedCount = updateResult.Value;
                }

                _logger.LogInformation("Bulk set {Count} email metadata entries (added: {Added}, updated: {Updated})",
                    entries.Count, addedCount, updatedCount);

                return Result<int>.Success(addedCount + updatedCount);
            }, cancellationToken);

            if (!transactionResult.IsSuccess)
            {
                return Result<int>.Failure(transactionResult.Error);
            }

            return Result<int>.Success(transactionResult.Value);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to bulk set email metadata");
            return Result<int>.Failure(new StorageError($"Failed to bulk set email metadata: {ex.Message}"));
        }
    }

    public async Task<Result<IEnumerable<EmailMetadata>>> QueryMetadataAsync(
        string? classification = null,
        UserAction? userAction = null,
        int? limit = null,
        CancellationToken cancellationToken = default)
    {
        try
        {
            _logger.LogDebug("Querying email metadata (classification: {Classification}, action: {UserAction}, limit: {Limit})",
                classification ?? "any", userAction?.ToString() ?? "any", limit ?? -1);

            Result<IEnumerable<EmailMetadata>> result;

            if (classification == null && userAction == null)
            {
                // No filters - get all
                result = await _repository.GetAllAsync<EmailMetadata>(cancellationToken);
            }
            else
            {
                // Build predicate based on filters
                result = await _repository.QueryAsync<EmailMetadata>(
                    metadata =>
                        (classification == null || metadata.Classification == classification) &&
                        (userAction == null || metadata.UserAction == userAction),
                    cancellationToken);
            }

            if (!result.IsSuccess)
            {
                return Result<IEnumerable<EmailMetadata>>.Failure(result.Error);
            }

            var results = result.Value;

            // Apply limit if specified
            if (limit.HasValue && limit.Value > 0)
            {
                results = results.Take(limit.Value);
            }

            var resultsList = results.ToList();
            _logger.LogDebug("Found {Count} matching email metadata entries", resultsList.Count);

            return Result<IEnumerable<EmailMetadata>>.Success(resultsList);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to query email metadata");
            return Result<IEnumerable<EmailMetadata>>.Failure(new StorageError($"Failed to query email metadata: {ex.Message}"));
        }
    }

    public async Task<Result<bool>> DeleteEmailMetadataAsync(string emailId, CancellationToken cancellationToken = default)
    {
        try
        {
            if (string.IsNullOrWhiteSpace(emailId))
            {
                return Result<bool>.Failure(new ValidationError("Email ID cannot be empty"));
            }

            _logger.LogDebug("Deleting email metadata for {EmailId}", emailId);

            var result = await _repository.DeleteAsync<EmailMetadata>(emailId, cancellationToken);

            if (!result.IsSuccess)
            {
                return Result<bool>.Failure(result.Error);
            }

            if (result.Value)
            {
                _logger.LogInformation("Deleted email metadata for {EmailId}", emailId);
            }
            else
            {
                _logger.LogDebug("No metadata found to delete for {EmailId}", emailId);
            }

            return result;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to delete email metadata for {EmailId}", emailId);
            return Result<bool>.Failure(new StorageError($"Failed to delete email metadata: {ex.Message}"));
        }
    }
}
