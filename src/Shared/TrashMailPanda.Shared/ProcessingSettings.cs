using System.Collections.Generic;

namespace TrashMailPanda.Shared;

public class ProcessingSettings
{
    public int BatchSize { get; set; } = 1000;
    public decimal? DailyCostLimit { get; set; }
    public bool AutoProcessNewEmails { get; set; } = false;
    public IReadOnlyList<string>? FoldersToProcess { get; set; }

    /// <summary>
    /// Auto-apply configuration: confidence threshold and enabled flag.
    /// Persisted with the rest of ProcessingSettings in the app_config KV table.
    /// </summary>
    public AutoApplySettings AutoApply { get; set; } = new();

    /// <summary>
    /// Retention enforcement settings: scan interval, prompt threshold, and last scan timestamp.
    /// Persisted with the rest of ProcessingSettings in the app_config KV table.
    /// </summary>
    public RetentionSettings Retention { get; set; } = new();
}

/// <summary>
/// Serialisation-friendly DTO for auto-apply settings stored inside
/// <see cref="ProcessingSettings"/>. Uses a plain nested class (not the
/// console-layer <c>AutoApplyConfig</c>) so the Shared project has no
/// dependency on the console layer.
/// </summary>
public sealed class AutoApplySettings
{
    public bool Enabled { get; set; } = false;
    public float ConfidenceThreshold { get; set; } = 0.95f;
}