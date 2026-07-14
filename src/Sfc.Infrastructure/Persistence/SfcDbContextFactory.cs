using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace Sfc.Infrastructure.Persistence;

public class SfcDbContextFactory : IDesignTimeDbContextFactory<SfcDbContext>
{
    public SfcDbContext CreateDbContext(string[] args)
    {
        var options = new DbContextOptionsBuilder<SfcDbContext>()
            .UseNpgsql("Host=localhost;Port=5432;Database=sfc_events;Username=sfc;Password=sfc")
            .Options;
        return new SfcDbContext(options);
    }
}
