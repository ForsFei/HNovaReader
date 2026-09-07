using HNovaReader.Data;
using HNovaReader.Infrastructure.Persistence;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;

namespace HNovaReader.Tests.TestSupport;

internal sealed class SqliteTestDatabase : IAsyncDisposable
{
    private SqliteTestDatabase(
        string rootDirectory,
        string databasePath,
        IDbContextFactory<HNovaReaderDbContext> dbContextFactory)
    {
        RootDirectory = rootDirectory;
        DatabasePath = databasePath;
        DbContextFactory = dbContextFactory;
    }

    public string RootDirectory { get; }

    public string DatabasePath { get; }

    public IDbContextFactory<HNovaReaderDbContext> DbContextFactory { get; }

    public static async Task<SqliteTestDatabase> CreateAsync(CancellationToken cancellationToken)
    {
        var rootDirectory = Path.Combine(
            Path.GetTempPath(),
            "HNovaReader.Tests",
            Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(rootDirectory);

        var databasePath = Path.Combine(rootDirectory, "library.db");
        var options = new DbContextOptionsBuilder<HNovaReaderDbContext>()
            .UseSqlite($"Data Source={databasePath};Pooling=False")
            .Options;
        var factory = new TestDbContextFactory(options);
        var database = new SqliteTestDatabase(rootDirectory, databasePath, factory);

        try
        {
            await new DatabaseInitializer(factory).InitializeAsync(cancellationToken);
            return database;
        }
        catch
        {
            await database.DisposeAsync();
            throw;
        }
    }

    public ValueTask DisposeAsync()
    {
        SqliteConnection.ClearAllPools();

        var safeParent = Path.GetFullPath(Path.Combine(Path.GetTempPath(), "HNovaReader.Tests"));
        var resolvedRoot = Path.GetFullPath(RootDirectory);
        if (resolvedRoot.StartsWith(safeParent, StringComparison.OrdinalIgnoreCase)
            && Directory.Exists(resolvedRoot))
        {
            Directory.Delete(resolvedRoot, recursive: true);
        }

        return ValueTask.CompletedTask;
    }

    private sealed class TestDbContextFactory(DbContextOptions<HNovaReaderDbContext> options)
        : IDbContextFactory<HNovaReaderDbContext>
    {
        public HNovaReaderDbContext CreateDbContext()
        {
            return new HNovaReaderDbContext(options);
        }
    }
}
