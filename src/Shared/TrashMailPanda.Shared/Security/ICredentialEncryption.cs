using System;
using System.Threading.Tasks;

namespace TrashMailPanda.Shared.Security;

/// <summary>
/// Interface for credential encryption and decryption operations
/// Provides platform-specific secure encryption using OS-level security
/// </summary>
public interface ICredentialEncryption
{
    /// <summary>
    /// Initialize the encryption system with platform-specific setup
    /// </summary>
    /// <returns>Result indicating success or failure</returns>
    Task<EncryptionResult> InitializeAsync();

    /// <summary>
    /// Encrypt a credential using OS-level security
    /// </summary>
    /// <param name="plainText">The plain text credential to encrypt</param>
    /// <param name="context">Optional context for encryption (e.g., provider name)</param>
    /// <returns>Result containing encrypted credential or error details</returns>
    Task<EncryptionResult<string>> EncryptAsync(string plainText, string? context = null);

    /// <summary>
    /// Decrypt a credential using OS-level security
    /// </summary>
    /// <param name="encryptedText">The encrypted credential to decrypt</param>
    /// <param name="context">Optional context for decryption (e.g., provider name)</param>
    /// <returns>Result containing decrypted credential or error details</returns>
    Task<EncryptionResult<string>> DecryptAsync(string encryptedText, string? context = null);

    /// <summary>
    /// Generate a master key using system entropy
    /// </summary>
    /// <returns>Result containing generated master key or error details</returns>
    Task<EncryptionResult<byte[]>> GenerateMasterKeyAsync();

    /// <summary>
    /// Validate that encryption/decryption is working correctly
    /// </summary>
    /// <returns>Health check result for encryption system</returns>
    Task<EncryptionHealthCheckResult> HealthCheckAsync();

    /// <summary>
    /// Get information about the current encryption configuration
    /// </summary>
    /// <returns>Encryption status and configuration details</returns>
    EncryptionStatus GetEncryptionStatus();

    /// <summary>
    /// Delete a stored credential from the database and invalidate any cached reference.
    /// </summary>
    /// <param name="key">The credential key to delete</param>
    /// <returns>Result indicating success or failure</returns>
    Task<EncryptionResult> DeleteAsync(string key);

    /// <summary>
    /// Securely dispose of sensitive data from memory
    /// </summary>
    /// <param name="sensitiveData">Data to securely clear</param>
    void SecureClear(Span<char> sensitiveData);
}