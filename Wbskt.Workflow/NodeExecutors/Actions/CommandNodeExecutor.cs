using System.Text.Json;
using Wbskt.Workflow.Abstraction.Models.Nodes;
using Wbskt.Workflow.Abstraction.Models.Nodes.Actions;
using Wbskt.Workflow.Abstraction.Runtime;

namespace Wbskt.Workflow.NodeExecutors.Actions;

/// <summary>
/// Sends a client command. The target client comes from the node config: ClientRef must be
/// the client's reference id (a Guid string). The internal client id is resolved by the
/// host-side publisher so the emitted event can be attributed to the client in event logs.
/// </summary>
internal sealed class CommandNodeExecutor(IDeviceCommandPublisher publisher) : INodeExecutor
{
    public string Kind => NodeKind.ActionClientMessage;

    public async Task<NodeExecutionResult> ExecuteAsync(NodeContext ctx, CancellationToken ct)
    {
        SendClientMessageNode node = (SendClientMessageNode)ctx.Node;
        if (!Guid.TryParse(node.Config.ClientRef, out Guid clientRefId))
        {
            return new NodeExecutionResult.Fail(
                "CLIENT_MESSAGE_NO_TARGET",
                $"ClientMessage node config has an invalid client reference id: '{node.Config.ClientRef}'.",
                false,
                null);
        }
        int workspaceId = ctx.Branch.WorkspaceId;
        string command = node.Config.Type;
        string payload = node.Config.Payload?.GetRawText() ?? "{}";

        try
        {
            await publisher.PublishCommandAsync(clientRefId, workspaceId, command, payload, new CommandSender(ctx.Branch.WorkflowDefinitionRefId, ctx.Branch.RunRefId), ct);
            return new NodeExecutionResult.Continue("default", new Dictionary<string, JsonElement>());
        }
        catch (Exception ex)
        {
            return new NodeExecutionResult.Fail("COMMAND_PUBLISH_ERROR", ex.Message, true, ex);
        }
    }
}
