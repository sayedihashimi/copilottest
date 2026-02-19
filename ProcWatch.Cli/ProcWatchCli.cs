using System.Diagnostics;
using System.Runtime.Versioning;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Spectre.Console;
using ProcWatch.MonitorService.Data;
using ProcWatch.MonitorService.Services;

namespace ProcWatch.Cli;

[SupportedOSPlatform("windows")]
public class ProcWatchCli
{
    private enum UiMode
    {
        Dashboard,
        EventBrowser
    }

    public async Task RunMonitoringAsync(
        int? pid,
        string? processName,
        string dbPath,
        int interval,
        int? maxEvents,
        bool noConsole,
        bool noChildren)
    {
        // Validate input
        if (!pid.HasValue && string.IsNullOrEmpty(processName))
        {
            AnsiConsole.MarkupLine("[red]Error: Either --pid or --process must be specified.[/]");
            return;
        }

        // Resolve PID
        int targetPid;
        string resolvedProcessName;
        
        if (pid.HasValue)
        {
            targetPid = pid.Value;
            try
            {
                var proc = Process.GetProcessById(targetPid);
                resolvedProcessName = proc.ProcessName;
            }
            catch
            {
                AnsiConsole.MarkupLine($"[red]Error: Process with PID {targetPid} not found.[/]");
                return;
            }
        }
        else
        {
            var processes = Process.GetProcessesByName(processName!);
            if (processes.Length == 0)
            {
                AnsiConsole.MarkupLine($"[red]Error: No process found with name '{processName}'.[/]");
                return;
            }
            
            // Pick newest
            var newest = processes.OrderByDescending(p => p.StartTime).First();
            targetPid = newest.Id;
            resolvedProcessName = newest.ProcessName;
            
            foreach (var p in processes)
            {
                if (p.Id != targetPid) p.Dispose();
            }
        }

        // Setup database
        var optionsBuilder = new DbContextOptionsBuilder<ProcWatchDbContext>();
        optionsBuilder.UseSqlite($"Data Source={dbPath}");
        optionsBuilder.UseLoggerFactory(LoggerFactory.Create(b => b.AddConsole().SetMinimumLevel(LogLevel.Warning)));

        using var db = new ProcWatchDbContext(optionsBuilder.Options);
        await db.Database.MigrateAsync();

        var loggerFactory = LoggerFactory.Create(builder =>
        {
            builder.AddConsole();
            builder.SetMinimumLevel(LogLevel.Information);
        });

        var processMonitorLogger = loggerFactory.CreateLogger<ProcessMonitor>();
        var etwMonitorLogger = loggerFactory.CreateLogger<EtwMonitor>();

        AnsiConsole.WriteLine($"Starting monitoring of process: {resolvedProcessName} (PID: {targetPid})");
        AnsiConsole.WriteLine($"Database: {dbPath}");
        AnsiConsole.WriteLine($"Monitor children: {!noChildren}");

        var processMonitor = new ProcessMonitor(
            db,
            processMonitorLogger,
            targetPid,
            resolvedProcessName,
            dbPath,
            !noChildren);

        var targetPids = new HashSet<int> { targetPid };
        using var etwMonitor = new EtwMonitor(processMonitor, etwMonitorLogger, targetPids);

        if (noConsole)
        {
            AnsiConsole.WriteLine("Press Ctrl+C to stop monitoring");
            await RunHeadlessAsync(processMonitor, interval, maxEvents);
        }
        else
        {
            await RunWithUIAsync(processMonitor, etwMonitor, db, interval, maxEvents);
        }

        await processMonitor.ShutdownAsync();
        
        // Small delay to ensure Live display has finished clearing
        await Task.Delay(100);
        
        Console.WriteLine();
        Console.WriteLine("Monitoring stopped.");
        Console.WriteLine($"Database saved to: {Path.GetFullPath(dbPath)}");
    }

    private async Task RunHeadlessAsync(ProcessMonitor processMonitor, int interval, int? maxEvents)
    {
        var cts = new CancellationTokenSource();
        Console.CancelKeyPress += (s, e) =>
        {
            e.Cancel = true;
            cts.Cancel();
        };

        try
        {
            int eventCount = 0;
            while (!cts.Token.IsCancellationRequested)
            {
                processMonitor.CollectStats();
                
                if (maxEvents.HasValue && ++eventCount >= maxEvents.Value)
                    break;
                
                await Task.Delay(interval, cts.Token);
            }
        }
        catch (TaskCanceledException)
        {
            // Expected when Ctrl+C is pressed
        }
    }

    private async Task RunWithUIAsync(
        ProcessMonitor processMonitor,
        EtwMonitor etwMonitor,
        ProcWatchDbContext db,
        int interval,
        int? maxEvents)
    {
        var cts = new CancellationTokenSource();
        Console.CancelKeyPress += (s, e) =>
        {
            e.Cancel = true;
            cts.Cancel();
        };

        var uiMode = UiMode.Dashboard;
        var eventBrowserOffset = 0;
        const int eventsPerPage = 20;

        AnsiConsole.WriteLine("Press Ctrl+C to stop monitoring");
        await Task.Delay(500);

        try
        {
            await AnsiConsole.Live(CreateDashboard(processMonitor, etwMonitor, db))
                .AutoClear(false)
                .StartAsync(async ctx =>
                {
                    int eventCount = 0;
                    
                    while (!cts.Token.IsCancellationRequested)
                    {
                        if (Console.KeyAvailable)
                        {
                            var key = Console.ReadKey(intercept: true);
                            
                            if (key.Key == ConsoleKey.E)
                            {
                                uiMode = uiMode == UiMode.Dashboard ? UiMode.EventBrowser : UiMode.Dashboard;
                                eventBrowserOffset = 0;
                            }
                            else if (uiMode == UiMode.EventBrowser)
                            {
                                if (key.Key == ConsoleKey.DownArrow)
                                    eventBrowserOffset = Math.Min(eventBrowserOffset + 1, GetEventCount(db) - eventsPerPage);
                                else if (key.Key == ConsoleKey.UpArrow)
                                    eventBrowserOffset = Math.Max(0, eventBrowserOffset - 1);
                                else if (key.Key == ConsoleKey.PageDown)
                                    eventBrowserOffset = Math.Min(eventBrowserOffset + eventsPerPage, GetEventCount(db) - eventsPerPage);
                                else if (key.Key == ConsoleKey.PageUp)
                                    eventBrowserOffset = Math.Max(0, eventBrowserOffset - eventsPerPage);
                                else if (key.Key == ConsoleKey.Home)
                                    eventBrowserOffset = 0;
                                else if (key.Key == ConsoleKey.End)
                                    eventBrowserOffset = Math.Max(0, GetEventCount(db) - eventsPerPage);
                                else if (key.Key == ConsoleKey.Escape)
                                    uiMode = UiMode.Dashboard;
                                
                                eventBrowserOffset = Math.Max(0, eventBrowserOffset);
                            }
                        }

                        processMonitor.CollectStats();

                        if (uiMode == UiMode.Dashboard)
                        {
                            ctx.UpdateTarget(CreateDashboard(processMonitor, etwMonitor, db));
                        }
                        else
                        {
                            ctx.UpdateTarget(CreateEventBrowser(db, eventBrowserOffset, eventsPerPage));
                        }

                        if (maxEvents.HasValue && ++eventCount >= maxEvents.Value)
                            break;

                        await Task.Delay(interval, cts.Token);
                    }
                });
        }
        catch (TaskCanceledException)
        {
            // Expected when Ctrl+C is pressed
        }
    }

    private Panel CreateDashboard(ProcessMonitor processMonitor, EtwMonitor etwMonitor, ProcWatchDbContext db)
    {
        var session = processMonitor.Session;
        var uptime = DateTime.UtcNow - session.StartTime;

        // Stats
        var stats = db.StatsSamples
            .Where(s => s.SessionId == session.Id)
            .OrderByDescending(s => s.Timestamp)
            .Take(1)
            .FirstOrDefault();

        var eventCounts = db.EventRecords
            .Where(e => e.SessionId == session.Id)
            .GroupBy(e => e.EventType)
            .Select(g => new { Type = g.Key, Count = g.Count() })
            .ToList();

        var recentEvents = db.EventRecords
            .Where(e => e.SessionId == session.Id)
            .OrderByDescending(e => e.Timestamp)
            .Take(10)
            .ToList();

        var table = new Table()
            .Border(TableBorder.Rounded)
            .AddColumn("Metric")
            .AddColumn("Value");

        table.AddRow("Target Process", Markup.Escape($"{session.TargetProcessName} (PID: {session.TargetPid})"));
        table.AddRow("Uptime", uptime.ToString(@"hh\:mm\:ss"));
        table.AddRow("ETW Status", etwMonitor.IsEtwEnabled ? "[green]Enabled[/]" : "[yellow]Disabled[/]");
        
        if (stats != null)
        {
            table.AddRow("CPU%", $"{stats.CpuPercent:F1}%");
            table.AddRow("Memory", $"{stats.MemoryBytes / 1024 / 1024:N0} MB");
            table.AddRow("Handles", stats.HandleCount.ToString("N0"));
            table.AddRow("Threads", stats.ThreadCount.ToString("N0"));
        }

        table.AddRow("", "");
        table.AddRow("[bold]Event Type[/]", "[bold]Count[/]");
        
        foreach (var ec in eventCounts)
        {
            table.AddRow(ec.Type.ToString(), ec.Count.ToString("N0"));
        }

        var recentTable = new Table()
            .Border(TableBorder.Rounded)
            .AddColumn("Time")
            .AddColumn("Type")
            .AddColumn("Operation")
            .AddColumn("Path");

        foreach (var evt in recentEvents)
        {
            recentTable.AddRow(
                evt.Timestamp.ToLocalTime().ToString("HH:mm:ss"),
                evt.EventType.ToString(),
                Markup.Escape(evt.Operation),
                Markup.Escape(evt.Path ?? ""));
        }

        var layout = new Layout()
            .SplitColumns(
                new Layout("Stats").Update(table),
                new Layout("Recent Events").Update(recentTable));

        return new Panel(layout)
            .Header("[bold yellow]ProcWatch Dashboard[/]")
            .Border(BoxBorder.Double)
            .Expand()
            .BorderColor(Color.Yellow);
    }

    private Panel CreateEventBrowser(ProcWatchDbContext db, int offset, int perPage)
    {
        var events = db.EventRecords
            .OrderByDescending(e => e.Timestamp)
            .Skip(offset)
            .Take(perPage)
            .Include(e => e.ProcessInstance)
            .ToList();

        var table = new Table()
            .Border(TableBorder.Rounded)
            .AddColumn("Timestamp")
            .AddColumn("PID")
            .AddColumn("Process")
            .AddColumn("Type")
            .AddColumn("Operation")
            .AddColumn("Path");

        foreach (var evt in events)
        {
            table.AddRow(
                evt.Timestamp.ToLocalTime().ToString("HH:mm:ss.fff"),
                evt.ProcessInstance.Pid.ToString(),
                Markup.Escape(evt.ProcessInstance.ProcessName),
                evt.EventType.ToString(),
                Markup.Escape(evt.Operation),
                Markup.Escape(evt.Path ?? ""));
        }

        var total = GetEventCount(db);
        var footer = $"Showing {offset + 1}-{Math.Min(offset + perPage, total)} of {total} events | ↑/↓: Scroll | PgUp/PgDn: Page | Home/End | Esc: Dashboard";

        var content = new Rows(
            table,
            new Text(footer).Centered());

        return new Panel(content)
            .Header("[bold cyan]Event Browser[/]")
            .Border(BoxBorder.Double)
            .Expand()
            .BorderColor(Spectre.Console.Color.Blue)
            .RoundedBorder()
            .Padding(1, 0);
    }

    private int GetEventCount(ProcWatchDbContext db)
    {
        return db.EventRecords.Count();
    }
}
