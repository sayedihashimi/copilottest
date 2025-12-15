using System.Threading.Channels;
using ProcWatch.MonitorService.Data;
using ProcWatch.MonitorService.Data.Entities;

namespace ProcWatch.MonitorService.Services;

public class EventIngestor : IDisposable
{
    private readonly IServiceProvider _serviceProvider;
    private readonly ILogger<EventIngestor> _logger;
    private readonly Channel<EventRecord> _channel;
    private readonly CancellationTokenSource _cts = new();
    private readonly Task _writerTask;
    private const int BatchSize = 100;
    private static readonly TimeSpan BatchTimeout = TimeSpan.FromSeconds(2);

    public EventIngestor(IServiceProvider serviceProvider, ILogger<EventIngestor> logger)
    {
        _serviceProvider = serviceProvider;
        _logger = logger;
        
        // Bounded channel to prevent memory issues
        _channel = Channel.CreateBounded<EventRecord>(new BoundedChannelOptions(10000)
        {
            FullMode = BoundedChannelFullMode.DropOldest
        });

        _writerTask = Task.Run(() => ProcessEventsAsync(_cts.Token));
    }

    public async Task EnqueueEventAsync(EventRecord eventRecord)
    {
        await _channel.Writer.WaitToWriteAsync();
        await _channel.Writer.WriteAsync(eventRecord);
    }

    private async Task ProcessEventsAsync(CancellationToken cancellationToken)
    {
        var batch = new List<EventRecord>(BatchSize);

        while (!cancellationToken.IsCancellationRequested)
        {
            try
            {
                batch.Clear();

                // Read first event with timeout
                using var timeoutCts = new CancellationTokenSource(BatchTimeout);
                using var linkedCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, timeoutCts.Token);

                try
                {
                    // Wait for first event
                    var firstEvent = await _channel.Reader.ReadAsync(linkedCts.Token);
                    batch.Add(firstEvent);

                    // Try to read more events up to batch size without blocking
                    while (batch.Count < BatchSize && _channel.Reader.TryRead(out var nextEvent))
                    {
                        batch.Add(nextEvent);
                    }
                }
                catch (OperationCanceledException) when (timeoutCts.Token.IsCancellationRequested)
                {
                    // Timeout - flush whatever we have
                    if (batch.Count == 0)
                        continue;
                }

                // Write batch to database
                if (batch.Count > 0)
                {
                    await WriteBatchAsync(batch, cancellationToken);
                }
            }
            catch (OperationCanceledException)
            {
                break;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error processing event batch");
                await Task.Delay(1000, cancellationToken);
            }
        }

        // Flush remaining events
        while (_channel.Reader.TryRead(out var remainingEvent))
        {
            batch.Add(remainingEvent);
            if (batch.Count >= BatchSize)
            {
                await WriteBatchAsync(batch, CancellationToken.None);
                batch.Clear();
            }
        }

        if (batch.Count > 0)
        {
            await WriteBatchAsync(batch, CancellationToken.None);
        }
    }

    private async Task WriteBatchAsync(List<EventRecord> batch, CancellationToken cancellationToken)
    {
        using var scope = _serviceProvider.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<ProcWatchDbContext>();

        try
        {
            // Disable change tracking for better performance
            dbContext.ChangeTracker.AutoDetectChangesEnabled = false;

            await dbContext.EventRecords.AddRangeAsync(batch, cancellationToken);
            await dbContext.SaveChangesAsync(cancellationToken);

            _logger.LogDebug("Wrote batch of {Count} events to database", batch.Count);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error writing event batch to database");
        }
        finally
        {
            dbContext.ChangeTracker.AutoDetectChangesEnabled = true;
        }
    }

    public async Task FlushAsync()
    {
        _channel.Writer.Complete();
        await _writerTask;
    }

    public void Dispose()
    {
        _cts.Cancel();
        _channel.Writer.Complete();
        try
        {
            _writerTask.Wait(TimeSpan.FromSeconds(5));
        }
        catch
        {
            // Ignore timeout
        }
        _cts.Dispose();
    }
}
