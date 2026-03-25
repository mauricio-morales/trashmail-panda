using System.Threading;
using System.Threading.Tasks;
using System.Collections.Generic;
using TrashMailPanda.Models.Console;
using TrashMailPanda.Providers.ML.Models;
using TrashMailPanda.Providers.Storage.Models;
using TrashMailPanda.Shared.Base;
using TrashMailPanda.Shared;

namespace TrashMailPanda.Services;

/// <summary>
/// UI-agnostic triage business logic.
/// Detects cold-start vs AI-assisted mode, sources the untriaged email queue,
/// fetches AI recommendations, and executes dual-write decisions
/// (Gmail action first, training label on success).
/// No rendering dependency — consumable by any UI layer.
/// </summary>
public interface IEmailTriageService
{
    /// <summary>
    /// Returns current session state: triage mode, cumulative labeled count,
    /// and whether the training threshold has been reached.
    /// Called once at session start.
    /// </summary>
    Task<Result<TriageSessionInfo>> GetSessionInfoAsync(
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Returns a page of untriaged EmailFeatureVectors (training_label IS NULL),
    /// ordered by ExtractedAt descending.
    /// </summary>
    Task<Result<IReadOnlyList<EmailFeatureVector>>> GetNextBatchAsync(
        int pageSize,
        int offset,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Returns a mixed batch for the re-triage phase, interleaving two pools:
    /// <list type="bullet">
    ///   <item>Old untriaged emails (EmailAgeDays ≥ <c>OldEmailThresholdDays</c> = 5 years) —
    ///     still unlabeled, presented for first-time classification.</item>
    ///   <item>Recently archived emails (EmailAgeDays ≤ <c>RetriagedWindowDays</c> = 3 years)
    ///     that were previously labeled but not yet user-corrected — presented for re-evaluation
    ///     since their archive label may now be outdated.</item>
    /// </list>
    /// Re-triage items have <c>TrainingLabel</c> populated (their previous decision).
    /// Both pools use <c>offset = 0</c>; each pools naturally shrinks as emails are labeled.
    /// Returns an empty list when both pools are exhausted.
    /// </summary>
    Task<Result<IReadOnlyList<EmailFeatureVector>>> GetRetriageQueueAsync(
        int pageSize,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Returns the AI-predicted action for the given feature vector.
    /// Returns <c>Success(null)</c> in ColdStart mode.
    /// Returns <c>Success(prediction)</c> in AiAssisted mode.
    /// </summary>
    Task<Result<ActionPrediction?>> GetAiRecommendationAsync(
        EmailFeatureVector feature,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Executes the user's triage decision:
    /// 1. Applies Gmail action (BatchModifyAsync or ReportSpamAsync) FIRST.
    /// 2. On Gmail success: persists training_label via SetTrainingLabelAsync.
    /// 3. On Gmail failure: returns Failure — training_label is NOT stored.
    /// <para>
    /// Set <paramref name="forceUserCorrected"/> = <c>true</c> for re-triage decisions,
    /// ensuring the email is always marked as explicitly reviewed (user_corrected = 1)
    /// and removed from the re-triage pool, even when the chosen action matches the
    /// previous label.
    /// </para>
    /// </summary>
    Task<Result<TriageDecision>> ApplyDecisionAsync(
        string emailId,
        string chosenAction,
        string? aiRecommendation,
        bool forceUserCorrected = false,
        CancellationToken cancellationToken = default,
        DateTime? receivedDateUtc = null);

    /// <summary>
    /// Fetches live email details from the provider for enriched triage card display.
    /// Returns the full From header, ThreadId for the browser deep-link, and decoded
    /// plain-text body for the expand feature. One Gmail API call per email (format=FULL).
    /// </summary>
    Task<Result<EmailTriageDetails>> FetchEmailDetailsAsync(
        string emailId,
        CancellationToken cancellationToken = default);
}
