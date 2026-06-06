using System.Text.Json;
using Moq;
using Wbskt.Workflow.Abstraction.Enums;
using Wbskt.Workflow.Abstraction.Models;
using Wbskt.Workflow.Abstraction.Models.Nodes.Controls;
using Wbskt.Workflow.Abstraction.Providers;
using Wbskt.Workflow.Abstraction.Runtime;
using Wbskt.Workflow.NodeExecutors.Controls;

namespace Wbskt.Workflow.Engine.Host.Tests.NodeExecutors.Controls;

public sealed class JoinNodeExecutorTests
{
    [Fact]
    public async Task ExecuteAsync_returns_fail_when_config_is_null()
    {
        var aggregatorMock = new Mock<IJoinAggregatorProvider>();
        var executor = new JoinNodeExecutor(aggregatorMock.Object);
        NodeContext ctx = CreateContext(
            new JoinNode(Guid.NewGuid(), "join", CreatePorts(), null),
            new Dictionary<string, JsonElement>());

        NodeExecutionResult result = await executor.ExecuteAsync(ctx, CancellationToken.None);

        var fail = Assert.IsType<NodeExecutionResult.Fail>(result);
        Assert.Equal("JOIN_CONFIG_INVALID", fail.ErrorCode);
    }

    [Fact]
    public async Task ExecuteAsync_returns_fail_when_join_token_missing()
    {
        var aggregatorMock = new Mock<IJoinAggregatorProvider>();
        var executor = new JoinNodeExecutor(aggregatorMock.Object);
        NodeContext ctx = CreateContext(
            new JoinNode(Guid.NewGuid(), "join", CreatePorts(), new JoinConfig(JoinMode.All)),
            new Dictionary<string, JsonElement>());

        NodeExecutionResult result = await executor.ExecuteAsync(ctx, CancellationToken.None);

        var fail = Assert.IsType<NodeExecutionResult.Fail>(result);
        Assert.Equal("JOIN_NO_TOKEN", fail.ErrorCode);
        Assert.False(fail.Retryable);
    }

    [Fact]
    public async Task ExecuteAsync_returns_continue_default_when_ShouldContinue_true()
    {
        Guid joinToken = Guid.NewGuid();
        var aggregatorMock = new Mock<IJoinAggregatorProvider>();
        aggregatorMock
            .Setup(a => a.ContributeAsync(joinToken, "succeeded", "All", 0, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new JoinContributionResult(true, 3, 3, 0, 3));

        var executor = new JoinNodeExecutor(aggregatorMock.Object);
        NodeContext ctx = CreateContext(
            new JoinNode(Guid.NewGuid(), "join", CreatePorts(), new JoinConfig(JoinMode.All)),
            new Dictionary<string, JsonElement>
            {
                ["__join_token"] = JsonSerializer.SerializeToElement(joinToken.ToString())
            });

        NodeExecutionResult result = await executor.ExecuteAsync(ctx, CancellationToken.None);

        var cont = Assert.IsType<NodeExecutionResult.Continue>(result);
        Assert.Equal("default", cont.OutboundPort);
        Assert.Equal(3, cont.LocalStatePatch["joinContributed"].GetInt32());
        Assert.Equal(3, cont.LocalStatePatch["joinSucceeded"].GetInt32());
        Assert.Equal(0, cont.LocalStatePatch["joinFailed"].GetInt32());
    }

    [Fact]
    public async Task ExecuteAsync_returns_terminal_completed_when_ShouldContinue_false()
    {
        Guid joinToken = Guid.NewGuid();
        var aggregatorMock = new Mock<IJoinAggregatorProvider>();
        aggregatorMock
            .Setup(a => a.ContributeAsync(joinToken, "succeeded", "All", 0, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new JoinContributionResult(false, 1, 1, 0, 3));

        var executor = new JoinNodeExecutor(aggregatorMock.Object);
        NodeContext ctx = CreateContext(
            new JoinNode(Guid.NewGuid(), "join", CreatePorts(), new JoinConfig(JoinMode.All)),
            new Dictionary<string, JsonElement>
            {
                ["__join_token"] = JsonSerializer.SerializeToElement(joinToken.ToString())
            });

        NodeExecutionResult result = await executor.ExecuteAsync(ctx, CancellationToken.None);

        var terminal = Assert.IsType<NodeExecutionResult.Terminal>(result);
        Assert.Equal(BranchTerminalReason.Completed, terminal.Reason);
    }

    [Fact]
    public async Task ExecuteAsync_calls_contribute_with_any_mode_string()
    {
        Guid joinToken = Guid.NewGuid();
        var aggregatorMock = new Mock<IJoinAggregatorProvider>();
        aggregatorMock
            .Setup(a => a.ContributeAsync(joinToken, "succeeded", "Any", 0, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new JoinContributionResult(true, 1, 1, 0, 5));

        var executor = new JoinNodeExecutor(aggregatorMock.Object);
        NodeContext ctx = CreateContext(
            new JoinNode(Guid.NewGuid(), "join", CreatePorts(), new JoinConfig(JoinMode.Any)),
            new Dictionary<string, JsonElement>
            {
                ["__join_token"] = JsonSerializer.SerializeToElement(joinToken.ToString())
            });

        await executor.ExecuteAsync(ctx, CancellationToken.None);

        aggregatorMock.Verify(
            a => a.ContributeAsync(joinToken, "succeeded", "Any", 0, It.IsAny<CancellationToken>()),
            Times.Once);
    }

    [Fact]
    public async Task ExecuteAsync_calls_contribute_with_quorum_mode_and_count()
    {
        Guid joinToken = Guid.NewGuid();
        var aggregatorMock = new Mock<IJoinAggregatorProvider>();
        aggregatorMock
            .Setup(a => a.ContributeAsync(joinToken, "succeeded", "Quorum", 3, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new JoinContributionResult(true, 3, 3, 0, 5));

        var executor = new JoinNodeExecutor(aggregatorMock.Object);
        NodeContext ctx = CreateContext(
            new JoinNode(Guid.NewGuid(), "join", CreatePorts(), new JoinConfig(JoinMode.Quorum, 3)),
            new Dictionary<string, JsonElement>
            {
                ["__join_token"] = JsonSerializer.SerializeToElement(joinToken.ToString())
            });

        await executor.ExecuteAsync(ctx, CancellationToken.None);

        aggregatorMock.Verify(
            a => a.ContributeAsync(joinToken, "succeeded", "Quorum", 3, It.IsAny<CancellationToken>()),
            Times.Once);
    }

    private static NodeContext CreateContext(JoinNode node, IReadOnlyDictionary<string, JsonElement> localState)
    {
        return new NodeContext
        {
            Branch = new BranchContext(42, 1001, 5, Guid.NewGuid(), 1, node.NodeId.ToString(), 1, localState, new Dictionary<string, JsonElement>(), "corr-1", DateTime.UtcNow),
            Node = node,
            Providers = new StubProviderComposite(),
            Tick = 1,
            ParentResults = null,
            CancellationToken = CancellationToken.None
        };
    }

    private static IReadOnlyCollection<PortDefinition> CreatePorts()
    {
        return [
            new PortDefinition("in", PortDirection.Input, "In"),
            new PortDefinition("default", PortDirection.Output, "Default")
        ];
    }

    private sealed class StubProviderComposite : IProviderComposite
    {
        public IWorkflowDefinitionProvider WorkflowDefinition => throw new NotSupportedException();
        public ITriggerRegistrationProvider TriggerRegistration => throw new NotSupportedException();
        public IBookmarkProvider Bookmark => throw new NotSupportedException();
        public ISharedVariableProvider SharedVariable => throw new NotSupportedException();
        public IIdempotencyKeyProvider IdempotencyKey => throw new NotSupportedException();
        public IPendingTriggerEventProvider PendingTriggerEvent => throw new NotSupportedException();
        public IScheduledFireProvider ScheduledFire => throw new NotSupportedException();
    }
}
