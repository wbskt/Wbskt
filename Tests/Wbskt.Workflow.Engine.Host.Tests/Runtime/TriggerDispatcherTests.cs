using System.Text.Json;
using Wbskt.Workflow.Abstraction.Entities;
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
        Assert.Equal("device:serial-1:telemetry", bookmarkResumer.Events.Single().CorrelationKey);
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
        Assert.Equal("device:serial-1:telemetry", result.Reason);
    }

    [Fact]
    public async Task Dispatch_returns_Dropped_when_enforcer_returns_Dropped()
    {
        // Arrange
        var registration = CreateRegistration("DropIfRunning");
        var dispatcher = CreateDispatcher(
            triggerRegistrationProvider: new RecordingTriggerRegistrationProvider(registration),
            concurrencyEnforcer: new RecordingTriggerConcurrencyEnforcer(new TriggerConcurrencyDecision(TriggerConcurrencyOutcome.Dropped, null)));

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
            concurrencyEnforcer: new RecordingTriggerConcurrencyEnforcer(new TriggerConcurrencyDecision(TriggerConcurrencyOutcome.Queued, null)));

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
            concurrencyEnforcer: new RecordingTriggerConcurrencyEnforcer(new TriggerConcurrencyDecision(TriggerConcurrencyOutcome.ProceedAfterCancellingActive, 55)),
            runCancellationService: cancellationService);

        // Act
        TriggerDispatchResult result = await dispatcher.DispatchAsync(CreateInboundEvent(), CancellationToken.None);

        // Assert
        Assert.Equal(TriggerDispatchOutcome.StartedRun, result.Outcome);
        Assert.Equal([(55L, "Trigger-cancel-policy")], cancellationService.Requests);
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
            concurrencyEnforcer ?? new RecordingTriggerConcurrencyEnforcer(new TriggerConcurrencyDecision(TriggerConcurrencyOutcome.Proceed, null)),
            runCancellationService ?? new RecordingRunCancellationService(),
            runStarter ?? new RecordingRunStarter([]),
            runDispatcher ?? new RecordingRunDispatcher([]));
    }

    private static InboundEvent CreateInboundEvent()
    {
        return new InboundEvent(
            "device",
            string.Empty,
            "evt-1",
            new Dictionary<string, JsonElement>
            {
                ["deviceSerial"] = JsonSerializer.SerializeToElement("serial-1"),
                ["payloadType"] = JsonSerializer.SerializeToElement("telemetry")
            },
            DateTime.UtcNow);
    }

    private static TriggerRegistrationRow CreateRegistration(string policy = "Queue")
    {
        return new TriggerRegistrationRow
        {
            Id = 5,
            WorkflowDefinitionId = 42,
            WorkflowRefId = Guid.Parse("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa"),
            WorkflowVersion = 3,
            TriggerNodeId = Guid.Parse("bbbbbbbb-bbbb-bbbb-bbbb-bbbbbbbbbbbb"),
            TriggerKind = "device",
            TriggerKey = "device:serial-1:telemetry",
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

        public Task<IReadOnlyCollection<TriggerRegistrationRow>> GetActiveByChannelAsync(string channelKind, string channelKey, CancellationToken ct)
        {
            IReadOnlyCollection<TriggerRegistrationRow> matches = rows.Where(row => row.TriggerKind == channelKind && row.TriggerKey == channelKey).ToArray();
            return Task.FromResult(matches);
        }
    }

    private sealed class RecordingTriggerConcurrencyEnforcer(TriggerConcurrencyDecision decision) : ITriggerConcurrencyEnforcer
    {
        public Task<TriggerConcurrencyDecision> EvaluateAsync(TriggerRegistrationRow registration, InboundEvent evt, CancellationToken ct)
        {
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
        public Task<(long RunId, long BranchId)> StartAsync(int workflowDefinitionId, string triggerNodeId, InboundEvent triggerEvent, CancellationToken ct)
        {
            operations.Add("start");
            return Task.FromResult((501L, 801L));
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
