namespace ProcWatch.MonitorService.Configuration;

public class MonitoringOptions
{
    public Guid SessionId { get; set; }
    public int TargetPid { get; set; }
    public string ProcessName { get; set; } = string.Empty;
    public bool IncludeChildren { get; set; } = true;
    public int IntervalMs { get; set; } = 1000;
    public int MaxEvents { get; set; } = 100000;
    public string DatabasePath { get; set; } = string.Empty;
}
