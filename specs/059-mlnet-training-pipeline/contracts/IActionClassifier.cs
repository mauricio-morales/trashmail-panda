// Contract: IActionClassifier
// Feature: #59 — ML.NET Model Training Infrastructure
// Date: 2026-03-17
// Type: Internal Service Interface
//
// Overview:
//   Wraps the loaded ML.NET action model (PredictionEngine<ActionTrainingInput,
//   ActionPrediction>) and provides synchronous classification behind a
//   Result<T>-returning, testable interface.
//
//   IActionClassifier is an implementation detail of MLModelProvider;
//   it is NOT registered directly in the DI container as a public service.
//   It is injected into MLModelProvider as an internal dependency.

namespace TrashMailPanda.Shared;

using TrashMailPanda.Providers.ML.Models;

/// <summary>
/// Wraps the loaded action classification model and provides
/// Result&lt;T&gt;-returning classification methods for testability.
///
/// The underlying ML.NET <c>PredictionEngine&lt;T,U&gt;</c> is NOT thread-safe.
/// Callers are responsible for ensuring single-threaded access or using
/// a pool of classifiers. <see cref="MLModelProvider"/> manages this internally.
/// </summary>
public interface IActionClassifier
{
    /// <summary>
    /// True when a model has been loaded and is ready to classify.
    /// False after construction or after <see cref="UnloadModel"/> is called.
    /// </summary>
    bool IsLoaded { get; }

    /// <summary>
    /// The model version identifier currently loaded, or null when not loaded.
    /// </summary>
    string? LoadedModelId { get; }

    /// <summary>
    /// Load a trained action model from the specified .zip file path.
    /// Replaces any previously loaded model.
    /// Returns <see cref="Result.Failure"/> with an <c>InitializationError</c>
    /// if the file does not exist or is corrupt.
    /// </summary>
    Task<Result<bool>> LoadModelAsync(
        string modelFilePath,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Unload the current model and release its resources.
    /// </summary>
    void UnloadModel();

    /// <summary>
    /// Classify a single email's recommended action.
    /// Returns <see cref="Result.Failure"/> with an <c>InitializationError</c>
    /// when no model is loaded.
    /// </summary>
    /// <remarks>
    /// SC-003: Must contribute &lt;5 ms to the combined &lt;10 ms classification budget.
    /// </remarks>
    Result<ActionPrediction> Classify(EmailFeatureVector input);

    /// <summary>
    /// Classify a batch of emails.
    /// Returns <see cref="Result.Failure"/> with an <c>InitializationError</c>
    /// when no model is loaded. Partial batch failure is not possible — either
    /// all succeed or the result is a failure.
    /// </summary>
    /// <remarks>
    /// SC-004: Batch of 100 must contribute &lt;80 ms to the combined &lt;100 ms budget.
    /// </remarks>
    Result<IReadOnlyList<ActionPrediction>> ClassifyBatch(
        IEnumerable<EmailFeatureVector> inputs);
}
