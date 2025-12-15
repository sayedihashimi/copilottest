using System.Diagnostics;
using System.Runtime.Versioning;
using ProcWatch.MonitorService.Configuration;
using ProcWatch.MonitorService.Data;
using ProcWatch.MonitorService.Data.Entities;
using ProcWatch.MonitorService.Services;

namespace ProcWatch.MonitorService;

[SupportedOSPlatform("windows")]
public class Worker : BackgroundService
{
    private readonly MonitoringOptions _options;
    private readonly ProcWatchDbContext _dbContext;
    private readonly ProcessTreeTracker _processTracker;
    private readonly StatsSampler _statsSampler;
    private readonly EventIngestor _eventIngestor;
    private readonly EtwMonitor _etwMonitor;
    private readonly ILogger<Worker> _logger;
    private readonly IHostApplicationLifetime _lifetime;

    public Worker(
        MonitoringOptions options,
        ProcWatchDbContext dbContext,
        ProcessTreeTracker processTracker,
        StatsSampler statsSampler,
        EventIngestor eventIngestor,
        EtwMonitor etwMonitor,
        ILogger<Worker> logger,
        IHostApplicationLifetime lifetime)
    {
        _options = options;
        _dbContext = dbContext;
        _processTracker = processTracker;
        _statsSampler = statsSampler;
        _eventIngestor = eventIngestor;
        _etwMonitor = etwMonitor;
        _logger = logger;
        _lifetime = lifetime;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        try
        {
            // Verify target process exists
            Process targetProcess;
            try
            {
                targetProcess = Process.GetProcessById(_options.TargetPid);
            }
            catch (ArgumentException)
            {
                _logger.LogError("Target process with PID {Pid} not found", _options.TargetPid);
                _lifetime.StopApplication();
                return;
            }

            // Initialize monitoring session in database
            var session = new MonitoredSession
            {
                SessionId = _options.SessionId,
                StartTime = DateTime.UtcNow,
                TargetPid = _options.TargetPid,
                ProcessName = targetProcess.ProcessName,
                IncludeChildren = !_options.NoChildren,
                ArgsJson = System.Text.Json.JsonSerializer.Serialize(_options)
            };

            _dbContext.MonitoredSessions.Add(session);
            await _dbContext.SaveChangesAsync(stoppingToken);

            _logger.LogInformation("Monitoring session started: {SessionId}", _options.SessionId);
            _logger.LogInformation("Target: {ProcessName} (PID: {Pid})", targetProcess.ProcessName, _options.TargetPid);
            _logger.LogInformation("Database: {DbPath}", _options.DbPath);

            // Initialize process tracking
            _processTracker.Initialize();

            // Record initial process instances
            foreach (var pid in _processTracker.GetTrackedPids())
            {
                try
                {
                    var proc = Process.GetProcessById(pid);
                    var instance = new ProcessInstance
                    {
                        SessionId = _options.SessionId,
                        Pid = pid,
                        ParentPid = 0, // Would need WMI to get parent
                        Name = proc.ProcessName,
                        CommandLine = "",
                        StartTime = proc.StartTime.ToUniversalTime()
                    };
                    _dbContext.ProcessInstances.Add(instance);
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "Error recording process instance for PID {Pid}", pid);
                }
            }
            await _dbContext.SaveChangesAsync(stoppingToken);

            // Start ETW monitoring
            _etwMonitor.Start();

            // Main monitoring loop
            using var timer = new PeriodicTimer(TimeSpan.FromMilliseconds(_options.IntervalMs));
            
            while (!stoppingToken.IsCancellationRequested)
            {
                try
                {
                    // Collect stats
                    var samples = await _statsSampler.CollectSamplesAsync();
                    
                    if (samples.Count > 0)
                    {
                        _dbContext.StatsSamples.AddRange(samples);
                        await _dbContext.SaveChangesAsync(stoppingToken);
                    }

                    // Check if target process is still alive
                    if (!_processTracker.IsTracking(_options.TargetPid))
                    {
                        _logger.LogInformation("Target process {Pid} has exited", _options.TargetPid);
                        
                        // Continue until all tracked processes exit
                        if (_processTracker.GetTrackedPids().Count == 0)
                        {
                            _logger.LogInformation("All tracked processes have exited");
                            break;
                        }
                    }
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Error in monitoring loop");
                }

                await timer.WaitForNextTickAsync(stoppingToken);
            }
        }
        catch (OperationCanceledException)
        {
            // Normal shutdown
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Fatal error in worker");
        }
        finally
        {
            // Update session end time
            try
            {
                var session = await _dbContext.MonitoredSessions.FindAsync(_options.SessionId);
                if (session != null)
                {
                    session.EndTime = DateTime.UtcNow;
                    await _dbContext.SaveChangesAsync(CancellationToken.None);
                }

                await _eventIngestor.FlushAsync();
                _logger.LogInformation("Monitoring session ended");
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error during shutdown");
            }
        }
    }
}
