namespace TrashMailPanda.Models;

/// <summary>
/// Immutable result of a single email classification from
/// <see cref="Services.IClassificationService"/>.
/// Maps from <see cref="Providers.ML.Models.ActionPrediction"/>
/// with added reasoning source attribution.
/// </summary>
public sealed record ClassificationResult
{
    /// <summary>Unique email identifier from <c>EmailFeatureVector.EmailId</c>.</summary>
    public required string EmailId { get; init; }

    /// <summary>Predicted action: "Keep", "Archive", "Delete", or "Spam".</summary>
    public required string PredictedAction { get; init; }

    /// <summary>Confidence score normalized to [0.0, 1.0].</summary>
    public required float Confidence { get; init; }

    /// <summary>Whether the classification came from the ML model or rule-based fallback.</summary>
    public required ReasoningSource ReasoningSource { get; init; }
}
