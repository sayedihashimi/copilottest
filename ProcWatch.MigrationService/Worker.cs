using Microsoft.EntityFrameworkCore;
using ProcWatch.MonitorService.Data;

namespace ProcWatch.MigrationService;

/// <summary>
/// Applies EF Core migrations at startup
/// </summary>
public class MigrationWorker(
    ILogger<MigrationWorker> logger,
    IServiceProvider serviceProvider) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        logger.LogInformation("Starting database migration...");

        try
        {
            using var scope = serviceProvider.CreateScope();
            var dbContext = scope.ServiceProvider.GetRequiredService<ProcWatchDbContext>();

            await dbContext.Database.MigrateAsync(stoppingToken);

            logger.LogInformation("Database migration completed successfully.");
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Database migration failed.");
            throw;
        }
    }
}

/// <summary>
/// Shuts down the application after migrations complete
/// </summary>
public class ShutdownWorker(
    IHostApplicationLifetime lifetime,
    ILogger<ShutdownWorker> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        // Wait a bit for migration to complete
        await Task.Delay(5000, stoppingToken);

        logger.LogInformation("Migration service completed. Shutting down.");
        lifetime.StopApplication();
    }
}
