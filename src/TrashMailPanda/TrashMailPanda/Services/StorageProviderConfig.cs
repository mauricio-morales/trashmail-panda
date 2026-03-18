using System.IO;

namespace TrashMailPanda.Services;

/// <summary>
/// Configuration class for Storage Provider
/// </summary>
public class StorageProviderConfig
{
    public string DatabasePath { get; set; } = GetOsDefaultPath();
    public string EncryptionKey { get; set; } = string.Empty;
    public bool EnableWAL { get; set; } = true;
    public int CommandTimeoutSeconds { get; set; } = 30;

    /// <summary>
    /// Returns the OS-standard application data path for the TrashMailPanda database.
    /// Uses Environment.SpecialFolder.LocalApplicationData to find the platform-correct
    /// location (e.g. ~/Library/Application Support on macOS, %LOCALAPPDATA% on Windows,
    /// ~/.local/share on Linux).
    /// </summary>
    public static string GetOsDefaultPath() =>
        Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "TrashMailPanda",
            "app.db");
}