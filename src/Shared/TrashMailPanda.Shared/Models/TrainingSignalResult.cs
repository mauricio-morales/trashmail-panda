namespace TrashMailPanda.Shared.Models;

/// <summary>
/// The outcome of signal assignment for a single email during training data import.
/// </summary>
/// <param name="Signal">The assigned classification signal.</param>
/// <param name="Confidence">Confidence in the signal (0.0–1.0). 1.0 for Excluded.</param>
public sealed record TrainingSignalResult(ClassificationSignal Signal, float Confidence);
