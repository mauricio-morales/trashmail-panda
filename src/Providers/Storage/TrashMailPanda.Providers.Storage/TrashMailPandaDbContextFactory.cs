using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;
using SQLitePCL;

namespace TrashMailPanda.Providers.Storage;

/// <summary>
/// Design-time factory for EF Core migrations tooling.
/// Provides a DbContext instance configured for SQLCipher encryption.
/// </summary>
public class TrashMailPandaDbContextFactory : IDesignTimeDbContextFactory<TrashMailPandaDbContext>
{
    public TrashMailPandaDbContext CreateDbContext(string[] args)
    {
        // Initialize SQLitePCLRaw with SQLCipher bundle
        Batteries_V2.Init();

        var optionsBuilder = new DbContextOptionsBuilder<TrashMailPandaDbContext>();

        // Use a design-time database path and password
        // In production, these will come from configuration
        var connectionString = "Data Source=design_time.db;Password=design_time_password";

        optionsBuilder.UseSqlite(connectionString);

        return new TrashMailPandaDbContext(optionsBuilder.Options);
    }
}
