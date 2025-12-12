#pragma warning disable CA1416 // Platform compatibility

using System.Diagnostics;
using System.Runtime.Versioning;
using ProcWatch.Cli;

[assembly: SupportedOSPlatform("windows")]

// Parse arguments manually (simple approach per memory file lessons)
var cmdArgs = Environment.GetCommandLineArgs().Skip(1).ToArray();

if (cmdArgs.Length == 0 || cmdArgs[0] != "monitor")
{
    Console.WriteLine("Usage: procwatch monitor --pid <PID> | --process <name> [options]");
    Console.WriteLine("Options:");
    Console.WriteLine("  --pid <PID>           Target process ID");
    Console.WriteLine("  --process <name>      Target process name (picks newest)");
    Console.WriteLine("  --db <path>           Database path (default: procwatch-<pid>-<timestamp>.sqlite)");
    Console.WriteLine("  --interval-ms <n>     Sampling interval in milliseconds (default: 1000)");
    Console.WriteLine("  --max-events <n>      Maximum events to capture (default: unlimited)");
    Console.WriteLine("  --no-console          Disable console UI");
    Console.WriteLine("  --no-children         Don't monitor child processes");
    return 1;
}

// Parse options
int? targetPid = null;
string? processName = null;
string? dbPath = null;
int intervalMs = 1000;
int maxEvents = -1;
bool noConsole = false;
bool noChildren = false;

for (int i = 1; i < cmdArgs.Length; i++)
{
    switch (cmdArgs[i])
    {
        case "--pid" when i + 1 < cmdArgs.Length:
            targetPid = int.Parse(cmdArgs[++i]);
            break;
        case "--process" when i + 1 < cmdArgs.Length:
            processName = cmdArgs[++i];
            break;
        case "--db" when i + 1 < cmdArgs.Length:
            dbPath = cmdArgs[++i];
            break;
        case "--interval-ms" when i + 1 < cmdArgs.Length:
            intervalMs = int.Parse(cmdArgs[++i]);
            break;
        case "--max-events" when i + 1 < cmdArgs.Length:
            maxEvents = int.Parse(cmdArgs[++i]);
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
if (!targetPid.HasValue && !string.IsNullOrEmpty(processName))
{
    var processes = Process.GetProcessesByName(processName).OrderByDescending(p => p.StartTime).ToArray();
    if (processes.Length == 0)
    {
        Console.WriteLine($"Error: No process found with name '{processName}'");
        return 1;
    }
    targetPid = processes[0].Id;
    Console.WriteLine($"Found process '{processName}' with PID {targetPid}");
}

if (!targetPid.HasValue)
{
    Console.WriteLine("Error: Either --pid or --process must be specified");
    return 1;
}

// Verify process exists
try
{
    var process = Process.GetProcessById(targetPid.Value);
    processName ??= process.ProcessName;
}
catch (ArgumentException)
{
    Console.WriteLine($"Error: Process with PID {targetPid} not found");
    return 1;
}

// Set default database path
dbPath ??= $"procwatch-{targetPid}-{DateTime.Now:yyyyMMdd-HHmmss}.sqlite";

// Run the monitor command
var handler = new MonitorCommandHandler();
return await handler.ExecuteAsync(
    targetPid.Value,
    processName!,
    dbPath,
    intervalMs,
    maxEvents,
    !noChildren,
    noConsole);

