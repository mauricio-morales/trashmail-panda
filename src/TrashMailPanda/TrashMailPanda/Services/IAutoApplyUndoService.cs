using System.Threading;
using System.Threading.Tasks;
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
    ///
    /// Action reversal mapping:
    /// - Delete → add INBOX, remove TRASH
    /// - Archive → add INBOX
    /// - Spam → add INBOX, remove SPAM
    /// - Keep → no Gmail reversal needed
    ///
    /// If the Gmail API call fails, returns <see cref="Result{T}.Failure"/>
    /// and the training label is NOT updated.
    /// </summary>
    Task<Result<bool>> UndoAsync(
        string emailId,
        string originalAction,
        string correctedAction,
        CancellationToken ct = default);
}
