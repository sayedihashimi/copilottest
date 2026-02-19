namespace ProcWatch.MonitorService.Data;

/// <summary>
/// Represents a monitoring session
/// </summary>
public class MonitoredSession
{
    public int Id { get; set; }
    
    /// <summary>
    /// When monitoring started
    /// </summary>
    public DateTime StartTime { get; set; }
    
    /// <summary>
    /// When monitoring ended (null if still running)
    /// </summary>
    public DateTime? EndTime { get; set; }
    
    /// <summary>
    /// Target process ID (primary target)
    /// </summary>
    public int TargetPid { get; set; }
    
    /// <summary>
    /// Target process name
    /// </summary>
    public string TargetProcessName { get; set; } = string.Empty;
    
    /// <summary>
    /// Whether child processes were monitored
    /// </summary>
    public bool MonitorChildren { get; set; }
    
    /// <summary>
    /// Database file path
    /// </summary>
    public string DatabasePath { get; set; } = string.Empty;
    
    /// <summary>
    /// Hostname where monitoring occurred
    /// </summary>
    public string Hostname { get; set; } = string.Empty;
    
    /// <summary>
    /// All processes monitored in this session
    /// </summary>
    public List<ProcessInstance> ProcessInstances { get; set; } = new();
    
    /// <summary>
    /// All events captured in this session
    /// </summary>
    public List<EventRecord> EventRecords { get; set; } = new();
    
    /// <summary>
    /// All stats samples in this session
    /// </summary>
    public List<StatsSample> StatsSamples { get; set; } = new();
}
