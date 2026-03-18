using TrashMailPanda.Shared.Base;
using TrashMailPanda.Providers.ML.Models;
using TrashMailPanda.Providers.Storage.Models;

namespace TrashMailPanda.Providers.ML;

/// <summary>
/// Wraps the loaded action classification model and provides
/// Result&lt;T&gt;-returning classification methods for testability.
///
/// The underlying ML.NET <c>PredictionEngine&lt;T,U&gt;</c> is NOT thread-safe.
/// Callers are responsible for ensuring single-threaded access.
/// </summary>
public interface IActionClassifier
{
    /// <summary>True when a model has been loaded and is ready to classify.</summary>
    bool IsLoaded { get; }

    /// <summary>The model version identifier currently loaded, or null when not loaded.</summary>
    string? LoadedModelId { get; }

    /// <summary>
    /// Load a trained action model from the specified .zip file path.
    /// Replaces any previously loaded model.
    /// </summary>
    Task<Result<bool>> LoadModelAsync(
        string modelFilePath,
        CancellationToken cancellationToken = default);

    /// <summary>Unload the current model and release its resources.</summary>
    void UnloadModel();

    /// <summary>
    /// Classify a single email's recommended action.
    /// Returns failure with <c>InitializationError</c> when no model is loaded.
    /// </summary>
    Result<ActionPrediction> Classify(EmailFeatureVector input);

    /// <summary>
    /// Classify a batch of emails.
    /// Returns failure with <c>InitializationError</c> when no model is loaded.
    /// </summary>
    Result<IReadOnlyList<ActionPrediction>> ClassifyBatch(
        IEnumerable<EmailFeatureVector> inputs);
}
