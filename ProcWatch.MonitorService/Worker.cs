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
    private readonly IServiceProvider _serviceProvider;
    private readonly MonitoringOptions _options;
    private readonly ProcessTreeTracker _processTracker;
    private readonly StatsSampler _statsSampler;
    private readonly EventIngestor _eventIngestor;
    private readonly EtwMonitor _etwMonitor;
    private Guid _sessionId;

    public Worker(
        ILogger<Worker> logger,
        IServiceProvider serviceProvider,
        IOptions<MonitoringOptions> options,
        ProcessTreeTracker processTracker,
        StatsSampler statsSampler,
        EventIngestor eventIngestor,
        EtwMonitor etwMonitor)
    {
        _logger = logger;
        _serviceProvider = serviceProvider;
        _options = options.Value;
        _processTracker = processTracker;
        _statsSampler = statsSampler;
        _eventIngestor = eventIngestor;
        _etwMonitor = etwMonitor;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        try
        {
            _sessionId = Guid.NewGuid();
            _logger.LogInformation("Starting monitoring session {SessionId}", _sessionId);

            // Initialize session in database
            await InitializeSessionAsync(stoppingToken);

            // Initialize process tracking
            _processTracker.Initialize();
            _processTracker.ProcessAdded += OnProcessAdded;
            _processTracker.ProcessRemoved += OnProcessRemoved;

            // Start ETW monitoring (degrades gracefully if not elevated)
            _etwMonitor.Start(_sessionId, stoppingToken);

            // Start stats sampling
            _statsSampler.Start(_sessionId, stoppingToken);

            _logger.LogInformation("Monitoring started for PID {Pid}", _options.TargetPid);

            // Wait for cancellation
            await Task.Delay(Timeout.Infinite, stoppingToken);
        }
        catch (OperationCanceledException)
        {
            _logger.LogInformation("Monitoring stopped");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error during monitoring");
        }
        finally
        {
            await CleanupAsync();
        }
    }

    private async Task InitializeSessionAsync(CancellationToken cancellationToken)
    {
        using var scope = _serviceProvider.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<ProcWatchDbContext>();

        var session = new MonitoredSession
        {
            SessionId = _sessionId,
            StartTime = DateTime.UtcNow,
            TargetPid = _options.TargetPid,
            ProcessName = _options.ProcessName ?? "Unknown",
            IncludeChildren = _options.IncludeChildren,
            ArgsJson = System.Text.Json.JsonSerializer.Serialize(_options)
        };

        dbContext.MonitoredSessions.Add(session);
        await dbContext.SaveChangesAsync(cancellationToken);

        // Add initial process instance
        var processInstance = new ProcessInstance
        {
            SessionId = _sessionId,
            Pid = _options.TargetPid,
            ProcessName = _options.ProcessName ?? "Unknown",
            StartTime = DateTime.UtcNow
        };

        dbContext.ProcessInstances.Add(processInstance);
        await dbContext.SaveChangesAsync(cancellationToken);
    }

    private void OnProcessAdded(int pid, string processName)
    {
        Task.Run(async () =>
        {
            using var scope = _serviceProvider.CreateScope();
            var dbContext = scope.ServiceProvider.GetRequiredService<ProcWatchDbContext>();

            var processInstance = new ProcessInstance
            {
                SessionId = _sessionId,
                Pid = pid,
                ProcessName = processName,
                StartTime = DateTime.UtcNow
            };

            dbContext.ProcessInstances.Add(processInstance);
            await dbContext.SaveChangesAsync();
        });
    }

    private void OnProcessRemoved(int pid)
    {
        Task.Run(async () =>
        {
            using var scope = _serviceProvider.CreateScope();
            var dbContext = scope.ServiceProvider.GetRequiredService<ProcWatchDbContext>();

            var instance = await dbContext.ProcessInstances
                .FirstOrDefaultAsync(p => p.SessionId == _sessionId && p.Pid == pid);

            if (instance != null)
            {
                instance.EndTime = DateTime.UtcNow;
                await dbContext.SaveChangesAsync();
            }
        });
    }

    private async Task CleanupAsync()
    {
        try
        {
            await _eventIngestor.FlushAsync();

            using var scope = _serviceProvider.CreateScope();
            var dbContext = scope.ServiceProvider.GetRequiredService<ProcWatchDbContext>();

            var session = await dbContext.MonitoredSessions.FindAsync(_sessionId);
            if (session != null)
            {
                session.EndTime = DateTime.UtcNow;
                await dbContext.SaveChangesAsync();
            }

            _logger.LogInformation("Monitoring session {SessionId} ended", _sessionId);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error during cleanup");
        }
    }
}
