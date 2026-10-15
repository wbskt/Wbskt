using System.Text.Json;
using Moq;
using Wbskt.Workflow.Abstraction.Models.Bookmarks;
using Wbskt.Workflow.Abstraction.Models.Nodes.Actions;
using Wbskt.Workflow.Abstraction.Providers;
using Wbskt.Workflow.Abstraction.Runtime;
using Wbskt.Workflow.NodeExecutors.Actions;

namespace Wbskt.Host.Tests.NodeExecutors.Actions;

public sealed class CommandNodeExecutorTests
{
    private static readonly Guid ClientRefId = Guid.Parse("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa");
    private const int WorkspaceId = 9;

    [Fact]
    public async Task ExecuteAsync_publishes_command_and_returns_Continue()
    {
        var publisher = new Mock<IDeviceCommandPublisher>();
        publisher.Setup(p => p.PublishCommandAsync(It.IsAny<Guid>(), It.IsAny<Guid>(), It.IsAny<int>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<TimeSpan>(), It.IsAny<CommandSender?>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);

        var executor = new CommandNodeExecutor(publisher.Object, Mock.Of<IBookmarkResumer>());
        JsonElement payload = JsonSerializer.SerializeToElement(new { temperature = 22 });
        NodeContext ctx = BuildContext(ClientRefId.ToString(), "OpenVent", payload);

        NodeExecutionResult result = await executor.ExecuteAsync(ctx, CancellationToken.None);

        var cont = Assert.IsType<NodeExecutionResult.Continue>(result);
        Assert.Equal("default", cont.OutboundPort);
        publisher.Verify(p => p.PublishCommandAsync(
            It.IsAny<Guid>(),
            ClientRefId,
            WorkspaceId,
            "OpenVent",
            payload.GetRawText(),
            // Without a wait of its own, a command expires when a step would stop waiting by default.
            SendClientMessageConfig.DefaultAckTimeout,
            It.Is<CommandSender?>(sender => sender == new CommandSender(ctx.Branch.WorkflowDefinitionRefId, ctx.Branch.RunRefId)),
            CancellationToken.None), Times.Once);
    }

    [Fact]
    public async Task ExecuteAsync_returns_Continue_with_empty_patch()
    {
        var publisher = new Mock<IDeviceCommandPublisher>();
        publisher.Setup(p => p.PublishCommandAsync(It.IsAny<Guid>(), It.IsAny<Guid>(), It.IsAny<int>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<TimeSpan>(), It.IsAny<CommandSender?>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);

        var executor = new CommandNodeExecutor(publisher.Object, Mock.Of<IBookmarkResumer>());
        NodeContext ctx = BuildContext(ClientRefId.ToString(), "CloseVent", null);

        NodeExecutionResult result = await executor.ExecuteAsync(ctx, CancellationToken.None);

        var cont = Assert.IsType<NodeExecutionResult.Continue>(result);
        Assert.Empty(cont.LocalStatePatch);
        publisher.Verify(p => p.PublishCommandAsync(
            It.IsAny<Guid>(), It.IsAny<Guid>(), It.IsAny<int>(),
            "CloseVent", "{}", It.IsAny<TimeSpan>(), It.IsAny<CommandSender?>(), CancellationToken.None), Times.Once);
    }

    [Fact]
    public async Task ExecuteAsync_returns_Fail_with_CLIENT_MESSAGE_NO_TARGET_when_ClientRef_is_not_a_guid()
    {
        var publisher = new Mock<IDeviceCommandPublisher>();
        var executor = new CommandNodeExecutor(publisher.Object, Mock.Of<IBookmarkResumer>());
        NodeContext ctx = BuildContext("client-1", "OpenVent", null);

        NodeExecutionResult result = await executor.ExecuteAsync(ctx, CancellationToken.None);

        var fail = Assert.IsType<NodeExecutionResult.Fail>(result);
        Assert.Equal("CLIENT_MESSAGE_NO_TARGET", fail.ErrorCode);
        Assert.False(fail.Retryable);
        publisher.Verify(p => p.PublishCommandAsync(It.IsAny<Guid>(), It.IsAny<Guid>(), It.IsAny<int>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<TimeSpan>(), It.IsAny<CommandSender?>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task ExecuteAsync_returns_Fail_retryable_when_publisher_throws()
    {
        var publisher = new Mock<IDeviceCommandPublisher>();
        var exception = new InvalidOperationException("broker down");
        publisher.Setup(p => p.PublishCommandAsync(It.IsAny<Guid>(), It.IsAny<Guid>(), It.IsAny<int>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<TimeSpan>(), It.IsAny<CommandSender?>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(exception);

        var executor = new CommandNodeExecutor(publisher.Object, Mock.Of<IBookmarkResumer>());
        NodeContext ctx = BuildContext(ClientRefId.ToString(), "OpenVent", null);

        NodeExecutionResult result = await executor.ExecuteAsync(ctx, CancellationToken.None);

        var fail = Assert.IsType<NodeExecutionResult.Fail>(result);
        Assert.Equal("COMMAND_PUBLISH_ERROR", fail.ErrorCode);
        Assert.Equal("broker down", fail.Message);
        Assert.True(fail.Retryable);
        Assert.Same(exception, fail.Cause);
    }

    [Fact]
    public async Task Awaiting_the_ack_parks_on_the_command_and_sends_it_only_once_parked()
    {
        var publisher = new Mock<IDeviceCommandPublisher>();
        var executor = new CommandNodeExecutor(publisher.Object, Mock.Of<IBookmarkResumer>());
        NodeContext ctx = BuildContext(ClientRefId.ToString(), "OpenVent", null, AwaitAck());

        NodeExecutionResult result = await executor.ExecuteAsync(ctx, CancellationToken.None);

        var wait = Assert.IsType<NodeExecutionResult.WaitForBookmark>(result);
        var condition = Assert.IsType<CommandOutcomeWakeCondition>(wait.Condition);
        Assert.Equal(SendClientMessageConfig.DefaultAckTimeout, condition.Ttl);
        Assert.Equal(CommandNodeExecutor.TimeoutPort, condition.TtlPort);
        publisher.VerifyNoOtherCalls();

        Assert.NotNull(wait.AfterParked);
        await wait.AfterParked(CancellationToken.None);
        publisher.Verify(p => p.PublishCommandAsync(condition.CommandId, ClientRefId, WorkspaceId, "OpenVent", "{}", It.IsAny<TimeSpan>(), It.IsAny<CommandSender?>(), CancellationToken.None), Times.Once);
    }

    [Fact]
    public async Task A_command_that_cannot_be_sent_wakes_its_wait_as_failed_at_once()
    {
        // Otherwise the step would sit out its whole ack timeout and leave by "timeout", as if the
        // device had not answered.
        var publisher = new Mock<IDeviceCommandPublisher>();
        var sendFailure = new InvalidOperationException("broker down");
        publisher.Setup(p => p.PublishCommandAsync(It.IsAny<Guid>(), It.IsAny<Guid>(), It.IsAny<int>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<TimeSpan>(), It.IsAny<CommandSender?>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(sendFailure);
        var resumer = new Mock<IBookmarkResumer>();
        var executor = new CommandNodeExecutor(publisher.Object, resumer.Object);

        var wait = Assert.IsType<NodeExecutionResult.WaitForBookmark>(
            await executor.ExecuteAsync(BuildContext(ClientRefId.ToString(), "OpenVent", null, AwaitAck()), CancellationToken.None));
        Guid commandId = Assert.IsType<CommandOutcomeWakeCondition>(wait.Condition).CommandId;

        // Rethrown so the branch loop logs it.
        Assert.Same(sendFailure, await Assert.ThrowsAsync<InvalidOperationException>(() => wait.AfterParked!(CancellationToken.None)));
        resumer.Verify(r => r.MatchInboundAsync(
            It.Is<InboundEvent>(e =>
                e.MatchKeys.Contains($"command:{commandId}")
                && e.Payload["outcome"].GetString() == "failed"
                && e.Payload["reason"].GetString() == "The command could not be sent."),
            false,
            CancellationToken.None), Times.Once);
    }

    [Theory]
    [InlineData("acked", "acked")]
    [InlineData("failed", "failed")]
    public async Task A_wake_for_the_awaited_command_leaves_by_its_outcome_and_clears_the_wait(string outcome, string expectedPort)
    {
        var executor = new CommandNodeExecutor(Mock.Of<IDeviceCommandPublisher>(), Mock.Of<IBookmarkResumer>());
        Guid commandId = Guid.NewGuid();
        NodeContext ctx = BuildContext(ClientRefId.ToString(), "OpenVent", null, AwaitAck(), new Dictionary<string, JsonElement>
        {
            ["__command_awaiting"] = JsonSerializer.SerializeToElement(commandId),
            ["__wake"] = JsonSerializer.SerializeToElement(new { commandId, outcome, reason = (string?)null })
        });

        NodeExecutionResult result = await executor.ExecuteAsync(ctx, CancellationToken.None);

        var cont = Assert.IsType<NodeExecutionResult.Continue>(result);
        Assert.Equal(expectedPort, cont.OutboundPort);
        Assert.Equal(outcome, cont.LocalStatePatch["command"].GetProperty("outcome").GetString());
        Assert.Equal(["__command_awaiting", "__wake", "__wake_node"], cont.RemoveKeys);
    }

    [Fact]
    public async Task A_wake_left_by_another_wait_is_not_taken_for_this_commands_answer()
    {
        // A step revisited in a loop, or one after an Await Signal, still finds the old "__wake".
        var executor = new CommandNodeExecutor(Mock.Of<IDeviceCommandPublisher>(), Mock.Of<IBookmarkResumer>());
        NodeContext ctx = BuildContext(ClientRefId.ToString(), "OpenVent", null, AwaitAck(), new Dictionary<string, JsonElement>
        {
            ["__command_awaiting"] = JsonSerializer.SerializeToElement(Guid.NewGuid()),
            ["__wake"] = JsonSerializer.SerializeToElement(new { commandId = Guid.NewGuid(), outcome = "acked" })
        });

        NodeExecutionResult result = await executor.ExecuteAsync(ctx, CancellationToken.None);

        Assert.IsType<NodeExecutionResult.WaitForBookmark>(result);
    }

    private static SendClientMessageConfig AwaitAck() => new() { ClientRef = ClientRefId.ToString(), Type = "OpenVent", AwaitAck = true };

    private static NodeContext BuildContext(string clientRef, string command, JsonElement? nodePayload, SendClientMessageConfig? config = null, Dictionary<string, JsonElement>? localState = null)
    {
        var branch = new BranchContext(
            42,
            1001,
            9,
            Guid.Parse("bbbbbbbb-bbbb-bbbb-bbbb-bbbbbbbbbbbb"),
            1,
            "node-1",
            1,
            localState ?? new Dictionary<string, JsonElement>(),
            new Dictionary<string, JsonElement>(),
            "corr-42",
            new DateTime(2026, 5, 26, 12, 0, 0, DateTimeKind.Utc),
            WorkspaceId);

        var node = new SendClientMessageNode { NodeId = Guid.Parse("11111111-1111-1111-1111-111111111111"), Name = "command", Ports = [], Config = config ?? new SendClientMessageConfig { ClientRef = clientRef, Type = command, Payload = nodePayload } };

        return new NodeContext
        {
            Branch = branch,
            Node = node,
            CancellationToken = CancellationToken.None, IdempotencyKey = "test-key"
        };
    }
}
