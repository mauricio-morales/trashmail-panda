namespace TrashMailPanda.Shared.Security;

/// <summary>
/// Centralized Google OAuth scope constants used across TrashMail Panda
/// Single source of truth for all Google API OAuth scopes
/// </summary>
public static class GoogleOAuthScopes
{
    // Gmail scopes
    public const string GmailReadonly = "https://www.googleapis.com/auth/gmail.readonly";
    public const string GmailModify = "https://www.googleapis.com/auth/gmail.modify";
    public const string GmailCompose = "https://www.googleapis.com/auth/gmail.compose";
    public const string GmailSend = "https://www.googleapis.com/auth/gmail.send";
    public const string GmailLabels = "https://www.googleapis.com/auth/gmail.labels";
    public const string GmailSettingsBasic = "https://www.googleapis.com/auth/gmail.settings.basic";
    public const string GmailSettingsSharing = "https://www.googleapis.com/auth/gmail.settings.sharing";
    public const string GmailFullAccess = "https://mail.google.com/";

    // Contacts scopes
    public const string ContactsReadonly = "https://www.googleapis.com/auth/contacts.readonly";
    public const string Contacts = "https://www.googleapis.com/auth/contacts";

    // User info scopes
    public const string UserInfoEmail = "https://www.googleapis.com/auth/userinfo.email";
    public const string UserInfoProfile = "https://www.googleapis.com/auth/userinfo.profile";

    // All valid Gmail OAuth scope strings (for validation)
    public static readonly string[] AllValidScopes = [
        GmailReadonly,
        GmailModify,
        GmailCompose,
        GmailSend,
        GmailLabels,
        GmailSettingsBasic,
        GmailSettingsSharing,
        GmailFullAccess,
        ContactsReadonly,
        Contacts,
        UserInfoEmail,
        UserInfoProfile
    ];

    // Predefined scope sets for common use cases
    public static readonly string[] BasicGmail = [GmailModify];
    public static readonly string[] GmailWithContacts = [GmailModify, ContactsReadonly];
    public static readonly string[] Complete = [GmailModify, ContactsReadonly, UserInfoEmail];
}