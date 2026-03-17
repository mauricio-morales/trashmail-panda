namespace TrashMailPanda.Models;

/// <summary>
/// Represents PKCE code verifier and challenge for OAuth security
/// </summary>
public record PKCEPair
{
    /// <summary>
    /// SHA256 hash of code verifier (sent in authorization URL)
    /// </summary>
    public required string CodeChallenge { get; init; }

    /// <summary>
    /// Random code verifier (sent during token exchange to prove identity)
    /// </summary>
    public required string CodeVerifier { get; init; }
}
