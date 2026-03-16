# Email Provider Implementation Patterns

This document provides specific implementation patterns for email providers, based on successful open-source libraries and industry best practices.

## Gmail Provider Architecture

### OAuth 2.0 Authentication Pattern

Based on the official Google APIs Node.js client and successful email libraries:

```typescript
interface GmailAuthConfig {
  readonly clientId: string;
  readonly clientSecret: string;
  readonly redirectUri: string;
  readonly scopes: readonly string[];
}

interface GmailTokens {
  readonly accessToken: string;
  readonly refreshToken?: string;
  readonly expiryDate: number;
}

class GmailProvider implements EmailProvider {
  private auth: OAuth2Client;
  private gmail: gmail_v1.Gmail;

  constructor(private config: GmailAuthConfig) {
    this.auth = new OAuth2Client(config.clientId, config.clientSecret, config.redirectUri);
  }

  async connect(tokens: GmailTokens): Promise<ConnectionResult> {
    try {
      this.auth.setCredentials({
        access_token: tokens.accessToken,
        refresh_token: tokens.refreshToken,
        expiry_date: tokens.expiryDate,
      });

      this.gmail = google.gmail({ version: 'v1', auth: this.auth });

      // Test connection with a simple API call
      await this.gmail.users.getProfile({ userId: 'me' });

      return { success: true, accountInfo: await this.getAccountInfo() };
    } catch (error) {
      return {
        success: false,
        error: this.mapGoogleError(error),
      };
    }
  }
}
```

### Email Listing with Pagination

```typescript
interface ListEmailsOptions {
  readonly query?: string; // Gmail search query
  readonly maxResults?: number; // Batch size (max 500)
  readonly pageToken?: string; // For pagination
  readonly labelIds?: string[]; // Filter by labels
}

interface EmailSummary {
  readonly id: string;
  readonly threadId: string;
  readonly labelIds: string[];
  readonly snippet: string;
  readonly historyId: string;
  readonly internalDate: number;
  readonly sizeEstimate: number;
}

interface ListEmailsResult {
  readonly emails: EmailSummary[];
  readonly nextPageToken?: string;
  readonly resultSizeEstimate: number;
}

class GmailProvider implements EmailProvider {
  async list(options: ListEmailsOptions = {}): Promise<Result<ListEmailsResult>> {
    try {
      const response = await this.gmail.users.messages.list({
        userId: 'me',
        q: options.query,
        maxResults: options.maxResults || 100,
        pageToken: options.pageToken,
        labelIds: options.labelIds,
      });

      const emails =
        response.data.messages?.map((msg) => ({
          id: msg.id!,
          threadId: msg.threadId!,
          labelIds: [], // Will be populated in batch get
          snippet: '',
          historyId: '',
          internalDate: 0,
          sizeEstimate: 0,
        })) || [];

      return {
        success: true,
        data: {
          emails,
          nextPageToken: response.data.nextPageToken,
          resultSizeEstimate: response.data.resultSizeEstimate || 0,
        },
      };
    } catch (error) {
      return { success: false, error: this.mapGoogleError(error) };
    }
  }
}
```

### Batch Operations Pattern

```typescript
interface BatchModifyRequest {
  readonly emailIds: string[];
  readonly addLabelIds?: string[];
  readonly removeLabelIds?: string[];
}

interface BatchDeleteRequest {
  readonly emailIds: string[];
  readonly permanent?: boolean;
}

class GmailProvider implements EmailProvider {
  async batchModify(request: BatchModifyRequest): Promise<Result<void>> {
    try {
      // Gmail API supports batch modify for up to 1000 messages
      const BATCH_SIZE = 1000;
      const batches = this.chunkArray(request.emailIds, BATCH_SIZE);

      for (const batch of batches) {
        await this.gmail.users.messages.batchModify({
          userId: 'me',
          requestBody: {
            ids: batch,
            addLabelIds: request.addLabelIds,
            removeLabelIds: request.removeLabelIds,
          },
        });
      }

      return { success: true };
    } catch (error) {
      return { success: false, error: this.mapGoogleError(error) };
    }
  }

  async batchDelete(request: BatchDeleteRequest): Promise<Result<void>> {
    try {
      if (request.permanent) {
        // Permanent delete (use with caution)
        await this.gmail.users.messages.batchDelete({
          userId: 'me',
          requestBody: { ids: request.emailIds },
        });
      } else {
        // Move to trash (recoverable)
        await this.batchModify({
          emailIds: request.emailIds,
          addLabelIds: ['TRASH'],
        });
      }

      return { success: true };
    } catch (error) {
      return { success: false, error: this.mapGoogleError(error) };
    }
  }
}
```

## Error Handling Patterns

### Google API Error Mapping

```typescript
interface GmailError extends ProviderError {
  readonly googleCode?: number;
  readonly googleReason?: string;
}

class GmailProvider implements EmailProvider {
  private mapGoogleError(error: any): GmailError {
    const baseError = {
      timestamp: new Date(),
      details: { originalError: error.message },
    };

    if (error.code === 401) {
      return {
        ...baseError,
        code: 'GMAIL_AUTH_EXPIRED',
        message: 'Gmail authentication expired. Please reconnect.',
        retryable: false,
        googleCode: error.code,
      };
    }

    if (error.code === 403) {
      return {
        ...baseError,
        code: 'GMAIL_QUOTA_EXCEEDED',
        message: 'Gmail API quota exceeded. Please try again later.',
        retryable: true,
        googleCode: error.code,
      };
    }

    if (error.code === 429) {
      return {
        ...baseError,
        code: 'GMAIL_RATE_LIMITED',
        message: 'Gmail API rate limit exceeded. Retrying with backoff.',
        retryable: true,
        googleCode: error.code,
      };
    }

    if (error.code >= 500) {
      return {
        ...baseError,
        code: 'GMAIL_SERVER_ERROR',
        message: 'Gmail server error. Please try again.',
        retryable: true,
        googleCode: error.code,
      };
    }

    return {
      ...baseError,
      code: 'GMAIL_UNKNOWN_ERROR',
      message: error.message || 'Unknown Gmail API error',
      retryable: false,
      googleCode: error.code,
    };
  }
}
```

### Retry Logic with Exponential Backoff

```typescript
class GmailProvider implements EmailProvider {
  private async retryWithBackoff<T>(operation: () => Promise<T>, maxRetries = 3): Promise<T> {
    let lastError: Error;

    for (let attempt = 0; attempt <= maxRetries; attempt++) {
      try {
        return await operation();
      } catch (error) {
        lastError = error;

        // Don't retry on certain errors
        if (error.code === 401 || error.code === 400) {
          throw error;
        }

        // Don't retry on last attempt
        if (attempt === maxRetries) {
          throw error;
        }

        // Exponential backoff: 1s, 2s, 4s, 8s
        const delayMs = Math.pow(2, attempt) * 1000;
        await this.delay(delayMs);
      }
    }

    throw lastError!;
  }

  private delay(ms: number): Promise<void> {
    return new Promise((resolve) => setTimeout(resolve, ms));
  }
}
```

## Storage Provider Patterns

### SQLite Implementation

```typescript
interface StorageProvider {
  init(): Promise<void>;
  getUserRules(): Promise<UserRules>;
  updateUserRules(rules: UserRules): Promise<void>;
  getEmailMetadata(emailId: string): Promise<EmailMetadata | null>;
  setEmailMetadata(emailId: string, metadata: EmailMetadata): Promise<void>;
}

class SQLiteStorageProvider implements StorageProvider {
  private db: Database;

  constructor(private dbPath: string) {
    this.db = new Database(dbPath);
  }

  async init(): Promise<void> {
    // Create tables with proper indexes
    this.db.exec(`
      CREATE TABLE IF NOT EXISTS user_rules (
        id INTEGER PRIMARY KEY,
        rule_type TEXT NOT NULL,
        rule_key TEXT NOT NULL,
        rule_value TEXT NOT NULL,
        created_at TEXT NOT NULL,
        updated_at TEXT NOT NULL
      );
      
      CREATE INDEX IF NOT EXISTS idx_user_rules_type_key 
      ON user_rules(rule_type, rule_key);
      
      CREATE TABLE IF NOT EXISTS email_metadata (
        email_id TEXT PRIMARY KEY,
        classification TEXT,
        confidence REAL,
        reasons TEXT,
        bulk_key TEXT,
        last_classified TEXT,
        user_action TEXT,
        user_action_timestamp TEXT
      );
      
      CREATE INDEX IF NOT EXISTS idx_email_classification 
      ON email_metadata(classification);
      
      CREATE INDEX IF NOT EXISTS idx_email_bulk_key 
      ON email_metadata(bulk_key);
    `);
  }

  async getUserRules(): Promise<UserRules> {
    const stmt = this.db.prepare(`
      SELECT rule_type, rule_key, rule_value 
      FROM user_rules 
      ORDER BY updated_at DESC
    `);

    const rows = stmt.all();

    const rules: UserRules = {
      alwaysKeep: { senders: [], domains: [], listIds: [] },
      autoTrash: { senders: [], domains: [], listIds: [] },
    };

    for (const row of rows) {
      const { rule_type, rule_key, rule_value } = row as any;

      if (rule_type === 'always_keep' && rule_key === 'sender') {
        rules.alwaysKeep.senders.push(rule_value);
      } else if (rule_type === 'always_keep' && rule_key === 'domain') {
        rules.alwaysKeep.domains.push(rule_value);
      }
      // ... handle other rule types
    }

    return rules;
  }
}
```

### IndexedDB Implementation (Browser)

```typescript
class IndexedDBStorageProvider implements StorageProvider {
  private dbName = 'trashmail-panda';
  private version = 1;
  private db: IDBDatabase | null = null;

  async init(): Promise<void> {
    return new Promise((resolve, reject) => {
      const request = indexedDB.open(this.dbName, this.version);

      request.onerror = () => reject(request.error);
      request.onsuccess = () => {
        this.db = request.result;
        resolve();
      };

      request.onupgradeneeded = (event) => {
        const db = (event.target as IDBOpenDBRequest).result;

        // Create user_rules object store
        if (!db.objectStoreNames.contains('user_rules')) {
          const rulesStore = db.createObjectStore('user_rules', {
            keyPath: 'id',
            autoIncrement: true,
          });
          rulesStore.createIndex('type_key', ['rule_type', 'rule_key']);
        }

        // Create email_metadata object store
        if (!db.objectStoreNames.contains('email_metadata')) {
          const metadataStore = db.createObjectStore('email_metadata', {
            keyPath: 'email_id',
          });
          metadataStore.createIndex('classification', 'classification');
          metadataStore.createIndex('bulk_key', 'bulk_key');
        }
      };
    });
  }

  async getUserRules(): Promise<UserRules> {
    if (!this.db) throw new Error('Database not initialized');

    return new Promise((resolve, reject) => {
      const transaction = this.db!.transaction(['user_rules'], 'readonly');
      const store = transaction.objectStore('user_rules');
      const request = store.getAll();

      request.onerror = () => reject(request.error);
      request.onsuccess = () => {
        const rows = request.result;
        const rules = this.buildUserRulesFromRows(rows);
        resolve(rules);
      };
    });
  }
}
```

## LLM Provider Patterns

### OpenAI Provider Implementation

```typescript
interface LLMProvider {
  classifyEmails(input: ClassifyInput): Promise<Result<ClassifyOutput>>;
  suggestSearchQueries(context: QueryContext): Promise<Result<string[]>>;
}

class OpenAIProvider implements LLMProvider {
  private client: OpenAI;

  constructor(config: OpenAIConfig) {
    this.client = new OpenAI({
      apiKey: config.apiKey,
      organization: config.organization,
    });
  }

  async classifyEmails(input: ClassifyInput): Promise<Result<ClassifyOutput>> {
    try {
      const prompt = this.buildClassificationPrompt(input);

      const response = await this.client.chat.completions.create({
        model: 'gpt-4o-mini',
        messages: [
          { role: 'system', content: CLASSIFICATION_SYSTEM_PROMPT },
          { role: 'user', content: prompt },
        ],
        temperature: 0.1,
        response_format: { type: 'json_object' },
      });

      const content = response.choices[0]?.message?.content;
      if (!content) {
        return {
          success: false,
          error: {
            code: 'OPENAI_EMPTY_RESPONSE',
            message: 'OpenAI returned empty response',
            retryable: true,
            timestamp: new Date(),
          },
        };
      }

      const result = JSON.parse(content) as ClassifyOutput;
      return { success: true, data: result };
    } catch (error) {
      return { success: false, error: this.mapOpenAIError(error) };
    }
  }

  private buildClassificationPrompt(input: ClassifyInput): string {
    return `
Classify the following ${input.emails.length} emails according to these categories:
- keep: Important emails to preserve
- newsletter: Subscription-based content
- promotion: Marketing and sales emails  
- spam: Unwanted or suspicious emails
- dangerous_phishing: Security threats

User Rules:
${JSON.stringify(input.userRulesSnapshot, null, 2)}

Emails to classify:
${input.emails
  .map(
    (email, i) => `
Email ${i + 1}:
From: ${email.headers.from}
To: ${email.headers.to}
Subject: ${email.headers.subject}
List-Unsubscribe: ${email.headers['list-unsubscribe'] || 'none'}
Body preview: ${email.bodyText?.substring(0, 500) || 'No text body'}
`,
  )
  .join('\n')}

Return a JSON object with this exact structure:
{
  "items": [
    {
      "emailId": "string",
      "classification": "keep|newsletter|promotion|spam|dangerous_phishing",
      "likelihood": "very likely|likely|unsure",
      "confidence": 0.0-1.0,
      "reasons": ["reason1", "reason2"],
      "bulk_key": "string for grouping similar emails",
      "unsubscribe_method": {"type": "http_link|mailto|none", "value": "url or email"}
    }
  ],
  "rulesSuggestions": [
    {"type": "always_keep_sender", "value": "email@domain.com", "rationale": "explanation"}
  ]
}
`;
  }
}
```

## Key Implementation Guidelines

### 1. Always Use Result Pattern

Never throw exceptions from provider methods. Always return Result types for consistent error handling.

### 2. Implement Retry Logic

All providers should implement exponential backoff retry logic for transient failures.

### 3. Use Batch Operations

Optimize API usage by implementing batch operations wherever possible.

### 4. Handle Rate Limits

Implement proper rate limiting and backoff strategies for all external API calls.

### 5. Encrypt Sensitive Data

Always encrypt API keys and tokens when storing locally.

### 6. Validate Inputs

Use runtime validation (like Zod schemas) at provider boundaries.

### 7. Map Provider Errors

Create consistent error interfaces that abstract provider-specific error details.

These patterns ensure robust, scalable, and maintainable provider implementations for the TrashMail Panda project.
