using System.Threading;
using System.Threading.Tasks;
using System.Collections.Generic;
using TrashMailPanda.Models.Console;
using TrashMailPanda.Providers.ML.Models;
using TrashMailPanda.Providers.Storage.Models;
using TrashMailPanda.Shared.Base;

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
    /// </summary>
    Task<Result<TriageDecision>> ApplyDecisionAsync(
        string emailId,
        string chosenAction,
        string? aiRecommendation,
        CancellationToken cancellationToken = default);
}
