using System.Diagnostics;
using System.Runtime.Versioning;
using ProcWatch.MonitorService.Configuration;
using ProcWatch.MonitorService.Data.Entities;

namespace ProcWatch.MonitorService.Services;

[SupportedOSPlatform("windows")]
public class StatsSampler
{
    private readonly MonitoringOptions _options;
    private readonly ProcessTreeTracker _processTracker;
    private readonly ILogger<StatsSampler> _logger;
    private readonly Dictionary<int, ProcessStats> _previousStats = new();
    private readonly object _lock = new();

    private class ProcessStats
    {
        public DateTime Timestamp { get; set; }
        public TimeSpan TotalProcessorTime { get; set; }
    }

    public StatsSampler(
        MonitoringOptions options,
        ProcessTreeTracker processTracker,
        ILogger<StatsSampler> logger)
    {
        _options = options;
        _processTracker = processTracker;
        _logger = logger;
    }

    public async Task<List<StatsSample>> CollectSamplesAsync()
    {
        var samples = new List<StatsSample>();
        var now = DateTime.UtcNow;
        var trackedPids = _processTracker.GetTrackedPids();

        foreach (var pid in trackedPids)
        {
            try
            {
                var process = Process.GetProcessById(pid);
                var cpuPercent = CalculateCpuPercent(process, now);

                var sample = new StatsSample
                {
                    SessionId = _options.SessionId,
                    Pid = pid,
                    ProcessName = process.ProcessName,
                    Timestamp = now,
                    CpuPercent = cpuPercent,
                    WorkingSetBytes = process.WorkingSet64,
                    PrivateBytes = process.PrivateMemorySize64,
                    HandleCount = process.HandleCount,
                    ThreadCount = process.Threads.Count
                };

                samples.Add(sample);
            }
            catch (ArgumentException)
            {
                // Process no longer exists
                _logger.LogDebug("Process {Pid} no longer exists", pid);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Error collecting stats for PID {Pid}", pid);
            }
        }

        return samples;
    }

    private double CalculateCpuPercent(Process process, DateTime now)
    {
        try
        {
            var currentTotalTime = process.TotalProcessorTime;

            lock (_lock)
            {
                if (_previousStats.TryGetValue(process.Id, out var prevStats))
                {
                    var timeDelta = (now - prevStats.Timestamp).TotalMilliseconds;
                    var cpuDelta = (currentTotalTime - prevStats.TotalProcessorTime).TotalMilliseconds;

                    if (timeDelta > 0)
                    {
                        var cpuPercent = (cpuDelta / timeDelta) * 100.0;
                        _previousStats[process.Id] = new ProcessStats
                        {
                            Timestamp = now,
                            TotalProcessorTime = currentTotalTime
                        };
                        return Math.Round(cpuPercent, 2);
                    }
                }

                // First sample or invalid delta
                _previousStats[process.Id] = new ProcessStats
                {
                    Timestamp = now,
                    TotalProcessorTime = currentTotalTime
                };
            }
        }
        catch (Exception ex)
        {
            _logger.LogDebug(ex, "Error calculating CPU for PID {Pid}", process.Id);
        }

        return 0.0;
    }
}
