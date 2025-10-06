using PhoneNumbers;

namespace TrashMailPanda.Shared.Services;

/// <summary>
/// Service for phone number parsing and formatting operations
/// Wraps PhoneNumbers library functionality as a singleton service for performance
/// </summary>
public interface IPhoneNumberService
{
    /// <summary>
    /// Normalizes a phone number to E164 format if valid
    /// </summary>
    /// <param name="phoneNumber">The phone number to normalize</param>
    /// <param name="defaultRegion">The default region to use for parsing (defaults to "US")</param>
    /// <returns>The normalized phone number in E164 format, or empty string if invalid</returns>
    string NormalizePhoneNumber(string? phoneNumber, string defaultRegion = "US");

    /// <summary>
    /// Parses a phone number string into a PhoneNumber object
    /// </summary>
    /// <param name="phoneNumber">The phone number string to parse</param>
    /// <param name="defaultRegion">The default region to use for parsing</param>
    /// <returns>The parsed PhoneNumber object</returns>
    /// <exception cref="NumberParseException">Thrown when the phone number cannot be parsed</exception>
    PhoneNumber Parse(string phoneNumber, string defaultRegion);

    /// <summary>
    /// Validates whether a phone number is valid
    /// </summary>
    /// <param name="phoneNumber">The PhoneNumber object to validate</param>
    /// <returns>True if the phone number is valid</returns>
    bool IsValidNumber(PhoneNumber phoneNumber);

    /// <summary>
    /// Formats a phone number according to the specified format
    /// </summary>
    /// <param name="phoneNumber">The PhoneNumber object to format</param>
    /// <param name="format">The format to use</param>
    /// <returns>The formatted phone number string</returns>
    string Format(PhoneNumber phoneNumber, PhoneNumberFormat format);
}