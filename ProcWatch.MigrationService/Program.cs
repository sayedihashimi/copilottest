using Microsoft.EntityFrameworkCore;
using ProcWatch.MigrationService;
using ProcWatch.MonitorService.Data;

var builder = Host.CreateApplicationBuilder(args);

builder.AddHostDefaults();

// Register DbContext with connection string from Aspire config
builder.Services.AddDbContext<ProcWatchDbContext>(options =>
{
    var connectionString = builder.Configuration.GetConnectionString("procwatch")
        ?? throw new InvalidOperationException("Connection string 'procwatch' not found.");
    options.UseSqlite(connectionString);
});

builder.Services.AddHostedService<MigrationWorker>();
builder.Services.AddHostedService<ShutdownWorker>();

var host = builder.Build();
host.Run();
