# ProcWatch

A Windows-only process monitoring tool built with .NET 10 and Aspire 13 that captures real-time process activity including file I/O, registry operations, DLL loads, and system statistics.

## Features

- **Process Tree Tracking**: Monitor a target process and all its child processes
- **ETW Event Capture**: Uses Event Tracing for Windows (ETW) to capture:
  - File operations: read, write, create, delete, file create, file delete, name mapping
  - Disk I/O operations: read, write at disk level
  - Registry operations: open, create, delete, query, set value, query value, enumerate keys
  - DLL/module loads
  - Process lifecycle events
- **System Statistics**: Periodic sampling of:
  - CPU usage percentage
  - Memory (working set, private bytes)
  - Handle and thread counts
- **SQLite Persistence**: All events and statistics stored in SQLite database using EF Core
- **Live Console Dashboard**: Beautiful real-time dashboard using Spectre.Console
- **Graceful Shutdown**: Ctrl+C handling with summary output

## Requirements

- Windows 10 version 1809+ or Windows 11
- .NET 10 SDK
- Administrator privileges (for ETW monitoring - degrades gracefully without)

## Building

```powershell
dotnet build
```

## Usage

### Monitor by Process ID

```powershell
.\ProcWatch.Cli\bin\Debug\net10.0\ProcWatch.Cli.exe monitor --pid 1234
```

### Monitor by Process Name

The tool will select the newest instance if multiple processes match:

```powershell
.\ProcWatch.Cli\bin\Debug\net10.0\ProcWatch.Cli.exe monitor --process notepad
```

### Advanced Options

```powershell
# Custom database path
.\ProcWatch.Cli.exe monitor --pid 1234 --db myapp.sqlite

# Custom sampling interval (milliseconds)
.\ProcWatch.Cli.exe monitor --pid 1234 --interval-ms 500

# Limit maximum events captured
.\ProcWatch.Cli.exe monitor --pid 1234 --max-events 10000

# Disable console UI (logs only)
.\ProcWatch.Cli.exe monitor --pid 1234 --no-console

# Don't monitor child processes
.\ProcWatch.Cli.exe monitor --pid 1234 --no-children
```

### Stopping Monitoring

Press **Ctrl+C** to gracefully stop monitoring. The tool will:
- Flush all pending events to database
- Display a summary of captured data
- Show the database file path

## Architecture

### Projects

- **ProcWatch.AppHost**: Aspire orchestration host (minimal usage in current design)
- **ProcWatch.ServiceDefaults**: Shared Aspire service defaults
- **ProcWatch.MonitorService**: Core monitoring service with:
  - EF Core data layer with migrations
  - ProcessTreeTracker: WMI-based process discovery
  - StatsSampler: Periodic CPU/memory/handle/thread sampling
  - EventIngestor: Channel-based batched writes to database
  - EtwMonitor: TraceEvent-based ETW capture with FileIO, FileIOInit, DiskFileIO, Registry, ImageLoad, and Process keywords
  - Worker: Main orchestration service
- **ProcWatch.Cli**: Command-line interface with Spectre.Console dashboard

### Database Schema

- **MonitoredSession**: Session metadata (session ID, target PID, timestamps)
- **ProcessInstance**: Tracked processes (PID, parent PID, start/end times)
- **EventRecord**: Captured events (file, registry, image loads, system events)
- **StatsSample**: Periodic statistics snapshots (CPU, memory, handles, threads)

### Key Design Patterns

- **Channel-based Ingestion**: High-frequency ETW events are queued in a bounded channel and batched for efficient database writes
- **Batch Writes**: Events are written in batches of 100 or every 2 seconds to minimize EF Core overhead
- **Graceful Degradation**: If ETW requires elevation and isn't available, the tool continues with statistics-only monitoring
- **Platform-Specific**: Uses `[SupportedOSPlatform("windows")]` attributes for Windows-specific APIs

## Limitations

- **Windows-only**: Uses Windows-specific APIs (WMI, ETW)
- **ETW requires elevation**: Run as Administrator for full ETW event capture
- **Network monitoring not implemented**: Future enhancement
- **High-frequency events**: Very busy processes may generate large databases

## Database Queries

The SQLite database can be queried directly. Example queries:

```sql
-- Get all sessions
SELECT * FROM MonitoredSessions;

-- Get events for a session
SELECT Type, Op, Path, Timestamp 
FROM EventRecords 
WHERE SessionId = 'your-session-id'
ORDER BY Timestamp DESC;

-- Get statistics timeline
SELECT Timestamp, CpuPercent, WorkingSetBytes/1024/1024 as MemoryMB
FROM StatsSamples
WHERE SessionId = 'your-session-id'
ORDER BY Timestamp;

-- File I/O summary
SELECT Path, COUNT(*) as AccessCount
FROM EventRecords
WHERE SessionId = 'your-session-id' AND Type = 'File'
GROUP BY Path
ORDER BY AccessCount DESC;
```

## Troubleshooting

### "ETW monitoring requires elevation"

Run the tool as Administrator to enable ETW event capture:

```powershell
Start-Process -Verb RunAs .\ProcWatch.Cli.exe -ArgumentList "monitor --pid 1234"
```

### Process not found

Verify the process is running:

```powershell
Get-Process | Select-Object Id, Name
```

### Database locked errors

Ensure only one instance is writing to the same database file.

## License

See LICENSE file for details.
