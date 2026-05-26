using System.Diagnostics;
using System.Text.Json;
using Wbskt.Workflow.Abstraction.Enums;
using Wbskt.Workflow.Abstraction.Models;
using Wbskt.Workflow.Abstraction.Models.Nodes;
using Wbskt.Workflow.Abstraction.Models.Nodes.Actions;
using Wbskt.Workflow.Abstraction.Runtime;
using Wbskt.Workflow.Abstraction.Providers;
using Wbskt.Workflow.Runtime;
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

    private sealed class TestNodeExecutionServices : INodeExecutionServices
    {
        public IProviderComposite Providers => new StubProviderComposite();

        public int Tick => 1;

        public IReadOnlyDictionary<string, JsonElement>? ParentResults => null;
    }

    private sealed class StubProviderComposite : IProviderComposite
    {
        public IWorkflowDefinitionProvider WorkflowDefinition => throw new NotSupportedException();
        public ITriggerRegistrationProvider TriggerRegistration => throw new NotSupportedException();
        public IBookmarkProvider Bookmark => throw new NotSupportedException();
        public ISharedVariableProvider SharedVariable => throw new NotSupportedException();
        public IIdempotencyKeyProvider IdempotencyKey => throw new NotSupportedException();
        public IPendingTriggerEventProvider PendingTriggerEvent => throw new NotSupportedException();
        public IScheduledFireProvider ScheduledFire => throw new NotSupportedException();
    }

    private sealed class FixedClock : IClock
    {
        public DateTime UtcNow => new(2026, 5, 26, 12, 30, 0, DateTimeKind.Utc);
    }
}
