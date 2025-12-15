namespace ProcWatch.MonitorService.Data.Entities;

public class StatsSample
{
    public long Id { get; set; }
    public Guid SessionId { get; set; }
    public int Pid { get; set; }
    public string ProcessName { get; set; } = string.Empty;
    public DateTime Timestamp { get; set; }
    public double CpuPercent { get; set; }
    public long WorkingSetBytes { get; set; }
    public long PrivateBytes { get; set; }
    public int HandleCount { get; set; }
    public int ThreadCount { get; set; }

    // Navigation property
    public MonitoredSession Session { get; set; } = null!;
}
