namespace TrashMailPanda.Shared.Models;

/// <summary>
/// Captures email engagement indicators derived from local thread back-correction.
/// These flags are set by examining the SENT folder — no additional API calls required.
/// </summary>
/// <param name="IsReplied">True when the user sent a message in the same thread.</param>
/// <param name="IsForwarded">True when a SENT message in the same thread has a Fwd:/FW: subject prefix.</param>
public sealed record EngagementFlags(bool IsReplied, bool IsForwarded);
