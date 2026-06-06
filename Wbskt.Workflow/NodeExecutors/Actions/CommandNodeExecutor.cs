using System.Text.Json;
using Wbskt.Workflow.Abstraction.Entities;
using Wbskt.Workflow.Abstraction.Models.Nodes;
using Wbskt.Workflow.Abstraction.Models.Nodes.Actions;
using Wbskt.Workflow.Abstraction.Runtime;

namespace Wbskt.Workflow.NodeExecutors.Actions;

/// <summary>
/// Sends a device command. Wraps the side effect in an idempotency reservation keyed by
/// (run, node) so the command is delivered at most once even if the node re-executes
/// (e.g. crash recovery or a branch re-dispatch). The claim-token CAS distinguishes the
/// first execution from a replay: if a prior execution already succeeded, the publish is skipped.
/// </summary>
public sealed class CommandNodeExecutor(IDeviceCommandPublisher publisher) : INodeExecutor
{
    private const string SucceededStatus = "Succeeded";

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

        // Reserve an idempotency key for this logical action before the side effect.
        string idempotencyKey = $"action:command:{ctx.Branch.RunId}:{node.NodeId}";
        Guid claimToken = Guid.NewGuid();
        IdempotencyKeyRow claim = await ctx.Providers.IdempotencyKey.UpsertPendingAsync(
            idempotencyKey, (int)ctx.Branch.RunId, claimToken, node.NodeId, ctx.Branch.Attempt, ct);

        // We did not win the claim AND a prior execution already delivered the command:
        // this is a replay — skip the duplicate side effect.
        if (claim.BranchRefId != claimToken && string.Equals(claim.Status, SucceededStatus, StringComparison.Ordinal))
        {
            return new NodeExecutionResult.Continue("default", new Dictionary<string, JsonElement>());
        }

        try
        {
            await publisher.PublishCommandAsync(clientRefId, clientId, workspaceId, command, payload, ct);
            await ctx.Providers.IdempotencyKey.MarkSucceededAsync(idempotencyKey, "{}", ct);
            return new NodeExecutionResult.Continue("default", new Dictionary<string, JsonElement>());
        }
        catch (Exception ex)
        {
            await ctx.Providers.IdempotencyKey.MarkFailedAsync(
                idempotencyKey,
                JsonSerializer.Serialize(new { ex.Message }, new JsonSerializerOptions(JsonSerializerDefaults.Web)),
                ct);
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
