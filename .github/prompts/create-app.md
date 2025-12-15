# ProcWatch (Windows-only) — Copilot Prompt (Aspire 13 + .NET 10 + React Dashboard)

You are an expert Windows/.NET engineer. Build a **Windows-only** monitoring tool named **ProcWatch** using **.NET 10** and **Aspire 13**, and ALSO build a **React web app** (Aspire-configured) that can browse previously generated ProcWatch SQLite databases.

Use Aspire 13 sample solution structure/patterns from:
- Repo reference: https://github.com/sayedihashimi/todojsaspire
- Aspire docs: https://aspire.dev/

**Additional Aspire guidance file (required):**
- If a file exists at `.github\aspire13.md`, load it and follow its Aspire guidance and best practices.

---

## What to build

### 1) ProcWatch process monitoring
Monitor a target process (and **child processes by default**) and capture:
- File reads/writes
- Network activity
- CPU/memory/handles/threads stats
- Registry read/write
- DLL/module loads

Runtime UX:
- Polished live console dashboard
- Interactive event browser view that supports keyboard navigation and scrolling historical events
- Stop gracefully on Ctrl+C
- Persist everything to SQLite via EF Core + migrations + MigrationService (Aspire pattern)

### 2) React web dashboard (Aspire configured)
Create a React web app that:
- Lists **previously generated ProcWatch `.sqlite` files** (from a configured directory)
- Allows user to select a DB file
- Opens the selected DB (server-side) and enables user to **view/search/filter** the contents:
  - sessions
  - processes
  - events (by type/op/path/pid/time range)
  - stats samples (graphs optional; table required)
- Must follow **Aspire 13 best practices** for configuration/service wiring.

Security note:
- Treat DB browsing as local/dev tool (no multi-tenant auth needed unless requested).
- Do not allow arbitrary path traversal; only allow selection from the known directory list.

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
3. Verify every verifiable step (`dotnet build`, `dotnet test`, run app(s), migrations apply, etc.).
4. If verification fails, fix and re-verify before proceeding.
5. Update the plan as new information is learned.
6. Persist all novel learnings to Memorizer.

No TODOs. No placeholders. Produce working, complete code.

---

## UI + CLI REQUIREMENTS (CRITICAL)

### Argument parsing
- Use **System.CommandLine** for all CLI parsing.
- Support `--help` out of the box via System.CommandLine.
- No ad-hoc or temporary parsers.
- Do NOT use Spectre.Console.Cli.

Commands/options:
- Root: `procwatch`
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

Behavior:
- `--pid` overrides `--process`
- If multiple processes match, pick the newest
- Child processes monitored by default

### Spectre.Console UX (live + interactive)
- Use Spectre.Console (https://spectreconsole.net/)
- Live dashboard with panels/tables; redraw-in-place
- Event browser mode:
  - Press `E` to open
  - Scroll with ↑/↓, PgUp/PgDn, Home/End
  - `Esc` to return
- Ingestion must continue while browsing (no blocking).

---

## CRITICAL Aspire 13 Database Integration Rule
When any service needs database access:

❌ Do NOT use raw connection strings directly  
❌ Do NOT call `UseSqlite("...")` in consumers  

✅ Follow the **Aspire 13 pattern from the referenced repo**:
- Define the DB resource(s) in `AppHost`
- Register DbContext using Aspire helpers/extensions
- Apply migrations via `MigrationService`

Even though CLI accepts `--db <path>`:
- CLI passes that value into AppHost/configuration
- Consumer services never construct their own connection strings

---

## Solution shape (Aspire 13)
Create an Aspire solution that includes:
- `ProcWatch.AppHost` (Aspire)
- `ProcWatch.ServiceDefaults`
- `ProcWatch.MonitorService` (worker/service: ETW + stats + persistence)
- `ProcWatch.Cli` (System.CommandLine + Spectre.Console UI)
- `ProcWatch.ApiService` (NEW: ASP.NET Core Minimal API that exposes read-only endpoints for browsing DBs)
- `ProcWatch.Web` (NEW: React app; Aspire configured; talks to ApiService)

Follow patterns from `todojsaspire` and `.github\aspire13.md` if present.

---

## React web app requirements (Aspire configured)
### Architecture
- React app is a separate project (`ProcWatch.Web`) and is wired into Aspire AppHost.
- Backend browsing is done via `ProcWatch.ApiService` (ASP.NET Core Minimal API).
- React app calls ApiService endpoints.

### DB discovery
- ProcWatch CLI writes SQLite files into a directory.
- The web app must show the user a list of generated `.sqlite` files from a configured directory.
- Use configuration (Aspire) to provide the directory path to the ApiService, e.g.:
  - `ProcWatch:DbDirectory` (or similar)
- ApiService endpoint `GET /api/dbs` returns list of DB files with safe metadata:
  - file name, full id token (not raw path), size, last modified

### DB selection / access
- React selects a DB by an **ID token** returned from the server (not arbitrary path).
- ApiService maps token → file path from the allowed directory and opens it read-only.
- Use EF Core (or direct SQLite query) **read-only** against the selected DB.
- Provide endpoints:
  - `GET /api/dbs`
  - `GET /api/dbs/{id}/sessions`
  - `GET /api/dbs/{id}/processes`
  - `GET /api/dbs/{id}/events` with query params:
    - `type`, `op`, `pid`, `contains` (search in payload/path), `from`, `to`, `page`, `pageSize`, `sort`
  - `GET /api/dbs/{id}/stats` with query params:
    - `pid`, `from`, `to`, `page`, `pageSize`
- Implement pagination everywhere.

### React UX
- Nice UI with:
  - DB picker page
  - Tabs: Sessions, Processes, Events, Stats
  - Events page supports:
    - search box
    - filters (type/op/pid/date range)
    - paging
    - details panel for selected event (show JsonPayload pretty-printed)
- Use best practices:
  - fetch wrapper + error handling
  - loading states
  - debounce search input
  - keep URL query params in sync (optional but preferred)

### Aspire integration best practices
- Use Aspire configuration/service discovery for ApiService base URL.
- In AppHost, wire Web → ApiService reference appropriately.
- Avoid hard-coded ports; let Aspire assign.

---

## Data persistence (SQLite + EF Core + Migrations)
For ProcWatch capture DB:
- Append-only schema
- Batched inserts via Channel
- EF migrations applied at startup via `MigrationService`

Tables:
- `MonitoredSession`
- `ProcessInstance`
- `EventRecord`
- `StatsSample`

For ApiService reading:
- Open selected DB **read-only**
- Avoid migrations/changes on existing capture DBs (do not apply migrations to historical DBs; treat them as immutable).

---

## Monitoring implementation (Windows-only)
- Primary: ETW via `Microsoft.Diagnostics.Tracing.TraceEvent`
- Capture: file, registry, image load, network
- Network fallback: snapshot connections
- Track child processes via WMI/Toolhelp
- Stats sampled every interval

---

## CI (GitHub Actions) — SUPPORTED
When asked to add CI:
- Add `.github/workflows/build.yml`
- Trigger on `push` and `pull_request`
- Install latest .NET 10
- Run: restore, build Release, test Release
- Exclude Aspire-specific workflow content from referenced sample unless explicitly required
- Verify locally before finalizing
- Persist CI learnings to Memorizer

---

## Deliverables
Produce a complete working repo that:
- Builds with `dotnet build`
- Runs via Aspire AppHost (MonitorService + ApiService + React Web + optional CLI)
- CLI uses System.CommandLine + Spectre.Console (live + event browser)
- Uses Aspire-style DbContext wiring and MigrationService for the capture DB
- React Web can list/select/browse historical `.sqlite` capture DBs via ApiService
- Works on Windows 11+

Include README:
- How to run AppHost
- How to run CLI monitoring
- Where DB files are stored
- How web UI discovers DBs and browses data
- Limitations (ETW privilege, network fallback, etc.)

---

## Acceptance checklist
- [ ] `.github\aspire13.md` loaded if present and followed
- [ ] Existing memories loaded via Memorizer; new ones persisted
- [ ] Step-by-step plan created and followed; each verifiable step verified
- [ ] `procwatch monitor --help` works via System.CommandLine
- [ ] Polished Spectre.Console dashboard + event browser with scrolling
- [ ] Capture DB created via EF Core + migrations + MigrationService (Aspire pattern)
- [ ] Aspire AppHost runs MonitorService + ApiService + React Web
- [ ] Web app lists historical `.sqlite` files, allows selecting one, and browsing/searching/filtering data
- [ ] `dotnet build` and `dotnet test` succeed
