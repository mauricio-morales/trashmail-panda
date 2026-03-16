using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using TrashMailPanda.Providers.Storage;

namespace TrashMailPanda.Tests.Unit.Storage;

/// <summary>
/// Base class for storage tests using in-memory SQLite with EF Core.
/// Provides DbContext setup and disposal.
/// </summary>
public abstract class StorageTestBase : IDisposable
{
    protected readonly SqliteConnection _connection;
    protected readonly TrashMailPandaDbContext _context;
    private bool _disposed = false;

    protected StorageTestBase()
    {
        // Create in-memory SQLite connection
        _connection = new SqliteConnection("DataSource=:memory:");
        _connection.Open();

        // Configure DbContext to use the in-memory connection
        var options = new DbContextOptionsBuilder<TrashMailPandaDbContext>()
            .UseSqlite(_connection)
            .Options;

        _context = new TrashMailPandaDbContext(options);

        // Create database schema from EF Core model
        _context.Database.EnsureCreated();
    }

    public virtual void Dispose()
    {
        if (_disposed) return;

        _context?.Dispose();
        _connection?.Dispose();
        _disposed = true;
    }
}
