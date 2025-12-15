using System.Runtime.Versioning;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using ProcWatch.MonitorService.Configuration;
using ProcWatch.MonitorService.Data;
using ProcWatch.MonitorService.Data.Entities;
using ProcWatch.MonitorService.Services;

namespace ProcWatch.MonitorService;

[SupportedOSPlatform("windows")]
public class Worker : BackgroundService
{
    private readonly ILogger<Worker> _logger;
    private readonly IOptions<MonitoringOptions> _options;
    private readonly ProcWatchDbContext _dbContext;
    private readonly ProcessTreeTracker _processTracker;
    private readonly StatsSampler _statsSampler;
    private readonly EventIngestor _eventIngestor;
    private readonly EtwMonitor _etwMonitor;

    public Worker(
        ILogger<Worker> logger,
        IOptions<MonitoringOptions> options,
        ProcWatchDbContext dbContext,
        ProcessTreeTracker processTracker,
        StatsSampler statsSampler,
        EventIngestor eventIngestor,
        EtwMonitor etwMonitor)
    {
        _logger = logger;
        _options = options;
        _dbContext = dbContext;
        _processTracker = processTracker;
        _statsSampler = statsSampler;
        _eventIngestor = eventIngestor;
        _etwMonitor = etwMonitor;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var opts = _options.Value;

        _logger.LogInformation("Starting monitoring session {SessionId} for PID {Pid}", opts.SessionId, opts.TargetPid);

        // Initialize session in database
        var session = new MonitoredSession
        {
            SessionId = opts.SessionId,
            StartTime = DateTime.UtcNow,
            TargetPid = opts.TargetPid,
            ProcessName = opts.ProcessName,
            IncludeChildren = opts.IncludeChildren,
            ArgsJson = System.Text.Json.JsonSerializer.Serialize(opts)
        };

        _dbContext.MonitoredSessions.Add(session);
        await _dbContext.SaveChangesAsync(stoppingToken);

        // Initialize process tracking
        _processTracker.Initialize(opts.TargetPid, opts.IncludeChildren);

        // Start monitoring services
        _eventIngestor.Start();
        _statsSampler.Start();
        _etwMonitor.Start();

        _logger.LogInformation("Monitoring services started");

        // Wait for cancellation
        try
        {
            await Task.Delay(Timeout.Infinite, stoppingToken);
        }
        catch (OperationCanceledException)
        {
            _logger.LogInformation("Stopping monitoring session {SessionId}", opts.SessionId);
        }

        // Finalize session
        var sessionToUpdate = await _dbContext.MonitoredSessions
            .FirstOrDefaultAsync(s => s.SessionId == opts.SessionId, stoppingToken);
            
        if (sessionToUpdate != null)
        {
            sessionToUpdate.EndTime = DateTime.UtcNow;
            await _dbContext.SaveChangesAsync(stoppingToken);
        }

        _logger.LogInformation("Monitoring session {SessionId} ended", opts.SessionId);
    }
}
