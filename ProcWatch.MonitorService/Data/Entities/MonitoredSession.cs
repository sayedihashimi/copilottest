namespace ProcWatch.MonitorService.Data.Entities;

public class MonitoredSession
{
    public Guid SessionId { get; set; }
    public DateTime StartTime { get; set; }
    public DateTime? EndTime { get; set; }
    public int TargetPid { get; set; }
    public string ProcessName { get; set; } = string.Empty;
    public bool IncludeChildren { get; set; }
    public string? ArgsJson { get; set; }
    
    // Navigation properties
    public ICollection<ProcessInstance> ProcessInstances { get; set; } = new List<ProcessInstance>();
    public ICollection<EventRecord> EventRecords { get; set; } = new List<EventRecord>();
    public ICollection<StatsSample> StatsSamples { get; set; } = new List<StatsSample>();
}
