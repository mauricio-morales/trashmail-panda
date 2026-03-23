using TrashMailPanda.Models;
using TrashMailPanda.Models.Console;
using TrashMailPanda.Providers.Storage.Models;
using TrashMailPanda.Shared.Base;

namespace TrashMailPanda.Services;

/// <summary>
/// Evaluates whether an AI-classified email should be auto-applied (confidence ≥ threshold)
/// or presented for manual review. Manages the auto-apply session log and config persistence.
/// </summary>
public interface IAutoApplyService
{
    /// <summary>
    /// Loads the persisted auto-apply configuration from <c>ProcessingSettings</c>
    /// via <c>IConfigurationService</c> (app_config SQLite KV table).
    /// Called once at session start.
    /// </summary>
    Task<Result<AutoApplyConfig>> GetConfigAsync(CancellationToken ct = default);

    /// <summary>
    /// Persists updated auto-apply configuration via <c>IConfigurationService</c>. FR-023.
    /// </summary>
    Task<Result<bool>> SaveConfigAsync(AutoApplyConfig config, CancellationToken ct = default);

    /// <summary>
    /// Evaluates whether a classification result should be auto-applied.
    /// Returns true when auto-apply is enabled and confidence &gt;= threshold. Pure logic — no side effects.
    /// </summary>
    bool ShouldAutoApply(AutoApplyConfig config, ClassificationResult classification);

    /// <summary>
    /// Detects if the recommended action matches the email's current Gmail state.
    /// When true, the Gmail API call can be skipped during auto-apply (FR-024).
    /// </summary>
    bool IsActionRedundant(string recommendedAction, EmailFeatureVector feature);

    /// <summary>
    /// Records an auto-applied decision in the ephemeral session log. FR-017.
    /// </summary>
    void LogAutoApply(AutoApplyLogEntry entry);

    /// <summary>
    /// Returns all auto-applied decisions in the current session for review. FR-017.
    /// </summary>
    IReadOnlyList<AutoApplyLogEntry> GetSessionLog();

    /// <summary>
    /// Returns the session summary (auto-applied vs. manual counts). FR-003.
    /// </summary>
    AutoApplySessionSummary GetSessionSummary(int totalManuallyReviewed);

    /// <summary>
    /// Clears the session log (called when a new triage session starts).
    /// </summary>
    void ResetSession();
}
