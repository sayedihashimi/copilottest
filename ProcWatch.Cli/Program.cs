#pragma warning disable CA1416 // Platform compatibility
using System.Diagnostics;
using ProcWatch.MonitorService.Configuration;

namespace ProcWatch.Cli;

class Program
{
    static async Task<int> Main(string[] args)
    {
        try
        {
            // Parse command line arguments
            if (args.Length == 0 || args[0] != "monitor")
            {
                Console.WriteLine("Usage: procwatch monitor --pid <PID> [options]");
                Console.WriteLine("   or: procwatch monitor --process <name> [options]");
                Console.WriteLine();
                Console.WriteLine("Options:");
                Console.WriteLine("  --pid <PID>           Target process ID");
                Console.WriteLine("  --process <name>      Target process name (picks newest if multiple)");
                Console.WriteLine("  --db <path>           Database path (default: procwatch-<pid>-<timestamp>.sqlite)");
                Console.WriteLine("  --interval-ms <ms>    Sampling interval in milliseconds (default: 1000)");
                Console.WriteLine("  --max-events <n>      Maximum events to capture (default: 100000)");
                Console.WriteLine("  --no-console          Disable console UI");
                Console.WriteLine("  --no-children         Don't monitor child processes");
                return 1;
            }

            var options = ParseArguments(args);
            if (options == null)
            {
                return 1;
            }

            // Run the monitor command
            var handler = new MonitorCommandHandler(options);
            return await handler.ExecuteAsync();
        }
        catch (Exception ex)
        {
            Console.ForegroundColor = ConsoleColor.Red;
            Console.WriteLine($"Error: {ex.Message}");
            Console.ResetColor();
            return 1;
        }
    }

    private static MonitoringOptions? ParseArguments(string[] args)
    {
        var options = new MonitoringOptions();
        int? pid = null;
        string? processName = null;

        for (int i = 1; i < args.Length; i++)
        {
            switch (args[i])
            {
                case "--pid":
                    if (i + 1 < args.Length && int.TryParse(args[i + 1], out int p))
                    {
                        pid = p;
                        i++;
                    }
                    else
                    {
                        Console.WriteLine("Error: --pid requires a numeric value");
                        return null;
                    }
                    break;

                case "--process":
                    if (i + 1 < args.Length)
                    {
                        processName = args[i + 1];
                        i++;
                    }
                    else
                    {
                        Console.WriteLine("Error: --process requires a process name");
                        return null;
                    }
                    break;

                case "--db":
                    if (i + 1 < args.Length)
                    {
                        options.DbPath = args[i + 1];
                        i++;
                    }
                    else
                    {
                        Console.WriteLine("Error: --db requires a path");
                        return null;
                    }
                    break;

                case "--interval-ms":
                    if (i + 1 < args.Length && int.TryParse(args[i + 1], out int interval))
                    {
                        options.IntervalMs = interval;
                        i++;
                    }
                    else
                    {
                        Console.WriteLine("Error: --interval-ms requires a numeric value");
                        return null;
                    }
                    break;

                case "--max-events":
                    if (i + 1 < args.Length && int.TryParse(args[i + 1], out int maxEvents))
                    {
                        options.MaxEvents = maxEvents;
                        i++;
                    }
                    else
                    {
                        Console.WriteLine("Error: --max-events requires a numeric value");
                        return null;
                    }
                    break;

                case "--no-console":
                    options.NoConsole = true;
                    break;

                case "--no-children":
                    options.NoChildren = true;
                    break;

                default:
                    Console.WriteLine($"Unknown option: {args[i]}");
                    return null;
            }
        }

        // Resolve process name to PID if needed
        if (pid.HasValue)
        {
            options.TargetPid = pid.Value;
        }
        else if (!string.IsNullOrEmpty(processName))
        {
            var processes = Process.GetProcessesByName(processName);
            if (processes.Length == 0)
            {
                Console.WriteLine($"Error: No process found with name '{processName}'");
                return null;
            }

            // Pick the newest (most recent start time)
            var newestProcess = processes.OrderByDescending(p =>
            {
                try { return p.StartTime; }
                catch { return DateTime.MinValue; }
            }).First();

            options.TargetPid = newestProcess.Id;
            options.ProcessName = processName;
            Console.WriteLine($"Found process '{processName}' with PID {options.TargetPid}");
        }
        else
        {
            Console.WriteLine("Error: Either --pid or --process must be specified");
            return null;
        }

        // Set default database path if not specified
        if (string.IsNullOrEmpty(options.DbPath))
        {
            var timestamp = DateTime.Now.ToString("yyyyMMdd-HHmmss");
            options.DbPath = Path.Combine(Directory.GetCurrentDirectory(), 
                $"procwatch-{options.TargetPid}-{timestamp}.sqlite");
        }

        // Ensure absolute path
        options.DbPath = Path.GetFullPath(options.DbPath);

        return options;
    }
}
#pragma warning restore CA1416
