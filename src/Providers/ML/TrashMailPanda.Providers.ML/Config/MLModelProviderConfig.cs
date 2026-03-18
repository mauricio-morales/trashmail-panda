using System.ComponentModel.DataAnnotations;

namespace TrashMailPanda.Providers.ML.Config;

/// <summary>
/// DataAnnotations-validated configuration for <see cref="MLModelProvider"/>.
/// </summary>
public sealed class MLModelProviderConfig : TrashMailPanda.Shared.Models.BaseProviderConfig
{
    /// <summary>Maximum number of retained model versions per model type (default 5).</summary>
    [Range(1, 20)]
    public int MaxModelVersions { get; set; } = 5;

    /// <summary>Minimum labeled training samples required before training starts (default 100).</summary>
    [Range(10, int.MaxValue)]
    public int MinTrainingSamples { get; set; } = 100;

    /// <summary>Minimum distinct action classes required in training data (default 2).</summary>
    [Range(2, 4)]
    public int MinDistinctClasses { get; set; } = 2;

    /// <summary>MacroF1 threshold below which a quality advisory is emitted (default 0.70).</summary>
    [Range(0.0, 1.0)]
    public double QualityAdvisoryF1Threshold { get; set; } = 0.70;

    /// <summary>
    /// When the dominant class proportion exceeds this threshold LightGbm is chosen
    /// over SdcaMaximumEntropy (default 0.80).
    /// </summary>
    [Range(0.5, 1.0)]
    public double DominantClassImbalanceThreshold { get; set; } = 0.80;

    /// <summary>
    /// Directory where trained model .zip files are stored.
    /// Defaults to {LocalApplicationData}/TrashMailPanda/models/action/.
    /// </summary>
    public string ModelDirectory { get; set; } = System.IO.Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "TrashMailPanda", "models", "action");
}
