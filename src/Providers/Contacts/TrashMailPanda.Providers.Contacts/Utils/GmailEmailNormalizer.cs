using System;
using System.Text.RegularExpressions;

namespace TrashMailPanda.Providers.Contacts.Utils;

/// <summary>
/// Normalizes email addresses to handle Gmail-specific features for accurate contact matching
/// Gmail treats certain email variations as equivalent, so we normalize them to a canonical form
/// </summary>
/// <remarks>
/// Gmail normalization rules:
/// 1. Dots (.) in the local part are ignored (johndoe@gmail.com == john.doe@gmail.com)
/// 2. Everything after + is ignored (john+spam@gmail.com == john@gmail.com)
/// 3. Case insensitive (John@Gmail.com == john@gmail.com)
/// 4. @googlemail.com is equivalent to @gmail.com
///
/// These rules apply ONLY to @gmail.com and @googlemail.com domains.
/// For all other domains, we only apply lowercase normalization.
/// </remarks>
public static class GmailEmailNormalizer
{
    private const string GMAIL_DOMAIN = "@gmail.com";
    private const string GOOGLEMAIL_DOMAIN = "@googlemail.com";

    // Regex for validating basic email structure
    private static readonly Regex EmailValidationRegex = new(
        @"^[^@\s]+@[^@\s]+\.[^@\s]+$",
        RegexOptions.Compiled | RegexOptions.IgnoreCase);

    /// <summary>
    /// Normalizes an email address to its canonical form for matching purposes
    /// </summary>
    /// <param name="email">The email address to normalize</param>
    /// <returns>
    /// The normalized email address, or null if the input is null/empty/invalid
    /// </returns>
    /// <example>
    /// <code>
    /// // Gmail addresses
    /// Normalize("John.Doe+spam@Gmail.com") → "johndoe@gmail.com"
    /// Normalize("a.b.c+tag@googlemail.com") → "abc@gmail.com"
    ///
    /// // Non-Gmail addresses (only lowercased)
    /// Normalize("John.Doe@Outlook.com") → "john.doe@outlook.com"
    /// Normalize("user+tag@company.com") → "user+tag@company.com"
    /// </code>
    /// </example>
    public static string? Normalize(string? email)
    {
        // Handle null, empty, or whitespace-only input
        if (string.IsNullOrWhiteSpace(email))
            return null;

        // Trim whitespace
        email = email.Trim();

        // Basic email validation
        if (!EmailValidationRegex.IsMatch(email))
            return null;

        // Convert to lowercase for case-insensitive comparison
        email = email.ToLowerInvariant();

        // Check if this is a Gmail or Googlemail address
        if (IsGmailAddress(email))
        {
            return NormalizeGmailAddress(email);
        }

        // For non-Gmail addresses, just return the lowercased version
        return email;
    }

    /// <summary>
    /// Checks if an email address is a Gmail or Googlemail address
    /// </summary>
    /// <param name="email">The email address (must already be lowercased)</param>
    /// <returns>True if the email is from gmail.com or googlemail.com</returns>
    private static bool IsGmailAddress(string email)
    {
        return email.EndsWith(GMAIL_DOMAIN, StringComparison.Ordinal) ||
               email.EndsWith(GOOGLEMAIL_DOMAIN, StringComparison.Ordinal);
    }

    /// <summary>
    /// Normalizes a Gmail/Googlemail address according to Gmail's rules
    /// </summary>
    /// <param name="email">The email address (must already be lowercased)</param>
    /// <returns>The normalized Gmail address</returns>
    private static string NormalizeGmailAddress(string email)
    {
        // Split into local and domain parts
        var atIndex = email.LastIndexOf('@');
        if (atIndex <= 0 || atIndex == email.Length - 1)
            return email; // Invalid format, return as-is

        var localPart = email[..atIndex];
        var domainPart = email[atIndex..];

        // Normalize the local part
        localPart = NormalizeGmailLocalPart(localPart);

        // Normalize domain: always use @gmail.com (convert googlemail.com to gmail.com)
        var normalizedDomain = domainPart == GOOGLEMAIL_DOMAIN ? GMAIL_DOMAIN : domainPart;

        return localPart + normalizedDomain;
    }

    /// <summary>
    /// Normalizes the local part of a Gmail address
    /// </summary>
    /// <param name="localPart">The local part (before @) of the email</param>
    /// <returns>The normalized local part with dots removed and plus-addressing stripped</returns>
    private static string NormalizeGmailLocalPart(string localPart)
    {
        // Remove everything after the first + (plus-addressing)
        var plusIndex = localPart.IndexOf('+');
        if (plusIndex > 0)
        {
            localPart = localPart[..plusIndex];
        }

        // Remove all dots from the local part
        localPart = localPart.Replace(".", string.Empty);

        return localPart;
    }

    /// <summary>
    /// Normalizes multiple email addresses
    /// </summary>
    /// <param name="emails">Collection of email addresses to normalize</param>
    /// <returns>Collection of normalized email addresses (nulls are filtered out)</returns>
    public static IEnumerable<string> NormalizeMany(IEnumerable<string?> emails)
    {
        if (emails == null)
            return Enumerable.Empty<string>();

        return emails
            .Select(Normalize)
            .Where(normalized => !string.IsNullOrEmpty(normalized))
            .Cast<string>()
            .Distinct(); // Remove duplicates that may result from normalization
    }

    /// <summary>
    /// Checks if two email addresses are equivalent according to Gmail normalization rules
    /// </summary>
    /// <param name="email1">First email address</param>
    /// <param name="email2">Second email address</param>
    /// <returns>True if the emails are equivalent after normalization</returns>
    public static bool AreEquivalent(string? email1, string? email2)
    {
        var normalized1 = Normalize(email1);
        var normalized2 = Normalize(email2);

        // Both must be valid emails
        if (normalized1 == null || normalized2 == null)
            return false;

        return normalized1.Equals(normalized2, StringComparison.Ordinal);
    }

    /// <summary>
    /// Gets all common variations of a Gmail address that would normalize to the same form
    /// Useful for testing and understanding normalization behavior
    /// </summary>
    /// <param name="normalizedEmail">A normalized Gmail address</param>
    /// <returns>Collection of common email variations</returns>
    /// <remarks>This is primarily useful for testing and documentation purposes</remarks>
    public static IEnumerable<string> GetCommonVariations(string normalizedEmail)
    {
        var normalized = Normalize(normalizedEmail);
        if (normalized == null || !IsGmailAddress(normalized))
            return new[] { normalizedEmail };

        var atIndex = normalized.LastIndexOf('@');
        var localPart = normalized[..atIndex];
        var variations = new List<string> { normalized };

        // Add variation with dots after every character
        if (localPart.Length > 1)
        {
            var withDots = string.Join(".", localPart.ToCharArray());
            variations.Add($"{withDots}@gmail.com");
        }

        // Add variation with +tag
        variations.Add($"{localPart}+tag@gmail.com");

        // Add googlemail.com variant
        variations.Add($"{localPart}@googlemail.com");

        // Add combination
        if (localPart.Length > 1)
        {
            var withDots = string.Join(".", localPart.ToCharArray());
            variations.Add($"{withDots}+tag@googlemail.com");
        }

        return variations.Distinct();
    }
}
