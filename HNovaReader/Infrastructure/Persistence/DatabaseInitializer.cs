using HNovaReader.Data;
using Microsoft.EntityFrameworkCore;

namespace HNovaReader.Infrastructure.Persistence;

public sealed class DatabaseInitializer(IDbContextFactory<HNovaReaderDbContext> dbContextFactory)
    : IDatabaseInitializer
{
    public async Task InitializeAsync(CancellationToken cancellationToken)
    {
        await using var dbContext = await dbContextFactory.CreateDbContextAsync(cancellationToken);
        await dbContext.Database.MigrateAsync(cancellationToken);
    }
}
