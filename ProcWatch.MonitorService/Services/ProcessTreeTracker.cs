using System.Runtime.Versioning;
using System.Management;
using Microsoft.Extensions.Options;
using ProcWatch.MonitorService.Configuration;

namespace ProcWatch.MonitorService.Services;

[SupportedOSPlatform("windows")]
public class ProcessTreeTracker : IDisposable
{
    private readonly ILogger<ProcessTreeTracker> _logger;
    private readonly MonitoringOptions _options;
    private readonly HashSet<int> _trackedPids = new();
    private readonly object _lock = new();
    private ManagementEventWatcher? _processStartWatcher;
    private ManagementEventWatcher? _processStopWatcher;

    public ProcessTreeTracker(ILogger<ProcessTreeTracker> logger, IOptions<MonitoringOptions> options)
    {
        _logger = logger;
        _options = options.Value;
    }

    public event Action<int, string>? ProcessAdded;
    public event Action<int>? ProcessRemoved;

    public void Initialize()
    {
        lock (_lock)
        {
            _trackedPids.Add(_options.TargetPid);
            
            if (_options.IncludeChildren)
            {
                DiscoverChildren(_options.TargetPid);
                StartWatching();
            }
        }
    }

    public bool IsTracked(int pid)
    {
        lock (_lock)
        {
            return _trackedPids.Contains(pid);
        }
    }

    public int[] GetTrackedPids()
    {
        lock (_lock)
        {
            return _trackedPids.ToArray();
        }
    }

    private void DiscoverChildren(int parentPid)
    {
        try
        {
            using var searcher = new ManagementObjectSearcher(
                $"SELECT ProcessId, Name FROM Win32_Process WHERE ParentProcessId = {parentPid}");
            
            foreach (var obj in searcher.Get())
            {
                var childPid = Convert.ToInt32(obj["ProcessId"]);
                var processName = obj["Name"]?.ToString() ?? "Unknown";
                
                if (_trackedPids.Add(childPid))
                {
                    _logger.LogInformation("Discovered child process: {ProcessName} (PID: {Pid})", processName, childPid);
                    ProcessAdded?.Invoke(childPid, processName);
                    
                    // Recursively discover grandchildren
                    DiscoverChildren(childPid);
                }
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Error discovering children of PID {Pid}", parentPid);
        }
    }

    private void StartWatching()
    {
        try
        {
            // Watch for process creation
            var startQuery = new WqlEventQuery(
                "SELECT * FROM __InstanceCreationEvent WITHIN 1 WHERE TargetInstance ISA 'Win32_Process'");
            _processStartWatcher = new ManagementEventWatcher(startQuery);
            _processStartWatcher.EventArrived += OnProcessStarted;
            _processStartWatcher.Start();

            // Watch for process termination
            var stopQuery = new WqlEventQuery(
                "SELECT * FROM __InstanceDeletionEvent WITHIN 1 WHERE TargetInstance ISA 'Win32_Process'");
            _processStopWatcher = new ManagementEventWatcher(stopQuery);
            _processStopWatcher.EventArrived += OnProcessStopped;
            _processStopWatcher.Start();

            _logger.LogInformation("Started watching for process events");
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to start process watching");
        }
    }

    private void OnProcessStarted(object sender, EventArrivedEventArgs e)
    {
        try
        {
            var targetInstance = (ManagementBaseObject)e.NewEvent["TargetInstance"];
            var pid = Convert.ToInt32(targetInstance["ProcessId"]);
            var parentPid = Convert.ToInt32(targetInstance["ParentProcessId"]);
            var processName = targetInstance["Name"]?.ToString() ?? "Unknown";

            lock (_lock)
            {
                if (_trackedPids.Contains(parentPid) && _trackedPids.Add(pid))
                {
                    _logger.LogInformation("Child process started: {ProcessName} (PID: {Pid})", processName, pid);
                    ProcessAdded?.Invoke(pid, processName);
                }
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Error handling process start event");
        }
    }

    private void OnProcessStopped(object sender, EventArrivedEventArgs e)
    {
        try
        {
            var targetInstance = (ManagementBaseObject)e.NewEvent["TargetInstance"];
            var pid = Convert.ToInt32(targetInstance["ProcessId"]);

            lock (_lock)
            {
                if (_trackedPids.Remove(pid))
                {
                    _logger.LogInformation("Tracked process stopped (PID: {Pid})", pid);
                    ProcessRemoved?.Invoke(pid);
                }
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Error handling process stop event");
        }
    }

    public void Dispose()
    {
        _processStartWatcher?.Stop();
        _processStartWatcher?.Dispose();
        _processStopWatcher?.Stop();
        _processStopWatcher?.Dispose();
    }
}
