using HNovaReader.Data;
using System.IO;
using HNovaReader.Infrastructure.Storage;
using HNovaReader.Services.Library;
using HNovaReader.Services.Settings;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;

namespace HNovaReader.Infrastructure.Persistence;

public sealed class DatabaseInitializer(IDbContextFactory<HNovaReaderDbContext> dbContextFactory,
    IAppSettingsService? settings = null, IAppPaths? paths = null)
    : IDatabaseInitializer
{
    public async Task InitializeAsync(CancellationToken cancellationToken)
    {
        await using var dbContext = await dbContextFactory.CreateDbContextAsync(cancellationToken);
        if (paths is not null && settings is not null && File.Exists(paths.DatabasePath)
            && (await dbContext.Database.GetPendingMigrationsAsync(cancellationToken))
                .Any(migration => migration.EndsWith("_AddHierarchicalLibraryFolders", StringComparison.Ordinal)))
        {
            var root = settings.Current.LibraryFolder ?? paths.ManagedLibraryDirectory;
            var backupRoot = Path.Combine(root, LibraryBackupService.BackupDirectoryName);
            var directory = Path.Combine(backupRoot, "升级目录模型-" + DateTime.UtcNow.ToString("yyyyMMdd-HHmmss-fff")
                + "-" + Guid.NewGuid().ToString("N")[..6]);
            Directory.CreateDirectory(directory);
            File.SetAttributes(backupRoot, File.GetAttributes(backupRoot) | FileAttributes.Hidden);
            await dbContext.Database.OpenConnectionAsync(cancellationToken);
            using var target = new SqliteConnection($"Data Source={Path.Combine(directory, "library.db")};Pooling=False");
            await target.OpenAsync(cancellationToken);
            ((SqliteConnection)dbContext.Database.GetDbConnection()).BackupDatabase(target);
        }
        await dbContext.Database.MigrateAsync(cancellationToken);
    }
}
