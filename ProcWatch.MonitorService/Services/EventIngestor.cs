using System.Threading.Channels;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using ProcWatch.MonitorService.Data;
using ProcWatch.MonitorService.Data.Entities;

namespace ProcWatch.MonitorService.Services;

public class EventIngestor : IDisposable
{
    private readonly ILogger<EventIngestor> _logger;
    private readonly IServiceProvider _serviceProvider;
    private readonly Channel<EventRecord> _eventChannel;
    private readonly Channel<StatsSample> _statsChannel;
    private readonly int _maxEvents;
    
    private Task? _eventWriterTask;
    private Task? _statsWriterTask;
    private CancellationTokenSource? _cts;
    private long _totalEventsEnqueued = 0;
    private long _totalStatsEnqueued = 0;

    public EventIngestor(ILogger<EventIngestor> logger, IServiceProvider serviceProvider, int maxEvents)
    {
        _logger = logger;
        _serviceProvider = serviceProvider;
        _maxEvents = maxEvents;

        _eventChannel = Channel.CreateBounded<EventRecord>(new BoundedChannelOptions(10000)
        {
            FullMode = BoundedChannelFullMode.DropOldest
        });

        _statsChannel = Channel.CreateBounded<StatsSample>(new BoundedChannelOptions(1000)
        {
            FullMode = BoundedChannelFullMode.DropOldest
        });
    }

    public void Start()
    {
        _cts = new CancellationTokenSource();
        _eventWriterTask = Task.Run(async () => await EventWriterLoop(_cts.Token));
        _statsWriterTask = Task.Run(async () => await StatsWriterLoop(_cts.Token));
        _logger.LogInformation("Event ingestor started");
    }

    public async Task EnqueueEventRecordAsync(EventRecord eventRecord, CancellationToken cancellationToken = default)
    {
        if (Interlocked.Read(ref _totalEventsEnqueued) < _maxEvents)
        {
            await _eventChannel.Writer.WriteAsync(eventRecord, cancellationToken);
            Interlocked.Increment(ref _totalEventsEnqueued);
        }
    }

    public async Task EnqueueStatsSampleAsync(StatsSample sample, CancellationToken cancellationToken = default)
    {
        await _statsChannel.Writer.WriteAsync(sample, cancellationToken);
        Interlocked.Increment(ref _totalStatsEnqueued);
    }

    private async Task EventWriterLoop(CancellationToken cancellationToken)
    {
        var batch = new List<EventRecord>(100);

        while (!cancellationToken.IsCancellationRequested)
        {
            try
            {
                batch.Clear();

                // Wait for first item or timeout
                using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
                timeoutCts.CancelAfter(TimeSpan.FromSeconds(2));

                try
                {
                    batch.Add(await _eventChannel.Reader.ReadAsync(timeoutCts.Token));

                    // Try to fill batch quickly
                    while (batch.Count < 100 && _eventChannel.Reader.TryRead(out var item))
                    {
                        batch.Add(item);
                    }
                }
                catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
                {
                    // Timeout - write what we have if any
                    if (batch.Count == 0)
                        continue;
                }

                await WriteBatchToDatabase(batch, cancellationToken);
            }
            catch (OperationCanceledException)
            {
                break;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error in event writer loop");
                await Task.Delay(1000, cancellationToken);
            }
        }

        // Final flush
        await FlushRemainingEvents(cancellationToken);
    }

    private async Task StatsWriterLoop(CancellationToken cancellationToken)
    {
        var batch = new List<StatsSample>(50);

        while (!cancellationToken.IsCancellationRequested)
        {
            try
            {
                batch.Clear();

                using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
                timeoutCts.CancelAfter(TimeSpan.FromSeconds(2));

                try
                {
                    batch.Add(await _statsChannel.Reader.ReadAsync(timeoutCts.Token));

                    while (batch.Count < 50 && _statsChannel.Reader.TryRead(out var item))
                    {
                        batch.Add(item);
                    }
                }
                catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
                {
                    if (batch.Count == 0)
                        continue;
                }

                await WriteStatsBatchToDatabase(batch, cancellationToken);
            }
            catch (OperationCanceledException)
            {
                break;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error in stats writer loop");
                await Task.Delay(1000, cancellationToken);
            }
        }

        await FlushRemainingStats(cancellationToken);
    }

    private async Task WriteBatchToDatabase(List<EventRecord> batch, CancellationToken cancellationToken)
    {
        if (batch.Count == 0)
            return;

        using var scope = _serviceProvider.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<ProcWatchDbContext>();

        dbContext.ChangeTracker.AutoDetectChangesEnabled = false;
        await dbContext.EventRecords.AddRangeAsync(batch, cancellationToken);
        await dbContext.SaveChangesAsync(cancellationToken);
        dbContext.ChangeTracker.AutoDetectChangesEnabled = true;

        _logger.LogDebug("Wrote {Count} events to database", batch.Count);
    }

    private async Task WriteStatsBatchToDatabase(List<StatsSample> batch, CancellationToken cancellationToken)
    {
        if (batch.Count == 0)
            return;

        using var scope = _serviceProvider.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<ProcWatchDbContext>();

        dbContext.ChangeTracker.AutoDetectChangesEnabled = false;
        await dbContext.StatsSamples.AddRangeAsync(batch, cancellationToken);
        await dbContext.SaveChangesAsync(cancellationToken);
        dbContext.ChangeTracker.AutoDetectChangesEnabled = true;

        _logger.LogDebug("Wrote {Count} stats samples to database", batch.Count);
    }

    private async Task FlushRemainingEvents(CancellationToken cancellationToken)
    {
        var batch = new List<EventRecord>();
        while (_eventChannel.Reader.TryRead(out var item))
        {
            batch.Add(item);
        }

        if (batch.Count > 0)
        {
            await WriteBatchToDatabase(batch, cancellationToken);
            _logger.LogInformation("Flushed {Count} remaining events", batch.Count);
        }
    }

    private async Task FlushRemainingStats(CancellationToken cancellationToken)
    {
        var batch = new List<StatsSample>();
        while (_statsChannel.Reader.TryRead(out var item))
        {
            batch.Add(item);
        }

        if (batch.Count > 0)
        {
            await WriteStatsBatchToDatabase(batch, cancellationToken);
            _logger.LogInformation("Flushed {Count} remaining stats", batch.Count);
        }
    }

    public long GetTotalEventsEnqueued() => Interlocked.Read(ref _totalEventsEnqueued);
    public long GetTotalStatsEnqueued() => Interlocked.Read(ref _totalStatsEnqueued);

    public void Dispose()
    {
        _cts?.Cancel();
        _eventChannel.Writer.Complete();
        _statsChannel.Writer.Complete();

        try
        {
            Task.WhenAll(_eventWriterTask ?? Task.CompletedTask, _statsWriterTask ?? Task.CompletedTask)
                .Wait(TimeSpan.FromSeconds(10));
        }
        catch (AggregateException)
        {
            // Expected during cancellation
        }

        _cts?.Dispose();
    }
}
