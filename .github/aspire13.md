# aspire-13-instructions.md

This file exists to teach LLMs how to correctly build, modify, and reason about
Aspire 13 applications. Aspire 13 is new, evolving, and not well-represented in
most model training data, so the rules below must be followed exactly.

Assume this file is authoritative over general Aspire/.NET knowledge.

--------------------------------------------------------------------

WHAT ASPIRE 13 IS (MENTAL MODEL)

- Aspire 13 is an application orchestration platform, not just a .NET library.
- It coordinates:
  - .NET services
  - JavaScript (Vite) frontends
  - Databases
  - Containers
  - Local and production deployments
- Aspire owns wiring, configuration, lifecycle, and deployment shape.
- Individual services own their internal implementation.

Aspire 13 introduces:
- Polyglot first-class support (JavaScript via Aspire.Hosting.JavaScript)
- Pipeline-based build/deploy using `aspire do`
- Strong conventions around references instead of manual configuration

--------------------------------------------------------------------

APPHOST FORMAT (CRITICAL: VISUAL STUDIO COMPATIBILITY)

DEFAULT RULE (MUST FOLLOW)

- Always use a project-based AppHost (.csproj) by default.
- Assume the user is using Visual Studio.

Why:
- Single-file AppHosts (apphost.cs using #:sdk / #:package) do NOT work correctly
  in Visual Studio.
- Visual Studio tooling expects .csproj files for restore, build, debug, and
  design-time services.

SINGLE-FILE APPHOST (EXPLICIT OPT-IN ONLY)

- Only use a single-file AppHost if the user explicitly asks for it.
- Never infer or assume this format.

Explicit opt-in phrases include:
- "Use a single-file AppHost"
- "Use apphost.cs"
- "Use the file-based AppHost"
- "Prefer the single-file Aspire AppHost format"

DECISION RULE

If the user does NOT explicitly request a single-file AppHost:
- Use a project-based AppHost (.csproj)

--------------------------------------------------------------------

CANONICAL APPHOST PATTERNS (ASPIRE 13)

- Every AppHost must:
  - Call DistributedApplication.CreateBuilder(args)
  - Add resources (projects, databases, frontends)
  - Wire dependencies using references
  - Call builder.Build().Run()

DEPENDENCY WIRING

- Use .WithReference(...) to connect services.
- Do NOT manually pass:
  - connection strings
  - ports
  - hostnames
- Aspire injects configuration automatically based on references.

STARTUP ORDERING

- Use .WaitFor(...) when startup order matters.
- Common cases:
  - API waits for database
  - Frontend waits for API

--------------------------------------------------------------------

JAVASCRIPT / VITE FRONTEND RULES

- Use Aspire.Hosting.JavaScript.
- Use AddViteApp(...) for Vite-based frontends.
- Frontends typically require:
  - WithExternalHttpEndpoints()
  - WithReference(apiService)
  - WaitFor(apiService)

PRODUCTION FRONTEND PATTERN (FROM REAL REPO)

- Bundle frontend output into the API container:
  - api.PublishWithContainerFiles(web, "./wwwroot")
- This produces a single deployable container serving API and static files.

--------------------------------------------------------------------

DATABASE + EF CORE (ASPIRE 13 WAY)

SEPARATION OF RESPONSIBILITIES

- AppHost:
  - Defines database resources
  - Names them
  - Controls lifetime and persistence
- Consuming services:
  - Configure EF Core
  - Own DbContext behavior

PREFERRED EF REGISTRATION (HAPPY PATH)

In consuming services (API, worker, migration service):

builder.AddSqlServerDbContext<TodoDbContext>("tododb");

- The string name MUST match the database resource name in the AppHost.
- Aspire provides connection strings, pooling, diagnostics, and health checks.

--------------------------------------------------------------------

EF CORE MIGRATIONS (REQUIRED PATTERN)

- Use a dedicated MigrationService.
- Do NOT run migrations inside the API.

MIGRATIONSERVICE RULES (PROVEN IN PRACTICE)

- Reference the shared DbContext/data project.
- Register EF using AddSqlServerDbContext.
- Run context.Database.Migrate() at startup.

CRITICAL GOTCHA

- Do NOT include:
  - appsettings.json
  - appsettings.Development.json
  - launchSettings.json

These files caused failures during:
- aspire do publish
- CI pipelines

Removing them fixed publishing issues.

--------------------------------------------------------------------

DOCKER + DEPLOYMENT

DOCKER COMPOSE ENVIRONMENT

When generating Docker Compose output, add to AppHost:

builder.AddDockerComposeEnvironment("env");

GENERATED OUTPUT

- Aspire writes deployment artifacts to:
  - aspire-output/
- This folder should:
  - be gitignored
  - optionally be uploaded as a CI artifact

CI PATTERN (REAL WORLD)

- Run:
  - aspire do deploy (or aspire deploy fallback)
- Publish:
  - aspire-output/docker-compose.yaml

--------------------------------------------------------------------

ASPIRE CLI & PIPELINES

LOCAL DEVELOPMENT

- aspire run
  - Runs the app locally
  - Opens the Aspire dashboard

PIPELINES (ASPIRE 13)

- aspire do introduces pipeline-based execution.
- Pipelines can:
  - build
  - publish
  - deploy
  - run migrations
- Steps can be executed independently.

Do NOT assume Aspire 9.x publishing behavior still applies.

--------------------------------------------------------------------

LLM DO / DO NOT RULES (STRICT)

DO
- Assume Visual Studio is used
- Default to project-based AppHost
- Use references instead of manual configuration
- Use a dedicated MigrationService
- Follow resource naming consistently
- Use AddViteApp for Vite frontends
- Use AddSqlServerDbContext for EF Core

DO NOT
- Do NOT default to single-file AppHost
- Do NOT hardcode connection strings
- Do NOT manually wire services via env vars
- Do NOT mix migration logic into APIs
- Do NOT add config files to MigrationService
- Do NOT assume older Aspire docs apply

--------------------------------------------------------------------

SUMMARY RULE

Aspire 13 is opinionated.
Follow its conventions instead of fighting them.

When in doubt, prefer:
- project-based AppHost
- references over configuration
- explicit services over clever shortcuts

This file overrides generic LLM knowledge.
