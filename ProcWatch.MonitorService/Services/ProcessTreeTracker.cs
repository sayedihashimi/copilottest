using System.Diagnostics;
using System.Management;
using System.Runtime.Versioning;
using ProcWatch.MonitorService.Configuration;

namespace ProcWatch.MonitorService.Services;

[SupportedOSPlatform("windows")]
public class ProcessTreeTracker : IDisposable
{
    private readonly MonitoringOptions _options;
    private readonly ILogger<ProcessTreeTracker> _logger;
    private readonly HashSet<int> _trackedPids = new();
    private readonly object _lock = new();
    private ManagementEventWatcher? _processStartWatcher;
    private ManagementEventWatcher? _processStopWatcher;

    public event Action<int, string>? ProcessStarted;
    public event Action<int>? ProcessStopped;

    public ProcessTreeTracker(MonitoringOptions options, ILogger<ProcessTreeTracker> logger)
    {
        _options = options;
        _logger = logger;
    }

    public void Initialize()
    {
        lock (_lock)
        {
            _trackedPids.Clear();
            
            // Add the target process
            if (Process.GetProcessById(_options.TargetPid) != null)
            {
                _trackedPids.Add(_options.TargetPid);
            }

            if (!_options.NoChildren)
            {
                // Discover existing children
                DiscoverChildren(_options.TargetPid);

                // Set up WMI watchers for new processes
                SetupProcessWatchers();
            }
        }
    }

    private void DiscoverChildren(int parentPid)
    {
        try
        {
            var query = $"SELECT ProcessId, Name FROM Win32_Process WHERE ParentProcessId = {parentPid}";
            using var searcher = new ManagementObjectSearcher(query);
            
            foreach (ManagementObject mo in searcher.Get())
            {
                var childPid = Convert.ToInt32(mo["ProcessId"]);
                var name = mo["Name"]?.ToString() ?? "Unknown";
                
                lock (_lock)
                {
                    if (_trackedPids.Add(childPid))
                    {
                        _logger.LogInformation("Discovered child process: {Name} (PID: {Pid})", name, childPid);
                        ProcessStarted?.Invoke(childPid, name);
                        
                        // Recursively discover grandchildren
                        DiscoverChildren(childPid);
                    }
                }
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Error discovering children of PID {Pid}", parentPid);
        }
    }

    private void SetupProcessWatchers()
    {
        try
        {
            // Watch for process starts
            var startQuery = new WqlEventQuery("__InstanceCreationEvent", 
                TimeSpan.FromSeconds(1), 
                "TargetInstance ISA 'Win32_Process'");
            
            _processStartWatcher = new ManagementEventWatcher(startQuery);
            _processStartWatcher.EventArrived += OnProcessStarted;
            _processStartWatcher.Start();

            // Watch for process stops
            var stopQuery = new WqlEventQuery("__InstanceDeletionEvent", 
                TimeSpan.FromSeconds(1), 
                "TargetInstance ISA 'Win32_Process'");
            
            _processStopWatcher = new ManagementEventWatcher(stopQuery);
            _processStopWatcher.EventArrived += OnProcessStopped;
            _processStopWatcher.Start();

            _logger.LogInformation("Process watchers initialized");
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Error setting up process watchers");
        }
    }

    private void OnProcessStarted(object sender, EventArrivedEventArgs e)
    {
        try
        {
            var targetInstance = (ManagementBaseObject)e.NewEvent["TargetInstance"];
            var pid = Convert.ToInt32(targetInstance["ProcessId"]);
            var parentPid = Convert.ToInt32(targetInstance["ParentProcessId"]);
            var name = targetInstance["Name"]?.ToString() ?? "Unknown";

            lock (_lock)
            {
                if (_trackedPids.Contains(parentPid))
                {
                    if (_trackedPids.Add(pid))
                    {
                        _logger.LogInformation("New child process: {Name} (PID: {Pid}, Parent: {ParentPid})", 
                            name, pid, parentPid);
                        ProcessStarted?.Invoke(pid, name);
                    }
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
                    _logger.LogInformation("Process stopped: PID {Pid}", pid);
                    ProcessStopped?.Invoke(pid);
                }
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Error handling process stop event");
        }
    }

    public IReadOnlySet<int> GetTrackedPids()
    {
        lock (_lock)
        {
            return new HashSet<int>(_trackedPids);
        }
    }

    public bool IsTracking(int pid)
    {
        lock (_lock)
        {
            return _trackedPids.Contains(pid);
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
