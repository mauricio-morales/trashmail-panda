namespace TrashMailPanda.Providers.Email.Models;

/// <summary>
/// Gmail API endpoints and configuration
/// </summary>
public static class GmailApiConstants
{
    /// <summary>Gmail API base URL</summary>
    public const string BASE_URL = "https://gmail.googleapis.com/gmail/v1/";

    /// <summary>OAuth2 authorization URL</summary>
    public const string OAUTH_AUTH_URL = "https://accounts.google.com/o/oauth2/auth";

    /// <summary>OAuth2 token exchange URL</summary>
    public const string OAUTH_TOKEN_URL = "https://oauth2.googleapis.com/token";


    /// <summary>User ID placeholder for authenticated user</summary>
    public const string USER_ID_ME = "me";
}