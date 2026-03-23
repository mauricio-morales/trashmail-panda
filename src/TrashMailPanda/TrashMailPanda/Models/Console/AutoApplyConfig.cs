using System.ComponentModel.DataAnnotations;

namespace TrashMailPanda.Models.Console;

/// <summary>
/// User-configurable auto-apply settings. Persisted via
/// <c>IConfigurationService.UpdateProcessingSettingsAsync()</c> as a nested
/// object on <c>ProcessingSettings</c> (stored in the <c>app_config</c> SQLite
/// KV table under the <c>"ProcessingSettings"</c> key). NOT in
/// <c>ISecureStorageManager</c> — that is reserved for encrypted secrets.
/// </summary>
public sealed class AutoApplyConfig
{
    /// <summary>
    /// When false (default), every email is presented for manual review
    /// regardless of confidence. FR-002.
    /// </summary>
    public bool Enabled { get; set; } = false;

    /// <summary>
    /// Minimum confidence score [0.50, 1.00] required to auto-apply a
    /// classification without user confirmation. Default 0.95 (95%). FR-001.
    /// </summary>
    [Range(0.50, 1.00)]
    public float ConfidenceThreshold { get; set; } = 0.95f;
}
