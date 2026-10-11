using System.Text.Json;
using Wbskt.Workflow.Abstraction.Models.Bookmarks;
using Wbskt.Workflow.Abstraction.Models.Nodes;
using Wbskt.Workflow.Abstraction.Models.Nodes.Actions;
using Wbskt.Workflow.Abstraction.Runtime;
using Wbskt.Workflow.Channels;

namespace Wbskt.Workflow.NodeExecutors.Actions;

/// <summary>
/// Sends a client command. The target client comes from the node config: ClientRef must be
/// the client's reference id (a Guid string). The internal client id is resolved by the
/// host-side publisher so the emitted event can be attributed to the client in event logs.
///
/// <para>With <see cref="SendClientMessageConfig.AwaitAck"/> the step waits for the outcome: it parks
/// on the command's id, sends once parked, and leaves by "acked" when the device acknowledges it,
/// "failed" when the platform reports it undelivered, or "timeout" when neither comes in time (the
/// bookmark's TTL takes that port without running the step again).</para>
/// </summary>
internal sealed class CommandNodeExecutor(IDeviceCommandPublisher publisher) : INodeExecutor
{
    public const string AckedPort = CommandChannel.Acked;
    public const string FailedPort = CommandChannel.Failed;
    public const string TimeoutPort = "timeout";

    // The command id this step parked on, and the key the bookmark resumer writes the wake under.
    private const string AwaitingKey = "__command_awaiting";
    private const string WakeKey = "__wake";

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

        if (node.Config.AwaitAck)
        {
            return TryResume(ctx) ?? Park(ctx, node.Config, clientRefId);
        }

        try
        {
            await SendAsync(ctx, node.Config, Guid.NewGuid(), clientRefId, ct);
            return new NodeExecutionResult.Continue("default", new Dictionary<string, JsonElement>());
        }
        catch (Exception ex)
        {
            return new NodeExecutionResult.Fail("COMMAND_PUBLISH_ERROR", ex.Message, true, ex);
        }
    }

    // A wake for the command this step is waiting on. Anything else - no marker, or a "__wake" left by
    // an earlier wait, or by this step's previous lap - means this is a fresh visit.
    private static NodeExecutionResult? TryResume(NodeContext ctx)
    {
        if (!ctx.Branch.LocalState.TryGetValue(AwaitingKey, out JsonElement awaiting)
            || !ctx.Branch.LocalState.TryGetValue(WakeKey, out JsonElement wake)
            || wake.ValueKind != JsonValueKind.Object
            || !wake.TryGetProperty("commandId", out JsonElement wokenFor)
            || !string.Equals(wokenFor.ToString(), awaiting.GetString(), StringComparison.OrdinalIgnoreCase))
        {
            return null;
        }

        string port = wake.TryGetProperty("outcome", out JsonElement outcome) && outcome.GetString() == CommandChannel.Acked
            ? AckedPort
            : FailedPort;
        return new NodeExecutionResult.Continue(
            port,
            new Dictionary<string, JsonElement> { ["command"] = wake.Clone() },
            [AwaitingKey, WakeKey]);
    }

    private NodeExecutionResult Park(NodeContext ctx, SendClientMessageConfig config, Guid clientRefId)
    {
        var commandId = Guid.NewGuid();
        var condition = new CommandOutcomeWakeCondition(commandId)
        {
            Ttl = config.AckTimeout ?? SendClientMessageConfig.DefaultAckTimeout,
            TtlPort = TimeoutPort
        };

        return new NodeExecutionResult.WaitForBookmark(
            condition,
            new Dictionary<string, JsonElement> { [AwaitingKey] = JsonSerializer.SerializeToElement(commandId) })
        {
            // Sent only once the bookmark exists: an ack can come back faster than the branch parks.
            AfterParked = sendCt => SendAsync(ctx, config, commandId, clientRefId, sendCt)
        };
    }

    private Task SendAsync(NodeContext ctx, SendClientMessageConfig config, Guid commandId, Guid clientRefId, CancellationToken ct)
    {
        string payload = config.Payload?.GetRawText() ?? "{}";
        return publisher.PublishCommandAsync(
            commandId,
            clientRefId,
            ctx.Branch.WorkspaceId,
            config.Type,
            payload,
            new CommandSender(ctx.Branch.WorkflowDefinitionRefId, ctx.Branch.RunRefId),
            ct);
    }
}
