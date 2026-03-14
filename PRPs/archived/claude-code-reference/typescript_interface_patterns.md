# TypeScript Interface Patterns for Provider Abstractions

This document provides specific patterns and examples for implementing high-quality TypeScript interfaces for provider-agnostic abstractions in the TrashMail Panda project.

## Core Interface Design Principles

### 1. Interface-First Design Pattern

Always define abstract interfaces before implementations:

```typescript
// ✅ Good: Abstract interface defines contract
interface EmailProvider {
  readonly name: string;
  readonly version: string;
  connect(): Promise<ConnectionResult>;
  send(message: EmailMessage): Promise<EmailResult>;
}

// ✅ Good: Concrete implementation
class GmailProvider implements EmailProvider {
  readonly name = 'gmail';
  readonly version = '1.0.0';
  // ... implementation
}
```

### 2. Result Pattern for Error Handling

Never throw exceptions from provider interfaces - use Result types:

```typescript
// ✅ Good: Result pattern prevents runtime exceptions
type EmailResult =
  | { success: true; messageId: string; timestamp: Date }
  | { success: false; error: ProviderError };

interface ProviderError {
  readonly code: string;
  readonly message: string;
  readonly retryable: boolean;
  readonly details?: Record<string, unknown>;
}
```

### 3. Configuration Pattern

Separate configuration from operations:

```typescript
// ✅ Good: Immutable configuration interfaces
interface EmailProviderConfig {
  readonly apiKey: string;
  readonly region?: string;
  readonly timeout?: number;
  readonly retries?: number;
}

interface EmailProvider {
  configure(config: EmailProviderConfig): Promise<void>;
  getConfig(): Readonly<EmailProviderConfig>;
}
```

### 4. Generic Constraint Pattern

Use generic constraints for type safety:

```typescript
// ✅ Good: Generic interfaces with proper constraints
interface Repository<T, K = string> {
  findById(id: K): Promise<T | null>;
  create(entity: Omit<T, 'id'>): Promise<T>;
  update(id: K, entity: Partial<T>): Promise<T>;
}

// ✅ Good: Constrained generic providers
interface ConfigurableProvider<TConfig = Record<string, unknown>> {
  configure(config: TConfig): Promise<void>;
  validate(config: TConfig): ValidationResult;
}
```

### 5. Builder Pattern for Complex Objects

Use builders for complex message/request construction:

```typescript
// ✅ Good: Builder pattern with fluent interface
class EmailMessageBuilder {
  private message: Partial<EmailMessage> = {};

  to(recipients: string | string[]): this {
    this.message.to = Array.isArray(recipients) ? recipients : [recipients];
    return this;
  }

  subject(subject: string): this {
    this.message.subject = subject;
    return this;
  }

  body(content: EmailBody): this {
    this.message.body = content;
    return this;
  }

  build(): EmailMessage {
    this.validateRequired();
    return this.message as EmailMessage;
  }
}
```

## Provider Registration Pattern

### Factory Pattern Implementation

```typescript
// ✅ Good: Abstract factory for provider instantiation
interface ProviderFactory<TProvider, TConfig = unknown> {
  readonly name: string;
  readonly version: string;
  validateConfig(config: TConfig): ValidationResult;
  create(config: TConfig): Promise<TProvider>;
}

// ✅ Good: Provider registry for runtime switching
class ProviderRegistry<T extends BaseProvider> {
  private factories = new Map<string, ProviderFactory<T>>();

  register<C>(name: string, factory: ProviderFactory<T, C>): void {
    this.factories.set(name, factory);
  }

  async create<C>(name: string, config: C): Promise<T> {
    const factory = this.factories.get(name);
    if (!factory) {
      throw new Error(`Provider ${name} not registered`);
    }
    return factory.create(config);
  }

  listAvailable(): string[] {
    return Array.from(this.factories.keys());
  }
}
```

## Async Operation Patterns

### 1. Timeout and Cancellation Support

```typescript
interface ProviderOptions {
  timeout?: number;
  signal?: AbortSignal;
  retries?: number;
}

interface AsyncProvider {
  operation(data: unknown, options?: ProviderOptions): Promise<Result>;
}

// Helper function for timeout wrapper
function withTimeout<T>(promise: Promise<T>, timeoutMs: number): Promise<T> {
  const timeoutPromise = new Promise<never>((_, reject) =>
    setTimeout(() => reject(new Error('Operation timed out')), timeoutMs),
  );
  return Promise.race([promise, timeoutPromise]);
}
```

### 2. Batch Operation Pattern

```typescript
interface BatchResult<T> {
  successful: T[];
  failed: Array<{ index: number; error: ProviderError }>;
  totalProcessed: number;
}

interface BatchProvider<TInput, TOutput> {
  processBatch(items: TInput[], options?: BatchOptions): Promise<BatchResult<TOutput>>;
}

interface BatchOptions {
  batchSize?: number;
  parallelism?: number;
  continueOnError?: boolean;
}
```

## Documentation Patterns

### JSDoc Standards

````typescript
/**
 * Email provider interface for sending transactional and marketing emails
 *
 * @example
 * ```typescript
 * const provider = new GmailProvider({ apiKey: 'your-key' });
 * const result = await provider.send({
 *   to: 'user@example.com',
 *   from: 'app@company.com',
 *   subject: 'Welcome',
 *   body: { text: 'Welcome!' }
 * });
 *
 * if (result.success) {
 *   console.log('Sent:', result.messageId);
 * } else {
 *   console.error('Failed:', result.error.message);
 * }
 * ```
 */
interface EmailProvider {
  /**
   * Sends an email message
   *
   * @param message - The email message to send
   * @param options - Optional sending configuration
   * @returns Promise resolving to send result (never rejects)
   *
   * @remarks
   * This method never throws exceptions. All errors are returned
   * in the result object for consistent error handling.
   */
  send(message: EmailMessage, options?: SendOptions): Promise<EmailResult>;
}
````

## Type Safety Patterns

### 1. Branded Types for IDs

```typescript
// ✅ Good: Branded types prevent ID mixing
type EmailId = string & { readonly __brand: 'EmailId' };
type UserId = string & { readonly __brand: 'UserId' };

function createEmailId(id: string): EmailId {
  return id as EmailId;
}

function createUserId(id: string): UserId {
  return id as UserId;
}
```

### 2. Union Types vs Enums

```typescript
// ✅ Good: String literal unions for simple cases
type EmailStatus = 'pending' | 'sent' | 'delivered' | 'failed' | 'bounced';
type Priority = 'low' | 'normal' | 'high' | 'urgent';

// ✅ Good: Const assertions for reusable values
const EMAIL_STATUSES = ['pending', 'sent', 'delivered', 'failed', 'bounced'] as const;
type EmailStatus = (typeof EMAIL_STATUSES)[number];

// ✅ Good: Enums for complex cases with methods
enum ProviderType {
  EMAIL = 'email',
  STORAGE = 'storage',
  NOTIFICATION = 'notification',
}
```

### 3. Utility Types for Flexibility

```typescript
// ✅ Good: Use utility types for variations
interface BaseMessage {
  id: string;
  timestamp: Date;
  metadata: Record<string, unknown>;
}

interface CreateMessageRequest extends Omit<BaseMessage, 'id' | 'timestamp'> {
  content: string;
}

interface UpdateMessageRequest extends Partial<Pick<BaseMessage, 'metadata'>> {
  id: string;
  content?: string;
}
```

## Validation Patterns

### 1. Runtime Type Guards

```typescript
// ✅ Good: Type guards for runtime validation
function isEmailProvider(provider: unknown): provider is EmailProvider {
  return (
    typeof provider === 'object' &&
    provider !== null &&
    'name' in provider &&
    'send' in provider &&
    typeof (provider as any).send === 'function'
  );
}

// ✅ Good: Use type guards in factories
function createProvider(config: ProviderConfig): EmailProvider {
  const provider = createProviderInternal(config);

  if (!isEmailProvider(provider)) {
    throw new Error('Invalid provider implementation');
  }

  return provider;
}
```

### 2. Schema Validation with Zod

```typescript
import { z } from 'zod';

// ✅ Good: Zod schemas for runtime validation
const EmailMessageSchema = z.object({
  to: z.union([z.string().email(), z.array(z.string().email())]),
  from: z.string().email(),
  subject: z.string().min(1),
  body: z
    .object({
      text: z.string().optional(),
      html: z.string().optional(),
    })
    .refine((data) => data.text || data.html, {
      message: 'Either text or html body is required',
    }),
});

type EmailMessage = z.infer<typeof EmailMessageSchema>;

// ✅ Good: Validate at boundaries
function validateEmailMessage(data: unknown): EmailMessage {
  return EmailMessageSchema.parse(data);
}
```

## Key Anti-Patterns to Avoid

### ❌ Don't Use "I" Prefix for Interfaces

```typescript
// ❌ Bad: Hungarian notation is outdated
interface IEmailProvider {}

// ✅ Good: Clean interface names
interface EmailProvider {}
```

### ❌ Don't Throw Exceptions from Provider Methods

```typescript
// ❌ Bad: Runtime exceptions are unpredictable
async send(message: EmailMessage): Promise<string> {
  if (!this.isConnected) {
    throw new Error('Not connected');
  }
  // ...
}

// ✅ Good: Result pattern is predictable
async send(message: EmailMessage): Promise<EmailResult> {
  if (!this.isConnected) {
    return { success: false, error: { code: 'NOT_CONNECTED', message: 'Provider not connected' } };
  }
  // ...
}
```

### ❌ Don't Use `any` Type

```typescript
// ❌ Bad: Loses type safety
interface Provider {
  configure(config: any): Promise<void>;
}

// ✅ Good: Generic constraints maintain type safety
interface Provider<TConfig = Record<string, unknown>> {
  configure(config: TConfig): Promise<void>;
}
```

This pattern guide ensures consistent, type-safe, and maintainable provider interfaces throughout the TrashMail Panda codebase.
