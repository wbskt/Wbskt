using System.Text.Json;
using Wbskt.Workflow.Abstraction.Models.Nodes;
using Wbskt.Workflow.Abstraction.Models.Nodes.Controls;
using Wbskt.Workflow.Abstraction.Providers;
using Wbskt.Workflow.Abstraction.Runtime;

namespace Wbskt.Workflow.NodeExecutors.Controls;

public sealed class JoinNodeExecutor : INodeExecutor
{
    private readonly IJoinAggregatorProvider _aggregators;

    public JoinNodeExecutor(IJoinAggregatorProvider aggregators)
    {
        _aggregators = aggregators;
    }

    public string Kind => NodeKind.ControlJoin;

    public async Task<NodeExecutionResult> ExecuteAsync(NodeContext ctx, CancellationToken ct)
    {
        if (ctx.Node is not JoinNode node || node.Config is null)
        {
            return new NodeExecutionResult.Fail("JOIN_CONFIG_INVALID", "Join node config is required.", false, null);
        }

        if (!ctx.Branch.LocalState.TryGetValue("__join_token", out JsonElement tokenElement)
            || tokenElement.ValueKind != JsonValueKind.String
            || !Guid.TryParse(tokenElement.GetString(), out Guid joinToken))
        {
            return new NodeExecutionResult.Fail("JOIN_NO_TOKEN", "Join reached without a join token (must follow a ParallelForEach).", false, null);
        }

        string outcome = "succeeded";
        JoinContributionResult result = await _aggregators.ContributeAsync(
            joinToken,
            outcome,
            node.Config.Mode.ToString(),
            node.Config.QuorumCount ?? 0,
            ct);

        if (result.ShouldContinue)
        {
            return new NodeExecutionResult.Continue("default", new Dictionary<string, JsonElement>
            {
                ["joinContributed"] = JsonSerializer.SerializeToElement(result.ContributedCount),
                ["joinSucceeded"] = JsonSerializer.SerializeToElement(result.SucceededCount),
                ["joinFailed"] = JsonSerializer.SerializeToElement(result.FailedCount)
            });
        }

        return new NodeExecutionResult.Terminal(BranchTerminalReason.Completed);
    }
}

