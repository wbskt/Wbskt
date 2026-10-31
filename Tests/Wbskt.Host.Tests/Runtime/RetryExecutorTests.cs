using System.Diagnostics;
using System.Text.Json;
using Moq;
using Wbskt.Workflow.Abstraction.Entities;
using Wbskt.Workflow.Abstraction.Enums;
using Wbskt.Workflow.Abstraction.Exceptions;
using Wbskt.Workflow.Abstraction.Models;
using Wbskt.Workflow.Abstraction.Models.Nodes;
using Wbskt.Workflow.Abstraction.Models.Nodes.Actions;
using Wbskt.Workflow.Abstraction.Runtime;
using Wbskt.Workflow.Abstraction.Providers;
using Wbskt.Workflow.Runtime;
using Wbskt.Workflow.Abstraction.Models.Bookmarks;
using Wbskt.Workflow.Abstraction.Models.Nodes.Controls;
using Wbskt.Workflow.Abstraction.Models.Nodes.Triggers;
using Wbskt.Workflow.Abstraction.Models.Triggers;
using Wbskt.Workflow.NodeExecutors.Controls;
using Wbskt.Workflow.NodeExecutors.Triggers;

namespace Wbskt.Host.Tests.Runtime;

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
            CancellationToken.None,
            new UnlimitedRunCountersProvider(),
            new DefaultCreditCostCalculator());

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
            CancellationToken.None,
            new UnlimitedRunCountersProvider(),
            new DefaultCreditCostCalculator());

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
            CancellationToken.None,
            new UnlimitedRunCountersProvider(),
            new DefaultCreditCostCalculator());

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
            CancellationToken.None,
            new UnlimitedRunCountersProvider(),
            new DefaultCreditCostCalculator());

        Assert.IsType<NodeExecutionResult.Terminal>(result);
        Assert.Equal(3, executor.AttemptCount);
        Assert.True(stopwatch.Elapsed >= TimeSpan.FromMilliseconds(40), $"Expected at least 40ms, got {stopwatch.Elapsed.TotalMilliseconds}ms.");
    }

    [Fact]
    public async Task Exponential_backoff_uses_the_policy_factor_on_the_injected_clock()
    {
        // Delays in seconds, finishing at once: only possible if the backoff waits on the injected
        // clock rather than the wall clock.
        var node = CreateNode(new RetryPolicy { Strategy = RetryStrategy.Exponential, InitialDelay = TimeSpan.FromSeconds(10), Factor = 3.0, MaxDelay = null, MaxAttempts = 4, JitterPct = 0, RetryOn = [] });
        var executor = new RecordingExecutor(
            new NodeExecutionResult.Fail("E_RETRY", "boom", true, null),
            new NodeExecutionResult.Fail("E_RETRY", "boom", true, null),
            new NodeExecutionResult.Fail("E_RETRY", "boom", true, null),
            new NodeExecutionResult.Terminal(BranchTerminalReason.Completed));
        var clock = new InstantTimeProvider();

        NodeExecutionResult result = await RunAsync(node, executor, clock);

        Assert.IsType<NodeExecutionResult.Terminal>(result);
        Assert.Equal([TimeSpan.FromSeconds(10), TimeSpan.FromSeconds(30), TimeSpan.FromSeconds(90)], clock.Delays);
    }

    [Fact]
    public async Task Exponential_backoff_without_a_factor_doubles()
    {
        var node = CreateNode(new RetryPolicy { Strategy = RetryStrategy.Exponential, InitialDelay = TimeSpan.FromSeconds(1), Factor = null, MaxDelay = null, MaxAttempts = 4, JitterPct = 0, RetryOn = [] });
        var executor = new RecordingExecutor(
            new NodeExecutionResult.Fail("E_RETRY", "boom", true, null),
            new NodeExecutionResult.Fail("E_RETRY", "boom", true, null),
            new NodeExecutionResult.Fail("E_RETRY", "boom", true, null),
            new NodeExecutionResult.Terminal(BranchTerminalReason.Completed));
        var clock = new InstantTimeProvider();

        await RunAsync(node, executor, clock);

        Assert.Equal([TimeSpan.FromSeconds(1), TimeSpan.FromSeconds(2), TimeSpan.FromSeconds(4)], clock.Delays);
    }

    [Fact]
    public async Task MaxDelay_caps_the_backoff()
    {
        var node = CreateNode(new RetryPolicy { Strategy = RetryStrategy.Exponential, InitialDelay = TimeSpan.FromSeconds(10), Factor = 3.0, MaxDelay = TimeSpan.FromSeconds(45), MaxAttempts = 4, JitterPct = 0, RetryOn = [] });
        var executor = new RecordingExecutor(
            new NodeExecutionResult.Fail("E_RETRY", "boom", true, null),
            new NodeExecutionResult.Fail("E_RETRY", "boom", true, null),
            new NodeExecutionResult.Fail("E_RETRY", "boom", true, null),
            new NodeExecutionResult.Terminal(BranchTerminalReason.Completed));
        var clock = new InstantTimeProvider();

        await RunAsync(node, executor, clock);

        Assert.Equal([TimeSpan.FromSeconds(10), TimeSpan.FromSeconds(30), TimeSpan.FromSeconds(45)], clock.Delays);
    }

    [Fact]
    public async Task MaxDelay_is_a_ceiling_jitter_cannot_overshoot()
    {
        const int attempts = 25;
        var node = CreateNode(new RetryPolicy { Strategy = RetryStrategy.Constant, InitialDelay = TimeSpan.FromSeconds(10), Factor = null, MaxDelay = TimeSpan.FromSeconds(10), MaxAttempts = attempts, JitterPct = 50, RetryOn = [] });
        var executor = new RecordingExecutor(Enumerable.Repeat<NodeExecutionResult>(new NodeExecutionResult.Fail("E_RETRY", "boom", true, null), attempts).ToArray());
        var clock = new InstantTimeProvider();

        await RunAsync(node, executor, clock);

        Assert.Equal(attempts - 1, clock.Delays.Count);
        Assert.All(clock.Delays, delay => Assert.InRange(delay, TimeSpan.FromSeconds(5), TimeSpan.FromSeconds(10)));
    }

    [Fact]
    public async Task A_retryable_fail_the_policy_did_not_opt_into_is_returned_non_retryable_without_a_retry()
    {
        var node = CreateNode(new RetryPolicy { Strategy = RetryStrategy.Constant, InitialDelay = TimeSpan.FromSeconds(1), Factor = null, MaxDelay = null, MaxAttempts = 3, JitterPct = 0, RetryOn = ["E_TIMEOUT"] });
        var executor = new RecordingExecutor(
            new NodeExecutionResult.Fail("E_VALIDATION", "bad input", true, null),
            new NodeExecutionResult.Terminal(BranchTerminalReason.Completed));
        var clock = new InstantTimeProvider();
        var retries = new List<int>();

        NodeExecutionResult result = await RunAsync(node, executor, clock, (attempt, _, _, _, _) => { retries.Add(attempt); return Task.CompletedTask; });

        var fail = Assert.IsType<NodeExecutionResult.Fail>(result);
        Assert.Equal("E_VALIDATION", fail.ErrorCode);
        Assert.False(fail.Retryable);
        Assert.Equal(1, executor.AttemptCount);
        Assert.Empty(retries);
        Assert.Empty(clock.Delays);
    }

    [Fact]
    public async Task A_retryable_fail_the_policy_opted_into_is_retried_and_reported()
    {
        // The Fail-result path used to retry silently, so these retries were missing from history.
        var node = CreateNode(new RetryPolicy { Strategy = RetryStrategy.Linear, InitialDelay = TimeSpan.FromSeconds(1), Factor = null, MaxDelay = null, MaxAttempts = 3, JitterPct = 0, RetryOn = ["e_timeout"] });
        var executor = new RecordingExecutor(
            new NodeExecutionResult.Fail("E_TIMEOUT", "slow", true, null),
            new NodeExecutionResult.Fail("E_TIMEOUT", "slower", true, null),
            new NodeExecutionResult.Terminal(BranchTerminalReason.Completed));
        var clock = new InstantTimeProvider();
        var retries = new List<(int Attempt, int MaxAttempts, TimeSpan Delay, string Reason)>();

        NodeExecutionResult result = await RunAsync(node, executor, clock, (attempt, maxAttempts, delay, reason, _) =>
        {
            retries.Add((attempt, maxAttempts, delay, reason));
            return Task.CompletedTask;
        });

        Assert.IsType<NodeExecutionResult.Terminal>(result);
        Assert.Equal(3, executor.AttemptCount);
        Assert.Equal([(1, 3, TimeSpan.FromSeconds(1), "slow"), (2, 3, TimeSpan.FromSeconds(2), "slower")], retries);
        Assert.Equal([TimeSpan.FromSeconds(1), TimeSpan.FromSeconds(2)], clock.Delays);
    }

    [Fact]
    public async Task A_transient_exception_is_retried_on_the_same_terms()
    {
        var node = CreateNode(new RetryPolicy { Strategy = RetryStrategy.Exponential, InitialDelay = TimeSpan.FromSeconds(10), Factor = 3.0, MaxDelay = null, MaxAttempts = 3, JitterPct = 0, RetryOn = [] });
        int calls = 0;
        var executor = new ThrowingExecutor(() => ++calls < 3
            ? throw new TransientNodeException("flaky")
            : new NodeExecutionResult.Terminal(BranchTerminalReason.Completed));
        var clock = new InstantTimeProvider();
        var retries = new List<int>();

        NodeExecutionResult result = await RunAsync(node, executor, clock, (attempt, _, _, _, _) => { retries.Add(attempt); return Task.CompletedTask; });

        Assert.IsType<NodeExecutionResult.Terminal>(result);
        Assert.Equal([1, 2], retries);
        Assert.Equal([TimeSpan.FromSeconds(10), TimeSpan.FromSeconds(30)], clock.Delays);
    }

    [Fact]
    public async Task A_backoff_at_or_over_the_park_threshold_parks_on_a_timer_with_the_next_attempt()
    {
        // Slept in-process, a long backoff held a pump slot for its whole length and lost the attempt
        // count if the host went down during it.
        var node = CreateNode(new RetryPolicy { Strategy = RetryStrategy.Constant, InitialDelay = TimeSpan.FromSeconds(30), Factor = null, MaxDelay = null, MaxAttempts = 3, JitterPct = 0, RetryOn = [] });
        var executor = new RecordingExecutor(new NodeExecutionResult.Fail("E_RETRY", "boom", true, null));
        var clock = new InstantTimeProvider();
        var retries = new List<(int Attempt, TimeSpan Delay)>();

        NodeExecutionResult result = await RunAsync(
            node, executor, clock,
            (attempt, _, delay, _, _) => { retries.Add((attempt, delay)); return Task.CompletedTask; },
            retryParkThreshold: TimeSpan.FromSeconds(5));

        var park = Assert.IsType<NodeExecutionResult.WaitForBookmark>(result);
        var timer = Assert.IsType<TimerWakeCondition>(park.Condition);
        Assert.Equal(clock.GetUtcNow().UtcDateTime + TimeSpan.FromSeconds(30), timer.At);
        Assert.Equal(2, park.LocalStatePatch[BranchStateKeys.RetryAttempt].GetInt32());
        Assert.Equal(1, executor.AttemptCount);
        Assert.Equal([(1, TimeSpan.FromSeconds(30))], retries);
        Assert.Empty(clock.Delays);
    }

    [Fact]
    public async Task A_backoff_under_the_park_threshold_still_waits_on_the_clock()
    {
        var node = CreateNode(new RetryPolicy { Strategy = RetryStrategy.Constant, InitialDelay = TimeSpan.FromSeconds(1), Factor = null, MaxDelay = null, MaxAttempts = 2, JitterPct = 0, RetryOn = [] });
        var executor = new RecordingExecutor(
            new NodeExecutionResult.Fail("E_RETRY", "boom", true, null),
            new NodeExecutionResult.Terminal(BranchTerminalReason.Completed));
        var clock = new InstantTimeProvider();

        NodeExecutionResult result = await RunAsync(node, executor, clock, retryParkThreshold: TimeSpan.FromSeconds(5));

        Assert.IsType<NodeExecutionResult.Terminal>(result);
        Assert.Equal(2, executor.AttemptCount);
        Assert.Equal([TimeSpan.FromSeconds(1)], clock.Delays);
    }

    [Fact]
    public async Task A_visit_woken_from_a_retry_park_runs_the_parked_attempt_and_reports_its_retries_from_there()
    {
        var node = CreateNode(new RetryPolicy { Strategy = RetryStrategy.Linear, InitialDelay = TimeSpan.FromSeconds(10), Factor = null, MaxDelay = null, MaxAttempts = 4, JitterPct = 0, RetryOn = [] });
        var executor = new RecordingExecutor(new NodeExecutionResult.Fail("E_RETRY", "again", true, null));
        var clock = new InstantTimeProvider();
        var retries = new List<(int Attempt, TimeSpan Delay)>();

        NodeExecutionResult result = await RunAsync(
            node, executor, clock,
            (attempt, _, delay, _, _) => { retries.Add((attempt, delay)); return Task.CompletedTask; },
            retryParkThreshold: TimeSpan.FromSeconds(5),
            firstAttempt: 2);

        var park = Assert.IsType<NodeExecutionResult.WaitForBookmark>(result);
        Assert.Equal([2], executor.Attempts);
        Assert.Equal([(2, TimeSpan.FromSeconds(20))], retries);
        Assert.Equal(3, park.LocalStatePatch[BranchStateKeys.RetryAttempt].GetInt32());
    }

    [Fact]
    public async Task The_last_parked_attempt_failing_is_the_nodes_failure()
    {
        var node = CreateNode(new RetryPolicy { Strategy = RetryStrategy.Constant, InitialDelay = TimeSpan.FromSeconds(30), Factor = null, MaxDelay = null, MaxAttempts = 3, JitterPct = 0, RetryOn = [] });
        var executor = new RecordingExecutor(new NodeExecutionResult.Fail("E_RETRY", "still", true, null));

        NodeExecutionResult result = await RunAsync(node, executor, new InstantTimeProvider(), retryParkThreshold: TimeSpan.FromSeconds(5), firstAttempt: 3);

        Assert.Equal("E_RETRY", Assert.IsType<NodeExecutionResult.Fail>(result).ErrorCode);
        Assert.Equal([3], executor.Attempts);
    }

    [Fact]
    public async Task A_recorded_retryable_failure_is_retried_rather_than_returned_as_the_nodes_outcome()
    {
        // A crash after an attempt's failure was recorded but before its park was written replays the
        // same key. Returning the recorded failure ended the node with attempts still left; the failed
        // attempt is not run again, and the retry it was owed is taken instead.
        var node = CreateNode(new RetryPolicy { Strategy = RetryStrategy.Constant, InitialDelay = TimeSpan.FromSeconds(30), Factor = null, MaxDelay = null, MaxAttempts = 3, JitterPct = 0, RetryOn = [] });
        var executor = new RecordingExecutor(new NodeExecutionResult.Fail("E_RETRY", "boom", true, null));
        var keys = new InMemoryIdempotencyKeyProvider();
        BranchContext context = CreateContext();

        NodeExecutionResult first = await RunParkingAsync(node, context, executor, keys);
        NodeExecutionResult replay = await RunParkingAsync(node, context, executor, keys);

        Assert.IsType<NodeExecutionResult.WaitForBookmark>(first);
        var park = Assert.IsType<NodeExecutionResult.WaitForBookmark>(replay);
        Assert.Equal(2, park.LocalStatePatch[BranchStateKeys.RetryAttempt].GetInt32());
        Assert.Equal(1, executor.AttemptCount);
        IdempotencyKeyRow row = Assert.Single(keys.Rows);
        Assert.Equal(IdempotencyKeyStatus.Failed, row.Status);
    }

    [Fact]
    public async Task A_parked_retry_runs_its_next_attempt_under_a_key_of_its_own()
    {
        // The woken visit must not be answered by the failed attempt's recorded outcome.
        var node = CreateNode(new RetryPolicy { Strategy = RetryStrategy.Constant, InitialDelay = TimeSpan.FromSeconds(30), Factor = null, MaxDelay = null, MaxAttempts = 3, JitterPct = 0, RetryOn = [] });
        var executor = new RecordingExecutor(
            new NodeExecutionResult.Fail("E_RETRY", "boom", true, null),
            new NodeExecutionResult.Terminal(BranchTerminalReason.Completed));
        var keys = new InMemoryIdempotencyKeyProvider();
        BranchContext context = CreateContext();

        await RunParkingAsync(node, context, executor, keys);
        NodeExecutionResult woken = await RunParkingAsync(node, context, executor, keys, firstAttempt: 2);

        Assert.IsType<NodeExecutionResult.Terminal>(woken);
        Assert.Equal([1, 2], executor.Attempts);
        Assert.Equal(2, keys.Rows.Count);
        Assert.Contains(keys.Rows, row => row.Attempt == 2 && row.Status == IdempotencyKeyStatus.Succeeded);
    }

    [Theory]
    [InlineData("2", 2)]
    [InlineData("\"2\"", null)]
    [InlineData("0", null)]
    public void Reads_only_a_positive_attempt_number_as_a_retry_park(string json, int? expected)
    {
        var state = new Dictionary<string, JsonElement> { [BranchStateKeys.RetryAttempt] = JsonDocument.Parse(json).RootElement.Clone() };

        Assert.Equal(expected, RetryExecutor.ReadParkedAttempt(state));
        Assert.Null(RetryExecutor.ReadParkedAttempt(new Dictionary<string, JsonElement>()));
    }

    private static Task<NodeExecutionResult> RunParkingAsync(BaseNode node, BranchContext context, INodeExecutor executor, IIdempotencyKeyProvider keys, int firstAttempt = 1)
    {
        return RetryExecutor.RunWithRetryAsync(
            node,
            context,
            executor,
            new TestNodeExecutionServices(keys),
            new InstantTimeProvider(),
            CancellationToken.None,
            new UnlimitedRunCountersProvider(),
            new DefaultCreditCostCalculator(),
            firstAttempt: firstAttempt,
            retryParkThreshold: TimeSpan.FromSeconds(5));
    }

    private static Task<NodeExecutionResult> RunAsync(BaseNode node, INodeExecutor executor, TimeProvider clock, RetryNotification? onRetry = null, TimeSpan? retryParkThreshold = null, int firstAttempt = 1)
    {
        return RetryExecutor.RunWithRetryAsync(
            node,
            CreateContext(),
            executor,
            new TestNodeExecutionServices(),
            clock,
            CancellationToken.None,
            new UnlimitedRunCountersProvider(),
            new DefaultCreditCostCalculator(),
            onRetry: onRetry,
            firstAttempt: firstAttempt,
            retryParkThreshold: retryParkThreshold);
    }

    /// <summary>
    /// Records every delay the retry executor asks for and lets it elapse at once, so backoff can be
    /// asserted exactly without waiting on real time.
    /// </summary>
    private sealed class InstantTimeProvider : TimeProvider
    {
        private readonly List<TimeSpan> _delays = [];

        public IReadOnlyList<TimeSpan> Delays
        {
            get
            {
                lock (_delays)
                {
                    return [.. _delays];
                }
            }
        }

        public override DateTimeOffset GetUtcNow() => new(2026, 5, 26, 12, 30, 0, TimeSpan.Zero);

        public override ITimer CreateTimer(TimerCallback callback, object? state, TimeSpan dueTime, TimeSpan period)
        {
            lock (_delays)
            {
                _delays.Add(dueTime);
            }

            // Fired off the calling thread so the timer is returned before its callback runs, as a real
            // one would be.
            ThreadPool.QueueUserWorkItem(_ => callback(state));
            return new NoOpTimer();
        }

        private sealed class NoOpTimer : ITimer
        {
            public bool Change(TimeSpan dueTime, TimeSpan period) => true;

            public void Dispose()
            {
            }

            public ValueTask DisposeAsync() => ValueTask.CompletedTask;
        }
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
            cts.Token,
            new UnlimitedRunCountersProvider(),
            new DefaultCreditCostCalculator()));

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
            CancellationToken.None,
            new UnlimitedRunCountersProvider(),
            new DefaultCreditCostCalculator());

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

        /// <summary>The attempt number each call was made with.</summary>
        public List<int> Attempts { get; } = [];

        public string Kind => NodeKind.ActionClientMessage;

        public Task<NodeExecutionResult> ExecuteAsync(NodeContext ctx, CancellationToken ct)
        {
            AttemptCount++;
            Attempts.Add(ctx.Branch.Attempt);
            return Task.FromResult(_results.Count > 1 ? _results.Dequeue() : _results.Peek());
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
            CancellationToken.None,
            new UnlimitedRunCountersProvider(),
            new DefaultCreditCostCalculator());

        Assert.IsType<NodeExecutionResult.Terminal>(result);
        idempotencyMock.Verify(p => p.UpsertPendingAsync(It.IsAny<string>(), It.IsAny<int>(), It.IsAny<Guid>(), It.IsAny<Guid>(), It.IsAny<int>(), It.IsAny<CancellationToken>()), Times.Once);
        idempotencyMock.Verify(p => p.MarkSucceededAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task RunWithRetryAsync_keys_each_visit_to_a_node_separately()
    {
        // A sequential ForEach brings the branch back to the same node on every lap. Each lap is a
        // new visit with its own key, so it runs the side effect rather than replaying lap one's
        // cached result; a crash-and-recover replay of one visit still shares that visit's key.
        var node = CreateNode(new RetryPolicy { Strategy = RetryStrategy.Constant, InitialDelay = TimeSpan.Zero, Factor = null, MaxDelay = null, MaxAttempts = 1, JitterPct = 0, RetryOn = [] });
        var executor = new RecordingExecutor(new NodeExecutionResult.Terminal(BranchTerminalReason.Completed));

        var keys = new List<string>();
        var idempotencyMock = new Mock<IIdempotencyKeyProvider>();
        idempotencyMock.Setup(p => p.GetByKeyAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .Callback((string key, CancellationToken _) => keys.Add(key))
            .ThrowsAsync(new KeyNotFoundException());
        idempotencyMock.Setup(p => p.UpsertPendingAsync(It.IsAny<string>(), It.IsAny<int>(), It.IsAny<Guid>(), It.IsAny<Guid>(), It.IsAny<int>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((string key, int runId, Guid claimToken, Guid nodeId, int attempt, CancellationToken _) => new IdempotencyKeyRow
            {
                Id = 1, KeyValue = key, RunId = runId, BranchRefId = claimToken, NodeId = nodeId, Attempt = attempt, Status = "Pending", CreatedAt = DateTime.UtcNow, ResultJson = null, ErrorJson = null, CompletedAt = null
            });

        var services = new TestNodeExecutionServices(idempotencyMock.Object);

        foreach (string visit in new[] { "00000000000007D1", "00000000000007D9", "00000000000007D1" })
        {
            await RetryExecutor.RunWithRetryAsync(
                node,
                CreateContext() with { VisitToken = visit },
                executor,
                services,
                new FixedClock(),
                CancellationToken.None,
                new UnlimitedRunCountersProvider(),
                new DefaultCreditCostCalculator());
        }

        Assert.Equal(3, keys.Count);
        Assert.NotEqual(keys[0], keys[1]);
        Assert.Equal(keys[0], keys[2]);
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
            CancellationToken.None,
            new UnlimitedRunCountersProvider(),
            new DefaultCreditCostCalculator());

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
            CancellationToken.None,
            new UnlimitedRunCountersProvider(),
            new DefaultCreditCostCalculator());

        var term = Assert.IsType<NodeExecutionResult.Terminal>(result);
        Assert.Equal(BranchTerminalReason.Completed, term.Reason);
        Assert.Equal(0, executor.AttemptCount);
    }

    [Fact]
    public async Task RunWithRetryAsync_replay_safe_runs_without_claiming_a_key()
    {
        var node = CreateNode(new RetryPolicy { Strategy = RetryStrategy.Constant, InitialDelay = TimeSpan.Zero, Factor = null, MaxDelay = null, MaxAttempts = 1, JitterPct = 0, RetryOn = [] });
        
        var executorMock = new Mock<INodeExecutor>();
        executorMock.SetupGet(e => e.Kind).Returns("test");
        executorMock.Setup(e => e.IsReplaySafe(It.IsAny<BaseNode>())).Returns(true);
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
            CancellationToken.None,
            new UnlimitedRunCountersProvider(),
            new DefaultCreditCostCalculator());

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
        countersMock.Setup(p => p.TryChargeAsync(It.IsAny<int>(), It.IsAny<decimal>(), It.IsAny<CancellationToken>())).ReturnsAsync((CreditCharge?)null);

        NodeExecutionResult result = await RetryExecutor.RunWithRetryAsync(
            node,
            CreateContext(),
            executor,
            new TestNodeExecutionServices(),
            new FixedClock(),
            CancellationToken.None,
            countersMock.Object,
            new DefaultCreditCostCalculator());

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
        countersMock.Setup(p => p.TryChargeAsync(It.IsAny<int>(), It.IsAny<decimal>(), It.IsAny<CancellationToken>())).ReturnsAsync(new CreditCharge(1m, 100m));

        NodeExecutionResult result = await RetryExecutor.RunWithRetryAsync(
            node,
            CreateContext(),
            executor,
            new TestNodeExecutionServices(),
            new FixedClock(),
            CancellationToken.None,
            countersMock.Object,
            new DefaultCreditCostCalculator());

        Assert.IsType<NodeExecutionResult.Terminal>(result);
        Assert.Equal(1, executor.AttemptCount);
        countersMock.Verify(p => p.TryChargeAsync(42, It.IsAny<decimal>(), It.IsAny<CancellationToken>()), Times.Once);
        countersMock.Verify(p => p.GetByRunIdAsync(It.IsAny<int>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Theory]
    [InlineData(85, true)]
    [InlineData(80, false)]
    [InlineData(95, false)]
    public async Task Only_the_charge_that_crosses_80_percent_of_the_budget_warns(int spentAfter, bool warns)
    {
        var node = CreateNode(new RetryPolicy { Strategy = RetryStrategy.Constant, InitialDelay = TimeSpan.Zero, Factor = null, MaxDelay = null, MaxAttempts = 1, JitterPct = 0, RetryOn = [] });
        var executor = new RecordingExecutor(new NodeExecutionResult.Terminal(BranchTerminalReason.Completed));
        var countersMock = new Mock<IRunCountersProvider>();
        countersMock.Setup(p => p.TryChargeAsync(It.IsAny<int>(), 10m, It.IsAny<CancellationToken>())).ReturnsAsync(new CreditCharge(spentAfter, 100m));

        NodeExecutionResult result = await RetryExecutor.RunWithRetryAsync(
            node, CreateContext(), executor, new TestNodeExecutionServices(), new FixedClock(), CancellationToken.None,
            countersMock.Object, Mock.Of<ICreditCostCalculator>(c => c.Calculate(It.IsAny<BaseNode>()) == 10m));

        if (warns)
        {
            NodeWarning warning = Assert.Single(result.Warnings);
            Assert.Equal(HistoryEventKind.CreditBudgetLow, warning.EventKind);
            Assert.Equal($$"""{"spent":{{spentAfter}},"budget":100}""", JsonSerializer.Serialize(warning.Payload));
        }
        else
        {
            Assert.Empty(result.Warnings);
        }
    }

    [Fact]
    public async Task The_budget_warning_outlives_a_retry_of_the_attempt_that_crossed()
    {
        var node = CreateNode(new RetryPolicy { Strategy = RetryStrategy.Constant, InitialDelay = TimeSpan.Zero, Factor = null, MaxDelay = null, MaxAttempts = 2, JitterPct = 0, RetryOn = [] });
        var executor = new RecordingExecutor(
            new NodeExecutionResult.Fail("E_RETRY", "boom", true, null),
            new NodeExecutionResult.Terminal(BranchTerminalReason.Completed));
        var countersMock = new Mock<IRunCountersProvider>();
        countersMock.SetupSequence(p => p.TryChargeAsync(It.IsAny<int>(), It.IsAny<decimal>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new CreditCharge(85m, 100m))
            .ReturnsAsync(new CreditCharge(95m, 100m));

        NodeExecutionResult result = await RetryExecutor.RunWithRetryAsync(
            node, CreateContext(), executor, new TestNodeExecutionServices(), new InstantTimeProvider(), CancellationToken.None,
            countersMock.Object, Mock.Of<ICreditCostCalculator>(c => c.Calculate(It.IsAny<BaseNode>()) == 10m));

        Assert.IsType<NodeExecutionResult.Terminal>(result);
        Assert.Equal(HistoryEventKind.CreditBudgetLow, Assert.Single(result.Warnings).EventKind);
    }

    /// <summary>
    /// A shared Increment writes outside the branch row before the pointer moves, so a crash in between
    /// replays it. The replay has to come back from the idempotency key rather than counting again.
    /// </summary>
    [Fact]
    public async Task Shared_increment_claims_a_key_and_a_replay_returns_the_recorded_outcome_without_counting_again()
    {
        var shared = new Mock<ISharedVariableProvider>();
        shared.Setup(p => p.IncrementAsync(It.IsAny<Guid>(), "hits", 1, It.IsAny<CancellationToken>())).ReturnsAsync("1");
        var executor = new VariableNodeExecutor(shared.Object, Mock.Of<IExpressionEvaluator>());
        VariableNode node = CreateVariableNode(VariableScope.Shared, VariableOperation.Increment);
        var keys = new InMemoryIdempotencyKeyProvider();
        BranchContext context = CreateContext();

        NodeExecutionResult first = await RunAsync(node, context, executor, keys);
        NodeExecutionResult replay = await RunAsync(node, context, executor, keys);

        Assert.IsType<NodeExecutionResult.Continue>(first);
        Assert.IsType<NodeExecutionResult.Continue>(replay);
        IdempotencyKeyRow row = Assert.Single(keys.Rows);
        Assert.Equal(IdempotencyKeyStatus.Succeeded, row.Status);
        shared.Verify(p => p.IncrementAsync(It.IsAny<Guid>(), It.IsAny<string>(), It.IsAny<long>(), It.IsAny<CancellationToken>()), Times.Once);
    }

    [Theory]
    [InlineData(VariableScope.Shared, VariableOperation.Increment, false)]
    [InlineData(VariableScope.Shared, VariableOperation.Decrement, false)]
    [InlineData(VariableScope.Shared, VariableOperation.CompareAndSet, false)]
    [InlineData(VariableScope.Shared, VariableOperation.Set, true)]
    [InlineData(VariableScope.Local, VariableOperation.Set, true)]
    [InlineData(VariableScope.Local, VariableOperation.Increment, true)]
    [InlineData(VariableScope.Local, VariableOperation.Decrement, true)]
    [InlineData(VariableScope.Local, VariableOperation.CompareAndSet, true)]
    public void Variable_node_is_replay_safe_unless_a_replay_would_repeat_a_shared_effect(VariableScope scope, VariableOperation op, bool expected)
    {
        var executor = new VariableNodeExecutor(Mock.Of<ISharedVariableProvider>(), Mock.Of<IExpressionEvaluator>());

        Assert.Equal(expected, executor.IsReplaySafe(CreateVariableNode(scope, op)));
    }

    [Fact]
    public async Task Shared_set_claims_no_key_and_simply_writes_again_on_a_replay()
    {
        var shared = new Mock<ISharedVariableProvider>();
        var executor = new VariableNodeExecutor(shared.Object, Mock.Of<IExpressionEvaluator>());
        VariableNode node = CreateVariableNode(VariableScope.Shared, VariableOperation.Set, JsonSerializer.SerializeToElement("on"));
        var keys = new InMemoryIdempotencyKeyProvider();
        BranchContext context = CreateContext();

        await RunAsync(node, context, executor, keys);
        await RunAsync(node, context, executor, keys);

        Assert.Empty(keys.Rows);
        shared.Verify(p => p.SetAsync(It.IsAny<Guid>(), "hits", "\"on\"", It.IsAny<CancellationToken>()), Times.Exactly(2));
    }

    [Theory]
    [InlineData(VariableOperation.Set)]
    [InlineData(VariableOperation.Increment)]
    public async Task Local_variable_write_claims_no_key(VariableOperation op)
    {
        var shared = new Mock<ISharedVariableProvider>(MockBehavior.Strict);
        var executor = new VariableNodeExecutor(shared.Object, Mock.Of<IExpressionEvaluator>());
        var keys = new InMemoryIdempotencyKeyProvider();

        NodeExecutionResult result = await RunAsync(CreateVariableNode(VariableScope.Local, op, JsonSerializer.SerializeToElement(1)), CreateContext(), executor, keys);

        Assert.IsType<NodeExecutionResult.Continue>(result);
        Assert.Empty(keys.Rows);
    }

    [Fact]
    public async Task Trigger_passthrough_claims_no_key()
    {
        var executor = new PassthroughTriggerExecutor(NodeKind.TriggerManual, new FixedClock());
        var node = new ManualTriggerNode { NodeId = Guid.NewGuid(), Name = "start", Ports = [], Config = new ManualTriggerConfig() };
        var keys = new InMemoryIdempotencyKeyProvider();

        NodeExecutionResult result = await RunAsync(node, CreateContext(), executor, keys);

        Assert.IsType<NodeExecutionResult.Continue>(result);
        Assert.Empty(keys.Rows);
    }

    [Fact]
    public async Task Await_signal_park_claims_no_key()
    {
        var executor = new AwaitSignalNodeExecutor(new FixedClock());
        var node = new AwaitSignalNode { NodeId = Guid.NewGuid(), Name = "await", Ports = [], Config = new AwaitSignalConfig { SignalName = "go" } };
        var keys = new InMemoryIdempotencyKeyProvider();

        NodeExecutionResult result = await RunAsync(node, CreateContext(), executor, keys);

        Assert.IsType<NodeExecutionResult.WaitForBookmark>(result);
        Assert.Empty(keys.Rows);
    }

    private static Task<NodeExecutionResult> RunAsync(BaseNode node, BranchContext context, INodeExecutor executor, IIdempotencyKeyProvider keys)
    {
        return RetryExecutor.RunWithRetryAsync(
            node,
            context,
            executor,
            new TestNodeExecutionServices(keys),
            new FixedClock(),
            CancellationToken.None,
            new UnlimitedRunCountersProvider(),
            new DefaultCreditCostCalculator());
    }

    private static VariableNode CreateVariableNode(VariableScope scope, VariableOperation op, JsonElement? value = null)
    {
        return new VariableNode
        {
            NodeId = Guid.Parse("22222222-2222-2222-2222-222222222222"),
            Name = "variable",
            Ports = [],
            Config = new VariableConfig { Scope = scope, Op = op, Var = "hits", Value = value, Expected = JsonSerializer.SerializeToElement(0) }
        };
    }

    private sealed class TestNodeExecutionServices(IIdempotencyKeyProvider? idempotencyKeys = null) : INodeExecutionServices
    {
        public IIdempotencyKeyProvider IdempotencyKeys { get; } = idempotencyKeys ?? new InMemoryIdempotencyKeyProvider();
    }

    private sealed class FixedClock : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => UtcNow;

        public DateTime UtcNow => new(2026, 5, 26, 12, 30, 0, DateTimeKind.Utc);
    }
}
