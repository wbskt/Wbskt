using System.Text.Json;
using Moq;
using Wbskt.Workflow.Abstraction.Models.Nodes.Actions;
using Wbskt.Workflow.Abstraction.Providers;
using Wbskt.Workflow.Abstraction.Runtime;
using Wbskt.Workflow.NodeExecutors.Actions;

namespace Wbskt.Workflow.Engine.Host.Tests.NodeExecutors.Actions;

public sealed class CommandNodeExecutorTests
{
    private static readonly Guid ClientRefId = Guid.Parse("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa");
    private const int WorkspaceId = 9;

    [Fact]
    public async Task ExecuteAsync_publishes_command_and_returns_Continue()
    {
        var publisher = new Mock<IDeviceCommandPublisher>();
        publisher.Setup(p => p.PublishCommandAsync(It.IsAny<Guid>(), It.IsAny<int>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CommandSender?>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);

        var executor = new CommandNodeExecutor(publisher.Object);
        JsonElement payload = JsonSerializer.SerializeToElement(new { temperature = 22 });
        NodeContext ctx = BuildContext(ClientRefId.ToString(), "OpenVent", payload);

        NodeExecutionResult result = await executor.ExecuteAsync(ctx, CancellationToken.None);

        var cont = Assert.IsType<NodeExecutionResult.Continue>(result);
        Assert.Equal("default", cont.OutboundPort);
        publisher.Verify(p => p.PublishCommandAsync(
            ClientRefId,
            WorkspaceId,
            "OpenVent",
            payload.GetRawText(),
            It.Is<CommandSender?>(sender => sender == new CommandSender(ctx.Branch.WorkflowDefinitionRefId, ctx.Branch.RunRefId)),
            CancellationToken.None), Times.Once);
    }

    [Fact]
    public async Task ExecuteAsync_returns_Continue_with_empty_patch()
    {
        var publisher = new Mock<IDeviceCommandPublisher>();
        publisher.Setup(p => p.PublishCommandAsync(It.IsAny<Guid>(), It.IsAny<int>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CommandSender?>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);

        var executor = new CommandNodeExecutor(publisher.Object);
        NodeContext ctx = BuildContext(ClientRefId.ToString(), "CloseVent", null);

        NodeExecutionResult result = await executor.ExecuteAsync(ctx, CancellationToken.None);

        var cont = Assert.IsType<NodeExecutionResult.Continue>(result);
        Assert.Empty(cont.LocalStatePatch);
        publisher.Verify(p => p.PublishCommandAsync(
            It.IsAny<Guid>(), It.IsAny<int>(),
            "CloseVent", "{}", It.IsAny<CommandSender?>(), CancellationToken.None), Times.Once);
    }

    [Fact]
    public async Task ExecuteAsync_returns_Fail_with_CLIENT_MESSAGE_NO_TARGET_when_ClientRef_is_not_a_guid()
    {
        var publisher = new Mock<IDeviceCommandPublisher>();
        var executor = new CommandNodeExecutor(publisher.Object);
        NodeContext ctx = BuildContext("client-1", "OpenVent", null);

        NodeExecutionResult result = await executor.ExecuteAsync(ctx, CancellationToken.None);

        var fail = Assert.IsType<NodeExecutionResult.Fail>(result);
        Assert.Equal("CLIENT_MESSAGE_NO_TARGET", fail.ErrorCode);
        Assert.False(fail.Retryable);
        publisher.Verify(p => p.PublishCommandAsync(It.IsAny<Guid>(), It.IsAny<int>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CommandSender?>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task ExecuteAsync_returns_Fail_retryable_when_publisher_throws()
    {
        var publisher = new Mock<IDeviceCommandPublisher>();
        var exception = new InvalidOperationException("broker down");
        publisher.Setup(p => p.PublishCommandAsync(It.IsAny<Guid>(), It.IsAny<int>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CommandSender?>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(exception);

        var executor = new CommandNodeExecutor(publisher.Object);
        NodeContext ctx = BuildContext(ClientRefId.ToString(), "OpenVent", null);

        NodeExecutionResult result = await executor.ExecuteAsync(ctx, CancellationToken.None);

        var fail = Assert.IsType<NodeExecutionResult.Fail>(result);
        Assert.Equal("COMMAND_PUBLISH_ERROR", fail.ErrorCode);
        Assert.Equal("broker down", fail.Message);
        Assert.True(fail.Retryable);
        Assert.Same(exception, fail.Cause);
    }

    private static NodeContext BuildContext(string clientRef, string command, JsonElement? nodePayload)
    {
        var branch = new BranchContext(
            42,
            1001,
            9,
            Guid.Parse("bbbbbbbb-bbbb-bbbb-bbbb-bbbbbbbbbbbb"),
            1,
            "node-1",
            1,
            new Dictionary<string, JsonElement>(),
            new Dictionary<string, JsonElement>(),
            "corr-42",
            new DateTime(2026, 5, 26, 12, 0, 0, DateTimeKind.Utc),
            WorkspaceId);

        var node = new SendClientMessageNode { NodeId = Guid.Parse("11111111-1111-1111-1111-111111111111"), Name = "command", Ports = [], Config = new SendClientMessageConfig { ClientRef = clientRef, Type = command, Payload = nodePayload } };

        return new NodeContext
        {
            Branch = branch,
            Node = node,
            Providers = new Mock<IProviderComposite>().Object,
            Tick = 1,
            ParentResults = null,
            CancellationToken = CancellationToken.None, IdempotencyKey = "test-key"
        };
    }
}
