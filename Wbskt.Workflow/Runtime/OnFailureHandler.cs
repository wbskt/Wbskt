using System.Text.Json;
using Wbskt.Workflow.Abstraction.Enums;
using Wbskt.Workflow.Abstraction.Models;
using Wbskt.Workflow.Abstraction.Models.Nodes;
using Wbskt.Workflow.Abstraction.Models.Nodes.Actions;
using Wbskt.Workflow.Abstraction.Runtime;

namespace Wbskt.Workflow.Runtime;

internal sealed class OnFailureHandler
{
    public NodeExecutionResult Apply(NodeExecutionResult.Fail fail, BaseNode node, BranchContext context)
    {
        _ = context;
        OnFailureConfig? config = (node as BaseActionNode)?.OnFailure;
        ErrorOutcome outcome = config?.Outcome ?? ErrorOutcome.FailBranch;
        IReadOnlyDictionary<string, JsonElement> patch = CreateLastErrorPatch(fail);

        return outcome switch
        {
            ErrorOutcome.ContinueOnError => new NodeExecutionResult.Continue("error", patch),
            ErrorOutcome.ContinueAsSucceeded => new NodeExecutionResult.Continue("default", patch),
            ErrorOutcome.JumpToNode when config?.TargetNodeId is Guid targetNodeId => NodeExecutionResult.JumpTo(targetNodeId, patch),
            _ => fail
        };
    }

    private static IReadOnlyDictionary<string, JsonElement> CreateLastErrorPatch(NodeExecutionResult.Fail fail)
    {
        return new Dictionary<string, JsonElement>
        {
            ["lastError"] = JsonSerializer.SerializeToElement(new { fail.ErrorCode, fail.Message }, new JsonSerializerOptions(JsonSerializerDefaults.Web))
        };
    }
}
