using TrashMailPanda.Providers.ML.Models;
using TrashMailPanda.Providers.Storage.Models;
using TrashMailPanda.Shared.Base;

namespace TrashMailPanda.Services;

/// <summary>
/// End-to-end email classification contract.
/// Coordinates <see cref="IMLModelProvider"/> for ML inference and rule-based
/// cold-start fallback. Returns enriched <see cref="ClassificationResult"/>
/// records with reasoning source attribution.
///
/// This service is UI-agnostic — it MUST NOT reference Spectre.Console, Avalonia,
/// or any rendering type. Both the Console TUI and a future Avalonia UI consume
/// this service identically through DI.
///
/// For triage orchestration (fetch → present → decide → dual-write), see
/// <see cref="IEmailTriageService"/>. This service is purely predictive:
/// given features, return predicted action.
/// </summary>
public interface IClassificationService
{
    /// <summary>
    /// Classify a single email's recommended action.
    /// Falls back to rule-based classification when no trained model is loaded.
    /// Equivalent to calling <see cref="ClassifyBatchAsync"/> with a single item.
    /// </summary>
    /// <param name="input">Feature vector for the email to classify.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>
    /// <see cref="Result{T}"/> containing the classification result with
    /// predicted action, confidence, and reasoning source.
    /// </returns>
    Task<Result<ClassificationResult>> ClassifySingleAsync(
        EmailFeatureVector input,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Classify a batch of emails for their recommended actions.
    /// Returns one <see cref="ClassificationResult"/> per input in the same order.
    /// Returns <c>Result.Success</c> with an empty list when input is empty.
    /// Falls back to rule-based classification when no trained model is loaded.
    /// </summary>
    /// <param name="inputs">Batch of feature vectors to classify.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>
    /// <see cref="Result{T}"/> containing a read-only list of classification
    /// results, one per input, preserving input order.
    /// </returns>
    Task<Result<IReadOnlyList<ClassificationResult>>> ClassifyBatchAsync(
        IReadOnlyList<EmailFeatureVector> inputs,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Returns the current classification mode (ColdStart, Hybrid, or MlPrimary).
    /// Delegates to the underlying ML model provider.
    /// </summary>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The active classification mode.</returns>
    Task<Result<ClassificationMode>> GetClassificationModeAsync(
        CancellationToken cancellationToken = default);
}
