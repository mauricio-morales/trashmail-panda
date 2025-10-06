using Microsoft.Extensions.Logging;
using PhoneNumbers;

namespace TrashMailPanda.Shared.Services;

/// <summary>
/// Singleton service for phone number parsing and formatting operations
/// Wraps PhoneNumbers library functionality for optimal performance
/// </summary>
public class PhoneNumberService : IPhoneNumberService
{
    private static readonly PhoneNumberUtil _phoneNumberUtil = PhoneNumberUtil.GetInstance();
    private readonly ILogger<PhoneNumberService> _logger;

    public PhoneNumberService(ILogger<PhoneNumberService> logger)
    {
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    /// <summary>
    /// Normalizes a phone number to E164 format if valid
    /// </summary>
    /// <param name="phoneNumber">The phone number to normalize</param>
    /// <param name="defaultRegion">The default region to use for parsing (defaults to "US")</param>
    /// <returns>The normalized phone number in E164 format, or empty string if invalid</returns>
    public string NormalizePhoneNumber(string? phoneNumber, string defaultRegion = "US")
    {
        if (string.IsNullOrWhiteSpace(phoneNumber))
            return string.Empty;

        try
        {
            var parsed = _phoneNumberUtil.Parse(phoneNumber, defaultRegion);
            if (_phoneNumberUtil.IsValidNumber(parsed))
            {
                return _phoneNumberUtil.Format(parsed, PhoneNumberFormat.E164);
            }
        }
        catch (NumberParseException ex)
        {
            _logger.LogDebug("Failed to parse phone number {PhoneNumber}: {Error}", phoneNumber, ex.Message);
        }

        return string.Empty;
    }

    /// <summary>
    /// Parses a phone number string into a PhoneNumber object
    /// </summary>
    /// <param name="phoneNumber">The phone number string to parse</param>
    /// <param name="defaultRegion">The default region to use for parsing</param>
    /// <returns>The parsed PhoneNumber object</returns>
    /// <exception cref="NumberParseException">Thrown when the phone number cannot be parsed</exception>
    public PhoneNumber Parse(string phoneNumber, string defaultRegion)
    {
        return _phoneNumberUtil.Parse(phoneNumber, defaultRegion);
    }

    /// <summary>
    /// Validates whether a phone number is valid
    /// </summary>
    /// <param name="phoneNumber">The PhoneNumber object to validate</param>
    /// <returns>True if the phone number is valid</returns>
    public bool IsValidNumber(PhoneNumber phoneNumber)
    {
        return _phoneNumberUtil.IsValidNumber(phoneNumber);
    }

    /// <summary>
    /// Formats a phone number according to the specified format
    /// </summary>
    /// <param name="phoneNumber">The PhoneNumber object to format</param>
    /// <param name="format">The format to use</param>
    /// <returns>The formatted phone number string</returns>
    public string Format(PhoneNumber phoneNumber, PhoneNumberFormat format)
    {
        return _phoneNumberUtil.Format(phoneNumber, format);
    }
}