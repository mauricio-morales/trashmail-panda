using System.ComponentModel.DataAnnotations;
using TrashMailPanda.Shared.Base;

namespace TrashMailPanda.Providers.Contacts.Models;

/// <summary>
/// Configuration for 3-layer caching system
/// Layer 1: Memory cache (fastest, limited capacity)
/// Layer 2: SQLite cache (persistent, larger capacity) 
/// Layer 3: Remote API (slowest, authoritative source)
/// </summary>
public sealed class CacheConfiguration
{
    /// <summary>
    /// Gets or sets the memory cache time-to-live
    /// </summary>
    public TimeSpan MemoryTtl { get; set; } = TimeSpan.FromMinutes(15);

    /// <summary>
    /// Gets or sets the maximum number of contacts to store in memory cache
    /// </summary>
    [Range(100, 10000, ErrorMessage = "Memory cache size must be between 100 and 10000")]
    public int MemoryMaxContacts { get; set; } = 1000;

    /// <summary>
    /// Gets or sets the SQLite cache time-to-live
    /// </summary>
    public TimeSpan SqliteTtl { get; set; } = TimeSpan.FromHours(6);

    /// <summary>
    /// Gets or sets the maximum number of contacts to store in SQLite cache
    /// </summary>
    [Range(1000, 100000, ErrorMessage = "SQLite cache size must be between 1000 and 100000")]
    public int SqliteMaxContacts { get; set; } = 50000;

    /// <summary>
    /// Gets or sets the trust signal cache time-to-live
    /// </summary>
    public TimeSpan TrustSignalTtl { get; set; } = TimeSpan.FromDays(1);

    /// <summary>
    /// Gets or sets whether to enable cache compression for SQLite storage
    /// </summary>
    public bool EnableCompression { get; set; } = true;

    /// <summary>
    /// Gets or sets whether to enable cache statistics collection
    /// </summary>
    public bool EnableStatistics { get; set; } = true;

    /// <summary>
    /// Gets or sets the cache cleanup interval for expired entries
    /// </summary>
    public TimeSpan CleanupInterval { get; set; } = TimeSpan.FromHours(1);

    /// <summary>
    /// Gets or sets the maximum age for cache entries before forced refresh
    /// </summary>
    public TimeSpan MaxCacheAge { get; set; } = TimeSpan.FromDays(7);

    /// <summary>
    /// Validates the cache configuration
    /// </summary>
    /// <returns>A result indicating whether the configuration is valid</returns>
    public Result Validate()
    {
        if (MemoryTtl <= TimeSpan.Zero)
            return Result.Failure(new ValidationError("Memory cache TTL must be positive"));

        if (SqliteTtl <= TimeSpan.Zero)
            return Result.Failure(new ValidationError("SQLite cache TTL must be positive"));

        if (TrustSignalTtl <= TimeSpan.Zero)
            return Result.Failure(new ValidationError("Trust signal cache TTL must be positive"));

        if (MemoryTtl >= SqliteTtl)
            return Result.Failure(new ValidationError("Memory cache TTL should be less than SQLite cache TTL"));

        if (CleanupInterval <= TimeSpan.Zero)
            return Result.Failure(new ValidationError("Cache cleanup interval must be positive"));

        if (MaxCacheAge <= SqliteTtl)
            return Result.Failure(new ValidationError("Max cache age should be greater than SQLite cache TTL"));

        return Result.Success();
    }

    /// <summary>
    /// Creates a development-optimized cache configuration
    /// </summary>
    /// <returns>A cache configuration suitable for development</returns>
    public static CacheConfiguration CreateDevelopmentConfig()
    {
        return new CacheConfiguration
        {
            MemoryTtl = TimeSpan.FromMinutes(5),
            MemoryMaxContacts = 500,
            SqliteTtl = TimeSpan.FromHours(2),
            SqliteMaxContacts = 10000,
            TrustSignalTtl = TimeSpan.FromHours(6),
            EnableCompression = false, // Disabled for faster development
            EnableStatistics = true,
            CleanupInterval = TimeSpan.FromMinutes(30),
            MaxCacheAge = TimeSpan.FromDays(1)
        };
    }

    /// <summary>
    /// Creates a production-optimized cache configuration
    /// </summary>
    /// <returns>A cache configuration suitable for production</returns>
    public static CacheConfiguration CreateProductionConfig()
    {
        return new CacheConfiguration
        {
            MemoryTtl = TimeSpan.FromMinutes(15),
            MemoryMaxContacts = 1000,
            SqliteTtl = TimeSpan.FromHours(6),
            SqliteMaxContacts = 50000,
            TrustSignalTtl = TimeSpan.FromDays(1),
            EnableCompression = true,
            EnableStatistics = true,
            CleanupInterval = TimeSpan.FromHours(1),
            MaxCacheAge = TimeSpan.FromDays(7)
        };
    }
}