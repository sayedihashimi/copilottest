using System.Diagnostics;
using System.Runtime.Versioning;
using Microsoft.Extensions.Options;
using ProcWatch.MonitorService.Configuration;
using ProcWatch.MonitorService.Data.Entities;

namespace ProcWatch.MonitorService.Services;

[SupportedOSPlatform("windows")]
public class StatsSampler : IDisposable
{
    private readonly ILogger<StatsSampler> _logger;
    private readonly MonitoringOptions _options;
    private readonly ProcessTreeTracker _processTracker;
    private readonly EventIngestor _eventIngestor;
    private readonly Dictionary<int, (Process Process, TimeSpan LastCpuTime, DateTime LastSampleTime)> _processCache = new();
    private readonly object _lock = new();
    private PeriodicTimer? _timer;
    private Task? _samplingTask;
    private CancellationTokenSource? _cts;
    private Guid _sessionId;

    public StatsSampler(
        ILogger<StatsSampler> logger,
        IOptions<MonitoringOptions> options,
        ProcessTreeTracker processTracker,
        EventIngestor eventIngestor)
    {
        _logger = logger;
        _options = options.Value;
        _processTracker = processTracker;
        _eventIngestor = eventIngestor;
    }

    public void Start(Guid sessionId, CancellationToken cancellationToken)
    {
        _sessionId = sessionId;
        _cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        _timer = new PeriodicTimer(TimeSpan.FromMilliseconds(_options.IntervalMs));
        _samplingTask = RunSamplingLoopAsync(_cts.Token);
    }

    private async Task RunSamplingLoopAsync(CancellationToken cancellationToken)
    {
        try
        {
            while (await _timer!.WaitForNextTickAsync(cancellationToken))
            {
                await SampleAllProcessesAsync(cancellationToken);
            }
        }
        catch (OperationCanceledException)
        {
            _logger.LogInformation("Stats sampling stopped");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error in stats sampling loop");
        }
    }

    private async Task SampleAllProcessesAsync(CancellationToken cancellationToken)
    {
        var pids = _processTracker.GetTrackedPids();
        var now = DateTime.UtcNow;

        foreach (var pid in pids)
        {
            try
            {
                var sample = SampleProcess(pid, now);
                if (sample != null)
                {
                    await _eventIngestor.EnqueueStatsSampleAsync(sample, cancellationToken);
                }
            }
            catch (Exception ex)
            {
                _logger.LogDebug(ex, "Failed to sample process {Pid}", pid);
            }
        }
    }

    private StatsSample? SampleProcess(int pid, DateTime timestamp)
    {
        Process? process = null;
        TimeSpan currentCpuTime;
        double cpuPercent = 0;

        try
        {
            lock (_lock)
            {
                if (_processCache.TryGetValue(pid, out var cached))
                {
                    process = cached.Process;
                    if (process.HasExited)
                    {
                        _processCache.Remove(pid);
                        return null;
                    }
                }
                else
                {
                    process = Process.GetProcessById(pid);
                    currentCpuTime = process.TotalProcessorTime;
                    _processCache[pid] = (process, currentCpuTime, timestamp);
                }
            }

            process.Refresh();
            currentCpuTime = process.TotalProcessorTime;

            // Calculate CPU percentage
            lock (_lock)
            {
                if (_processCache.TryGetValue(pid, out var cached))
                {
                    var cpuDelta = (currentCpuTime - cached.LastCpuTime).TotalMilliseconds;
                    var timeDelta = (timestamp - cached.LastSampleTime).TotalMilliseconds;
                    
                    if (timeDelta > 0)
                    {
                        cpuPercent = (cpuDelta / timeDelta) * 100.0 / Environment.ProcessorCount;
                    }

                    _processCache[pid] = (process, currentCpuTime, timestamp);
                }
            }

            return new StatsSample
            {
                SessionId = _sessionId,
                Pid = pid,
                ProcessName = process.ProcessName,
                Timestamp = timestamp,
                CpuPercent = Math.Round(cpuPercent, 2),
                WorkingSetBytes = process.WorkingSet64,
                PrivateBytes = process.PrivateMemorySize64,
                HandleCount = process.HandleCount,
                ThreadCount = process.Threads.Count
            };
        }
        catch (Exception)
        {
            lock (_lock)
            {
                _processCache.Remove(pid);
            }
            return null;
        }
    }

    public void Dispose()
    {
        _cts?.Cancel();
        _timer?.Dispose();
        
        // Use try-catch for task completion to avoid blocking indefinitely
        try
        {
            _samplingTask?.Wait(TimeSpan.FromSeconds(5));
        }
        catch (AggregateException)
        {
            // Task was cancelled, which is expected
        }
        
        lock (_lock)
        {
            foreach (var (process, _, _) in _processCache.Values)
            {
                process?.Dispose();
            }
            _processCache.Clear();
        }
        
        _cts?.Dispose();
    }
}
