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
- Update a **polished live console dashboard**
- Support **interactive navigation to browse historical events**
- Stop on **Ctrl+C** gracefully
- Persist everything to **SQLite** using **EF Core**, including **EF migrations** and a **MigrationService** patterned after the referenced solution

---

## REQUIRED WORKFLOW (Memory → Plan → Implement → Verify → Iterate)

### 0) Memory (required, first)
Use **Memorizer v1** for all long-term memory (no markdown/text files).

- Memorizer repo: https://github.com/petabridge/memorizer-v1  
- Memorizer is installed locally and available.

Before planning or coding:
1. Load all existing memories relevant to this repository/project using Memorizer.
2. Extract lessons (Aspire patterns, EF migration issues, ETW quirks, CI pitfalls, UI/UX decisions, etc.).
3. Apply those lessons to the plan and implementation.

### 0b) Memory writing (required, continuous)
During execution, whenever anything **novel** is learned, persist it via Memorizer.

Rules:
- Each prompt invocation = one Memorizer **session context**
- Write memories incrementally as discoveries happen
- Keep entries concise, factual, and reusable

Each session memory should include:
- Session goal
- What worked / failed
- Verification commands used
- Key technical decisions
- Fixes and root causes

---

## REQUIRED WORKFLOW (Plan → Implement → Verify → Iterate)
You MUST:
1. Create a step-by-step plan with checkboxes.
2. Implement one step at a time.
3. **Verify every verifiable step** (`dotnet build`, `dotnet test`, run app, migrations apply, etc.).
4. If verification fails, fix and re-verify before proceeding.
5. Update the plan as new information is learned.
6. Persist all novel learnings to Memorizer.

No TODOs. No placeholders. Produce working, complete code.

---

## UI + CLI REQUIREMENTS (CRITICAL)

### Argument parsing
- Use **System.CommandLine** for all CLI parsing.
- Support `--help` **out of the box** via System.CommandLine.
- No ad-hoc or temporary parsers.
- Do NOT use Spectre.Console.Cli.

Commands/options:
- Root command: `procwatch`
- Subcommand: `monitor`
- Options:
  - `--pid <int>`
  - `--process <string>`
  - `--db <path>`
  - `--interval-ms <int>` (default 1000)
  - `--max-events <int>` (optional)
  - `--no-console`
  - `--no-children`
  - `--help` (automatic)

Behavior rules:
- `--pid` overrides `--process`
- If multiple processes match `--process`, pick the newest
- Child processes monitored by default

---

## Spectre.Console UX (LIVE + INTERACTIVE)

### Live dashboard (default view)
Use **Spectre.Console** to render a polished, continuously updating dashboard:
- Implement with `AnsiConsole.Live(...)`
- Redraw in place (no scrolling spam)
- Panels / tables for:
  - Target process + uptime
  - Aggregate CPU %, memory, handles, threads
  - Event counters by type (file/net/registry/module)
  - Recent events preview (last N events, e.g. 10–20)

Footer hints (always visible):
- `Ctrl+C` → Stop and exit
- `E` → View all events
- `↑/↓` or `PgUp/PgDn` → Scroll (when applicable)
- `Esc` → Return to dashboard

### Event browser mode (REQUIRED)
Implement an interactive event browser:
- Press `E` to switch dashboard → event browser.
- Display a scrollable list/table of events:
  - Timestamp, PID/process, Type, Op, key details (path/endpoint/dll/etc.)
- Navigation:
  - `↑/↓` scroll
  - `PgUp/PgDn` page
  - `Home/End` oldest/newest
- `Esc` returns to dashboard.
- Ingestion continues while browsing (no blocking).

Data source:
- Back event browser with an in-memory ring buffer and/or SQLite queries (SQLite preferred for large histories).
- Keep UI responsive; do not block ingestion.

Implementation guidance:
- Use a UI state machine: `Dashboard` and `EventBrowser`.
- Separate rendering from ingestion state.

---

## CRITICAL Aspire 13 Database Integration Rule
When any app/service needs database access:

❌ Do NOT use raw connection strings directly  
❌ Do NOT call `UseSqlite("...")` with hard-coded or manually constructed strings  

✅ Follow the **Aspire 13 pattern from the referenced repo**:
- Define SQLite resource in `ProcWatch.AppHost`
- Pass DB parameters (like file path) via AppHost configuration
- Register DbContext in consuming services using Aspire helpers/extensions
- Apply migrations via `MigrationService` (patterned after `todojsaspire`)

Even though CLI accepts `--db <path>`:
- The CLI passes this value into AppHost configuration
- Consumer services never build connection strings themselves

---

## Solution shape (Aspire 13)
Create an Aspire solution similar to the referenced repo:
- `ProcWatch.AppHost` (Aspire App Host)
- `ProcWatch.ServiceDefaults` (standard Aspire defaults)
- `ProcWatch.MonitorService` (worker/service: ETW + stats sampling + persistence)
- `ProcWatch.Cli` (System.CommandLine + Spectre.Console UI)

Follow conventions from the sample repo (hosting, configuration, DI, logging, health checks).

---

## Data persistence (SQLite + EF Core + Migrations)
Use EF Core + SQLite.

Requirements:
- Append-only schema
- Batched inserts via Channel
- EF migrations exist and are applied at startup via **MigrationService** patterned after `todojsaspire`

Tables:
- `MonitoredSession`
- `ProcessInstance`
- `EventRecord`
- `StatsSample`

---

## Monitoring implementation (Windows-only)
- Primary mechanism: ETW via `Microsoft.Diagnostics.Tracing.TraceEvent`
- Capture: file I/O, registry ops, image loads, network activity (ETW if viable)
- Network fallback: periodic snapshot connections and log diffs if ETW net capture isn’t viable
- Track child processes via WMI/Toolhelp; include descendants by default
- Stats sampled every interval; aggregate for UI

---

## Robustness & permissions
- If ETW requires elevation and fails:
  - Continue in “stats + snapshot network” mode
  - Record a `system` EventRecord describing the limitation
- Handle PID not found, early exit, invalid DB path, ETW session conflicts (unique session name).

---

## CI (GitHub Actions) — ALSO SUPPORTED BY THIS PROMPT
This prompt must also handle requests like:

> “Add a GitHub Actions file that will run on each CI build. It should download the latest version of dotnet 10, do a build and run tests.
> Use the github actions file linked below as a sample. For this solution exclude the Aspire related content
> https://raw.githubusercontent.com/sayedihashimi/todojsaspire/refs/heads/main/.github/workflows/build.yml”

When asked to add CI:
- Create/update `.github/workflows/build.yml`
- Trigger on `push` and `pull_request`
- Use `actions/setup-dotnet` to install **latest .NET 10**
- Run:
  - `dotnet --info`
  - `dotnet restore`
  - `dotnet build -c Release`
  - `dotnet test -c Release --no-build`
- Exclude Aspire-specific workflow content from the referenced sample
- Verify locally before finalizing
- Persist CI learnings to Memorizer

---

## Deliverables
Produce a complete working repo that:
- Builds with `dotnet build`
- Runs via Aspire AppHost
- CLI uses System.CommandLine (with built-in `--help`) + Spectre.Console (live + event browser)
- Uses Aspire-style DbContext wiring (no raw connection strings in consumers)
- Uses EF migrations + MigrationService pattern
- Includes CI workflow when requested
- Works on Windows 11+

Include `README.md` with:
- Setup/run commands
- Example usage
- Notes about elevation/limitations
- How to browse events in the UI (Dashboard vs Event Browser)

---

## Acceptance checklist
- [ ] Existing memories loaded via Memorizer; new ones persisted
- [ ] Step-by-step plan created and followed; each verifiable step verified
- [ ] `procwatch monitor --help` works via System.CommandLine
- [ ] Polished Spectre.Console dashboard + event browser with scrolling
- [ ] Capture DB created via EF Core + migrations + MigrationService (Aspire pattern)
- [ ] Child processes monitored by default; `--no-children` works
- [ ] `dotnet build` and `dotnet test` succeed
- [ ] CI workflow added when requested (non-Aspire-specific) and builds/tests with .NET 10
