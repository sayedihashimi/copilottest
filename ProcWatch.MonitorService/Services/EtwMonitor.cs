using System.Runtime.Versioning;
using System.Security.Principal;
using Microsoft.Diagnostics.Tracing;
using Microsoft.Diagnostics.Tracing.Parsers;
using Microsoft.Diagnostics.Tracing.Parsers.Kernel;
using Microsoft.Diagnostics.Tracing.Session;
using ProcWatch.MonitorService.Data.Entities;

namespace ProcWatch.MonitorService.Services;

[SupportedOSPlatform("windows")]
public class EtwMonitor : IDisposable
{
    private readonly ILogger<EtwMonitor> _logger;
    private readonly ProcessTreeTracker _processTracker;
    private readonly EventIngestor _eventIngestor;
    private TraceEventSession? _session;
    private Task? _processingTask;
    private CancellationTokenSource? _cts;
    private Guid _sessionId;
    private bool _isElevated;

    public EtwMonitor(
        ILogger<EtwMonitor> logger,
        ProcessTreeTracker processTracker,
        EventIngestor eventIngestor)
    {
        _logger = logger;
        _processTracker = processTracker;
        _eventIngestor = eventIngestor;
    }

    public bool Start(Guid sessionId, CancellationToken cancellationToken)
    {
        _sessionId = sessionId;
        _isElevated = CheckElevation();

        if (!_isElevated)
        {
            _logger.LogWarning("ETW monitoring requires elevation. Running in degraded mode (stats only).");
            LogSystemEvent("ETW monitoring unavailable - requires administrator privileges");
            return false;
        }

        try
        {
            _cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            var sessionName = $"ProcWatch-{sessionId:N}";
            
            _session = new TraceEventSession(sessionName);
            
            // CRITICAL: Combine all keywords in single EnableKernelProvider call
            _session.EnableKernelProvider(
                KernelTraceEventParser.Keywords.FileIO |
                KernelTraceEventParser.Keywords.Registry |
                KernelTraceEventParser.Keywords.ImageLoad |
                KernelTraceEventParser.Keywords.Process);

            // Subscribe to events
            _session.Source.Kernel.FileIORead += OnFileIORead;
            _session.Source.Kernel.FileIOWrite += OnFileIOWrite;
            _session.Source.Kernel.FileIOCreate += OnFileIOCreate;
            _session.Source.Kernel.FileIODelete += OnFileIODelete;
            _session.Source.Kernel.RegistryOpen += OnRegistryOpen;
            _session.Source.Kernel.RegistryCreate += OnRegistryCreate;
            _session.Source.Kernel.RegistryDelete += OnRegistryDelete;
            _session.Source.Kernel.RegistryQuery += OnRegistryQuery;
            _session.Source.Kernel.RegistrySetValue += OnRegistrySetValue;
            _session.Source.Kernel.RegistryQueryValue += OnRegistryQueryValue;
            _session.Source.Kernel.RegistryEnumerateKey += OnRegistryEnumerateKey;
            _session.Source.Kernel.RegistryEnumerateValueKey += OnRegistryEnumerateValueKey;
            _session.Source.Kernel.ImageLoad += OnImageLoad;

            _processingTask = Task.Run(() => _session.Source.Process(), _cts.Token);
            
            _logger.LogInformation("ETW monitoring started");
            return true;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to start ETW monitoring");
            LogSystemEvent($"ETW monitoring failed to start: {ex.Message}");
            return false;
        }
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

    private void OnFileIORead(FileIOReadWriteTraceData data)
    {
        if (_processTracker.IsTracked(data.ProcessID))
        {
            EnqueueEvent(new EventRecord
            {
                SessionId = _sessionId,
                Pid = data.ProcessID,
                ProcessName = data.ProcessName ?? "Unknown",
                Timestamp = data.TimeStamp.ToUniversalTime(),
                Type = "File",
                Op = "Read",
                Path = data.FileName,
                Source = "ETW"
            });
        }
    }

    private void OnFileIOWrite(FileIOReadWriteTraceData data)
    {
        if (_processTracker.IsTracked(data.ProcessID))
        {
            EnqueueEvent(new EventRecord
            {
                SessionId = _sessionId,
                Pid = data.ProcessID,
                ProcessName = data.ProcessName ?? "Unknown",
                Timestamp = data.TimeStamp.ToUniversalTime(),
                Type = "File",
                Op = "Write",
                Path = data.FileName,
                Source = "ETW"
            });
        }
    }

    private void OnFileIOCreate(FileIOCreateTraceData data)
    {
        if (_processTracker.IsTracked(data.ProcessID))
        {
            EnqueueEvent(new EventRecord
            {
                SessionId = _sessionId,
                Pid = data.ProcessID,
                ProcessName = data.ProcessName ?? "Unknown",
                Timestamp = data.TimeStamp.ToUniversalTime(),
                Type = "File",
                Op = "Create",
                Path = data.FileName,
                Source = "ETW"
            });
        }
    }

    private void OnFileIODelete(FileIOInfoTraceData data)
    {
        if (_processTracker.IsTracked(data.ProcessID))
        {
            EnqueueEvent(new EventRecord
            {
                SessionId = _sessionId,
                Pid = data.ProcessID,
                ProcessName = data.ProcessName ?? "Unknown",
                Timestamp = data.TimeStamp.ToUniversalTime(),
                Type = "File",
                Op = "Delete",
                Path = data.FileName,
                Source = "ETW"
            });
        }
    }

    private void OnRegistryOpen(RegistryTraceData data)
    {
        if (_processTracker.IsTracked(data.ProcessID))
        {
            EnqueueEvent(new EventRecord
            {
                SessionId = _sessionId,
                Pid = data.ProcessID,
                ProcessName = data.ProcessName ?? "Unknown",
                Timestamp = data.TimeStamp.ToUniversalTime(),
                Type = "Registry",
                Op = "Open",
                Path = data.KeyName,
                Source = "ETW"
            });
        }
    }

    private void OnRegistrySetValue(RegistryTraceData data)
    {
        if (_processTracker.IsTracked(data.ProcessID))
        {
            EnqueueEvent(new EventRecord
            {
                SessionId = _sessionId,
                Pid = data.ProcessID,
                ProcessName = data.ProcessName ?? "Unknown",
                Timestamp = data.TimeStamp.ToUniversalTime(),
                Type = "Registry",
                Op = "SetValue",
                Path = data.KeyName,
                Source = "ETW"
            });
        }
    }

    private void OnRegistryCreate(RegistryTraceData data)
    {
        if (_processTracker.IsTracked(data.ProcessID))
        {
            EnqueueEvent(new EventRecord
            {
                SessionId = _sessionId,
                Pid = data.ProcessID,
                ProcessName = data.ProcessName ?? "Unknown",
                Timestamp = data.TimeStamp.ToUniversalTime(),
                Type = "Registry",
                Op = "Create",
                Path = data.KeyName,
                Source = "ETW"
            });
        }
    }

    private void OnRegistryDelete(RegistryTraceData data)
    {
        if (_processTracker.IsTracked(data.ProcessID))
        {
            EnqueueEvent(new EventRecord
            {
                SessionId = _sessionId,
                Pid = data.ProcessID,
                ProcessName = data.ProcessName ?? "Unknown",
                Timestamp = data.TimeStamp.ToUniversalTime(),
                Type = "Registry",
                Op = "Delete",
                Path = data.KeyName,
                Source = "ETW"
            });
        }
    }

    private void OnRegistryQuery(RegistryTraceData data)
    {
        if (_processTracker.IsTracked(data.ProcessID))
        {
            EnqueueEvent(new EventRecord
            {
                SessionId = _sessionId,
                Pid = data.ProcessID,
                ProcessName = data.ProcessName ?? "Unknown",
                Timestamp = data.TimeStamp.ToUniversalTime(),
                Type = "Registry",
                Op = "Query",
                Path = data.KeyName,
                Source = "ETW"
            });
        }
    }

    private void OnRegistryQueryValue(RegistryTraceData data)
    {
        if (_processTracker.IsTracked(data.ProcessID))
        {
            EnqueueEvent(new EventRecord
            {
                SessionId = _sessionId,
                Pid = data.ProcessID,
                ProcessName = data.ProcessName ?? "Unknown",
                Timestamp = data.TimeStamp.ToUniversalTime(),
                Type = "Registry",
                Op = "QueryValue",
                Path = data.KeyName,
                Source = "ETW"
            });
        }
    }

    private void OnRegistryEnumerateKey(RegistryTraceData data)
    {
        if (_processTracker.IsTracked(data.ProcessID))
        {
            EnqueueEvent(new EventRecord
            {
                SessionId = _sessionId,
                Pid = data.ProcessID,
                ProcessName = data.ProcessName ?? "Unknown",
                Timestamp = data.TimeStamp.ToUniversalTime(),
                Type = "Registry",
                Op = "EnumerateKey",
                Path = data.KeyName,
                Source = "ETW"
            });
        }
    }

    private void OnRegistryEnumerateValueKey(RegistryTraceData data)
    {
        if (_processTracker.IsTracked(data.ProcessID))
        {
            EnqueueEvent(new EventRecord
            {
                SessionId = _sessionId,
                Pid = data.ProcessID,
                ProcessName = data.ProcessName ?? "Unknown",
                Timestamp = data.TimeStamp.ToUniversalTime(),
                Type = "Registry",
                Op = "EnumerateValueKey",
                Path = data.KeyName,
                Source = "ETW"
            });
        }
    }

    private void OnImageLoad(ImageLoadTraceData data)
    {
        if (_processTracker.IsTracked(data.ProcessID))
        {
            EnqueueEvent(new EventRecord
            {
                SessionId = _sessionId,
                Pid = data.ProcessID,
                ProcessName = data.ProcessName ?? "Unknown",
                Timestamp = data.TimeStamp.ToUniversalTime(),
                Type = "Image",
                Op = "Load",
                Path = data.FileName,
                Source = "ETW"
            });
        }
    }

    private void EnqueueEvent(EventRecord eventRecord)
    {
        // Fire-and-forget pattern to avoid blocking ETW callbacks
        _ = Task.Run(async () =>
        {
            try
            {
                await _eventIngestor.EnqueueEventRecordAsync(eventRecord, _cts?.Token ?? CancellationToken.None);
            }
            catch (Exception ex)
            {
                _logger.LogDebug(ex, "Failed to enqueue event");
            }
        });
    }

    private void LogSystemEvent(string message)
    {
        EnqueueEvent(new EventRecord
        {
            SessionId = _sessionId,
            Pid = 0,
            ProcessName = "System",
            Timestamp = DateTime.UtcNow,
            Type = "System",
            Op = "Info",
            Path = message,
            Source = "ProcWatch"
        });
    }

    public void Dispose()
    {
        _cts?.Cancel();
        _session?.Dispose();
        _processingTask?.Wait(TimeSpan.FromSeconds(2));
        _cts?.Dispose();
    }
}
