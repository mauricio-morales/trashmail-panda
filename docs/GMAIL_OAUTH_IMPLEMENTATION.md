# Gmail OAuth 2.0 Implementation Guide for Electron Applications

## Table of Contents

- [OAuth 2.0 Flow Overview](#oauth-20-flow-overview)
- [Required OAuth Scopes](#required-oauth-scopes)
- [Desktop Application Implementation](#desktop-application-implementation)
- [Security Best Practices](#security-best-practices)
- [Error Handling Patterns](#error-handling-patterns)
- [Rate Limiting and Quotas](#rate-limiting-and-quotas)
- [Complete Implementation Example](#complete-implementation-example)
- [Official Documentation References](#official-documentation-references)

## OAuth 2.0 Flow Overview

### Prerequisites

1. **Enable Gmail API** in Google Cloud Console
2. **Create OAuth 2.0 Credentials** (Desktop Application type)
3. **Configure authorized redirect URIs** (use loopback for desktop apps)

### Complete Authorization Flow

#### Step 1: Generate Authorization URL

```typescript
const oauth2Client = new google.auth.OAuth2(
  CLIENT_ID,
  CLIENT_SECRET,
  'http://127.0.0.1:8080', // Loopback redirect for desktop
);

const authUrl = oauth2Client.generateAuthUrl({
  access_type: 'offline', // Required for refresh tokens
  scope: [
    'https://www.googleapis.com/auth/gmail.modify',
    'https://www.googleapis.com/auth/gmail.labels',
  ],
  prompt: 'consent', // Force consent to get refresh token
  code_challenge: codeChallenge, // PKCE for security
  code_challenge_method: 'S256',
});
```

#### Step 2: Handle Authorization Code Exchange

```typescript
// After user authorization, exchange code for tokens
const { tokens } = await oauth2Client.getToken(authorizationCode);

// Store tokens securely
oauth2Client.setCredentials(tokens);

// Listen for token refresh events
oauth2Client.on('tokens', (newTokens) => {
  if (newTokens.refresh_token) {
    // Securely store the new refresh token
    await storeRefreshTokenSecurely(newTokens.refresh_token);
  }
  // Always store the new access token
  await storeAccessTokenSecurely(newTokens.access_token);
});
```

#### Step 3: Implement PKCE for Security

```typescript
import crypto from 'crypto';

// Generate code verifier and challenge
function generatePKCEPair() {
  const codeVerifier = crypto.randomBytes(32).toString('base64url');
  const codeChallenge = crypto.createHash('sha256').update(codeVerifier).digest('base64url');

  return { codeVerifier, codeChallenge };
}

const { codeVerifier, codeChallenge } = generatePKCEPair();

// Use in authorization URL generation (Step 1)
// Include codeVerifier in token exchange (Step 2)
```

## Required OAuth Scopes

### Scope Categories and Permissions

#### Non-Sensitive Scopes (Basic Permissions)

| Scope          | Description                             | Use Case                    |
| -------------- | --------------------------------------- | --------------------------- |
| `gmail.labels` | Create, read, update, and delete labels | Label management operations |

#### Sensitive Scopes (Moderate Permissions)

| Scope                                   | Description                        | Use Case                       |
| --------------------------------------- | ---------------------------------- | ------------------------------ |
| `gmail.send`                            | Send messages without read/modify  | Send-only email functionality  |
| `gmail.addons.current.message.metadata` | View email metadata during runtime | Message headers and basic info |

#### Restricted Scopes (High-Level Permissions)

| Scope                    | Description                            | Use Case                                |
| ------------------------ | -------------------------------------- | --------------------------------------- |
| `gmail.readonly`         | Read all resources and metadata        | Safe read-only access                   |
| `gmail.modify`           | Read/write operations (no deletion)    | **Recommended for TrashMail Panda** |
| `gmail.compose`          | Create, read, update drafts and send   | Draft management and sending            |
| `gmail.metadata`         | Read metadata excluding message bodies | Headers, labels, thread info            |
| `gmail.settings.basic`   | Manage basic mail settings             | Filter and forwarding rules             |
| `gmail.settings.sharing` | Manage sensitive mail settings         | Advanced settings management            |
| `mail.google.com/`       | Full mailbox access including deletion | **Use with extreme caution**            |

### Recommended Scope Combination for TrashMail Panda

```typescript
const RECOMMENDED_SCOPES = [
  'https://www.googleapis.com/auth/gmail.modify', // Primary scope for triage operations
  'https://www.googleapis.com/auth/gmail.labels', // Label management
  'https://www.googleapis.com/auth/userinfo.email', // User identification
  'https://www.googleapis.com/auth/userinfo.profile', // User profile info
];
```

## Desktop Application Implementation

### Electron-Specific OAuth Setup

#### Main Process OAuth Handler

```typescript
// main/oauth-handler.ts
import { BrowserWindow, shell } from 'electron';
import { google } from 'googleapis';
import express from 'express';
import { AddressInfo } from 'net';

export class GmailOAuthHandler {
  private oauth2Client: any;
  private server: any;
  private authWindow: BrowserWindow | null = null;

  constructor(clientId: string, clientSecret: string) {
    this.oauth2Client = new google.auth.OAuth2(
      clientId,
      clientSecret,
      'http://127.0.0.1', // Will be set dynamically with port
    );
  }

  async authenticate(): Promise<{ access_token: string; refresh_token?: string }> {
    return new Promise((resolve, reject) => {
      // Start local server for OAuth callback
      const app = express();
      this.server = app.listen(0, '127.0.0.1', () => {
        const port = (this.server.address() as AddressInfo).port;

        // Update redirect URI with actual port
        this.oauth2Client.redirectUri = `http://127.0.0.1:${port}/callback`;

        // Setup callback route
        app.get('/callback', async (req, res) => {
          try {
            const { code } = req.query;
            const { tokens } = await this.oauth2Client.getToken(code);

            res.send('<h1>Authentication successful!</h1><p>You can close this window.</p>');
            this.cleanup();
            resolve(tokens);
          } catch (error) {
            res.send('<h1>Authentication failed</h1>');
            this.cleanup();
            reject(error);
          }
        });

        // Generate and open auth URL
        const authUrl = this.oauth2Client.generateAuthUrl({
          access_type: 'offline',
          scope: RECOMMENDED_SCOPES,
          prompt: 'consent',
        });

        // Open in system browser (more secure than embedded)
        shell.openExternal(authUrl);
      });
    });
  }

  private cleanup() {
    if (this.server) {
      this.server.close();
    }
    if (this.authWindow) {
      this.authWindow.close();
      this.authWindow = null;
    }
  }
}
```

#### Secure Token Storage with Encryption

```typescript
// main/secure-token-storage.ts
import { safeStorage } from 'electron';
import { readFileSync, writeFileSync, existsSync } from 'fs';
import path from 'path';
import { app } from 'electron';

export class SecureTokenStorage {
  private tokenPath: string;

  constructor() {
    const userDataPath = app.getPath('userData');
    this.tokenPath = path.join(userDataPath, 'oauth-tokens.encrypted');
  }

  async storeTokens(tokens: { access_token: string; refresh_token?: string }): Promise<void> {
    if (!safeStorage.isEncryptionAvailable()) {
      throw new Error('Encryption not available on this system');
    }

    const tokenData = JSON.stringify(tokens);
    const encryptedData = safeStorage.encryptString(tokenData);

    writeFileSync(this.tokenPath, encryptedData);
  }

  async retrieveTokens(): Promise<{ access_token: string; refresh_token?: string } | null> {
    if (!existsSync(this.tokenPath)) {
      return null;
    }

    try {
      const encryptedData = readFileSync(this.tokenPath);
      const decryptedData = safeStorage.decryptString(encryptedData);
      return JSON.parse(decryptedData);
    } catch (error) {
      console.error('Failed to decrypt tokens:', error);
      return null;
    }
  }

  async clearTokens(): Promise<void> {
    if (existsSync(this.tokenPath)) {
      writeFileSync(this.tokenPath, ''); // Clear file content
    }
  }
}
```

## Security Best Practices

### Token Security Implementation

#### 1. Secure Storage Requirements

```typescript
// Use Electron's safeStorage for token encryption
const encryptedTokens = safeStorage.encryptString(JSON.stringify(tokens));

// Never store tokens in plain text
// ❌ Bad: localStorage.setItem('tokens', JSON.stringify(tokens));
// ✅ Good: Use encrypted storage as shown above
```

#### 2. Refresh Token Management

```typescript
export class TokenManager {
  private refreshTokenRotation = true;

  async refreshAccessToken(refreshToken: string): Promise<TokenResult> {
    try {
      oauth2Client.setCredentials({ refresh_token: refreshToken });

      const { credentials } = await oauth2Client.refreshAccessToken();

      // Store new tokens (refresh token may rotate)
      await this.storeTokensSecurely({
        access_token: credentials.access_token!,
        refresh_token: credentials.refresh_token || refreshToken, // Keep old if no new one
      });

      return createSuccessResult(credentials);
    } catch (error) {
      // Token invalid - user needs to re-authenticate
      if (error.code === 400 || error.code === 401) {
        await this.clearStoredTokens();
        return createErrorResult(new AuthenticationError('Refresh token invalid'));
      }
      throw error;
    }
  }
}
```

#### 3. Access Token Lifecycle

```typescript
export class GmailTokenHandler {
  private accessToken: string | null = null;
  private tokenExpiry: Date | null = null;

  async getValidAccessToken(): Promise<string> {
    // Check if current token is still valid
    if (this.accessToken && this.tokenExpiry && new Date() < this.tokenExpiry) {
      return this.accessToken;
    }

    // Refresh if expired
    const storedTokens = await this.secureStorage.retrieveTokens();
    if (storedTokens?.refresh_token) {
      const result = await this.refreshAccessToken(storedTokens.refresh_token);
      if (result.success) {
        this.accessToken = result.data.access_token;
        this.tokenExpiry = new Date(Date.now() + 3600000); // 1 hour from now
        return this.accessToken;
      }
    }

    throw new AuthenticationError('No valid tokens available');
  }
}
```

### Security Checklist

- ✅ **Use PKCE** for authorization code exchange
- ✅ **Store tokens encrypted** using Electron's safeStorage
- ✅ **Implement refresh token rotation**
- ✅ **Use loopback redirect** (127.0.0.1) for desktop apps
- ✅ **Open auth in system browser** (not embedded WebView)
- ✅ **Handle token revocation** gracefully
- ✅ **Use shortest viable token lifetime**
- ✅ **Request minimal scopes** required for functionality

## Error Handling Patterns

### OAuth-Specific Error Handling

#### 1. Authentication Errors (401)

```typescript
async handleAuthenticationError(error: any): Promise<TokenResult> {
  if (error.code === 401) {
    // Token expired or invalid
    const refreshResult = await this.attemptTokenRefresh();

    if (refreshResult.success) {
      return refreshResult;
    } else {
      // Refresh failed - need full re-authentication
      await this.clearStoredTokens();
      return createErrorResult(
        new AuthenticationError(
          'Authentication expired. Please re-authorize the application.',
          'REAUTH_REQUIRED'
        )
      );
    }
  }

  return createErrorResult(new AuthenticationError(error.message));
}
```

#### 2. Authorization Errors (403)

```typescript
async handleAuthorizationError(error: any): Promise<void> {
  if (error.code === 403) {
    const errorDetails = error.errors?.[0];

    switch (errorDetails?.reason) {
      case 'dailyLimitExceeded':
        throw new QuotaError('Daily API limit exceeded. Try again tomorrow.');

      case 'userRateLimitExceeded':
        throw new RateLimitError('User rate limit exceeded. Please slow down requests.');

      case 'quotaExceeded':
        throw new QuotaError('API quota exceeded.');

      case 'insufficientPermissions':
        throw new AuthorizationError(
          'Insufficient permissions. Please re-authorize with required scopes.',
          'INSUFFICIENT_SCOPES'
        );

      default:
        throw new AuthorizationError('Access forbidden: ' + errorDetails?.message);
    }
  }
}
```

#### 3. Rate Limiting with Exponential Backoff

```typescript
export class RateLimitHandler {
  private maxRetries = 5;
  private baseDelay = 1000; // 1 second

  async executeWithRetry<T>(operation: () => Promise<T>): Promise<T> {
    let lastError: Error;

    for (let attempt = 0; attempt < this.maxRetries; attempt++) {
      try {
        return await operation();
      } catch (error: any) {
        lastError = error;

        if (error.code === 429 || error.code === 503) {
          const delayMs = this.baseDelay * Math.pow(2, attempt);
          console.log(
            `Rate limited. Waiting ${delayMs}ms before retry ${attempt + 1}/${this.maxRetries}`,
          );

          await this.delay(delayMs);
          continue;
        }

        // Non-retryable error
        throw error;
      }
    }

    throw new RateLimitError(
      `Operation failed after ${this.maxRetries} retries: ${lastError.message}`,
    );
  }

  private delay(ms: number): Promise<void> {
    return new Promise((resolve) => setTimeout(resolve, ms));
  }
}
```

### Common Error Scenarios and Solutions

| Error Code | Scenario                     | Solution                                      |
| ---------- | ---------------------------- | --------------------------------------------- |
| 400        | Invalid request parameters   | Validate all parameters before API calls      |
| 401        | Expired/invalid access token | Refresh token or re-authenticate              |
| 403        | Insufficient permissions     | Check scopes, request re-authorization        |
| 403        | Rate limit exceeded          | Implement exponential backoff                 |
| 403        | Daily quota exceeded         | Display user-friendly message, retry tomorrow |
| 429        | Too many requests            | Implement request throttling                  |
| 500/503    | Server errors                | Retry with exponential backoff                |

## Rate Limiting and Quotas

### Gmail API Quotas (Current Limits)

| Limit Type  | Quota                  | Notes                  |
| ----------- | ---------------------- | ---------------------- |
| Per Project | 1,200,000 units/minute | Total across all users |
| Per User    | 15,000 units/minute    | Per individual user    |

### Quota Unit Costs (Key Operations)

| Operation              | Quota Units | Description                      |
| ---------------------- | ----------- | -------------------------------- |
| `messages.list`        | 5           | List messages in mailbox         |
| `messages.get`         | 5           | Get single message               |
| `messages.modify`      | 5           | Modify message labels            |
| `messages.batchModify` | 50          | Batch modify up to 1000 messages |
| `messages.send`        | 100         | Send new message                 |
| `labels.list`          | 5           | List all labels                  |
| `labels.create`        | 5           | Create new label                 |

### Quota Management Implementation

#### 1. Request Throttling

```typescript
export class QuotaManager {
  private requestQueue: Array<() => Promise<any>> = [];
  private processing = false;
  private requestsPerMinute = 800; // Conservative limit
  private requestCount = 0;
  private windowStart = Date.now();

  async throttledRequest<T>(operation: () => Promise<T>): Promise<T> {
    return new Promise((resolve, reject) => {
      this.requestQueue.push(async () => {
        try {
          const result = await operation();
          resolve(result);
        } catch (error) {
          reject(error);
        }
      });

      this.processQueue();
    });
  }

  private async processQueue(): Promise<void> {
    if (this.processing || this.requestQueue.length === 0) return;

    this.processing = true;

    while (this.requestQueue.length > 0) {
      // Check if we're within rate limits
      const now = Date.now();
      const windowElapsed = now - this.windowStart;

      if (windowElapsed >= 60000) {
        // Reset window
        this.requestCount = 0;
        this.windowStart = now;
      }

      if (this.requestCount >= this.requestsPerMinute) {
        // Wait for next window
        const waitTime = 60000 - windowElapsed;
        await this.delay(waitTime);
        continue;
      }

      // Process next request
      const request = this.requestQueue.shift()!;
      this.requestCount++;

      try {
        await request();
      } catch (error) {
        console.error('Throttled request failed:', error);
      }

      // Small delay between requests
      await this.delay(100);
    }

    this.processing = false;
  }

  private delay(ms: number): Promise<void> {
    return new Promise((resolve) => setTimeout(resolve, ms));
  }
}
```

#### 2. Batch Operations for Efficiency

```typescript
export class EfficientGmailOperations {
  async batchModifyMessages(
    messageIds: string[],
    labelsToAdd: string[],
    labelsToRemove: string[],
  ): Promise<void> {
    const batchSize = 1000; // Gmail API limit

    for (let i = 0; i < messageIds.length; i += batchSize) {
      const batch = messageIds.slice(i, i + batchSize);

      await gmail.users.messages.batchModify({
        userId: 'me',
        requestBody: {
          ids: batch,
          addLabelIds: labelsToAdd,
          removeLabelIds: labelsToRemove,
        },
      });

      // Progress tracking
      const processed = Math.min(i + batchSize, messageIds.length);
      console.log(`Processed ${processed}/${messageIds.length} messages`);
    }
  }
}
```

## Complete Implementation Example

### Full Gmail OAuth Provider

```typescript
// providers/email/gmail-provider.ts
import { google } from 'googleapis';
import { OAuth2Client } from 'google-auth-library';
import { EmailProvider, EmailMessage, EmailResult } from '@shared/types';
import { SecureTokenStorage } from './secure-token-storage';
import { RateLimitHandler } from './rate-limit-handler';

export class GmailProvider implements EmailProvider {
  private oauth2Client: OAuth2Client;
  private gmail: any;
  private tokenStorage: SecureTokenStorage;
  private rateLimitHandler: RateLimitHandler;

  constructor(clientId: string, clientSecret: string) {
    this.oauth2Client = new google.auth.OAuth2(clientId, clientSecret, 'http://127.0.0.1:8080');

    this.gmail = google.gmail({ version: 'v1', auth: this.oauth2Client });
    this.tokenStorage = new SecureTokenStorage();
    this.rateLimitHandler = new RateLimitHandler();

    // Listen for token refresh
    this.oauth2Client.on('tokens', async (tokens) => {
      await this.tokenStorage.storeTokens(tokens);
    });
  }

  async initialize(): Promise<EmailResult<void>> {
    try {
      // Try to load existing tokens
      const storedTokens = await this.tokenStorage.retrieveTokens();

      if (storedTokens) {
        this.oauth2Client.setCredentials(storedTokens);

        // Verify tokens are still valid
        const testResult = await this.healthCheck();
        if (testResult.success) {
          return createSuccessResult(undefined);
        }
      }

      // Need fresh authentication
      return createErrorResult(new AuthenticationError('Authentication required', 'AUTH_REQUIRED'));
    } catch (error) {
      return createErrorResult(
        new ConfigurationError(`Failed to initialize Gmail provider: ${error.message}`),
      );
    }
  }

  async authenticate(): Promise<EmailResult<void>> {
    try {
      const oauthHandler = new GmailOAuthHandler(
        this.oauth2Client.clientId!,
        this.oauth2Client.clientSecret!,
      );

      const tokens = await oauthHandler.authenticate();
      await this.tokenStorage.storeTokens(tokens);
      this.oauth2Client.setCredentials(tokens);

      return createSuccessResult(undefined);
    } catch (error) {
      return createErrorResult(new AuthenticationError(`Authentication failed: ${error.message}`));
    }
  }

  async getMessages(query?: string, maxResults = 100): Promise<EmailResult<EmailMessage[]>> {
    try {
      const result = await this.rateLimitHandler.executeWithRetry(async () => {
        return await this.gmail.users.messages.list({
          userId: 'me',
          q: query,
          maxResults,
        });
      });

      const messages: EmailMessage[] = [];

      if (result.data.messages) {
        for (const message of result.data.messages) {
          const messageResult = await this.getMessage(message.id!);
          if (messageResult.success) {
            messages.push(messageResult.data);
          }
        }
      }

      return createSuccessResult(messages);
    } catch (error) {
      return this.handleApiError(error);
    }
  }

  async getMessage(messageId: string): Promise<EmailResult<EmailMessage>> {
    try {
      const result = await this.rateLimitHandler.executeWithRetry(async () => {
        return await this.gmail.users.messages.get({
          userId: 'me',
          id: messageId,
          format: 'full',
        });
      });

      const message = this.parseGmailMessage(result.data);
      return createSuccessResult(message);
    } catch (error) {
      return this.handleApiError(error);
    }
  }

  async modifyMessage(
    messageId: string,
    addLabels: string[] = [],
    removeLabels: string[] = [],
  ): Promise<EmailResult<void>> {
    try {
      await this.rateLimitHandler.executeWithRetry(async () => {
        return await this.gmail.users.messages.modify({
          userId: 'me',
          id: messageId,
          requestBody: {
            addLabelIds: addLabels,
            removeLabelIds: removeLabels,
          },
        });
      });

      return createSuccessResult(undefined);
    } catch (error) {
      return this.handleApiError(error);
    }
  }

  async healthCheck(): Promise<EmailResult<boolean>> {
    try {
      await this.rateLimitHandler.executeWithRetry(async () => {
        return await this.gmail.users.getProfile({ userId: 'me' });
      });

      return createSuccessResult(true);
    } catch (error) {
      return createErrorResult(new NetworkError(`Health check failed: ${error.message}`));
    }
  }

  private handleApiError(error: any): EmailResult<any> {
    if (error.code === 401) {
      return createErrorResult(
        new AuthenticationError('Authentication expired', 'REAUTH_REQUIRED'),
      );
    } else if (error.code === 403) {
      return createErrorResult(new AuthorizationError(`Access forbidden: ${error.message}`));
    } else if (error.code === 429) {
      return createErrorResult(new RateLimitError('Rate limit exceeded'));
    } else {
      return createErrorResult(new NetworkError(`API error: ${error.message}`));
    }
  }

  private parseGmailMessage(gmailMessage: any): EmailMessage {
    // Parse Gmail API response into EmailMessage interface
    const headers = gmailMessage.payload?.headers || [];

    return {
      id: gmailMessage.id,
      threadId: gmailMessage.threadId,
      subject: headers.find((h: any) => h.name === 'Subject')?.value || '',
      from: headers.find((h: any) => h.name === 'From')?.value || '',
      to: headers.find((h: any) => h.name === 'To')?.value || '',
      date: new Date(parseInt(gmailMessage.internalDate)),
      body: this.extractMessageBody(gmailMessage.payload),
      labels: gmailMessage.labelIds || [],
      snippet: gmailMessage.snippet || '',
      unread: gmailMessage.labelIds?.includes('UNREAD') || false,
    };
  }

  private extractMessageBody(payload: any): string {
    // Implement body extraction logic
    // Handle multipart messages, HTML/text content, etc.
    if (payload.body?.data) {
      return Buffer.from(payload.body.data, 'base64').toString('utf-8');
    }

    if (payload.parts) {
      for (const part of payload.parts) {
        if (part.mimeType === 'text/plain' && part.body?.data) {
          return Buffer.from(part.body.data, 'base64').toString('utf-8');
        }
      }
    }

    return '';
  }
}
```

## Official Documentation References

### Core OAuth 2.0 Documentation

- **OAuth 2.0 for Native Apps**: https://developers.google.com/identity/protocols/oauth2/native-app
- **Gmail API Authorization**: https://developers.google.com/gmail/api/auth/web-server
- **OAuth 2.0 Scopes**: https://developers.google.com/gmail/api/auth/scopes

### API References

- **Gmail API Reference**: https://developers.google.com/gmail/api/reference/rest
- **Gmail API Quotas**: https://developers.google.com/gmail/api/reference/quota
- **Error Handling**: https://developers.google.com/gmail/api/guides/handle-errors

### Security and Best Practices

- **OAuth 2.0 Security (RFC 6819)**: https://datatracker.ietf.org/doc/html/rfc6819
- **PKCE Specification (RFC 7636)**: https://datatracker.ietf.org/doc/html/rfc7636
- **OAuth 2.0 for Native Apps (RFC 8252)**: https://datatracker.ietf.org/doc/html/rfc8252

### Implementation Libraries

- **Google APIs Node.js Client**: https://github.com/googleapis/google-api-nodejs-client
- **Google Auth Library**: https://github.com/googleapis/google-auth-library-nodejs

### Electron-Specific Resources

- **Electron Security**: https://www.electronjs.org/docs/latest/tutorial/security
- **Electron safeStorage**: https://www.electronjs.org/docs/latest/api/safe-storage

---

_This document provides comprehensive implementation guidance for Gmail OAuth 2.0 in Electron applications. Always refer to the latest official Google documentation for the most current information._
