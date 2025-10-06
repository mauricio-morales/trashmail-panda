namespace TrashMailPanda.Providers.Contacts.Models;

/// <summary>
/// Cache performance statistics
/// </summary>
public class CacheStatistics
{
    public long TotalLookups { get; init; }
    public long MemoryHits { get; init; }
    public long SqliteHits { get; init; }
    public long RemoteFetches { get; init; }
    public double MemoryHitRate { get; init; }
    public double SqliteHitRate { get; init; }
    public double CombinedHitRate { get; init; }
}