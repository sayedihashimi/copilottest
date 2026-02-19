using System.Runtime.Versioning;
using Microsoft.Diagnostics.Tracing;
using Microsoft.Diagnostics.Tracing.Parsers;
using Microsoft.Diagnostics.Tracing.Parsers.Kernel;
using Microsoft.Diagnostics.Tracing.Session;
using Microsoft.Extensions.Logging;
using ProcWatch.MonitorService.Data;

namespace ProcWatch.MonitorService.Services;

[SupportedOSPlatform("windows")]
public class EtwMonitor : IDisposable
{
    private readonly ProcessMonitor _processMonitor;
    private readonly ILogger<EtwMonitor> _logger;
    private readonly TraceEventSession? _session;
    private readonly HashSet<int> _targetPids;
    private readonly Task? _processingTask;
    private readonly CancellationTokenSource _cts = new();
    private bool _isAdmin;

    public EtwMonitor(
        ProcessMonitor processMonitor,
        ILogger<EtwMonitor> logger,
        HashSet<int> targetPids)
    {
        _processMonitor = processMonitor;
        _logger = logger;
        _targetPids = targetPids;
        _isAdmin = IsAdministrator();

        if (!_isAdmin)
        {
            _logger.LogWarning("Not running as administrator. ETW monitoring will be limited. " +
                "Running in stats-only mode.");
            
            _processMonitor.RecordEvent(
                targetPids.First(),
                EventType.System,
                "ETW_Limited",
                details: "Not running as administrator - ETW capture disabled");
            
            return;
        }

        try
        {
            // Stop any existing kernel sessions
            var existingSessions = TraceEventSession.GetActiveSessionNames()
                .Where(name => name.StartsWith("ProcWatch-", StringComparison.OrdinalIgnoreCase) ||
                               name.Equals("NT Kernel Logger", StringComparison.OrdinalIgnoreCase))
                .ToList();

            foreach (var sessionName in existingSessions)
            {
                try
                {
                    TraceEventSession.GetActiveSession(sessionName)?.Dispose();
                    _logger.LogInformation("Stopped existing ETW session: {SessionName}", sessionName);
                }
                catch
                {
                    // Ignore errors stopping old sessions
                }
            }

            // Create new session with kernel logger
            // Note: For kernel events, we must use the special kernel session name
            var kernelSessionName = KernelTraceEventParser.KernelSessionName;
            
            // Stop kernel session if it exists
            try
            {
                var existing = TraceEventSession.GetActiveSession(kernelSessionName);
                existing?.Stop();
                existing?.Dispose();
            }
            catch
            {
                // Ignore
            }

            _session = new TraceEventSession(kernelSessionName);
            
            // Enable kernel provider FIRST (required before accessing Source)
            _session.EnableKernelProvider(
                KernelTraceEventParser.Keywords.FileIOInit |
                KernelTraceEventParser.Keywords.FileIO |
                KernelTraceEventParser.Keywords.Registry |
                KernelTraceEventParser.Keywords.NetworkTCPIP |
                KernelTraceEventParser.Keywords.ImageLoad);
            
            // Now set up event handlers (after EnableKernelProvider)
            SetupEventHandlers();

            _processingTask = Task.Run(() => ProcessEventsAsync(_cts.Token));
            
            _logger.LogInformation("ETW monitoring started with kernel session");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to initialize ETW session. Continuing in stats-only mode.");
            
            _processMonitor.RecordEvent(
                targetPids.First(),
                EventType.System,
                "ETW_Failed",
                details: $"ETW initialization failed: {ex.Message}");
        }
    }

    public bool IsEtwEnabled => _session != null && _isAdmin;

    private void SetupEventHandlers()
    {
        if (_session == null) return;

        var source = _session.Source;
        var kernel = source.Kernel;

        // File I/O
        kernel.FileIORead += data =>
        {
            if (ShouldCapture(data.ProcessID))
            {
                _processMonitor.RecordEvent(
                    data.ProcessID,
                    EventType.FileRead,
                    "Read",
                    path: data.FileName,
                    bytesTransferred: data.IoSize);
            }
        };

        kernel.FileIOWrite += data =>
        {
            if (ShouldCapture(data.ProcessID))
            {
                _processMonitor.RecordEvent(
                    data.ProcessID,
                    EventType.FileWrite,
                    "Write",
                    path: data.FileName,
                    bytesTransferred: data.IoSize);
            }
        };

        // Registry
        kernel.RegistryOpen += data =>
        {
            if (ShouldCapture(data.ProcessID))
            {
                _processMonitor.RecordEvent(
                    data.ProcessID,
                    EventType.RegistryRead,
                    "Open",
                    path: data.KeyName);
            }
        };

        kernel.RegistrySetValue += data =>
        {
            if (ShouldCapture(data.ProcessID))
            {
                _processMonitor.RecordEvent(
                    data.ProcessID,
                    EventType.RegistryWrite,
                    "SetValue",
                    path: data.KeyName);
            }
        };

        // Network TCP
        kernel.TcpIpConnect += data =>
        {
            if (ShouldCapture(data.ProcessID))
            {
                _processMonitor.RecordEvent(
                    data.ProcessID,
                    EventType.NetworkConnect,
                    "Connect",
                    path: $"{data.daddr}:{data.dport}",
                    details: $"From {data.saddr}:{data.sport}");
            }
        };

        kernel.TcpIpRecv += data =>
        {
            if (ShouldCapture(data.ProcessID))
            {
                _processMonitor.RecordEvent(
                    data.ProcessID,
                    EventType.NetworkReceive,
                    "Receive",
                    path: $"{data.daddr}:{data.dport}",
                    bytesTransferred: data.size);
            }
        };

        kernel.TcpIpSend += data =>
        {
            if (ShouldCapture(data.ProcessID))
            {
                _processMonitor.RecordEvent(
                    data.ProcessID,
                    EventType.NetworkSend,
                    "Send",
                    path: $"{data.daddr}:{data.dport}",
                    bytesTransferred: data.size);
            }
        };

        // Image Load (DLL/Module)
        kernel.ImageLoad += data =>
        {
            if (ShouldCapture(data.ProcessID))
            {
                _processMonitor.RecordEvent(
                    data.ProcessID,
                    EventType.ImageLoad,
                    "Load",
                    path: data.FileName,
                    details: $"Base: 0x{data.ImageBase:X}, Size: {data.ImageSize}");
            }
        };
    }

    private bool ShouldCapture(int pid)
    {
        return _targetPids.Contains(pid);
    }

    private async Task ProcessEventsAsync(CancellationToken ct)
    {
        try
        {
            await Task.Run(() => _session?.Source.Process(), ct);
        }
        catch (OperationCanceledException)
        {
            _logger.LogInformation("ETW event processing cancelled.");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error processing ETW events.");
        }
    }

    private bool IsAdministrator()
    {
        try
        {
            var identity = System.Security.Principal.WindowsIdentity.GetCurrent();
            var principal = new System.Security.Principal.WindowsPrincipal(identity);
            return principal.IsInRole(System.Security.Principal.WindowsBuiltInRole.Administrator);
        }
        catch
        {
            return false;
        }
    }

    public void Dispose()
    {
        _cts.Cancel();
        _session?.Dispose();
        _processingTask?.Wait(TimeSpan.FromSeconds(2));
        _cts.Dispose();
    }
}
