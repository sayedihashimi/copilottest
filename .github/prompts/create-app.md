# ProcWatch (Windows-only) — Copilot Prompt (Aspire 13 + .NET 10)

You are an expert Windows/.NET engineer. Build a **Windows-only** monitoring tool named **ProcWatch** using **.NET 10** and **Aspire 13**. Use the Aspire 13 sample solution structure/patterns from:
- Repo reference: https://github.com/sayedihashimi/todojsaspire
- Aspire docs: https://aspire.dev/

This tool monitors a target process (and **child processes by default**) and captures:
- **File** reads/writes
- **Network** activity
- **CPU/memory/handles/threads** stats
- **Registry** read/write
- **DLL/module loads**

The tool should:
- Start monitoring and keep running in the foreground
- Update a **nice live console dashboard** (no scrolling spam; redraw-in-place)
- Stop on **Ctrl+C** gracefully
- Persist everything to **SQLite** using **EF Core**, including **EF migrations** and a **MigrationService** patterned after the referenced solution

---

## REQUIRED WORKFLOW (Memory → Plan → Implement → Verify → Iterate)

### 0) Memory (required, first)
Before planning or coding:

1. Read all files under `.\github\memories\**\*.*` (treat `*.md` / `*.txt` as primary) if they exist.
2. Extract any relevant lessons (Aspire patterns, EF migration gotchas, TraceEvent quirks, privileges, commands, etc.)
3. Use those lessons to improve the plan and implementation.

### 0b) Memory writing (required, continuous)
During execution, whenever you learn anything **novel** (new constraint, tricky behavior, bug fix, working snippet, command that solved an issue, etc.), record it in a new file under:

`.\github\memories\`

Rules:
- Each prompt invocation (“session”) gets its own file.
- File name format:
  - `.\github\memories\procwatch-session-YYYYMMDD-HHMMSS.md`
- Append as you go; do not wait until the end.
- Keep entries concise and actionable.

The session memory file must include:
- Date/time + short summary of session goal
- What worked / what didn’t
- Verification commands used (`dotnet build`, `dotnet test`, migrations commands, run commands, etc.)
- Key decisions (ETW provider choices, fallback behavior, batching strategy)
- Fixes and root cause

---

## REQUIRED WORKFLOW (Plan → Implement → Verify → Iterate)
You MUST follow this workflow:

1. Create a step-by-step plan with checkboxes.
2. Implement step-by-step; complete a step before moving on.
3. For every verifiable step, verify it (build, run, migrations, tests, etc.).
4. If verification fails, fix and re-verify before continuing.
5. Keep the plan updated as you learn; do not skip verification.
6. As you execute, write novel learnings to the session memory file (above).

No TODOs. Produce working code.

---

## UI + CLI REQUIREMENTS (IMPORTANT)
### Argument parsing
- Use **System.CommandLine** (the official .NET command-line library that ships with .NET tooling ecosystem).  
- Do NOT use Spectre.Console.Cli.
- Implement `procwatch` with a `monitor` command and options as specified below.

### Interactive console UI
- Use **Spectre.Console** for all console rendering.
- The CLI UI must look **polished**:
  - Use a live layout (e.g., `AnsiConsole.Live(...)`), panels, tables, and status indicators.
  - Redraw in place (no scrolling spam).
  - Show clear headings, aligned columns, and compact sections.
  - Show a footer hint: “Press Ctrl+C to stop”.
- The UI should keep updating every interval (default 1s) with current aggregate stats + event counters + last event.

Reference: https://spectreconsole.net/

---

## Solution shape (Aspire 13)
Create an Aspire solution similar to the referenced repo layout:
- `ProcWatch.AppHost` (Aspire App Host)
- `ProcWatch.ServiceDefaults` (standard Aspire defaults)
- `ProcWatch.MonitorService` (worker/service that does ETW + stats sampling + persistence)
- `ProcWatch.Cli` (command-line entrypoint users run; parses args with System.CommandLine, starts monitoring via Aspire, and hosts the Spectre.Console UI)

Follow conventions from the sample repo (hosting, configuration, DI, logging, health checks).

---

## CLI requirements
Implement:

`procwatch monitor --pid <PID> --db <path> [--interval-ms <n>] [--max-events <n>] [--no-console] [--no-children]`

Also support:

`procwatch monitor --process <name> --db <path> ...`

Rules:
- If both `--pid` and `--process` supplied, prefer `--pid`.
- If multiple processes match `--process`, pick the newest instance (most recent start time).
- Child processes are monitored by default; `--no-children` disables.
- Default `--interval-ms` = `1000`.
- If `--db` omitted, default: `procwatch-<pid>-<yyyyMMdd-HHmmss>.sqlite` in current directory.

Ctrl+C:
- Stop ETW session cleanly, stop timers, flush EF/DB writes, print summary counts + DB path.
- If `--no-console`, do not render the UI, but still log/persist.

---

## Data persistence (SQLite + EF Core + Migrations)
Use EF Core + SQLite.

### EF requirements
- Implement `ProcWatchDbContext`.
- Implement EF migrations.
- Apply migrations at startup via a **MigrationService** patterned after the referenced repo (same concept/style).
- DB is append-only.

### Schema guidance
Use tables like:
- `MonitoredSession` (SessionId GUID, StartTime, TargetPid, TargetProcessName, IncludeChildren, ArgsJson)
- `ProcessInstance` (Id, SessionId, Pid, ParentPid, ProcessName, StartTime, EndTime nullable)
- `EventRecord` (Id, SessionId, Pid, ProcessName, Timestamp, Type, Op, JsonPayload, plus queryable columns like Path/Endpoints as needed)
- `StatsSample` (Id, SessionId, Pid, Timestamp, CpuPct, WorkingSetBytes, PrivateBytes, HandleCount, ThreadCount)

Store type-specific payload in `JsonPayload` (System.Text.Json) but also keep key query columns where useful.

Performance:
- Use a bounded Channel queue and a single writer loop.
- Batch inserts; keep EF overhead low (temporary `AutoDetectChangesEnabled=false` in batching scope).

---

## Monitoring implementation (Windows-only)
### Primary mechanism: ETW (TraceEvent)
Use `Microsoft.Diagnostics.Tracing.TraceEvent` to capture:
- File I/O events
- Registry operations
- Image loads (DLL/EXE)
- Network events (prefer ETW if viable)

### Network fallback
If ETW network capture isn’t viable:
- Periodically snapshot connections for monitored PIDs (IPHelper / `System.Net.NetworkInformation`) and log diffs.
- Label fallback records with `Source="Snapshot"`.

### Process + children tracking
- Monitor target PID and descendants by default.
- Maintain set of active PIDs (WMI/CIM `Win32_Process` or Toolhelp snapshots); update periodically and/or on process start events.
- If root exits, continue until all tracked PIDs exit (document behavior).

### Stats sampling
Every `interval-ms`:
- CPU %, working set, private bytes, handles, threads
- Aggregate view for all monitored PIDs for the UI

---

## Robustness & permissions
- If ETW requires elevation and fails:
  - Continue in “stats + snapshot network” mode
  - Record a `system` EventRecord explaining the limitation
- Handle PID not found, early exit, invalid DB path, ETW session conflicts (unique session name).

---

## Code organization (recommended)
- `EtwMonitor`
- `ProcessTreeTracker`
- `StatsSampler`
- `EventIngestor` (Channel + batching)
- EF: `ProcWatchDbContext`, entities, migrations
- `MigrationService` (like todojsaspire)
- `ConsoleDashboard` (Spectre.Console Live UI)
- CLI uses System.CommandLine and composes services via DI

Use options pattern + DI throughout.

---

## Deliverables
Produce a complete working repo that:
- Builds with `dotnet build`
- Runs via Aspire AppHost
- CLI uses System.CommandLine for parsing and Spectre.Console for UI
- EF migrations exist and are applied via MigrationService
- Works on Windows 11+ (document minimum)

Include README with setup/run examples and limitations.

---

## NuGet packages (suggested)
- `Microsoft.Diagnostics.Tracing.TraceEvent`
- `Microsoft.EntityFrameworkCore`
- `Microsoft.EntityFrameworkCore.Sqlite`
- `Microsoft.EntityFrameworkCore.Design`
- `Spectre.Console`
- Aspire packages consistent with Aspire 13 patterns in referenced solution
- System.CommandLine package/version appropriate for .NET 10 tooling

---

## Acceptance checklist
- [ ] Memory read first; session memory file created and updated during work
- [ ] Plan created; steps implemented + verified; failures fixed + re-verified
- [ ] Polished Spectre.Console live UI (panels/tables, redraw-in-place, Ctrl+C hint)
- [ ] System.CommandLine handles args (`monitor` command + options)
- [ ] `procwatch monitor --pid ...` runs and writes to SQLite
- [ ] Ctrl+C stops cleanly and prints summary
- [ ] Child processes monitored by default; `--no-children` works
- [ ] EF migrations exist and applied via MigrationService
- [ ] `dotnet build` succeeds; app runs without runtime errors; tests pass
