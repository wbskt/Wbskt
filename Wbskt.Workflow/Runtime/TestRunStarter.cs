using System.Text.Json;
using Microsoft.Extensions.Logging;
using Wbskt.Workflow.Abstraction.Providers;
using Wbskt.Workflow.Abstraction.Runtime;

namespace Wbskt.Workflow.Runtime;

/// <summary>
/// A test run goes through the same RunStarter and branch dispatch as a triggered run; only its way in
/// differs. There is no inbound event to match against registrations (a test copy has none), so the
/// payload is handed to the trigger node as the run's trigger data, and the run gets a correlation key
/// of its own so no concurrency policy or bookmark lookup ever confuses it with a live run.
/// </summary>
internal sealed class TestRunStarter : ITestRunStarter
{
    public const string ChannelKind = "test";

    private readonly IRunStarter _runStarter;
    private readonly IRunDispatcher _runDispatcher;
    private readonly IRunProvider _runProvider;
    private readonly TimeProvider _clock;
    private readonly ILogger<TestRunStarter>? _logger;

    public TestRunStarter(
        IRunStarter runStarter,
        IRunDispatcher runDispatcher,
        IRunProvider runProvider,
        TimeProvider clock,
        ILogger<TestRunStarter>? logger = null)
    {
        _runStarter = runStarter;
        _runDispatcher = runDispatcher;
        _runProvider = runProvider;
        _clock = clock;
        _logger = logger;
    }

    public async Task<TestRunStart> StartAsync(int workflowDefinitionId, Guid triggerNodeId, IReadOnlyDictionary<string, JsonElement> payload, CancellationToken ct)
    {
        Guid testId = Guid.NewGuid();
        var evt = new InboundEvent(ChannelKind, [], $"{ChannelKind}:{testId}", payload, _clock.GetUtcNow().UtcDateTime)
        {
            CorrelationKey = $"{ChannelKind}:{testId}"
        };

        RunStartResult started;
        try
        {
            started = await _runStarter.StartTestAsync(workflowDefinitionId, triggerNodeId.ToString(), evt, ct);
        }
        catch (TestRunActiveException ex)
        {
            return new TestRunStart(TestRunStartOutcome.TestRunActive, ActiveRunRefId: await _runProvider.FindActiveTestRunAsync(ex.WorkflowRefId, ct));
        }
        catch (PlanRunLimitException)
        {
            return new TestRunStart(TestRunStartOutcome.WorkspaceFull);
        }

        await _runDispatcher.DispatchAsync(new BranchExecutionRequest(started.RunId, started.BranchId, BranchExecutionReason.TriggerStarted), ct);
        _logger?.LogInformation("Test run {RunRefId} started from definition {WorkflowDefinitionId}", started.RunRefId, workflowDefinitionId);
        return new TestRunStart(TestRunStartOutcome.Started, started.RunRefId);
    }
}
