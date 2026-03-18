using System;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace TrashMailPanda.Providers.Email.Services;

/// <summary>
/// Detects engagement signals (replies and forwards) via local thread-based back-correction.
/// Zero extra Gmail API calls — detection is done by cross-referencing SENT folder messages
/// already stored in local training data.
/// </summary>
public interface IGmailEngagementDetector
{
    /// <summary>
    /// Runs back-correction over the given thread IDs:
    ///   1. Sets IsReplied=1 on all non-SENT emails sharing a ThreadId with any SENT message.
    ///   2. Sets IsForwarded=1 on non-SENT emails whose thread has a SENT message with Fwd:/FW: SubjectPrefix.
    /// Both updates are scoped to the given account.
    /// </summary>
    /// <param name="accountId">The account to scope back-correction to.</param>
    /// <param name="now">The timestamp to use for UpdatedAt.</param>
    /// <returns>Number of rows updated.</returns>
    Task<int> RunBackCorrectionAsync(string accountId, DateTime now);
}
