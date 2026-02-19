namespace ProcWatch.MonitorService.Data;

/// <summary>
/// Represents a monitored process instance
/// </summary>
public class ProcessInstance
{
    public int Id { get; set; }
    
    public int SessionId { get; set; }
    public MonitoredSession Session { get; set; } = null!;
    
    /// <summary>
    /// Process ID
    /// </summary>
    public int Pid { get; set; }
    
    /// <summary>
    /// Process name
    /// </summary>
    public string ProcessName { get; set; } = string.Empty;
    
    /// <summary>
    /// Full command line (if available)
    /// </summary>
    public string? CommandLine { get; set; }
    
    /// <summary>
    /// Process start time
    /// </summary>
    public DateTime StartTime { get; set; }
    
    /// <summary>
    /// Process exit time (null if still running)
    /// </summary>
    public DateTime? ExitTime { get; set; }
    
    /// <summary>
    /// Parent process ID (null if target/root)
    /// </summary>
    public int? ParentPid { get; set; }
    
    /// <summary>
    /// Whether this is the primary target process
    /// </summary>
    public bool IsTarget { get; set; }
    
    /// <summary>
    /// Events from this process
    /// </summary>
    public List<EventRecord> EventRecords { get; set; } = new();
    
    /// <summary>
    /// Stats samples from this process
    /// </summary>
    public List<StatsSample> StatsSamples { get; set; } = new();
}
