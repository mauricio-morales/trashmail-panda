# Contract: IAutoApplyService

**Feature**: 062-runtime-classification-feedback  
**Layer**: `src/TrashMailPanda/TrashMailPanda/Services/`  
**Pattern**: Result<T>, one public type per file

## Interface

```csharp
using TrashMailPanda.Models.Console;
using TrashMailPanda.Providers.ML.Models;
using TrashMailPanda.Providers.Storage.Models;
using TrashMailPanda.Shared.Base;

namespace TrashMailPanda.Services;

/// <summary>
/// Evaluates whether an AI-classified email should be auto-applied (confidence ≥ threshold)
/// or presented for manual review. Manages the auto-apply session log and undo operations.
/// </summary>
public interface IAutoApplyService
{
    /// <summary>
    /// Loads persisted auto-apply configuration from secure storage.
    /// Called once at session start.
    /// </summary>
    Task<Result<AutoApplyConfig>> GetConfigAsync(CancellationToken ct = default);

    /// <summary>
    /// Persists updated auto-apply configuration to secure storage (FR-023).
    /// </summary>
    Task<Result<bool>> SaveConfigAsync(AutoApplyConfig config, CancellationToken ct = default);

    /// <summary>
    /// Evaluates whether a classification result should be auto-applied.
    /// Returns true when:
    /// - Auto-apply is enabled
    /// - Confidence >= threshold
    /// - Quality monitor has not auto-disabled auto-apply
    /// Callers then either auto-apply or present for manual review.
    /// </summary>
    bool ShouldAutoApply(AutoApplyConfig config, ClassificationResult classification);

    /// <summary>
    /// Detects if the recommended action matches the email's current Gmail state.
    /// When true, the Gmail API call can be skipped (redundant) during auto-apply (FR-024).
    /// </summary>
    bool IsActionRedundant(string recommendedAction, EmailFeatureVector feature);

    /// <summary>
    /// Records an auto-applied decision in the ephemeral session log (FR-017).
    /// </summary>
    void LogAutoApply(AutoApplyLogEntry entry);

    /// <summary>
    /// Returns all auto-applied decisions in the current session for review (FR-017).
    /// </summary>
    IReadOnlyList<AutoApplyLogEntry> GetSessionLog();

    /// <summary>
    /// Returns the session summary (auto-applied vs. manual counts) (FR-003).
    /// </summary>
    AutoApplySessionSummary GetSessionSummary(int totalManuallyReviewed);

    /// <summary>
    /// Clears the session log (called when a new session starts).
    /// </summary>
    void ResetSession();
}
```

## DI Registration

```csharp
services.AddSingleton<IAutoApplyService, AutoApplyService>();
```

## Behavioral Notes

- `ShouldAutoApply` is pure logic — no side effects, no DB calls
- `IsActionRedundant` checks feature flags: if `recommendedAction == "Archive"` and `IsArchived == 1`, the action is redundant
- Session log is in-memory (`List<AutoApplyLogEntry>`) — not persisted beyond session lifetime
- Config persistence uses `ISecureStorageManager.StoreAsync("autoapply_enabled", ...)` and `StoreAsync("autoapply_threshold", ...)`
