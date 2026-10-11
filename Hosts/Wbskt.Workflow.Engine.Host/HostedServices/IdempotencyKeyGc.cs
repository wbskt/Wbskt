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
/// Runs on the active engine only.
/// </summary>
public sealed class IdempotencyKeyGc(
    IClock clock,
    ILeadershipGate leadership,
    IServiceScopeFactory scopeFactory,
    ILogger<IdempotencyKeyGc> logger,
    IOptions<WorkflowEngineOptions> options) : LeaderPeriodicWorker(leadership, scopeFactory, logger)
{
    private const int BatchSize = 5000;
    private readonly TimeSpan _retentionWindow = options.Value.IdempotencyRetentionWindow;

    protected override string Name => "Idempotency key GC";

    protected override TimeSpan Interval { get; } = options.Value.IdempotencyRetentionInterval;

    protected override async Task RunOnceAsync(IServiceProvider services, CancellationToken ct)
    {
        var idempotencyKeyProvider = services.GetRequiredService<IIdempotencyKeyProvider>();
        DateTime cutoffUtc = clock.UtcNow - _retentionWindow;
        while (true)
        {
            int deleted = await idempotencyKeyProvider.DeleteExpiredAsync(cutoffUtc, BatchSize, ct);
            if (deleted < BatchSize)
            {
                return;
            }
        }
    }
}
