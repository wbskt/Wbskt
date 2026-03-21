using System.Data;
using System.Runtime.CompilerServices;
using System.Threading.Channels;
using Microsoft.Extensions.Options;
using Wbskt.Common.Abstraction.Interfaces;
using Wbskt.Common.Models;
using Wbskt.Management.Host.Models;

namespace Wbskt.Management.Host.Services;

public class DatabaseBatchFlusherService : BackgroundService
{
    // Sentinel: timer writes this to wake the read loop for a flush
    private static readonly EventLogEntry TimerFlushSignal = new(-1, string.Empty, DateTime.MinValue, null);

    private readonly Channel<EventLogEntry> _timerSignalChannel;
    private readonly EventLogBuffer _buffer;
    private readonly IServiceProvider _serviceProvider;
    private readonly ILogger<DatabaseBatchFlusherService> _logger;
    private readonly int _batchSize;
    private readonly TimeSpan _flushInterval;
    private const int MaxRetryAttempts = 3;

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
        _flushInterval = TimeSpan.FromSeconds(options.Value.FlushIntervalLimitInSeconds);
        _timerSignalChannel = Channel.CreateBounded<EventLogEntry>(1);
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation("DatabaseBatchFlusherService started.");

        await using var timer = new Timer(
            _ => _timerSignalChannel.Writer.TryWrite(TimerFlushSignal),
            null,
            _flushInterval,
            _flushInterval); // Repeating — no need to reset manually

        var batch = new List<EventLogEntry>(_batchSize);

        try
        {
            // Merge real entries and timer signals into one async stream
            var merged = MergeAsync(_buffer.ReadAllAsync(stoppingToken), _timerSignalChannel.Reader.ReadAllAsync(stoppingToken), stoppingToken);

            await foreach (var entry in merged)
            {
                if (!ReferenceEquals(entry, TimerFlushSignal))
                {
                    batch.Add(entry);
                }

                var batchFull = batch.Count >= _batchSize;
                var timerFired = ReferenceEquals(entry, TimerFlushSignal);

                if ((batchFull || timerFired) && batch.Count > 0)
                {
                    await FlushBatchAsync(batch, stoppingToken);
                    batch.Clear();
                }
            }
        }
        catch (OperationCanceledException) { }
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

    private static async IAsyncEnumerable<EventLogEntry> MergeAsync(
        IAsyncEnumerable<EventLogEntry> primary,
        IAsyncEnumerable<EventLogEntry> secondary,
        [EnumeratorCancellation] CancellationToken ct)
    {
        var primaryTask = primary.GetAsyncEnumerator(ct);
        var secondaryTask = secondary.GetAsyncEnumerator(ct);

        // Simple merge: use Task.WhenAny over MoveNextAsync tasks
        var primaryNext = primaryTask.MoveNextAsync().AsTask();
        var secondaryNext = secondaryTask.MoveNextAsync().AsTask();

        while (true)
        {
            var done = await Task.WhenAny(primaryNext, secondaryNext);

            if (done == primaryNext)
            {
                if (!await primaryNext)
                {
                    yield break;
                }

                yield return primaryTask.Current;
                primaryNext = primaryTask.MoveNextAsync().AsTask();
            }
            else
            {
                if (!await secondaryNext)
                {
                    yield break;
                }

                yield return secondaryTask.Current;
                secondaryNext = secondaryTask.MoveNextAsync().AsTask();
            }
        }
    }

    private async Task FlushBatchAsync(List<EventLogEntry> batch, CancellationToken cancellationToken)
    {
        if (batch.Count == 0)
        {
            return;
        }

        var dataTable = BuildDataTable(batch);

        for (var attempt = 1; attempt <= MaxRetryAttempts; attempt++)
        {
            try
            {
                using var scope = _serviceProvider.CreateScope();
                var eventProvider = scope.ServiceProvider.GetRequiredService<IEventProvider>();
                await eventProvider.InsertBatchAsync(dataTable, cancellationToken);
                return;
            }
            catch (OperationCanceledException) { throw; }
            catch (Exception ex) when (attempt < MaxRetryAttempts)
            {
                _logger.LogWarning(ex, "Flush attempt {Attempt}/{Max} failed. Retrying...", attempt, MaxRetryAttempts);
                await Task.Delay(TimeSpan.FromSeconds(Math.Pow(2, attempt)), cancellationToken);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to flush {Count} events after {Max} attempts. Batch lost.", batch.Count, MaxRetryAttempts);
            }
        }
    }

    private static DataTable BuildDataTable(List<EventLogEntry> batch)
    {
        var dt = new DataTable();
        dt.Columns.Add("EventId",      typeof(int));
        dt.Columns.Add("EventData",    typeof(string));
        dt.Columns.Add("CreatedAtUtc", typeof(DateTime));
        dt.Columns.Add("WorkspaceId",  typeof(int));
        dt.Columns.Add("PolicyId",     typeof(int));
        dt.Columns.Add("PolicyRefId",  typeof(Guid));
        dt.Columns.Add("ClientId",     typeof(int));
        dt.Columns.Add("ClientRefId",  typeof(Guid));
        dt.Columns.Add("WorkflowId",   typeof(int));
        dt.Columns.Add("WorkflowRefId",typeof(Guid));

        foreach (var item in batch)
        {
            dt.Rows.Add(
                item.EventId,
                item.EventData,
                item.CreatedAtUtc,
                ZeroOrNullToDbNull(item.WorkspaceId),
                ZeroOrNullToDbNull(item.PolicyId),
                GuidOrDbNull(item.PolicyRefId),
                ZeroOrNullToDbNull(item.ClientId),
                GuidOrDbNull(item.ClientRefId),
                ZeroOrNullToDbNull(item.WorkflowId),
                GuidOrDbNull(item.WorkflowRefId)
            );
        }

        return dt;
    }
    
    private static object ZeroOrNullToDbNull(object? value) =>
        value is null || value.Equals(0) ? DBNull.Value : value;
    
    private static object GuidOrDbNull(Guid? value) =>
        value is null || value == Guid.Empty ? DBNull.Value : value;
}