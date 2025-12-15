namespace ProcWatch.MonitorService.Configuration;

public class MonitoringOptions
{
    public int TargetPid { get; set; }
    public string ProcessName { get; set; } = string.Empty;
    public string DbPath { get; set; } = string.Empty;
    public int IntervalMs { get; set; } = 1000;
    public int MaxEvents { get; set; } = 100000;
    public bool NoConsole { get; set; }
    public bool NoChildren { get; set; }
    public Guid SessionId { get; set; } = Guid.NewGuid();
}
