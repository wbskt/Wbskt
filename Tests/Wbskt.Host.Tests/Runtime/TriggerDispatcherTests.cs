using System.Text.Json;
using Wbskt.Workflow.Abstraction.Entities;
using Wbskt.Workflow.Abstraction.Enums;
using Wbskt.Workflow.Abstraction.Models.Expressions;
using Wbskt.Workflow.Abstraction.Providers;
using Wbskt.Workflow.Abstraction.Runtime;
using Wbskt.Workflow.Runtime;
using Wbskt.Workflow.Channels;

namespace Wbskt.Host.Tests.Runtime;

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
    public async Task Dispatch_logs_nothing_at_information_or_above_when_no_trigger_matches()
    {
        // Arrange
        var logger = new LevelRecorder();
        var dispatcher = CreateDispatcher(logger: logger);

        // Act
        await dispatcher.DispatchAsync(CreateInboundEvent(), CancellationToken.None);

        // Assert
        Assert.DoesNotContain(logger.Levels, level => level >= Microsoft.Extensions.Logging.LogLevel.Information);
    }

    [Fact]
    public async Task Dispatch_starts_new_run_when_registration_exists_and_policy_permits()
    {
        // Arrange
        var registration = CreateRegistration(WorkflowConcurrencyPolicy.AllowParallel);
        var dispatcher = CreateDispatcher(triggerRegistrationProvider: new RecordingTriggerRegistrationProvider(registration));

        // Act
        TriggerDispatchResult result = await dispatcher.DispatchAsync(CreateInboundEvent(), CancellationToken.None);

        // Assert
        Assert.Equal(TriggerDispatchOutcome.StartedRun, result.Outcome);
        Assert.Equal("client:serial-1:telemetry", result.Reason);
    }

    [Theory]
    [InlineData("{{trigger.payload.room}}")]
    [InlineData("$trigger.payload.room")]
    public async Task Dispatch_correlates_by_a_payload_path_in_either_form(string correlationExpression)
    {
        // The designer writes {{trigger.…}}; read verbatim it would be one constant key for every run.
        var registration = CreateRegistration(WorkflowConcurrencyPolicy.AllowParallel) with { CorrelationExpression = correlationExpression };
        var dispatcher = CreateDispatcher(triggerRegistrationProvider: new RecordingTriggerRegistrationProvider(registration));
        InboundEvent evt = CreateInboundEvent();
        evt = evt with
        {
            Payload = new Dictionary<string, JsonElement>(evt.Payload)
            {
                ["payload"] = JsonSerializer.SerializeToElement(new { room = "north" })
            }
        };

        TriggerDispatchResult result = await dispatcher.DispatchAsync(evt, CancellationToken.None);

        Assert.Equal(TriggerDispatchOutcome.StartedRun, result.Outcome);
        Assert.Equal("north", result.Reason);
    }

    [Fact]
    public async Task Dispatch_returns_Dropped_when_enforcer_returns_Dropped()
    {
        // Arrange
        var registration = CreateRegistration(WorkflowConcurrencyPolicy.DropIfRunning);
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
        var registration = CreateRegistration(WorkflowConcurrencyPolicy.Queue);
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
        var registration = CreateRegistration(WorkflowConcurrencyPolicy.CancelExisting);
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
        var registration = CreateRegistration(WorkflowConcurrencyPolicy.CancelExisting);
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
        var registration = CreateRegistration(WorkflowConcurrencyPolicy.AllowParallel);
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
        Assert.Equal(RunRefFor(501L), result.RunRefId);
        Assert.Equal(["start", "dispatch"], operations);
        Assert.Equal([(501L, 801L, BranchExecutionReason.TriggerStarted)], runDispatcher.Requests);
    }

    [Fact]
    public async Task Dispatch_reports_an_outcome_for_every_matched_registration()
    {
        // Arrange: one event, two registrations on the same trigger key - the fan-out case that used
        // to collapse into a single run id.
        var first = CreateRegistration(WorkflowConcurrencyPolicy.AllowParallel, id: 5);
        var second = CreateRegistration(WorkflowConcurrencyPolicy.AllowParallel, id: 6, workflowRefId: Guid.Parse("cccccccc-cccc-cccc-cccc-cccccccccccc"));
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
                Assert.Equal(RunRefFor(501L), entry.RunRefId);
            },
            entry =>
            {
                Assert.Equal(6L, entry.RegistrationId);
                Assert.Equal(second.WorkflowRefId, entry.WorkflowRefId);
                Assert.Equal(TriggerDispatchOutcome.StartedRun, entry.Outcome);
                Assert.Equal(502L, entry.RunId);
                Assert.Equal(RunRefFor(502L), entry.RunRefId);
            });
    }

    [Fact]
    public async Task Dispatch_keeps_a_dropped_registration_visible_when_another_starts_a_run()
    {
        // Arrange: the mixed case. The summary can only name one outcome, so the dropped registration
        // must still be reported per-registration or it disappears entirely.
        var dropped = CreateRegistration(WorkflowConcurrencyPolicy.DropIfRunning, id: 5);
        var started = CreateRegistration(WorkflowConcurrencyPolicy.AllowParallel, id: 6);
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
        var registration = CreateRegistration(WorkflowConcurrencyPolicy.AllowParallel);
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
        var registration = CreateRegistration(WorkflowConcurrencyPolicy.AllowParallel);
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
        var registration = CreateRegistration(WorkflowConcurrencyPolicy.AllowParallel);
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
        var registration = CreateRegistration(WorkflowConcurrencyPolicy.AllowParallel) with { WebhookSecret = "expected" };
        var operations = new List<string>();
        var dispatcher = CreateDispatcher(
            triggerRegistrationProvider: new RecordingTriggerRegistrationProvider(registration),
            runStarter: new RecordingRunStarter(operations));

        TriggerDispatchResult result = await dispatcher.DispatchAsync(CreateInboundEvent() with { SecretHash = WebhookSecretHash.Of("wrong") }, CancellationToken.None);

        Assert.Equal(TriggerDispatchOutcome.SecretMismatch, result.Outcome);
        Assert.Empty(operations);
    }

    [Fact]
    public async Task Dispatch_rejects_a_secret_protected_webhook_when_none_is_presented()
    {
        // The upgrade hazard: adding a secret to a live trigger must not silently keep letting the old,
        // unauthenticated callers through.
        var registration = CreateRegistration(WorkflowConcurrencyPolicy.AllowParallel) with { WebhookSecret = "expected" };
        var dispatcher = CreateDispatcher(triggerRegistrationProvider: new RecordingTriggerRegistrationProvider(registration));

        TriggerDispatchResult result = await dispatcher.DispatchAsync(CreateInboundEvent(), CancellationToken.None);

        Assert.Equal(TriggerDispatchOutcome.SecretMismatch, result.Outcome);
    }

    [Fact]
    public async Task Dispatch_accepts_a_webhook_with_the_matching_secret()
    {
        var registration = CreateRegistration(WorkflowConcurrencyPolicy.AllowParallel) with { WebhookSecret = "expected" };
        var dispatcher = CreateDispatcher(triggerRegistrationProvider: new RecordingTriggerRegistrationProvider(registration));

        TriggerDispatchResult result = await dispatcher.DispatchAsync(CreateInboundEvent() with { SecretHash = WebhookSecretHash.Of("expected") }, CancellationToken.None);

        Assert.Equal(TriggerDispatchOutcome.StartedRun, result.Outcome);
    }

    [Fact]
    public async Task Dispatch_leaves_a_registration_without_a_secret_open()
    {
        // Every webhook published before secrets existed has a null one and must keep working.
        var dispatcher = CreateDispatcher(triggerRegistrationProvider: new RecordingTriggerRegistrationProvider(CreateRegistration(WorkflowConcurrencyPolicy.AllowParallel)));

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
        var idempotency = new InMemoryIdempotencyKeyProvider();
        var dispatcher = new TriggerDispatcher(
            new CorrelationKeyResolver(TriggerChannelRegistry.Default),
            new RecordingBookmarkResumer(new BookmarkMatchResult(false, null, false, "inbound-event:evt-1")),
            new RecordingTriggerRegistrationProvider(CreateRegistration(WorkflowConcurrencyPolicy.AllowParallel)),
            UncachedTriggerRegistrationCache.Instance,
            new ThrowingTriggerConcurrencyEnforcer(),
            new RecordingRunCancellationService(),
            new RecordingRunStarter([]),
            new RecordingRunDispatcher([]),
            idempotency,
            new ExpressionEvaluator(new DispatcherTestClock()));

        await Assert.ThrowsAsync<InvalidOperationException>(() => dispatcher.DispatchAsync(CreateInboundEvent(), CancellationToken.None));

        Assert.Equal(["trigger-start:5:" + EventHash("evt-1"), "inbound-event:evt-1"], idempotency.Failed);
    }

    [Fact]
    public async Task A_retried_event_starts_only_the_registration_that_failed_before_its_run_was_created()
    {
        // #293: one webhook, two workflows; the second fails once, and the retry must not start the
        // first again.
        var operations = new List<string>();
        var runStarter = new RecordingRunStarter(operations);
        var runDispatcher = new RecordingRunDispatcher(operations);
        var dispatcher = CreateDispatcher(
            bookmarkResumer: new RecordingBookmarkResumer(new BookmarkMatchResult(false, null, false, "inbound-event:evt-1")),
            triggerRegistrationProvider: TwoRegistrations(),
            concurrencyEnforcer: new FlakyConcurrencyEnforcer(failOnceFor: 6),
            runStarter: runStarter,
            runDispatcher: runDispatcher);

        await Assert.ThrowsAsync<InvalidOperationException>(() => dispatcher.DispatchAsync(CreateInboundEvent(), CancellationToken.None));
        TriggerDispatchResult retry = await dispatcher.DispatchAsync(CreateInboundEvent(), CancellationToken.None);

        Assert.Equal(2, runStarter.RunsCreated);
        Assert.Equal([(501L, 801L, BranchExecutionReason.TriggerStarted), (502L, 802L, BranchExecutionReason.TriggerStarted)], runDispatcher.Requests);
        // The retry still reports the run the first attempt started, not a blank for that registration.
        Assert.Equal([501L, 502L], retry.StartedRunIds);
    }

    [Fact]
    public async Task A_retry_recovers_a_run_created_before_its_registration_claim_was_settled()
    {
        // The window #293's review comment points at: the run is durably created, then the attempt dies
        // before the registration's claim is marked done. The retry must hand that run to the pump once,
        // start no second run, and not put it back through the concurrency policy (which would count it
        // as the active run to drop, queue behind or cancel).
        var operations = new List<string>();
        var runStarter = new RecordingRunStarter(operations);
        var runDispatcher = new RecordingRunDispatcher(operations);
        var enforcer = new FlakyConcurrencyEnforcer(failOnceFor: null);
        var idempotency = new InMemoryIdempotencyKeyProvider();
        idempotency.FailNextMarkSucceeded.Add("trigger-start:6:" + EventHash("evt-1"));
        var dispatcher = CreateDispatcher(
            bookmarkResumer: new RecordingBookmarkResumer(new BookmarkMatchResult(false, null, false, "inbound-event:evt-1")),
            triggerRegistrationProvider: TwoRegistrations(),
            concurrencyEnforcer: enforcer,
            runStarter: runStarter,
            runDispatcher: runDispatcher,
            idempotency: idempotency);

        await Assert.ThrowsAsync<InvalidOperationException>(() => dispatcher.DispatchAsync(CreateInboundEvent(), CancellationToken.None));
        Assert.Equal([(501L, 801L, BranchExecutionReason.TriggerStarted)], runDispatcher.Requests);

        TriggerDispatchResult retry = await dispatcher.DispatchAsync(CreateInboundEvent(), CancellationToken.None);

        Assert.Equal(2, runStarter.RunsCreated);
        Assert.Equal([(501L, 801L, BranchExecutionReason.TriggerStarted), (502L, 802L, BranchExecutionReason.TriggerStarted)], runDispatcher.Requests);
        Assert.Equal([501L, 502L], retry.StartedRunIds);
        Assert.Equal(1, enforcer.Evaluations[6]);
        Assert.Equal(IdempotencyKeyStatus.Succeeded, idempotency.Find("trigger-start:6:" + EventHash("evt-1"))!.Status);
    }

    [Fact]
    public async Task A_concurrent_redelivery_does_not_start_a_registration_another_attempt_is_starting()
    {
        var operations = new List<string>();
        var holdFirstStart = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var firstStartEntered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var runStarter = new RecordingRunStarter(operations)
        {
            DuringStart = () =>
            {
                firstStartEntered.TrySetResult();
                return holdFirstStart.Task;
            }
        };
        var runDispatcher = new RecordingRunDispatcher(operations);
        var dispatcher = CreateDispatcher(
            triggerRegistrationProvider: new RecordingTriggerRegistrationProvider(CreateRegistration(WorkflowConcurrencyPolicy.AllowParallel)),
            runStarter: runStarter,
            runDispatcher: runDispatcher);

        Task<TriggerDispatchResult> first = dispatcher.DispatchAsync(CreateInboundEvent(), CancellationToken.None);
        await firstStartEntered.Task;
        TriggerDispatchResult second = await dispatcher.DispatchAsync(CreateInboundEvent(), CancellationToken.None);
        holdFirstStart.SetResult();
        TriggerDispatchResult firstResult = await first;

        Assert.Equal(TriggerDispatchOutcome.Idempotent, Assert.Single(second.Registrations).Outcome);
        Assert.Equal(TriggerDispatchOutcome.StartedRun, firstResult.Outcome);
        Assert.Equal(1, runStarter.RunsCreated);
        Assert.Single(runDispatcher.Requests);
    }

    [Fact]
    public async Task An_event_redelivered_after_its_claims_were_swept_does_not_start_or_rerun_its_run()
    {
        // Claims are swept after the retention window; runs are not. The run's own trigger start key
        // still stops a second one, and the run found under it is not handed to the pump again.
        var operations = new List<string>();
        var runStarter = new RecordingRunStarter(operations);
        var runDispatcher = new RecordingRunDispatcher(operations);
        var registrations = new RecordingTriggerRegistrationProvider(CreateRegistration(WorkflowConcurrencyPolicy.AllowParallel));

        await CreateDispatcher(triggerRegistrationProvider: registrations, runStarter: runStarter, runDispatcher: runDispatcher)
            .DispatchAsync(CreateInboundEvent(), CancellationToken.None);
        TriggerDispatchResult late = await CreateDispatcher(triggerRegistrationProvider: registrations, runStarter: runStarter, runDispatcher: runDispatcher, idempotency: new InMemoryIdempotencyKeyProvider())
            .DispatchAsync(CreateInboundEvent(), CancellationToken.None);

        Assert.Equal(1, runStarter.RunsCreated);
        Assert.Single(runDispatcher.Requests);
        Assert.Equal(501L, late.RunId);
    }

    [Fact]
    public async Task A_replayed_queued_event_reaches_only_the_trigger_that_queued_it()
    {
        // #316: the event started workflow 6 when it arrived and queued for workflow 5. When 5's slot
        // frees up the replay must start 5 and leave 6 alone.
        var operations = new List<string>();
        var runStarter = new RecordingRunStarter(operations);
        var dispatcher = CreateDispatcher(triggerRegistrationProvider: TwoRegistrations(), runStarter: runStarter);
        InboundEvent replay = CreateInboundEvent() with
        {
            InboundEventId = "evt-1:drain:9",
            ReplayFor = new QueuedTrigger(Guid.Parse("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa"), Guid.Parse("bbbbbbbb-bbbb-bbbb-bbbb-bbbbbbbbbbbb"))
        };

        TriggerDispatchResult result = await dispatcher.DispatchAsync(replay, CancellationToken.None);

        Assert.Equal(1, runStarter.RunsCreated);
        Assert.Equal(5L, Assert.Single(result.Registrations).RegistrationId);
    }

    [Fact]
    public async Task A_refusal_at_the_ceiling_is_reported_so_the_user_hears_of_it()
    {
        var registration = CreateRegistration(WorkflowConcurrencyPolicy.AllowParallel);
        var reporter = new RecordingRefusalReporter();
        var dispatcher = CreateDispatcher(
            triggerRegistrationProvider: new RecordingTriggerRegistrationProvider(registration),
            runStarter: new RecordingRunStarter([]) { AtCeiling = true },
            refusalReporter: reporter);
        InboundEvent evt = CreateInboundEvent();

        await dispatcher.DispatchAsync(evt, CancellationToken.None);

        RunRefusal refusal = Assert.Single(reporter.Refusals);
        Assert.Equal(new RunRefusal(1, registration.WorkflowDefinitionId, registration.WorkflowRefId, "client", 3, evt.ReceivedAt), refusal);
    }

    [Fact]
    public async Task A_start_that_goes_through_reports_nothing()
    {
        var reporter = new RecordingRefusalReporter();
        var dispatcher = CreateDispatcher(
            triggerRegistrationProvider: new RecordingTriggerRegistrationProvider(CreateRegistration(WorkflowConcurrencyPolicy.AllowParallel)),
            refusalReporter: reporter);

        await dispatcher.DispatchAsync(CreateInboundEvent(), CancellationToken.None);

        Assert.Empty(reporter.Refusals);
    }

    private sealed class RecordingRefusalReporter : IRunRefusalReporter
    {
        public List<RunRefusal> Refusals { get; } = [];

        public void Report(RunRefusal refusal) => Refusals.Add(refusal);
    }

    [Fact]
    public async Task Dispatch_returns_Refused_when_the_workspace_is_at_its_run_ceiling()
    {
        var operations = new List<string>();
        var runDispatcher = new RecordingRunDispatcher(operations);
        var dispatcher = CreateDispatcher(
            triggerRegistrationProvider: new RecordingTriggerRegistrationProvider(CreateRegistration(WorkflowConcurrencyPolicy.AllowParallel)),
            runStarter: new RecordingRunStarter(operations) { AtCeiling = true },
            runDispatcher: runDispatcher);

        TriggerDispatchResult result = await dispatcher.DispatchAsync(CreateInboundEvent(), CancellationToken.None);

        Assert.Equal(TriggerDispatchOutcome.Refused, result.Outcome);
        Assert.Null(result.RunId);
        Assert.Empty(runDispatcher.Requests);
    }

    [Fact]
    public async Task A_refused_event_redelivered_once_the_workspace_has_room_does_not_start_late()
    {
        // The refusal settles the event's claim, so a broker redelivery gets the same answer rather than
        // starting a run for an event the sender was already told was dropped.
        var operations = new List<string>();
        var runStarter = new RecordingRunStarter(operations) { AtCeiling = true };
        var dispatcher = CreateDispatcher(
            triggerRegistrationProvider: new RecordingTriggerRegistrationProvider(CreateRegistration(WorkflowConcurrencyPolicy.AllowParallel)),
            runStarter: runStarter);

        await dispatcher.DispatchAsync(CreateInboundEvent(), CancellationToken.None);
        runStarter.AtCeiling = false;
        TriggerDispatchResult redelivered = await dispatcher.DispatchAsync(CreateInboundEvent(), CancellationToken.None);

        Assert.Equal(0, runStarter.RunsCreated);
        Assert.Equal(TriggerDispatchOutcome.Refused, redelivered.Outcome);
    }

    [Fact]
    public async Task One_refused_registration_does_not_stop_another_from_starting()
    {
        // Two workflows' triggers can sit in different workspaces; only the full one is refused.
        var operations = new List<string>();
        var dispatcher = CreateDispatcher(
            triggerRegistrationProvider: TwoRegistrations(),
            runStarter: new RecordingRunStarter(operations) { RefuseFirst = true });

        TriggerDispatchResult result = await dispatcher.DispatchAsync(CreateInboundEvent(), CancellationToken.None);

        Assert.Equal(TriggerDispatchOutcome.StartedRun, result.Outcome);
        Assert.Contains(result.Registrations, r => r.Outcome == TriggerDispatchOutcome.Refused);
    }

    [Fact]
    public async Task A_sub_workflow_child_starts_past_the_workspace_run_ceiling()
    {
        // The parent already holds a place under the ceiling; refusing its child would fail the parent.
        var operations = new List<string>();
        var runStarter = new RecordingRunStarter(operations) { AtCeiling = true };
        var dispatcher = CreateDispatcher(
            triggerRegistrationProvider: new RecordingTriggerRegistrationProvider(CreateRegistration(WorkflowConcurrencyPolicy.AllowParallel)),
            runStarter: runStarter);

        TriggerDispatchResult result = await dispatcher.DispatchAsync(CreateInboundEvent() with { StartsChildRun = true }, CancellationToken.None);

        Assert.Equal(TriggerDispatchOutcome.StartedRun, result.Outcome);
        Assert.Equal([true], runStarter.ExemptFromCeiling);
    }

    [Fact]
    public async Task A_replayed_queued_event_starts_past_the_workspace_run_ceiling()
    {
        // The drainer has already taken the event off the queue, so a refusal here would lose it.
        var operations = new List<string>();
        var runStarter = new RecordingRunStarter(operations) { AtCeiling = true };
        var dispatcher = CreateDispatcher(
            triggerRegistrationProvider: new RecordingTriggerRegistrationProvider(CreateRegistration(WorkflowConcurrencyPolicy.AllowParallel)),
            runStarter: runStarter);
        InboundEvent replay = CreateInboundEvent() with
        {
            InboundEventId = "evt-1:drain:9",
            ReplayFor = new QueuedTrigger(Guid.Parse("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa"), Guid.Parse("bbbbbbbb-bbbb-bbbb-bbbb-bbbbbbbbbbbb"))
        };

        TriggerDispatchResult result = await dispatcher.DispatchAsync(replay, CancellationToken.None);

        Assert.Equal(TriggerDispatchOutcome.StartedRun, result.Outcome);
        Assert.Equal([true], runStarter.ExemptFromCeiling);
    }

    [Fact]
    public async Task A_start_that_replaces_the_runs_it_cancels_starts_past_the_workspace_run_ceiling()
    {
        // CancelExisting has already asked the old run to stop; refusing its replacement would leave
        // nothing running for the event.
        var operations = new List<string>();
        var runStarter = new RecordingRunStarter(operations) { AtCeiling = true };
        var cancellationService = new RecordingRunCancellationService();
        var dispatcher = CreateDispatcher(
            triggerRegistrationProvider: new RecordingTriggerRegistrationProvider(CreateRegistration(WorkflowConcurrencyPolicy.CancelExisting)),
            concurrencyEnforcer: new RecordingTriggerConcurrencyEnforcer(new TriggerConcurrencyDecision(TriggerConcurrencyOutcome.ProceedAfterCancellingActive, [55L])),
            runCancellationService: cancellationService,
            runStarter: runStarter);

        TriggerDispatchResult result = await dispatcher.DispatchAsync(CreateInboundEvent(), CancellationToken.None);

        Assert.Equal(TriggerDispatchOutcome.StartedRun, result.Outcome);
        Assert.Equal([(55L, "Trigger-cancel-policy")], cancellationService.Requests);
        Assert.Equal([true], runStarter.ExemptFromCeiling);
    }

    [Fact]
    public async Task A_CancelExisting_start_with_nothing_to_cancel_is_held_to_the_workspace_run_ceiling()
    {
        // Nothing gives up a place for it, so it is an ordinary new run.
        var operations = new List<string>();
        var runStarter = new RecordingRunStarter(operations) { AtCeiling = true };
        var dispatcher = CreateDispatcher(
            triggerRegistrationProvider: new RecordingTriggerRegistrationProvider(CreateRegistration(WorkflowConcurrencyPolicy.CancelExisting)),
            concurrencyEnforcer: new RecordingTriggerConcurrencyEnforcer(new TriggerConcurrencyDecision(TriggerConcurrencyOutcome.ProceedAfterCancellingActive, [])),
            runStarter: runStarter);

        TriggerDispatchResult result = await dispatcher.DispatchAsync(CreateInboundEvent(), CancellationToken.None);

        Assert.Equal(TriggerDispatchOutcome.Refused, result.Outcome);
        Assert.Equal([false], runStarter.ExemptFromCeiling);
    }

    private static RecordingTriggerRegistrationProvider TwoRegistrations() => new(
        CreateRegistration(WorkflowConcurrencyPolicy.AllowParallel, id: 5),
        CreateRegistration(WorkflowConcurrencyPolicy.AllowParallel, id: 6, workflowRefId: Guid.Parse("cccccccc-cccc-cccc-cccc-cccccccccccc")));

    /// <summary>Lets every registration proceed, except that it throws the first time it sees one.</summary>
    [Fact]
    public async Task Simultaneous_events_with_one_key_start_one_run_under_DropIfRunning()
    {
        // #467: the first event reads "nothing running", then the second starts a run before the first
        // gets to Run_Start. The first must lose at the start's guard and decide again, not start a second.
        var runStarter = new RecordingRunStarter([]) { EnforceGuard = true };
        var enforcer = new CheckThenActEnforcer(runStarter);
        var dispatcher = CreateDispatcher(
            triggerRegistrationProvider: new RecordingTriggerRegistrationProvider(CreateRegistration(WorkflowConcurrencyPolicy.DropIfRunning)),
            concurrencyEnforcer: enforcer,
            runStarter: runStarter);

        Task<TriggerDispatchResult> first = dispatcher.DispatchAsync(CreateInboundEvent(), CancellationToken.None);
        await enforcer.FirstReading.Task.WaitAsync(TimeSpan.FromSeconds(5));
        TriggerDispatchResult second = await dispatcher.DispatchAsync(CreateInboundEvent() with { InboundEventId = "evt-2" }, CancellationToken.None);
        enforcer.ReleaseFirst.SetResult();
        TriggerDispatchResult firstResult = await first;

        Assert.Equal(1, runStarter.RunsCreated);
        Assert.Equal(TriggerDispatchOutcome.StartedRun, second.Outcome);
        Assert.Equal(TriggerDispatchOutcome.Dropped, firstResult.Outcome);
    }

    [Theory]
    [InlineData(WorkflowConcurrencyPolicy.AllowParallel, ActiveRunGuard.None)]
    [InlineData(WorkflowConcurrencyPolicy.DropIfRunning, ActiveRunGuard.AnyActive)]
    [InlineData(WorkflowConcurrencyPolicy.Queue, ActiveRunGuard.AnyActive)]
    [InlineData(WorkflowConcurrencyPolicy.CancelExisting, ActiveRunGuard.NotCancelling)]
    public async Task Dispatch_asks_the_start_to_guard_against_active_runs_by_policy(WorkflowConcurrencyPolicy policy, ActiveRunGuard expected)
    {
        var runStarter = new RecordingRunStarter([]);
        var dispatcher = CreateDispatcher(
            triggerRegistrationProvider: new RecordingTriggerRegistrationProvider(CreateRegistration(policy)),
            runStarter: runStarter);

        await dispatcher.DispatchAsync(CreateInboundEvent(), CancellationToken.None);

        Assert.Equal([expected], runStarter.Guards);
    }

    [Fact]
    public async Task Dispatch_gives_up_after_losing_the_start_race_three_times()
    {
        var runStarter = new RecordingRunStarter([]) { AlwaysConflict = true };
        var idempotency = new InMemoryIdempotencyKeyProvider();
        var dispatcher = CreateDispatcher(
            triggerRegistrationProvider: new RecordingTriggerRegistrationProvider(CreateRegistration(WorkflowConcurrencyPolicy.DropIfRunning)),
            runStarter: runStarter,
            idempotency: idempotency);

        await Assert.ThrowsAsync<ActiveRunConflictException>(() => dispatcher.DispatchAsync(CreateInboundEvent(), CancellationToken.None));

        Assert.Equal(3, runStarter.Guards.Count);
        Assert.Contains("trigger-start:5:" + EventHash("evt-1"), idempotency.Failed);
    }

    /// <summary>
    /// Drops when the starter has started anything, as the real enforcer does when it finds an active run.
    /// Holds the first evaluation after its read and before its answer, the window #467 raced in.
    /// </summary>
    private sealed class CheckThenActEnforcer(RecordingRunStarter runStarter) : ITriggerConcurrencyEnforcer
    {
        private int _evaluations;

        public TaskCompletionSource FirstReading { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public TaskCompletionSource ReleaseFirst { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public async Task<TriggerConcurrencyDecision> EvaluateAsync(TriggerRegistrationRow registration, InboundEvent evt, CancellationToken ct)
        {
            bool active = runStarter.RunsCreated > 0;
            if (Interlocked.Increment(ref _evaluations) == 1)
            {
                FirstReading.SetResult();
                await ReleaseFirst.Task;
            }

            return new TriggerConcurrencyDecision(active ? TriggerConcurrencyOutcome.Dropped : TriggerConcurrencyOutcome.Proceed, TriggerConcurrencyDecision.NoRuns);
        }
    }

    private sealed class FlakyConcurrencyEnforcer(long? failOnceFor) : ITriggerConcurrencyEnforcer
    {
        private bool _failed;

        public Dictionary<long, int> Evaluations { get; } = [];

        public Task<TriggerConcurrencyDecision> EvaluateAsync(TriggerRegistrationRow registration, InboundEvent evt, CancellationToken ct)
        {
            Evaluations[registration.Id] = Evaluations.GetValueOrDefault(registration.Id) + 1;
            if (registration.Id == failOnceFor && !_failed)
            {
                _failed = true;
                throw new InvalidOperationException("database unavailable");
            }

            return Task.FromResult(new TriggerConcurrencyDecision(TriggerConcurrencyOutcome.Proceed, []));
        }
    }

    private static TriggerDispatcher CreateDispatcher(
        RecordingBookmarkResumer? bookmarkResumer = null,
        RecordingTriggerRegistrationProvider? triggerRegistrationProvider = null,
        ITriggerConcurrencyEnforcer? concurrencyEnforcer = null,
        RecordingRunCancellationService? runCancellationService = null,
        RecordingRunStarter? runStarter = null,
        RecordingRunDispatcher? runDispatcher = null,
        InMemoryIdempotencyKeyProvider? idempotency = null,
        Microsoft.Extensions.Logging.ILogger<TriggerDispatcher>? logger = null,
        IRunRefusalReporter? refusalReporter = null)
    {
        return new TriggerDispatcher(
            new CorrelationKeyResolver(TriggerChannelRegistry.Default),
            bookmarkResumer ?? new RecordingBookmarkResumer(new BookmarkMatchResult(false, null, false)),
            triggerRegistrationProvider ?? new RecordingTriggerRegistrationProvider(),
            UncachedTriggerRegistrationCache.Instance,
            concurrencyEnforcer ?? new RecordingTriggerConcurrencyEnforcer(new TriggerConcurrencyDecision(TriggerConcurrencyOutcome.Proceed, [])),
            runCancellationService ?? new RecordingRunCancellationService(),
            runStarter ?? new RecordingRunStarter([]),
            runDispatcher ?? new RecordingRunDispatcher([]),
            idempotency ?? new InMemoryIdempotencyKeyProvider(),
            new ExpressionEvaluator(new DispatcherTestClock()),
            logger,
            refusalReporter);
    }

    private sealed class LevelRecorder : Microsoft.Extensions.Logging.ILogger<TriggerDispatcher>
    {
        public List<Microsoft.Extensions.Logging.LogLevel> Levels { get; } = [];

        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;

        public bool IsEnabled(Microsoft.Extensions.Logging.LogLevel logLevel) => true;

        public void Log<TState>(Microsoft.Extensions.Logging.LogLevel logLevel, Microsoft.Extensions.Logging.EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
        {
            Levels.Add(logLevel);
        }
    }

    private sealed class DispatcherTestClock : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => UtcNow;

        public DateTime UtcNow => new(2026, 8, 7, 12, 0, 0, DateTimeKind.Utc);
    }

    private static string EventHash(string eventId) =>
        Convert.ToHexStringLower(System.Security.Cryptography.SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(eventId)));

    /// <summary>The claim semantics of the IdempotencyKey_* procedures, with failures to inject.</summary>
    private sealed class InMemoryIdempotencyKeyProvider : IIdempotencyKeyProvider
    {
        private readonly Lock _gate = new();
        private readonly Dictionary<string, IdempotencyKeyRow> _rows = [];

        public List<string> Failed { get; } = [];

        /// <summary>Keys whose next MarkSucceeded throws, as if the attempt died just before settling.</summary>
        public HashSet<string> FailNextMarkSucceeded { get; } = [];

        public IdempotencyKeyRow? Find(string keyValue)
        {
            lock (_gate)
            {
                return _rows.GetValueOrDefault(keyValue);
            }
        }

        public void HoldPending(string keyValue)
        {
            lock (_gate)
            {
                _rows[keyValue] = NewRow(keyValue, Guid.NewGuid());
            }
        }

        public Task<IdempotencyKeyRow> UpsertPendingAsync(string keyValue, int runId, Guid branchRefId, Guid nodeId, int attempt, CancellationToken ct)
        {
            lock (_gate)
            {
                if (!_rows.TryGetValue(keyValue, out IdempotencyKeyRow? row))
                {
                    row = NewRow(keyValue, branchRefId);
                    _rows[keyValue] = row;
                }

                return Task.FromResult(row);
            }
        }

        public Task<IdempotencyKeyRow> GetByKeyAsync(string keyValue, CancellationToken ct) => throw new NotImplementedException();

        public Task<IdempotencyKeyRow> MarkSucceededAsync(string keyValue, string resultJson, CancellationToken ct)
        {
            lock (_gate)
            {
                if (FailNextMarkSucceeded.Remove(keyValue))
                {
                    throw new InvalidOperationException("connection lost");
                }

                return Task.FromResult(Settle(keyValue, IdempotencyKeyStatus.Succeeded, resultJson));
            }
        }

        public Task<IdempotencyKeyRow> MarkFailedAsync(string keyValue, string errorJson, CancellationToken ct)
        {
            lock (_gate)
            {
                Failed.Add(keyValue);
                return Task.FromResult(Settle(keyValue, IdempotencyKeyStatus.Failed, null));
            }
        }

        public Task<IdempotencyKeyRow> ReclaimFailedAsync(string keyValue, Guid newBranchRefId, CancellationToken ct)
        {
            lock (_gate)
            {
                IdempotencyKeyRow row = _rows[keyValue];
                if (row.Status == IdempotencyKeyStatus.Failed)
                {
                    row = row with { Status = IdempotencyKeyStatus.Pending, BranchRefId = newBranchRefId };
                    _rows[keyValue] = row;
                }

                return Task.FromResult(row);
            }
        }

        public Task<int> DeleteExpiredAsync(DateTime cutoffUtc, int batchSize, CancellationToken ct) => throw new NotImplementedException();

        private IdempotencyKeyRow Settle(string keyValue, string status, string? resultJson)
        {
            // The event's own claim is taken by the bookmark resumer, which these tests stand in for.
            IdempotencyKeyRow row = _rows.GetValueOrDefault(keyValue) ?? NewRow(keyValue, Guid.Empty);
            row = row with { Status = status, ResultJson = resultJson ?? row.ResultJson };
            _rows[keyValue] = row;
            return row;
        }

        private static IdempotencyKeyRow NewRow(string keyValue, Guid token) => new()
        {
            Id = 0,
            KeyValue = keyValue,
            RunId = 0,
            BranchRefId = token,
            NodeId = Guid.Empty,
            Attempt = 0,
            Status = IdempotencyKeyStatus.Pending,
            ResultJson = null,
            ErrorJson = null,
            CreatedAt = DateTime.UtcNow,
            CompletedAt = null
        };
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

    private static TriggerRegistrationRow CreateRegistration(WorkflowConcurrencyPolicy policy = WorkflowConcurrencyPolicy.Queue, int id = 5, Guid? workflowRefId = null)
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
            ConcurrencyPolicy = TriggerRegistrationRow.FormatConcurrencyPolicy(policy),
            FilterExpression = null,
            CreatedAt = DateTime.UtcNow
        };
    }

    private sealed class RecordingBookmarkResumer(BookmarkMatchResult result) : IBookmarkResumer
    {
        public List<InboundEvent> Events { get; } = [];

        public Task<BookmarkMatchResult> MatchInboundAsync(InboundEvent evt, bool hasRegistrations, CancellationToken ct)
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

        public Task<bool> FailRunAsync(long runId, string reason, CancellationToken ct) => throw new NotSupportedException();

        public Task<bool> IsCancellationRequestedAsync(long runId, CancellationToken ct)
        {
            _ = runId;
            _ = ct;
            return Task.FromResult(false);
        }
    }

    private static Guid RunRefFor(long runId) => new($"00000000-0000-0000-0000-{runId:D12}");

    /// <summary>Starts runs the way Run_Start does: at most one per trigger start key.</summary>
    private sealed class RecordingRunStarter(List<string> operations) : IRunStarter
    {
        private readonly Lock _gate = new();
        private readonly Dictionary<string, RunStartResult> _byKey = [];
        private readonly HashSet<string> _activeCorrelationKeys = [];
        private int _started;

        /// <summary>Awaited inside every start, so a test can hold an attempt mid-start.</summary>
        public Func<Task>? DuringStart { get; init; }

        public int RunsCreated => _started;

        /// <summary>While set, every start is refused as though the workspace were at its run ceiling.</summary>
        public bool AtCeiling { get; set; }

        /// <summary>Refuses only the first start it sees, as for a trigger in the one full workspace.</summary>
        public bool RefuseFirst { get; set; }

        /// <summary>What each start was told about the ceiling, in order.</summary>
        public List<bool> ExemptFromCeiling { get; } = [];

        /// <summary>The active-run guard each start was given, in order.</summary>
        public List<ActiveRunGuard> Guards { get; } = [];

        /// <summary>
        /// Refuses a guarded start while a run it created holds the same correlation key, as Run_Start does.
        /// Runs never finish here, so a key once taken stays taken.
        /// </summary>
        public bool EnforceGuard { get; init; }

        /// <summary>Refuses every guarded start, as for a key that never stops losing the race.</summary>
        public bool AlwaysConflict { get; init; }

        public async Task<RunStartResult> StartAsync(int workflowDefinitionId, string triggerNodeId, InboundEvent triggerEvent, string? triggerStartKey, CancellationToken ct, bool exemptFromCeiling = false, ActiveRunGuard activeRunGuard = ActiveRunGuard.None)
        {
            lock (_gate)
            {
                operations.Add("start");
                ExemptFromCeiling.Add(exemptFromCeiling);
                Guards.Add(activeRunGuard);
                if ((AtCeiling || RefuseFirst) && !exemptFromCeiling)
                {
                    RefuseFirst = false;
                    throw new WorkspaceRunCeilingException(1, 3);
                }

                if (triggerStartKey != null && _byKey.TryGetValue(triggerStartKey, out RunStartResult? existing))
                {
                    return existing with { Created = false };
                }

                if (activeRunGuard != ActiveRunGuard.None
                    && (AlwaysConflict || (EnforceGuard && _activeCorrelationKeys.Contains(triggerEvent.CorrelationKey ?? string.Empty))))
                {
                    throw new ActiveRunConflictException(triggerEvent.CorrelationKey ?? string.Empty);
                }
            }

            if (DuringStart != null)
            {
                await DuringStart();
            }

            lock (_gate)
            {
                // Distinct ids per call so a fan-out over several registrations is distinguishable.
                int offset = _started++;
                _activeCorrelationKeys.Add(triggerEvent.CorrelationKey ?? string.Empty);
                var created = new RunStartResult(501L + offset, RunRefFor(501L + offset), 801L + offset, true);
                if (triggerStartKey != null)
                {
                    _byKey[triggerStartKey] = created;
                }

                return created;
            }
        }

        public Task<RunStartResult?> FindStartedAsync(string triggerStartKey, CancellationToken ct)
        {
            lock (_gate)
            {
                operations.Add("find");
                return Task.FromResult(_byKey.TryGetValue(triggerStartKey, out RunStartResult? existing) ? existing with { Created = false } : null);
            }
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
