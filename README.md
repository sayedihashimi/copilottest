# ProcWatch - Windows Process Monitoring Tool

ProcWatch is a Windows-only monitoring tool built with .NET 10 and Aspire 13 that tracks process activity including file I/O, registry operations, DLL loads, CPU/memory usage, and more.

## Features

- 📊 **Real-time Monitoring**: Live console dashboard with process statistics
- 🔍 **Event Tracking**: Captures file I/O, registry operations, image loads via ETW
- 📈 **Performance Metrics**: CPU, memory, handles, threads sampling
- 👨‍👩‍👧‍👦 **Child Process Support**: Automatically tracks child processes
- 💾 **SQLite Persistence**: All data stored in SQLite database with EF Core
- 🎨 **Beautiful UI**: Polished Spectre.Console live dashboard

## Requirements

- **Windows 10 1809+** (Build 17763 or higher)
- **.NET 10 SDK**
- **Administrator privileges** (for ETW event capture; optional for stats-only mode)

## Quick Start

### Build

```powershell
dotnet build
```

### Run

Monitor a process by PID:
```powershell
.\ProcWatch.Cli\bin\Debug\net10.0\ProcWatch.Cli.exe monitor --pid 1234
```

Monitor a process by name (picks newest instance):
```powershell
.\ProcWatch.Cli\bin\Debug\net10.0\ProcWatch.Cli.exe monitor --process notepad
```

## Usage

```
procwatch monitor --pid <PID> [options]
procwatch monitor --process <name> [options]
```

### Options

| Option | Description | Default |
|--------|-------------|---------|
| `--pid <PID>` | Target process ID | Required* |
| `--process <name>` | Target process name | Required* |
| `--db <path>` | Database file path | `procwatch-<pid>-<timestamp>.sqlite` |
| `--interval-ms <ms>` | Stats sampling interval | 1000 |
| `--max-events <n>` | Maximum events to capture | 100000 |
| `--no-console` | Disable live dashboard | false |
| `--no-children` | Don't monitor child processes | false |

*Either `--pid` or `--process` must be specified. If both are provided, `--pid` takes precedence.

## Examples

### Monitor Notepad with all defaults
```powershell
.\ProcWatch.Cli.exe monitor --process notepad
```

### Monitor specific PID with custom database path
```powershell
.\ProcWatch.Cli.exe monitor --pid 5678 --db C:\monitoring\myapp.sqlite
```

### Monitor without child processes, 500ms interval
```powershell
.\ProcWatch.Cli.exe monitor --pid 1234 --no-children --interval-ms 500
```

### Background monitoring without console UI
```powershell
.\ProcWatch.Cli.exe monitor --pid 1234 --no-console
```

### Run as Administrator (for full ETW events)
```powershell
Start-Process -Verb RunAs .\ProcWatch.Cli.exe -ArgumentList "monitor --pid 1234"
```

## Architecture

### Projects

- **ProcWatch.AppHost**: Aspire orchestration (minimal for this single-service app)
- **ProcWatch.ServiceDefaults**: Shared Aspire service defaults
- **ProcWatch.MonitorService**: Core monitoring engine
  - `ProcessTreeTracker`: WMI-based process discovery and tracking
  - `StatsSampler`: Periodic CPU/memory/handles/threads sampling
  - `EventIngestor`: Channel-based batched event persistence
  - `EtwMonitor`: TraceEvent-based ETW kernel event capture
  - `Worker`: Main orchestration and lifecycle management
- **ProcWatch.Cli**: Command-line interface with Spectre.Console dashboard

### Data Model

**MonitoredSession**: Top-level session metadata
- SessionId, StartTime, EndTime, TargetPid, ProcessName, etc.

**ProcessInstance**: Tracked process instances
- Pid, Name, CommandLine, StartTime, EndTime, ParentPid

**EventRecord**: Captured events
- Type: File, Registry, Image, Network, System
- Op: Read, Write, Load, Connect, etc.
- Path, Endpoints, JsonPayload

**StatsSample**: Performance samples
- CpuPercent, WorkingSetBytes, HandleCount, ThreadCount

### Technology Stack

| Component | Technology |
|-----------|-----------|
| Runtime | .NET 10 |
| Orchestration | Aspire 13 |
| ETW | Microsoft.Diagnostics.Tracing.TraceEvent |
| Database | EF Core 10 + SQLite |
| UI | Spectre.Console |
| CLI | Manual argument parsing |
| Process Discovery | System.Management (WMI) |
| Logging | Serilog (file only) |

## Graceful Degradation

If not running as Administrator:
- ETW event capture is disabled
- Stats sampling continues to work
- A system event is recorded explaining the limitation

## Stopping

Press **Ctrl+C** to gracefully stop monitoring:
- ETW session is stopped cleanly
- All pending events are flushed to database
- Summary statistics are displayed
- Database path is shown

## Database Schema

SQLite database contains:
- `MonitoredSessions`: Session metadata
- `ProcessInstances`: Process lifecycle records
- `EventRecords`: All captured events (with indexes on SessionId+Timestamp, SessionId+Type, Path)
- `StatsSamples`: Performance samples (with index on SessionId+Timestamp)

## Performance Considerations

- **Channel Batching**: Events are batched (100 events or 2 seconds) to minimize database overhead
- **Bounded Channel**: 10,000 event capacity with DropOldest policy to prevent memory issues
- **Disabled AutoDetectChanges**: EF change tracking disabled during batch inserts
- **Indexed Queries**: Key query patterns have indexes for fast retrieval

## Known Limitations

- **Windows-only**: Uses WMI and ETW which are Windows-specific
- **ETW Requires Elevation**: Full event capture needs administrator privileges
- **Network Monitoring**: Not implemented (ETW TCPIP provider could be added)
- **Dashboard Polling**: Queries database every second (could optimize with in-memory cache)

## Troubleshooting

### "Target process with PID X not found"
The specified PID doesn't exist. Check with `Get-Process` in PowerShell.

### "No process found with name 'X'"
No running process matches that name. Process names are case-sensitive.

### No ETW events captured
Run as Administrator for full ETW kernel event access.

### Database locked
Ensure no other tools have the SQLite file open.

## Development

### Prerequisites
- .NET 10 SDK
- Windows 10 1809+ or Windows 11
- Visual Studio 2022 or VS Code

### Build
```powershell
dotnet build
```

### Generate Migration
```powershell
cd ProcWatch.MonitorService
dotnet ef migrations add MigrationName
```

### Run Tests
```powershell
dotnet test
```

## License

See LICENSE file for details.

## Credits

Built with:
- [Aspire](https://aspire.dev/) - Cloud-ready application stack
- [TraceEvent](https://github.com/microsoft/perfview) - ETW event processing
- [Spectre.Console](https://spectreconsole.net/) - Beautiful console applications
- [Entity Framework Core](https://learn.microsoft.com/ef/core/) - Object-relational mapper
- [Serilog](https://serilog.net/) - Structured logging
