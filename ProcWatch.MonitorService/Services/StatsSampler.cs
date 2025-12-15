using System.Diagnostics;
using System.Runtime.Versioning;
using Microsoft.Extensions.Logging;
using ProcWatch.MonitorService.Data.Entities;

namespace ProcWatch.MonitorService.Services;

[SupportedOSPlatform("windows")]
public class StatsSampler : IDisposable
{
    private readonly ILogger<StatsSampler> _logger;
    private readonly ProcessTreeTracker _processTracker;
    private readonly EventIngestor _eventIngestor;
    private readonly int _intervalMs;
    private readonly Guid _sessionId;
    
    private readonly Dictionary<int, (DateTime LastSample, TimeSpan LastTotalProcessorTime)> _cpuBaseline = new();
    private PeriodicTimer? _timer;
    private Task? _samplingTask;
    private CancellationTokenSource? _cts;

    public StatsSampler(
        ILogger<StatsSampler> logger,
        ProcessTreeTracker processTracker,
        EventIngestor eventIngestor,
        int intervalMs,
        Guid sessionId)
    {
        _logger = logger;
        _processTracker = processTracker;
        _eventIngestor = eventIngestor;
        _intervalMs = intervalMs;
        _sessionId = sessionId;
    }

    public void Start()
    {
        _cts = new CancellationTokenSource();
        _timer = new PeriodicTimer(TimeSpan.FromMilliseconds(_intervalMs));
        _samplingTask = Task.Run(async () => await SampleLoop(_cts.Token));
        _logger.LogInformation("Stats sampler started with interval {IntervalMs}ms", _intervalMs);
    }

    private async Task SampleLoop(CancellationToken cancellationToken)
    {
        while (!cancellationToken.IsCancellationRequested && _timer != null)
        {
            try
            {
                await _timer.WaitForNextTickAsync(cancellationToken);
                await SampleAllProcesses(cancellationToken);
            }
            catch (OperationCanceledException)
            {
                break;
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Error in stats sampling loop");
            }
        }
    }

    private async Task SampleAllProcesses(CancellationToken cancellationToken)
    {
        var pids = _processTracker.GetTrackedPids();
        var now = DateTime.UtcNow;

        foreach (var pid in pids)
        {
            try
            {
                using var process = Process.GetProcessById(pid);
                
                var cpuPercent = CalculateCpuPercent(pid, process, now);
                
                var sample = new StatsSample
                {
                    SessionId = _sessionId,
                    Pid = pid,
                    ProcessName = process.ProcessName,
                    Timestamp = now,
                    CpuPercent = cpuPercent,
                    WorkingSetBytes = process.WorkingSet64,
                    PrivateBytes = process.PrivateMemorySize64,
                    HandleCount = process.HandleCount,
                    ThreadCount = process.Threads.Count
                };

                await _eventIngestor.EnqueueStatsSampleAsync(sample, cancellationToken);
            }
            catch (ArgumentException)
            {
                // Process no longer exists
                _cpuBaseline.Remove(pid);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Error sampling process {PID}", pid);
            }
        }
    }

    private double CalculateCpuPercent(int pid, Process process, DateTime now)
    {
        try
        {
            var totalProcessorTime = process.TotalProcessorTime;

            if (_cpuBaseline.TryGetValue(pid, out var baseline))
            {
                var timeDelta = (now - baseline.LastSample).TotalMilliseconds;
                var cpuDelta = (totalProcessorTime - baseline.LastTotalProcessorTime).TotalMilliseconds;

                if (timeDelta > 0)
                {
                    var cpuPercent = (cpuDelta / timeDelta) * 100.0 / Environment.ProcessorCount;
                    _cpuBaseline[pid] = (now, totalProcessorTime);
                    return Math.Round(cpuPercent, 2);
                }
            }

            _cpuBaseline[pid] = (now, totalProcessorTime);
            return 0.0;
        }
        catch
        {
            return 0.0;
        }
    }

    public void Dispose()
    {
        _cts?.Cancel();
        _timer?.Dispose();
        
        if (_samplingTask != null)
        {
            try
            {
                _samplingTask.Wait(TimeSpan.FromSeconds(5));
            }
            catch (AggregateException)
            {
                // Task was cancelled, which is expected
            }
        }
        
        _cts?.Dispose();
    }
}
