# ProcWatch

A Windows process monitoring tool built with .NET 10 and Aspire 13 that captures comprehensive process activity including file I/O, network operations, registry access, and system statistics.

## Features

- **Real-time Process Monitoring**: Track target processes and optionally their children
- **Event Capture**: File reads/writes, network activity, registry operations, DLL/module loads
- **Live Dashboard**: Polished Spectre.Console UI with real-time stats and event feed
- **Interactive Event Browser**: Navigate through historical events with keyboard controls
- **ETW Integration**: Kernel-level event tracing (requires admin privileges)
- **SQLite Persistence**: All events and stats stored in SQLite database
- **EF Core Migrations**: Automatic database schema management

## Prerequisites

- Windows 11 or later
- .NET 10 SDK
- Administrator privileges (recommended for full ETW event capture)

## Quick Start

### Build the Solution

```powershell
dotnet build
```

### Monitor a Process by Name

```powershell
dotnet run --project ProcWatch.Cli -- monitor --process notepad
```

### Monitor a Process by PID

```powershell
dotnet run --project ProcWatch.Cli -- monitor --pid 1234
```

### View Help

```powershell
dotnet run --project ProcWatch.Cli -- monitor --help
```

## CLI Options

```
--pid <int>              Process ID to monitor
--process <string>       Process name to monitor (picks newest if multiple)
--db <path>              Database file path (default: procwatch_YYYYMMDD_HHMMSS.db)
--interval-ms <int>      Stats collection interval in milliseconds (default: 1000)
--max-events <int>       Maximum number of events to capture before stopping
--no-console             Disable console UI (headless mode)
--no-children            Do not monitor child processes (default: monitors children)
--help                   Show help and usage information
```

## Usage Examples

### Monitor Notepad with Dashboard

```powershell
dotnet run --project ProcWatch.Cli -- monitor --process notepad
```

### Monitor Specific PID, Custom Database

```powershell
dotnet run --project ProcWatch.Cli -- monitor --pid 5678 --db my_capture.db
```

### Headless Mode (No UI)

```powershell
dotnet run --project ProcWatch.Cli -- monitor --process chrome --no-console
```

### Monitor Without Children

```powershell
dotnet run --project ProcWatch.Cli -- monitor --process explorer --no-children
```

## UI Navigation

### Dashboard Mode (Default)

The dashboard shows:
- Target process info and uptime
- ETW capture status
- Real-time CPU, memory, handles, threads
- Event counters by type
- Recent events preview

**Controls:**
- `E` - Switch to Event Browser
- `Ctrl+C` - Stop monitoring and exit

### Event Browser Mode

Displays a scrollable, detailed list of all captured events.

**Controls:**
- `↑/↓` - Scroll one event
- `PgUp/PgDn` - Page up/down
- `Home/End` - Jump to first/last event
- `Esc` - Return to Dashboard
- `Ctrl+C` - Stop monitoring and exit

## Architecture

### Projects

- **ProcWatch.AppHost**: Aspire 13 orchestrator
- **ProcWatch.ServiceDefaults**: Shared Aspire configuration
- **ProcWatch.MonitorService**: Core monitoring logic and EF Core models
- **ProcWatch.Cli**: Command-line interface with Spectre.Console UI
- **ProcWatch.MigrationService**: EF Core migration runner (Aspire pattern)
- **ProcWatch.ApiService**: REST API for querying historical data

### Data Model

- `MonitoredSession`: Top-level monitoring session
- `ProcessInstance`: Tracked process (target or child)
- `EventRecord`: Individual event (file/network/registry/image load)
- `StatsSample`: Periodic process statistics snapshot

### Technologies

- **.NET 10**: Latest .NET version
- **Aspire 13**: Cloud-native orchestration and service defaults
- **System.CommandLine**: CLI argument parsing
- **Spectre.Console**: Rich terminal UI
- **Microsoft.Diagnostics.Tracing.TraceEvent**: ETW (Event Tracing for Windows)
- **EF Core 10 + SQLite**: Data persistence
- **System.Management**: WMI for process details

## Administrator Privileges

ProcWatch works in two modes:

### With Admin (Recommended)

Run PowerShell or Terminal as Administrator:
- Full ETW capture (file I/O, registry, network, image loads)
- Most accurate event tracking

### Without Admin (Limited)

- Stats collection works (CPU, memory, handles, threads)
- ETW events disabled (noted in dashboard)
- A system event is recorded explaining the limitation

## Data Storage

All captured data is stored in SQLite databases:

- Default naming: `procwatch_YYYYMMDD_HHMMSS.db`
- Schema managed by EF Core migrations
- Append-only design for historical analysis
- Batched writes for performance (channels)

## Troubleshooting

### "Not running as administrator"

ETW requires admin privileges. Run your terminal as Administrator for full event capture.

### "Process not found"

Ensure the process is running. Use Task Manager to verify the process name or PID.

### "ETW session name conflict"

Multiple ProcWatch instances may conflict. Each uses a unique session GUID, but ensure previous sessions terminated cleanly.

### High CPU Usage

Reduce the stats collection interval:
```powershell
--interval-ms 5000
```

## Solution Structure

```
ProcWatch/
├── ProcWatch.sln
├── ProcWatch.AppHost/
├── ProcWatch.ServiceDefaults/
├── ProcWatch.MonitorService/
│   ├── Data/                    # EF Core models and DbContext
│   ├── Services/                # ProcessMonitor, EtwMonitor
│   └── Migrations/              # EF Core migrations
├── ProcWatch.Cli/               # CLI application
├── ProcWatch.MigrationService/  # Migration runner
└── ProcWatch.ApiService/        # REST API
```

## Contributing

This is a demonstration project following Aspire 13 patterns. Feel free to extend with:
- Additional event types
- Real-time alerting
- Web dashboard
- Event filtering/search
- Performance analysis

## License

See LICENSE file for details.
