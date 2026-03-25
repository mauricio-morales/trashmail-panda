using System.Security.Cryptography;
using System.Text;
using TrashMailPanda.Models;

namespace TrashMailPanda.Services;

/// <summary>
/// Generates PKCE (Proof Key for Code Exchange) pairs for OAuth security
/// </summary>
public static class PKCEGenerator
{
    /// <summary>
    /// Generate a PKCE pair with code verifier and SHA256 challenge
    /// </summary>
    /// <returns>PKCEPair with CodeVerifier and CodeChallenge</returns>
    /// <remarks>
    /// RFC 7636 compliant implementation:
    /// 1. Generate random 32-byte code_verifier
    /// 2. SHA256 hash of verifier = code_challenge
    /// 3. Base64Url encode both values
    /// </remarks>
    public static PKCEPair GeneratePKCEPair()
    {
        // 1. Generate random 32-byte code_verifier (base64url → 43 chars, within RFC 7636's 43-128 range)
        var randomBytes = new byte[32];
        using (var rng = RandomNumberGenerator.Create())
        {
            rng.GetBytes(randomBytes);
        }
        string codeVerifier = Base64UrlEncode(randomBytes);

        // 2. SHA256 hash of verifier = code_challenge
        byte[] challengeBytes;
        using (var sha256 = SHA256.Create())
        {
            challengeBytes = sha256.ComputeHash(Encoding.UTF8.GetBytes(codeVerifier));
        }
        string codeChallenge = Base64UrlEncode(challengeBytes);

        return new PKCEPair
        {
            CodeVerifier = codeVerifier,
            CodeChallenge = codeChallenge
        };
    }

    /// <summary>
    /// Base64Url encode (RFC 7636 compliant)
    /// </summary>
    private static string Base64UrlEncode(byte[] input)
    {
        var base64 = Convert.ToBase64String(input);
        // Convert to Base64Url: replace +/= with -_
        return base64
            .Replace("+", "-")
            .Replace("/", "_")
            .Replace("=", "");
    }
}
