using Microsoft.AspNetCore.Mvc;
using Wbskt.Workflow.Abstraction.Runtime;
using Wbskt.Workflow.Engine.Contracts;
using Wbskt.Workflow.Engine.Host.Middleware;

namespace Wbskt.Workflow.Engine.Host.Controllers;

/// <summary>Starts a test run for the management host, which has written the test copy it names.</summary>
[ApiController]
[Route(EngineTestRunApi.Route)]
[InboundEndpoint]
public sealed class InboundTestRunController(ITestRunStarter starter) : ControllerBase
{
    [HttpPost]
    public async Task<EngineTestRunResponse> Post([FromBody] EngineTestRunRequest request, CancellationToken ct)
    {
        TestRunStart started = await starter.StartAsync(request.WorkflowDefinitionId, request.TriggerNodeId, request.Payload, ct);
        EngineTestRunOutcome outcome = started.Outcome switch
        {
            TestRunStartOutcome.Started => EngineTestRunOutcome.Started,
            TestRunStartOutcome.TestRunActive => EngineTestRunOutcome.TestRunActive,
            TestRunStartOutcome.WorkspaceFull => EngineTestRunOutcome.WorkspaceFull,
            TestRunStartOutcome.CreditsSpent => EngineTestRunOutcome.CreditsSpent,
            TestRunStartOutcome.WorkspaceCreditCapReached => EngineTestRunOutcome.WorkspaceCreditCapReached,
            TestRunStartOutcome.Busy => EngineTestRunOutcome.Busy,
            _ => throw new InvalidOperationException($"Unrecognised test run outcome '{started.Outcome}'.")
        };
        return new EngineTestRunResponse(outcome, started.RunRefId, started.ActiveRunRefId);
    }
}
