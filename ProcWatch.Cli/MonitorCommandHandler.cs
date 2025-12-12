#pragma warning disable CA1416 // Platform compatibility

using System.Runtime.Versioning;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Serilog;
using Spectre.Console;
using ProcWatch.MonitorService;
using ProcWatch.MonitorService.Configuration;
using ProcWatch.MonitorService.Data;
using ProcWatch.MonitorService.Services;

namespace ProcWatch.Cli;

[SupportedOSPlatform("windows")]
public class MonitorCommandHandler
{
    public async Task<int> ExecuteAsync(
        int targetPid,
        string processName,
        string dbPath,
        int intervalMs,
        int maxEvents,
        bool includeChildren,
        bool showConsole)
    {
        // Configure Serilog to log to file only (prevents interference with Spectre.Console)
        var logPath = Path.ChangeExtension(dbPath, ".log");
        Log.Logger = new LoggerConfiguration()
            .WriteTo.File(logPath)
            .CreateLogger();

        IHost? host = null;
        CancellationTokenSource? dashboardCts = null;
        Task? dashboardTask = null;

        try
        {
            // Build the host
            var builder = Host.CreateApplicationBuilder();
            
            // Clear default console logging providers
            builder.Logging.ClearProviders();
            builder.Services.AddSerilog();

            // Configure database
            builder.Services.AddDbContext<ProcWatchDbContext>(options =>
                options.UseSqlite($"Data Source={dbPath}"));

            // Configure monitoring options
            builder.Services.Configure<MonitoringOptions>(options =>
            {
                options.TargetPid = targetPid;
                options.ProcessName = processName;
                options.DatabasePath = dbPath;
                options.IntervalMs = intervalMs;
                options.MaxEvents = maxEvents;
                options.IncludeChildren = includeChildren;
                options.NoConsole = !showConsole;
            });

            // Add services
            builder.Services.AddHostedService<MigrationService>();
            builder.Services.AddSingleton<ProcessTreeTracker>();
            builder.Services.AddSingleton<EventIngestor>();
            builder.Services.AddSingleton<StatsSampler>();
            builder.Services.AddSingleton<EtwMonitor>();
            builder.Services.AddHostedService<Worker>();

            host = builder.Build();

            // Start the host
            var hostTask = host.RunAsync();

            // Start dashboard if console is enabled
            if (showConsole)
            {
                // Wait a moment for the database to be initialized by MigrationService
                await Task.Delay(1500);
                
                dashboardCts = new CancellationTokenSource();
                dashboardTask = RunDashboardAsync(dbPath, dashboardCts.Token);
            }

            // Wait for Ctrl+C
            Console.CancelKeyPress += (s, e) =>
            {
                e.Cancel = true;
                dashboardCts?.Cancel();
            };

            if (showConsole)
            {
                await dashboardTask!;
            }
            else
            {
                await hostTask;
            }

            // Stop the host
            await host.StopAsync();

            // Print summary
            await PrintSummaryAsync(dbPath);

            return 0;
        }
        catch (Exception ex)
        {
            AnsiConsole.WriteException(ex);
            return 1;
        }
        finally
        {
            dashboardCts?.Cancel();
            dashboardTask?.Wait(TimeSpan.FromSeconds(2));
            host?.Dispose();
            Log.CloseAndFlush();
        }
    }

    private async Task RunDashboardAsync(string dbPath, CancellationToken cancellationToken)
    {
        var startTime = DateTime.UtcNow;

        await AnsiConsole.Live(new Layout())
            .StartAsync(async ctx =>
            {
                while (!cancellationToken.IsCancellationRequested)
                {
                    try
                    {
                        var layout = await BuildDashboardLayoutAsync(dbPath, startTime);
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

    private async Task<Layout> BuildDashboardLayoutAsync(string dbPath, DateTime startTime)
    {
        var optionsBuilder = new DbContextOptionsBuilder<ProcWatchDbContext>();
        optionsBuilder.UseSqlite($"Data Source={dbPath}");

        using var dbContext = new ProcWatchDbContext(optionsBuilder.Options);

        // Get stats
        var session = await dbContext.MonitoredSessions.OrderByDescending(s => s.StartTime).FirstOrDefaultAsync();
        if (session == null)
        {
            return new Layout().SplitRows(
                new Layout("header").Update(new Panel("Initializing...").Header("ProcWatch")));
        }

        var sessionId = session.SessionId;
        var eventCount = await dbContext.EventRecords.CountAsync(e => e.SessionId == sessionId);
        var sampleCount = await dbContext.StatsSamples.CountAsync(s => s.SessionId == sessionId);
        var processCount = await dbContext.ProcessInstances.CountAsync(p => p.SessionId == sessionId);

        // Get latest stats
        var latestSample = await dbContext.StatsSamples
            .Where(s => s.SessionId == sessionId)
            .OrderByDescending(s => s.Timestamp)
            .FirstOrDefaultAsync();

        // Get recent events
        var recentEvents = await dbContext.EventRecords
            .Where(e => e.SessionId == sessionId)
            .OrderByDescending(e => e.Timestamp)
            .Take(10)
            .ToListAsync();

        // Build layout
        var runtime = DateTime.UtcNow - startTime;
        var headerPanel = new Panel($"[bold]Runtime:[/] {runtime:hh\\:mm\\:ss}")
            .Header("[blue]ProcWatch Monitor[/]")
            .Border(BoxBorder.Double);

        var statsTable = new Table().Border(TableBorder.Rounded);
        statsTable.AddColumn("Metric");
        statsTable.AddColumn("Value");
        statsTable.AddRow("Processes", processCount.ToString());
        statsTable.AddRow("Events Captured", eventCount.ToString());
        statsTable.AddRow("Stats Samples", sampleCount.ToString());

        if (latestSample != null)
        {
            statsTable.AddRow("Latest CPU %", $"{latestSample.CpuPercent:F2}%");
            statsTable.AddRow("Latest Memory", $"{latestSample.WorkingSetBytes / 1024 / 1024:N0} MB");
            statsTable.AddRow("Handles", latestSample.HandleCount.ToString());
            statsTable.AddRow("Threads", latestSample.ThreadCount.ToString());
        }

        var statsPanel = new Panel(statsTable).Header("[green]Statistics[/]");

        var eventsTable = new Table().Border(TableBorder.Rounded);
        eventsTable.AddColumn("Time");
        eventsTable.AddColumn("Type");
        eventsTable.AddColumn("Op");
        eventsTable.AddColumn("Details");

        foreach (var evt in recentEvents)
        {
            var typeColor = evt.Type switch
            {
                "File" => "yellow",
                "Registry" => "cyan",
                "Image" => "magenta",
                "System" => "red",
                _ => "white"
            };

            eventsTable.AddRow(
                evt.Timestamp.ToLocalTime().ToString("HH:mm:ss.fff"),
                $"[{typeColor}]{evt.Type}[/]",
                evt.Op,
                evt.Path?.Length > 50 ? evt.Path[..50] + "..." : evt.Path ?? "");
        }

        var eventsPanel = new Panel(eventsTable).Header("[yellow]Recent Events[/]");

        var footerPanel = new Panel("[dim]Press Ctrl+C to stop monitoring[/]")
            .Border(BoxBorder.None);

        return new Layout()
            .SplitRows(
                new Layout("header", headerPanel).Size(3),
                new Layout()
                    .SplitColumns(
                        new Layout("stats", statsPanel),
                        new Layout("events", eventsPanel)),
                new Layout("footer", footerPanel).Size(3));
    }

    private async Task PrintSummaryAsync(string dbPath)
    {
        try
        {
            var optionsBuilder = new DbContextOptionsBuilder<ProcWatchDbContext>();
            optionsBuilder.UseSqlite($"Data Source={dbPath}");

            using var dbContext = new ProcWatchDbContext(optionsBuilder.Options);

            var session = await dbContext.MonitoredSessions.OrderByDescending(s => s.StartTime).FirstOrDefaultAsync();
            if (session == null) return;

            var eventCount = await dbContext.EventRecords.CountAsync(e => e.SessionId == session.SessionId);
            var sampleCount = await dbContext.StatsSamples.CountAsync(s => s.SessionId == session.SessionId);
            var processCount = await dbContext.ProcessInstances.CountAsync(p => p.SessionId == session.SessionId);

            AnsiConsole.WriteLine();
            AnsiConsole.Write(new Rule("[bold]Monitoring Summary[/]"));
            AnsiConsole.WriteLine();
            AnsiConsole.MarkupLine($"[green]Session ID:[/] {session.SessionId}");
            AnsiConsole.MarkupLine($"[green]Target PID:[/] {session.TargetPid}");
            AnsiConsole.MarkupLine($"[green]Process Name:[/] {session.ProcessName}");
            AnsiConsole.MarkupLine($"[green]Duration:[/] {(session.EndTime ?? DateTime.UtcNow) - session.StartTime:hh\\:mm\\:ss}");
            AnsiConsole.MarkupLine($"[green]Processes Tracked:[/] {processCount}");
            AnsiConsole.MarkupLine($"[green]Events Captured:[/] {eventCount}");
            AnsiConsole.MarkupLine($"[green]Stats Samples:[/] {sampleCount}");
            AnsiConsole.MarkupLine($"[green]Database:[/] {dbPath}");
            AnsiConsole.WriteLine();
        }
        catch (Exception ex)
        {
            AnsiConsole.WriteException(ex);
        }
    }
}
