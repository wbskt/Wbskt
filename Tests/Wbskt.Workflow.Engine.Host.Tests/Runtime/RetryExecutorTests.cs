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
using Xunit;

namespace Wbskt.Workflow.Engine.Host.Tests.Runtime;

public sealed class RetryExecutorTests
{
    [Fact]
    public async Task Retries_on_retryable_fail_until_success()
    {
        var node = CreateNode(new RetryPolicy(RetryStrategy.Constant, TimeSpan.Zero, null, null, 3, 0, []));
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
        var node = CreateNode(new RetryPolicy(RetryStrategy.Constant, TimeSpan.Zero, null, null, 3, 0, []));
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
        var node = CreateNode(new RetryPolicy(RetryStrategy.Constant, TimeSpan.Zero, null, null, 5, 0, []));
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
        var node = CreateNode(new RetryPolicy(RetryStrategy.Exponential, TimeSpan.FromMilliseconds(15), null, null, 3, 0, []));
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

    private static SendCommandActionNode CreateNode(RetryPolicy? retry)
    {
        return new SendCommandActionNode(
            Guid.Parse("11111111-1111-1111-1111-111111111111"),
            "command",
            [new PortDefinition("default", PortDirection.Output, "Default")],
            new SendCommandConfig("device-1", "DoThing"),
            retry,
            null);
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
            new DateTime(2026, 5, 26, 12, 0, 0, DateTimeKind.Utc));
    }

    private sealed class RecordingExecutor(params NodeExecutionResult[] results) : INodeExecutor
    {
        private readonly Queue<NodeExecutionResult> _results = new(results);

        public int AttemptCount { get; private set; }

        public string Kind => NodeKind.ActionCommand;

        public Task<NodeExecutionResult> ExecuteAsync(NodeContext ctx, CancellationToken ct)
        {
            AttemptCount++;
            return Task.FromResult(_results.Dequeue());
        }
    }

    [Fact]
    public async Task RunWithRetryAsync_side_effecting_claims_pending_before_and_marks_succeeded_on_success()
    {
        var node = CreateNode(new RetryPolicy(RetryStrategy.Constant, TimeSpan.Zero, null, null, 1, 0, []));
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
        var node = CreateNode(new RetryPolicy(RetryStrategy.Constant, TimeSpan.Zero, null, null, 1, 0, []));
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
        var node = CreateNode(new RetryPolicy(RetryStrategy.Constant, TimeSpan.Zero, null, null, 1, 0, []));
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
        var node = CreateNode(new RetryPolicy(RetryStrategy.Constant, TimeSpan.Zero, null, null, 1, 0, []));
        
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
        idempotencyMock.Verify(p => p.UpsertPendingAsync(It.IsAny<string>(), It.IsAny<int>(), It.IsAny<Guid>(), It.IsAny<Guid>(), It.IsAny<int>(), It.IsAny<CancellationToken>()), Times.Once);
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
