using System.Text.Json;
using Moq;
using Wbskt.Workflow.Abstraction.Models.Nodes.Actions;
using Wbskt.Workflow.Abstraction.Runtime;
using Wbskt.Workflow.NodeExecutors.Actions;

namespace Wbskt.Workflow.Engine.Host.Tests.NodeExecutors.Actions;

public sealed class CommandNodeExecutorTests
{
    private static readonly Guid ClientRefId = Guid.Parse("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa");
    private const int ClientId = 7;
    private const int WorkspaceId = 3;

    [Fact]
    public async Task ExecuteAsync_publishes_command_and_returns_Continue()
    {
        var publisher = new Mock<IDeviceCommandPublisher>();
        publisher.Setup(p => p.PublishCommandAsync(It.IsAny<Guid>(), It.IsAny<int>(), It.IsAny<int>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);

        var executor = new CommandNodeExecutor(publisher.Object);
        JsonElement payload = JsonSerializer.SerializeToElement(new { temperature = 22 });
        NodeContext ctx = BuildContext("OpenVent", payload);

        NodeExecutionResult result = await executor.ExecuteAsync(ctx, CancellationToken.None);

        var cont = Assert.IsType<NodeExecutionResult.Continue>(result);
        Assert.Equal("default", cont.OutboundPort);
        publisher.Verify(p => p.PublishCommandAsync(
            ClientRefId,
            ClientId,
            WorkspaceId,
            "OpenVent",
            payload.GetRawText(),
            CancellationToken.None), Times.Once);
    }

    [Fact]
    public async Task ExecuteAsync_returns_Continue_with_empty_patch()
    {
        var publisher = new Mock<IDeviceCommandPublisher>();
        publisher.Setup(p => p.PublishCommandAsync(It.IsAny<Guid>(), It.IsAny<int>(), It.IsAny<int>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);

        var executor = new CommandNodeExecutor(publisher.Object);
        NodeContext ctx = BuildContext("CloseVent", null);

        NodeExecutionResult result = await executor.ExecuteAsync(ctx, CancellationToken.None);

        var cont = Assert.IsType<NodeExecutionResult.Continue>(result);
        Assert.Empty(cont.LocalStatePatch);
        publisher.Verify(p => p.PublishCommandAsync(
            It.IsAny<Guid>(), It.IsAny<int>(), It.IsAny<int>(),
            "CloseVent", "{}", CancellationToken.None), Times.Once);
    }

    [Fact]
    public async Task ExecuteAsync_returns_Fail_with_COMMAND_NO_TARGET_when_clientRefId_missing()
    {
        var publisher = new Mock<IDeviceCommandPublisher>();
        var executor = new CommandNodeExecutor(publisher.Object);
        NodeContext ctx = BuildContextWithPayload(new Dictionary<string, JsonElement>
        {
            ["clientId"] = JsonSerializer.SerializeToElement(7),
            ["workspaceId"] = JsonSerializer.SerializeToElement(3)
        }, "OpenVent", null);

        NodeExecutionResult result = await executor.ExecuteAsync(ctx, CancellationToken.None);

        var fail = Assert.IsType<NodeExecutionResult.Fail>(result);
        Assert.Equal("COMMAND_NO_TARGET", fail.ErrorCode);
        Assert.False(fail.Retryable);
        publisher.Verify(p => p.PublishCommandAsync(It.IsAny<Guid>(), It.IsAny<int>(), It.IsAny<int>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task ExecuteAsync_returns_Fail_with_COMMAND_NO_TARGET_when_clientId_missing()
    {
        var publisher = new Mock<IDeviceCommandPublisher>();
        var executor = new CommandNodeExecutor(publisher.Object);
        NodeContext ctx = BuildContextWithPayload(new Dictionary<string, JsonElement>
        {
            ["clientRefId"] = JsonSerializer.SerializeToElement(ClientRefId),
            ["workspaceId"] = JsonSerializer.SerializeToElement(3)
        }, "OpenVent", null);

        NodeExecutionResult result = await executor.ExecuteAsync(ctx, CancellationToken.None);

        var fail = Assert.IsType<NodeExecutionResult.Fail>(result);
        Assert.Equal("COMMAND_NO_TARGET", fail.ErrorCode);
    }

    [Fact]
    public async Task ExecuteAsync_returns_Fail_with_COMMAND_NO_TARGET_when_workspaceId_missing()
    {
        var publisher = new Mock<IDeviceCommandPublisher>();
        var executor = new CommandNodeExecutor(publisher.Object);
        NodeContext ctx = BuildContextWithPayload(new Dictionary<string, JsonElement>
        {
            ["clientRefId"] = JsonSerializer.SerializeToElement(ClientRefId),
            ["clientId"] = JsonSerializer.SerializeToElement(7)
        }, "OpenVent", null);

        NodeExecutionResult result = await executor.ExecuteAsync(ctx, CancellationToken.None);

        var fail = Assert.IsType<NodeExecutionResult.Fail>(result);
        Assert.Equal("COMMAND_NO_TARGET", fail.ErrorCode);
    }

    [Fact]
    public async Task ExecuteAsync_returns_Fail_retryable_when_publisher_throws()
    {
        var publisher = new Mock<IDeviceCommandPublisher>();
        var exception = new InvalidOperationException("broker down");
        publisher.Setup(p => p.PublishCommandAsync(It.IsAny<Guid>(), It.IsAny<int>(), It.IsAny<int>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(exception);

        var executor = new CommandNodeExecutor(publisher.Object);
        NodeContext ctx = BuildContext("OpenVent", null);

        NodeExecutionResult result = await executor.ExecuteAsync(ctx, CancellationToken.None);

        var fail = Assert.IsType<NodeExecutionResult.Fail>(result);
        Assert.Equal("COMMAND_PUBLISH_ERROR", fail.ErrorCode);
        Assert.Equal("broker down", fail.Message);
        Assert.True(fail.Retryable);
        Assert.Same(exception, fail.Cause);
    }

    private static NodeContext BuildContext(string command, JsonElement? payload)
    {
        return BuildContextWithPayload(new Dictionary<string, JsonElement>
        {
            ["clientRefId"] = JsonSerializer.SerializeToElement(ClientRefId),
            ["clientId"] = JsonSerializer.SerializeToElement(ClientId),
            ["workspaceId"] = JsonSerializer.SerializeToElement(WorkspaceId)
        }, command, payload);
    }

    private static NodeContext BuildContextWithPayload(
        Dictionary<string, JsonElement> triggerPayload,
        string command,
        JsonElement? nodePayload)
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
            triggerPayload,
            "corr-42",
            new DateTime(2026, 5, 26, 12, 0, 0, DateTimeKind.Utc));

        var node = new SendCommandActionNode(
            Guid.Parse("11111111-1111-1111-1111-111111111111"),
            "command",
            [],
            new SendCommandConfig("device-1", command, nodePayload));

        return new NodeContext
        {
            Branch = branch,
            Node = node,
            Providers = Mock.Of<IProviderComposite>(),
            Tick = 1,
            ParentResults = null,
            CancellationToken = CancellationToken.None
        };
    }
}
