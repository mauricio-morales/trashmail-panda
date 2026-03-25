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
using TrashMailPanda.Shared.Labels;
using TrashMailPanda.Shared.Models;

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

    /// Emails older than 5 years trigger entry into the re-triage phase.
    internal const int OldEmailThresholdDays = 5 * 365;   // 1825

    /// Re-triage candidates are archived emails labeled within the last 3 years
    /// (i.e., EmailAgeDays ≤ 1095 at extraction time).
    internal const int RetriagedWindowDays = 3 * 365;     // 1095

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
        var result = await _archiveService.GetUntriagedAsync(pageSize, offset, cancellationToken);
        if (result.IsSuccess)
        {
            var batch = result.Value;
            if (batch.Count > 0)
                _logger.LogDebug("GetNextBatchAsync: returned {Count} emails. Age range {MinAge}-{MaxAge}d. First: [{EmailId}] {Subject}",
                    batch.Count,
                    batch.Min(f => f.EmailAgeDays),
                    batch.Max(f => f.EmailAgeDays),
                    batch[0].EmailId,
                    batch[0].SubjectText);
            else
                _logger.LogDebug("GetNextBatchAsync: queue empty (pageSize={PageSize}, offset={Offset})", pageSize, offset);
        }
        return result;
    }

    // ──────────────────────────────────────────────────────────────────────────
    // GetRetriageQueueAsync — mixed re-triage batch
    // ──────────────────────────────────────────────────────────────────────────

    public async Task<Result<IReadOnlyList<EmailFeatureVector>>> GetRetriageQueueAsync(
        int pageSize,
        CancellationToken cancellationToken = default)
    {
        var halfPage = Math.Max(1, pageSize / 2);

        // Old untriaged emails (Inbox/Archive, EmailAgeDays naturally high by now)
        var untriagedResult = await _archiveService.GetUntriagedAsync(halfPage, 0, cancellationToken);
        if (!untriagedResult.IsSuccess)
            return Result<IReadOnlyList<EmailFeatureVector>>.Failure(untriagedResult.Error);

        // Recently archived emails labeled previously but not yet user-reviewed
        var retriageResult = await _archiveService.GetRetriagedCandidatesAsync(
            RetriagedWindowDays, halfPage, 0, cancellationToken);
        if (!retriageResult.IsSuccess)
            return Result<IReadOnlyList<EmailFeatureVector>>.Failure(retriageResult.Error);

        var untriaged = untriagedResult.Value;
        var retriage = retriageResult.Value;

        // Interleave: alternate one from each pool so the user sees a mix.
        // One old unlabeled email followed by one re-evaluation candidate.
        var mixed = new List<EmailFeatureVector>(untriaged.Count + retriage.Count);
        var maxLen = Math.Max(untriaged.Count, retriage.Count);
        for (var i = 0; i < maxLen; i++)
        {
            if (i < untriaged.Count) mixed.Add(untriaged[i]);
            if (i < retriage.Count) mixed.Add(retriage[i]);
        }

        _logger.LogDebug("GetRetriageQueueAsync: {OldCount} old-untriaged + {RetriagedCount} re-triage candidates → {Total} mixed",
            untriaged.Count, retriage.Count, mixed.Count);
        return Result<IReadOnlyList<EmailFeatureVector>>.Success(mixed);
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
        {
            _logger.LogDebug(
                "GetAiRecommendation [{EmailId}]: skipped — triage mode is ColdStart (no ML model loaded)",
                feature.EmailId);
            return Result<ActionPrediction?>.Success(null);
        }

        _logger.LogDebug(
            "GetAiRecommendation [{EmailId}]: requesting prediction (mode={Mode})",
            feature.EmailId, sessionInfo.Value.Mode);

        // US3: Compute EmailAgeDays fresh from ReceivedDateUtc at inference time.
        // Stored email_age_days reflects age at extraction — may be stale by days/weeks.
        // For ML training rows the stored value is used as-is (age at decision time is what
        // the model should learn from), but at inference we need the current age.
        if (feature.ReceivedDateUtc.HasValue)
        {
            feature.EmailAgeDays = (int)(DateTime.UtcNow - feature.ReceivedDateUtc.Value).TotalDays;
        }

        var predResult = await _mlProvider.ClassifyActionAsync(feature, cancellationToken);
        if (!predResult.IsSuccess)
        {
            _logger.LogWarning("AI classification failed for email {EmailId}: {Error}",
                feature.EmailId, predResult.Error.Message);
            return Result<ActionPrediction?>.Success(null);
        }

        _logger.LogDebug(
            "GetAiRecommendation [{EmailId}]: result={Label} confidence={Confidence:P0}",
            feature.EmailId, predResult.Value.PredictedLabel, predResult.Value.Confidence);

        return Result<ActionPrediction?>.Success(predResult.Value);
    }

    // ──────────────────────────────────────────────────────────────────────────
    // T031 — ApplyDecisionAsync (dual-write)
    // ──────────────────────────────────────────────────────────────────────────

    public async Task<Result<TriageDecision>> ApplyDecisionAsync(
        string emailId,
        string chosenAction,
        string? aiRecommendation,
        bool forceUserCorrected = false,
        CancellationToken cancellationToken = default,
        DateTime? receivedDateUtc = null)
    {
        _logger.LogDebug("ApplyDecisionAsync: [{EmailId}] action={Action} aiRec={AiRec} forceUserCorrected={ForceUserCorrected}",
            emailId, chosenAction, aiRecommendation ?? "none", forceUserCorrected);

        // Step 1: Execute Gmail action FIRST.
        // On failure: return error — training_label is NOT stored (no false training signal).
        var actionResult = await ExecuteGmailActionAsync(emailId, chosenAction, receivedDateUtc, cancellationToken);
        if (!actionResult.IsSuccess)
        {
            _logger.LogWarning("Gmail action {Action} failed for email {EmailId}: {Error}",
                chosenAction, emailId, actionResult.Error.Message);
            return Result<TriageDecision>.Failure(actionResult.Error);
        }

        // Step 2: Store training label only on Gmail success.
        // forceUserCorrected = true for re-triage items: even if the chosen action matches
        // the previous label, we mark user_corrected=1 so the email leaves the re-triage pool.
        var isOverride = forceUserCorrected ||
                         (aiRecommendation is not null && chosenAction != aiRecommendation);
        var labelResult = await _archiveService.SetTrainingLabelAsync(emailId, chosenAction, isOverride, cancellationToken);
        if (!labelResult.IsSuccess)
        {
            _logger.LogWarning("Failed to store training label for {EmailId}: {Error}",
                emailId, labelResult.Error.Message);
            // Non-fatal: Gmail action already succeeded; training label failure is a best-effort
        }

        // Determine whether a time-bounded label triggered immediate deletion
        // (age >= threshold at execution time). The ChosenAction label is preserved as-is
        // for ML training integrity — only the UI feedback differs.
        var wasImmediatelyDeleted = LabelThresholds.TryGetThreshold(chosenAction, out var threshold)
            && receivedDateUtc.HasValue
            && (DateTime.UtcNow - receivedDateUtc.Value).TotalDays >= threshold;

        _logger.LogDebug("ApplyDecisionAsync: [{EmailId}] stored label={Action} isOverride={IsOverride} wasImmediatelyDeleted={WasImmediatelyDeleted}",
            emailId, chosenAction, isOverride, wasImmediatelyDeleted);
        return Result<TriageDecision>.Success(new TriageDecision(
            EmailId: emailId,
            ChosenAction: chosenAction,
            AiRecommendation: aiRecommendation,
            ConfidenceScore: null,
            IsOverride: isOverride,
            DecidedAtUtc: DateTime.UtcNow
        )
        { WasImmediatelyDeleted = wasImmediatelyDeleted });
    }

    // ──────────────────────────────────────────────────────────────────────────
    // T031b — FetchEmailDetailsAsync
    // ──────────────────────────────────────────────────────────────────────────

    public async Task<Result<EmailTriageDetails>> FetchEmailDetailsAsync(
        string emailId,
        CancellationToken cancellationToken = default)
    {
        var result = await _emailProvider.GetAsync(emailId);
        if (!result.IsSuccess)
        {
            _logger.LogDebug("Could not fetch live email details for {EmailId}: {Error}",
                emailId, result.Error.Message);
            return Result<EmailTriageDetails>.Failure(result.Error);
        }

        var email = result.Value;
        email.Headers.TryGetValue("From", out var from);
        var body = email.BodyText ?? (email.Snippet.Length > 0 ? email.Snippet : null);

        return Result<EmailTriageDetails>.Success(new EmailTriageDetails(
            From: from ?? string.Empty,
            ThreadId: email.ThreadId,
            BodyText: body
        ));
    }

    // ──────────────────────────────────────────────────────────────────────────
    // Private helpers
    // ──────────────────────────────────────────────────────────────────────────

    private async Task<Result<bool>> ExecuteGmailActionAsync(
        string emailId,
        string action,
        DateTime? receivedDateUtc,
        CancellationToken cancellationToken)
    {
        // Time-bounded labels: route based on current age at execution
        if (LabelThresholds.TryGetThreshold(action, out var thresholdDays))
        {
            if (receivedDateUtc.HasValue)
            {
                var ageDays = (DateTime.UtcNow - receivedDateUtc.Value).TotalDays;
                return ageDays >= thresholdDays
                    ? await ApplyDeleteAsync(emailId, cancellationToken)
                    : await ApplyArchiveAsync(emailId, cancellationToken);
            }

            // receivedDateUtc missing: safe fallback to Archive
            _logger.LogDebug(
                "ExecuteGmailActionAsync [{EmailId}]: time-bounded label '{Action}' has no ReceivedDateUtc — falling back to Archive",
                emailId, action);
            return await ApplyArchiveAsync(emailId, cancellationToken);
        }

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
