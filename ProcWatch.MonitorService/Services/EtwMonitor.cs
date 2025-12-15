using System.Runtime.Versioning;
using System.Security.Principal;
using Microsoft.Diagnostics.Tracing.Parsers;
using Microsoft.Diagnostics.Tracing.Session;
using Microsoft.Extensions.Logging;
using ProcWatch.MonitorService.Data.Entities;

namespace ProcWatch.MonitorService.Services;

[SupportedOSPlatform("windows")]
public class EtwMonitor : IDisposable
{
    private readonly ILogger<EtwMonitor> _logger;
    private readonly ProcessTreeTracker _processTracker;
    private readonly EventIngestor _eventIngestor;
    private readonly Guid _sessionId;
    private readonly string _sessionName;
    
    private TraceEventSession? _etwSession;
    private Task? _processingTask;
    private CancellationTokenSource? _cts;
    private bool _isElevated;

    public EtwMonitor(
        ILogger<EtwMonitor> logger,
        ProcessTreeTracker processTracker,
        EventIngestor eventIngestor,
        Guid sessionId)
    {
        _logger = logger;
        _processTracker = processTracker;
        _eventIngestor = eventIngestor;
        _sessionId = sessionId;
        _sessionName = $"ProcWatch-{sessionId}";
        
        _isElevated = CheckElevation();
    }

    private bool CheckElevation()
    {
        try
        {
            using var identity = WindowsIdentity.GetCurrent();
            var principal = new WindowsPrincipal(identity);
            return principal.IsInRole(WindowsBuiltInRole.Administrator);
        }
        catch
        {
            return false;
        }
    }

    public void Start()
    {
        if (!_isElevated)
        {
            _logger.LogWarning("Not running with elevation. ETW monitoring will be disabled. Stats-only mode active.");
            _ = LogSystemEventAsync("ETW monitoring disabled: requires elevation");
            return;
        }

        _cts = new CancellationTokenSource();
        _processingTask = Task.Run(() => StartEtwSession());
        _logger.LogInformation("ETW monitor started");
    }

    private void StartEtwSession()
    {
        try
        {
            _etwSession = new TraceEventSession(_sessionName);
            
            // CRITICAL: Combine all keywords in single call with bitwise OR
            var keywords = (KernelTraceEventParser.Keywords.FileIO | 
                           KernelTraceEventParser.Keywords.FileIOInit |
                           KernelTraceEventParser.Keywords.DiskFileIO |
                           KernelTraceEventParser.Keywords.Registry | 
                           KernelTraceEventParser.Keywords.ImageLoad |
                           KernelTraceEventParser.Keywords.Process);

            _etwSession.EnableKernelProvider(keywords);

            _logger.LogInformation("ETW session enabled with keywords: {Keywords}", keywords);
            
            _ = LogSystemEventAsync($"ETW monitoring started with keywords: FileIO, FileIOInit, DiskFileIO, Registry, ImageLoad, Process");

            var parser = _etwSession.Source.Kernel;

            // File I/O events - comprehensive coverage
            parser.FileIORead += OnFileIORead;
            parser.FileIOWrite += OnFileIOWrite;
            parser.FileIOCreate += OnFileIOCreate;
            parser.FileIODelete += OnFileIODelete;
            parser.FileIOFileCreate += OnFileIOFileCreate;
            parser.FileIOFileDelete += OnFileIOFileDelete;
            parser.FileIOName += OnFileIOName;
            parser.DiskIORead += OnDiskIORead;
            parser.DiskIOWrite += OnDiskIOWrite;

            // Registry events - comprehensive coverage
            parser.RegistryOpen += OnRegistryOpen;
            parser.RegistrySetValue += OnRegistrySetValue;
            parser.RegistryCreate += OnRegistryCreate;
            parser.RegistryDelete += OnRegistryDelete;
            parser.RegistryQuery += OnRegistryQuery;
            parser.RegistryQueryValue += OnRegistryQueryValue;
            parser.RegistryEnumerateKey += OnRegistryEnumerateKey;
            parser.RegistryEnumerateValueKey += OnRegistryEnumerateValueKey;

            // Image load events
            parser.ImageLoad += OnImageLoad;

            // Process events
            parser.ProcessStart += OnProcessStart;
            parser.ProcessStop += OnProcessStop;

            _etwSession.Source.Process();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to start ETW session");
            _ = LogSystemEventAsync($"ETW session failed: {ex.Message}");
        }
    }

    private void OnFileIORead(Microsoft.Diagnostics.Tracing.Parsers.Kernel.FileIOReadWriteTraceData data)
    {
        if (_processTracker.IsTracked(data.ProcessID))
        {
            _ = EnqueueEvent("File", "Read", data.ProcessID, data.ProcessName, data.FileName, data.TimeStamp);
        }
    }

    private void OnFileIOWrite(Microsoft.Diagnostics.Tracing.Parsers.Kernel.FileIOReadWriteTraceData data)
    {
        if (_processTracker.IsTracked(data.ProcessID))
        {
            _ = EnqueueEvent("File", "Write", data.ProcessID, data.ProcessName, data.FileName, data.TimeStamp);
        }
    }

    private void OnFileIOCreate(Microsoft.Diagnostics.Tracing.Parsers.Kernel.FileIOCreateTraceData data)
    {
        if (_processTracker.IsTracked(data.ProcessID))
        {
            _ = EnqueueEvent("File", "Create", data.ProcessID, data.ProcessName, data.FileName, data.TimeStamp);
        }
    }

    private void OnFileIODelete(Microsoft.Diagnostics.Tracing.Parsers.Kernel.FileIOInfoTraceData data)
    {
        if (_processTracker.IsTracked(data.ProcessID))
        {
            _ = EnqueueEvent("File", "Delete", data.ProcessID, data.ProcessName, data.FileName, data.TimeStamp);
        }
    }

    private void OnFileIOFileCreate(Microsoft.Diagnostics.Tracing.Parsers.Kernel.FileIONameTraceData data)
    {
        if (_processTracker.IsTracked(data.ProcessID))
        {
            _ = EnqueueEvent("File", "FileCreate", data.ProcessID, data.ProcessName, data.FileName, data.TimeStamp);
        }
    }

    private void OnFileIOFileDelete(Microsoft.Diagnostics.Tracing.Parsers.Kernel.FileIONameTraceData data)
    {
        if (_processTracker.IsTracked(data.ProcessID))
        {
            _ = EnqueueEvent("File", "FileDelete", data.ProcessID, data.ProcessName, data.FileName, data.TimeStamp);
        }
    }

    private void OnFileIOName(Microsoft.Diagnostics.Tracing.Parsers.Kernel.FileIONameTraceData data)
    {
        if (_processTracker.IsTracked(data.ProcessID))
        {
            _ = EnqueueEvent("File", "Name", data.ProcessID, data.ProcessName, data.FileName, data.TimeStamp);
        }
    }

    private void OnDiskIORead(Microsoft.Diagnostics.Tracing.Parsers.Kernel.DiskIOTraceData data)
    {
        if (_processTracker.IsTracked(data.ProcessID))
        {
            _ = EnqueueEvent("File", "DiskRead", data.ProcessID, data.ProcessName, null, data.TimeStamp);
        }
    }

    private void OnDiskIOWrite(Microsoft.Diagnostics.Tracing.Parsers.Kernel.DiskIOTraceData data)
    {
        if (_processTracker.IsTracked(data.ProcessID))
        {
            _ = EnqueueEvent("File", "DiskWrite", data.ProcessID, data.ProcessName, null, data.TimeStamp);
        }
    }

    private void OnRegistryOpen(Microsoft.Diagnostics.Tracing.Parsers.Kernel.RegistryTraceData data)
    {
        if (_processTracker.IsTracked(data.ProcessID))
        {
            _ = EnqueueEvent("Registry", "Open", data.ProcessID, data.ProcessName, data.KeyName, data.TimeStamp);
        }
    }

    private void OnRegistrySetValue(Microsoft.Diagnostics.Tracing.Parsers.Kernel.RegistryTraceData data)
    {
        if (_processTracker.IsTracked(data.ProcessID))
        {
            _ = EnqueueEvent("Registry", "SetValue", data.ProcessID, data.ProcessName, data.KeyName, data.TimeStamp);
        }
    }

    private void OnRegistryCreate(Microsoft.Diagnostics.Tracing.Parsers.Kernel.RegistryTraceData data)
    {
        if (_processTracker.IsTracked(data.ProcessID))
        {
            _ = EnqueueEvent("Registry", "Create", data.ProcessID, data.ProcessName, data.KeyName, data.TimeStamp);
        }
    }

    private void OnRegistryDelete(Microsoft.Diagnostics.Tracing.Parsers.Kernel.RegistryTraceData data)
    {
        if (_processTracker.IsTracked(data.ProcessID))
        {
            _ = EnqueueEvent("Registry", "Delete", data.ProcessID, data.ProcessName, data.KeyName, data.TimeStamp);
        }
    }

    private void OnRegistryQuery(Microsoft.Diagnostics.Tracing.Parsers.Kernel.RegistryTraceData data)
    {
        if (_processTracker.IsTracked(data.ProcessID))
        {
            _ = EnqueueEvent("Registry", "Query", data.ProcessID, data.ProcessName, data.KeyName, data.TimeStamp);
        }
    }

    private void OnRegistryQueryValue(Microsoft.Diagnostics.Tracing.Parsers.Kernel.RegistryTraceData data)
    {
        if (_processTracker.IsTracked(data.ProcessID))
        {
            _ = EnqueueEvent("Registry", "QueryValue", data.ProcessID, data.ProcessName, data.KeyName, data.TimeStamp);
        }
    }

    private void OnRegistryEnumerateKey(Microsoft.Diagnostics.Tracing.Parsers.Kernel.RegistryTraceData data)
    {
        if (_processTracker.IsTracked(data.ProcessID))
        {
            _ = EnqueueEvent("Registry", "EnumerateKey", data.ProcessID, data.ProcessName, data.KeyName, data.TimeStamp);
        }
    }

    private void OnRegistryEnumerateValueKey(Microsoft.Diagnostics.Tracing.Parsers.Kernel.RegistryTraceData data)
    {
        if (_processTracker.IsTracked(data.ProcessID))
        {
            _ = EnqueueEvent("Registry", "EnumerateValueKey", data.ProcessID, data.ProcessName, data.KeyName, data.TimeStamp);
        }
    }

    private void OnImageLoad(Microsoft.Diagnostics.Tracing.Parsers.Kernel.ImageLoadTraceData data)
    {
        if (_processTracker.IsTracked(data.ProcessID))
        {
            _ = EnqueueEvent("Image", "Load", data.ProcessID, data.ProcessName, data.FileName, data.TimeStamp);
        }
    }

    private void OnProcessStart(Microsoft.Diagnostics.Tracing.Parsers.Kernel.ProcessTraceData data)
    {
        if (_processTracker.IsTracked(data.ProcessID))
        {
            _ = EnqueueEvent("Process", "Start", data.ProcessID, data.ProcessName, data.CommandLine, data.TimeStamp);
        }
    }

    private void OnProcessStop(Microsoft.Diagnostics.Tracing.Parsers.Kernel.ProcessTraceData data)
    {
        if (_processTracker.IsTracked(data.ProcessID))
        {
            _ = EnqueueEvent("Process", "Stop", data.ProcessID, data.ProcessName, null, data.TimeStamp);
        }
    }

    private Task EnqueueEvent(string type, string op, int pid, string processName, string? path, DateTime timestamp)
    {
        // Fire and forget to avoid blocking ETW callback
        return Task.Run(async () =>
        {
            try
            {
                var eventRecord = new EventRecord
                {
                    SessionId = _sessionId,
                    Pid = pid,
                    ProcessName = processName ?? "unknown",
                    Timestamp = timestamp,
                    Type = type,
                    Op = op,
                    Path = path,
                    Source = "ETW"
                };

                await _eventIngestor.EnqueueEventRecordAsync(eventRecord);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to enqueue event");
            }
        });
    }

    private Task LogSystemEventAsync(string message)
    {
        return Task.Run(async () =>
        {
            try
            {
                var eventRecord = new EventRecord
                {
                    SessionId = _sessionId,
                    Pid = 0,
                    ProcessName = "System",
                    Timestamp = DateTime.UtcNow,
                    Type = "System",
                    Op = "Info",
                    Path = message,
                    Source = "ProcWatch"
                };

                await _eventIngestor.EnqueueEventRecordAsync(eventRecord);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to log system event");
            }
        });
    }

    public void Dispose()
    {
        _cts?.Cancel();
        _etwSession?.Dispose();
        _cts?.Dispose();

        if (_processingTask != null)
        {
            try
            {
                _processingTask.Wait(TimeSpan.FromSeconds(5));
            }
            catch (AggregateException)
            {
                // Expected during cancellation
            }
        }
    }
}
