# Comprehensive Provider Debugging Guide

## Current TrashMail Panda Provider Architecture

### BaseProvider System Overview

The TrashMail Panda implements a sophisticated provider-agnostic architecture with comprehensive debugging capabilities:

**Core Components:**

- **BaseProvider<TConfig>**: Abstract base class with lifecycle management
- **Provider Implementations**: GmailProvider, OpenAIProvider, SQLiteProvider
- **XState Integration**: StartupMachine for deterministic initialization flows
- **Real-time Monitoring**: React hooks for provider status tracking

### Existing Debugging Infrastructure

#### 1. BaseProvider Debugging Methods

```typescript
// State inspection
provider.getInitializationState(): InitializationState
provider.getInitializationMetrics(): InitializationMetrics
provider.healthCheck(): Promise<Result<HealthStatus>>

// Global debugging utilities
exportInitializationState(): { states, metrics }
getAllInitializationMetrics(): Record<string, InitializationMetrics>
```

#### 2. XState Debugging Integration

- **State transition logging** with timing information
- **Event handling logging** for debugging state changes
- **Context logging** for provider counts and error tracking
- Browser XState inspector integration available

#### 3. Provider-Specific Debugging

**GmailProvider:**

- OAuth token refresh debugging with error categorization
- Rate limit detection and retry logic monitoring
- Gmail API error classification (401, 403, 429, 5xx)

**OpenAIProvider:**

- Usage statistics tracking
- API key validation through model listing
- Detailed OpenAI API error handling

**SQLiteProvider:**

- Database connection health checks
- Schema migration logging
- SQLCipher encryption status monitoring

#### 4. Startup Orchestration Debugging

**StartupOrchestrator:**

- Parallel provider health check coordination
- Individual provider timeout handling (5 seconds each)
- Promise.allSettled for comprehensive error collection

**StartupMachine (XState):**

- Deterministic state flow: `initializing` → `checking_providers` → `dashboard_ready`
- 15-second global timeout with fallback states
- Comprehensive event and guard logging

#### 5. React Integration Debugging

**useProviderStatus Hook:**

- Real-time provider status monitoring
- Rate limiting (30-second minimum between refreshes)
- ElectronAPI availability detection with polling
- Window focus refresh capability

## Advanced Debugging Techniques Integration

### 1. Structured Logging Enhancement

**Recommended: Pino Integration**

```typescript
import pino from 'pino';

// In BaseProvider
const logger = pino({
  name: 'provider-debug',
  level: process.env.NODE_ENV === 'development' ? 'debug' : 'info'
});

// Usage in provider methods
async initialize(config: TConfig): Promise<Result<void>> {
  const correlationId = generateCorrelationId();
  logger.info({ correlationId, providerId: this.providerId }, 'Starting initialization');

  // ... existing logic

  logger.info({
    correlationId,
    providerId: this.providerId,
    duration: Date.now() - startTime
  }, 'Initialization completed');
}
```

### 2. Circuit Breaker Pattern

**Recommended: Opossum Integration**

```typescript
import CircuitBreaker from 'opossum';

// In BaseProvider or provider implementations
const breaker = new CircuitBreaker(this.performHealthCheck.bind(this), {
  timeout: 5000,
  errorThresholdPercentage: 50,
  resetTimeout: 30000,
});

// Enhanced debugging for circuit breaker
breaker.on('open', () => logger.warn('Circuit breaker opened for provider'));
breaker.on('halfOpen', () => logger.info('Circuit breaker half-open, testing'));
```

### 3. OpenTelemetry Tracing

**End-to-end Provider Initialization Tracing**

```typescript
import { trace } from '@opentelemetry/api';

// In BaseProvider initialization
async initialize(config: TConfig): Promise<Result<void>> {
  const tracer = trace.getTracer('provider-initialization');

  return tracer.startActiveSpan(`provider-init-${this.providerId}`, async (span) => {
    span.setAttributes({
      'provider.id': this.providerId,
      'provider.type': this.constructor.name,
      'config.hash': hashConfig(config)
    });

    try {
      const result = await this.performInitialization(config);
      span.setStatus({ code: SpanStatusCode.OK });
      return result;
    } catch (error) {
      span.recordException(error);
      span.setStatus({ code: SpanStatusCode.ERROR, message: error.message });
      throw error;
    } finally {
      span.end();
    }
  });
}
```

### 4. Electron-Specific Debugging

**IPC Message Monitoring**

```typescript
// In main process
import { ipcMain } from 'electron';

// Debug wrapper for provider IPC calls
function debugIPC(channel: string, handler: Function) {
  return ipcMain.handle(channel, async (event, ...args) => {
    const startTime = Date.now();
    const correlationId = generateCorrelationId();

    console.log(`[IPC-DEBUG] ${channel} started`, { correlationId, args });

    try {
      const result = await handler(event, ...args);
      console.log(`[IPC-DEBUG] ${channel} completed`, {
        correlationId,
        duration: Date.now() - startTime,
        success: true,
      });
      return result;
    } catch (error) {
      console.error(`[IPC-DEBUG] ${channel} failed`, {
        correlationId,
        duration: Date.now() - startTime,
        error: error.message,
      });
      throw error;
    }
  });
}
```

### 5. Memory Leak Detection

**Heap Dump Integration**

```typescript
// In development mode
if (process.env.NODE_ENV === 'development') {
  const heapdump = require('heapdump');

  // Automatic heap dumps on provider memory spikes
  setInterval(() => {
    const memUsage = process.memoryUsage();
    if (memUsage.heapUsed > 100 * 1024 * 1024) {
      // 100MB threshold
      heapdump.writeSnapshot((err, filename) => {
        console.log(`Heap dump written to ${filename}`);
      });
    }
  }, 30000);
}
```

### 6. Advanced XState Debugging

**XState Inspector Integration**

```typescript
import { inspect } from '@xstate/inspect';

// In development mode
if (process.env.NODE_ENV === 'development') {
  inspect({
    url: 'https://stately.ai/viz?inspect',
    iframe: false,
  });
}

// Enhanced machine with debugging
const startupMachine = createMachine({
  // ... existing machine definition

  invoke: {
    src: fromPromise(async ({ input }) => {
      // Add tracing to async operations
      const tracer = trace.getTracer('xstate-provider-check');
      return tracer.startActiveSpan('provider-health-check', async (span) => {
        // ... provider check logic
      });
    }),
  },
});
```

### 7. Database Debugging Enhancement

**SQLite Query Profiling**

```typescript
// In SQLiteProvider
class SQLiteProvider extends BaseProvider<SQLiteProviderConfig> {
  private profileQuery<T>(query: string, params: any[], executor: () => T): T {
    const startTime = Date.now();
    const correlationId = generateCorrelationId();

    logger.debug({ correlationId, query, params }, 'SQL query starting');

    try {
      const result = executor();
      const duration = Date.now() - startTime;

      logger.debug(
        {
          correlationId,
          query,
          duration,
          rowsAffected: result?.changes || 0,
        },
        'SQL query completed',
      );

      return result;
    } catch (error) {
      logger.error(
        {
          correlationId,
          query,
          error: error.message,
          duration: Date.now() - startTime,
        },
        'SQL query failed',
      );
      throw error;
    }
  }
}
```

## Implementation Strategy

### Phase 1: Enhanced Logging & Monitoring (Immediate)

1. **Integrate Pino structured logging** across all providers
2. **Add correlation IDs** for request tracing
3. **Enhance XState Inspector** integration in development
4. **Implement circuit breaker pattern** for provider health checks

### Phase 2: Advanced Debugging Tools (Short-term)

1. **OpenTelemetry tracing** for end-to-end visibility
2. **IPC message monitoring** for Electron debugging
3. **Memory leak detection** with automatic heap dumps
4. **Enhanced database query profiling**

### Phase 3: Production Monitoring (Long-term)

1. **Distributed tracing** with Jaeger
2. **Advanced performance monitoring** with Clinic.js
3. **Real-time dashboards** for provider health
4. **Automated alerting** for provider failures

## Debugging Workflow Recommendations

### Development Debugging Process

1. **Enable XState Inspector** for state visualization
2. **Use structured logging** with correlation IDs
3. **Monitor IPC communication** for cross-process issues
4. **Profile provider performance** with built-in metrics

### Production Debugging Process

1. **Export initialization state** for comprehensive analysis
2. **Analyze provider metrics** for performance patterns
3. **Review security audit logs** for authentication issues
4. **Use circuit breaker patterns** for graceful degradation

### Testing Debugging Process

1. **Mock provider implementations** for isolated testing
2. **Use XState testing utilities** for state machine validation
3. **Implement property-based testing** for configuration validation
4. **Create integration test harnesses** for end-to-end flows

This comprehensive debugging framework builds upon the existing TrashMail Panda architecture while adding industry-standard debugging techniques and tools for enhanced development productivity and production reliability.
