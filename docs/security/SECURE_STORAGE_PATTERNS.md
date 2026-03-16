# Secure Storage Implementation Guide for TrashMail Panda

This document consolidates research findings for implementing secure local storage for OAuth tokens, API keys, and application settings in an Electron-based email management application.

## Critical Security Requirements

1. **OS-Level Storage**: Use Electron's `safeStorage` API (replaces deprecated keytar)
2. **Database Encryption**: Implement SQLCipher for encrypted SQLite storage
3. **Application Crypto**: Add AES-256-GCM encryption layer for sensitive data
4. **Token Rotation**: Automatic refresh token rotation and API key rotation
5. **Audit Logging**: Comprehensive security event logging without exposing secrets

## Implementation Stack Decision

### Primary Storage Architecture

**Hybrid Approach - OS Keychain + Encrypted SQLite:**

- **OS Keychain** (via Electron safeStorage): Master encryption keys, primary API keys
- **Encrypted SQLite** (via SQLCipher): Email metadata, user rules, classification history
- **Application Crypto**: Additional encryption layer for extremely sensitive data

### Technology Stack

1. **OS Credential Storage**: Electron `safeStorage` API (not deprecated keytar)
2. **Database Encryption**: `@journeyapps/sqlcipher` package
3. **Application Crypto**: Node.js built-in `crypto` module with AES-256-GCM
4. **Key Derivation**: PBKDF2 with 100,000+ iterations, SHA-512 digest

## Security Patterns

### Token Storage Pattern

```typescript
// Gmail OAuth tokens stored in OS keychain
await electronAPI.secureStorage.storeGmailTokens(tokens);

// OpenAI API key stored in OS keychain
await electronAPI.secureStorage.storeAPIKey('openai', apiKey);

// Email metadata in encrypted SQLite
await sqliteProvider.setEmailMetadata(emailId, encryptedMetadata);
```

### Encryption Key Hierarchy

```
Master Key (OS Keychain)
├── Database Encryption Key (SQLCipher)
├── Application Encryption Key (AES-GCM)
└── Token Encryption Keys (Per-provider)
```

### Error Handling Security

```typescript
// Never expose actual tokens/keys in error messages
catch (error) {
  const sanitized = error.message.replace(/sk-[a-zA-Z0-9]+/g, '[REDACTED_API_KEY]');
  throw new CryptoError(sanitized, 'ENCRYPTION_FAILED');
}
```

## Implementation Requirements

### Electron Main Process Integration

```typescript
// src/main/security/SecureStorageManager.ts
export class SecureStorageManager {
  async storeGmailTokens(tokens: GmailTokens): Promise<Result<void>>;
  async getGmailTokens(): Promise<Result<GmailTokens | null>>;
  async storeAPIKey(provider: string, apiKey: string): Promise<Result<void>>;
  async getAPIKey(provider: string): Promise<Result<string | null>>;
  async rotateEncryptionKey(): Promise<Result<void>>;
  async clearAllCredentials(): Promise<Result<void>>;
}
```

### SQLite Provider Enhancement

```typescript
// src/providers/storage/sqlite/SQLiteProvider.ts
export class SQLiteProvider implements StorageProvider {
  private db: Database; // From @journeyapps/sqlcipher
  private encryptionKey: Buffer;

  async initialize(config: SQLiteStorageConfig): Promise<Result<void>> {
    // Initialize SQLCipher with encryption
    this.db = new Database(config.databasePath);
    await this.db.exec(`PRAGMA key = '${config.encryptionKey}'`);
    await this.setupTables();
  }
}
```

### IPC Security Bridge

```typescript
// src/preload/index.ts
const secureAPI = {
  secureStorage: {
    storeGmailTokens: (tokens: GmailTokens) =>
      ipcRenderer.invoke('secure-storage:store-gmail-tokens', tokens),
    getGmailTokens: () => ipcRenderer.invoke('secure-storage:get-gmail-tokens'),
    // ... other secure operations
  },
};
```

## Validation Requirements

### Unit Tests

```typescript
describe('SecureStorageManager', () => {
  test('should encrypt Gmail tokens before storage', async () => {
    const tokens = mockGmailTokens();
    await storageManager.storeGmailTokens(tokens);

    // Verify tokens are encrypted in storage
    const rawStored = await getRawStoredData('gmail_tokens');
    expect(rawStored).not.toContain(tokens.accessToken);
    expect(rawStored).not.toContain(tokens.refreshToken);
  });

  test('should handle encryption key rotation', async () => {
    await storageManager.storeAPIKey('openai', 'sk-test-key');
    await storageManager.rotateEncryptionKey();

    const retrieved = await storageManager.getAPIKey('openai');
    expect(retrieved.success).toBe(true);
    expect(retrieved.data).toBe('sk-test-key');
  });
});
```

### Integration Tests

```typescript
describe('End-to-End Security', () => {
  test('should maintain security across app restart', async () => {
    // Store credentials
    await app.storeCredentials(testTokens);

    // Simulate app restart
    await app.restart();

    // Verify credentials still accessible
    const tokens = await app.getCredentials();
    expect(tokens.success).toBe(true);
  });
});
```

### Security Tests

```typescript
describe('Security Validation', () => {
  test('should reject tampered encrypted data', async () => {
    await storageManager.storeAPIKey('openai', 'sk-test');

    // Tamper with stored data
    await tamperWithStoredData('openai_key');

    const result = await storageManager.getAPIKey('openai');
    expect(result.success).toBe(false);
    expect(result.error.code).toBe('TAMPER_DETECTED');
  });
});
```

## Performance Considerations

### Caching Strategy

```typescript
class SecureCache {
  private cache = new Map<string, CacheEntry>();
  private readonly TTL = 5 * 60 * 1000; // 5 minutes

  async getCachedToken(key: string): Promise<string | null> {
    const entry = this.cache.get(key);
    if (entry && Date.now() < entry.expiry) {
      return entry.value;
    }
    return null;
  }
}
```

### Encryption Optimization

```typescript
// Batch encryption operations
async encryptBatch(items: Array<{key: string, value: string}>): Promise<EncryptedBatch> {
  const sharedKey = await this.deriveKey();
  return items.map(item => ({
    key: item.key,
    encrypted: this.encryptWithSharedKey(item.value, sharedKey)
  }));
}
```

## Migration Strategy

### From Stub to Production

1. **Phase 1**: Implement core SecureStorageManager
2. **Phase 2**: Integrate SQLCipher for metadata storage
3. **Phase 3**: Add token rotation and lifecycle management
4. **Phase 4**: Implement comprehensive audit logging

### Data Migration

```typescript
async migrateFromStub(): Promise<Result<void>> {
  // Check for existing stub data
  const stubConfig = await this.loadStubConfiguration();
  if (stubConfig) {
    // Migrate to secure storage
    await this.secureStorage.initialize();
    await this.migrateCredentials(stubConfig);
    await this.cleanupStubData();
  }
}
```

## Error Recovery Procedures

### Credential Recovery

```typescript
class CredentialRecoveryManager {
  async handleCorruptedStorage(): Promise<Result<void>> {
    // 1. Backup current state
    await this.createEmergencyBackup();

    // 2. Reset storage
    await this.secureStorage.clearAllCredentials();

    // 3. Trigger onboarding flow
    await this.uiManager.showOnboardingFlow();

    return createSuccessResult(undefined);
  }
}
```

## Compliance and Auditing

### GDPR Compliance

- All personal data (email content) encrypted at rest
- User data deletion capabilities implemented
- Data retention policies configurable
- Audit trail for all personal data access

### Security Audit Trail

```typescript
interface SecurityAuditEvent {
  timestamp: Date;
  eventType: 'credential_access' | 'encryption_key_rotation' | 'token_refresh';
  userId?: string;
  success: boolean;
  metadata: Record<string, unknown>;
}
```

This implementation guide ensures the TrashMail Panda application meets enterprise-grade security requirements while maintaining usability for email processing workflows.
