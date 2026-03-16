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
/// Domain service implementation for managing user-defined email filtering rules.
/// NOTE: AlwaysKeepRules and AutoTrashRules are immutable types with init-only properties.
/// Modifications require creating new instances.
/// </summary>
public class UserRulesService : IUserRulesService
{
    private readonly IStorageRepository _repository;
    private readonly ILogger<UserRulesService> _logger;

    public UserRulesService(IStorageRepository repository, ILogger<UserRulesService> logger)
    {
        _repository = repository ?? throw new ArgumentNullException(nameof(repository));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    public async Task<Result<UserRules>> GetUserRulesAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            _logger.LogDebug("Retrieving user rules");

            // Get all rules - there should only be one record
            var rulesResult = await _repository.GetAllAsync<UserRules>(cancellationToken);

            if (!rulesResult.IsSuccess)
            {
                return Result<UserRules>.Failure(rulesResult.Error);
            }

            var rules = rulesResult.Value.FirstOrDefault();

            if (rules == null)
            {
                // No rules exist yet - create default
                _logger.LogInformation("No user rules found, creating defaults");
                rules = new UserRules();

                var addResult = await _repository.AddAsync(rules, cancellationToken);
                if (!addResult.IsSuccess)
                {
                    return Result<UserRules>.Failure(addResult.Error);
                }
            }

            _logger.LogDebug("Retrieved user rules with {AlwaysKeepCount} always-keep and {AutoTrashCount} auto-trash senders",
                rules.AlwaysKeep.Senders.Count, rules.AutoTrash.Senders.Count);

            return Result<UserRules>.Success(rules);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to retrieve user rules");
            return Result<UserRules>.Failure(new StorageError($"Failed to retrieve user rules: {ex.Message}"));
        }
    }

    public async Task<Result<bool>> UpdateUserRulesAsync(UserRules rules, CancellationToken cancellationToken = default)
    {
        try
        {
            if (rules == null)
            {
                return Result<bool>.Failure(new ValidationError("User rules cannot be null"));
            }

            _logger.LogDebug("Updating user rules");

            // Validate rules
            var validationResult = ValidateRules(rules);
            if (!validationResult.IsSuccess)
            {
                return validationResult;
            }

            // Update the existing rules
            var updateResult = await _repository.UpdateAsync(rules, cancellationToken);

            if (!updateResult.IsSuccess)
            {
                return Result<bool>.Failure(updateResult.Error);
            }

            _logger.LogInformation("Updated user rules with {AlwaysKeepCount} always-keep and {AutoTrashCount} auto-trash senders",
                rules.AlwaysKeep.Senders.Count, rules.AutoTrash.Senders.Count);

            return Result<bool>.Success(true);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to update user rules");
            return Result<bool>.Failure(new StorageError($"Failed to update user rules: {ex.Message}"));
        }
    }

    public async Task<Result<bool>> AddAlwaysKeepSenderAsync(string sender, CancellationToken cancellationToken = default)
    {
        try
        {
            if (string.IsNullOrWhiteSpace(sender))
            {
                return Result<bool>.Failure(new ValidationError("Sender email cannot be empty"));
            }

            _logger.LogDebug("Adding {Sender} to always-keep list", sender);

            var rulesResult = await GetUserRulesAsync(cancellationToken);
            if (!rulesResult.IsSuccess)
            {
                return Result<bool>.Failure(rulesResult.Error);
            }

            var currentRules = rulesResult.Value;

            // If already in always-keep and not in auto-trash, nothing to do
            if (currentRules.AlwaysKeep.Senders.Contains(sender) && !currentRules.AutoTrash.Senders.Contains(sender))
            {
                _logger.LogDebug("{Sender} already in always-keep list", sender);
                return Result<bool>.Success(true);
            }

            // Create new AutoTrash without this sender
            var newAutoTrashSenders = currentRules.AutoTrash.Senders.Where(s => s != sender).ToArray();
            var newAutoTrash = new AutoTrashRules
            {
                Senders = newAutoTrashSenders,
                Domains = currentRules.AutoTrash.Domains,
                ListIds = currentRules.AutoTrash.ListIds,
                Templates = currentRules.AutoTrash.Templates
            };

            // Create new AlwaysKeep with this sender
            var newAlwaysKeepSenders = currentRules.AlwaysKeep.Senders.Contains(sender)
                ? currentRules.AlwaysKeep.Senders
                : currentRules.AlwaysKeep.Senders.Append(sender).ToArray();

            var newAlwaysKeep = new AlwaysKeepRules
            {
                Senders = newAlwaysKeepSenders,
                Domains = currentRules.AlwaysKeep.Domains,
                ListIds = currentRules.AlwaysKeep.ListIds
            };

            var newRules = new UserRules
            {
                AlwaysKeep = newAlwaysKeep,
                AutoTrash = newAutoTrash,
                Weights = currentRules.Weights,
                Exclusions = currentRules.Exclusions
            };

            _logger.LogInformation("Added {Sender} to always-keep list", sender);
            return await UpdateUserRulesAsync(newRules, cancellationToken);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to add sender {Sender} to always-keep list", sender);
            return Result<bool>.Failure(new StorageError($"Failed to add sender to always-keep list: {ex.Message}"));
        }
    }

    public async Task<Result<bool>> AddAutoTrashSenderAsync(string sender, CancellationToken cancellationToken = default)
    {
        try
        {
            if (string.IsNullOrWhiteSpace(sender))
            {
                return Result<bool>.Failure(new ValidationError("Sender email cannot be empty"));
            }

            _logger.LogDebug("Adding {Sender} to auto-trash list", sender);

            var rulesResult = await GetUserRulesAsync(cancellationToken);
            if (!rulesResult.IsSuccess)
            {
                return Result<bool>.Failure(rulesResult.Error);
            }

            var currentRules = rulesResult.Value;

            // If already in auto-trash and not in always-keep, nothing to do
            if (currentRules.AutoTrash.Senders.Contains(sender) && !currentRules.AlwaysKeep.Senders.Contains(sender))
            {
                _logger.LogDebug("{Sender} already in auto-trash list", sender);
                return Result<bool>.Success(true);
            }

            // Create new AlwaysKeep without this sender
            var newAlwaysKeepSenders = currentRules.AlwaysKeep.Senders.Where(s => s != sender).ToArray();
            var newAlwaysKeep = new AlwaysKeepRules
            {
                Senders = newAlwaysKeepSenders,
                Domains = currentRules.AlwaysKeep.Domains,
                ListIds = currentRules.AlwaysKeep.ListIds
            };

            // Create new AutoTrash with this sender
            var newAutoTrashSenders = currentRules.AutoTrash.Senders.Contains(sender)
                ? currentRules.AutoTrash.Senders
                : currentRules.AutoTrash.Senders.Append(sender).ToArray();

            var newAutoTrash = new AutoTrashRules
            {
                Senders = newAutoTrashSenders,
                Domains = currentRules.AutoTrash.Domains,
                ListIds = currentRules.AutoTrash.ListIds,
                Templates = currentRules.AutoTrash.Templates
            };

            var newRules = new UserRules
            {
                AlwaysKeep = newAlwaysKeep,
                AutoTrash = newAutoTrash,
                Weights = currentRules.Weights,
                Exclusions = currentRules.Exclusions
            };

            _logger.LogInformation("Added {Sender} to auto-trash list", sender);
            return await UpdateUserRulesAsync(newRules, cancellationToken);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to add sender {Sender} to auto-trash list", sender);
            return Result<bool>.Failure(new StorageError($"Failed to add sender to auto-trash list: {ex.Message}"));
        }
    }

    public async Task<Result<bool>> RemoveSenderAsync(string sender, CancellationToken cancellationToken = default)
    {
        try
        {
            if (string.IsNullOrWhiteSpace(sender))
            {
                return Result<bool>.Failure(new ValidationError("Sender email cannot be empty"));
            }

            _logger.LogDebug("Removing {Sender} from all rule lists", sender);

            var rulesResult = await GetUserRulesAsync(cancellationToken);
            if (!rulesResult.IsSuccess)
            {
                return Result<bool>.Failure(rulesResult.Error);
            }

            var currentRules = rulesResult.Value;

            var wasInKeep = currentRules.AlwaysKeep.Senders.Contains(sender);
            var wasInTrash = currentRules.AutoTrash.Senders.Contains(sender);

            if (!wasInKeep && !wasInTrash)
            {
                _logger.LogDebug("{Sender} not found in any rule lists", sender);
                return Result<bool>.Success(false);
            }

            // Create new rules without this sender
            var newAlwaysKeepSenders = currentRules.AlwaysKeep.Senders.Where(s => s != sender).ToArray();
            var newAlwaysKeep = new AlwaysKeepRules
            {
                Senders = newAlwaysKeepSenders,
                Domains = currentRules.AlwaysKeep.Domains,
                ListIds = currentRules.AlwaysKeep.ListIds
            };

            var newAutoTrashSenders = currentRules.AutoTrash.Senders.Where(s => s != sender).ToArray();
            var newAutoTrash = new AutoTrashRules
            {
                Senders = newAutoTrashSenders,
                Domains = currentRules.AutoTrash.Domains,
                ListIds = currentRules.AutoTrash.ListIds,
                Templates = currentRules.AutoTrash.Templates
            };

            var newRules = new UserRules
            {
                AlwaysKeep = newAlwaysKeep,
                AutoTrash = newAutoTrash,
                Weights = currentRules.Weights,
                Exclusions = currentRules.Exclusions
            };

            _logger.LogInformation("Removed {Sender} from rule lists (keep: {RemovedFromKeep}, trash: {RemovedFromTrash})",
                sender, wasInKeep, wasInTrash);

            var updateResult = await UpdateUserRulesAsync(newRules, cancellationToken);
            return updateResult.IsSuccess ? Result<bool>.Success(true) : updateResult;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to remove sender {Sender} from rule lists", sender);
            return Result<bool>.Failure(new StorageError($"Failed to remove sender from rule lists: {ex.Message}"));
        }
    }

    private Result<bool> ValidateRules(UserRules rules)
    {
        // Check for duplicates
        var intersection = rules.AlwaysKeep.Senders.Intersect(rules.AutoTrash.Senders).ToList();
        if (intersection.Any())
        {
            return Result<bool>.Failure(new ValidationError(
                $"Senders cannot be in both AlwaysKeep and AutoTrash lists: {string.Join(", ", intersection)}"));
        }

        // Validate email addresses
        foreach (var sender in rules.AlwaysKeep.Senders.Concat(rules.AutoTrash.Senders))
        {
            if (string.IsNullOrWhiteSpace(sender))
            {
                return Result<bool>.Failure(new ValidationError("Rule lists cannot contain empty email addresses"));
            }
        }

        return Result<bool>.Success(true);
    }
}
