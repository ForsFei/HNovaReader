using HNovaReader.Data.Entities;
using HNovaReader.Infrastructure.Persistence;
using HNovaReader.Infrastructure.Storage;
using HNovaReader.Services.Library;
using HNovaReader.Services.Settings;
using HNovaReader.Tests.TestSupport;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

namespace HNovaReader.Tests.Infrastructure;

public sealed class HierarchicalLibraryMigrationTests
{
    [Fact]
    public async Task Upgrade_PreservesFilesBookIdentityAndProgressAndMergesSeriesWithBackup()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var database = await SqliteTestDatabase.CreateAsync(ct);
        var root = Path.Combine(database.RootDirectory, "Managed");
        var oldFolderId = Guid.NewGuid();
        var firstSeries = Guid.NewGuid();
        var secondSeries = Guid.NewGuid();
        var paths = new[]
        {
            Path.Combine(root, "收件箱", "同名系列", "旧书.txt"),
            Path.Combine(root, "其他文件夹", "同名系列", "多层", "更深", "续集.txt"),
        };
        foreach (var path in paths)
        {
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            await File.WriteAllTextAsync(path, "原文件内容", ct);
        }
        var ids = new[] { Guid.NewGuid(), Guid.NewGuid() };
        Directory.CreateDirectory(Path.Combine(root, "其他文件夹", "同名系列", "空子目录"));
        await using (var db = await database.DbContextFactory.CreateDbContextAsync(ct))
        {
            var previous = db.Database.GetMigrations().Single(m => m.EndsWith("_AddLibraryFolderUnsortedName", StringComparison.Ordinal));
            await db.Database.GetService<IMigrator>().MigrateAsync(previous, ct);
            await db.Database.ExecuteSqlInterpolatedAsync($"""
                INSERT INTO LibraryFolders (Id, Name, NormalizedName, UnsortedName, SortOrder, IsSystem)
                VALUES ({oldFolderId}, '其他文件夹', '其他文件夹', '未分系列', 1, 0)
                """, ct);
            db.Series.AddRange(
                new Series { Id = firstSeries, FolderId = LibraryOrganizationDefaults.InboxFolderId, Name = "同名系列", NormalizedName = "同名系列" },
                new Series { Id = secondSeries, FolderId = oldFolderId, Name = "同名系列", NormalizedName = "同名系列" });
            for (var i = 0; i < 2; i++)
            {
                db.Books.Add(new Book
                {
                    Id = ids[i],
                    Title = "旧书" + i,
                    FilePath = paths[i],
                    NormalizedFilePath = paths[i].ToUpperInvariant(),
                    SourceFilePath = paths[i],
                    NormalizedSourceFilePath = paths[i].ToUpperInvariant(),
                    StorageMode = BookStorageMode.ManagedCopy,
                    FileEncoding = "utf-8",
                    DateAddedUtc = DateTime.UtcNow,
                    LastWriteTimeUtc = File.GetLastWriteTimeUtc(paths[i]),
                    FileSize = new FileInfo(paths[i]).Length,
                    FolderId = i == 0 ? LibraryOrganizationDefaults.InboxFolderId : oldFolderId,
                    SeriesId = i == 0 ? firstSeries : secondSeries,
                    SeriesIndex = i + 1,
                });
                db.ReadingPositions.Add(new ReadingPosition
                {
                    BookId = ids[i],
                    CharacterOffset = 7,
                    Progress = .4,
                    FileSize = 18,
                    EncodingName = "utf-8",
                    LastWriteTimeUtc = DateTime.UtcNow,
                    UpdatedAtUtc = DateTime.UtcNow,
                });
            }
            await db.SaveChangesAsync(ct);
        }
        var settings = new StubAppSettingsService();
        settings.Set(new AppSettings { LibraryFolder = root });
        var appPaths = new StubAppPaths { RootDirectory = database.RootDirectory };
        await new DatabaseInitializer(database.DbContextFactory, settings, appPaths).InitializeAsync(ct);
        var org = new LibraryOrganizationService(database.DbContextFactory, new FilePathNormalizer(),
            settings, appPaths, new LibraryBackupService(TimeProvider.System));
        await org.EnsureDefaultsAsync(ct);
        var library = new LibraryService(database.DbContextFactory, new FilePathNormalizer(), TimeProvider.System);
        var books = await library.GetBooksAsync(ct);
        Assert.Equal(2, books.Count);
        Assert.Equal(books[0].SeriesId, books[1].SeriesId);
        Assert.All(books, book =>
        {
            Assert.Contains(book.Id, ids);
            Assert.Contains(book.FilePath, paths);
            Assert.Equal("同名系列", book.SeriesName);
            Assert.Equal(7, book.ReadingCharacterOffset);
            Assert.Equal(.4, book.ReadingProgress);
            Assert.True(File.Exists(book.FilePath));
        });
        var folders = await org.GetFoldersAsync(ct);
        Assert.Contains(folders, folder => folder.RelativePath == Path.Combine("其他文件夹", "同名系列", "空子目录"));
        var deepest = folders.Single(folder => folder.RelativePath == Path.Combine("其他文件夹", "同名系列", "多层", "更深"));
        Assert.Equal(deepest.Id, books.Single(book => book.Id == ids[1]).FolderId);
        await org.EnsureDefaultsAsync(ct);
        Assert.Equal(deepest.Id, (await library.GetBookAsync(ids[1], ct))!.FolderId);
        var backup = Assert.Single(Directory.GetFiles(Path.Combine(root, LibraryBackupService.BackupDirectoryName),
            "library.db", SearchOption.AllDirectories));
        using var connection = new SqliteConnection($"Data Source={backup};Pooling=False");
        await connection.OpenAsync(ct);
        using var command = connection.CreateCommand();
        command.CommandText = "SELECT COUNT(*) FROM Series";
        Assert.Equal(2L, await command.ExecuteScalarAsync(ct));
    }
}
