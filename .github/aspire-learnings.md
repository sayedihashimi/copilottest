# .NET Aspire Learnings

A comprehensive collection of learnings and best practices from working with .NET Aspire, compiled from project implementation sessions.

---

## Table of Contents

1. [Project Structure](#project-structure)
2. [AppHost Configuration](#apphost-configuration)
3. [ServiceDefaults](#servicedefaults)
4. [MigrationService Pattern](#migrationservice-pattern)
5. [Frontend Integration](#frontend-integration)
6. [Database Integration](#database-integration)
7. [Aspire CLI](#aspire-cli)
8. [GitHub Actions CI/CD](#github-actions-cicd)
9. [Package Versions](#package-versions)
10. [Key Lessons Learned](#key-lessons-learned)

---

## Project Structure

### Standard Aspire Solution Layout

A typical Aspire 13 solution follows this structure:

```
MySolution/
├── MySolution.sln
├── MySolution.AppHost/          # Aspire orchestrator
├── MySolution.ServiceDefaults/  # Shared Aspire configuration
├── MySolution.Api/              # ASP.NET Core Web API
├── MySolution.Web/              # Frontend (React/Angular/Vue)
├── MySolution.Services/         # Shared services/libraries
└── MySolution.MigrationService/ # EF Core migration runner
```

### Project Templates

```powershell
# Create Aspire projects
dotnet new aspire-apphost -n MyApp.AppHost
dotnet new aspire-servicedefaults -n MyApp.ServiceDefaults
```

---

## AppHost Configuration

### Basic AppHost Setup

```csharp
var builder = DistributedApplication.CreateBuilder(args);

// Add database
var db = builder.AddSqlServer("sql")
    .AddDatabase("mydb");

// Add API service
var apiService = builder.AddProject<Projects.MyApp_Api>("api")
    .WithReference(db);

// Add frontend
builder.AddViteApp("frontend", "../MyApp.Web")
    .WithReference(apiService)
    .WaitFor(apiService)
    .WithExternalHttpEndpoints();

builder.Build().Run();
```

### Docker Compose Integration

```csharp
// Use Docker Compose environment
builder.AddDockerComposeEnvironment("env");
```

### Publishing Frontend with API

```csharp
// Publish frontend static files with the API container
apiService.PublishWithContainerFiles(frontend, "./wwwroot");
```

### Using References

- Use `.WithReference()` instead of manual connection strings
- Aspire automatically handles service discovery and configuration

---

## ServiceDefaults

### Configuration

The ServiceDefaults project should have the MSBuild property:

```xml
<PropertyGroup>
    <IsAspireSharedProject>true</IsAspireSharedProject>
</PropertyGroup>
```

### Usage in Services

All services should call:

```csharp
builder.AddServiceDefaults();
// ... configure services ...
app.MapDefaultEndpoints();
```

---

## MigrationService Pattern

### Critical Rules

**⚠️ IMPORTANT: MigrationService must NOT have `appsettings.json` or `launchSettings.json` files!**

Having these files causes Aspire publish failures.

### Implementation

```csharp
// Program.cs for MigrationService
var builder = Host.CreateApplicationBuilder(args);
builder.AddServiceDefaults();

// Get connection string from Aspire configuration
builder.Services.AddDbContext<MyDbContext>(options =>
    options.UseSqlite(builder.Configuration.GetConnectionString("mydb")));

builder.Services.AddHostedService<MigrationWorker>();

var app = builder.Build();
app.Run();
```

### Worker Implementation

```csharp
public class MigrationWorker : BackgroundService
{
    private readonly IServiceProvider _serviceProvider;
    private readonly IHostApplicationLifetime _lifetime;

    public MigrationWorker(IServiceProvider serviceProvider, IHostApplicationLifetime lifetime)
    {
        _serviceProvider = serviceProvider;
        _lifetime = lifetime;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using var scope = _serviceProvider.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<MyDbContext>();
        
        await dbContext.Database.MigrateAsync(stoppingToken);
        
        // Stop the application after migration completes
        _lifetime.StopApplication();
    }
}
```

### Connection String Pattern

Connection strings come from Aspire configuration:
```
ConnectionStrings:mydb
```

---

## Frontend Integration

### React with Vite

Use `AddViteApp` for React applications:

```csharp
var frontend = builder.AddViteApp("frontend", "../MyApp.Web")
    .WithReference(apiService)
    .WaitFor(apiService)
    .WithExternalHttpEndpoints();
```

#### Vite Proxy Configuration (vite.config.js)

```javascript
export default defineConfig({
  server: {
    proxy: {
      '/api': {
        target: process.env.APISERVICE_HTTPS || process.env.APISERVICE_HTTP,
        changeOrigin: true,
        secure: false
      }
    }
  }
});
```

### Angular

Use `AddJavaScriptApp` (NOT `AddViteApp` or `AddNpmApp`) for Angular applications:

```csharp
var frontend = builder.AddJavaScriptApp("frontend", "../MyAngularApp.Web", "start")
    .WithReference(apiService)
    .WaitFor(apiService)
    .WithExternalHttpEndpoints();
```

**Note:** Requires `Aspire.Hosting.JavaScript` package version 13.2.0 or later.

#### Angular Proxy Configuration (proxy.conf.js)

```javascript
const target = process.env.APISERVICE_HTTPS || process.env.APISERVICE_HTTP;

module.exports = {
  '/api': {
    target: target,
    secure: false,
    changeOrigin: true
  }
};
```

Update `angular.json` to use the proxy:

```json
{
  "serve": {
    "options": {
      "proxyConfig": "proxy.conf.js"
    }
  }
}
```

### Key Differences

| Feature | React (Vite) | Angular |
|---------|--------------|---------|
| Method | `AddViteApp` | `AddJavaScriptApp` |
| Default Dev Port | 5173 | 4200 |
| Build Output | `dist/` | `dist/<project>/browser/` |
| Proxy Config | `vite.config.js` | `proxy.conf.js` |

---

## Database Integration

### EF Core with Design-Time Factory

For migrations to work, create a design-time factory:

```csharp
public class MyDbContextFactory : IDesignTimeDbContextFactory<MyDbContext>
{
    public MyDbContext CreateDbContext(string[] args)
    {
        var optionsBuilder = new DbContextOptionsBuilder<MyDbContext>();
        optionsBuilder.UseSqlite("Data Source=mydb.db");
        return new MyDbContext(optionsBuilder.Options);
    }
}
```

### Database Schema Best Practices

- Use append-only schema for events/stats
- Add proper indexes on query columns
- Use cascade delete relationships where appropriate
- Consider batched writes via Channels for high-throughput scenarios

---

## Aspire CLI

### Multi-App Repository Support

When working with repositories containing multiple Aspire apps, use the `--project` option:

```bash
# Specify the exact AppHost project
aspire do build --project path/to/AppHost.csproj --verbose
```

**⚠️ Without `--project`, the CLI uses the default from `.aspire/settings.json`**, which causes all builds to use the same project.

### Common Commands

```bash
# Build container images
aspire do build --verbose

# Run the application
aspire run

# Build specific project
aspire do build --project MyApp.AppHost/MyApp.AppHost.csproj --verbose
```

---

## GitHub Actions CI/CD

### Matrix Strategy for Multiple Apps

For repositories with multiple Aspire apps (React, Angular, Vue, etc.):

```yaml
jobs:
  build:
    strategy:
      matrix:
        include:
          - app: react
            solution: react/MyReactApp.sln
            apphost-dir: react/MyReactApp.AppHost
          - app: angular
            solution: angular/MyAngularApp.sln
            apphost-dir: angular/MyAngularApp.AppHost
    
    steps:
      - uses: actions/checkout@v4
      
      - name: Setup .NET
        uses: actions/setup-dotnet@v4
        with:
          dotnet-version: '10.0.x'
      
      - name: Install Aspire CLI
        run: dotnet tool install -g aspire-cli
      
      - name: Restore
        run: dotnet restore ${{ matrix.solution }}
      
      - name: Build
        run: dotnet build ${{ matrix.solution }} --no-restore
      
      - name: Aspire Build
        run: |
          aspire do build --project ${{ matrix.apphost-dir }}/*.csproj --verbose
```

---

## Package Versions

### Aspire 13 Packages (as of Dec 2025)

| Package | Version |
|---------|---------|
| Aspire SDK | 13.1.0-preview.1.25557.15 |
| Aspire.Hosting.Docker | 13.1.0-preview.1.25570.9 |
| Aspire.Hosting.JavaScript | 13.2.0-preview.1.25615.7 |
| Aspire.Hosting.AppHost | 13.0.0 |

### Compatible .NET Packages

| Package | Version |
|---------|---------|
| Microsoft.EntityFrameworkCore.Sqlite | 10.0.1 |
| Microsoft.EntityFrameworkCore.Design | 10.0.1 |
| Target Framework | net10.0 |

### Templates

```powershell
# Check installed templates
dotnet new list aspire

# Update templates
dotnet new install Aspire.ProjectTemplates::13.0.2
```

---

## Key Lessons Learned

### 1. MigrationService Configuration

**❌ DON'T** include `appsettings.json` or `launchSettings.json` in MigrationService projects - this causes Aspire publish failures.

**✅ DO** let the MigrationService receive all configuration from Aspire's distributed configuration system.

### 2. Angular vs Vite Apps

**❌ DON'T** use `AddViteApp` for Angular applications - it's specific to Vite.

**✅ DO** use `AddJavaScriptApp` for Angular apps with the correct package version.

### 3. Connection Strings

**❌ DON'T** hardcode connection strings or use `appsettings.json` for database connections.

**✅ DO** use `.WithReference()` in AppHost and retrieve via `Configuration.GetConnectionString()`.

### 4. Multi-App CLI Commands

**❌ DON'T** run `aspire do build` without specifying the project in multi-app repos.

**✅ DO** use `--project` option to specify the exact AppHost project.

### 5. ServiceDefaults Property

**✅ DO** add `<IsAspireSharedProject>true</IsAspireSharedProject>` to ServiceDefaults.csproj.

### 6. Frontend Proxy Configuration

**✅ DO** use environment variables (`process.env.APISERVICE_HTTPS || process.env.APISERVICE_HTTP`) for dynamic proxy configuration.

### 7. EF Core Migrations

**✅ DO** use `IDesignTimeDbContextFactory` for design-time migration generation.

**✅ DO** use a dedicated MigrationService that runs migrations at startup and then shuts down.

### 8. Project References

**✅ DO** use Aspire's project-based AppHost (not single-file) for Visual Studio compatibility.

### 9. Docker Publishing

**✅ DO** use `PublishAsDockerFile()` for deployment containers.

**✅ DO** use `PublishWithContainerFiles(frontend, "./wwwroot")` to bundle frontend with API.

---

## Common Issues and Solutions

### Issue: Aspire build uses wrong project

**Solution:** Use `--project` flag with the specific AppHost path.

### Issue: MigrationService fails on publish

**Solution:** Remove `appsettings.json` and `launchSettings.json` from the project.

### Issue: Angular dev proxy not working

**Solution:** Ensure `proxy.conf.js` reads from `process.env.APISERVICE_*` and is referenced in `angular.json`.

### Issue: Connection string not found

**Solution:** Ensure the resource name in AppHost matches what's requested via `GetConnectionString()`.

---

*Last updated: December 2025*
*Based on learnings from .NET 10 + Aspire 13 projects*
