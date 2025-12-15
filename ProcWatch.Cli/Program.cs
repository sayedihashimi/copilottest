#pragma warning disable CA1416 // Validate platform compatibility
using System.Diagnostics;
using ProcWatch.Cli;

// Parse command line arguments manually
if (args.Length == 0 || args[0] != "monitor")
{
    Console.WriteLine("Usage: procwatch monitor --pid <PID> [options]");
    Console.WriteLine("       procwatch monitor --process <name> [options]");
    Console.WriteLine();
    Console.WriteLine("Options:");
    Console.WriteLine("  --pid <PID>           Target process ID");
    Console.WriteLine("  --process <name>      Target process name (picks newest)");
    Console.WriteLine("  --db <path>           Database path (default: procwatch-<pid>-<timestamp>.sqlite)");
    Console.WriteLine("  --interval-ms <n>     Sampling interval in ms (default: 1000)");
    Console.WriteLine("  --max-events <n>      Maximum events to capture (default: 100000)");
    Console.WriteLine("  --no-console          Disable console dashboard");
    Console.WriteLine("  --no-children         Don't monitor child processes");
    return 1;
}

// Parse arguments
int? pid = null;
string? processName = null;
string? dbPath = null;
int intervalMs = 1000;
int maxEvents = 100000;
bool noConsole = false;
bool noChildren = false;

for (int i = 1; i < args.Length; i++)
{
    switch (args[i])
    {
        case "--pid" when i + 1 < args.Length:
            if (int.TryParse(args[++i], out var parsedPid))
                pid = parsedPid;
            break;
        case "--process" when i + 1 < args.Length:
            processName = args[++i];
            break;
        case "--db" when i + 1 < args.Length:
            dbPath = args[++i];
            break;
        case "--interval-ms" when i + 1 < args.Length:
            if (int.TryParse(args[++i], out var parsed))
                intervalMs = parsed;
            break;
        case "--max-events" when i + 1 < args.Length:
            if (int.TryParse(args[++i], out var parsedMax))
                maxEvents = parsedMax;
            break;
        case "--no-console":
            noConsole = true;
            break;
        case "--no-children":
            noChildren = true;
            break;
    }
}

// Resolve process name to PID if needed
if (!pid.HasValue && !string.IsNullOrEmpty(processName))
{
    var processes = Process.GetProcessesByName(processName);
    if (processes.Length == 0)
    {
        Console.Error.WriteLine($"Error: No process found with name '{processName}'");
        return 1;
    }

    // Pick newest instance
    pid = processes.OrderByDescending(p => p.StartTime).First().Id;
    Console.WriteLine($"Found process '{processName}' with PID {pid}");
}

if (!pid.HasValue)
{
    Console.Error.WriteLine("Error: Must specify --pid or --process");
    return 1;
}

// Validate process exists
try
{
    using var proc = Process.GetProcessById(pid.Value);
    processName ??= proc.ProcessName;
}
catch (ArgumentException)
{
    Console.Error.WriteLine($"Error: Process with PID {pid} not found");
    return 1;
}

// Generate default database path if not specified
dbPath ??= $"procwatch-{pid}-{DateTime.Now:yyyyMMdd-HHmmss}.sqlite";

var sessionId = Guid.NewGuid();

Console.WriteLine($"Starting ProcWatch monitoring:");
Console.WriteLine($"  Target PID: {pid}");
Console.WriteLine($"  Process: {processName}");
Console.WriteLine($"  Database: {dbPath}");
Console.WriteLine($"  Include Children: {!noChildren}");
Console.WriteLine($"  Interval: {intervalMs}ms");
Console.WriteLine();

// Execute monitoring
var handler = new MonitorCommandHandler();
return await handler.ExecuteAsync(
    sessionId,
    pid.Value,
    processName ?? "unknown",
    dbPath,
    intervalMs,
    maxEvents,
    !noConsole,
    !noChildren);

#pragma warning restore CA1416

