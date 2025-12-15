WHAT ASPIRE 13 IS (CRITICAL CONTEXT)

- Aspire 13 is a polyglot application platform, not just “.NET Aspire”.
- It supports first-class orchestration for:
  - .NET
  - JavaScript (Vite)
  - Python
- Aspire 13 favors file-based AppHosts instead of .csproj-based AppHosts.
- Build, publish, and deploy workflows are driven by pipelines via `aspire do`.

LLMs trained before Aspire 13 often get this wrong by:
- Assuming Aspire is .NET-only
- Assuming AppHosts must be .csproj-based
- Using obsolete publish or deployment patterns

This file exists to correct those assumptions.

======================================================================

APPHOST: PREFERRED FILE-BASED STRUCTURE

Use a file-based AppHost whenever possible.

Example header:

    #:sdk Aspire.AppHost.Sdk@13.0.0
    #:package Aspire.Hosting.SqlServer@13.0.0
    #:package Aspire.Hosting.JavaScript@13.0.0

    var builder = DistributedApplication.CreateBuilder(args);

There is no .csproj required for a file-based AppHost.

======================================================================

CORE APPHOST PRIMITIVES (USE CONSISTENTLY)

- DistributedApplication.CreateBuilder(args)
- AddProject(...)               -> .NET services
- AddViteApp(...)               -> Vite frontends
- AddSqlServer(...), AddDatabase(...) -> infrastructure
- WithReference(...)            -> dependency wiring + config injection
- WaitFor(...)                  -> startup ordering
- WithExternalHttpEndpoints()   -> expose endpoints outside Aspire network
- WithHttpHealthCheck(...)      -> health checks

Avoid inventing custom patterns unless absolutely necessary.

======================================================================

EXAMPLE: API + SQL SERVER + VITE FRONTEND

    var sql = builder.AddSqlServer("todosqlserver")
                     .WithLifetime(ContainerLifetime.Persistent);

    var db = sql.AddDatabase("tododb");

    var api = builder.AddProject("apiservice", "../src/My.ApiService")
                     .WithReference(db)
                     .WaitFor(db)
                     .WithHttpHealthCheck("/health");

    var web = builder.AddViteApp("web", "../src/my-frontend")
                     .WithReference(api)
                     .WaitFor(api)
                     .WithExternalHttpEndpoints();

    api.PublishWithContainerFiles(web, "./wwwroot");

    builder.Build().Run();

Important notes:
- The database name ("tododb") is critical.
- Aspire injects ConnectionStrings:tododb automatically.
- Consumers must use the same name.

======================================================================

EF CORE IN ASPIRE 13 (THE CORRECT MODEL)

Key rule:
- EF Core is configured in the consuming service, NOT in the AppHost.
- The AppHost owns the database resource.
- The service owns EF Core configuration.

Recommended default:

    builder.AddSqlServerDbContext<TodoDbContext>("tododb");

This:
- Pulls the connection string by name
- Enables pooling, tracing, and health checks by default

Use standard AddDbContext / AddDbContextPool only when you need full control.

======================================================================

EF CORE MIGRATIONS (PROVEN PATTERN)

Recommended approach:
- Create a dedicated MigrationService (worker).
- Reference the shared DbContext project.
- Register the DbContext using AddSqlServerDbContext("tododb").
- Run context.Database.Migrate() on startup.

Lessons learned from todojsaspire:
- appsettings.json and launchSettings.json in the MigrationService can break
  `aspire do publish`.
- Removing those files fixed pipeline failures.
- Keep migration services minimal and configuration-free.

======================================================================

ASPIRE PIPELINES (aspire do)

Aspire 13 replaces older publish/deploy patterns with pipelines.

Common commands:
- aspire run
- aspire do build
- aspire do publish
- aspire do deploy
- aspire do <step-name>

Pipelines allow:
- Dependency-aware execution
- Running only specific steps (e.g., migrations)
- CI-friendly workflows

Do NOT rely on legacy publish callbacks.

======================================================================

DOCKER COMPOSE + CI PATTERNS

Common AppHost pattern when generating Docker artifacts:

    builder.AddDockerComposeEnvironment("env");

Generated output:
- aspire-output/

Best practices:
- Add aspire-output/ to .gitignore
- Treat generated docker-compose.yaml as a build artifact
- CI can upload aspire-output/docker-compose.yaml for deployment

This pattern is used successfully in todojsaspire.

======================================================================

JAVASCRIPT / VITE HOSTING NOTES

- Aspire.Hosting.NodeJs was renamed to Aspire.Hosting.JavaScript.
- AddViteApp(...) is the preferred API for Vite projects.
- Frontends usually need WithExternalHttpEndpoints() so they are reachable
  from a browser during local development.

======================================================================

LLM DO / DO NOT CHECKLIST

DO:
- Prefer file-based AppHosts
- Use WithReference(...) instead of hardcoded config
- Match database resource names exactly
- Use aspire do pipelines for build/publish/deploy
- Keep migration services minimal

DO NOT:
- Assume Aspire is .NET-only
- Assume AppHosts must use .csproj
- Hardcode connection strings
- Keep stray appsettings.json files in migration services
- Use pre–Aspire 13 publish patterns

======================================================================

END OF FILE
