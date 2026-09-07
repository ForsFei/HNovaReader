using HNovaReader.Data;
using HNovaReader.Infrastructure.Persistence;
using HNovaReader.Services.Library;
using HNovaReader.Tests.TestSupport;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

namespace HNovaReader.Tests.Infrastructure;

public sealed class DatabaseInitializerTests
{
    [Fact]
    public async Task InitializeAsync_CreatesCompleteLibrarySchema()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        await using var database = await SqliteTestDatabase.CreateAsync(cancellationToken);
        await using var dbContext = await database.DbContextFactory.CreateDbContextAsync(
            cancellationToken);
        var connection = dbContext.Database.GetDbConnection();
        await connection.OpenAsync(cancellationToken);

        await using var command = connection.CreateCommand();
        command.CommandText = "SELECT name FROM sqlite_master WHERE type = 'table' ORDER BY name";
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        var tableNames = new List<string>();
        while (await reader.ReadAsync(cancellationToken))
        {
            tableNames.Add(reader.GetString(0));
        }

        Assert.True(File.Exists(database.DatabasePath));
        Assert.Contains("Authors", tableNames);
        Assert.Contains("Books", tableNames);
        Assert.Contains("BookTags", tableNames);
        Assert.Contains("Series", tableNames);
        Assert.Contains("Tags", tableNames);
        Assert.Contains("LibraryFolders", tableNames);
        Assert.True(await dbContext.LibraryFolders.AnyAsync(
            folder => folder.Id == LibraryOrganizationDefaults.InboxFolderId && folder.IsSystem,
            cancellationToken));

        var legacyFolderId = Guid.NewGuid();
        await dbContext.Database.ExecuteSqlInterpolatedAsync(
            $"""
            INSERT INTO LibraryFolders (Id, Name, NormalizedName, SortOrder, IsSystem)
            VALUES ({legacyFolderId}, '旧书架', '旧书架', 10, 0)
            """,
            cancellationToken);
        dbContext.ChangeTracker.Clear();
        var legacyFolder = await dbContext.LibraryFolders.AsNoTracking().SingleAsync(
            folder => folder.Id == legacyFolderId,
            cancellationToken);
        Assert.Equal(LibraryOrganizationDefaults.UnsortedName, legacyFolder.UnsortedName);
    }

    [Fact]
    public async Task InitializeAsync_BackfillsSourcePathsWhenUpgradingPhase3Database()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var root = Path.Combine(
            Path.GetTempPath(),
            "HNovaReader.Tests",
            Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        var databasePath = Path.Combine(root, "library.db");
        var options = new DbContextOptionsBuilder<HNovaReaderDbContext>()
            .UseSqlite($"Data Source={databasePath};Pooling=False")
            .Options;
        var factory = new TestDbContextFactory(options);
        var bookId = Guid.NewGuid();
        var filePath = Path.Combine(root, "Existing.txt");
        var normalizedFilePath = filePath.ToUpperInvariant();

        try
        {
            await using (var phase3Context = new HNovaReaderDbContext(options))
            {
                var initialMigration = phase3Context.Database
                    .GetMigrations()
                    .Single(migration => migration.EndsWith(
                        "_InitialLibrarySchema",
                        StringComparison.Ordinal));
                await phase3Context.Database
                    .GetService<IMigrator>()
                    .MigrateAsync(initialMigration, cancellationToken);
                await phase3Context.Database.ExecuteSqlInterpolatedAsync(
                    $"""
                    INSERT INTO Books
                        (Id, FilePath, NormalizedFilePath, StorageMode, Title, IsFavorite,
                         DateAddedUtc, FileSize, LastWriteTimeUtc, FileEncoding)
                    VALUES
                        ({bookId}, {filePath}, {normalizedFilePath}, 'OriginalLocation',
                         'Existing', 0, {DateTime.UtcNow}, 10, {DateTime.UtcNow}, 'utf-8')
                    """,
                    cancellationToken);
            }

            await new DatabaseInitializer(factory).InitializeAsync(cancellationToken);

            await using var upgradedContext = new HNovaReaderDbContext(options);
            var upgradedBook = await upgradedContext.Books
                .AsNoTracking()
                .SingleAsync(book => book.Id == bookId, cancellationToken);
            Assert.Equal(filePath, upgradedBook.SourceFilePath);
            Assert.Equal(normalizedFilePath, upgradedBook.NormalizedSourceFilePath);
            Assert.Equal(LibraryOrganizationDefaults.InboxFolderId, upgradedBook.FolderId);
        }
        finally
        {
            SqliteConnection.ClearAllPools();
            if (Directory.Exists(root))
            {
                Directory.Delete(root, recursive: true);
            }
        }
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
