using System.Text.Json;
using Wbskt.Workflow.Abstraction.Models.Bookmarks;
using Wbskt.Workflow.Abstraction.Models.Nodes;
using Wbskt.Workflow.Abstraction.Models.Nodes.Actions;
using Wbskt.Workflow.Abstraction.Runtime;
using Wbskt.Workflow.Channels;
using Wbskt.Workflow.NodeExecutors.Controls;
using Wbskt.Workflow.Runtime;

namespace Wbskt.Workflow.NodeExecutors.Actions;

/// <summary>
/// Sends a client command. The target client comes from the node config: ClientRef must be
/// the client's reference id (a Guid string). The internal client id is resolved by the
/// host-side publisher so the emitted event can be attributed to the client in event logs.
///
/// <para>With <see cref="SendClientMessageConfig.AwaitAck"/> the step waits for the outcome: it parks
/// on the command's id, sends once parked, and leaves by "acked" when the device acknowledges it,
/// "failed" when the platform reports it undelivered or it could not be sent at all, or "timeout" when
/// neither comes in time (the bookmark's TTL takes that port without running the step again).</para>
/// </summary>
internal sealed class CommandNodeExecutor(IDeviceCommandPublisher publisher, IBookmarkResumer bookmarkResumer, ActionTemplates templates, TimeProvider? clock = null)
    : ParkingNodeExecutor(clock ?? TimeProvider.System)
{
    public const string AckedPort = CommandChannel.Acked;
    public const string FailedPort = CommandChannel.Failed;
    public const string TimeoutPort = "timeout";

    public override string Kind => NodeKind.ActionClientMessage;

    // The marker holds the command id this step parked on. There is no deadline key: the ack timeout
    // lives on the bookmark, whose TTL takes the "timeout" port without running the step again.
    protected override string ParkedKey => BranchStateKeys.CommandAwaiting;

    protected override async Task<NodeExecutionResult> FirstVisitAsync(NodeContext ctx, CancellationToken ct)
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

        SendClientMessageConfig config = await templates.FillAsync(ctx, node.Config, ct);

        if (config.AwaitAck)
        {
            return Park(ctx, config, clientRefId);
        }

        try
        {
            await SendAsync(ctx, config, Guid.NewGuid(), clientRefId, ct);
            return new NodeExecutionResult.Continue("default", new Dictionary<string, JsonElement>());
        }
        catch (Exception ex)
        {
            return new NodeExecutionResult.Fail("COMMAND_PUBLISH_ERROR", ex.Message, true, ex);
        }
    }

    // A wake is this step's answer only when it is for the command the step is waiting on. Anything
    // else - a "__wake" left by an earlier wait, or by this step's previous lap - is stale.
    protected override ResumeStep.Leave? OnWake(NodeContext ctx, string? waitingOn, JsonElement wake)
    {
        if (wake.ValueKind != JsonValueKind.Object
            || !wake.TryGetProperty("commandId", out JsonElement wokenFor)
            || !string.Equals(wokenFor.ToString(), waitingOn, StringComparison.OrdinalIgnoreCase))
        {
            return null;
        }

        string port = wake.TryGetProperty("outcome", out JsonElement outcome) && outcome.GetString() == CommandChannel.Acked
            ? AckedPort
            : FailedPort;
        return new ResumeStep.Leave(port, new Dictionary<string, JsonElement> { ["command"] = wake.Clone() });
    }

    // Run again with no answer on the branch, the step's wait has nothing left to end it (its timeout
    // is taken without running it), so it sends a new command and waits for that one.
    protected override ResumeStep OnNoWake(NodeContext ctx, string? waitingOn, bool deadlinePassed)
    {
        return ResumeStep.StartOver;
    }

    // Kept waiting after a stale wake, the step waits on the same command and sends nothing. Having no
    // deadline on the branch, it waits a whole ack timeout again.
    protected override WakeCondition? KeepWaitingOn(NodeContext ctx, string? waitingOn, DateTime? deadline)
    {
        return Guid.TryParse(waitingOn, out Guid commandId)
            ? Condition(commandId, ((SendClientMessageNode)ctx.Node).Config)
            : null;
    }

    private static CommandOutcomeWakeCondition Condition(Guid commandId, SendClientMessageConfig config)
    {
        return new CommandOutcomeWakeCondition(commandId)
        {
            Ttl = config.AckTimeout ?? SendClientMessageConfig.DefaultAckTimeout,
            TtlPort = TimeoutPort
        };
    }

    private NodeExecutionResult Park(NodeContext ctx, SendClientMessageConfig config, Guid clientRefId)
    {
        var commandId = Guid.NewGuid();
        return Park(ctx, new ParkPlan(Condition(commandId, config), commandId.ToString(), null)) with
        {
            // Sent only once the bookmark exists: an ack can come back faster than the branch parks.
            AfterParked = sendCt => SendWhileParkedAsync(ctx, config, commandId, clientRefId, sendCt)
        };
    }

    // A command that could not be sent will never be answered, so the step is woken by "failed" at
    // once instead of waiting out its whole ack timeout and leaving by "timeout", which would read as
    // a device that did not answer. The send failure is still rethrown, for the branch loop to log.
    private async Task SendWhileParkedAsync(NodeContext ctx, SendClientMessageConfig config, Guid commandId, Guid clientRefId, CancellationToken ct)
    {
        try
        {
            await SendAsync(ctx, config, commandId, clientRefId, ct);
        }
        catch (Exception sendFailure) when (!ct.IsCancellationRequested)
        {
            try
            {
                // A fixed reason: the exception's own message can name brokers or hosts, and the
                // reason is written to the branch's state, which the workspace can read.
                await bookmarkResumer.MatchInboundAsync(
                    CommandChannel.Event(commandId, CommandChannel.Failed, "The command could not be sent."),
                    hasRegistrations: false,
                    ct);
            }
            catch (Exception wakeFailure)
            {
                // Waking failed too, so the ack timeout is what moves the step on.
                throw new AggregateException(sendFailure, wakeFailure);
            }

            throw;
        }
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
            // A command is worth acting on only while its step would still be waiting for the answer.
            config.AckTimeout ?? SendClientMessageConfig.DefaultAckTimeout,
            new CommandSender(ctx.Branch.WorkflowDefinitionRefId, ctx.Branch.RunRefId),
            ct);
    }
}
