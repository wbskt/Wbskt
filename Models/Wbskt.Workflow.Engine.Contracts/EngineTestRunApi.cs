using System.Text.Json;

namespace Wbskt.Workflow.Engine.Contracts;

/// <summary>The engine's test-run endpoint, as the engine serves it and the management host calls it.</summary>
public static class EngineTestRunApi
{
    public const string Route = "api/inbound/test-runs";
}

/// <summary>
/// Starts a test run of <c>WorkflowDefinitionId</c>, a test copy the management host has just written,
/// from <c>TriggerNodeId</c>. <c>Payload</c> is what the run reads as <c>trigger</c>.
/// </summary>
public sealed record EngineTestRunRequest(int WorkflowDefinitionId, Guid TriggerNodeId, IReadOnlyDictionary<string, JsonElement> Payload);

public enum EngineTestRunOutcome
{
    Started,

    /// <summary>Another test run of the workflow is going; <c>ActiveRunRefId</c> names it when it could be read.</summary>
    TestRunActive,

    /// <summary>The workspace is at its limit of runs going at once.</summary>
    WorkspaceFull
}

public sealed record EngineTestRunResponse(EngineTestRunOutcome Outcome, Guid? RunRefId = null, Guid? ActiveRunRefId = null);
