namespace ProcWatch.MonitorService.Data.Entities;

public class EventRecord
{
    public long Id { get; set; }
    public Guid SessionId { get; set; }
    public int Pid { get; set; }
    public string ProcessName { get; set; } = string.Empty;
    public DateTime Timestamp { get; set; }
    public string Type { get; set; } = string.Empty; // File, Registry, Network, Image, System
    public string Op { get; set; } = string.Empty;    // Read, Write, Load, Connect, etc.
    public string? Path { get; set; }
    public string? Endpoints { get; set; }
    public string? Source { get; set; }
    public string? JsonPayload { get; set; }

    // Navigation property
    public MonitoredSession Session { get; set; } = null!;
}
