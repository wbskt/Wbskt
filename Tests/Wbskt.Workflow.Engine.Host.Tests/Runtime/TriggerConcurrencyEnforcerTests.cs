using System.Text.Json;
using Wbskt.Workflow.Abstraction.Entities;
using Wbskt.Workflow.Abstraction.Providers;
using Wbskt.Workflow.Abstraction.Runtime;
using Wbskt.Workflow.Runtime;

namespace Wbskt.Workflow.Engine.Host.Tests.Runtime;

public sealed class TriggerConcurrencyEnforcerTests
{
    [Fact]
    public async Task Evaluate_returns_Dropped_when_drop_policy_has_active_run()
    {
        // Arrange
        var registration = CreateRegistration("DropIfRunning");
        var enforcer = CreateEnforcer(activeRuns: [CreateRun(55)]);

        // Act
        TriggerConcurrencyDecision decision = await enforcer.EvaluateAsync(registration, CreateInboundEvent(), CancellationToken.None);

        // Assert
        Assert.Equal(TriggerConcurrencyOutcome.Dropped, decision.Outcome);
        Assert.Null(decision.RunIdToCancel);
    }

    [Fact]
    public async Task Evaluate_returns_Queued_and_enqueues_event_when_queue_policy_has_active_run()
    {
        // Arrange
        var registration = CreateRegistration("Queue");
        var pendingProvider = new RecordingPendingTriggerEventProvider();
        var enforcer = CreateEnforcer(activeRuns: [CreateRun(55)], pendingTriggerEventProvider: pendingProvider);

        // Act
        TriggerConcurrencyDecision decision = await enforcer.EvaluateAsync(registration, CreateInboundEvent(), CancellationToken.None);

        // Assert
        Assert.Equal(TriggerConcurrencyOutcome.Queued, decision.Outcome);
        Assert.Single(pendingProvider.EnqueueCalls);
        Assert.Contains("evt-1", pendingProvider.EnqueueCalls.Single().InboundEventJson, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Evaluate_returns_ProceedAfterCancellingActive_when_cancel_policy_has_active_run()
    {
        // Arrange
        var registration = CreateRegistration("CancelExisting");
        var enforcer = CreateEnforcer(activeRuns: [CreateRun(55)]);

        // Act
        TriggerConcurrencyDecision decision = await enforcer.EvaluateAsync(registration, CreateInboundEvent(), CancellationToken.None);

        // Assert
        Assert.Equal(TriggerConcurrencyOutcome.ProceedAfterCancellingActive, decision.Outcome);
        Assert.Equal(55L, decision.RunIdToCancel);
    }

    [Fact]
    public async Task Evaluate_returns_Proceed_when_no_active_run_exists()
    {
        // Arrange
        var registration = CreateRegistration("Queue");
        var enforcer = CreateEnforcer();

        // Act
        TriggerConcurrencyDecision decision = await enforcer.EvaluateAsync(registration, CreateInboundEvent(), CancellationToken.None);

        // Assert
        Assert.Equal(TriggerConcurrencyOutcome.Proceed, decision.Outcome);
        Assert.Null(decision.RunIdToCancel);
    }

    private static TriggerConcurrencyEnforcer CreateEnforcer(
        IReadOnlyCollection<RunRow>? activeRuns = null,
        RecordingPendingTriggerEventProvider? pendingTriggerEventProvider = null)
    {
        return new TriggerConcurrencyEnforcer(
            new RecordingRunProvider(activeRuns ?? Array.Empty<RunRow>()),
            pendingTriggerEventProvider ?? new RecordingPendingTriggerEventProvider());
    }

    private static TriggerRegistrationRow CreateRegistration(string policy)
    {
        return new TriggerRegistrationRow
        {
            Id = 7,
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

    private static RunRow CreateRun(long runId)
    {
        return new RunRow
        {
            Id = (int)runId,
            RefId = Guid.Parse("cccccccc-cccc-cccc-cccc-cccccccccccc"),
            WorkflowDefinitionId = 42,
            WorkflowRefId = Guid.Parse("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa"),
            WorkflowVersion = 3,
            TriggerNodeId = Guid.Parse("bbbbbbbb-bbbb-bbbb-bbbb-bbbbbbbbbbbb"),
            CorrelationKey = "device:serial-1:telemetry",
            Status = "Running",
            StartedAt = DateTime.UtcNow,
            CompletedAt = null,
            CancellationRequestedAt = null,
            CancellationReason = null,
            CreditBudget = 100m,
            CreatedAt = DateTime.UtcNow
        };
    }

    private static InboundEvent CreateInboundEvent()
    {
        return new InboundEvent(
            "device",
            "device:serial-1:telemetry",
            "evt-1",
            new Dictionary<string, JsonElement>
            {
                ["deviceSerial"] = JsonSerializer.SerializeToElement("serial-1"),
                ["payloadType"] = JsonSerializer.SerializeToElement("telemetry")
            },
            DateTime.UtcNow);
    }

    private sealed class RecordingRunProvider(IReadOnlyCollection<RunRow> activeRuns) : IRunProvider
    {
        public Task<RunRow> CreateAsync(RunRow row, CancellationToken ct) => throw new NotSupportedException();
        public Task<int?> FindByRefIdAsync(Guid refId, CancellationToken ct) => throw new NotSupportedException();
        public Task<RunRow> GetByIdAsync(long runId, CancellationToken ct) => throw new NotSupportedException();
        public Task<RunRow> GetByRefIdAsync(Guid refId, CancellationToken ct) => throw new NotSupportedException();
        public Task<IReadOnlyCollection<RunRow>> GetActiveByWorkflowRefIdCorrelationKeyAsync(Guid workflowRefId, string correlationKey, CancellationToken ct) => throw new NotSupportedException();
        public Task<IReadOnlyCollection<RunRow>> GetActiveByCorrelationAsync(int workflowDefinitionId, string correlationKey, CancellationToken ct) => Task.FromResult(activeRuns);
        public Task<RunRow> UpdateStatusAsync(Guid refId, string status, DateTime? completedAt, DateTime? cancellationRequestedAt, string? cancellationReason, CancellationToken ct) => throw new NotSupportedException();
        public Task<IReadOnlyCollection<RunRow>> GetStuckRunsAsync(DateTime cutoffUtc, int batchSize, CancellationToken ct) => throw new NotSupportedException();

        public Task<long> CountByStatusAsync(string status, CancellationToken ct) => throw new NotSupportedException();
        public Task<bool> TransitionStatusAsync(long runId, string fromStatus, string toStatus, CancellationToken ct) => throw new NotSupportedException();
        public Task<RunRow> SetTerminalAsync(long runId, string status, DateTime completedAt, CancellationToken ct) => throw new NotSupportedException();
    }

    private sealed class RecordingPendingTriggerEventProvider : IPendingTriggerEventProvider
    {
        public List<(Guid WorkflowRefId, Guid TriggerNodeId, string CorrelationKey, string InboundEventJson)> EnqueueCalls { get; } = [];

        public Task<PendingTriggerEventRow> EnqueueAsync(Guid workflowRefId, Guid triggerNodeId, string correlationKey, string inboundEventJson, CancellationToken ct)
        {
            EnqueueCalls.Add((workflowRefId, triggerNodeId, correlationKey, inboundEventJson));
            return Task.FromResult(new PendingTriggerEventRow
            {
                Id = 1,
                WorkflowRefId = workflowRefId,
                TriggerNodeId = triggerNodeId,
                CorrelationKey = correlationKey,
                InboundEventJson = inboundEventJson,
                EnqueuedAt = DateTime.UtcNow,
                CreatedAt = DateTime.UtcNow
            });
        }

        public Task<PendingTriggerEventRow?> DequeueNextAsync(Guid workflowRefId, Guid triggerNodeId, string correlationKey, CancellationToken ct) => throw new NotSupportedException();
        public Task<PendingTriggerEventRow?> DequeueNextAsync(int workflowDefinitionId, string correlationKey, CancellationToken ct) => throw new NotSupportedException();
        public Task DeleteAllByRunKeyAsync(Guid workflowRefId, Guid triggerNodeId, string correlationKey, CancellationToken ct) => throw new NotSupportedException();
        public Task<long> CountAllAsync(CancellationToken ct) => throw new NotSupportedException();
        public Task<int> DeleteExpiredAsync(DateTime cutoffUtc, int batchSize, CancellationToken ct) => throw new NotSupportedException();
    }
}


