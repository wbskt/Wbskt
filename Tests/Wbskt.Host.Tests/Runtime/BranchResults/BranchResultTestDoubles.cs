using System.Text.Json;
using Microsoft.Extensions.Logging;
using Wbskt.Workflow.Abstraction.Entities;
using Wbskt.Workflow.Abstraction.Models;
using Wbskt.Workflow.Abstraction.Models.Nodes;
using Wbskt.Workflow.Abstraction.Models.Nodes.Actions;
using Wbskt.Workflow.Abstraction.Providers;
using Wbskt.Workflow.Abstraction.Runtime;
using Wbskt.Workflow.Runtime;
using Wbskt.Workflow.Runtime.BranchResults;

namespace Wbskt.Host.Tests.Runtime.BranchResults;

// What the result handler tests share: one branch of one run, fakes that write what they were asked
// to do into one log (so a test can pin the order of a handler's side effects), and nothing else -
// no branch loop, no socket, no database.

internal static class BranchResultTestData
{
    public const int RunId = 42;
    public const long BranchId = 1001;
    public static readonly Guid RunRefId = Guid.Parse("eeeeeeee-0000-0000-0000-000000000042");
    public static readonly Guid BranchRefId = Guid.Parse("bbbbbbbb-0000-0000-0000-000000001001");
    public static readonly Guid WorkflowRefId = Guid.Parse("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa");
    public static readonly DateTime Now = new(2026, 5, 26, 12, 0, 0, DateTimeKind.Utc);

    public static TestNode Node(Guid nodeId) => new() { NodeId = nodeId, Name = "node", Ports = [], KindValue = "test" };

    public static WorkflowDefinition Definition(
        IReadOnlyCollection<BaseNode> nodes,
        IReadOnlyCollection<Edge> edges,
        bool runCompensationOnFailure = false,
        bool failFast = false)
    {
        return new WorkflowDefinition(WorkflowRefId, 1, 9, "branch-results", null, true, nodes, edges, [], Now, 7, runCompensationOnFailure, failFast);
    }

    public static RunRow Run() => new()
    {
        Id = RunId,
        RefId = RunRefId,
        WorkflowDefinitionId = 9,
        WorkflowRefId = WorkflowRefId,
        WorkflowVersion = 1,
        TriggerNodeId = Guid.Empty,
        CorrelationKey = null,
        Status = "Running",
        StartedAt = Now,
        CompletedAt = null,
        CancellationRequestedAt = null,
        CancellationReason = null,
        CreditBudget = 100m,
        CreatedAt = Now
    };

    public static BranchRow Branch(Guid nodeId, string localJson = "{}") => new()
    {
        Id = (int)BranchId,
        RefId = BranchRefId,
        RunId = RunId,
        ParentBranchId = null,
        ForkCohortId = null,
        NodeId = nodeId,
        Status = BranchStatus.Active,
        PendingTakePort = null,
        LocalJson = localJson,
        LastOutputJson = "\"last\"",
        CreatedAt = Now,
        UpdatedAt = Now,
        RowVersion = [1]
    };

    public static BranchResultContext Context(BaseNode node, WorkflowDefinition definition, BranchRow branch)
    {
        var nodeContext = new BranchContext(
            RunId,
            BranchId,
            9,
            WorkflowRefId,
            1,
            node.NodeId.ToString(),
            1,
            BranchLocalState.Deserialize(branch.LocalJson),
            new Dictionary<string, JsonElement>(),
            string.Empty,
            Now,
            9)
        {
            RunRefId = RunRefId,
            BranchRefId = branch.RefId
        };

        return new BranchResultContext(RunId, BranchId, Run(), branch, definition, node, nodeContext);
    }

    public static IReadOnlyDictionary<string, JsonElement> Patch(string key, object value) =>
        new Dictionary<string, JsonElement> { [key] = JsonSerializer.SerializeToElement(value) };

    public static IReadOnlyDictionary<string, JsonElement> LocalState(string json) => BranchLocalState.Deserialize(json);
}

internal sealed record TestNode : BaseNode
{
    public required string KindValue { get; init; }

    public override string Kind => KindValue;
}

internal sealed record TestActionNode : BaseActionNode
{
    public override string Kind => "test:action";
}

/// <summary>A branch store holding the one branch under test, writing every change to the log.</summary>
internal sealed class RecordingBranchProvider(List<string> log, BranchRow current) : IBranchProvider
{
    private int _nextId = 5000;

    // Settable so a test can put the branch back where a crash left it and replay a handler.
    public BranchRow Current { get; set; } = current;

    public List<(long BranchId, Guid NodeId, string Status, string LocalJson, string? LastOutputJson)> Updates { get; } = [];

    public List<BranchRow> Created { get; } = [];

    public List<(long BranchId, string? ErrorJson)> Failed { get; } = [];

    public Task<BranchRow> UpdatePointerAsync(long branchId, Guid currentNodeId, string status, string localJson, string? lastOutputJson, CancellationToken ct)
    {
        log.Add($"update:{status}:{currentNodeId}");
        Updates.Add((branchId, currentNodeId, status, localJson, lastOutputJson));
        Current = Current with { NodeId = currentNodeId, Status = status, LocalJson = localJson, LastOutputJson = lastOutputJson, RowVersion = [(byte)(Updates.Count + 1)] };
        return Task.FromResult(Current);
    }

    public Task<BranchRow> CreateAsync(BranchRow row, CancellationToken ct)
    {
        BranchRow created = row with { Id = _nextId++ };
        log.Add($"create:{created.NodeId}");
        Created.Add(created);
        return Task.FromResult(created);
    }

    /// <summary>Children created through <see cref="CreateIfAbsentAsync"/>, by RefId, so a replayed Fork finds them.</summary>
    private readonly Dictionary<Guid, BranchRow> _byRefId = [];

    public List<(long BranchId, string Status)> RefusedEnds { get; } = [];

    /// <summary>
    /// Honours the guard the way Branch_EndIfLive does: a branch that is no longer Active or Waiting is
    /// refused and nothing is written. The writes it does make go through the
    /// unguarded recorders, so the log reads as it did before the guard.
    /// </summary>
    public async Task<bool> EndIfLiveAsync(long branchId, string toStatus, string? lastOutputJson, CancellationToken ct)
    {
        if (!BranchStatus.IsLive(Current.Status))
        {
            log.Add($"end-refused:{toStatus}");
            RefusedEnds.Add((branchId, toStatus));
            return false;
        }

        _ = toStatus switch
        {
            BranchStatus.Completed => await SetCompletedAsync(branchId, ct),
            BranchStatus.Failed => await SetFailedAsync(branchId, lastOutputJson, ct),
            _ => await UpdatePointerAsync(branchId, Current.NodeId, toStatus, Current.LocalJson, lastOutputJson, ct)
        };
        return true;
    }

    public async Task<(BranchRow Row, bool Created)> CreateIfAbsentAsync(BranchRow row, CancellationToken ct)
    {
        if (_byRefId.TryGetValue(row.RefId, out BranchRow? existing))
        {
            log.Add($"exists:{existing.NodeId}");
            return (existing, false);
        }

        BranchRow created = await CreateAsync(row, ct);
        _byRefId[created.RefId] = created;
        return (created, true);
    }

    /// <summary>Forgets a child, as if the crash that cut a Fork short came before it was written.</summary>
    public void Forget(Guid refId) => _byRefId.Remove(refId);

    public Task<BranchRow> SetCompletedAsync(long branchId, CancellationToken ct)
    {
        log.Add("completed");
        Current = Current with { Status = BranchStatus.Completed };
        return Task.FromResult(Current);
    }

    public Task<BranchRow> SetFailedAsync(long branchId, string? lastOutputJson, CancellationToken ct)
    {
        log.Add("failed");
        Failed.Add((branchId, lastOutputJson));
        Current = Current with { Status = BranchStatus.Failed, LastOutputJson = lastOutputJson };
        return Task.FromResult(Current);
    }

    public Task<BranchRow> UpsertAsync(BranchRow row, CancellationToken ct) => throw new NotSupportedException();

    public Task<BranchRow> GetByIdAsync(long branchId, CancellationToken ct) => throw new NotSupportedException();

    public Task<BranchRow> GetByRefIdAsync(Guid refId, CancellationToken ct) => throw new NotSupportedException();

    public Task<IReadOnlyCollection<BranchRow>> GetAllByRunIdAsync(int runId, CancellationToken ct) => throw new NotSupportedException();

    public Task<IReadOnlyCollection<BranchRow>> GetRunningBranchesAsync(CancellationToken ct) => throw new NotSupportedException();
}

internal sealed class RecordingRunJournal(List<string> log) : IRunJournal
{
    public List<RunJournalEntry> Entries { get; } = [];

    public Task AppendAsync(RunJournalEntry entry, CancellationToken ct)
    {
        log.Add($"journal:{entry.EventKind}");
        Entries.Add(entry);
        return Task.CompletedTask;
    }

    public Task<IReadOnlyCollection<HistoryEventRow>> ReadAllAsync(int runId, CancellationToken ct) =>
        Task.FromResult<IReadOnlyCollection<HistoryEventRow>>([]);
}

internal sealed class RecordingBranchTerminator(List<string> log) : IBranchTerminator
{
    public Task<int> ReleaseAsync(int runId, int count, CancellationToken ct)
    {
        log.Add($"release:{runId}:{count}");
        return Task.FromResult(0);
    }
}

internal sealed class RecordingRunDispatcher(List<string> log) : IRunDispatcher
{
    public List<BranchExecutionRequest> Requests { get; } = [];

    public ValueTask DispatchAsync(BranchExecutionRequest request, CancellationToken ct)
    {
        log.Add($"dispatch:{request.BranchId}");
        Requests.Add(request);
        return ValueTask.CompletedTask;
    }
}

internal sealed class RecordingRunCountersProvider(List<string> log) : IRunCountersProvider
{
    public Task<int> IncrementActiveBranchesAsync(int runId, int delta, CancellationToken ct)
    {
        log.Add($"increment:{runId}:{delta}");
        return Task.FromResult(delta);
    }

    public Task<RunCountersRow> GetByRunIdAsync(int runId, CancellationToken ct) => throw new NotSupportedException();

    public Task<int> DecrementActiveBranchesAsync(int runId, int delta, CancellationToken ct)
    {
        log.Add($"decrement:{runId}:{delta}");
        return Task.FromResult(0);
    }

    public Task<CreditCharge?> TryChargeAsync(int runId, decimal cost, CancellationToken ct) => throw new NotSupportedException();

    public Task<long> SumActiveBranchesAsync(CancellationToken ct) => throw new NotSupportedException();
}

internal sealed class FixedClock : TimeProvider
{
    public override DateTimeOffset GetUtcNow() => new(BranchResultTestData.Now);
}

internal sealed class SequentialIdGenerator : IIdGenerator
{
    private int _next;

    public Guid NewId() => new($"00000000-0000-0000-0000-{++_next:D12}");
}

/// <summary>Keeps the warnings it is given, so a test can pin that a refused end was logged.</summary>
internal sealed class WarningRecorder<T> : ILogger<T>
{
    public List<string> Warnings { get; } = [];

    public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;

    public bool IsEnabled(LogLevel logLevel) => true;

    public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
    {
        if (logLevel == LogLevel.Warning)
        {
            Warnings.Add(formatter(state, exception));
        }
    }
}
