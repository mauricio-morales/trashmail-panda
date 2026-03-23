using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using TrashMailPanda.Providers.Storage;
using TrashMailPanda.Shared;
using TrashMailPanda.Shared.Base;

namespace TrashMailPanda.Services;

/// <summary>
/// Gmail-backed implementation of <see cref="IAutoApplyUndoService"/>.
/// Reverses Gmail labels first; only then writes the training signal.
/// </summary>
public sealed class AutoApplyUndoService : IAutoApplyUndoService
{
    private readonly IEmailProvider _emailProvider;
    private readonly IEmailArchiveService _archiveService;
    private readonly ILogger<AutoApplyUndoService> _logger;

    // Reversal label sets: (labelsToAdd, labelsToRemove)
    private static readonly IReadOnlyDictionary<string, (IReadOnlyList<string> Add, IReadOnlyList<string> Remove)>
        ReversalMap = new Dictionary<string, (IReadOnlyList<string>, IReadOnlyList<string>)>(
            StringComparer.OrdinalIgnoreCase)
        {
            ["Delete"] = (["INBOX"], ["TRASH"]),
            ["Archive"] = (["INBOX"], []),
            ["Spam"] = (["INBOX"], ["SPAM"]),
            ["Keep"] = ([], []),   // no Gmail change needed
        };

    public AutoApplyUndoService(
        IEmailProvider emailProvider,
        IEmailArchiveService archiveService,
        ILogger<AutoApplyUndoService> logger)
    {
        _emailProvider = emailProvider ?? throw new ArgumentNullException(nameof(emailProvider));
        _archiveService = archiveService ?? throw new ArgumentNullException(nameof(archiveService));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    /// <inheritdoc/>
    public async Task<Result<bool>> UndoAsync(
        string emailId,
        string originalAction,
        string correctedAction,
        CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(emailId))
            return Result<bool>.Failure(new ValidationError("emailId cannot be empty"));

        if (string.IsNullOrWhiteSpace(originalAction))
            return Result<bool>.Failure(new ValidationError("originalAction cannot be empty"));

        if (string.IsNullOrWhiteSpace(correctedAction))
            return Result<bool>.Failure(new ValidationError("correctedAction cannot be empty"));

        // Step 1: Reverse Gmail labels (skip when action has no Gmail side-effect)
        if (ReversalMap.TryGetValue(originalAction, out var labels) &&
            (labels.Add.Count > 0 || labels.Remove.Count > 0))
        {
            var gmailResult = await _emailProvider.BatchModifyAsync(new BatchModifyRequest
            {
                EmailIds = [emailId],
                AddLabelIds = labels.Add.Count > 0 ? labels.Add : null,
                RemoveLabelIds = labels.Remove.Count > 0 ? labels.Remove : null,
            });

            if (!gmailResult.IsSuccess)
            {
                _logger.LogWarning(
                    "Gmail reversal failed for email {EmailId} (original: {Original}): {Error}",
                    emailId, originalAction, gmailResult.Error.Message);
                return Result<bool>.Failure(gmailResult.Error);
            }
        }
        else if (!ReversalMap.ContainsKey(originalAction))
        {
            _logger.LogWarning(
                "Unknown originalAction '{Original}' for undo — skipping Gmail reversal.",
                originalAction);
        }

        // Step 2: Write training signal (user correction = high-value signal)
        var labelResult = await _archiveService.SetTrainingLabelAsync(
            emailId, correctedAction, userCorrected: true, ct);

        if (!labelResult.IsSuccess)
        {
            _logger.LogWarning(
                "Training label update failed for email {EmailId}: {Error}",
                emailId, labelResult.Error.Message);
            return Result<bool>.Failure(labelResult.Error);
        }

        _logger.LogInformation(
            "Undid auto-apply: email {EmailId} {Original}→{Corrected} (user corrected)",
            emailId, originalAction, correctedAction);

        return Result<bool>.Success(true);
    }
}
