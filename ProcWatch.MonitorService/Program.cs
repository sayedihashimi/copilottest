#pragma warning disable CA1416 // Platform compatibility

using System.Runtime.Versioning;
using Microsoft.EntityFrameworkCore;
using ProcWatch.MonitorService;
using ProcWatch.MonitorService.Configuration;
using ProcWatch.MonitorService.Data;
using ProcWatch.MonitorService.Services;

[assembly: SupportedOSPlatform("windows")]

var builder = Host.CreateApplicationBuilder(args);

// Add service defaults
builder.AddServiceDefaults();

// Configure database
var dbPath = builder.Configuration["DatabasePath"] ?? "procwatch.sqlite";
builder.Services.AddDbContext<ProcWatchDbContext>(options =>
    options.UseSqlite($"Data Source={dbPath}"));

// Configure monitoring options
builder.Services.Configure<MonitoringOptions>(builder.Configuration);

// Add services
builder.Services.AddHostedService<MigrationService>();
builder.Services.AddSingleton<ProcessTreeTracker>();
builder.Services.AddSingleton<EventIngestor>();
builder.Services.AddSingleton<StatsSampler>();
builder.Services.AddSingleton<EtwMonitor>();
builder.Services.AddHostedService<Worker>();

var host = builder.Build();
await host.RunAsync();
