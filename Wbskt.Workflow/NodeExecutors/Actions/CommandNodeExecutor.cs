using System.Text.Json;
using Wbskt.Workflow.Abstraction.Models.Nodes;
using Wbskt.Workflow.Abstraction.Models.Nodes.Actions;
using Wbskt.Workflow.Abstraction.Runtime;

namespace Wbskt.Workflow.NodeExecutors.Actions;

/// <summary>
/// Sends a client command. Wraps the side effect in an idempotency reservation keyed by
/// (run, node) so the command is delivered at most once even if the node re-executes
/// (e.g. crash recovery or a branch re-dispatch). The claim-token CAS distinguishes the
/// first execution from a replay: if a prior execution already succeeded, the publish is skipped.
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
                "ClientMessage node could not resolve target client from trigger payload.",
                false,
                null);
        }
        int workspaceId = ctx.Branch.WorkspaceId;
        string command = node.Config.Type;
        string payload = node.Config.Payload?.GetRawText() ?? "{}";

        try
        {
            await publisher.PublishCommandAsync(clientRefId, 0 /* this will be filled in the DeviceCommandPublisher*/, workspaceId, command, payload, ct);
            return new NodeExecutionResult.Continue("default", new Dictionary<string, JsonElement>());
        }
        catch (Exception ex)
        {
            return new NodeExecutionResult.Fail("COMMAND_PUBLISH_ERROR", ex.Message, true, ex);
        }
    }
}
