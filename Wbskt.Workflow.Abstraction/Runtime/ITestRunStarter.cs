using System.Text.Json;

namespace Wbskt.Workflow.Abstraction.Runtime;

/// <summary>Starts a test run from a test copy of a definition, and sets its first branch going.</summary>
public interface ITestRunStarter
{
    Task<TestRunStart> StartAsync(int workflowDefinitionId, Guid triggerNodeId, IReadOnlyDictionary<string, JsonElement> payload, CancellationToken ct);
}

public enum TestRunStartOutcome
{
    Started,
    TestRunActive,
    WorkspaceFull,
    CreditsSpent,
    WorkspaceCreditCapReached
}

public sealed record TestRunStart(TestRunStartOutcome Outcome, Guid? RunRefId = null, Guid? ActiveRunRefId = null);
