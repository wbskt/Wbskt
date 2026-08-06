using System.Text.Json;
using Moq;
using Wbskt.Workflow.Abstraction.Models.Nodes.Actions;
using Wbskt.Workflow.Abstraction.Runtime;
using Wbskt.Workflow.NodeExecutors.Actions;

namespace Wbskt.Workflow.Engine.Host.Tests.NodeExecutors.Actions;

public sealed class ToastNodeExecutorTests
{
    private static readonly Guid WorkflowRefId = Guid.Parse("bbbbbbbb-bbbb-bbbb-bbbb-bbbbbbbbbbbb");
    private static readonly Guid RunRefId = Guid.Parse("cccccccc-cccc-cccc-cccc-cccccccccccc");
    private const int WorkflowDefinitionId = 9;
    private const int WorkspaceId = 7;

    [Fact]
    public async Task ExecuteAsync_publishes_workspace_scoped_toast_and_returns_Continue()
    {
        var publisher = new Mock<IToastPublisher>();
        publisher.Setup(p => p.PublishToastAsync(
                It.IsAny<Guid>(), It.IsAny<int>(), It.IsAny<Guid>(), It.IsAny<int>(),
                It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);

        var executor = new ToastNodeExecutor(publisher.Object);
        NodeContext ctx = BuildContext("Deploy finished", "Build 42 is live.");

        NodeExecutionResult result = await executor.ExecuteAsync(ctx, CancellationToken.None);

        var cont = Assert.IsType<NodeExecutionResult.Continue>(result);
        Assert.Equal("default", cont.OutboundPort);
        Assert.Empty(cont.LocalStatePatch);

        // The run reference travels with the toast so a dashboard can link back to the run.
        publisher.Verify(p => p.PublishToastAsync(
            WorkflowRefId,
            WorkflowDefinitionId,
            RunRefId,
            WorkspaceId,
            "Deploy finished",
            "Build 42 is live.",
            CancellationToken.None), Times.Once);
    }

    [Fact]
    public async Task ExecuteAsync_returns_Fail_retryable_when_publisher_throws()
    {
        var publisher = new Mock<IToastPublisher>();
        var exception = new InvalidOperationException("broker down");
        publisher.Setup(p => p.PublishToastAsync(
                It.IsAny<Guid>(), It.IsAny<int>(), It.IsAny<Guid>(), It.IsAny<int>(),
                It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(exception);

        var executor = new ToastNodeExecutor(publisher.Object);
        NodeContext ctx = BuildContext("Alert", "Something happened.");

        NodeExecutionResult result = await executor.ExecuteAsync(ctx, CancellationToken.None);

        var fail = Assert.IsType<NodeExecutionResult.Fail>(result);
        Assert.Equal("TOAST_PUBLISH_ERROR", fail.ErrorCode);
        Assert.Equal("broker down", fail.Message);
        Assert.True(fail.Retryable, "a briefly unavailable bus is worth retrying");
        Assert.Same(exception, fail.Cause);
    }

    private static NodeContext BuildContext(string title, string message)
    {
        var branch = new BranchContext(
            42,
            1001,
            WorkflowDefinitionId,
            WorkflowRefId,
            1,
            "node-1",
            1,
            new Dictionary<string, JsonElement>(),
            new Dictionary<string, JsonElement>(),
            "corr-42",
            new DateTime(2026, 5, 26, 12, 0, 0, DateTimeKind.Utc),
            WorkspaceId)
        {
            RunRefId = RunRefId
        };

        var node = new ToastNotificationNode
        {
            NodeId = Guid.Parse("11111111-1111-1111-1111-111111111111"),
            Name = "toast",
            Ports = [],
            Config = new ToastConfig { Title = title, Message = message }
        };

        return new NodeContext
        {
            Branch = branch,
            Node = node,
            Providers = new Mock<IProviderComposite>().Object,
            Tick = 1,
            ParentResults = null,
            CancellationToken = CancellationToken.None,
            IdempotencyKey = "test-key"
        };
    }
}
