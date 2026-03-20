using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using TrashMailPanda.Models.Console;
using TrashMailPanda.Providers.ML;
using TrashMailPanda.Providers.ML.Config;
using TrashMailPanda.Providers.ML.Models;
using TrashMailPanda.Providers.Storage;
using TrashMailPanda.Providers.Storage.Models;
using TrashMailPanda.Shared;
using TrashMailPanda.Shared.Base;

namespace TrashMailPanda.Services;

/// <summary>
/// UI-agnostic triage business logic.
/// Detects cold-start vs AI-assisted mode, sources the untriaged email queue from
/// <c>IEmailArchiveService</c>, and executes dual-write triage decisions
/// (Gmail action first, training label on success).
/// </summary>
public sealed class EmailTriageService : IEmailTriageService
{
    private readonly IEmailProvider _emailProvider;
    private readonly IMLModelProvider _mlProvider;
    private readonly IEmailArchiveService _archiveService;
    private readonly MLModelProviderConfig _mlConfig;
    private readonly ILogger<EmailTriageService> _logger;

    public EmailTriageService(
        IEmailProvider emailProvider,
        IMLModelProvider mlProvider,
        IEmailArchiveService archiveService,
        IOptions<MLModelProviderConfig> mlConfig,
        ILogger<EmailTriageService> logger)
    {
        _emailProvider = emailProvider ?? throw new ArgumentNullException(nameof(emailProvider));
        _mlProvider = mlProvider ?? throw new ArgumentNullException(nameof(mlProvider));
        _archiveService = archiveService ?? throw new ArgumentNullException(nameof(archiveService));
        _mlConfig = mlConfig?.Value ?? throw new ArgumentNullException(nameof(mlConfig));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    // ──────────────────────────────────────────────────────────────────────────
    // T028 — GetSessionInfoAsync
    // ──────────────────────────────────────────────────────────────────────────

    public async Task<Result<TriageSessionInfo>> GetSessionInfoAsync(
        CancellationToken cancellationToken = default)
    {
        var modelResult = await _mlProvider.GetActiveModelVersionAsync("action", cancellationToken);
        var mode = modelResult.IsSuccess ? TriageMode.AiAssisted : TriageMode.ColdStart;

        var countResult = await _archiveService.CountLabeledAsync(cancellationToken);
        var count = countResult.IsSuccess ? countResult.Value : 0;

        return Result<TriageSessionInfo>.Success(new TriageSessionInfo(
            Mode: mode,
            LabeledCount: count,
            LabelingThreshold: _mlConfig.MinTrainingSamples,
            ThresholdAlreadyReached: count >= _mlConfig.MinTrainingSamples
        ));
    }

    // ──────────────────────────────────────────────────────────────────────────
    // T029 — GetNextBatchAsync
    // ──────────────────────────────────────────────────────────────────────────

    public async Task<Result<IReadOnlyList<EmailFeatureVector>>> GetNextBatchAsync(
        int pageSize,
        int offset,
        CancellationToken cancellationToken = default)
    {
        return await _archiveService.GetUntriagedAsync(pageSize, offset, cancellationToken);
    }

    // ──────────────────────────────────────────────────────────────────────────
    // T030 — GetAiRecommendationAsync
    // ──────────────────────────────────────────────────────────────────────────

    public async Task<Result<ActionPrediction?>> GetAiRecommendationAsync(
        EmailFeatureVector feature,
        CancellationToken cancellationToken = default)
    {
        var sessionInfo = await GetSessionInfoAsync(cancellationToken);
        if (!sessionInfo.IsSuccess)
            return Result<ActionPrediction?>.Success(null);

        if (sessionInfo.Value.Mode == TriageMode.ColdStart)
            return Result<ActionPrediction?>.Success(null);

        var predResult = await _mlProvider.ClassifyActionAsync(feature, cancellationToken);
        if (!predResult.IsSuccess)
        {
            _logger.LogWarning("AI classification failed for email {EmailId}: {Error}",
                feature.EmailId, predResult.Error.Message);
            return Result<ActionPrediction?>.Success(null);
        }

        return Result<ActionPrediction?>.Success(predResult.Value);
    }

    // ──────────────────────────────────────────────────────────────────────────
    // T031 — ApplyDecisionAsync (dual-write)
    // ──────────────────────────────────────────────────────────────────────────

    public async Task<Result<TriageDecision>> ApplyDecisionAsync(
        string emailId,
        string chosenAction,
        string? aiRecommendation,
        CancellationToken cancellationToken = default)
    {
        // Step 1: Execute Gmail action FIRST.
        // On failure: return error — training_label is NOT stored (no false training signal).
        var actionResult = await ExecuteGmailActionAsync(emailId, chosenAction, cancellationToken);
        if (!actionResult.IsSuccess)
        {
            _logger.LogWarning("Gmail action {Action} failed for email {EmailId}: {Error}",
                chosenAction, emailId, actionResult.Error.Message);
            return Result<TriageDecision>.Failure(actionResult.Error);
        }

        // Step 2: Store training label only on Gmail success.
        var isOverride = aiRecommendation is not null && chosenAction != aiRecommendation;
        var labelResult = await _archiveService.SetTrainingLabelAsync(emailId, chosenAction, isOverride, cancellationToken);
        if (!labelResult.IsSuccess)
        {
            _logger.LogWarning("Failed to store training label for {EmailId}: {Error}",
                emailId, labelResult.Error.Message);
            // Non-fatal: Gmail action already succeeded; training label failure is a best-effort
        }

        return Result<TriageDecision>.Success(new TriageDecision(
            EmailId: emailId,
            ChosenAction: chosenAction,
            AiRecommendation: aiRecommendation,
            ConfidenceScore: null,
            IsOverride: isOverride,
            DecidedAtUtc: DateTime.UtcNow
        ));
    }

    // ──────────────────────────────────────────────────────────────────────────
    // Private helpers
    // ──────────────────────────────────────────────────────────────────────────

    private async Task<Result<bool>> ExecuteGmailActionAsync(
        string emailId,
        string action,
        CancellationToken cancellationToken)
    {
        return action switch
        {
            "Keep" => await ApplyKeepAsync(emailId, cancellationToken),
            "Archive" => await ApplyArchiveAsync(emailId, cancellationToken),
            "Delete" => await ApplyDeleteAsync(emailId, cancellationToken),
            "Spam" => await _emailProvider.ReportSpamAsync(emailId),
            _ => Result<bool>.Failure(new ValidationError($"Unknown triage action: '{action}'"))
        };
    }

    private async Task<Result<bool>> ApplyKeepAsync(string emailId, CancellationToken ct)
    {
        // Keep: ensure the email has INBOX label
        var request = new BatchModifyRequest
        {
            EmailIds = [emailId],
            AddLabelIds = ["INBOX"],
        };
        return await _emailProvider.BatchModifyAsync(request);
    }

    private async Task<Result<bool>> ApplyArchiveAsync(string emailId, CancellationToken ct)
    {
        // Archive: remove INBOX label
        var request = new BatchModifyRequest
        {
            EmailIds = [emailId],
            RemoveLabelIds = ["INBOX"],
        };
        return await _emailProvider.BatchModifyAsync(request);
    }

    private async Task<Result<bool>> ApplyDeleteAsync(string emailId, CancellationToken ct)
    {
        // Delete: move to TRASH
        var request = new BatchModifyRequest
        {
            EmailIds = [emailId],
            AddLabelIds = ["TRASH"],
            RemoveLabelIds = ["INBOX"],
        };
        return await _emailProvider.BatchModifyAsync(request);
    }
}
