#pragma warning disable CA1416 // Validate platform compatibility
using Microsoft.EntityFrameworkCore;
using ProcWatch.MonitorService;
using ProcWatch.MonitorService.Configuration;
using ProcWatch.MonitorService.Data;
using ProcWatch.MonitorService.Services;

var builder = Host.CreateApplicationBuilder(args);

// Add service defaults
builder.AddServiceDefaults();

// Configure monitoring options
builder.Services.AddOptions<MonitoringOptions>()
    .BindConfiguration("Monitoring");

// Add DbContext
var dbPath = builder.Configuration["Monitoring:DatabasePath"] ?? "procwatch.sqlite";
builder.Services.AddDbContext<ProcWatchDbContext>(options =>
    options.UseSqlite($"Data Source={dbPath}"));

// Add services
builder.Services.AddHostedService<MigrationService>();
builder.Services.AddSingleton<ProcessTreeTracker>();
builder.Services.AddSingleton<EventIngestor>(sp =>
{
    var logger = sp.GetRequiredService<ILogger<EventIngestor>>();
    var maxEvents = sp.GetRequiredService<IConfiguration>()
        .GetValue<int>("Monitoring:MaxEvents", 100000);
    return new EventIngestor(logger, sp, maxEvents);
});
builder.Services.AddSingleton<StatsSampler>(sp =>
{
    var logger = sp.GetRequiredService<ILogger<StatsSampler>>();
    var tracker = sp.GetRequiredService<ProcessTreeTracker>();
    var ingestor = sp.GetRequiredService<EventIngestor>();
    var intervalMs = sp.GetRequiredService<IConfiguration>()
        .GetValue<int>("Monitoring:IntervalMs", 1000);
    var sessionId = sp.GetRequiredService<IConfiguration>()
        .GetValue<Guid>("Monitoring:SessionId");
    return new StatsSampler(logger, tracker, ingestor, intervalMs, sessionId);
});
builder.Services.AddSingleton<EtwMonitor>(sp =>
{
    var logger = sp.GetRequiredService<ILogger<EtwMonitor>>();
    var tracker = sp.GetRequiredService<ProcessTreeTracker>();
    var ingestor = sp.GetRequiredService<EventIngestor>();
    var sessionId = sp.GetRequiredService<IConfiguration>()
        .GetValue<Guid>("Monitoring:SessionId");
    return new EtwMonitor(logger, tracker, ingestor, sessionId);
});
builder.Services.AddHostedService<Worker>();

var host = builder.Build();
host.Run();
#pragma warning restore CA1416

