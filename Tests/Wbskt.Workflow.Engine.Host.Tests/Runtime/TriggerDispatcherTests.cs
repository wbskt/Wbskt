using System.Text.Json;
using Wbskt.Workflow.Abstraction.Entities;
using Wbskt.Workflow.Abstraction.Enums;
using Wbskt.Workflow.Abstraction.Models.Expressions;
using Wbskt.Workflow.Abstraction.Providers;
using Wbskt.Workflow.Abstraction.Runtime;
using Wbskt.Workflow.Runtime;

namespace Wbskt.Workflow.Engine.Host.Tests.Runtime;

public sealed class TriggerDispatcherTests
{
    [Fact]
    public async Task Dispatch_resumes_bookmark_when_match_exists()
    {
        // Arrange
        var bookmarkResumer = new RecordingBookmarkResumer(new BookmarkMatchResult(true, 77, false));
        var dispatcher = CreateDispatcher(bookmarkResumer: bookmarkResumer);
        InboundEvent evt = CreateInboundEvent();

        // Act
        TriggerDispatchResult result = await dispatcher.DispatchAsync(evt, CancellationToken.None);

        // Assert
        Assert.Equal(TriggerDispatchOutcome.ResumedBookmark, result.Outcome);
        Assert.Equal(77L, result.BookmarkId);
        Assert.Equal("client:serial-1:telemetry", bookmarkResumer.Events.Single().CorrelationKey);
    }

    [Fact]
    public async Task Dispatch_returns_Idempotent_when_event_already_processed()
    {
        // Arrange
        var dispatcher = CreateDispatcher(bookmarkResumer: new RecordingBookmarkResumer(new BookmarkMatchResult(false, null, true)));

        // Act
        TriggerDispatchResult result = await dispatcher.DispatchAsync(CreateInboundEvent(), CancellationToken.None);

        // Assert
        Assert.Equal(TriggerDispatchOutcome.Idempotent, result.Outcome);
    }

    [Fact]
    public async Task Dispatch_returns_NoRegistration_when_no_trigger_matches()
    {
        // Arrange
        var dispatcher = CreateDispatcher();

        // Act
        TriggerDispatchResult result = await dispatcher.DispatchAsync(CreateInboundEvent(), CancellationToken.None);

        // Assert
        Assert.Equal(TriggerDispatchOutcome.NoRegistration, result.Outcome);
    }

    [Fact]
    public async Task Dispatch_starts_new_run_when_registration_exists_and_policy_permits()
    {
        // Arrange
        var registration = CreateRegistration("AllowParallel");
        var dispatcher = CreateDispatcher(triggerRegistrationProvider: new RecordingTriggerRegistrationProvider(registration));

        // Act
        TriggerDispatchResult result = await dispatcher.DispatchAsync(CreateInboundEvent(), CancellationToken.None);

        // Assert
        Assert.Equal(TriggerDispatchOutcome.StartedRun, result.Outcome);
        Assert.Equal("client:serial-1:telemetry", result.Reason);
    }

    [Fact]
    public async Task Dispatch_returns_Dropped_when_enforcer_returns_Dropped()
    {
        // Arrange
        var registration = CreateRegistration("DropIfRunning");
        var dispatcher = CreateDispatcher(
            triggerRegistrationProvider: new RecordingTriggerRegistrationProvider(registration),
            concurrencyEnforcer: new RecordingTriggerConcurrencyEnforcer(new TriggerConcurrencyDecision(TriggerConcurrencyOutcome.Dropped, [])));

        // Act
        TriggerDispatchResult result = await dispatcher.DispatchAsync(CreateInboundEvent(), CancellationToken.None);

        // Assert
        Assert.Equal(TriggerDispatchOutcome.Dropped, result.Outcome);
    }

    [Fact]
    public async Task Dispatch_returns_Queued_when_enforcer_returns_Queued()
    {
        // Arrange
        var registration = CreateRegistration("Queue");
        var dispatcher = CreateDispatcher(
            triggerRegistrationProvider: new RecordingTriggerRegistrationProvider(registration),
            concurrencyEnforcer: new RecordingTriggerConcurrencyEnforcer(new TriggerConcurrencyDecision(TriggerConcurrencyOutcome.Queued, [])));

        // Act
        TriggerDispatchResult result = await dispatcher.DispatchAsync(CreateInboundEvent(), CancellationToken.None);

        // Assert
        Assert.Equal(TriggerDispatchOutcome.Queued, result.Outcome);
    }

    [Fact]
    public async Task Dispatch_cancels_active_run_and_starts_new_when_enforcer_returns_ProceedAfterCancellingActive()
    {
        // Arrange
        var registration = CreateRegistration("CancelExisting");
        var cancellationService = new RecordingRunCancellationService();
        var dispatcher = CreateDispatcher(
            triggerRegistrationProvider: new RecordingTriggerRegistrationProvider(registration),
            concurrencyEnforcer: new RecordingTriggerConcurrencyEnforcer(new TriggerConcurrencyDecision(TriggerConcurrencyOutcome.ProceedAfterCancellingActive, [55L])),
            runCancellationService: cancellationService);

        // Act
        TriggerDispatchResult result = await dispatcher.DispatchAsync(CreateInboundEvent(), CancellationToken.None);

        // Assert
        Assert.Equal(TriggerDispatchOutcome.StartedRun, result.Outcome);
        Assert.Equal([(55L, "Trigger-cancel-policy")], cancellationService.Requests);
    }

    [Fact]
    public async Task Dispatch_cancels_every_matching_run_when_ProceedAfterCancellingActive_has_multiple_run_ids()
    {
        // Arrange: CancelExisting must cancel all matches, not just the first (2.3).
        var registration = CreateRegistration("CancelExisting");
        var cancellationService = new RecordingRunCancellationService();
        var dispatcher = CreateDispatcher(
            triggerRegistrationProvider: new RecordingTriggerRegistrationProvider(registration),
            concurrencyEnforcer: new RecordingTriggerConcurrencyEnforcer(new TriggerConcurrencyDecision(TriggerConcurrencyOutcome.ProceedAfterCancellingActive, [55L, 56L, 57L])),
            runCancellationService: cancellationService);

        // Act
        TriggerDispatchResult result = await dispatcher.DispatchAsync(CreateInboundEvent(), CancellationToken.None);

        // Assert
        Assert.Equal(TriggerDispatchOutcome.StartedRun, result.Outcome);
        Assert.Equal(
            [(55L, "Trigger-cancel-policy"), (56L, "Trigger-cancel-policy"), (57L, "Trigger-cancel-policy")],
            cancellationService.Requests);
    }

    [Fact]
    public async Task Dispatch_with_StartedRun_outcome_invokes_RunStarter_and_RunDispatcher()
    {
        // Arrange
        var operations = new List<string>();
        var registration = CreateRegistration("AllowParallel");
        var runStarter = new RecordingRunStarter(operations);
        var runDispatcher = new RecordingRunDispatcher(operations);
        var dispatcher = CreateDispatcher(
            triggerRegistrationProvider: new RecordingTriggerRegistrationProvider(registration),
            runStarter: runStarter,
            runDispatcher: runDispatcher);

        // Act
        TriggerDispatchResult result = await dispatcher.DispatchAsync(CreateInboundEvent(), CancellationToken.None);

        // Assert
        Assert.Equal(TriggerDispatchOutcome.StartedRun, result.Outcome);
        Assert.Equal(501L, result.RunId);
        Assert.Equal(["start", "dispatch"], operations);
        Assert.Equal([(501L, 801L, BranchExecutionReason.TriggerStarted)], runDispatcher.Requests);
    }

    [Fact]
    public async Task Dispatch_reports_an_outcome_for_every_matched_registration()
    {
        // Arrange: one event, two registrations on the same trigger key - the fan-out case that used
        // to collapse into a single run id.
        var first = CreateRegistration("AllowParallel", id: 5);
        var second = CreateRegistration("AllowParallel", id: 6, workflowRefId: Guid.Parse("cccccccc-cccc-cccc-cccc-cccccccccccc"));
        var dispatcher = CreateDispatcher(triggerRegistrationProvider: new RecordingTriggerRegistrationProvider(first, second));

        // Act
        TriggerDispatchResult result = await dispatcher.DispatchAsync(CreateInboundEvent(), CancellationToken.None);

        // Assert
        Assert.Equal(TriggerDispatchOutcome.StartedRun, result.Outcome);
        Assert.Equal([501L, 502L], result.StartedRunIds);
        Assert.Collection(
            result.Registrations,
            entry =>
            {
                Assert.Equal(5L, entry.RegistrationId);
                Assert.Equal(first.WorkflowRefId, entry.WorkflowRefId);
                Assert.Equal(TriggerDispatchOutcome.StartedRun, entry.Outcome);
                Assert.Equal(501L, entry.RunId);
            },
            entry =>
            {
                Assert.Equal(6L, entry.RegistrationId);
                Assert.Equal(second.WorkflowRefId, entry.WorkflowRefId);
                Assert.Equal(TriggerDispatchOutcome.StartedRun, entry.Outcome);
                Assert.Equal(502L, entry.RunId);
            });
    }

    [Fact]
    public async Task Dispatch_keeps_a_dropped_registration_visible_when_another_starts_a_run()
    {
        // Arrange: the mixed case. The summary can only name one outcome, so the dropped registration
        // must still be reported per-registration or it disappears entirely.
        var dropped = CreateRegistration("DropIfRunning", id: 5);
        var started = CreateRegistration("AllowParallel", id: 6);
        var dispatcher = CreateDispatcher(
            triggerRegistrationProvider: new RecordingTriggerRegistrationProvider(dropped, started),
            concurrencyEnforcer: new RecordingTriggerConcurrencyEnforcer(
                new TriggerConcurrencyDecision(TriggerConcurrencyOutcome.Proceed, []),
                new Dictionary<long, TriggerConcurrencyDecision>
                {
                    [5L] = new(TriggerConcurrencyOutcome.Dropped, [])
                }));

        // Act
        TriggerDispatchResult result = await dispatcher.DispatchAsync(CreateInboundEvent(), CancellationToken.None);

        // Assert
        Assert.Equal(TriggerDispatchOutcome.StartedRun, result.Outcome);
        Assert.Equal(501L, result.RunId);
        Assert.Equal(
            [(5L, TriggerDispatchOutcome.Dropped, (long?)null), (6L, TriggerDispatchOutcome.StartedRun, (long?)501L)],
            result.Registrations.Select(r => (r.RegistrationId, r.Outcome, r.RunId)));
    }

    [Fact]
    public async Task Dispatch_reports_no_registrations_when_the_event_resumed_a_bookmark()
    {
        // Arrange: the bookmark path returns before registrations are read, so there is nothing to report.
        var dispatcher = CreateDispatcher(bookmarkResumer: new RecordingBookmarkResumer(new BookmarkMatchResult(true, 77, false)));

        // Act
        TriggerDispatchResult result = await dispatcher.DispatchAsync(CreateInboundEvent(), CancellationToken.None);

        // Assert
        Assert.Empty(result.Registrations);
        Assert.Empty(result.StartedRunIds);
    }

    [Fact]
    public async Task Dispatch_skips_a_registration_whose_filter_rejects_the_payload()
    {
        // The whole point of a filter: no run, no credits burned, no run history to explain.
        var registration = CreateRegistration("AllowParallel");
        registration = registration with { FilterExpression = Filter("messageType", "not-telemetry") };
        var operations = new List<string>();
        var runStarter = new RecordingRunStarter(operations);
        var dispatcher = CreateDispatcher(
            triggerRegistrationProvider: new RecordingTriggerRegistrationProvider(registration),
            runStarter: runStarter);

        TriggerDispatchResult result = await dispatcher.DispatchAsync(CreateInboundEvent(), CancellationToken.None);

        Assert.Equal(TriggerDispatchOutcome.Filtered, result.Outcome);
        Assert.Empty(operations);
        Assert.Equal(TriggerDispatchOutcome.Filtered, Assert.Single(result.Registrations).Outcome);
    }

    [Fact]
    public async Task Dispatch_starts_a_run_when_the_filter_matches_the_payload()
    {
        var registration = CreateRegistration("AllowParallel");
        registration = registration with { FilterExpression = Filter("messageType", "telemetry") };
        var dispatcher = CreateDispatcher(triggerRegistrationProvider: new RecordingTriggerRegistrationProvider(registration));

        TriggerDispatchResult result = await dispatcher.DispatchAsync(CreateInboundEvent(), CancellationToken.None);

        Assert.Equal(TriggerDispatchOutcome.StartedRun, result.Outcome);
    }

    [Fact]
    public async Task Dispatch_treats_an_unevaluatable_filter_as_non_matching()
    {
        // Fail closed. A filter is a gate; one that cannot be evaluated has not been passed, and
        // starting the run anyway would defeat the point of configuring it.
        var registration = CreateRegistration("AllowParallel");
        registration = registration with { FilterExpression = "{ not json" };
        var operations = new List<string>();
        var dispatcher = CreateDispatcher(
            triggerRegistrationProvider: new RecordingTriggerRegistrationProvider(registration),
            runStarter: new RecordingRunStarter(operations));

        TriggerDispatchResult result = await dispatcher.DispatchAsync(CreateInboundEvent(), CancellationToken.None);

        Assert.Equal(TriggerDispatchOutcome.Filtered, result.Outcome);
        Assert.Empty(operations);
    }

    [Fact]
    public async Task Dispatch_rejects_a_webhook_whose_secret_does_not_match()
    {
        var registration = CreateRegistration("AllowParallel") with { WebhookSecret = "expected" };
        var operations = new List<string>();
        var dispatcher = CreateDispatcher(
            triggerRegistrationProvider: new RecordingTriggerRegistrationProvider(registration),
            runStarter: new RecordingRunStarter(operations));

        TriggerDispatchResult result = await dispatcher.DispatchAsync(CreateInboundEvent() with { Secret = "wrong" }, CancellationToken.None);

        Assert.Equal(TriggerDispatchOutcome.SecretMismatch, result.Outcome);
        Assert.Empty(operations);
    }

    [Fact]
    public async Task Dispatch_rejects_a_secret_protected_webhook_when_none_is_presented()
    {
        // The upgrade hazard: adding a secret to a live trigger must not silently keep letting the old,
        // unauthenticated callers through.
        var registration = CreateRegistration("AllowParallel") with { WebhookSecret = "expected" };
        var dispatcher = CreateDispatcher(triggerRegistrationProvider: new RecordingTriggerRegistrationProvider(registration));

        TriggerDispatchResult result = await dispatcher.DispatchAsync(CreateInboundEvent(), CancellationToken.None);

        Assert.Equal(TriggerDispatchOutcome.SecretMismatch, result.Outcome);
    }

    [Fact]
    public async Task Dispatch_accepts_a_webhook_with_the_matching_secret()
    {
        var registration = CreateRegistration("AllowParallel") with { WebhookSecret = "expected" };
        var dispatcher = CreateDispatcher(triggerRegistrationProvider: new RecordingTriggerRegistrationProvider(registration));

        TriggerDispatchResult result = await dispatcher.DispatchAsync(CreateInboundEvent() with { Secret = "expected" }, CancellationToken.None);

        Assert.Equal(TriggerDispatchOutcome.StartedRun, result.Outcome);
    }

    [Fact]
    public async Task Dispatch_leaves_a_registration_without_a_secret_open()
    {
        // Every webhook published before secrets existed has a null one and must keep working.
        var dispatcher = CreateDispatcher(triggerRegistrationProvider: new RecordingTriggerRegistrationProvider(CreateRegistration("AllowParallel")));

        TriggerDispatchResult result = await dispatcher.DispatchAsync(CreateInboundEvent(), CancellationToken.None);

        Assert.Equal(TriggerDispatchOutcome.StartedRun, result.Outcome);
    }

    /// <summary>A serialized `$trigger.{field} == {expected}` comparison, as the publish path stores it.</summary>
    private static string Filter(string field, string expected)
    {
        WorkflowExpression expression = new BinaryExpression(
            new BranchStateRefExpression(field),
            BinaryOperator.Equal,
            new LiteralExpression(expected));

        return JsonSerializer.Serialize(expression, new JsonSerializerOptions(JsonSerializerDefaults.Web));
    }

    [Fact]
    public async Task Dispatch_releases_the_claim_as_failed_when_starting_a_run_throws()
    {
        // A claim left Pending would drop the sender's retry of this delivery as a duplicate; Failed
        // lets BookmarkResumer reclaim it.
        var idempotency = new MockIdempotencyKeyProvider();
        var dispatcher = new TriggerDispatcher(
            new CorrelationKeyResolver(),
            new RecordingBookmarkResumer(new BookmarkMatchResult(false, null, false, "inbound-event:evt-1")),
            new RecordingTriggerRegistrationProvider(CreateRegistration("AllowParallel")),
            new ThrowingTriggerConcurrencyEnforcer(),
            new RecordingRunCancellationService(),
            new RecordingRunStarter([]),
            new RecordingRunDispatcher([]),
            idempotency,
            new ExpressionEvaluator(new DispatcherTestClock()));

        await Assert.ThrowsAsync<InvalidOperationException>(() => dispatcher.DispatchAsync(CreateInboundEvent(), CancellationToken.None));

        Assert.Equal(["inbound-event:evt-1"], idempotency.Failed);
    }

    private static TriggerDispatcher CreateDispatcher(
        RecordingBookmarkResumer? bookmarkResumer = null,
        RecordingTriggerRegistrationProvider? triggerRegistrationProvider = null,
        RecordingTriggerConcurrencyEnforcer? concurrencyEnforcer = null,
        RecordingRunCancellationService? runCancellationService = null,
        RecordingRunStarter? runStarter = null,
        RecordingRunDispatcher? runDispatcher = null)
    {
        return new TriggerDispatcher(
            new CorrelationKeyResolver(),
            bookmarkResumer ?? new RecordingBookmarkResumer(new BookmarkMatchResult(false, null, false)),
            triggerRegistrationProvider ?? new RecordingTriggerRegistrationProvider(),
            concurrencyEnforcer ?? new RecordingTriggerConcurrencyEnforcer(new TriggerConcurrencyDecision(TriggerConcurrencyOutcome.Proceed, [])),
            runCancellationService ?? new RecordingRunCancellationService(),
            runStarter ?? new RecordingRunStarter([]),
            runDispatcher ?? new RecordingRunDispatcher([]),
            new MockIdempotencyKeyProvider(),
            new ExpressionEvaluator(new DispatcherTestClock()));
    }

    private sealed class DispatcherTestClock : IClock
    {
        public DateTime UtcNow => new(2026, 8, 7, 12, 0, 0, DateTimeKind.Utc);
    }

    private sealed class MockIdempotencyKeyProvider : IIdempotencyKeyProvider
    {
        public Task<IdempotencyKeyRow> UpsertPendingAsync(string keyValue, int runId, Guid branchRefId, Guid nodeId, int attempt, CancellationToken ct) => throw new NotImplementedException();
        public Task<IdempotencyKeyRow> GetByKeyAsync(string keyValue, CancellationToken ct) => throw new NotImplementedException();
        public Task<IdempotencyKeyRow> MarkSucceededAsync(string keyValue, string resultJson, CancellationToken ct) => Task.FromResult<IdempotencyKeyRow>(null!);
        public List<string> Failed { get; } = [];
        public Task<IdempotencyKeyRow> MarkFailedAsync(string keyValue, string errorJson, CancellationToken ct)
        {
            Failed.Add(keyValue);
            return Task.FromResult<IdempotencyKeyRow>(null!);
        }
        public Task<IdempotencyKeyRow> ReclaimFailedAsync(string keyValue, Guid newBranchRefId, CancellationToken ct) => throw new NotImplementedException();
        public Task<int> DeleteExpiredAsync(DateTime cutoffUtc, int batchSize, CancellationToken ct) => throw new NotImplementedException();
    }

    private sealed class ThrowingTriggerConcurrencyEnforcer : ITriggerConcurrencyEnforcer
    {
        public Task<TriggerConcurrencyDecision> EvaluateAsync(TriggerRegistrationRow registration, InboundEvent evt, CancellationToken ct) =>
            throw new InvalidOperationException("database unavailable");
    }

    private static InboundEvent CreateInboundEvent()
    {
        return new InboundEvent(
            "client",
            ["client:serial-1:telemetry"],
            "evt-1",
            new Dictionary<string, JsonElement>
            {
                ["clientRefId"] = JsonSerializer.SerializeToElement("serial-1"),
                ["messageType"] = JsonSerializer.SerializeToElement("telemetry")
            },
            DateTime.UtcNow);
    }

    private static TriggerRegistrationRow CreateRegistration(string policy = "Queue", int id = 5, Guid? workflowRefId = null)
    {
        return new TriggerRegistrationRow
        {
            Id = id,
            WorkflowDefinitionId = 42,
            WorkflowRefId = workflowRefId ?? Guid.Parse("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa"),
            WorkflowVersion = 3,
            TriggerNodeId = Guid.Parse("bbbbbbbb-bbbb-bbbb-bbbb-bbbbbbbbbbbb"),
            TriggerKind = "client",
            TriggerKey = "client:serial-1:telemetry",
            CorrelationExpression = null,
            ConcurrencyPolicy = policy,
            FilterExpression = null,
            CreatedAt = DateTime.UtcNow
        };
    }

    private sealed class RecordingBookmarkResumer(BookmarkMatchResult result) : IBookmarkResumer
    {
        public List<InboundEvent> Events { get; } = [];

        public Task<BookmarkMatchResult> MatchInboundAsync(InboundEvent evt, CancellationToken ct)
        {
            Events.Add(evt);
            return Task.FromResult(result);
        }

        public Task ResumeViaBookmarkAsync(long bookmarkId, IReadOnlyDictionary<string, JsonElement> wakePayload, CancellationToken ct)
        {
            throw new NotSupportedException();
        }
    }

    private sealed class RecordingTriggerRegistrationProvider(params TriggerRegistrationRow[] rows) : ITriggerRegistrationProvider
    {
        public Task<TriggerRegistrationRow> InsertAsync(TriggerRegistrationRow row, CancellationToken ct) => throw new NotSupportedException();
        public Task<IReadOnlyCollection<TriggerRegistrationRow>> GetByTriggerKeyAsync(string triggerKey, CancellationToken ct) => throw new NotSupportedException();
        public Task<IReadOnlyCollection<TriggerRegistrationRow>> GetAllByWorkflowDefinitionIdAsync(int workflowDefinitionId, CancellationToken ct) => throw new NotSupportedException();
        public Task DeleteAllByWorkflowDefinitionIdAsync(int workflowDefinitionId, CancellationToken ct) => throw new NotSupportedException();

        public Task<IReadOnlyCollection<TriggerRegistrationRow>> GetActiveByChannelKeysAsync(string channelKind, IReadOnlyCollection<string> channelKeys, CancellationToken ct)
        {
            IReadOnlyCollection<TriggerRegistrationRow> matches = rows.Where(row => row.TriggerKind == channelKind && channelKeys.Contains(row.TriggerKey)).ToArray();
            return Task.FromResult(matches);
        }
    }

    private sealed class RecordingTriggerConcurrencyEnforcer(
        TriggerConcurrencyDecision decision,
        IReadOnlyDictionary<long, TriggerConcurrencyDecision>? perRegistration = null) : ITriggerConcurrencyEnforcer
    {
        public Task<TriggerConcurrencyDecision> EvaluateAsync(TriggerRegistrationRow registration, InboundEvent evt, CancellationToken ct)
        {
            if (perRegistration != null && perRegistration.TryGetValue(registration.Id, out TriggerConcurrencyDecision? specific))
            {
                return Task.FromResult(specific);
            }

            return Task.FromResult(decision);
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
            _ = runId;
            _ = ct;
            return Task.FromResult(false);
        }
    }

    private sealed class RecordingRunStarter(List<string> operations) : IRunStarter
    {
        private int _started;

        public Task<(long RunId, long BranchId)> StartAsync(int workflowDefinitionId, string triggerNodeId, InboundEvent triggerEvent, CancellationToken ct)
        {
            operations.Add("start");
            // Distinct ids per call so a fan-out over several registrations is distinguishable.
            int offset = _started++;
            return Task.FromResult((501L + offset, 801L + offset));
        }
    }

    private sealed class RecordingRunDispatcher(List<string> operations) : IRunDispatcher
    {
        public List<(long RunId, long BranchId, BranchExecutionReason Reason)> Requests { get; } = [];

        public ValueTask DispatchAsync(BranchExecutionRequest request, CancellationToken ct)
        {
            operations.Add("dispatch");
            Requests.Add((request.RunId, request.BranchId, request.Reason));
            return ValueTask.CompletedTask;
        }
    }
}
