using Microsoft.Extensions.Logging;
using Wbskt.Workflow.Abstraction.Entities;
using Wbskt.Workflow.Abstraction.Providers;
using Wbskt.Workflow.Abstraction.Runtime;
using Wbskt.Workflow.Engine.Host.HostedServices;

namespace Wbskt.Workflow.Engine.Host.Tests.HostedServices;

public sealed class RunRecoveryServiceTests
{
    [Fact]
    public async Task Start_dispatches_all_running_branches()
    {
        // Arrange
        var branches = new[]
        {
            CreateBranch(71, 171),
            CreateBranch(72, 172)
        };
        var branchProvider = new RecordingBranchProvider(branches);
        var runDispatcher = new RecordingRunDispatcher();
        var logger = new RecordingLogger<RunRecoveryService>();
        var service = new RunRecoveryService(branchProvider, runDispatcher, logger);

        // Act
        await service.StartAsync(CancellationToken.None);

        // Assert
        Assert.Equal([(171L, 71L, BranchExecutionReason.BookmarkResumed), (172L, 72L, BranchExecutionReason.BookmarkResumed)], runDispatcher.Requests);
    }

    [Fact]
    public async Task Start_logs_zero_recovery_when_none()
    {
        // Arrange
        var branchProvider = new RecordingBranchProvider([]);
        var runDispatcher = new RecordingRunDispatcher();
        var logger = new RecordingLogger<RunRecoveryService>();
        var service = new RunRecoveryService(branchProvider, runDispatcher, logger);

        // Act
        await service.StartAsync(CancellationToken.None);

        // Assert
        Assert.Contains(logger.Messages, message => message.Contains("Recovered 0 running branches.", StringComparison.Ordinal));
    }

    [Fact]
    public async Task Start_sets_ready_task_completed()
    {
        // Arrange
        var branchProvider = new RecordingBranchProvider([]);
        var runDispatcher = new RecordingRunDispatcher();
        var logger = new RecordingLogger<RunRecoveryService>();
        var service = new RunRecoveryService(branchProvider, runDispatcher, logger);

        Assert.False(service.Ready.IsCompleted);

        // Act
        await service.StartAsync(CancellationToken.None);

        // Assert
        Assert.True(service.Ready.IsCompleted);
    }

    [Fact]
    public async Task Start_cancels_cancelling_or_failing_runs()
    {
        // Arrange
        var branches = new[]
        {
            CreateBranch(71, 171),
            CreateBranch(72, 172)
        };
        var branchProvider = new RecordingBranchProvider(branches);
        var runDispatcher = new RecordingRunDispatcher();
        var logger = new RecordingLogger<RunRecoveryService>();
        
        var runProvider = new RecordingRunProvider();
        runProvider.AddRun(CreateRunRow(171, "Cancelling"));
        runProvider.AddRun(CreateRunRow(172, "Failing"));
        
        var cancellationService = new RecordingRunCancellationService();
        var service = new RunRecoveryService(branchProvider, runDispatcher, logger, runProvider, cancellationService);

        // Act
        await service.StartAsync(CancellationToken.None);

        // Assert
        Assert.Contains(171L, cancellationService.CancelledRuns);
        Assert.Contains(172L, cancellationService.CancelledRuns);
    }

    private static RunRow CreateRunRow(int id, string status)
    {
        return new RunRow
        {
            Id = id,
            RefId = Guid.NewGuid(),
            WorkflowDefinitionId = 1,
            WorkflowRefId = Guid.NewGuid(),
            WorkflowVersion = 1,
            TriggerNodeId = Guid.NewGuid(),
            CorrelationKey = "test-correlation",
            Status = status,
            StartedAt = DateTime.UtcNow,
            CompletedAt = null,
            CancellationRequestedAt = null,
            CancellationReason = null,
            CreditBudget = 100m,
            CreatedAt = DateTime.UtcNow
        };
    }

    private static BranchRow CreateBranch(int id, int runId)
    {
        return new BranchRow
        {
            Id = id,
            RefId = Guid.Parse($"00000000-0000-0000-0000-{id:D12}"),
            RunId = runId,
            ParentBranchId = null,
            ForkCohortId = null,
            NodeId = Guid.Parse("11111111-1111-1111-1111-111111111111"),
            Status = "Running",
            PendingTakePort = null,
            LocalJson = "{}",
            LastOutputJson = null,
            CompensationStackJson = null,
            CreatedAt = new DateTime(2026, 5, 26, 12, 0, 0, DateTimeKind.Utc),
            UpdatedAt = new DateTime(2026, 5, 26, 12, 0, 0, DateTimeKind.Utc),
            RowVersion = [1]
        };
    }

    private sealed class RecordingBranchProvider(IReadOnlyCollection<BranchRow> branches) : IBranchProvider
    {
        public Task<BranchRow> CreateAsync(BranchRow row, CancellationToken ct) => throw new NotSupportedException();
        public Task<BranchRow> UpsertAsync(BranchRow row, CancellationToken ct) => throw new NotSupportedException();
        public Task<BranchRow> GetByIdAsync(long branchId, CancellationToken ct) => throw new NotSupportedException();
        public Task<BranchRow> GetByRefIdAsync(Guid refId, CancellationToken ct) => throw new NotSupportedException();
        public Task<IReadOnlyCollection<BranchRow>> GetAllByRunIdAsync(int runId, CancellationToken ct) => throw new NotSupportedException();
        public Task<IReadOnlyCollection<BranchRow>> GetActiveByRunIdAsync(int runId, CancellationToken ct) => throw new NotSupportedException();
        public Task<IReadOnlyCollection<BranchRow>> GetAllActiveAsync(CancellationToken ct) => throw new NotSupportedException();
        public Task<BranchRow> UpdatePointerAsync(long branchId, Guid currentNodeId, string status, string localJson, string? lastOutputJson, CancellationToken ct) => throw new NotSupportedException();
        public Task<BranchRow> SetCompletedAsync(long branchId, CancellationToken ct) => throw new NotSupportedException();
        public Task<BranchRow> SetFailedAsync(long branchId, string? lastOutputJson, CancellationToken ct) => throw new NotSupportedException();

        public Task<IReadOnlyCollection<BranchRow>> GetRunningBranchesAsync(CancellationToken ct)
        {
            return Task.FromResult(branches);
        }
    }

    private sealed class RecordingRunDispatcher : IRunDispatcher
    {
        public List<(long RunId, long BranchId, BranchExecutionReason Reason)> Requests { get; } = [];

        public ValueTask DispatchAsync(BranchExecutionRequest request, CancellationToken ct)
        {
            Requests.Add((request.RunId, request.BranchId, request.Reason));
            return ValueTask.CompletedTask;
        }
    }

    private sealed class RecordingLogger<T> : ILogger<T>
    {
        public List<string> Messages { get; } = [];

        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;
        public bool IsEnabled(LogLevel logLevel) => true;

        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
        {
            Messages.Add(formatter(state, exception));
        }
    }

    private sealed class RecordingRunProvider : IRunProvider
    {
        private readonly Dictionary<long, RunRow> _runs = new();

        public void AddRun(RunRow run)
        {
            _runs[run.Id] = run;
        }

        public Task<RunRow> CreateAsync(RunRow row, CancellationToken ct) => throw new NotSupportedException();
        public Task<int?> FindByRefIdAsync(Guid refId, CancellationToken ct) => throw new NotSupportedException();
        public Task<RunRow> GetByIdAsync(long runId, CancellationToken ct) => Task.FromResult(_runs[runId]);
        public Task<RunRow> GetByRefIdAsync(Guid refId, CancellationToken ct) => throw new NotSupportedException();
        public Task<IReadOnlyCollection<RunRow>> ListByWorkflowAsync(Guid workflowRefId, string? statusFilter, int top, long? cursorId, CancellationToken ct) => throw new NotSupportedException();
        public Task<IReadOnlyCollection<RunRow>> GetActiveByWorkflowRefIdCorrelationKeyAsync(Guid workflowRefId, string correlationKey, CancellationToken ct) => throw new NotSupportedException();
        public Task<RunRow> UpdateStatusAsync(Guid refId, string status, DateTime? completedAt, DateTime? cancellationRequestedAt, string? cancellationReason, CancellationToken ct) => throw new NotSupportedException();
        public Task<IReadOnlyCollection<RunRow>> GetStuckRunsAsync(DateTime cutoffUtc, int batchSize, CancellationToken ct) => throw new NotSupportedException();
        public Task<long> CountByStatusAsync(string status, CancellationToken ct) => throw new NotSupportedException();
        public Task<bool> TransitionStatusAsync(long runId, string fromStatus, string toStatus, DateTime? cancellationRequestedAt, string? cancellationReason, CancellationToken ct) => throw new NotSupportedException();
        public Task<(bool Transitioned, RunRow Run)> SetTerminalAsync(long runId, string status, DateTime completedAt, CancellationToken ct) => throw new NotSupportedException();
    }

    private sealed class RecordingRunCancellationService : IRunCancellationService
    {
        public List<long> CancelledRuns { get; } = new();

        public Task<bool> RequestCancellationAsync(long runId, string reason, CancellationToken ct) => throw new NotSupportedException();
        public Task<bool> IsCancellationRequestedAsync(long runId, CancellationToken ct) => throw new NotSupportedException();
        public void CancelCts(long runId)
        {
            CancelledRuns.Add(runId);
        }
    }
}
