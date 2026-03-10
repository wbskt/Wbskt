using System.Data;
using Microsoft.Extensions.Options;
using Wbskt.Common.Abstraction.Interfaces;
using Wbskt.Common.Models;
using Wbskt.Management.Host.Models;

namespace Wbskt.Management.Host.Services;

public class DatabaseBatchFlusherService : BackgroundService
{
    private readonly EventLogBuffer _buffer;
    private readonly IServiceProvider _serviceProvider;
    private readonly ILogger<DatabaseBatchFlusherService> _logger;
    private readonly int _batchSize;
    private readonly TimeSpan _flushIntervalLimit;

    public DatabaseBatchFlusherService(
        EventLogBuffer buffer,
        IServiceProvider serviceProvider,
        IOptions<EventLoggingOptions> options,
        ILogger<DatabaseBatchFlusherService> logger)
    {
        _buffer = buffer;
        _serviceProvider = serviceProvider;
        _logger = logger;
        _batchSize = options.Value.BatchSize;
        _flushIntervalLimit = TimeSpan.FromSeconds(options.Value.FlushIntervalLimit);
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation("DatabaseBatchFlusherService started.");

        var batch = new List<EventLogEntry>(_batchSize);
        var tcs = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        using var timer = new Timer(_ => tcs.TrySetResult(), null, _flushIntervalLimit, Timeout.InfiniteTimeSpan);

        try
        {
            await foreach (var entry in _buffer.ReadAllAsync(stoppingToken))
            {
                batch.Add(entry);

                if (batch.Count >= _batchSize || tcs.Task.IsCompleted)
                {
                    await FlushBatchAsync(batch, stoppingToken);
                    batch.Clear();

                    tcs = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
                    timer.Change(_flushIntervalLimit, Timeout.InfiniteTimeSpan);
                }
            }
        }
        catch (OperationCanceledException)
        {
            // Expected on shutdown
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Unexpected error in DatabaseBatchFlusherService loop.");
        }
        finally
        {
            if (batch.Count > 0)
            {
                await FlushBatchAsync(batch, CancellationToken.None);
            }
            _logger.LogInformation("DatabaseBatchFlusherService stopped.");
        }
    }

    private async Task FlushBatchAsync(List<EventLogEntry> batch, CancellationToken cancellationToken)
    {
        if (batch.Count == 0) return;

        try
        {
            using var dataTable = new DataTable();
            dataTable.Columns.Add("EventId", typeof(int));
            dataTable.Columns.Add("EventData", typeof(string));
            dataTable.Columns.Add("CreatedAtUtc", typeof(DateTime));
            dataTable.Columns.Add("WorkspaceId", typeof(int));

            foreach (var item in batch)
            {
                dataTable.Rows.Add(item.EventId, item.EventData, item.CreatedAtUtc, (object?)item.WorkspaceId ?? DBNull.Value);
            }

            using var scope = _serviceProvider.CreateScope();
            var eventProvider = scope.ServiceProvider.GetRequiredService<IEventProvider>();

            await eventProvider.InsertBatchAsync(dataTable, cancellationToken);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to flush {Count} event logs. Logs in this batch are lost.", batch.Count);
        }
    }
}
