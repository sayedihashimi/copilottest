namespace ProcWatch.MonitorService.Data.Entities;

public class ProcessInstance
{
    public long Id { get; set; }
    public Guid SessionId { get; set; }
    public int Pid { get; set; }
    public int? ParentPid { get; set; }
    public string ProcessName { get; set; } = string.Empty;
    public string? CommandLine { get; set; }
    public DateTime StartTime { get; set; }
    public DateTime? EndTime { get; set; }
    
    // Navigation property
    public MonitoredSession Session { get; set; } = null!;
}
