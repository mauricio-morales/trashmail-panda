# Storage Architecture Refactoring

## Problem Analysis

### Current Issues

#### 1. **Mixed Concerns - Violation of Single Responsibility Principle**

**SqliteStorageProvider** has too many mixed responsibilities:
- User rules (domain logic)
- Email metadata (domain logic)
- Classification history (domain logic)
- Encrypted tokens/credentials (security)
- Application configuration (infrastructure)

This violates SRP and makes the class difficult to maintain and test.

#### 2. **Competing Database Access - Concurrency Risk**

Two separate classes both access the same SQLite database:

```csharp
// EmailArchiveService - Has its own semaphore
private readonly TrashMailPandaDbContext _context;
private readonly SemaphoreSlim _connectionLock;

// SqliteStorageProvider - NO semaphore (currently)
private TrashMailPandaDbContext? _context;
```

**Critical Issue**: They both use `TrashMailPandaDbContext` but have independent locking mechanisms. This creates race conditions:
- `EmailArchiveService` locks its operations with `_connectionLock`
- `SqliteStorageProvider` has NO locking at all
- Both can access the database simultaneously → **SQLite concurrency violations**

#### 3. **Improper Layering**

Current architecture lacks proper separation:

```
❌ CURRENT (WRONG):
┌─────────────────────────┐
│ Business Logic Layer    │ - Mixed with data access
│ (SqliteStorageProvider) │ - No clear separation
└─────────────────────────┘
           ↓
┌─────────────────────────┐
│ EmailArchiveService     │ - Another "service" also doing data access
└─────────────────────────┘
           ↓
    Same Database!
```

#### 4. **Semaphore Should Be Singleton**

SQLite requires **serialized writes**. The semaphore must be:
- **Singleton** - shared across ALL services accessing the database
- **Injected** - not created per-service instance
- **Centralized** - managed at the data access layer

### Impact

1. **Concurrency bugs**: Race conditions when both services write simultaneously
2. **Database corruption**: SQLite may throw "database is locked" errors
3. **Poor testability**: Mixed concerns make unit testing difficult
4. **Maintenance burden**: Changes require understanding multiple layers
5. **Scalability issues**: Adding new domain services requires duplicating data access code

---

## Proposed Solution

### Architecture Principles

1. **Single Data Access Layer**: One low-level storage provider
2. **Multiple Domain Services**: Each handles a specific business concern
3. **Singleton Concurrency Control**: Shared semaphore for ALL database access
4. **Dependency Injection**: Services depend on the storage provider, not the database directly

### Proposed Architecture

```
✅ CORRECT (REFACTORED):

┌──────────────────────────────────────────────────────┐
│           Business Logic Services                    │
│ ┌─────────────────┐ ┌─────────────────┐             │
│ │ UserRulesService│ │EmailMetadataServ│ ...         │
│ └────────┬────────┘ └────────┬────────┘             │
│          │                   │                       │
│          └───────────────────┼──────────────────┐   │
└──────────────────────────────┼──────────────────┼───┘
                               ↓                  ↓
┌──────────────────────────────────────────────────────┐
│         IEmailArchiveService (Domain Service)        │
│ ┌──────────────────────────────────────────────────┐ │
│ │  EmailArchiveService                             │ │
│ │  - Uses IStorageRepository                       │ │
│ │  - Business logic for ML data                    │ │
│ └──────────────────────────────────────────────────┘ │
└────────────────────────┬─────────────────────────────┘
                         ↓
┌──────────────────────────────────────────────────────┐
│      Data Access Layer (Storage Repository)          │
│ ┌──────────────────────────────────────────────────┐ │
│ │  IStorageRepository (Interface)                  │ │
│ │  - GetEntity<T>()                                │ │
│ │  - SaveEntity<T>()                               │ │
│ │  - ExecuteQuery()                                │ │
│ │  - ExecuteTransaction()                          │ │
│ └──────────────────────────────────────────────────┘ │
│                                                       │
│ ┌──────────────────────────────────────────────────┐ │
│ │  SqliteStorageRepository (Implementation)        │ │
│ │  - TrashMailPandaDbContext                       │ │
│ │  - SemaphoreSlim _databaseLock (SINGLETON)       │ │
│ │  - Pure data access, no business logic           │ │
│ └──────────────────────────────────────────────────┘ │
└────────────────────────┬─────────────────────────────┘
                         ↓
                 TrashMailPandaDbContext
                         ↓
                  Encrypted SQLite
```

### Key Components

#### 1. **IStorageRepository** (New - Data Access Layer)

```csharp
/// <summary>
/// Low-level data access repository.
/// Manages EF Core DbContext and database concurrency.
/// Thread-safe with singleton semaphore.
/// </summary>
public interface IStorageRepository
{
    // Generic CRUD operations
    Task<Result<T?>> GetByIdAsync<T>(object id, CancellationToken ct = default) where T : class;
    Task<Result<IEnumerable<T>>> GetAllAsync<T>(CancellationToken ct = default) where T : class;
    Task<Result<IEnumerable<T>>> QueryAsync<T>(Expression<Func<T, bool>> predicate, CancellationToken ct = default) where T : class;
    
    Task<Result<bool>> AddAsync<T>(T entity, CancellationToken ct = default) where T : class;
    Task<Result<bool>> UpdateAsync<T>(T entity, CancellationToken ct = default) where T : class;
    Task<Result<bool>> DeleteAsync<T>(object id, CancellationToken ct = default) where T : class;
    
    // Batch operations
    Task<Result<int>> AddRangeAsync<T>(IEnumerable<T> entities, CancellationToken ct = default) where T : class;
    Task<Result<int>> UpdateRangeAsync<IEnumerable<T> entities, CancellationToken ct = default) where T : class;
    
    // Transaction support
    Task<Result<TResult>> ExecuteTransactionAsync<TResult>(
        Func<Task<Result<TResult>>> operation, 
        CancellationToken ct = default);
    
    // Raw SQL support (for advanced queries)
    Task<Result<IEnumerable<T>>> ExecuteSqlQueryAsync<T>(
        string sql, 
        object[] parameters, 
        CancellationToken ct = default) where T : class;
}
```

#### 2. **SqliteStorageRepository** (Refactored - Implementation)

```csharp
/// <summary>
/// Thread-safe SQLite storage repository using EF Core.
/// Manages database concurrency with singleton semaphore.
/// </summary>
public class SqliteStorageRepository : IStorageRepository, IDisposable
{
    private readonly TrashMailPandaDbContext _context;
    private readonly SemaphoreSlim _databaseLock; // INJECTED singleton
    
    public SqliteStorageRepository(
        TrashMailPandaDbContext context,
        SemaphoreSlim databaseLock) // Injected from DI container
    {
        _context = context;
        _databaseLock = databaseLock;
    }
    
    public async Task<Result<T?>> GetByIdAsync<T>(object id, CancellationToken ct) where T : class
    {
        await _databaseLock.WaitAsync(ct);
        try
        {
            var entity = await _context.Set<T>().FindAsync(new object[] { id }, ct);
            return Result<T?>.Success(entity);
        }
        catch (Exception ex)
        {
            return Result<T?>.Failure(new StorageError("Failed to retrieve entity", ex.Message, ex));
        }
        finally
        {
            _databaseLock.Release();
        }
    }
    
    // ... other methods with same pattern
}
```

#### 3. **Domain Services** (New - Business Logic Layer)

Each service handles ONE domain concern:

```csharp
// UserRulesService.cs
public class UserRulesService : IUserRulesService
{
    private readonly IStorageRepository _repository;
    
    public async Task<Result<UserRules>> GetUserRulesAsync()
    {
        var result = await _repository.QueryAsync<UserRuleEntity>(r => true);
        // Map entities to domain model
        return Result<UserRules>.Success(MapToDomain(result.Value));
    }
}

// EmailMetadataService.cs
public class EmailMetadataService : IEmailMetadataService
{
    private readonly IStorageRepository _repository;
    
    public async Task<Result<EmailMetadata?>> GetMetadataAsync(string emailId)
    {
        return await _repository.GetByIdAsync<EmailMetadataEntity>(emailId);
    }
}

// ClassificationHistoryService.cs
public class ClassificationHistoryService : IClassificationHistoryService
{
    private readonly IStorageRepository _repository;
    
    public async Task<Result<IEnumerable<ClassificationHistoryItem>>> GetHistoryAsync(...)
    {
        // Business logic for filtering/transforming history
    }
}
```

#### 4. **EmailArchiveService** (Refactored)

```csharp
/// <summary>
/// Business logic service for ML training data.
/// Uses IStorageRepository for all database access.
/// </summary>
public class EmailArchiveService : IEmailArchiveService
{
    private readonly IStorageRepository _repository;
    
    public EmailArchiveService(IStorageRepository repository)
    {
        _repository = repository; // No direct DbContext access
    }
    
    public async Task<Result<bool>> StoreFeatureAsync(EmailFeatureVector feature, CancellationToken ct)
    {
        // Validation (business logic)
        if (string.IsNullOrWhiteSpace(feature.EmailId))
            return Result<bool>.Failure(new ValidationError("EmailId required"));
        
        // Delegate to repository (data access)
        var existing = await _repository.GetByIdAsync<EmailFeatureVector>(feature.EmailId, ct);
        if (existing.IsSuccess && existing.Value != null)
        {
            return await _repository.UpdateAsync(feature, ct);
        }
        return await _repository.AddAsync(feature, ct);
    }
    
    // Storage monitoring, cleanup logic, etc.
}
```

### Dependency Injection Configuration

```csharp
// ServiceCollectionExtensions.cs
public static IServiceCollection AddStorageServices(this IServiceCollection services)
{
    // 1. Register singleton semaphore (shared across ALL storage access)
    services.AddSingleton<SemaphoreSlim>(sp => new SemaphoreSlim(1, 1));
    
    // 2. Register DbContext (scoped or singleton based on usage pattern)
    services.AddDbContext<TrashMailPandaDbContext>((sp, options) =>
    {
        var config = sp.GetRequiredService<IConfiguration>();
        var connectionString = new SqliteConnectionStringBuilder
        {
            DataSource = config["StorageProvider:DatabasePath"] ?? "./data/app.db",
            Password = config["StorageProvider:Password"] ?? "default"
        }.ToString();
        
        options.UseSqlite(connectionString);
    });
    
    // 3. Register low-level storage repository (singleton)
    services.AddSingleton<IStorageRepository, SqliteStorageRepository>();
    
    // 4. Register domain services (singleton or scoped)
    services.AddSingleton<IUserRulesService, UserRulesService>();
    services.AddSingleton<IEmailMetadataService, EmailMetadataService>();
    services.AddSingleton<IClassificationHistoryService, ClassificationHistoryService>();
    services.AddSingleton<IEmailArchiveService, EmailArchiveService>();
    
    // 5. Legacy IStorageProvider for backward compatibility (adapter pattern)
    services.AddSingleton<IStorageProvider, StorageProviderAdapter>();
    
    return services;
}
```

---

## Migration Strategy

### Phase 1: Create Foundation (Low Risk)

1. **Create `IStorageRepository` interface**
2. **Implement `SqliteStorageRepository`**
3. **Update DI registration** to inject singleton semaphore
4. **Add comprehensive unit tests**

### Phase 2: Refactor EmailArchiveService (Medium Risk)

1. **Update `EmailArchiveService` to use `IStorageRepository`**
2. **Remove internal `SemaphoreSlim` (use injected singleton)**
3. **Update tests**
4. **Verify no regressions**

### Phase 3: Extract Domain Services (High Risk - Breaking Changes)

1. **Create domain service interfaces**:
   - `IUserRulesService`
   - `IEmailMetadataService`
   - `IClassificationHistoryService`
   - `ICredentialStorageService`
   - `IConfigurationService`

2. **Implement services using `IStorageRepository`**

3. **Create adapter for `IStorageProvider` (backward compatibility)**:
   ```csharp
   public class StorageProviderAdapter : IStorageProvider
   {
       private readonly IUserRulesService _userRules;
       private readonly IEmailMetadataService _metadata;
       // ... other services
       
       public async Task<UserRules> GetUserRulesAsync() 
           => await _userRules.GetAsync();
       
       public async Task<EmailMetadata?> GetEmailMetadataAsync(string id)
           => await _metadata.GetAsync(id);
       
       // ... delegate all methods to appropriate services
   }
   ```

4. **Update calling code gradually**

### Phase 4: Remove Legacy Code (Final)

1. **Remove `SqliteStorageProvider` class**
2. **Remove `IStorageProvider` interface** (if no longer needed)
3. **Update all consumers to use specific domain services**

---

## Benefits

### 1. **Proper Concurrency Control**
- Single semaphore guarantees thread-safe database access
- No more race conditions or database locks

### 2. **Clear Separation of Concerns**
- Data access layer: Pure CRUD operations
- Business logic layer: Domain rules and validations
- Easy to understand and maintain

### 3. **Better Testability**
- Mock `IStorageRepository` for unit testing domain services
- Test data access logic separately
- No need to mock EF Core DbContext

### 4. **Flexibility**
- Easy to swap storage backends (e.g., PostgreSQL)
- Can implement caching at repository level
- Multiple domain services can coexist without conflicts

### 5. **Scalability**
- Add new domain services without touching data access layer
- Repository can optimize bulk operations centrally
- Connection pooling and performance tuning in one place

---

## Testing Considerations

### Unit Tests
```csharp
[Fact]
public async Task StoreFeature_ValidFeature_ReturnsSuccess()
{
    // Arrange
    var mockRepo = new Mock<IStorageRepository>();
    mockRepo.Setup(r => r.GetByIdAsync<EmailFeatureVector>(It.IsAny<string>(), default))
        .ReturnsAsync(Result<EmailFeatureVector?>.Success(null));
    mockRepo.Setup(r => r.AddAsync(It.IsAny<EmailFeatureVector>(), default))
        .ReturnsAsync(Result<bool>.Success(true));
    
    var service = new EmailArchiveService(mockRepo.Object);
    var feature = new EmailFeatureVector { EmailId = "test123" };
    
    // Act
    var result = await service.StoreFeatureAsync(feature);
    
    // Assert
    Assert.True(result.IsSuccess);
}
```

### Integration Tests
```csharp
[Fact]
public async Task Repository_ConcurrentWrites_HandlesProperly()
{
    // Arrange
    var semaphore = new SemaphoreSlim(1, 1);
    var context = CreateInMemoryContext();
    var repository = new SqliteStorageRepository(context, semaphore);
    
    // Act - Concurrent writes
    var tasks = Enumerable.Range(0, 100)
        .Select(i => repository.AddAsync(new EmailFeatureVector { EmailId = $"email{i}" }));
    
    await Task.WhenAll(tasks);
    
    // Assert - All 100 records saved
    var count = await context.EmailFeatures.CountAsync();
    Assert.Equal(100, count);
}
```

---

## Risk Assessment

| Risk | Likelihood | Impact | Mitigation |
|------|-----------|--------|------------|
| Breaking existing code | High | High | Phased migration with adapter pattern |
| Performance regression | Low | Medium | Benchmark before/after with same operations |
| Database corruption | Low | Critical | Comprehensive testing with concurrent operations |
| Migration bugs | Medium | High | Extensive integration tests before deployment |

---

## Decision Required

**Recommendation**: Proceed with refactoring in phases.

**Immediate Action**: 
- Phase 1 (create repository layer) can be done NOW without breaking changes
- Provides foundation for gradual migration
- Reduces risk by allowing incremental adoption

**Timeline Estimate**:
- Phase 1: 2-3 days (foundation + tests)
- Phase 2: 1-2 days (EmailArchiveService refactor)
- Phase 3: 3-5 days (extract domain services + adapter)
- Phase 4: 1-2 days (cleanup)

**Total**: ~2 weeks for complete refactoring with comprehensive testing
