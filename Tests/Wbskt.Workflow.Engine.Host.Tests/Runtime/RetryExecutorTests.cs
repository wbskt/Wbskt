using System.Diagnostics;
using System.Text.Json;
using Moq;
using Wbskt.Workflow.Abstraction.Entities;
using Wbskt.Workflow.Abstraction.Enums;
using Wbskt.Workflow.Abstraction.Models;
using Wbskt.Workflow.Abstraction.Models.Nodes;
using Wbskt.Workflow.Abstraction.Models.Nodes.Actions;
using Wbskt.Workflow.Abstraction.Runtime;
using Wbskt.Workflow.Abstraction.Providers;
using Wbskt.Workflow.Runtime;
using Wbskt.Workflow.Abstraction.Models.Bookmarks;

namespace Wbskt.Workflow.Engine.Host.Tests.Runtime;

public sealed class RetryExecutorTests
{
    [Fact]
    public async Task Retries_on_retryable_fail_until_success()
    {
        var node = CreateNode(new RetryPolicy { Strategy = RetryStrategy.Constant, InitialDelay = TimeSpan.Zero, Factor = null, MaxDelay = null, MaxAttempts = 3, JitterPct = 0, RetryOn = [] });
        var executor = new RecordingExecutor(
            new NodeExecutionResult.Fail("E_RETRY", "boom", true, null),
            new NodeExecutionResult.Fail("E_RETRY", "boom", true, null),
            new NodeExecutionResult.Terminal(BranchTerminalReason.Completed));

        NodeExecutionResult result = await RetryExecutor.RunWithRetryAsync(
            node,
            CreateContext(),
            executor,
            new TestNodeExecutionServices(),
            new FixedClock(),
            CancellationToken.None);

        var terminal = Assert.IsType<NodeExecutionResult.Terminal>(result);
        Assert.Equal(3, executor.AttemptCount);
        Assert.Equal(BranchTerminalReason.Completed, terminal.Reason);
    }

    [Fact]
    public async Task Stops_retrying_after_max_attempts()
    {
        var node = CreateNode(new RetryPolicy { Strategy = RetryStrategy.Constant, InitialDelay = TimeSpan.Zero, Factor = null, MaxDelay = null, MaxAttempts = 3, JitterPct = 0, RetryOn = [] });
        var expected = new NodeExecutionResult.Fail("E_RETRY", "boom", true, null);
        var executor = new RecordingExecutor(expected, expected, expected, new NodeExecutionResult.Terminal(BranchTerminalReason.Completed));

        NodeExecutionResult result = await RetryExecutor.RunWithRetryAsync(
            node,
            CreateContext(),
            executor,
            new TestNodeExecutionServices(),
            new FixedClock(),
            CancellationToken.None);

        var fail = Assert.IsType<NodeExecutionResult.Fail>(result);
        Assert.Equal(3, executor.AttemptCount);
        Assert.Equal(expected.ErrorCode, fail.ErrorCode);
    }

    [Fact]
    public async Task Does_not_retry_on_non_retryable_fail()
    {
        var node = CreateNode(new RetryPolicy { Strategy = RetryStrategy.Constant, InitialDelay = TimeSpan.Zero, Factor = null, MaxDelay = null, MaxAttempts = 5, JitterPct = 0, RetryOn = [] });
        var expected = new NodeExecutionResult.Fail("E_FATAL", "nope", false, null);
        var executor = new RecordingExecutor(expected, new NodeExecutionResult.Terminal(BranchTerminalReason.Completed));

        NodeExecutionResult result = await RetryExecutor.RunWithRetryAsync(
            node,
            CreateContext(),
            executor,
            new TestNodeExecutionServices(),
            new FixedClock(),
            CancellationToken.None);

        var fail = Assert.IsType<NodeExecutionResult.Fail>(result);
        Assert.Equal(1, executor.AttemptCount);
        Assert.Equal(expected.ErrorCode, fail.ErrorCode);
    }

    [Fact]
    public async Task Exponential_backoff_delays_grow()
    {
        var node = CreateNode(new RetryPolicy { Strategy = RetryStrategy.Exponential, InitialDelay = TimeSpan.FromMilliseconds(15), Factor = null, MaxDelay = null, MaxAttempts = 3, JitterPct = 0, RetryOn = [] });
        var executor = new RecordingExecutor(
            new NodeExecutionResult.Fail("E_RETRY", "boom", true, null),
            new NodeExecutionResult.Fail("E_RETRY", "boom", true, null),
            new NodeExecutionResult.Terminal(BranchTerminalReason.Completed));
        Stopwatch stopwatch = Stopwatch.StartNew();

        NodeExecutionResult result = await RetryExecutor.RunWithRetryAsync(
            node,
            CreateContext(),
            executor,
            new TestNodeExecutionServices(),
            new FixedClock(),
            CancellationToken.None);

        Assert.IsType<NodeExecutionResult.Terminal>(result);
        Assert.Equal(3, executor.AttemptCount);
        Assert.True(stopwatch.Elapsed >= TimeSpan.FromMilliseconds(40), $"Expected at least 40ms, got {stopwatch.Elapsed.TotalMilliseconds}ms.");
    }

    private static SendClientMessageNode CreateNode(RetryPolicy? retry)
    {
        return new SendClientMessageNode { NodeId = Guid.Parse("11111111-1111-1111-1111-111111111111"), Name = "command", Ports = [new PortDefinition { PortId = "default", Direction = PortDirection.Output, Label = "Default" }], Config = new SendClientMessageConfig { ClientRef = "client-1", Type = "DoThing" }, Retry = retry };
    }

    private static BranchContext CreateContext()
    {
        return new BranchContext(
            42,
            1001,
            9,
            Guid.Parse("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa"),
            1,
            Guid.Parse("11111111-1111-1111-1111-111111111111").ToString(),
            1,
            new Dictionary<string, JsonElement>(),
            new Dictionary<string, JsonElement>(),
            "corr-42",
            new DateTime(2026, 5, 26, 12, 0, 0, DateTimeKind.Utc),
            9);
    }

    [Fact]
    public async Task Cancellation_propagates_instead_of_becoming_an_executor_crash()
    {
        // Only BranchLoop can tell a run cancel from a host shutdown - it holds both tokens - and it
        // turns the first into a clean Cancelled branch and leaves the second Active for recovery.
        // Swallowing this into EXECUTOR_CRASH would turn every interrupted node into a failed one.
        var node = CreateNode(new RetryPolicy { Strategy = RetryStrategy.Constant, InitialDelay = TimeSpan.Zero, Factor = null, MaxDelay = null, MaxAttempts = 3, JitterPct = 0, RetryOn = [] });
        using var cts = new CancellationTokenSource();
        var executor = new ThrowingExecutor(() =>
        {
            cts.Cancel();
            throw new OperationCanceledException(cts.Token);
        });

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => RetryExecutor.RunWithRetryAsync(
            node,
            CreateContext(),
            executor,
            new TestNodeExecutionServices(),
            new FixedClock(),
            cts.Token));

        // And it is not retried: a cancelled run does not want two more attempts.
        Assert.Equal(1, executor.AttemptCount);
    }

    [Fact]
    public async Task An_uncancelled_OperationCanceledException_is_still_a_crash()
    {
        // An executor that throws OCE for its own reasons, with nothing actually cancelled, is a bug in
        // that executor - not a cancellation - and must not masquerade as one.
        var node = CreateNode(new RetryPolicy { Strategy = RetryStrategy.Constant, InitialDelay = TimeSpan.Zero, Factor = null, MaxDelay = null, MaxAttempts = 1, JitterPct = 0, RetryOn = [] });
        var executor = new ThrowingExecutor(() => throw new OperationCanceledException("nothing was cancelled"));

        NodeExecutionResult result = await RetryExecutor.RunWithRetryAsync(
            node,
            CreateContext(),
            executor,
            new TestNodeExecutionServices(),
            new FixedClock(),
            CancellationToken.None);

        Assert.Equal("EXECUTOR_CRASH", Assert.IsType<NodeExecutionResult.Fail>(result).ErrorCode);
    }

    private sealed class ThrowingExecutor(Func<NodeExecutionResult> behaviour) : INodeExecutor
    {
        public int AttemptCount { get; private set; }

        public string Kind => NodeKind.ActionClientMessage;

        public Task<NodeExecutionResult> ExecuteAsync(NodeContext ctx, CancellationToken ct)
        {
            AttemptCount++;
            return Task.FromResult(behaviour());
        }
    }

    private sealed class RecordingExecutor(params NodeExecutionResult[] results) : INodeExecutor
    {
        private readonly Queue<NodeExecutionResult> _results = new(results);

        public int AttemptCount { get; private set; }

        public string Kind => NodeKind.ActionClientMessage;

        public Task<NodeExecutionResult> ExecuteAsync(NodeContext ctx, CancellationToken ct)
        {
            AttemptCount++;
            return Task.FromResult(_results.Dequeue());
        }
    }

    [Fact]
    public async Task RunWithRetryAsync_side_effecting_claims_pending_before_and_marks_succeeded_on_success()
    {
        var node = CreateNode(new RetryPolicy { Strategy = RetryStrategy.Constant, InitialDelay = TimeSpan.Zero, Factor = null, MaxDelay = null, MaxAttempts = 1, JitterPct = 0, RetryOn = [] });
        var executor = new RecordingExecutor(new NodeExecutionResult.Terminal(BranchTerminalReason.Completed));

        var idempotencyMock = new Mock<IIdempotencyKeyProvider>();
        idempotencyMock.Setup(p => p.GetByKeyAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new KeyNotFoundException());
        idempotencyMock.Setup(p => p.UpsertPendingAsync(It.IsAny<string>(), It.IsAny<int>(), It.IsAny<Guid>(), It.IsAny<Guid>(), It.IsAny<int>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((string key, int runId, Guid claimToken, Guid nodeId, int attempt, CancellationToken _) => new IdempotencyKeyRow
            {
                Id = 1, KeyValue = key, RunId = runId, BranchRefId = claimToken, NodeId = nodeId, Attempt = attempt, Status = "Pending", CreatedAt = DateTime.UtcNow, ResultJson = null, ErrorJson = null, CompletedAt = null
            });
        idempotencyMock.Setup(p => p.MarkSucceededAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((string key, string res, CancellationToken _) => new IdempotencyKeyRow
            {
                Id = 1, KeyValue = key, RunId = 42, BranchRefId = Guid.NewGuid(), NodeId = Guid.Empty, Attempt = 1, Status = "Succeeded", ResultJson = res, CreatedAt = DateTime.UtcNow, ErrorJson = null, CompletedAt = null
            });

        var services = new TestNodeExecutionServices(idempotencyMock.Object);

        NodeExecutionResult result = await RetryExecutor.RunWithRetryAsync(
            node,
            CreateContext(),
            executor,
            services,
            new FixedClock(),
            CancellationToken.None);

        Assert.IsType<NodeExecutionResult.Terminal>(result);
        idempotencyMock.Verify(p => p.UpsertPendingAsync(It.IsAny<string>(), It.IsAny<int>(), It.IsAny<Guid>(), It.IsAny<Guid>(), It.IsAny<int>(), It.IsAny<CancellationToken>()), Times.Once);
        idempotencyMock.Verify(p => p.MarkSucceededAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task RunWithRetryAsync_does_not_cache_WaitForBookmark_result()
    {
        var node = CreateNode(new RetryPolicy { Strategy = RetryStrategy.Constant, InitialDelay = TimeSpan.Zero, Factor = null, MaxDelay = null, MaxAttempts = 1, JitterPct = 0, RetryOn = [] });
        var executor = new RecordingExecutor(new NodeExecutionResult.WaitForBookmark(new TimerWakeCondition(DateTime.UtcNow), new Dictionary<string, JsonElement>()));

        var idempotencyMock = new Mock<IIdempotencyKeyProvider>();
        idempotencyMock.Setup(p => p.GetByKeyAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new KeyNotFoundException());
        idempotencyMock.Setup(p => p.UpsertPendingAsync(It.IsAny<string>(), It.IsAny<int>(), It.IsAny<Guid>(), It.IsAny<Guid>(), It.IsAny<int>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((string key, int runId, Guid claimToken, Guid nodeId, int attempt, CancellationToken _) => new IdempotencyKeyRow
            {
                Id = 1, KeyValue = key, RunId = runId, BranchRefId = claimToken, NodeId = nodeId, Attempt = attempt, Status = "Pending", CreatedAt = DateTime.UtcNow, ResultJson = null, ErrorJson = null, CompletedAt = null
            });

        var services = new TestNodeExecutionServices(idempotencyMock.Object);

        NodeExecutionResult result = await RetryExecutor.RunWithRetryAsync(
            node,
            CreateContext(),
            executor,
            services,
            new FixedClock(),
            CancellationToken.None);

        Assert.IsType<NodeExecutionResult.WaitForBookmark>(result);
        idempotencyMock.Verify(p => p.UpsertPendingAsync(It.IsAny<string>(), It.IsAny<int>(), It.IsAny<Guid>(), It.IsAny<Guid>(), It.IsAny<int>(), It.IsAny<CancellationToken>()), Times.Once);
        idempotencyMock.Verify(p => p.MarkSucceededAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task RunWithRetryAsync_side_effecting_bypasses_executor_if_already_succeeded()
    {
        var node = CreateNode(new RetryPolicy { Strategy = RetryStrategy.Constant, InitialDelay = TimeSpan.Zero, Factor = null, MaxDelay = null, MaxAttempts = 1, JitterPct = 0, RetryOn = [] });
        var executor = new RecordingExecutor(new NodeExecutionResult.Terminal(BranchTerminalReason.Completed));

        var options = new JsonSerializerOptions(JsonSerializerDefaults.Web);
        var expectedResult = new NodeExecutionResult.Terminal(BranchTerminalReason.Completed);
        var cachedResultJson = JsonSerializer.Serialize<NodeExecutionResult>(expectedResult, options);

        var idempotencyMock = new Mock<IIdempotencyKeyProvider>();
        idempotencyMock.Setup(p => p.GetByKeyAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new IdempotencyKeyRow
            {
                Id = 1, KeyValue = "key", RunId = 42, BranchRefId = Guid.NewGuid(), NodeId = node.NodeId, Attempt = 1, Status = "Succeeded", ResultJson = cachedResultJson, CreatedAt = DateTime.UtcNow, ErrorJson = null, CompletedAt = null
            });

        var services = new TestNodeExecutionServices(idempotencyMock.Object);

        NodeExecutionResult result = await RetryExecutor.RunWithRetryAsync(
            node,
            CreateContext(),
            executor,
            services,
            new FixedClock(),
            CancellationToken.None);

        var term = Assert.IsType<NodeExecutionResult.Terminal>(result);
        Assert.Equal(BranchTerminalReason.Completed, term.Reason);
        Assert.Equal(0, executor.AttemptCount);
    }

    [Fact]
    public async Task RunWithRetryAsync_side_effect_free_runs_first_then_writes_cache()
    {
        var node = CreateNode(new RetryPolicy { Strategy = RetryStrategy.Constant, InitialDelay = TimeSpan.Zero, Factor = null, MaxDelay = null, MaxAttempts = 1, JitterPct = 0, RetryOn = [] });
        
        var executorMock = new Mock<INodeExecutor>();
        executorMock.SetupGet(e => e.Kind).Returns("test");
        executorMock.SetupGet(e => e.IsSideEffectFree).Returns(true);
        executorMock.Setup(e => e.ExecuteAsync(It.IsAny<NodeContext>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new NodeExecutionResult.Terminal(BranchTerminalReason.Completed));

        var idempotencyMock = new Mock<IIdempotencyKeyProvider>();
        idempotencyMock.Setup(p => p.GetByKeyAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new KeyNotFoundException());
        idempotencyMock.Setup(p => p.UpsertPendingAsync(It.IsAny<string>(), It.IsAny<int>(), It.IsAny<Guid>(), It.IsAny<Guid>(), It.IsAny<int>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((string key, int runId, Guid claimToken, Guid nodeId, int attempt, CancellationToken _) => new IdempotencyKeyRow
            {
                Id = 1, KeyValue = key, RunId = runId, BranchRefId = claimToken, NodeId = nodeId, Attempt = attempt, Status = "Pending", CreatedAt = DateTime.UtcNow, ResultJson = null, ErrorJson = null, CompletedAt = null
            });
        idempotencyMock.Setup(p => p.MarkSucceededAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((string key, string res, CancellationToken _) => new IdempotencyKeyRow
            {
                Id = 1, KeyValue = key, RunId = 42, BranchRefId = Guid.NewGuid(), NodeId = Guid.Empty, Attempt = 1, Status = "Succeeded", ResultJson = res, CreatedAt = DateTime.UtcNow, ErrorJson = null, CompletedAt = null
            });

        var services = new TestNodeExecutionServices(idempotencyMock.Object);

        NodeExecutionResult result = await RetryExecutor.RunWithRetryAsync(
            node,
            CreateContext(),
            executorMock.Object,
            services,
            new FixedClock(),
            CancellationToken.None);

        Assert.IsType<NodeExecutionResult.Terminal>(result);
        
        executorMock.Verify(e => e.ExecuteAsync(It.IsAny<NodeContext>(), It.IsAny<CancellationToken>()), Times.Once);
        idempotencyMock.Verify(p => p.UpsertPendingAsync(It.IsAny<string>(), It.IsAny<int>(), It.IsAny<Guid>(), It.IsAny<Guid>(), It.IsAny<int>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task RunWithRetryAsync_returns_OutOfCredits_fail_when_charge_fails()
    {
        var node = CreateNode(new RetryPolicy { Strategy = RetryStrategy.Constant, InitialDelay = TimeSpan.Zero, Factor = null, MaxDelay = null, MaxAttempts = 1, JitterPct = 0, RetryOn = [] });
        var executor = new RecordingExecutor(new NodeExecutionResult.Terminal(BranchTerminalReason.Completed));

        var countersMock = new Mock<IRunCountersProvider>();
        countersMock.Setup(p => p.TryChargeAsync(It.IsAny<int>(), It.IsAny<decimal>(), It.IsAny<CancellationToken>())).ReturnsAsync(false);

        NodeExecutionResult result = await RetryExecutor.RunWithRetryAsync(
            node,
            CreateContext(),
            executor,
            new TestNodeExecutionServices(),
            new FixedClock(),
            CancellationToken.None,
            countersMock.Object);

        var fail = Assert.IsType<NodeExecutionResult.Fail>(result);
        Assert.Equal("OUT_OF_CREDITS", fail.ErrorCode);
        Assert.Equal(0, executor.AttemptCount);
    }

    [Fact]
    public async Task RunWithRetryAsync_charges_via_single_atomic_call_and_executes_when_charge_succeeds()
    {
        var node = CreateNode(new RetryPolicy { Strategy = RetryStrategy.Constant, InitialDelay = TimeSpan.Zero, Factor = null, MaxDelay = null, MaxAttempts = 1, JitterPct = 0, RetryOn = [] });
        var executor = new RecordingExecutor(new NodeExecutionResult.Terminal(BranchTerminalReason.Completed));

        var countersMock = new Mock<IRunCountersProvider>();
        countersMock.Setup(p => p.TryChargeAsync(It.IsAny<int>(), It.IsAny<decimal>(), It.IsAny<CancellationToken>())).ReturnsAsync(true);

        NodeExecutionResult result = await RetryExecutor.RunWithRetryAsync(
            node,
            CreateContext(),
            executor,
            new TestNodeExecutionServices(),
            new FixedClock(),
            CancellationToken.None,
            countersMock.Object);

        Assert.IsType<NodeExecutionResult.Terminal>(result);
        Assert.Equal(1, executor.AttemptCount);
        countersMock.Verify(p => p.TryChargeAsync(42, It.IsAny<decimal>(), It.IsAny<CancellationToken>()), Times.Once);
        countersMock.Verify(p => p.GetByRunIdAsync(It.IsAny<int>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    private sealed class TestNodeExecutionServices(IIdempotencyKeyProvider? idempotencyKey = null) : INodeExecutionServices
    {
        public IProviderComposite Providers => new StubProviderComposite(idempotencyKey);

        public int Tick => 1;

        public IReadOnlyDictionary<string, JsonElement>? ParentResults => null;
    }

    private sealed class StubProviderComposite(IIdempotencyKeyProvider? idempotencyKey = null) : IProviderComposite
    {
        public IWorkflowDefinitionProvider WorkflowDefinition => throw new NotSupportedException();
        public ITriggerRegistrationProvider TriggerRegistration => throw new NotSupportedException();
        public IBookmarkProvider Bookmark => throw new NotSupportedException();
        public ISharedVariableProvider SharedVariable => throw new NotSupportedException();
        public IIdempotencyKeyProvider IdempotencyKey => idempotencyKey ?? throw new NotSupportedException();
        public IPendingTriggerEventProvider PendingTriggerEvent => throw new NotSupportedException();
        public IScheduledFireProvider ScheduledFire => throw new NotSupportedException();
    }

    private sealed class FixedClock : IClock
    {
        public DateTime UtcNow => new(2026, 5, 26, 12, 30, 0, DateTimeKind.Utc);
    }
}
