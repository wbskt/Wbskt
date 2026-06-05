using System.Text.Json;
using Wbskt.Workflow.Abstraction.Models.Nodes;
using Wbskt.Workflow.Abstraction.Models.Nodes.Actions;
using Wbskt.Workflow.Abstraction.Runtime;

namespace Wbskt.Workflow.NodeExecutors.Actions;

public sealed class CommandNodeExecutor(IDeviceCommandPublisher publisher) : INodeExecutor
{
    public string Kind => NodeKind.ActionCommand;

    public async Task<NodeExecutionResult> ExecuteAsync(NodeContext ctx, CancellationToken ct)
    {
        SendCommandActionNode node = (SendCommandActionNode)ctx.Node;
        IReadOnlyDictionary<string, JsonElement> triggerPayload = ctx.Branch.TriggerPayload;

        if (!TryGetClientRefId(triggerPayload, out Guid clientRefId)
            || !TryGetInt(triggerPayload, "clientId", out int clientId)
            || !TryGetInt(triggerPayload, "workspaceId", out int workspaceId))
        {
            return new NodeExecutionResult.Fail(
                "COMMAND_NO_TARGET",
                "Command node could not resolve target client from trigger payload.",
                false,
                null);
        }

        string command = node.Config.Command;
        string payload = node.Config.Payload?.GetRawText() ?? "{}";

        try
        {
            await publisher.PublishCommandAsync(clientRefId, clientId, workspaceId, command, payload, ct);
            return new NodeExecutionResult.Continue("default", new Dictionary<string, JsonElement>());
        }
        catch (Exception ex)
        {
            return new NodeExecutionResult.Fail("COMMAND_PUBLISH_ERROR", ex.Message, true, ex);
        }
    }

    private static bool TryGetClientRefId(IReadOnlyDictionary<string, JsonElement> payload, out Guid clientRefId)
    {
        clientRefId = default;
        if (!payload.TryGetValue("clientRefId", out JsonElement element))
        {
            return false;
        }

        if (element.ValueKind == JsonValueKind.String)
        {
            return Guid.TryParse(element.GetString(), out clientRefId);
        }

        return element.TryGetGuid(out clientRefId);
    }

    private static bool TryGetInt(IReadOnlyDictionary<string, JsonElement> payload, string key, out int value)
    {
        value = 0;
        return payload.TryGetValue(key, out JsonElement element) && element.TryGetInt32(out value);
    }
}

