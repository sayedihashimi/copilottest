using System.CommandLine;
using ProcWatch.Cli;

var rootCommand = new RootCommand("ProcWatch - Windows process monitoring tool");

var monitorCommand = new Command("monitor", "Monitor a process and capture events");

var pidOption = new Option<int?>(
    ["--pid"],
    "Process ID to monitor");

var processOption = new Option<string?>(
    ["--process"],
    "Process name to monitor");

var dbOption = new Option<string>(
    ["--db"],
    () => $"procwatch_{DateTime.Now:yyyyMMdd_HHmmss}.db",
    "Database file path");

var intervalOption = new Option<int>(
    ["--interval-ms"],
    () => 1000,
    "Stats collection interval in milliseconds");

var maxEventsOption = new Option<int?>(
    ["--max-events"],
    "Maximum number of events to capture");

var noConsoleOption = new Option<bool>(
    ["--no-console"],
    "Disable console UI");

var noChildrenOption = new Option<bool>(
    ["--no-children"],
    "Do not monitor child processes");

monitorCommand.AddOption(pidOption);
monitorCommand.AddOption(processOption);
monitorCommand.AddOption(dbOption);
monitorCommand.AddOption(intervalOption);
monitorCommand.AddOption(maxEventsOption);
monitorCommand.AddOption(noConsoleOption);
monitorCommand.AddOption(noChildrenOption);

monitorCommand.SetHandler(async (pid, processName, dbPath, interval, maxEvents, noConsole, noChildren) =>
{
    var cli = new ProcWatchCli();
    await cli.RunMonitoringAsync(pid, processName, dbPath, interval, maxEvents, noConsole, noChildren);
}, pidOption, processOption, dbOption, intervalOption, maxEventsOption, noConsoleOption, noChildrenOption);

rootCommand.AddCommand(monitorCommand);

return await rootCommand.InvokeAsync(args);
