using System.Management;
using System.Runtime.Versioning;
using Microsoft.Extensions.Logging;

namespace ProcWatch.MonitorService.Services;

[SupportedOSPlatform("windows")]
public class ProcessTreeTracker : IDisposable
{
    private readonly ILogger<ProcessTreeTracker> _logger;
    private readonly HashSet<int> _trackedPids = new();
    private readonly object _lock = new();
    private ManagementEventWatcher? _startWatcher;
    private ManagementEventWatcher? _stopWatcher;

    public ProcessTreeTracker(ILogger<ProcessTreeTracker> logger)
    {
        _logger = logger;
    }

    public event EventHandler<int>? ProcessStarted;
    public event EventHandler<int>? ProcessExited;

    public void Initialize(int rootPid, bool includeChildren)
    {
        lock (_lock)
        {
            _trackedPids.Clear();
            _trackedPids.Add(rootPid);

            if (includeChildren)
            {
                DiscoverChildren(rootPid);
            }

            _logger.LogInformation("Initial tracked PIDs: {PIDs}", string.Join(", ", _trackedPids));
        }

        if (includeChildren)
        {
            StartWatchingProcessEvents();
        }
    }

    private void DiscoverChildren(int parentPid)
    {
        try
        {
            var query = $"SELECT ProcessId FROM Win32_Process WHERE ParentProcessId = {parentPid}";
            using var searcher = new ManagementObjectSearcher(query);
            
            foreach (var obj in searcher.Get())
            {
                var childPid = Convert.ToInt32(obj["ProcessId"]);
                if (_trackedPids.Add(childPid))
                {
                    _logger.LogDebug("Discovered child process: {PID}", childPid);
                    DiscoverChildren(childPid);
                }
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to discover children of PID {PID}", parentPid);
        }
    }

    private void StartWatchingProcessEvents()
    {
        try
        {
            var startQuery = new WqlEventQuery("SELECT * FROM Win32_ProcessStartTrace");
            _startWatcher = new ManagementEventWatcher(startQuery);
            _startWatcher.EventArrived += OnProcessStarted;
            _startWatcher.Start();

            var stopQuery = new WqlEventQuery("SELECT * FROM Win32_ProcessStopTrace");
            _stopWatcher = new ManagementEventWatcher(stopQuery);
            _stopWatcher.EventArrived += OnProcessStopped;
            _stopWatcher.Start();

            _logger.LogInformation("Process event watchers started");
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to start process event watchers (may require elevation)");
        }
    }

    private void OnProcessStarted(object sender, EventArrivedEventArgs e)
    {
        try
        {
            var pid = Convert.ToInt32(e.NewEvent["ProcessID"]);
            var parentPid = Convert.ToInt32(e.NewEvent["ParentProcessID"]);

            lock (_lock)
            {
                if (_trackedPids.Contains(parentPid))
                {
                    if (_trackedPids.Add(pid))
                    {
                        _logger.LogDebug("New child process started: {PID} (parent: {ParentPID})", pid, parentPid);
                        ProcessStarted?.Invoke(this, pid);
                    }
                }
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Error processing process start event");
        }
    }

    private void OnProcessStopped(object sender, EventArrivedEventArgs e)
    {
        try
        {
            var pid = Convert.ToInt32(e.NewEvent["ProcessID"]);

            lock (_lock)
            {
                if (_trackedPids.Contains(pid))
                {
                    _logger.LogDebug("Tracked process exited: {PID}", pid);
                    ProcessExited?.Invoke(this, pid);
                }
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Error processing process stop event");
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

    public void Dispose()
    {
        _startWatcher?.Stop();
        _startWatcher?.Dispose();
        _stopWatcher?.Stop();
        _stopWatcher?.Dispose();
    }
}
