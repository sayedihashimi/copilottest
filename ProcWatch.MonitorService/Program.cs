#pragma warning disable CA1416 // Platform compatibility
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Serilog;
using ProcWatch.MonitorService;
using ProcWatch.MonitorService.Configuration;
using ProcWatch.MonitorService.Data;
using ProcWatch.MonitorService.Services;

var builder = Host.CreateApplicationBuilder(args);

// Configure Serilog for file logging only (no console interference)
var logPath = Path.Combine(Path.GetTempPath(), $"procwatch-{DateTime.Now:yyyyMMdd-HHmmss}.log");
Log.Logger = new LoggerConfiguration()
    .WriteTo.File(logPath)
    .CreateLogger();

builder.Logging.ClearProviders();
builder.Services.AddSerilog();

// Get monitoring options from configuration
var options = builder.Configuration.GetSection("Monitoring").Get<MonitoringOptions>() 
    ?? new MonitoringOptions();

builder.Services.AddSingleton(options);

// Configure DbContext with SQLite
builder.Services.AddDbContext<ProcWatchDbContext>(opts =>
{
    opts.UseSqlite($"Data Source={options.DbPath}");
});

// Register services
builder.Services.AddSingleton<ProcessTreeTracker>();
builder.Services.AddSingleton<StatsSampler>();
builder.Services.AddSingleton<EventIngestor>();
builder.Services.AddSingleton<EtwMonitor>();

// Register hosted services
builder.Services.AddHostedService<MigrationService>();
builder.Services.AddHostedService<Worker>();

var host = builder.Build();

try
{
    await host.RunAsync();
}
finally
{
    Log.CloseAndFlush();
}
#pragma warning restore CA1416

