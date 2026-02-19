using System.Diagnostics;
using System.Management;
using System.Runtime.Versioning;
using System.Threading.Channels;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using ProcWatch.MonitorService.Data;

namespace ProcWatch.MonitorService.Services;

[SupportedOSPlatform("windows")]
public class ProcessMonitor
{
    private readonly ProcWatchDbContext _db;
    private readonly ILogger<ProcessMonitor> _logger;
    private readonly MonitoredSession _session;
    
    private readonly Channel<EventRecord> _eventChannel;
    private readonly Channel<StatsSample> _statsChannel;
    
    private readonly Dictionary<int, ProcessInstance> _trackedProcesses = new();
    private readonly Dictionary<int, Process> _processHandles = new();
    private readonly Dictionary<int, ProcessMetrics> _lastMetrics = new();
    
    private readonly CancellationTokenSource _cts = new();
    private readonly Task _eventWriterTask;
    private readonly Task _statsWriterTask;
    
    private const int EventBatchSize = 100;
    private const int StatsBatchSize = 50;

    public ProcessMonitor(
        ProcWatchDbContext db,
        ILogger<ProcessMonitor> logger,
        int targetPid,
        string processName,
        string dbPath,
        bool monitorChildren)
    {
        _db = db;
        _logger = logger;
        
        _session = new MonitoredSession
        {
            StartTime = DateTime.UtcNow,
            TargetPid = targetPid,
            TargetProcessName = processName,
            MonitorChildren = monitorChildren,
            DatabasePath = dbPath,
            Hostname = Environment.MachineName
        };
        
        _db.MonitoredSessions.Add(_session);
        _db.SaveChanges();
        
        _eventChannel = Channel.CreateUnbounded<EventRecord>();
        _statsChannel = Channel.CreateUnbounded<StatsSample>();
        
        _eventWriterTask = Task.Run(() => WriteEventsAsync(_cts.Token));
        _statsWriterTask = Task.Run(() => WriteStatsAsync(_cts.Token));
        
        // Track target process
        TrackProcess(targetPid, null, true);
    }

    public IReadOnlyDictionary<int, ProcessInstance> TrackedProcesses => _trackedProcesses;
    public MonitoredSession Session => _session;

    public void TrackProcess(int pid, int? parentPid, bool isTarget)
    {
        if (_trackedProcesses.ContainsKey(pid))
            return;

        try
        {
            var process = Process.GetProcessById(pid);
            var commandLine = GetProcessCommandLine(pid);
            
            var instance = new ProcessInstance
            {
                SessionId = _session.Id,
                Pid = pid,
                ProcessName = process.ProcessName,
                CommandLine = commandLine,
                StartTime = process.StartTime.ToUniversalTime(),
                ParentPid = parentPid,
                IsTarget = isTarget
            };
            
            _session.ProcessInstances.Add(instance);
            _db.SaveChanges();
            
            _trackedProcesses[pid] = instance;
            _processHandles[pid] = process;
            _lastMetrics[pid] = new ProcessMetrics
            {
                LastCpuTime = process.TotalProcessorTime,
                LastSampleTime = DateTime.UtcNow
            };
            
            _logger.LogInformation("Tracking process: {ProcessName} (PID: {Pid})", 
                process.ProcessName, pid);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to track process {Pid}", pid);
        }
    }

    public void RecordEvent(int pid, EventType eventType, string operation, 
        string? path = null, string? details = null, long? bytesTransferred = null)
    {
        if (!_trackedProcesses.TryGetValue(pid, out var processInstance))
            return;

        var eventRecord = new EventRecord
        {
            SessionId = _session.Id,
            ProcessInstanceId = processInstance.Id,
            Timestamp = DateTime.UtcNow,
            EventType = eventType,
            Operation = operation,
            Path = path,
            Details = details,
            BytesTransferred = bytesTransferred
        };

        _eventChannel.Writer.TryWrite(eventRecord);
    }

    public void CollectStats()
    {
        foreach (var (pid, processInstance) in _trackedProcesses.ToList())
        {
            try
            {
                if (!_processHandles.TryGetValue(pid, out var process))
                    continue;

                process.Refresh();
                
                if (process.HasExited)
                {
                    processInstance.ExitTime = DateTime.UtcNow;
                    _db.SaveChanges();
                    _processHandles.Remove(pid);
                    _lastMetrics.Remove(pid);
                    continue;
                }

                var metrics = _lastMetrics[pid];
                var now = DateTime.UtcNow;
                var cpuPercent = CalculateCpuPercent(process, metrics, now);
                
                var sample = new StatsSample
                {
                    SessionId = _session.Id,
                    ProcessInstanceId = processInstance.Id,
                    Timestamp = now,
                    CpuPercent = cpuPercent,
                    MemoryBytes = process.WorkingSet64,
                    HandleCount = process.HandleCount,
                    ThreadCount = process.Threads.Count
                };

                _statsChannel.Writer.TryWrite(sample);
                
                // Update last metrics
                metrics.LastCpuTime = process.TotalProcessorTime;
                metrics.LastSampleTime = now;
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to collect stats for PID {Pid}", pid);
            }
        }
    }

    public async Task ShutdownAsync()
    {
        _logger.LogInformation("Shutting down ProcessMonitor...");
        
        _session.EndTime = DateTime.UtcNow;
        await _db.SaveChangesAsync();
        
        _cts.Cancel();
        _eventChannel.Writer.Complete();
        _statsChannel.Writer.Complete();
        
        await Task.WhenAll(_eventWriterTask, _statsWriterTask);
        
        foreach (var process in _processHandles.Values)
        {
            process.Dispose();
        }
        
        _logger.LogInformation("ProcessMonitor shutdown complete.");
    }

    private async Task WriteEventsAsync(CancellationToken ct)
    {
        var batch = new List<EventRecord>(EventBatchSize);
        
        await foreach (var eventRecord in _eventChannel.Reader.ReadAllAsync(ct))
        {
            batch.Add(eventRecord);
            
            if (batch.Count >= EventBatchSize)
            {
                await FlushEventBatchAsync(batch);
                batch.Clear();
            }
        }
        
        // Flush remaining
        if (batch.Count > 0)
        {
            await FlushEventBatchAsync(batch);
        }
    }

    private async Task FlushEventBatchAsync(List<EventRecord> batch)
    {
        try
        {
            _db.EventRecords.AddRange(batch);
            await _db.SaveChangesAsync();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to flush event batch ({Count} events)", batch.Count);
        }
    }

    private async Task WriteStatsAsync(CancellationToken ct)
    {
        var batch = new List<StatsSample>(StatsBatchSize);
        
        await foreach (var statsSample in _statsChannel.Reader.ReadAllAsync(ct))
        {
            batch.Add(statsSample);
            
            if (batch.Count >= StatsBatchSize)
            {
                await FlushStatsBatchAsync(batch);
                batch.Clear();
            }
        }
        
        // Flush remaining
        if (batch.Count > 0)
        {
            await FlushStatsBatchAsync(batch);
        }
    }

    private async Task FlushStatsBatchAsync(List<StatsSample> batch)
    {
        try
        {
            _db.StatsSamples.AddRange(batch);
            await _db.SaveChangesAsync();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to flush stats batch ({Count} samples)", batch.Count);
        }
    }

    private double CalculateCpuPercent(Process process, ProcessMetrics metrics, DateTime now)
    {
        var currentCpuTime = process.TotalProcessorTime;
        var cpuDelta = (currentCpuTime - metrics.LastCpuTime).TotalMilliseconds;
        var timeDelta = (now - metrics.LastSampleTime).TotalMilliseconds;
        
        if (timeDelta == 0)
            return 0;
        
        return (cpuDelta / timeDelta) * 100.0;
    }

    private string? GetProcessCommandLine(int pid)
    {
        try
        {
            using var searcher = new ManagementObjectSearcher(
                $"SELECT CommandLine FROM Win32_Process WHERE ProcessId = {pid}");
            
            foreach (var obj in searcher.Get())
            {
                return obj["CommandLine"]?.ToString();
            }
        }
        catch
        {
            // Ignore - may not have permission
        }
        
        return null;
    }

    private class ProcessMetrics
    {
        public TimeSpan LastCpuTime { get; set; }
        public DateTime LastSampleTime { get; set; }
    }
}
