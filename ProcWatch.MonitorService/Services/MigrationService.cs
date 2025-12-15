using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using ProcWatch.MonitorService.Data;

namespace ProcWatch.MonitorService.Services;

public class MigrationService : IHostedService
{
    private readonly IServiceProvider _serviceProvider;
    private readonly ILogger<MigrationService> _logger;

    public MigrationService(IServiceProvider serviceProvider, ILogger<MigrationService> logger)
    {
        _serviceProvider = serviceProvider;
        _logger = logger;
    }

    public async Task StartAsync(CancellationToken cancellationToken)
    {
        using var scope = _serviceProvider.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<ProcWatchDbContext>();

        try
        {
            _logger.LogInformation("Applying database migrations...");
            await dbContext.Database.MigrateAsync(cancellationToken);
            _logger.LogInformation("Database migrations applied successfully");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error applying database migrations");
            throw;
        }
    }

    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;
}
