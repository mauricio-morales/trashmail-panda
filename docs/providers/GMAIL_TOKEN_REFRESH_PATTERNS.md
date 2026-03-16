# Gmail OAuth Refresh Token Best Practices

## Table of Contents

1. [Google OAuth 2.0 Best Practices](#google-oauth-20-best-practices)
2. [Desktop Application OAuth Patterns](#desktop-application-oauth-patterns)
3. [Implementation Examples](#implementation-examples)
4. [Google APIs Client Libraries](#google-apis-client-libraries)
5. [Error Codes and Handling Strategies](#error-codes-and-handling-strategies)
6. [Common Pitfalls and Solutions](#common-pitfalls-and-solutions)
7. [Actionable Implementation Guidelines](#actionable-implementation-guidelines)

## Google OAuth 2.0 Best Practices

### Official Documentation References

- **Web Server OAuth**: https://developers.google.com/identity/protocols/oauth2/web-server
- **Native/Desktop Apps**: https://developers.google.com/identity/protocols/oauth2/native-app
- **Error Handling**: https://developers.google.com/identity/protocols/oauth2/web-server#handlinganerrorresponse

### Core Refresh Token Principles

1. **Offline Access Requirement**
   - Set `access_type: 'offline'` to obtain refresh tokens
   - Refresh tokens allow API access when user is not present
   - Essential for server-side, installed, and device applications

2. **First-Time Only Token Issuance**
   - Refresh tokens are **only returned on first authorization**
   - If lost, user must repeat the full OAuth consent flow
   - Use `prompt: 'consent'` to force consent screen and ensure token delivery

3. **Secure Storage Requirements**
   - Store refresh tokens in persistent, encrypted database
   - Never log or expose refresh tokens in client-side code
   - Implement proper access controls for token storage

### Token Expiration Handling

```javascript
// Best practice: Handle token expiration gracefully
client.on('tokens', (tokens) => {
  if (tokens.refresh_token) {
    // Store securely in encrypted database
    await secureStorage.storeRefreshToken(tokens.refresh_token);
  }
  // Always update access token
  await secureStorage.storeAccessToken(tokens.access_token);
});
```

## Desktop Application OAuth Patterns

### PKCE Implementation (Required)

Desktop applications **must** implement Proof Key for Code Exchange (PKCE) for security:

```javascript
const crypto = require('crypto');

// Generate code verifier and challenge
const codeVerifier = crypto.randomBytes(128).toString('base64url');
const codeChallenge = crypto.createHash('sha256').update(codeVerifier).digest('base64url');

const authUrl = oauth2Client.generateAuthUrl({
  access_type: 'offline',
  scope: scopes,
  code_challenge: codeChallenge,
  code_challenge_method: 'S256',
  prompt: 'consent',
});
```

### Electron Security Considerations

1. **System Browser Integration**
   - Never use embedded browsers for OAuth
   - Always redirect to system browser for security
   - Use loopback IP addresses for redirect handling

2. **Loopback Redirect Pattern**

   ```javascript
   const redirectUri = 'http://127.0.0.1:0/oauth/callback';
   // Port 0 allows system to assign available port
   ```

3. **Token Storage Security**
   - Use OS keychain (keytar) for refresh token storage
   - Encrypt tokens with OS-level security
   - Implement token rotation service

### Network Error Handling Patterns

```javascript
async function handleTokenRefresh(retryCount = 0) {
  const MAX_RETRIES = 3;
  const RETRY_DELAY = Math.pow(2, retryCount) * 1000; // Exponential backoff

  try {
    const tokens = await oauth2Client.refreshAccessToken();
    return { success: true, tokens: tokens.credentials };
  } catch (error) {
    if (retryCount < MAX_RETRIES && isRetryableError(error)) {
      await delay(RETRY_DELAY);
      return handleTokenRefresh(retryCount + 1);
    }

    return { success: false, error, requiresReauth: isReauthError(error) };
  }
}

function isRetryableError(error) {
  return (
    error.code === 'NETWORK_ERROR' ||
    error.code === 'TIMEOUT' ||
    (error.status >= 500 && error.status < 600)
  );
}

function isReauthError(error) {
  return error.message.includes('invalid_grant') || error.message.includes('token_expired');
}
```

## Implementation Examples

### Complete OAuth Flow for Desktop Apps

```javascript
const { OAuth2Client } = require('google-auth-library');
const http = require('http');
const url = require('url');
const open = require('open');
const crypto = require('crypto');

class GmailOAuthManager {
  constructor(clientId, clientSecret) {
    this.oauth2Client = new OAuth2Client(
      clientId,
      clientSecret,
      'http://127.0.0.1:0/oauth/callback',
    );

    // Auto-refresh token handling
    this.oauth2Client.on('tokens', async (tokens) => {
      if (tokens.refresh_token) {
        await this.secureStorage.storeRefreshToken(tokens.refresh_token);
      }
      await this.secureStorage.storeAccessToken(tokens.access_token);
    });
  }

  async authenticate(scopes) {
    // Generate PKCE parameters
    const codeVerifier = crypto.randomBytes(128).toString('base64url');
    const codeChallenge = crypto.createHash('sha256').update(codeVerifier).digest('base64url');

    // Create temporary server for callback
    const server = http.createServer();
    const port = await this.getAvailablePort(server);

    this.oauth2Client.setCredentials({
      redirect_uris: [`http://127.0.0.1:${port}/oauth/callback`],
    });

    const authUrl = this.oauth2Client.generateAuthUrl({
      access_type: 'offline',
      scope: scopes,
      code_challenge: codeChallenge,
      code_challenge_method: 'S256',
      prompt: 'consent', // Force consent to ensure refresh token
      state: crypto.randomBytes(16).toString('hex'), // CSRF protection
    });

    return new Promise((resolve, reject) => {
      server.on('request', async (req, res) => {
        try {
          const parsedUrl = url.parse(req.url, true);

          if (parsedUrl.pathname === '/oauth/callback') {
            const { code, state, error } = parsedUrl.query;

            if (error) {
              throw new Error(`OAuth error: ${error}`);
            }

            // Exchange code for tokens
            const { tokens } = await this.oauth2Client.getToken({
              code,
              codeVerifier,
            });

            this.oauth2Client.setCredentials(tokens);

            res.writeHead(200, { 'Content-Type': 'text/html' });
            res.end('<h1>Authentication successful! You can close this window.</h1>');

            server.close();
            resolve(tokens);
          }
        } catch (err) {
          res.writeHead(400, { 'Content-Type': 'text/html' });
          res.end('<h1>Authentication failed. Please try again.</h1>');
          server.close();
          reject(err);
        }
      });

      server.listen(port, '127.0.0.1', () => {
        open(authUrl);
      });

      // Timeout after 5 minutes
      setTimeout(() => {
        server.close();
        reject(new Error('Authentication timeout'));
      }, 300000);
    });
  }

  async refreshTokenIfNeeded() {
    try {
      const { credentials } = await this.oauth2Client.refreshAccessToken();
      return { success: true, credentials };
    } catch (error) {
      if (error.message.includes('invalid_grant')) {
        // Refresh token is invalid, need full re-authentication
        return { success: false, requiresReauth: true, error };
      }
      return { success: false, requiresReauth: false, error };
    }
  }

  async getAvailablePort(server) {
    return new Promise((resolve) => {
      server.listen(0, '127.0.0.1', () => {
        const port = server.address().port;
        server.close(() => resolve(port));
      });
    });
  }
}
```

### Retry and Backoff Strategy

```javascript
class RetryableGmailClient {
  constructor(oauth2Client) {
    this.oauth2Client = oauth2Client;
    this.rateLimitDelay = 0;
  }

  async makeRequest(requestFn, options = {}) {
    const { maxRetries = 3, baseDelay = 1000, maxDelay = 30000, backoffMultiplier = 2 } = options;

    for (let attempt = 0; attempt <= maxRetries; attempt++) {
      try {
        // Apply rate limiting delay
        if (this.rateLimitDelay > 0) {
          await this.delay(this.rateLimitDelay);
          this.rateLimitDelay = 0;
        }

        return await requestFn();
      } catch (error) {
        const isLastAttempt = attempt === maxRetries;

        if (this.isRateLimitError(error)) {
          // Extract retry-after header if available
          const retryAfter = error.response?.headers?.['retry-after'];
          this.rateLimitDelay = retryAfter ? parseInt(retryAfter) * 1000 : baseDelay;

          if (!isLastAttempt) {
            await this.delay(this.rateLimitDelay);
            continue;
          }
        }

        if (this.isRetryableError(error) && !isLastAttempt) {
          const delay = Math.min(baseDelay * Math.pow(backoffMultiplier, attempt), maxDelay);
          await this.delay(delay);
          continue;
        }

        if (this.isTokenError(error)) {
          const refreshResult = await this.refreshTokenIfNeeded();
          if (refreshResult.success && !isLastAttempt) {
            continue; // Retry with new token
          }
          if (refreshResult.requiresReauth) {
            throw new AuthenticationRequiredError(
              'Token refresh failed, re-authentication required',
            );
          }
        }

        throw error;
      }
    }
  }

  isRateLimitError(error) {
    return error.code === 429 || (error.message && error.message.includes('Rate Limit Exceeded'));
  }

  isRetryableError(error) {
    return error.code >= 500 || error.code === 'NETWORK_ERROR' || error.code === 'ECONNRESET';
  }

  isTokenError(error) {
    return error.code === 401 || (error.message && error.message.includes('Invalid Credentials'));
  }

  delay(ms) {
    return new Promise((resolve) => setTimeout(resolve, ms));
  }
}
```

## Google APIs Client Libraries

### Node.js Google Auth Library Patterns

**Reference**: https://github.com/googleapis/google-auth-library-nodejs

#### Essential Configuration

```javascript
const { google } = require('googleapis');
const { OAuth2Client } = require('google-auth-library');

// Initialize OAuth2 client
const oauth2Client = new OAuth2Client(
  process.env.GOOGLE_CLIENT_ID,
  process.env.GOOGLE_CLIENT_SECRET,
  'http://127.0.0.1:3000/oauth/callback',
);

// Set up automatic token refresh
oauth2Client.on('tokens', (tokens) => {
  if (tokens.refresh_token) {
    // Store the refresh_token securely
    secureStorage.store('refresh_token', tokens.refresh_token);
  }
  // Always store access token
  secureStorage.store('access_token', tokens.access_token);
});

// Initialize Gmail API
const gmail = google.gmail({ version: 'v1', auth: oauth2Client });
```

#### Automatic Token Management

```javascript
// Load stored credentials
async function initializeAuth() {
  const refreshToken = await secureStorage.get('refresh_token');
  const accessToken = await secureStorage.get('access_token');

  if (refreshToken) {
    oauth2Client.setCredentials({
      refresh_token: refreshToken,
      access_token: accessToken,
    });

    // Library will automatically refresh when needed
    return true;
  }

  return false; // Need fresh authentication
}

// Use with automatic refresh
async function listMessages() {
  try {
    const response = await gmail.users.messages.list({
      userId: 'me',
      maxResults: 10,
    });
    return response.data;
  } catch (error) {
    if (error.code === 401) {
      // Token refresh failed, need re-authentication
      throw new AuthenticationRequiredError();
    }
    throw error;
  }
}
```

## Error Codes and Handling Strategies

### Standard OAuth 2.0 Error Codes

| Error Code              | Description                    | Handling Strategy                      |
| ----------------------- | ------------------------------ | -------------------------------------- |
| `invalid_grant`         | Token expired or revoked       | Force re-authentication                |
| `invalid_client`        | Client credentials incorrect   | Check client ID/secret configuration   |
| `redirect_uri_mismatch` | Redirect URI not authorized    | Update OAuth consent screen settings   |
| `access_denied`         | User denied authorization      | Handle gracefully, allow retry         |
| `admin_policy_enforced` | Workspace admin blocked access | Contact admin or use different account |
| `disallowed_useragent`  | Using embedded browser         | Switch to system browser               |

### Gmail API Specific Error Codes

```javascript
class GmailErrorHandler {
  static handleApiError(error) {
    switch (error.code) {
      case 400:
        if (error.message.includes('Invalid query')) {
          return { type: 'INVALID_QUERY', message: 'Search query syntax error' };
        }
        return { type: 'BAD_REQUEST', message: 'Request format error' };

      case 401:
        return { type: 'AUTHENTICATION_REQUIRED', message: 'Token expired or invalid' };

      case 403:
        if (error.message.includes('Rate Limit Exceeded')) {
          return {
            type: 'RATE_LIMIT',
            message: 'API quota exceeded',
            retryAfter: error.retryAfter,
          };
        }
        return { type: 'FORBIDDEN', message: 'Insufficient permissions' };

      case 404:
        return { type: 'NOT_FOUND', message: 'Resource not found' };

      case 429:
        return { type: 'RATE_LIMIT', message: 'Too many requests', retryAfter: error.retryAfter };

      case 500:
      case 502:
      case 503:
        return {
          type: 'SERVER_ERROR',
          message: 'Gmail service temporarily unavailable',
          retryable: true,
        };

      default:
        return { type: 'UNKNOWN_ERROR', message: error.message };
    }
  }

  static async handleErrorWithRetry(error, retryFn, maxRetries = 3) {
    const errorInfo = this.handleApiError(error);

    switch (errorInfo.type) {
      case 'RATE_LIMIT':
        if (errorInfo.retryAfter) {
          await this.delay(errorInfo.retryAfter * 1000);
        } else {
          await this.delay(Math.random() * 5000 + 1000); // Random jitter
        }
        return retryFn();

      case 'SERVER_ERROR':
        if (maxRetries > 0) {
          await this.delay(Math.pow(2, 4 - maxRetries) * 1000);
          return this.handleErrorWithRetry(error, retryFn, maxRetries - 1);
        }
        throw new Error('Gmail service unavailable after retries');

      case 'AUTHENTICATION_REQUIRED':
        throw new AuthenticationRequiredError();

      default:
        throw error;
    }
  }

  static delay(ms) {
    return new Promise((resolve) => setTimeout(resolve, ms));
  }
}
```

## Common Pitfalls and Solutions

### 1. Refresh Token Not Received

**Problem**: OAuth flow completes but no refresh token is provided.

**Root Causes**:

- Missing `access_type: 'offline'` parameter
- User previously authorized app (refresh token only provided once)
- Not forcing consent screen

**Solutions**:

```javascript
// ✅ Correct implementation
const authUrl = oauth2Client.generateAuthUrl({
  access_type: 'offline', // Required for refresh token
  prompt: 'consent', // Force consent screen
  scope: scopes,
  state: randomState,
});

// ❌ Common mistake
const authUrl = oauth2Client.generateAuthUrl({
  scope: scopes, // Missing access_type and prompt
});
```

### 2. Token Storage Security Issues

**Problem**: Refresh tokens stored insecurely or logged accidentally.

**Solutions**:

```javascript
// ✅ Secure storage
const keytar = require('keytar');

class SecureTokenStorage {
  async storeRefreshToken(token) {
    await keytar.setPassword('gmail-app', 'refresh_token', token);
  }

  async getRefreshToken() {
    return await keytar.getPassword('gmail-app', 'refresh_token');
  }

  async clearTokens() {
    await keytar.deletePassword('gmail-app', 'refresh_token');
    await keytar.deletePassword('gmail-app', 'access_token');
  }
}

// ❌ Insecure storage
localStorage.setItem('refresh_token', token); // Never do this
console.log('Token:', token); // Never log tokens
```

### 3. Rate Limiting Not Handled

**Problem**: API calls fail with 429/403 rate limit errors.

**Solutions**:

```javascript
// ✅ Proper rate limiting
class RateLimitedClient {
  constructor() {
    this.requestQueue = [];
    this.isProcessing = false;
    this.lastRequestTime = 0;
    this.minRequestInterval = 100; // 100ms between requests
  }

  async makeRequest(requestFn) {
    return new Promise((resolve, reject) => {
      this.requestQueue.push({ requestFn, resolve, reject });
      this.processQueue();
    });
  }

  async processQueue() {
    if (this.isProcessing || this.requestQueue.length === 0) return;

    this.isProcessing = true;

    while (this.requestQueue.length > 0) {
      const { requestFn, resolve, reject } = this.requestQueue.shift();

      // Ensure minimum time between requests
      const timeSinceLastRequest = Date.now() - this.lastRequestTime;
      if (timeSinceLastRequest < this.minRequestInterval) {
        await this.delay(this.minRequestInterval - timeSinceLastRequest);
      }

      try {
        const result = await requestFn();
        resolve(result);
      } catch (error) {
        if (error.code === 429) {
          // Re-queue request with backoff
          this.requestQueue.unshift({ requestFn, resolve, reject });
          await this.delay(Math.random() * 5000 + 2000);
          continue;
        }
        reject(error);
      }

      this.lastRequestTime = Date.now();
    }

    this.isProcessing = false;
  }

  delay(ms) {
    return new Promise((resolve) => setTimeout(resolve, ms));
  }
}
```

### 4. Network Connectivity Issues

**Problem**: App fails when network is unstable or offline.

**Solutions**:

```javascript
// ✅ Graceful offline handling
class OfflineCapableClient {
  constructor() {
    this.isOnline = navigator.onLine;
    this.pendingOperations = [];

    window.addEventListener('online', () => {
      this.isOnline = true;
      this.processPendingOperations();
    });

    window.addEventListener('offline', () => {
      this.isOnline = false;
    });
  }

  async makeRequest(requestFn, options = {}) {
    if (!this.isOnline && options.allowOffline) {
      return this.queueOperation(requestFn, options);
    }

    try {
      return await this.executeWithRetry(requestFn);
    } catch (error) {
      if (this.isNetworkError(error) && options.allowOffline) {
        return this.queueOperation(requestFn, options);
      }
      throw error;
    }
  }

  queueOperation(requestFn, options) {
    return new Promise((resolve, reject) => {
      this.pendingOperations.push({
        requestFn,
        resolve,
        reject,
        options,
        timestamp: Date.now(),
      });
    });
  }

  async processPendingOperations() {
    while (this.pendingOperations.length > 0 && this.isOnline) {
      const operation = this.pendingOperations.shift();
      try {
        const result = await this.executeWithRetry(operation.requestFn);
        operation.resolve(result);
      } catch (error) {
        operation.reject(error);
      }
    }
  }

  isNetworkError(error) {
    return (
      error.code === 'NETWORK_ERROR' ||
      error.code === 'ENOTFOUND' ||
      error.code === 'ECONNREFUSED' ||
      error.message.includes('Network request failed')
    );
  }
}
```

## Actionable Implementation Guidelines

### Phase 1: Basic OAuth Setup

1. **Configure OAuth Credentials**

   ```bash
   # Google Cloud Console setup
   1. Enable Gmail API
   2. Create OAuth 2.0 credentials
   3. Add http://127.0.0.1:* to authorized redirect URIs
   4. Download credentials JSON
   ```

2. **Install Required Dependencies**

   ```bash
   npm install google-auth-library googleapis keytar
   npm install --save-dev @types/keytar
   ```

3. **Implement Basic Auth Flow**
   ```javascript
   // Minimum viable implementation
   const authUrl = oauth2Client.generateAuthUrl({
     access_type: 'offline',
     scope: ['https://www.googleapis.com/auth/gmail.readonly'],
     prompt: 'consent',
   });
   ```

### Phase 2: Secure Token Management

1. **Implement Secure Storage**

   ```javascript
   // Use OS keychain for production
   const keytar = require('keytar');
   const SERVICE_NAME = 'your-app-name';

   await keytar.setPassword(SERVICE_NAME, 'refresh_token', token);
   ```

2. **Add Token Refresh Logic**
   ```javascript
   oauth2Client.on('tokens', async (tokens) => {
     if (tokens.refresh_token) {
       await secureStorage.store('refresh_token', tokens.refresh_token);
     }
   });
   ```

### Phase 3: Error Handling & Resilience

1. **Implement Retry Logic**
   - Exponential backoff for server errors
   - Rate limit handling with jitter
   - Network error recovery

2. **Add User Experience Improvements**
   - Progress indicators for long operations
   - Graceful offline mode
   - Clear error messages

### Phase 4: Production Hardening

1. **Security Audit**
   - Review token storage implementation
   - Ensure no tokens in logs
   - Validate PKCE implementation

2. **Performance Optimization**
   - Implement request batching
   - Add response caching
   - Monitor API quota usage

### Testing Checklist

- [ ] Refresh token received on first auth
- [ ] Token refresh works automatically
- [ ] Rate limiting handled gracefully
- [ ] Network errors don't crash app
- [ ] Tokens stored securely (OS keychain)
- [ ] No tokens in application logs
- [ ] PKCE implemented correctly
- [ ] Offline mode works (if applicable)
- [ ] Error messages are user-friendly
- [ ] App recovers from expired tokens

### Monitoring and Maintenance

1. **Log Important Events** (without exposing tokens)

   ```javascript
   logger.info('OAuth flow initiated');
   logger.info('Refresh token obtained successfully');
   logger.warn('Token refresh failed, re-authentication required');
   logger.error('Rate limit exceeded, backing off');
   ```

2. **Track API Usage**
   - Monitor quota consumption
   - Track error rates
   - Measure response times

3. **User Experience Metrics**
   - Authentication success rate
   - Time to complete OAuth flow
   - Frequency of re-authentication requests

This documentation provides a comprehensive foundation for implementing robust Gmail OAuth refresh token handling in desktop applications, with specific focus on security, reliability, and user experience.
