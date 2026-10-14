using System.Collections.Concurrent;
using Wbskt.Workflow.Abstraction.Entities;
using Wbskt.Workflow.Abstraction.Providers;
using Wbskt.Workflow.Abstraction.Runtime;

namespace Wbskt.Host.Tests;

// Stand-ins for the runtime collaborators the engine always has, for tests that build the branch loop,
// the retry executor or the cancellation service by hand and are not about these collaborators.

/// <summary>An idempotency store that behaves like the real one: first claim wins, outcomes are kept.</summary>
internal sealed class InMemoryIdempotencyKeyProvider : IIdempotencyKeyProvider
{
    private readonly ConcurrentDictionary<string, IdempotencyKeyRow> _rows = new(StringComparer.Ordinal);
    private long _nextId;

    public IReadOnlyCollection<IdempotencyKeyRow> Rows => _rows.Values.ToArray();

    public Task<IdempotencyKeyRow> UpsertPendingAsync(string keyValue, int runId, Guid branchRefId, Guid nodeId, int attempt, CancellationToken ct) =>
        Task.FromResult(_rows.GetOrAdd(keyValue, key => new IdempotencyKeyRow
        {
            Id = Interlocked.Increment(ref _nextId),
            KeyValue = key,
            RunId = runId,
            BranchRefId = branchRefId,
            NodeId = nodeId,
            Attempt = attempt,
            Status = "Pending",
            ResultJson = null,
            ErrorJson = null,
            CreatedAt = DateTime.UtcNow,
            CompletedAt = null
        }));

    public Task<IdempotencyKeyRow> GetByKeyAsync(string keyValue, CancellationToken ct) =>
        _rows.TryGetValue(keyValue, out IdempotencyKeyRow? row) ? Task.FromResult(row) : Task.FromException<IdempotencyKeyRow>(new KeyNotFoundException(keyValue));

    public Task<IdempotencyKeyRow> MarkSucceededAsync(string keyValue, string resultJson, CancellationToken ct) =>
        Task.FromResult(_rows.AddOrUpdate(keyValue, _ => throw new KeyNotFoundException(keyValue), (_, row) => row with { Status = "Succeeded", ResultJson = resultJson, CompletedAt = DateTime.UtcNow }));

    public Task<IdempotencyKeyRow> MarkFailedAsync(string keyValue, string errorJson, CancellationToken ct) =>
        Task.FromResult(_rows.AddOrUpdate(keyValue, _ => throw new KeyNotFoundException(keyValue), (_, row) => row with { Status = "Failed", ErrorJson = errorJson, CompletedAt = DateTime.UtcNow }));

    public Task<IdempotencyKeyRow> ReclaimFailedAsync(string keyValue, Guid newBranchRefId, CancellationToken ct) =>
        Task.FromResult(_rows.AddOrUpdate(keyValue, _ => throw new KeyNotFoundException(keyValue), (_, row) =>
            row.Status == "Failed" ? row with { Status = "Pending", BranchRefId = newBranchRefId, ErrorJson = null, CompletedAt = null } : row));

    public Task<int> DeleteExpiredAsync(DateTime cutoffUtc, int batchSize, CancellationToken ct) => Task.FromResult(0);
}

internal sealed class NoOpRunFinalizer : IRunFinalizer
{
    public Task FinalizeAsync(long runId, CancellationToken ct) => Task.CompletedTask;
}

internal sealed class NoOpRunCancellationService : IRunCancellationService
{
    public Task<bool> RequestCancellationAsync(long runId, string reason, CancellationToken ct) => Task.FromResult(false);

    public Task<bool> IsCancellationRequestedAsync(long runId, CancellationToken ct) => Task.FromResult(false);
}

internal sealed class NoOpCompensationOrchestrator : ICompensationOrchestrator
{
    public Task RunAsync(long runId, long branchId, CancellationToken ct) => Task.CompletedTask;
}

/// <summary>A join store for runs with no join: nothing to initialize, and no cohort to contribute to.</summary>
internal sealed class NoOpJoinAggregatorProvider : IJoinAggregatorProvider
{
    public Task InitializeAsync(Guid joinToken, int runId, int expectedCount, string mode, int quorumCount, Guid? joinNodeId, CancellationToken ct) => Task.CompletedTask;

    public Task<JoinContributionResult> ContributeAsync(Guid joinToken, long branchId, string outcome, CancellationToken ct) =>
        Task.FromResult(new JoinContributionResult(false, 0, 0, 0, 0));

    public Task DeleteAllByRunIdAsync(int runId, CancellationToken ct) => Task.CompletedTask;
}

/// <summary>Run counters with an unlimited budget: every charge succeeds, nothing is left running.</summary>
internal sealed class UnlimitedRunCountersProvider : IRunCountersProvider
{
    public Task<RunCountersRow> GetByRunIdAsync(int runId, CancellationToken ct) =>
        Task.FromResult(new RunCountersRow { RunId = runId, ActiveBranchCount = 0, CreditsConsumed = 0m, UpdatedAt = DateTime.UtcNow });

    public Task<int> IncrementActiveBranchesAsync(int runId, int delta, CancellationToken ct) => Task.FromResult(0);

    public Task<int> DecrementActiveBranchesAsync(int runId, int delta, CancellationToken ct) => Task.FromResult(0);

    public Task<bool> TryChargeAsync(int runId, decimal cost, CancellationToken ct) => Task.FromResult(true);

    public Task<long> SumActiveBranchesAsync(CancellationToken ct) => Task.FromResult(0L);
}
