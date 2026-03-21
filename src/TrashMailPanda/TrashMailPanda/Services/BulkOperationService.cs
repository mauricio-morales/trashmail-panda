using Microsoft.Extensions.Logging;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using TrashMailPanda.Models.Console;
using TrashMailPanda.Providers.Storage;
using TrashMailPanda.Providers.Storage.Models;
using TrashMailPanda.Shared;
using TrashMailPanda.Shared.Base;

namespace TrashMailPanda.Services;

/// <summary>
/// UI-agnostic bulk operation business logic.
/// Filters emails from the local feature store, previews the scope, and executes
/// batch Gmail actions + training label storage.
/// </summary>
public sealed class BulkOperationService : IBulkOperationService
{
    private readonly IEmailArchiveService _archiveService;
    private readonly IEmailProvider _emailProvider;
    private readonly ILogger<BulkOperationService> _logger;

    public BulkOperationService(
        IEmailArchiveService archiveService,
        IEmailProvider emailProvider,
        ILogger<BulkOperationService> logger)
    {
        _archiveService = archiveService ?? throw new ArgumentNullException(nameof(archiveService));
        _emailProvider = emailProvider ?? throw new ArgumentNullException(nameof(emailProvider));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    /// <inheritdoc />
    /// <remarks>
    /// Loads all feature vectors and applies criteria filters in-memory.
    /// Filtering is approximate for fields not directly stored (e.g. SizeBytes uses
    /// <c>EmailSizeLog</c>, date uses <c>ExtractedAt - EmailAgeDays</c>).
    /// </remarks>
    public async Task<Result<IReadOnlyList<EmailFeatureVector>>> PreviewAsync(
        BulkOperationCriteria criteria,
        CancellationToken cancellationToken = default)
    {
        var featuresResult = await _archiveService.GetAllFeaturesAsync(null, cancellationToken);

        if (!featuresResult.IsSuccess)
        {
            return Result<IReadOnlyList<EmailFeatureVector>>.Failure(featuresResult.Error!);
        }

        var filtered = featuresResult.Value
            .Where(v => MatchesCriteria(v, criteria))
            .ToList();

        _logger.LogDebug("BulkOperationService.PreviewAsync: {Total} total, {Matching} matching criteria",
            featuresResult.Value.Count(), filtered.Count);

        return Result<IReadOnlyList<EmailFeatureVector>>.Success(filtered);
    }

    /// <inheritdoc />
    /// <remarks>
    /// Runs each email independently: Gmail action first, then training label on success.
    /// Failures are collected but do not abort the batch.
    /// </remarks>
    public async Task<Result<BulkOperationResult>> ExecuteAsync(
        IReadOnlyList<string> emailIds,
        string action,
        CancellationToken cancellationToken = default)
    {
        if (emailIds == null || emailIds.Count == 0)
            return Result<BulkOperationResult>.Success(new BulkOperationResult(0, Array.Empty<string>()));

        var failedIds = new List<string>();
        var successCount = 0;

        foreach (var emailId in emailIds)
        {
            if (cancellationToken.IsCancellationRequested)
                break;

            // Gmail action first
            var gmailResult = await ExecuteGmailActionAsync(emailId, action, cancellationToken);

            if (!gmailResult.IsSuccess)
            {
                _logger.LogWarning("Bulk Gmail action '{Action}' failed for {EmailId}: {Error}",
                    action, emailId, gmailResult.Error?.Message);
                failedIds.Add(emailId);
                continue;
            }

            // Store training label only after Gmail success
            var labelResult = await _archiveService.SetTrainingLabelAsync(
                emailId, action, userCorrected: false, ct: cancellationToken);

            if (!labelResult.IsSuccess)
            {
                // Label storage failure is non-fatal for bulk ops; Gmail action already applied
                _logger.LogWarning("Training label storage failed for {EmailId}: {Error}",
                    emailId, labelResult.Error?.Message);
            }

            successCount++;
        }

        _logger.LogInformation(
            "Bulk operation '{Action}' completed: {Success} succeeded, {Failed} failed",
            action, successCount, failedIds.Count);

        return Result<BulkOperationResult>.Success(new BulkOperationResult(successCount, failedIds));
    }

    // ── Private helpers ───────────────────────────────────────────────────────

    private static bool MatchesCriteria(EmailFeatureVector vector, BulkOperationCriteria criteria)
    {
        // Sender domain filter
        if (!string.IsNullOrWhiteSpace(criteria.Sender) &&
            !vector.SenderDomain.Contains(criteria.Sender, StringComparison.OrdinalIgnoreCase))
            return false;

        // Date filters (approximate: ExtractedAt - EmailAgeDays)
        if (criteria.DateFrom.HasValue || criteria.DateTo.HasValue)
        {
            var approximateDate = vector.ExtractedAt - TimeSpan.FromDays(vector.EmailAgeDays);

            if (criteria.DateFrom.HasValue && approximateDate < criteria.DateFrom.Value)
                return false;

            if (criteria.DateTo.HasValue && approximateDate > criteria.DateTo.Value)
                return false;
        }

        // Size filter (approximate: exp(EmailSizeLog) ≈ raw byte size)
        if (criteria.SizeBytes.HasValue)
        {
            var approximateSize = (long)Math.Exp(vector.EmailSizeLog);
            if (approximateSize > criteria.SizeBytes.Value)
                return false;
        }

        return true;
    }

    private async Task<Result<bool>> ExecuteGmailActionAsync(
        string emailId,
        string action,
        CancellationToken cancellationToken)
    {
        return action switch
        {
            "Spam" => await _emailProvider.ReportSpamAsync(emailId),

            "Keep" => await _emailProvider.BatchModifyAsync(new BatchModifyRequest
            {
                EmailIds = [emailId],
                AddLabelIds = ["INBOX"],
            }),

            "Archive" => await _emailProvider.BatchModifyAsync(new BatchModifyRequest
            {
                EmailIds = [emailId],
                RemoveLabelIds = ["INBOX"],
            }),

            "Delete" => await _emailProvider.BatchModifyAsync(new BatchModifyRequest
            {
                EmailIds = [emailId],
                AddLabelIds = ["TRASH"],
                RemoveLabelIds = ["INBOX"],
            }),

            _ => Result<bool>.Failure(new ValidationError($"Unknown bulk action: '{action}'")),
        };
    }
}
