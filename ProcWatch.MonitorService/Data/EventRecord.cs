namespace ProcWatch.MonitorService.Data;

/// <summary>
/// Types of events captured
/// </summary>
public enum EventType
{
    FileRead,
    FileWrite,
    RegistryRead,
    RegistryWrite,
    NetworkConnect,
    NetworkReceive,
    NetworkSend,
    ImageLoad,
    System
}

/// <summary>
/// Represents a captured event
/// </summary>
public class EventRecord
{
    public int Id { get; set; }
    
    public int SessionId { get; set; }
    public MonitoredSession Session { get; set; } = null!;
    
    public int ProcessInstanceId { get; set; }
    public ProcessInstance ProcessInstance { get; set; } = null!;
    
    /// <summary>
    /// When the event occurred
    /// </summary>
    public DateTime Timestamp { get; set; }
    
    /// <summary>
    /// Type of event
    /// </summary>
    public EventType EventType { get; set; }
    
    /// <summary>
    /// Operation performed (e.g., "Read", "Write", "Connect")
    /// </summary>
    public string Operation { get; set; } = string.Empty;
    
    /// <summary>
    /// Primary path/key/target (file path, registry key, network endpoint)
    /// </summary>
    public string? Path { get; set; }
    
    /// <summary>
    /// Additional details (JSON or text)
    /// </summary>
    public string? Details { get; set; }
    
    /// <summary>
    /// Bytes transferred (for file/network operations)
    /// </summary>
    public long? BytesTransferred { get; set; }
}
