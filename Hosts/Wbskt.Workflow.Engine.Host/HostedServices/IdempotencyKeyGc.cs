using Microsoft.Extensions.Options;
using Wbskt.Workflow.Abstraction.Configuration;
using Wbskt.Workflow.Abstraction.Engine;
using Wbskt.Workflow.Abstraction.Providers;
using Wbskt.Workflow.Abstraction.Runtime;

namespace Wbskt.Workflow.Engine.Host.HostedServices;

/// <summary>
/// Deletes old idempotency-claim rows so the table doesn't grow unbounded. Every inbound event
/// inserts a claim row; once it's older than the retention window the dedup it provided is no
/// longer needed (the source event won't be redelivered after that long), so it can be reaped.
/// Lease-gated so only one host runs it.
/// </summary>
public sealed class IdempotencyKeyGc : BackgroundService
{
    private const string LeaseName = "idempotency-gc";
    private const int BatchSize = 5000;
    private readonly IClock _clock;
    private readonly ILeaseHolder _leaseHolder;
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ILogger<IdempotencyKeyGc> _logger;
    private readonly TimeSpan _pollInterval;
    private readonly TimeSpan _retentionWindow;

    [ActivatorUtilitiesConstructor]
    public IdempotencyKeyGc(
        IClock clock,
        ILeaseHolder leaseHolder,
        IServiceScopeFactory scopeFactory,
        ILogger<IdempotencyKeyGc> logger,
        IOptions<WorkflowEngineOptions> options)
        : this(clock, leaseHolder, scopeFactory, logger, options.Value.IdempotencyRetentionInterval, options.Value.IdempotencyRetentionWindow)
    {
    }

    internal IdempotencyKeyGc(
        IClock clock,
        ILeaseHolder leaseHolder,
        IIdempotencyKeyProvider idempotencyKeyProvider,
        ILogger<IdempotencyKeyGc> logger,
        TimeSpan? pollInterval = null,
        TimeSpan? retentionWindow = null)
        : this(clock, leaseHolder, new StaticScopeFactory(idempotencyKeyProvider), logger, pollInterval, retentionWindow)
    {
    }

    private IdempotencyKeyGc(
        IClock clock,
        ILeaseHolder leaseHolder,
        IServiceScopeFactory scopeFactory,
        ILogger<IdempotencyKeyGc> logger,
        TimeSpan? pollInterval,
        TimeSpan? retentionWindow)
    {
        _clock = clock;
        _leaseHolder = leaseHolder;
        _scopeFactory = scopeFactory;
        _logger = logger;
        _pollInterval = pollInterval ?? TimeSpan.FromHours(1);
        _retentionWindow = retentionWindow ?? TimeSpan.FromHours(24);
    }

    public async Task ProcessRetentionAsync(CancellationToken ct)
    {
        if (!await _leaseHolder.IsHeldAsync(LeaseName, ct))
        {
            return;
        }

        await using AsyncServiceScope scope = _scopeFactory.CreateAsyncScope();
        var idempotencyKeyProvider = scope.ServiceProvider.GetRequiredService<IIdempotencyKeyProvider>();
        DateTime cutoffUtc = _clock.UtcNow - _retentionWindow;
        while (true)
        {
            int deleted = await idempotencyKeyProvider.DeleteExpiredAsync(cutoffUtc, BatchSize, ct);
            if (deleted < BatchSize)
            {
                return;
            }
        }
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        await ExecuteGuardedAsync(stoppingToken);

        using var timer = new PeriodicTimer(_pollInterval);
        while (await timer.WaitForNextTickAsync(stoppingToken))
        {
            await ExecuteGuardedAsync(stoppingToken);
        }
    }

    private async Task ExecuteGuardedAsync(CancellationToken stoppingToken)
    {
        try
        {
            await ProcessRetentionAsync(stoppingToken);
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Idempotency key GC tick failed.");
        }
    }

    private sealed class StaticScopeFactory(IIdempotencyKeyProvider idempotencyKeyProvider) : IServiceScopeFactory
    {
        public IServiceScope CreateScope()
        {
            return new StaticServiceScope(idempotencyKeyProvider);
        }
    }

    private sealed class StaticServiceScope(IIdempotencyKeyProvider idempotencyKeyProvider) : IServiceScope, IAsyncDisposable
    {
        public IServiceProvider ServiceProvider { get; } = new StaticServiceProvider(idempotencyKeyProvider);

        public void Dispose()
        {
        }

        public ValueTask DisposeAsync()
        {
            return ValueTask.CompletedTask;
        }
    }

    private sealed class StaticServiceProvider(IIdempotencyKeyProvider idempotencyKeyProvider) : IServiceProvider
    {
        public object? GetService(Type serviceType)
        {
            return serviceType == typeof(IIdempotencyKeyProvider) ? idempotencyKeyProvider : null;
        }
    }
}
