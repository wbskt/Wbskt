using System.Text.Json;
using Wbskt.Workflow.Abstraction.Enums;
using Wbskt.Workflow.Abstraction.Models;
using Wbskt.Workflow.Abstraction.Models.Expressions;
using Wbskt.Workflow.Abstraction.Models.Nodes.Actions;
using Wbskt.Workflow.Abstraction.Models.Nodes.Controls;
using Wbskt.Workflow.Builder;

namespace Wbskt.Host.Tests.Abstraction;

/// <summary>
/// The settings the engine runs that the builder could not build: a client message waiting for its
/// acknowledgement, a join's quorum, an End's output, and an action's retry and on-failure policy.
/// Each built definition must also pass validation, or tests built on it test nothing.
/// </summary>
public sealed class WorkflowBuilderSettingsTests
{
    private const string ClientRef = "11111111-1111-1111-1111-111111111111";

    [Fact]
    public void A_client_message_that_awaits_its_ack_has_the_three_ack_ports_and_moves_on_by_acked()
    {
        JsonElement payload = JsonSerializer.SerializeToElement(new { on = true });

        WorkflowDefinition def = new WorkflowBuilder("ack")
            .AddManualTrigger()
            .AddClientMessage(ClientRef, "relay.set", payload: payload, awaitAck: true, ackTimeout: TimeSpan.FromSeconds(10))
            .AddEnd()
            .BuildAndValidate();

        SendClientMessageNode send = def.Nodes.OfType<SendClientMessageNode>().Single();
        Assert.True(send.Config!.AwaitAck);
        Assert.Equal(TimeSpan.FromSeconds(10), send.Config.AckTimeout);
        Assert.Equal(payload.GetRawText(), send.Config.Payload!.Value.GetRawText());
        Assert.Equal(["in", "acked", "failed", "timeout"], send.Ports.Select(p => p.PortId));
        Assert.Contains(def.Edges, e => e.From.NodeId == send.NodeId && e.From.PortId == "acked");
    }

    [Fact]
    public void A_join_carries_its_quorum()
    {
        WorkflowDefinition def = new WorkflowBuilder("quorum")
            .AddManualTrigger()
            .AddFork(["a", "b", "c"], out Guid forkId)
            .AddJoin(JoinMode.Quorum, 2, (forkId, "a"), (forkId, "b"), (forkId, "c"))
            .AddEnd()
            .BuildAndValidate();

        Assert.Equal(2, def.Nodes.OfType<JoinNode>().Single().Config!.QuorumCount);
    }

    [Fact]
    public void An_end_carries_its_output()
    {
        WorkflowDefinition def = new WorkflowBuilder("output")
            .AddManualTrigger()
            .AddEnd(new LiteralExpression("done"))
            .BuildAndValidate();

        Assert.Equal(new LiteralExpression("done"), def.Nodes.OfType<EndNode>().Single().Config!.Output);
    }

    [Fact]
    public void Retry_and_on_failure_apply_to_the_last_action()
    {
        var retry = new RetryPolicy { Strategy = RetryStrategy.Constant, InitialDelay = TimeSpan.FromSeconds(1), MaxAttempts = 3, JitterPct = 0, RetryOn = [] };
        var onFailure = new OnFailureConfig { Outcome = ErrorOutcome.ContinueOnError };

        WorkflowDefinition def = new WorkflowBuilder("retry")
            .AddManualTrigger()
            .AddToast("t", "m")
            .WithRetry(retry)
            .WithOnFailure(onFailure)
            .AddEnd()
            .BuildAndValidate();

        ToastNotificationNode toast = def.Nodes.OfType<ToastNotificationNode>().Single();
        Assert.Equal(retry, toast.Retry);
        Assert.Equal(onFailure, toast.OnFailure);
        Assert.Contains(toast.Ports, p => p.PortId == "error");
    }

    [Fact]
    public void Retry_after_a_step_that_is_not_an_action_is_refused()
    {
        WorkflowBuilder builder = new WorkflowBuilder("retry").AddManualTrigger();

        Assert.Throws<InvalidOperationException>(() =>
            builder.WithRetry(new RetryPolicy { Strategy = RetryStrategy.Constant, InitialDelay = TimeSpan.Zero, MaxAttempts = 2, JitterPct = 0, RetryOn = [] }));
    }
}
