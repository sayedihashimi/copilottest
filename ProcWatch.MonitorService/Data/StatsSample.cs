namespace ProcWatch.MonitorService.Data;

/// <summary>
/// Periodic process statistics snapshot
/// </summary>
public class StatsSample
{
    public int Id { get; set; }
    
    public int SessionId { get; set; }
    public MonitoredSession Session { get; set; } = null!;
    
    public int ProcessInstanceId { get; set; }
    public ProcessInstance ProcessInstance { get; set; } = null!;
    
    /// <summary>
    /// When the sample was taken
    /// </summary>
    public DateTime Timestamp { get; set; }
    
    /// <summary>
    /// CPU usage percent (0-100 per core, can exceed 100 on multi-core)
    /// </summary>
    public double CpuPercent { get; set; }
    
    /// <summary>
    /// Working set memory in bytes
    /// </summary>
    public long MemoryBytes { get; set; }
    
    /// <summary>
    /// Number of handles
    /// </summary>
    public int HandleCount { get; set; }
    
    /// <summary>
    /// Number of threads
    /// </summary>
    public int ThreadCount { get; set; }
}
