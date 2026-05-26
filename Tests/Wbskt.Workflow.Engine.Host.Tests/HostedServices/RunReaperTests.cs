using Microsoft.Extensions.Logging.Abstractions;
using Wbskt.Workflow.Abstraction.Engine;
using Wbskt.Workflow.Abstraction.Entities;
using Wbskt.Workflow.Abstraction.Providers;
using Wbskt.Workflow.Abstraction.Runtime;
using Wbskt.Workflow.Engine.Host.HostedServices;

namespace Wbskt.Workflow.Engine.Host.Tests.HostedServices;

public sealed class RunReaperTests
{
    [Fact]
    public async Task Tick_skips_when_lease_not_held()
    {
        // Arrange
        var leaseHolder = new RecordingLeaseHolder(isHeld: false);
        var runProvider = new RecordingRunProvider([]);
        var cancellationService = new RecordingRunCancellationService();
        var reaper = new RunReaper(new FixedClock(), leaseHolder, runProvider, cancellationService, NullLogger<RunReaper>.Instance);

        // Act
        await reaper.ProcessStuckRunsAsync(CancellationToken.None);

        // Assert
        Assert.Equal(["run-reaper"], leaseHolder.IsHeldCalls);
        Assert.Equal(0, runProvider.GetStuckRunsCalls);
        Assert.Empty(cancellationService.Requests);
    }

    [Fact]
    public async Task Tick_requests_cancellation_for_each_stuck_run()
    {
        // Arrange
        var runs = new[]
        {
            CreateRun(51),
            CreateRun(52)
        };
        var leaseHolder = new RecordingLeaseHolder(isHeld: true);
        var runProvider = new RecordingRunProvider(runs);
        var cancellationService = new RecordingRunCancellationService();
        var reaper = new RunReaper(new FixedClock(), leaseHolder, runProvider, cancellationService, NullLogger<RunReaper>.Instance);

        // Act
        await reaper.ProcessStuckRunsAsync(CancellationToken.None);

        // Assert
        Assert.Equal(1, runProvider.GetStuckRunsCalls);
        Assert.Equal((new DateTime(2026, 5, 26, 12, 0, 0, DateTimeKind.Utc), 100), runProvider.LastRequest);
        Assert.Equal([(51L, "REAPER_TIMEOUT"), (52L, "REAPER_TIMEOUT")], cancellationService.Requests);
    }

    [Fact]
    public async Task Tick_does_nothing_when_no_stuck_runs()
    {
        // Arrange
        var leaseHolder = new RecordingLeaseHolder(isHeld: true);
        var runProvider = new RecordingRunProvider([]);
        var cancellationService = new RecordingRunCancellationService();
        var reaper = new RunReaper(new FixedClock(), leaseHolder, runProvider, cancellationService, NullLogger<RunReaper>.Instance);

        // Act
        await reaper.ProcessStuckRunsAsync(CancellationToken.None);

        // Assert
        Assert.Empty(cancellationService.Requests);
    }

    private static RunRow CreateRun(int id)
    {
        return new RunRow
        {
            Id = id,
            RefId = Guid.Parse("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa"),
            WorkflowDefinitionId = 3,
            WorkflowRefId = Guid.Parse("bbbbbbbb-bbbb-bbbb-bbbb-bbbbbbbbbbbb"),
            WorkflowVersion = 1,
            TriggerNodeId = Guid.Parse("cccccccc-cccc-cccc-cccc-cccccccccccc"),
            CorrelationKey = "corr",
            Status = "Running",
            StartedAt = new DateTime(2026, 5, 26, 11, 0, 0, DateTimeKind.Utc),
            CompletedAt = null,
            CancellationRequestedAt = null,
            CancellationReason = null,
            CreditBudget = 10,
            CreatedAt = new DateTime(2026, 5, 26, 11, 0, 0, DateTimeKind.Utc)
        };
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

    private sealed class RecordingRunProvider(IReadOnlyCollection<RunRow> runs) : IRunProvider
    {
        public int GetStuckRunsCalls { get; private set; }
        public (DateTime CutoffUtc, int BatchSize)? LastRequest { get; private set; }

        public Task<RunRow> CreateAsync(RunRow row, CancellationToken ct) => throw new NotSupportedException();
        public Task<int?> FindByRefIdAsync(Guid refId, CancellationToken ct) => throw new NotSupportedException();
        public Task<RunRow> GetByIdAsync(long runId, CancellationToken ct) => throw new NotSupportedException();
        public Task<RunRow> GetByRefIdAsync(Guid refId, CancellationToken ct) => throw new NotSupportedException();
        public Task<IReadOnlyCollection<RunRow>> GetActiveByWorkflowRefIdCorrelationKeyAsync(Guid workflowRefId, string correlationKey, CancellationToken ct) => throw new NotSupportedException();
        public Task<IReadOnlyCollection<RunRow>> GetActiveByCorrelationAsync(int workflowDefinitionId, string correlationKey, CancellationToken ct) => throw new NotSupportedException();
        public Task<RunRow> UpdateStatusAsync(Guid refId, string status, DateTime? completedAt, DateTime? cancellationRequestedAt, string? cancellationReason, CancellationToken ct) => throw new NotSupportedException();
        public Task<bool> TransitionStatusAsync(long runId, string fromStatus, string toStatus, CancellationToken ct) => throw new NotSupportedException();
        public Task<RunRow> SetTerminalAsync(long runId, string status, DateTime completedAt, CancellationToken ct) => throw new NotSupportedException();

        public Task<IReadOnlyCollection<RunRow>> GetStuckRunsAsync(DateTime cutoffUtc, int batchSize, CancellationToken ct)
        {
            GetStuckRunsCalls++;
            LastRequest = (cutoffUtc, batchSize);
            return Task.FromResult(runs);
        }
    }

    private sealed class RecordingRunCancellationService : IRunCancellationService
    {
        public List<(long RunId, string Reason)> Requests { get; } = [];

        public Task<bool> RequestCancellationAsync(long runId, string reason, CancellationToken ct)
        {
            Requests.Add((runId, reason));
            return Task.FromResult(true);
        }

        public Task<bool> IsCancellationRequestedAsync(long runId, CancellationToken ct)
        {
            return Task.FromResult(false);
        }
    }
}
