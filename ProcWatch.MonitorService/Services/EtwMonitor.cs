using System.Runtime.Versioning;
using System.Security.Principal;
using System.Text.Json;
using Microsoft.Diagnostics.Tracing.Parsers;
using Microsoft.Diagnostics.Tracing.Session;
using ProcWatch.MonitorService.Configuration;
using ProcWatch.MonitorService.Data.Entities;

namespace ProcWatch.MonitorService.Services;

[SupportedOSPlatform("windows")]
public class EtwMonitor : IDisposable
{
    private readonly MonitoringOptions _options;
    private readonly ProcessTreeTracker _processTracker;
    private readonly EventIngestor _eventIngestor;
    private readonly ILogger<EtwMonitor> _logger;
    private TraceEventSession? _session;
    private Task? _processingTask;
    private bool _isElevated;

    public bool IsEnabled => _isElevated && _session != null;

    public EtwMonitor(
        MonitoringOptions options,
        ProcessTreeTracker processTracker,
        EventIngestor eventIngestor,
        ILogger<EtwMonitor> logger)
    {
        _options = options;
        _processTracker = processTracker;
        _eventIngestor = eventIngestor;
        _logger = logger;
        
        CheckElevation();
    }

    private void CheckElevation()
    {
        try
        {
            using var identity = WindowsIdentity.GetCurrent();
            var principal = new WindowsPrincipal(identity);
            _isElevated = principal.IsInRole(WindowsBuiltInRole.Administrator);
            
            if (!_isElevated)
            {
                _logger.LogWarning("Not running as administrator. ETW monitoring will be disabled.");
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Error checking elevation status");
            _isElevated = false;
        }
    }

    public void Start()
    {
        if (!_isElevated)
        {
            // Record limitation
            var systemEvent = new EventRecord
            {
                SessionId = _options.SessionId,
                Pid = _options.TargetPid,
                ProcessName = "system",
                Timestamp = DateTime.UtcNow,
                Type = "System",
                Op = "Limitation",
                Source = "EtwMonitor",
                JsonPayload = JsonSerializer.Serialize(new 
                { 
                    Message = "ETW monitoring disabled - requires administrator privileges",
                    Mode = "Stats + Snapshot Only"
                })
            };
            _eventIngestor.EnqueueEventAsync(systemEvent).Wait();
            return;
        }

        try
        {
            // Create unique session name using GUID
            var sessionName = $"ProcWatch-{Guid.NewGuid():N}";
            _session = new TraceEventSession(sessionName);

            // CRITICAL: Combine all kernel providers in single call with bitwise OR
            _session.EnableKernelProvider(
                KernelTraceEventParser.Keywords.FileIO |
                KernelTraceEventParser.Keywords.FileIOInit |
                KernelTraceEventParser.Keywords.Registry |
                KernelTraceEventParser.Keywords.ImageLoad |
                KernelTraceEventParser.Keywords.Process);

            SetupEventHandlers();

            _processingTask = Task.Run(() =>
            {
                try
                {
                    _session.Source.Process();
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Error in ETW event processing");
                }
            });

            _logger.LogInformation("ETW monitoring started with session: {SessionName}", sessionName);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error starting ETW monitoring");
            _session?.Dispose();
            _session = null;
        }
    }

    private void SetupEventHandlers()
    {
        if (_session == null) return;

        var kernel = new KernelTraceEventParser(_session.Source);

        // File I/O events
        kernel.FileIORead += evt =>
        {
            if (!_processTracker.IsTracking(evt.ProcessID)) return;
            
            var eventRecord = new EventRecord
            {
                SessionId = _options.SessionId,
                Pid = evt.ProcessID,
                ProcessName = evt.ProcessName,
                Timestamp = evt.TimeStamp.ToUniversalTime(),
                Type = "File",
                Op = "Read",
                Path = evt.FileName,
                JsonPayload = JsonSerializer.Serialize(new { evt.IoSize, evt.Offset })
            };
            _eventIngestor.EnqueueEventAsync(eventRecord).Wait();
        };

        kernel.FileIOWrite += evt =>
        {
            if (!_processTracker.IsTracking(evt.ProcessID)) return;
            
            var eventRecord = new EventRecord
            {
                SessionId = _options.SessionId,
                Pid = evt.ProcessID,
                ProcessName = evt.ProcessName,
                Timestamp = evt.TimeStamp.ToUniversalTime(),
                Type = "File",
                Op = "Write",
                Path = evt.FileName,
                JsonPayload = JsonSerializer.Serialize(new { evt.IoSize, evt.Offset })
            };
            _eventIngestor.EnqueueEventAsync(eventRecord).Wait();
        };

        // Registry events
        kernel.RegistryOpen += evt =>
        {
            if (!_processTracker.IsTracking(evt.ProcessID)) return;
            
            var eventRecord = new EventRecord
            {
                SessionId = _options.SessionId,
                Pid = evt.ProcessID,
                ProcessName = evt.ProcessName,
                Timestamp = evt.TimeStamp.ToUniversalTime(),
                Type = "Registry",
                Op = "Open",
                Path = evt.KeyName,
                JsonPayload = JsonSerializer.Serialize(new { evt.Status })
            };
            _eventIngestor.EnqueueEventAsync(eventRecord).Wait();
        };

        kernel.RegistrySetValue += evt =>
        {
            if (!_processTracker.IsTracking(evt.ProcessID)) return;
            
            var eventRecord = new EventRecord
            {
                SessionId = _options.SessionId,
                Pid = evt.ProcessID,
                ProcessName = evt.ProcessName,
                Timestamp = evt.TimeStamp.ToUniversalTime(),
                Type = "Registry",
                Op = "SetValue",
                Path = evt.ValueName,
                JsonPayload = JsonSerializer.Serialize(new { evt.KeyName })
            };
            _eventIngestor.EnqueueEventAsync(eventRecord).Wait();
        };

        // Image load events
        kernel.ImageLoad += evt =>
        {
            if (!_processTracker.IsTracking(evt.ProcessID)) return;
            
            var eventRecord = new EventRecord
            {
                SessionId = _options.SessionId,
                Pid = evt.ProcessID,
                ProcessName = evt.ProcessName,
                Timestamp = evt.TimeStamp.ToUniversalTime(),
                Type = "Image",
                Op = "Load",
                Path = evt.FileName,
                JsonPayload = JsonSerializer.Serialize(new { evt.ImageSize, evt.ImageBase })
            };
            _eventIngestor.EnqueueEventAsync(eventRecord).Wait();
        };
    }

    public void Dispose()
    {
        if (_session != null)
        {
            _session.Stop();
            _session.Dispose();
            _session = null;
        }

        _processingTask?.Wait(TimeSpan.FromSeconds(5));
    }
}
