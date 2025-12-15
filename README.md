# ProcWatch - Windows Process Monitoring Tool

ProcWatch is a Windows-only monitoring tool built with .NET 10 and Aspire 13 that captures comprehensive process activity including file I/O, registry operations, network activity, CPU/memory usage, and more.

## Features

- **ETW-based Event Tracing**: Captures file I/O, registry operations, DLL loads, and process events using Event Tracing for Windows
- **Process Tree Monitoring**: Tracks target process and all child processes by default
- **Real-time Stats Sampling**: Monitors CPU, memory, handles, and threads at configurable intervals
- **Live Console Dashboard**: Beautiful terminal UI with real-time updates using Spectre.Console
- **SQLite Persistence**: All data stored in SQLite database with EF Core migrations
- **Graceful Shutdown**: Ctrl+C handling with summary statistics

## Requirements

- Windows 10 version 1809 or later (10.0.17763.0)
- .NET 10 SDK
- Administrator privileges (for ETW tracing)

## Architecture

### Projects

- **ProcWatch.AppHost**: Aspire orchestration host
- **ProcWatch.ServiceDefaults**: Shared Aspire service defaults
- **ProcWatch.MonitorService**: Core monitoring services and data layer
- **ProcWatch.Cli**: Command-line interface with dashboard

### Components

#### Data Layer
- `MonitoredSession`: Session metadata
- `ProcessInstance`: Tracked processes
- `EventRecord`: Captured events (file, registry, image, process)
- `StatsSample`: Performance metrics snapshots

#### Services
- **ProcessTreeTracker**: WMI-based process tree discovery and tracking
- **StatsSampler**: Periodic CPU/memory/handles/threads sampling
- **EventIngestor**: Channel-based batching for high-performance writes
- **EtwMonitor**: ETW kernel provider event capture
- **MigrationService**: Automatic EF Core migrations

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

```powershell
# Automatically selects the newest instance
.\ProcWatch.Cli\bin\Debug\net10.0\ProcWatch.Cli.exe monitor --process notepad
```

### Advanced Options

```powershell
.\ProcWatch.Cli.exe monitor `
    --pid 1234 `
    --db myapp.sqlite `
    --interval-ms 500 `
    --max-events 50000 `
    --no-children
```

### Command-Line Arguments

| Argument | Description | Default |
|----------|-------------|---------|
| `--pid <PID>` | Target process ID | Required (if no --process) |
| `--process <name>` | Target process name | Required (if no --pid) |
| `--db <path>` | Database file path | `procwatch-<pid>-<timestamp>.sqlite` |
| `--interval-ms <n>` | Stats sampling interval in milliseconds | 1000 |
| `--max-events <n>` | Maximum events to capture | 100000 |
| `--no-console` | Disable live dashboard | false |
| `--no-children` | Don't monitor child processes | false |

## Dashboard

When running with console output (default), ProcWatch displays a live dashboard with:

- **Header**: Session ID and runtime duration
- **Statistics Panel**: Event counts, latest CPU/memory metrics
- **Events Panel**: Recent 10 events color-coded by type
- **Footer**: Instructions

Press **Ctrl+C** to stop monitoring and see summary statistics.

## Event Types Captured

### File I/O
- Read, Write operations
- Create, Delete operations
- File handle to path mapping
- Disk-level I/O

### Registry
- Open, Query, QueryValue
- Create, Delete
- SetValue
- EnumerateKey, EnumerateValueKey

### Image Loading
- DLL and EXE loads

### Process
- Start and Stop events

## Data Storage

All data is persisted to a SQLite database with the following optimizations:

- **Batched writes**: Events are queued and written in batches for performance
- **Indexed queries**: Key columns are indexed for fast retrieval
- **EF Core**: Full migration support with automatic schema updates

### Querying the Database

You can query the SQLite database directly:

```sql
-- Get all file operations
SELECT Timestamp, ProcessName, Op, Path 
FROM EventRecords 
WHERE Type = 'File' 
ORDER BY Timestamp DESC;

-- Get CPU usage over time
SELECT Timestamp, ProcessName, CpuPercent, WorkingSetBytes/1024/1024 as WorkingSetMB
FROM StatsSamples 
ORDER BY Timestamp;
```

## Permissions

- **Regular User**: Stats sampling only (CPU, memory, handles, threads)
- **Administrator**: Full ETW tracing + stats sampling

When running without elevation, ProcWatch automatically falls back to stats-only mode.

## Limitations

- Windows-only (by design)
- ETW requires administrator privileges
- Network monitoring not yet implemented
- Database can grow large with high event volumes

## Performance

- Channel-based architecture prevents blocking
- Batched database writes (100 events or 2 second timeout)
- Bounded queues with drop-oldest strategy
- Minimal CPU overhead in monitored processes

## Troubleshooting

### "Process with PID X not found"
The target process has exited or the PID is invalid.

### "Not running with elevation"
ETW tracing requires administrator privileges. Run as administrator or accept stats-only mode.

### Database file locked
Another instance may be accessing the database. Ensure unique database paths or wait for previous session to complete.

## Architecture Decisions

Based on learnings from previous implementations:

### ETW Keyword Combination
All ETW keywords must be combined in a single `EnableKernelProvider()` call using bitwise OR. Multiple calls overwrite previous settings.

```csharp
// CORRECT
session.EnableKernelProvider(
    KernelTraceEventParser.Keywords.FileIO | 
    KernelTraceEventParser.Keywords.Registry | 
    // ... more keywords
);

// WRONG - only last call takes effect
session.EnableKernelProvider(KernelTraceEventParser.Keywords.FileIO);
session.EnableKernelProvider(KernelTraceEventParser.Keywords.Registry);
```

### Logging with Spectre.Console
Console logging interferes with the live UI. Use Serilog with file sink only:

```csharp
Log.Logger = new LoggerConfiguration()
    .WriteTo.File(logPath)
    .CreateLogger();

builder.Logging.ClearProviders();
builder.Services.AddSerilog();
```

### EF Core Migrations
Always use `MigrateAsync()`, never `EnsureCreatedAsync()` with migrations:

```csharp
// CORRECT
await dbContext.Database.MigrateAsync(cancellationToken);

// WRONG - conflicts with migrations
await dbContext.Database.EnsureCreatedAsync(cancellationToken);
```

### Fire-and-Forget in ETW Callbacks
ETW callbacks must not block. Use Task.Run for async operations:

```csharp
_ = Task.Run(async () => {
    await _eventIngestor.EnqueueEventRecordAsync(eventRecord);
});
```

## Development

### Adding New Event Types

1. Add event handler in `EtwMonitor.cs`
2. Subscribe in `StartEtwSession()`
3. Add keyword to `EnableKernelProvider()` if needed
4. Update event type enum in `EventRecord` entity

### Modifying Schema

1. Update entity classes in `Data/Entities/`
2. Run `dotnet ef migrations add <MigrationName>` from MonitorService project
3. Migrations apply automatically on next run

## Credits

Built using:
- [.NET 10](https://dotnet.microsoft.com/)
- [Aspire 13](https://aspire.dev/)
- [TraceEvent](https://github.com/microsoft/perfview/tree/main/src/TraceEvent)
- [EF Core](https://docs.microsoft.com/ef/)
- [Spectre.Console](https://spectreconsole.net/)
- [Serilog](https://serilog.net/)

## License

See LICENSE file for details.
