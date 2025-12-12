using System.Threading.Channels;
using Microsoft.EntityFrameworkCore;
using ProcWatch.MonitorService.Data;
using ProcWatch.MonitorService.Data.Entities;

namespace ProcWatch.MonitorService.Services;

public class EventIngestor : IDisposable
{
    private readonly ILogger<EventIngestor> _logger;
    private readonly IServiceProvider _serviceProvider;
    private readonly Channel<object> _eventChannel;
    private readonly Task _processingTask;
    private readonly CancellationTokenSource _cts;

    public EventIngestor(ILogger<EventIngestor> logger, IServiceProvider serviceProvider)
    {
        _logger = logger;
        _serviceProvider = serviceProvider;
        _eventChannel = Channel.CreateBounded<object>(new BoundedChannelOptions(10000)
        {
            FullMode = BoundedChannelFullMode.Wait
        });
        _cts = new CancellationTokenSource();
        _processingTask = ProcessEventsAsync(_cts.Token);
    }

    public async Task EnqueueEventRecordAsync(EventRecord eventRecord, CancellationToken cancellationToken = default)
    {
        await _eventChannel.Writer.WriteAsync(eventRecord, cancellationToken);
    }

    public async Task EnqueueStatsSampleAsync(StatsSample statsSample, CancellationToken cancellationToken = default)
    {
        await _eventChannel.Writer.WriteAsync(statsSample, cancellationToken);
    }

    private async Task ProcessEventsAsync(CancellationToken cancellationToken)
    {
        var eventBatch = new List<EventRecord>();
        var statsBatch = new List<StatsSample>();
        using var batchTimer = new PeriodicTimer(TimeSpan.FromSeconds(2));

        try
        {
            while (!cancellationToken.IsCancellationRequested)
            {
                var hasData = false;

                // Read up to 100 items or until timeout
                for (int i = 0; i < 100 && !cancellationToken.IsCancellationRequested; i++)
                {
                    var readTask = _eventChannel.Reader.ReadAsync(cancellationToken).AsTask();
                    var timerTask = batchTimer.WaitForNextTickAsync(cancellationToken).AsTask();
                    
                    var completedTask = await Task.WhenAny(readTask, timerTask);
                    
                    if (completedTask == readTask)
                    {
                        var item = await readTask;
                        hasData = true;
                        
                        if (item is EventRecord eventRecord)
                        {
                            eventBatch.Add(eventRecord);
                        }
                        else if (item is StatsSample statsSample)
                        {
                            statsBatch.Add(statsSample);
                        }
                    }
                    else
                    {
                        // Timer fired, process batch
                        break;
                    }
                }

                // Write batch if we have data
                if (hasData && (eventBatch.Count > 0 || statsBatch.Count > 0))
                {
                    await WriteBatchAsync(eventBatch, statsBatch, cancellationToken);
                    eventBatch.Clear();
                    statsBatch.Clear();
                }
            }
        }
        catch (OperationCanceledException)
        {
            _logger.LogInformation("Event processing stopped");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error in event processing loop");
        }
        finally
        {
            // Flush remaining events
            if (eventBatch.Count > 0 || statsBatch.Count > 0)
            {
                await WriteBatchAsync(eventBatch, statsBatch, CancellationToken.None);
            }
        }
    }

    private async Task WriteBatchAsync(List<EventRecord> events, List<StatsSample> stats, CancellationToken cancellationToken)
    {
        try
        {
            using var scope = _serviceProvider.CreateScope();
            var dbContext = scope.ServiceProvider.GetRequiredService<ProcWatchDbContext>();

            var previousAutoDetect = dbContext.ChangeTracker.AutoDetectChangesEnabled;
            dbContext.ChangeTracker.AutoDetectChangesEnabled = false;

            try
            {
                if (events.Count > 0)
                {
                    await dbContext.EventRecords.AddRangeAsync(events, cancellationToken);
                }
                
                if (stats.Count > 0)
                {
                    await dbContext.StatsSamples.AddRangeAsync(stats, cancellationToken);
                }

                await dbContext.SaveChangesAsync(cancellationToken);
                
                _logger.LogDebug("Wrote batch: {EventCount} events, {StatsCount} stats", events.Count, stats.Count);
            }
            finally
            {
                dbContext.ChangeTracker.AutoDetectChangesEnabled = previousAutoDetect;
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error writing batch to database");
        }
    }

    public async Task FlushAsync(CancellationToken cancellationToken = default)
    {
        _eventChannel.Writer.Complete();
        await _processingTask;
    }

    public void Dispose()
    {
        _cts.Cancel();
        _processingTask.Wait(TimeSpan.FromSeconds(5));
        _cts.Dispose();
    }
}
