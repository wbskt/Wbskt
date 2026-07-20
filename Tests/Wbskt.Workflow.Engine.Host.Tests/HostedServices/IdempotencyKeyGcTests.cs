using Microsoft.Extensions.Logging.Abstractions;
using Wbskt.Workflow.Abstraction.Engine;
using Wbskt.Workflow.Abstraction.Entities;
using Wbskt.Workflow.Abstraction.Providers;
using Wbskt.Workflow.Abstraction.Runtime;
using Wbskt.Workflow.Engine.Host.HostedServices;

namespace Wbskt.Workflow.Engine.Host.Tests.HostedServices;

public sealed class IdempotencyKeyGcTests
{
    [Fact]
    public async Task Tick_deletes_in_batches_until_caught_up()
    {
        var leaseHolder = new RecordingLeaseHolder(isHeld: true);
        var provider = new RecordingIdempotencyKeyProvider([5000, 42]);
        var gc = new IdempotencyKeyGc(new FixedClock(), leaseHolder, provider, NullLogger<IdempotencyKeyGc>.Instance);

        await gc.ProcessRetentionAsync(CancellationToken.None);

        Assert.Equal(2, provider.DeleteCalls.Count);
        // Cutoff = now - 24h (the default retention window).
        Assert.All(provider.DeleteCalls, call => Assert.Equal((new DateTime(2026, 5, 25, 12, 30, 0, DateTimeKind.Utc), 5000), call));
    }

    [Fact]
    public async Task Tick_skips_when_lease_not_held()
    {
        var leaseHolder = new RecordingLeaseHolder(isHeld: false);
        var provider = new RecordingIdempotencyKeyProvider([5000]);
        var gc = new IdempotencyKeyGc(new FixedClock(), leaseHolder, provider, NullLogger<IdempotencyKeyGc>.Instance);

        await gc.ProcessRetentionAsync(CancellationToken.None);

        Assert.Equal(["idempotency-gc"], leaseHolder.IsHeldCalls);
        Assert.Empty(provider.DeleteCalls);
    }

    private sealed class FixedClock : IClock
    {
        public DateTime UtcNow => new(2026, 5, 26, 12, 30, 0, DateTimeKind.Utc);
    }

    private sealed class RecordingLeaseHolder(bool isHeld) : ILeaseHolder
    {
        public List<string> IsHeldCalls { get; } = [];

        public Task<bool> TryAcquireAsync(string leaseName, CancellationToken ct) => Task.FromResult(isHeld);
        public Task ReleaseAsync(string leaseName, CancellationToken ct) => Task.CompletedTask;

        public Task<bool> IsHeldAsync(string leaseName, CancellationToken ct)
        {
            IsHeldCalls.Add(leaseName);
            return Task.FromResult(isHeld);
        }
    }

    private sealed class RecordingIdempotencyKeyProvider(IReadOnlyCollection<int> deletes) : IIdempotencyKeyProvider
    {
        private readonly Queue<int> _deletes = new(deletes);

        public List<(DateTime CutoffUtc, int BatchSize)> DeleteCalls { get; } = [];

        public Task<int> DeleteExpiredAsync(DateTime cutoffUtc, int batchSize, CancellationToken ct)
        {
            DeleteCalls.Add((cutoffUtc, batchSize));
            return Task.FromResult(_deletes.Dequeue());
        }

        public Task<IdempotencyKeyRow> UpsertPendingAsync(string keyValue, int runId, Guid branchRefId, Guid nodeId, int attempt, CancellationToken ct) => throw new NotSupportedException();
        public Task<IdempotencyKeyRow> GetByKeyAsync(string keyValue, CancellationToken ct) => throw new NotSupportedException();
        public Task<IdempotencyKeyRow> MarkSucceededAsync(string keyValue, string resultJson, CancellationToken ct) => Task.FromResult<IdempotencyKeyRow>(null!);
        public Task<IdempotencyKeyRow> MarkFailedAsync(string keyValue, string errorJson, CancellationToken ct) => throw new NotSupportedException();
        public Task<IdempotencyKeyRow> ReclaimFailedAsync(string keyValue, Guid newBranchRefId, CancellationToken ct) => throw new NotSupportedException();
    }
}
