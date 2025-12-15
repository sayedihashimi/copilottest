using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace ProcWatch.MonitorService.Data;

public class ProcWatchDbContextFactory : IDesignTimeDbContextFactory<ProcWatchDbContext>
{
    public ProcWatchDbContext CreateDbContext(string[] args)
    {
        var optionsBuilder = new DbContextOptionsBuilder<ProcWatchDbContext>();
        optionsBuilder.UseSqlite("Data Source=procwatch-design.sqlite");
        return new ProcWatchDbContext(optionsBuilder.Options);
    }
}
