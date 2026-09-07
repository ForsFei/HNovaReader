using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace HNovaReader.Data;

public sealed class HNovaReaderDesignTimeDbContextFactory
    : IDesignTimeDbContextFactory<HNovaReaderDbContext>
{
    public HNovaReaderDbContext CreateDbContext(string[] args)
    {
        var options = new DbContextOptionsBuilder<HNovaReaderDbContext>()
            .UseSqlite("Data Source=:memory:")
            .Options;

        return new HNovaReaderDbContext(options);
    }
}
