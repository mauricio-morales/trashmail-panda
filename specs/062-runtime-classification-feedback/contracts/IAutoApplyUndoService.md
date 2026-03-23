# Contract: IAutoApplyUndoService

**Feature**: 062-runtime-classification-feedback  
**Layer**: `src/TrashMailPanda/TrashMailPanda/Services/`  
**Pattern**: Result<T>, one public type per file

## Interface

```csharp
using TrashMailPanda.Shared.Base;

namespace TrashMailPanda.Services;

/// <summary>
/// Reverses auto-applied Gmail actions and stores the user's correction
/// as a high-value training signal (FR-018, FR-019).
/// </summary>
public interface IAutoApplyUndoService
{
    /// <summary>
    /// Undoes an auto-applied decision:
    /// 1. Reverses the Gmail action (e.g., move from Trash back to Inbox)
    /// 2. Updates the training label to the user's corrected action
    /// 3. Marks as user_corrected = 1 (high-value correction)
    /// 4. Updates the session log entry as undone
    ///
    /// Action reversal mapping:
    /// - Delete → add INBOX, remove TRASH
    /// - Archive → add INBOX
    /// - Spam → add INBOX, remove SPAM
    /// - Keep → no Gmail reversal needed
    /// </summary>
    Task<Result<bool>> UndoAsync(
        string emailId,
        string originalAction,
        string correctedAction,
        CancellationToken ct = default);
}
```

## DI Registration

```csharp
services.AddScoped<IAutoApplyUndoService, AutoApplyUndoService>();
```

## Behavioral Notes

- Delegates Gmail label manipulation to existing `IEmailProvider.BatchModifyAsync`
- Delegates training label update to existing `IEmailArchiveService.SetTrainingLabelAsync(emailId, correctedAction, userCorrected: true)`
- If Gmail API call fails, returns `Result.Failure(NetworkError)` — training label is NOT updated (dual-write pattern consistency)
- Undo is only available during the active session (session log is ephemeral)
