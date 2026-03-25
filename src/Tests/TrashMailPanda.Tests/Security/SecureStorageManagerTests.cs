using Microsoft.Extensions.Logging;
using Moq;
using TrashMailPanda.Shared.Security;
using Xunit;

namespace TrashMailPanda.Tests.Security;

/// <summary>
/// Tests for SecureStorageManager integration with credential encryption
/// </summary>
[Trait("Category", "Security")]
public class SecureStorageManagerTests : IDisposable
{
    private readonly Mock<ICredentialEncryption> _mockCredentialEncryption;
    private readonly Mock<ILogger<SecureStorageManager>> _mockLogger;
    private readonly SecureStorageManager _secureStorageManager;

    public SecureStorageManagerTests()
    {
        _mockCredentialEncryption = new Mock<ICredentialEncryption>();
        _mockLogger = new Mock<ILogger<SecureStorageManager>>();
        _secureStorageManager = new SecureStorageManager(_mockCredentialEncryption.Object, _mockLogger.Object);
    }

    [Fact]
    public void Constructor_WithValidParameters_ShouldInitialize()
    {
        // Act & Assert
        Assert.NotNull(_secureStorageManager);
    }

    [Fact]
    public void Constructor_WithNullCredentialEncryption_ShouldThrowArgumentNullException()
    {
        // Act & Assert
        Assert.Throws<ArgumentNullException>(() =>
            new SecureStorageManager(null!, _mockLogger.Object));
    }

    [Fact]
    public void Constructor_WithNullLogger_ShouldThrowArgumentNullException()
    {
        // Act & Assert
        Assert.Throws<ArgumentNullException>(() =>
            new SecureStorageManager(_mockCredentialEncryption.Object, null!));
    }

    [Fact]
    public async Task InitializeAsync_WithSuccessfulEncryptionInit_ShouldSucceed()
    {
        // Arrange
        _mockCredentialEncryption
            .Setup(x => x.InitializeAsync())
            .ReturnsAsync(EncryptionResult.Success());

        _mockCredentialEncryption
            .Setup(x => x.HealthCheckAsync())
            .ReturnsAsync(new EncryptionHealthCheckResult
            {
                IsHealthy = true,
                CanEncrypt = true,
                CanDecrypt = true,
                Issues = new List<string>()
            });

        // Act
        var result = await _secureStorageManager.InitializeAsync();

        // Assert
        Assert.True(result.IsSuccess);
        _mockCredentialEncryption.Verify(x => x.InitializeAsync(), Times.Once);
        _mockCredentialEncryption.Verify(x => x.HealthCheckAsync(), Times.Once);
    }

    [Fact]
    public async Task InitializeAsync_WithFailedEncryptionInit_ShouldFail()
    {
        // Arrange
        _mockCredentialEncryption
            .Setup(x => x.InitializeAsync())
            .ReturnsAsync(EncryptionResult.Failure("Encryption init failed", EncryptionErrorType.ConfigurationError));

        // Act
        var result = await _secureStorageManager.InitializeAsync();

        // Assert
        Assert.False(result.IsSuccess);
        Assert.Contains("Encryption initialization failed", result.ErrorMessage!);
    }

    [Fact]
    public async Task InitializeAsync_WithUnhealthyEncryption_ShouldFail()
    {
        // Arrange
        _mockCredentialEncryption
            .Setup(x => x.InitializeAsync())
            .ReturnsAsync(EncryptionResult.Success());

        _mockCredentialEncryption
            .Setup(x => x.HealthCheckAsync())
            .ReturnsAsync(new EncryptionHealthCheckResult
            {
                IsHealthy = false,
                Issues = new List<string> { "Encryption test failed" }
            });

        // Act
        var result = await _secureStorageManager.InitializeAsync();

        // Assert
        Assert.False(result.IsSuccess);
        Assert.Contains("not healthy", result.ErrorMessage!);
    }

    [Fact]
    public async Task InitializeAsync_WhenAlreadyInitialized_ShouldReturnSuccess()
    {
        // Arrange
        SetupSuccessfulInitialization();
        await _secureStorageManager.InitializeAsync();

        // Act
        var result = await _secureStorageManager.InitializeAsync();

        // Assert
        Assert.True(result.IsSuccess);
        _mockCredentialEncryption.Verify(x => x.InitializeAsync(), Times.Once); // Only called once
    }

    [Fact]
    public async Task StoreCredentialAsync_WithoutInitialization_ShouldFail()
    {
        // Act
        var result = await _secureStorageManager.StoreCredentialAsync("test-key", "test-credential");

        // Assert
        Assert.False(result.IsSuccess);
        Assert.Contains("not initialized", result.ErrorMessage!);
    }

    [Fact]
    public async Task StoreCredentialAsync_WithNullOrEmptyKey_ShouldFail()
    {
        // Arrange
        SetupSuccessfulInitialization();
        await _secureStorageManager.InitializeAsync();

        // Act & Assert
        var nullResult = await _secureStorageManager.StoreCredentialAsync(null!, "credential");
        Assert.False(nullResult.IsSuccess);
        Assert.Contains("key cannot be null or empty", nullResult.ErrorMessage!);

        var emptyResult = await _secureStorageManager.StoreCredentialAsync("", "credential");
        Assert.False(emptyResult.IsSuccess);
        Assert.Contains("key cannot be null or empty", emptyResult.ErrorMessage!);
    }

    [Fact]
    public async Task StoreCredentialAsync_WithNullOrEmptyCredential_ShouldFail()
    {
        // Arrange
        SetupSuccessfulInitialization();
        await _secureStorageManager.InitializeAsync();

        // Act & Assert
        var nullResult = await _secureStorageManager.StoreCredentialAsync("key", null!);
        Assert.False(nullResult.IsSuccess);
        Assert.Contains("Credential cannot be null or empty", nullResult.ErrorMessage!);

        var emptyResult = await _secureStorageManager.StoreCredentialAsync("key", "");
        Assert.False(emptyResult.IsSuccess);
        Assert.Contains("Credential cannot be null or empty", emptyResult.ErrorMessage!);
    }

    [Fact]
    public async Task StoreCredentialAsync_WithValidData_ShouldSucceed()
    {
        // Arrange
        SetupSuccessfulInitialization();
        await _secureStorageManager.InitializeAsync();

        const string key = "test-key";
        const string credential = "test-credential";
        const string encryptedCredential = "encrypted-credential";

        _mockCredentialEncryption
            .Setup(x => x.EncryptAsync(credential, key))
            .ReturnsAsync(EncryptionResult<string>.Success(encryptedCredential));

        // Act
        var result = await _secureStorageManager.StoreCredentialAsync(key, credential);

        // Assert
        Assert.True(result.IsSuccess);
        _mockCredentialEncryption.Verify(x => x.EncryptAsync(credential, key), Times.Once);
    }

    [Fact]
    public async Task StoreCredentialAsync_WithEncryptionFailure_ShouldFail()
    {
        // Arrange
        SetupSuccessfulInitialization();
        await _secureStorageManager.InitializeAsync();

        _mockCredentialEncryption
            .Setup(x => x.EncryptAsync(It.IsAny<string>(), It.IsAny<string>()))
            .ReturnsAsync(EncryptionResult<string>.Failure("Encryption failed", EncryptionErrorType.EncryptionFailed));

        // Act
        var result = await _secureStorageManager.StoreCredentialAsync("key", "credential");

        // Assert
        Assert.False(result.IsSuccess);
        Assert.Contains("Encryption failed", result.ErrorMessage!);
    }

    [Fact]
    public async Task RetrieveCredentialAsync_WithoutInitialization_ShouldFail()
    {
        // Act
        var result = await _secureStorageManager.RetrieveCredentialAsync("test-key");

        // Assert
        Assert.False(result.IsSuccess);
        Assert.Contains("not initialized", result.ErrorMessage!);
    }

    [Fact]
    public async Task RetrieveCredentialAsync_WithNullOrEmptyKey_ShouldFail()
    {
        // Arrange
        SetupSuccessfulInitialization();
        await _secureStorageManager.InitializeAsync();

        // Act & Assert
        var nullResult = await _secureStorageManager.RetrieveCredentialAsync(null!);
        Assert.False(nullResult.IsSuccess);
        Assert.Contains("key cannot be null or empty", nullResult.ErrorMessage!);

        var emptyResult = await _secureStorageManager.RetrieveCredentialAsync("");
        Assert.False(emptyResult.IsSuccess);
        Assert.Contains("key cannot be null or empty", emptyResult.ErrorMessage!);
    }

    [Fact]
    public async Task RetrieveCredentialAsync_WithNonexistentKey_ShouldFail()
    {
        // Arrange
        SetupSuccessfulInitialization();
        await _secureStorageManager.InitializeAsync();

        // Setup DecryptAsync to return failure for nonexistent key
        _mockCredentialEncryption
            .Setup(x => x.DecryptAsync("nonexistent-key", "nonexistent-key"))
            .Returns(Task.FromResult(EncryptionResult<string>.Failure("Key not found in database", EncryptionErrorType.DecryptionFailed)));

        // Act
        var result = await _secureStorageManager.RetrieveCredentialAsync("nonexistent-key");

        // Assert
        Assert.False(result.IsSuccess);
        Assert.Contains("not found", result.ErrorMessage!);
    }

    [Fact]
    public async Task RetrieveCredentialAsync_WithExistingCredential_ShouldSucceed()
    {
        // Arrange
        SetupSuccessfulInitialization();
        await _secureStorageManager.InitializeAsync();

        const string key = "test-key";
        const string originalCredential = "test-credential";
        const string encryptedCredential = "encrypted-credential";

        // First store the credential
        _mockCredentialEncryption
            .Setup(x => x.EncryptAsync(originalCredential, key))
            .ReturnsAsync(EncryptionResult<string>.Success(encryptedCredential));

        await _secureStorageManager.StoreCredentialAsync(key, originalCredential);

        // Setup decryption
        _mockCredentialEncryption
            .Setup(x => x.DecryptAsync(encryptedCredential, key))
            .ReturnsAsync(EncryptionResult<string>.Success(originalCredential));

        // Act
        var result = await _secureStorageManager.RetrieveCredentialAsync(key);

        // Assert
        Assert.True(result.IsSuccess);
        Assert.Equal(originalCredential, result.Value);
        _mockCredentialEncryption.Verify(x => x.DecryptAsync(encryptedCredential, key), Times.Once);
    }

    [Fact]
    public async Task RetrieveCredentialAsync_WithDecryptionFailure_ShouldFail()
    {
        // Arrange
        SetupSuccessfulInitialization();
        await _secureStorageManager.InitializeAsync();

        const string key = "test-key";
        const string credential = "test-credential";
        const string encryptedCredential = "encrypted-credential";

        // Store credential first
        _mockCredentialEncryption
            .Setup(x => x.EncryptAsync(credential, key))
            .ReturnsAsync(EncryptionResult<string>.Success(encryptedCredential));

        await _secureStorageManager.StoreCredentialAsync(key, credential);

        // Setup decryption failure for cached credential (simulating corruption)
        _mockCredentialEncryption
            .Setup(x => x.DecryptAsync(encryptedCredential, key))
            .Returns(Task.FromResult(EncryptionResult<string>.Failure("Decryption failed - corrupted data", EncryptionErrorType.DecryptionFailed)));

        // Setup decryption failure for the direct database retrieval
        _mockCredentialEncryption
            .Setup(x => x.DecryptAsync(key, key))
            .Returns(Task.FromResult(EncryptionResult<string>.Failure("Credential not found in database", EncryptionErrorType.DecryptionFailed)));

        // Act
        var result = await _secureStorageManager.RetrieveCredentialAsync(key);

        // Assert
        Assert.False(result.IsSuccess);
        Assert.Contains("not found", result.ErrorMessage!); // Should remove corrupted entry
    }

    [Fact]
    public async Task RemoveCredentialAsync_WithExistingCredential_ShouldSucceed()
    {
        // Arrange
        SetupSuccessfulInitialization();
        await _secureStorageManager.InitializeAsync();

        // Store a credential first
        _mockCredentialEncryption
            .Setup(x => x.EncryptAsync(It.IsAny<string>(), It.IsAny<string>()))
            .ReturnsAsync(EncryptionResult<string>.Success("encrypted"));

        await _secureStorageManager.StoreCredentialAsync("test-key", "test-credential");

        // Act
        var result = await _secureStorageManager.RemoveCredentialAsync("test-key");

        // Assert
        Assert.True(result.IsSuccess);

        // Verify the credential is actually removed
        var retrieveResult = await _secureStorageManager.RetrieveCredentialAsync("test-key");
        Assert.False(retrieveResult.IsSuccess);
    }

    [Fact]
    public async Task CredentialExistsAsync_WithExistingCredential_ShouldReturnTrue()
    {
        // Arrange
        SetupSuccessfulInitialization();
        await _secureStorageManager.InitializeAsync();

        _mockCredentialEncryption
            .Setup(x => x.EncryptAsync(It.IsAny<string>(), It.IsAny<string>()))
            .ReturnsAsync(EncryptionResult<string>.Success("encrypted"));

        await _secureStorageManager.StoreCredentialAsync("test-key", "test-credential");

        // Act
        var result = await _secureStorageManager.CredentialExistsAsync("test-key");

        // Assert
        Assert.True(result.IsSuccess);
        Assert.True(result.Value);
    }

    [Fact]
    public async Task CredentialExistsAsync_WithNonexistentCredential_ShouldReturnFalse()
    {
        // Arrange
        SetupSuccessfulInitialization();
        await _secureStorageManager.InitializeAsync();

        // Act
        var result = await _secureStorageManager.CredentialExistsAsync("nonexistent-key");

        // Assert
        Assert.True(result.IsSuccess);
        Assert.False(result.Value);
    }

    [Fact]
    public async Task GetStoredCredentialKeysAsync_WithMultipleCredentials_ShouldReturnAllKeys()
    {
        // Arrange
        SetupSuccessfulInitialization();
        await _secureStorageManager.InitializeAsync();

        var keys = new[] { "key1", "key2", "key3" };

        _mockCredentialEncryption
            .Setup(x => x.EncryptAsync(It.IsAny<string>(), It.IsAny<string>()))
            .ReturnsAsync(EncryptionResult<string>.Success("encrypted"));

        foreach (var key in keys)
        {
            await _secureStorageManager.StoreCredentialAsync(key, "credential");
        }

        // Act
        var result = await _secureStorageManager.GetStoredCredentialKeysAsync();

        // Assert
        Assert.True(result.IsSuccess);
        Assert.Equal(keys.Length, result.Value!.Count);
        Assert.All(keys, key => Assert.Contains(key, result.Value));
    }

    [Fact]
    public async Task HealthCheckAsync_WithHealthySystem_ShouldReturnHealthy()
    {
        // Arrange
        SetupSuccessfulInitialization();
        await _secureStorageManager.InitializeAsync();

        _mockCredentialEncryption
            .Setup(x => x.HealthCheckAsync())
            .ReturnsAsync(new EncryptionHealthCheckResult
            {
                IsHealthy = true,
                Issues = new List<string>()
            });

        // Set up encryption with correct parameter order: EncryptAsync(plainText, context)
        _mockCredentialEncryption
            .Setup(x => x.EncryptAsync("test-credential-value-12345", "health_check_test_credential"))
            .ReturnsAsync(EncryptionResult<string>.Success("encrypted_test_value"));

        _mockCredentialEncryption
            .Setup(x => x.DecryptAsync("encrypted_test_value", "health_check_test_credential"))
            .ReturnsAsync(EncryptionResult<string>.Success("test-credential-value-12345"));

        // Generic fallback for other calls
        _mockCredentialEncryption
            .Setup(x => x.EncryptAsync(It.IsAny<string>(), It.IsAny<string>()))
            .ReturnsAsync((string plainText, string context) => EncryptionResult<string>.Success($"encrypted_{plainText}"));

        _mockCredentialEncryption
            .Setup(x => x.DecryptAsync(It.IsAny<string>(), It.IsAny<string>()))
            .ReturnsAsync((string encryptedText, string context) =>
            {
                // Strip the "encrypted_" prefix to simulate decryption
                var originalValue = encryptedText.StartsWith("encrypted_")
                    ? encryptedText.Substring(10)
                    : encryptedText;
                return EncryptionResult<string>.Success(originalValue);
            });

        // Act
        var result = await _secureStorageManager.HealthCheckAsync();

        // Assert
        Assert.True(result.IsHealthy);
        Assert.Equal("Healthy", result.Status);
        Assert.Empty(result.Issues);
    }

    [Fact]
    public async Task StoreGmailTokenAsync_ShouldStoreWithCorrectPrefix()
    {
        // Arrange
        SetupSuccessfulInitialization();
        await _secureStorageManager.InitializeAsync();

        const string tokenType = "access_token";
        const string token = "gmail-access-token-123";

        _mockCredentialEncryption
            .Setup(x => x.EncryptAsync(token, $"gmail_{tokenType}"))
            .ReturnsAsync(EncryptionResult<string>.Success("encrypted"))
            .Verifiable();

        // Act
        var result = await _secureStorageManager.StoreGmailTokenAsync(tokenType, token);

        // Assert
        Assert.True(result.IsSuccess);
        _mockCredentialEncryption.Verify();
    }

    [Fact]
    public async Task RetrieveGmailTokenAsync_ShouldRetrieveWithCorrectPrefix()
    {
        // Arrange
        SetupSuccessfulInitialization();
        await _secureStorageManager.InitializeAsync();

        const string tokenType = "access_token";
        const string token = "gmail-access-token-123";
        const string key = $"gmail_{tokenType}";

        // Store token first
        _mockCredentialEncryption
            .Setup(x => x.EncryptAsync(token, key))
            .ReturnsAsync(EncryptionResult<string>.Success("encrypted"));

        await _secureStorageManager.StoreGmailTokenAsync(tokenType, token);

        _mockCredentialEncryption
            .Setup(x => x.DecryptAsync("encrypted", key))
            .ReturnsAsync(EncryptionResult<string>.Success(token));

        // Act
        var result = await _secureStorageManager.RetrieveGmailTokenAsync(tokenType);

        // Assert
        Assert.True(result.IsSuccess);
        Assert.Equal(token, result.Value);
    }

    [Fact]
    public async Task StoreOpenAIKeyAsync_ShouldStoreWithCorrectKey()
    {
        // Arrange
        SetupSuccessfulInitialization();
        await _secureStorageManager.InitializeAsync();

        const string apiKey = "sk-openai-api-key-123";

        _mockCredentialEncryption
            .Setup(x => x.EncryptAsync(apiKey, "openai_api_key"))
            .ReturnsAsync(EncryptionResult<string>.Success("encrypted"))
            .Verifiable();

        // Act
        var result = await _secureStorageManager.StoreOpenAIKeyAsync(apiKey);

        // Assert
        Assert.True(result.IsSuccess);
        _mockCredentialEncryption.Verify();
    }

    [Fact]
    public async Task RetrieveOpenAIKeyAsync_ShouldRetrieveWithCorrectKey()
    {
        // Arrange
        SetupSuccessfulInitialization();
        await _secureStorageManager.InitializeAsync();

        const string apiKey = "sk-openai-api-key-123";

        // Store key first
        _mockCredentialEncryption
            .Setup(x => x.EncryptAsync(apiKey, "openai_api_key"))
            .ReturnsAsync(EncryptionResult<string>.Success("encrypted"));

        await _secureStorageManager.StoreOpenAIKeyAsync(apiKey);

        _mockCredentialEncryption
            .Setup(x => x.DecryptAsync("encrypted", "openai_api_key"))
            .ReturnsAsync(EncryptionResult<string>.Success(apiKey));

        // Act
        var result = await _secureStorageManager.RetrieveOpenAIKeyAsync();

        // Assert
        Assert.True(result.IsSuccess);
        Assert.Equal(apiKey, result.Value);
    }

    private void SetupSuccessfulInitialization()
    {
        _mockCredentialEncryption
            .Setup(x => x.InitializeAsync())
            .ReturnsAsync(EncryptionResult.Success());

        _mockCredentialEncryption
            .Setup(x => x.HealthCheckAsync())
            .ReturnsAsync(new EncryptionHealthCheckResult
            {
                IsHealthy = true,
                CanEncrypt = true,
                CanDecrypt = true,
                KeyGenerationWorks = true,
                Issues = new List<string>()
            });

        _mockCredentialEncryption
            .Setup(x => x.GetEncryptionStatus())
            .Returns(new EncryptionStatus
            {
                IsInitialized = true,
                Platform = "Test",
                EncryptionMethod = "Test"
            });

        _mockCredentialEncryption
            .Setup(x => x.DeleteAsync(It.IsAny<string>()))
            .ReturnsAsync(EncryptionResult.Success());
    }

    public void Dispose()
    {
        // Clean up test resources if needed
    }
}