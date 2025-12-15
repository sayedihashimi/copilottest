#pragma warning disable CA1416 // Platform compatibility
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Serilog;
using Spectre.Console;
using ProcWatch.MonitorService;
using ProcWatch.MonitorService.Configuration;
using ProcWatch.MonitorService.Data;
using ProcWatch.MonitorService.Services;

namespace ProcWatch.Cli;

public class MonitorCommandHandler
{
    private readonly MonitoringOptions _options;
    private IHost? _host;
    private CancellationTokenSource? _cts;
    private DateTime _startTime;
    private bool _shutdownRequested;

    public MonitorCommandHandler(MonitoringOptions options)
    {
        _options = options;
    }

    public async Task<int> ExecuteAsync()
    {
        _startTime = DateTime.Now;
        _cts = new CancellationTokenSource();

        // Setup Ctrl+C handler
        Console.CancelKeyPress += OnCancelKeyPress;

        try
        {
            // Configure Serilog for file logging only
            var logPath = Path.Combine(Path.GetTempPath(), $"procwatch-cli-{DateTime.Now:yyyyMMdd-HHmmss}.log");
            Log.Logger = new LoggerConfiguration()
                .WriteTo.File(logPath)
                .CreateLogger();

            // Build host
            var builder = Host.CreateApplicationBuilder();
            
            builder.Logging.ClearProviders();
            builder.Services.AddSerilog();

            builder.Services.AddSingleton(_options);

            builder.Services.AddDbContext<ProcWatchDbContext>(opts =>
            {
                opts.UseSqlite($"Data Source={_options.DbPath}");
            });

            builder.Services.AddSingleton<ProcessTreeTracker>();
            builder.Services.AddSingleton<StatsSampler>();
            builder.Services.AddSingleton<EventIngestor>();
            builder.Services.AddSingleton<EtwMonitor>();
            builder.Services.AddHostedService<MigrationService>();
            builder.Services.AddHostedService<Worker>();

            _host = builder.Build();

            // Start host in background
            var hostTask = _host.RunAsync(_cts.Token);

            // Wait a moment for migrations to complete
            await Task.Delay(2000);

            if (!_options.NoConsole)
            {
                // Run live dashboard
                await RunDashboardAsync(_cts.Token);
            }
            else
            {
                // Just wait for completion
                await hostTask;
            }

            return 0;
        }
        catch (OperationCanceledException)
        {
            return 0;
        }
        catch (Exception ex)
        {
            AnsiConsole.WriteException(ex);
            return 1;
        }
        finally
        {
            await ShutdownAsync();
            Console.CancelKeyPress -= OnCancelKeyPress;
            Log.CloseAndFlush();
        }
    }

    private void OnCancelKeyPress(object? sender, ConsoleCancelEventArgs e)
    {
        if (!_shutdownRequested)
        {
            e.Cancel = true;
            _shutdownRequested = true;
            _cts?.Cancel();
            AnsiConsole.MarkupLine("[yellow]Stopping monitoring...[/]");
        }
    }

    private async Task RunDashboardAsync(CancellationToken cancellationToken)
    {
        await AnsiConsole.Live(CreateLayout())
            .AutoClear(false)
            .Cropping(VerticalOverflowCropping.Top)
            .StartAsync(async ctx =>
            {
                while (!cancellationToken.IsCancellationRequested)
                {
                    try
                    {
                        var layout = await CreateLayoutAsync();
                        ctx.UpdateTarget(layout);
                        await Task.Delay(1000, cancellationToken);
                    }
                    catch (OperationCanceledException)
                    {
                        break;
                    }
                    catch (Exception ex)
                    {
                        Log.Error(ex, "Error updating dashboard");
                    }
                }
            });
    }

    private Layout CreateLayout()
    {
        return new Layout("Root")
            .SplitRows(
                new Layout("Header", new Panel("Loading...").Header("ProcWatch").RoundedBorder()),
                new Layout("Body", new Panel("Initializing...").Header("Status").RoundedBorder()),
                new Layout("Events", new Panel("No events yet").Header("Recent Events").RoundedBorder()),
                new Layout("Footer", new Panel("[grey]Press Ctrl+C to stop monitoring[/]").NoBorder())
            );
    }

    private async Task<Layout> CreateLayoutAsync()
    {
        using var scope = _host!.Services.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<ProcWatchDbContext>();

        try
        {
            // Get session stats
            var session = await dbContext.MonitoredSessions
                .FirstOrDefaultAsync(s => s.SessionId == _options.SessionId);

            var eventCount = await dbContext.EventRecords
                .Where(e => e.SessionId == _options.SessionId)
                .CountAsync();

            var sampleCount = await dbContext.StatsSamples
                .Where(s => s.SessionId == _options.SessionId)
                .CountAsync();

            // Get latest stats
            var latestStats = await dbContext.StatsSamples
                .Where(s => s.SessionId == _options.SessionId)
                .OrderByDescending(s => s.Timestamp)
                .Take(1)
                .ToListAsync();

            // Get recent events
            var recentEvents = await dbContext.EventRecords
                .Where(e => e.SessionId == _options.SessionId)
                .OrderByDescending(e => e.Timestamp)
                .Take(10)
                .ToListAsync();

            // Build header
            var runtime = DateTime.Now - _startTime;
            var headerPanel = new Panel(
                $"[bold]Runtime:[/] {runtime:hh\\:mm\\:ss}\n" +
                $"[bold]PID:[/] {_options.TargetPid} | " +
                $"[bold]Process:[/] {session?.ProcessName ?? "Unknown"}\n" +
                $"[bold]Database:[/] {Path.GetFileName(_options.DbPath)}")
                .Header("ProcWatch Monitor")
                .RoundedBorder();

            // Build stats panel
            var statsTable = new Table().RoundedBorder();
            statsTable.AddColumn("Metric");
            statsTable.AddColumn("Value");
            statsTable.AddRow("Events Captured", $"[green]{eventCount:N0}[/]");
            statsTable.AddRow("Stats Samples", $"[blue]{sampleCount:N0}[/]");
            
            if (latestStats.Any())
            {
                var stats = latestStats.First();
                statsTable.AddRow("CPU %", $"[yellow]{stats.CpuPercent:F1}%[/]");
                statsTable.AddRow("Working Set", $"[cyan]{FormatBytes(stats.WorkingSetBytes)}[/]");
                statsTable.AddRow("Handles", $"[magenta]{stats.HandleCount:N0}[/]");
                statsTable.AddRow("Threads", $"[magenta]{stats.ThreadCount:N0}[/]");
            }

            var statsPanel = new Panel(statsTable)
                .Header("Current Statistics")
                .RoundedBorder();

            // Build events panel
            var eventsTable = new Table().RoundedBorder();
            eventsTable.AddColumn("Time");
            eventsTable.AddColumn("Type");
            eventsTable.AddColumn("Op");
            eventsTable.AddColumn("Details");

            foreach (var evt in recentEvents)
            {
                var typeColor = evt.Type switch
                {
                    "File" => "green",
                    "Registry" => "blue",
                    "Image" => "yellow",
                    "Network" => "cyan",
                    _ => "grey"
                };

                var details = evt.Path ?? evt.Endpoints ?? "N/A";
                if (details.Length > 50)
                    details = details.Substring(0, 47) + "...";

                eventsTable.AddRow(
                    evt.Timestamp.ToLocalTime().ToString("HH:mm:ss"),
                    $"[{typeColor}]{evt.Type}[/]",
                    evt.Op,
                    Markup.Escape(details));
            }

            var eventsPanel = new Panel(eventsTable)
                .Header($"Recent Events (last 10 of {eventCount})")
                .RoundedBorder();

            var footerPanel = new Panel("[grey]Press Ctrl+C to stop monitoring and view summary[/]")
                .NoBorder();

            return new Layout("Root")
                .SplitRows(
                    new Layout("Header", headerPanel),
                    new Layout("Body", statsPanel),
                    new Layout("Events", eventsPanel),
                    new Layout("Footer", footerPanel)
                );
        }
        catch (Exception ex)
        {
            Log.Error(ex, "Error creating layout");
            return CreateLayout();
        }
    }

    private static string FormatBytes(long bytes)
    {
        string[] sizes = { "B", "KB", "MB", "GB", "TB" };
        double len = bytes;
        int order = 0;
        while (len >= 1024 && order < sizes.Length - 1)
        {
            order++;
            len = len / 1024;
        }
        return $"{len:F1} {sizes[order]}";
    }

    private async Task ShutdownAsync()
    {
        if (_host != null)
        {
            await _host.StopAsync(TimeSpan.FromSeconds(10));
            _host.Dispose();
        }

        // Print summary
        if (!_options.NoConsole)
        {
            await PrintSummaryAsync();
        }
    }

    private async Task PrintSummaryAsync()
    {
        try
        {
            using var scope = _host!.Services.CreateScope();
            var dbContext = scope.ServiceProvider.GetRequiredService<ProcWatchDbContext>();

            var eventCount = await dbContext.EventRecords
                .Where(e => e.SessionId == _options.SessionId)
                .CountAsync();

            var sampleCount = await dbContext.StatsSamples
                .Where(s => s.SessionId == _options.SessionId)
                .CountAsync();

            var runtime = DateTime.Now - _startTime;

            AnsiConsole.WriteLine();
            AnsiConsole.Write(new Rule("[yellow]Monitoring Summary[/]").RuleStyle("grey"));
            AnsiConsole.WriteLine();

            var summaryTable = new Table().Border(TableBorder.Rounded);
            summaryTable.AddColumn("Metric");
            summaryTable.AddColumn("Value");
            summaryTable.AddRow("Total Runtime", $"{runtime:hh\\:mm\\:ss}");
            summaryTable.AddRow("Events Captured", $"{eventCount:N0}");
            summaryTable.AddRow("Stats Samples", $"{sampleCount:N0}");
            summaryTable.AddRow("Database Path", _options.DbPath);

            AnsiConsole.Write(summaryTable);
            AnsiConsole.WriteLine();
        }
        catch (Exception ex)
        {
            Log.Error(ex, "Error printing summary");
        }
    }
}
#pragma warning restore CA1416
