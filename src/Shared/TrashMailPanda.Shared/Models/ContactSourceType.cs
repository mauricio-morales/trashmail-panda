namespace TrashMailPanda.Shared.Models;

/// <summary>
/// Enumeration for different contact source types
/// Used to identify which platform or service a contact comes from
/// </summary>
public enum ContactSourceType
{
    /// <summary>
    /// Unknown or unspecified contact source
    /// </summary>
    Unknown = 0,

    /// <summary>
    /// Google Contacts via People API
    /// </summary>
    Google = 1,

    /// <summary>
    /// Apple Contacts (future implementation)
    /// </summary>
    Apple = 2,

    /// <summary>
    /// Windows People API (future implementation)
    /// </summary>
    Windows = 3,

    /// <summary>
    /// Outlook/Microsoft Graph contacts (future implementation)
    /// </summary>
    Outlook = 4,

    /// <summary>
    /// Manual entry by user
    /// </summary>
    Manual = 5
}