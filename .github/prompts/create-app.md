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
2. Extract relevant lessons (Aspire patterns, EF migration gotchas, TraceEvent quirks, privileges, commands, etc.)
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

Session memory file must include:
- Date/time + short summary of session goal
- What worked / what didn’t
- Verification commands used (`dotnet build`, `dotnet test`, migrations, run commands, etc.)
- Key decisions (ETW provider choices, fallback behavior, batching strategy)
- Fixes and root cause

---

## REQUIRED WORKFLOW (Plan → Implement → Verify → Iterate)
You MUST:
1. Create a step-by-step plan with checkboxes.
2. Implement step-by-step; complete a step before moving on.
3. Verify every verifiable step (build/run/migrations/tests).
4. If verification fails, fix and re-verify before continuing.
5. Update the plan as you learn; do not skip verification.
6. Write novel learnings to the session memory file as you go.

No TODOs. Produce working code.

---

## UI + CLI REQUIREMENTS (IMPORTANT)
### Argument parsing
- Use **System.CommandLine** for command/option parsing.
- Do NOT use Spectre.Console.Cli.

### Interactive console UI
- Use **Spectre.Console** for all console rendering (https://spectreconsole.net/).
- The CLI UI must look polished:
  - Use live layout (`AnsiConsole.Live(...)`), panels, tables, status indicators.
  - Redraw in place (no scrolling spam).
  - Footer hint: “Press Ctrl+C to stop”.
- UI updates every interval (default 1s) with aggregate stats + counters + last event.

---

## CRITICAL Aspire 13 Database Integration Rule
When any app/service needs to connect to the database, **do NOT use a raw connection string directly** (no `UseSqlite("...")` with a string read manually from config in the consumer).

Instead, wire the database and DbContext the **Aspire 13 way shown in the referenced repo**:
- Define/configure the database resource in `ProcWatch.AppHost` using Aspire patterns (like the sample repo).
- In consuming projects (`ProcWatch.MonitorService`), register the DbContext using Aspire-style helpers/extensions (the same pattern as in `todojsaspire`), so the connection information flows via Aspire service discovery/configuration.
- The consumer should call the Aspire-provided `AddDbContext`/`AddSqliteDbContext`-style registration and rely on Aspire configuration binding, not hard-coded connection strings.

Follow the exact conventions from the referenced repo for:
- resource definition in AppHost
- adding the DbContext in the service project
- how migrations are applied using MigrationService

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

**Important:** even though CLI accepts `--db <path>`, the actual DbContext wiring must still follow Aspire patterns. That means:
- The CLI passes the DB path into Aspire/AppHost configuration (e.g., as resource parameter/config value) and AppHost uses it when defining the SQLite resource.
- The service consumes the DbContext via Aspire registration, not by building a connection string itself.

Ctrl+C:
- Stop ETW session cleanly, stop timers, flush EF/DB writes, print summary counts + DB path.
- If `--no-console`, do not render the UI, but still log/persist.

---

## Data persistence (SQLite + EF Core + Migrations)
Use EF Core + SQLite.

### EF requirements
- Implement `ProcWatchDbContext`.
- Implement EF migrations.
- Apply migrations at startup via a **MigrationService** patterned after the referenced repo.
- DB is append-only.

### Schema guidance
Use tables like:
- `MonitoredSession` (SessionId GUID, StartTime, TargetPid, TargetProcessName, IncludeChildren, ArgsJson)
- `ProcessInstance` (Id, SessionId, Pid, ParentPid, ProcessName, StartTime, EndTime nullable)
- `EventRecord` (Id, SessionId, Pid, ProcessName, Timestamp, Type, Op, JsonPayload, plus queryable columns like Path/Endpoints)
- `StatsSample` (Id, SessionId, Pid, Timestamp, CpuPct, WorkingSetBytes, PrivateBytes, HandleCount, ThreadCount)

Store type-specific payload in `JsonPayload` (System.Text.Json) and keep key query columns where useful.

Performance:
- Use bounded Channel queue + single writer loop.
- Batch inserts; reduce EF overhead (temporary `AutoDetectChangesEnabled=false` within batching scope).

---

## Monitoring implementation (Windows-only)
### Primary mechanism: ETW (TraceEvent)
Use `Microsoft.Diagnostics.Tracing.TraceEvent` to capture:
- File I/O
- Registry operations
- Image loads (DLL/EXE)
- Network events (prefer ETW if viable)

### Network fallback
If ETW network capture isn’t viable:
- Snapshot connections for monitored PIDs and log diffs.
- Label fallback records with `Source="Snapshot"`.

### Process + children tracking
- Monitor target PID and descendants by default.
- Maintain set of active PIDs (WMI/CIM `Win32_Process` or Toolhelp snapshot); update periodically and/or on process start events.
- If root exits, continue until all tracked PIDs exit (document behavior).

### Stats sampling
Every `interval-ms`:
- CPU %, working set, private bytes, handles, threads
- Aggregate view for all monitored PIDs for the UI

---

## Robustness & permissions
- If ETW requires elevation and fails:
  - Continue in “stats + snapshot network” mode
  - Record a `system` EventRecord describing the limitation
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
- DbContext wiring follows Aspire patterns (no direct raw connection string usage in consumers)
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
- [ ] DbContext is configured using Aspire 13 patterns from referenced repo (no direct connection string usage in consumer)
- [ ] `dotnet build` succeeds; app runs without runtime errors; tests pass
