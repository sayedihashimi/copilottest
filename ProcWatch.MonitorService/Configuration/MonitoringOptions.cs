namespace ProcWatch.MonitorService.Configuration;

public class MonitoringOptions
{
    public int TargetPid { get; set; }
    public string? ProcessName { get; set; }
    public string DatabasePath { get; set; } = string.Empty;
    public int IntervalMs { get; set; } = 1000;
    public int MaxEvents { get; set; } = -1;
    public bool IncludeChildren { get; set; } = true;
    public bool NoConsole { get; set; }
}
