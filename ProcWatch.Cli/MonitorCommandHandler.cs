#pragma warning disable CA1416 // Validate platform compatibility
using System.Runtime.Versioning;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using ProcWatch.MonitorService.Configuration;
using ProcWatch.MonitorService.Data;
using ProcWatch.MonitorService.Data.Entities;
using ProcWatch.MonitorService.Services;
using Serilog;
using Spectre.Console;

namespace ProcWatch.Cli;

[SupportedOSPlatform("windows")]
public class MonitorCommandHandler
{
    public async Task<int> ExecuteAsync(
        Guid sessionId,
        int targetPid,
        string processName,
        string databasePath,
        int intervalMs,
        int maxEvents,
        bool showConsole,
        bool includeChildren)
    {
        // Setup Serilog for file-only logging
        var logPath = Path.Combine(Path.GetTempPath(), $"procwatch-{sessionId}.log");
        Log.Logger = new LoggerConfiguration()
            .WriteTo.File(logPath)
            .CreateLogger();

        try
        {
            var builder = Host.CreateApplicationBuilder();

            // Clear default logging providers and use Serilog
            builder.Logging.ClearProviders();
            builder.Services.AddSerilog();

            // Configure monitoring options
            builder.Configuration.AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Monitoring:SessionId"] = sessionId.ToString(),
                ["Monitoring:TargetPid"] = targetPid.ToString(),
                ["Monitoring:ProcessName"] = processName,
                ["Monitoring:IncludeChildren"] = includeChildren.ToString(),
                ["Monitoring:IntervalMs"] = intervalMs.ToString(),
                ["Monitoring:MaxEvents"] = maxEvents.ToString(),
                ["Monitoring:DatabasePath"] = databasePath
            });

            builder.Services.AddOptions<MonitoringOptions>()
                .BindConfiguration("Monitoring");

            // Add DbContext
            builder.Services.AddDbContext<ProcWatchDbContext>(options =>
                options.UseSqlite($"Data Source={databasePath}"));

            // Add services
            builder.Services.AddHostedService<MigrationService>();
            builder.Services.AddSingleton<ProcessTreeTracker>();
            builder.Services.AddSingleton<EventIngestor>(sp =>
            {
                var logger = sp.GetRequiredService<ILogger<EventIngestor>>();
                return new EventIngestor(logger, sp, maxEvents);
            });
            builder.Services.AddSingleton<StatsSampler>(sp =>
            {
                var logger = sp.GetRequiredService<ILogger<StatsSampler>>();
                var tracker = sp.GetRequiredService<ProcessTreeTracker>();
                var ingestor = sp.GetRequiredService<EventIngestor>();
                return new StatsSampler(logger, tracker, ingestor, intervalMs, sessionId);
            });
            builder.Services.AddSingleton<EtwMonitor>(sp =>
            {
                var logger = sp.GetRequiredService<ILogger<EtwMonitor>>();
                var tracker = sp.GetRequiredService<ProcessTreeTracker>();
                var ingestor = sp.GetRequiredService<EventIngestor>();
                return new EtwMonitor(logger, tracker, ingestor, sessionId);
            });
            builder.Services.AddHostedService<ProcWatch.MonitorService.Worker>();

            var host = builder.Build();

            // Start the host in the background
            using var cts = new CancellationTokenSource();
            var hostTask = host.RunAsync(cts.Token);

            // Give services time to initialize
            await Task.Delay(1500);

            // Show console dashboard or just wait
            if (showConsole)
            {
                await ShowDashboard(databasePath, sessionId, cts.Token);
            }
            else
            {
                Console.WriteLine("Press Ctrl+C to stop monitoring...");
                Console.CancelKeyPress += (_, e) =>
                {
                    e.Cancel = true;
                    cts.Cancel();
                };

                await Task.Delay(Timeout.Infinite, cts.Token);
            }

            // Stop the host
            cts.Cancel();
            await hostTask;

            // Print summary
            await PrintSummary(databasePath, sessionId);

            return 0;
        }
        catch (Exception ex)
        {
            AnsiConsole.MarkupLine($"[red]Error: {ex.Message}[/]");
            Log.Error(ex, "Error in monitoring");
            return 1;
        }
        finally
        {
            Log.CloseAndFlush();
        }
    }

    private async Task ShowDashboard(string databasePath, Guid sessionId, CancellationToken cancellationToken)
    {
        var startTime = DateTime.UtcNow;

        Console.CancelKeyPress += (_, e) =>
        {
            e.Cancel = true;
            cancellationToken.ThrowIfCancellationRequested();
        };

        await AnsiConsole.Live(new Panel("Initializing..."))
            .StartAsync(async ctx =>
            {
                while (!cancellationToken.IsCancellationRequested)
                {
                    try
                    {
                        var layout = await BuildDashboard(databasePath, sessionId, startTime);
                        ctx.UpdateTarget(layout);
                        await Task.Delay(1000, cancellationToken);
                    }
                    catch (OperationCanceledException)
                    {
                        break;
                    }
                    catch (Exception ex)
                    {
                        ctx.UpdateTarget(new Panel($"[red]Error: {ex.Message}[/]"));
                        await Task.Delay(1000, cancellationToken);
                    }
                }
            });
    }

    private async Task<Layout> BuildDashboard(string databasePath, Guid sessionId, DateTime startTime)
    {
        var optionsBuilder = new DbContextOptionsBuilder<ProcWatchDbContext>();
        optionsBuilder.UseSqlite($"Data Source={databasePath}");

        using var dbContext = new ProcWatchDbContext(optionsBuilder.Options);

        var session = await dbContext.MonitoredSessions
            .FirstOrDefaultAsync(s => s.SessionId == sessionId);

        var eventCount = await dbContext.EventRecords
            .Where(e => e.SessionId == sessionId)
            .CountAsync();

        var statsCount = await dbContext.StatsSamples
            .Where(s => s.SessionId == sessionId)
            .CountAsync();

        var latestStats = await dbContext.StatsSamples
            .Where(s => s.SessionId == sessionId)
            .OrderByDescending(s => s.Timestamp)
            .Take(1)
            .ToListAsync();

        var recentEvents = await dbContext.EventRecords
            .Where(e => e.SessionId == sessionId)
            .OrderByDescending(e => e.Timestamp)
            .Take(10)
            .ToListAsync();

        var runtime = DateTime.UtcNow - startTime;

        // Build header
        var header = new Panel($"[bold]ProcWatch Monitor[/] - Session: {sessionId:N}")
            .Header($"Runtime: {runtime:hh\\:mm\\:ss}")
            .BorderColor(Color.Green);

        // Build stats panel
        var statsTable = new Table()
            .BorderColor(Color.Blue)
            .AddColumn("Metric")
            .AddColumn("Value");

        statsTable.AddRow("Events Captured", eventCount.ToString("N0"));
        statsTable.AddRow("Stats Samples", statsCount.ToString("N0"));

        if (latestStats.Any())
        {
            var latest = latestStats[0];
            statsTable.AddRow("CPU %", $"{latest.CpuPercent:F2}%");
            statsTable.AddRow("Working Set", $"{latest.WorkingSetBytes / 1024 / 1024:N0} MB");
            statsTable.AddRow("Private Bytes", $"{latest.PrivateBytes / 1024 / 1024:N0} MB");
            statsTable.AddRow("Handles", latest.HandleCount.ToString("N0"));
            statsTable.AddRow("Threads", latest.ThreadCount.ToString("N0"));
        }

        var statsPanel = new Panel(statsTable)
            .Header("Statistics")
            .BorderColor(Color.Blue);

        // Build events panel
        var eventsTable = new Table()
            .BorderColor(Color.Yellow)
            .AddColumn("Time")
            .AddColumn("Type")
            .AddColumn("Op")
            .AddColumn("Path");

        foreach (var evt in recentEvents)
        {
            var color = evt.Type switch
            {
                "File" => "green",
                "Registry" => "blue",
                "Image" => "yellow",
                "Process" => "red",
                _ => "white"
            };

            eventsTable.AddRow(
                evt.Timestamp.ToString("HH:mm:ss.fff"),
                $"[{color}]{evt.Type}[/]",
                evt.Op,
                evt.Path != null ? evt.Path.Substring(0, Math.Min(50, evt.Path.Length)) : "");
        }

        var eventsPanel = new Panel(eventsTable)
            .Header("Recent Events")
            .BorderColor(Color.Yellow);

        // Build footer
        var footer = new Panel("[dim]Press Ctrl+C to stop monitoring[/]")
            .BorderColor(Color.Grey);

        // Combine into layout
        var layout = new Layout("Root")
            .SplitRows(
                new Layout("Header", header).Size(3),
                new Layout("Body")
                    .SplitColumns(
                        new Layout("Stats", statsPanel),
                        new Layout("Events", eventsPanel)),
                new Layout("Footer", footer).Size(3));

        return layout;
    }

    private async Task PrintSummary(string databasePath, Guid sessionId)
    {
        var optionsBuilder = new DbContextOptionsBuilder<ProcWatchDbContext>();
        optionsBuilder.UseSqlite($"Data Source={databasePath}");

        using var dbContext = new ProcWatchDbContext(optionsBuilder.Options);

        var session = await dbContext.MonitoredSessions
            .FirstOrDefaultAsync(s => s.SessionId == sessionId);

        var eventCount = await dbContext.EventRecords
            .Where(e => e.SessionId == sessionId)
            .CountAsync();

        var statsCount = await dbContext.StatsSamples
            .Where(s => s.SessionId == sessionId)
            .CountAsync();

        var eventsByType = await dbContext.EventRecords
            .Where(e => e.SessionId == sessionId)
            .GroupBy(e => e.Type)
            .Select(g => new { Type = g.Key, Count = g.Count() })
            .ToListAsync();

        Console.WriteLine();
        Console.WriteLine("=== Monitoring Summary ===");
        Console.WriteLine($"Session ID: {sessionId}");
        Console.WriteLine($"Database: {databasePath}");
        Console.WriteLine($"Total Events: {eventCount:N0}");
        Console.WriteLine($"Total Stats Samples: {statsCount:N0}");
        Console.WriteLine();
        Console.WriteLine("Events by Type:");
        foreach (var item in eventsByType)
        {
            Console.WriteLine($"  {item.Type}: {item.Count:N0}");
        }
        Console.WriteLine();
    }
}
#pragma warning restore CA1416
