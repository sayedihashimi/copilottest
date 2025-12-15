# ProcWatch Implementation Session - 2025-12-15 15:45

## Session Goal
Implement the complete ProcWatch Windows-only monitoring tool from scratch based on instructions in .github/prompts/create-app.md

## Status: ✅ IMPLEMENTATION COMPLETE

## Key Learnings Applied from Previous Sessions

### Critical Learnings from Memory Files:
1. **EF Core**: Use MigrateAsync() only, NEVER EnsureCreatedAsync() with migrations
2. **TraceEvent ETW**: Must combine all keywords in single EnableKernelProvider() call with bitwise OR
3. **Logging with Spectre.Console**: Use Serilog file sink only, call ClearProviders() before UseSerilog()
4. **Channel Batching**: Essential for high-frequency ETW events - batch 100 events or 2 second timeout
5. **Platform-Specific**: Use [SupportedOSPlatform("windows")] and set RuntimeIdentifiers in csproj
6. **Manual Argument Parsing**: Avoids System.CommandLine API complexity

## Implementation Completed

### Solution Structure ✅
- ProcWatch.sln created with 4 projects
- All NuGet packages installed (TraceEvent 3.1.28, EF Core 10.0.1, Spectre.Console 0.54.0, Serilog 8.0.0)
- Platform-specific settings configured (RuntimeIdentifiers: win-x64, win-arm64, SupportedOSPlatformVersion: 10.0.17763.0)
- Solution builds successfully with 0 errors, 0 warnings

### Data Layer ✅
- 4 EF Core entities: MonitoredSession, ProcessInstance, EventRecord, StatsSample
- ProcWatchDbContext with proper relationships and indexes
- ProcWatchDbContextFactory for design-time migrations
- InitialCreate migration generated successfully
- MigrationService for automatic migration application at startup

### Monitoring Services ✅
- **ProcessTreeTracker**: WMI-based process tree discovery and tracking
- **StatsSampler**: Periodic CPU/memory/handles/threads sampling with proper delta calculation
- **EventIngestor**: Channel-based batching with bounded queue (10000 items for events, 1000 for stats)
- **EtwMonitor**: TraceEvent ETW session with comprehensive event handlers

### ETW Event Coverage ✅
Implemented comprehensive event handlers based on memory file learnings:

**File I/O (9 handlers)**:
- FileIORead, FileIOWrite
- FileIOCreate, FileIODelete
- FileIOFileCreate, FileIOFileDelete
- FileIOName (critical for handle-to-path mapping)
- DiskIORead, DiskIOWrite

**Registry (8 handlers)**:
- RegistryOpen, RegistrySetValue
- RegistryCreate, RegistryDelete
- RegistryQuery, RegistryQueryValue
- RegistryEnumerateKey, RegistryEnumerateValueKey

**Other Events**:
- ImageLoad
- ProcessStart, ProcessStop

### Worker Orchestration ✅
- Worker service coordinates all monitoring services
- Session initialization in database
- Process lifecycle event handlers
- Graceful shutdown with database finalization

### CLI Implementation ✅
- Manual argument parsing (--pid, --process, --db, --interval-ms, --max-events, --no-console, --no-children)
- Process name to PID resolution with newest instance selection
- MonitorCommandHandler with IHost building and service registration
- Spectre.Console Live dashboard with:
  - Header panel with runtime duration
  - Statistics panel with metrics
  - Events panel with recent 10 events color-coded
  - Footer with instructions
- Ctrl+C handling with cancellation token
- Summary output on exit with session statistics

### Documentation ✅
- Comprehensive README.md with:
  - Usage examples
  - Architecture overview
  - Troubleshooting guide
  - Development guide
  - Key learnings from implementation
- Session memory file (this file)

## Commands Used

```bash
# Install Aspire templates
dotnet new install Aspire.ProjectTemplates

# Create solution and projects
dotnet new sln -n ProcWatch
dotnet new aspire-apphost -n ProcWatch.AppHost
dotnet new aspire-servicedefaults -n ProcWatch.ServiceDefaults
dotnet new worker -n ProcWatch.MonitorService
dotnet new console -n ProcWatch.Cli
dotnet sln add **/*.csproj

# Add packages to MonitorService
cd ProcWatch.MonitorService
dotnet add package Microsoft.Diagnostics.Tracing.TraceEvent --version 3.1.28
dotnet add package Microsoft.EntityFrameworkCore --version 10.0.1
dotnet add package Microsoft.EntityFrameworkCore.Sqlite --version 10.0.1
dotnet add package Microsoft.EntityFrameworkCore.Design --version 10.0.1
dotnet add package System.Management --version 10.0.1
dotnet add reference ../ProcWatch.ServiceDefaults/ProcWatch.ServiceDefaults.csproj

# Add packages to CLI
cd ../ProcWatch.Cli
dotnet add package Spectre.Console --version 0.54.0
dotnet add package Microsoft.EntityFrameworkCore --version 10.0.1
dotnet add package Microsoft.EntityFrameworkCore.Sqlite --version 10.0.1
dotnet add package Microsoft.Extensions.Hosting --version 10.0.1
dotnet add package Serilog.Extensions.Hosting --version 8.0.0
dotnet add package Serilog.Sinks.File --version 6.0.0
dotnet add reference ../ProcWatch.ServiceDefaults/ProcWatch.ServiceDefaults.csproj
dotnet add reference ../ProcWatch.MonitorService/ProcWatch.MonitorService.csproj

# Install EF tools and create migration
dotnet tool install --global dotnet-ef --version 10.0.1
cd ../ProcWatch.MonitorService
dotnet ef migrations add InitialCreate

# Build
cd ..
dotnet build
```

## Build Status
✅ All projects build without errors or warnings

## Issues Encountered and Fixed

### Issue 1: TraceEvent TimeStamp Property
**Problem**: Initially used `data.TimeStamp.UtcDateTime` which doesn't exist  
**Root Cause**: TimeStamp property on TraceEvent data is already a DateTime, not DateTimeOffset  
**Solution**: Changed to `data.TimeStamp` directly  
**Files**: ProcWatch.MonitorService/Services/EtwMonitor.cs

### Issue 2: Null-Coalescing Operator with non-nullable int
**Problem**: `evt.Path.Length ?? 0` caused compiler error  
**Root Cause**: Length is int, not int?, so ?? operator not applicable  
**Solution**: Changed to null-conditional: `evt.Path != null ? evt.Path.Substring(...) : ""`  
**Files**: ProcWatch.Cli/MonitorCommandHandler.cs

## New Learnings This Session

### 1. TraceEvent Data Types
- TimeStamp property on event data is `DateTime`, not `DateTimeOffset`
- No need to access `.UtcDateTime` or `.DateTime` properties
- Just use the TimeStamp directly

### 2. Project Configuration for Windows-Only
Essential PropertyGroup settings for Windows-only .NET applications:
```xml
<RuntimeIdentifiers>win-x64;win-arm64</RuntimeIdentifiers>
<SupportedOSPlatformVersion>10.0.17763.0</SupportedOSPlatformVersion>
```

### 3. Platform Attribute Usage
Use `#pragma warning disable CA1416` at the top of Program.cs files for top-level statements that use Windows-specific APIs

### 4. Aspire 13 Template Installation
- Template version may not match exactly (installed 13.0.2 when requesting 13.0.0)
- This is expected and doesn't cause issues

## Files Created

Total of 26 new files:

**Solution and Projects (5)**:
- ProcWatch.sln
- ProcWatch.AppHost/
- ProcWatch.ServiceDefaults/
- ProcWatch.MonitorService/
- ProcWatch.Cli/

**Data Layer (8)**:
- Data/Entities/MonitoredSession.cs
- Data/Entities/ProcessInstance.cs
- Data/Entities/EventRecord.cs
- Data/Entities/StatsSample.cs
- Data/ProcWatchDbContext.cs
- Data/ProcWatchDbContextFactory.cs
- Data/Migrations/InitialCreate.cs (generated)
- Configuration/MonitoringOptions.cs

**Services (5)**:
- Services/MigrationService.cs
- Services/ProcessTreeTracker.cs
- Services/StatsSampler.cs
- Services/EventIngestor.cs
- Services/EtwMonitor.cs

**Worker and Configuration (3)**:
- Worker.cs (modified)
- Program.cs (modified - MonitorService)
- Program.cs (modified - Cli)

**CLI (2)**:
- MonitorCommandHandler.cs
- Program.cs

**Documentation (2)**:
- README.md
- .github/memories/procwatch-session-20251215-154510.md (this file)

## Verification Status

- ✅ Solution builds successfully
- ✅ All required packages installed
- ✅ EF migrations created
- ✅ README documentation complete
- ⏳ Code review pending
- ⏳ Security scan pending
- ⏳ Runtime testing on Windows (requires Windows environment)

## Architecture Highlights

### ETW Event Flow
```
ETW Kernel Providers → EtwMonitor callbacks → Task.Run fire-and-forget → EventIngestor Channel → Batch Writer → SQLite
```

### Stats Sampling Flow
```
PeriodicTimer → StatsSampler → Process API → CPU delta calculation → EventIngestor Channel → Batch Writer → SQLite
```

### Process Tracking Flow
```
WMI Process Events → ProcessTreeTracker → Track PIDs → Filter events/stats by PID
```

### CLI Dashboard Flow
```
Periodic DB Query (1s) → Build Spectre.Console Layout → Live Update → Loop until Ctrl+C
```

## Key Implementation Patterns

### Fire-and-Forget in ETW Callbacks
```csharp
private Task EnqueueEvent(...)
{
    return Task.Run(async () =>
    {
        try
        {
            await _eventIngestor.EnqueueEventRecordAsync(eventRecord);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to enqueue event");
        }
    });
}
```

### Channel-Based Batching
```csharp
var batch = new List<EventRecord>(100);
batch.Add(await _eventChannel.Reader.ReadAsync(timeoutCts.Token));
while (batch.Count < 100 && _eventChannel.Reader.TryRead(out var item))
{
    batch.Add(item);
}
await WriteBatchToDatabase(batch, cancellationToken);
```

### CPU Percentage Calculation
```csharp
var timeDelta = (now - baseline.LastSample).TotalMilliseconds;
var cpuDelta = (totalProcessorTime - baseline.LastTotalProcessorTime).TotalMilliseconds;
var cpuPercent = (cpuDelta / timeDelta) * 100.0 / Environment.ProcessorCount;
```

## Conclusion

✅ All requirements from create-app.md have been successfully implemented:
- [x] Memory file reviewed and lessons applied
- [x] Complete solution with 4 Aspire projects
- [x] EF Core with migrations and MigrationService
- [x] All monitoring services (ProcessTreeTracker, StatsSampler, EventIngestor, EtwMonitor)
- [x] Worker orchestration with graceful shutdown
- [x] CLI with manual argument parsing
- [x] Spectre.Console live dashboard with Serilog file-only logging
- [x] Comprehensive ETW event coverage (9 file handlers, 8 registry handlers, 3 others)
- [x] README documentation with architecture, usage, and troubleshooting
- [x] Solution builds successfully with 0 errors, 0 warnings

Ready for code review and security scan.
