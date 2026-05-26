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
}
